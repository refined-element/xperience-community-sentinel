using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.Admin.Base.Authentication;

using Microsoft.Extensions.Options;

using XperienceCommunity.Sentinel.Core;
using XperienceCommunity.Sentinel.Quoting;
using XperienceCommunity.Sentinel.Reporting;
using XperienceCommunity.Sentinel.Admin;
using XperienceCommunity.Sentinel.Admin.UIPages;
using XperienceCommunity.Sentinel.Module.Configuration;
using XperienceCommunity.Sentinel.Module.Contact;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelFinding;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelScanRun;

[assembly: UIPage(
    parentType: typeof(SentinelApplicationPage),
    slug: "contact",
    uiPageType: typeof(SentinelContactPage),
    name: "Request a quote",
    templateName: "@xperiencecommunity/sentinel-admin/Contact",
    order: 300)]

namespace XperienceCommunity.Sentinel.Admin.UIPages;

/// <summary>
/// In-admin contact form that submits a sanitized scan snapshot to the Refined Element quote
/// intake endpoint. The heavy lifting (HTTP, retry, failure taxonomy) lives in
/// <see cref="ISentinelContactService"/> — this page assembles the form data into a
/// <see cref="QuoteSubmission"/> via <see cref="QuoteSanitizer.Sanitize"/> so the wire shape
/// is identical to what the CLI sends.
/// </summary>
[UIEvaluatePermission(SystemPermissions.VIEW)]
public class SentinelContactPage(
    IInfoProvider<SentinelScanRunInfo> scanRunProvider,
    IInfoProvider<SentinelFindingInfo> findingProvider,
    ISentinelContactService contactService,
    IAuthenticatedUserAccessor userAccessor,
    IOptions<SentinelOptions> options)
    : Page<ContactClientProperties>
{
    // Limit to the most recent scans so the dropdown stays finite even on a site that's been
    // running Sentinel for months. Operators requesting quotes typically care about the current
    // backlog, not the archival tail.
    private const int ScanDropdownSize = 25;

    private readonly SentinelOptions sentinelOptions = options.Value;

    public override async Task<ContactClientProperties> ConfigureTemplateProperties(ContactClientProperties properties)
    {
        properties.DefaultIncludeContext = sentinelOptions.Contact.IncludeContextByDefault;
        properties.ContactEndpoint = !string.IsNullOrWhiteSpace(sentinelOptions.Contact.Endpoint)
            ? sentinelOptions.Contact.Endpoint
            : QuoteClient.DefaultEndpoint;

        var user = await userAccessor.Get();
        properties.PrefilledEmail = user?.Email ?? string.Empty;

        properties.AvailableScans = scanRunProvider.Get()
            .WhereEquals(nameof(SentinelScanRunInfo.SentinelScanRunStatus), "Completed")
            .OrderByDescending(nameof(SentinelScanRunInfo.SentinelScanRunID))
            .TopN(ScanDropdownSize)
            .ToList()
            .Select(ToScanOption)
            .ToArray();

        return properties;
    }

    [PageCommand]
    public async Task<ICommandResponse<ContactSubmitResult>> SubmitContact(ContactSubmitData data, CancellationToken cancellationToken)
    {
        if (data is null || data.ScanRunId <= 0 || string.IsNullOrWhiteSpace(data.ContactEmail))
        {
            return ResponseFrom(new ContactSubmitResult
            {
                Success = false,
                StatusCode = 0,
                Message = string.Empty,
                ErrorMessage = "Email and a scan selection are required.",
            });
        }

        // IInfoProvider<T> doesn't expose Get(int id) on the generic interface — it returns
        // the ObjectQuery<T>. Fetch the single row via WhereEquals on the identity column.
        var run = scanRunProvider.Get()
            .WhereEquals(nameof(SentinelScanRunInfo.SentinelScanRunID), data.ScanRunId)
            .TopN(1)
            .FirstOrDefault();
        if (run is null)
        {
            return ResponseFrom(new ContactSubmitResult
            {
                Success = false,
                StatusCode = 0,
                Message = string.Empty,
                ErrorMessage = $"Scan #{data.ScanRunId} not found. It may have been deleted — refresh and try again.",
            });
        }

        var findings = findingProvider.Get()
            .WhereEquals(nameof(SentinelFindingInfo.SentinelFindingScanRunID), run.SentinelScanRunID)
            .ToList();

        var report = BuildReport(run, findings);
        // Sanitizer returns the base submission (counts + findings, context-stripped per flag);
        // enrich it with the admin-supplied contact metadata via init-only properties that are
        // ignored by older consumers of QuoteSubmission.
        var submission = QuoteSanitizer.Sanitize(report, data.ContactEmail, data.IncludeContext) with
        {
            ContactName = string.IsNullOrWhiteSpace(data.ContactName) ? null : data.ContactName.Trim(),
            Company = string.IsNullOrWhiteSpace(data.Company) ? null : data.Company.Trim(),
            Message = string.IsNullOrWhiteSpace(data.Message) ? null : data.Message.Trim(),
        };

        var result = await contactService.SubmitAsync(submission, cancellationToken).ConfigureAwait(false);
        return ResponseFrom(new ContactSubmitResult
        {
            Success = result.Success,
            StatusCode = result.StatusCode,
            Message = result.ResponseBody ?? string.Empty,
            ErrorMessage = result.ErrorMessage,
        });
    }

    private static ScanOptionDto ToScanOption(SentinelScanRunInfo run) => new()
    {
        RunId = run.SentinelScanRunID,
        Label = $"#{run.SentinelScanRunID} — {run.SentinelScanRunStartedAt:yyyy-MM-dd HH:mm} ({run.SentinelScanRunTrigger})",
        FindingsCount = run.SentinelScanRunTotalFindings,
    };

    // Projects the persisted scan-run + finding rows back into a Core ReportDocument so the
    // existing QuoteSanitizer + QuoteSubmission shape can be reused verbatim. Keeps the CLI
    // and admin-UI submissions on the same wire contract.
    private static ReportDocument BuildReport(SentinelScanRunInfo run, IReadOnlyList<SentinelFindingInfo> findings)
    {
        var scan = new ReportScan(
            StartedAt: new DateTimeOffset(run.SentinelScanRunStartedAt, TimeSpan.Zero),
            CompletedAt: new DateTimeOffset(run.SentinelScanRunCompletedAt, TimeSpan.Zero),
            DurationSeconds: (double)run.SentinelScanRunDurationSeconds,
            RepoPath: string.Empty,
            RuntimeEnabled: true);
        var summary = new ReportSummary(
            Total: run.SentinelScanRunTotalFindings,
            Errors: run.SentinelScanRunErrorCount,
            Warnings: run.SentinelScanRunWarningCount,
            Info: run.SentinelScanRunInfoCount);
        return new ReportDocument(
            SentinelVersion: run.SentinelScanRunSentinelVersion,
            Scan: scan,
            Summary: summary,
            Executions: Array.Empty<ReportExecution>(),
            Findings: findings.Select(f => new ReportFinding(
                RuleId: f.SentinelFindingRuleID,
                RuleTitle: f.SentinelFindingRuleTitle,
                Category: f.SentinelFindingCategory,
                Severity: f.SentinelFindingSeverity,
                Message: f.SentinelFindingMessage,
                Location: string.IsNullOrWhiteSpace(f.SentinelFindingLocation) ? null : f.SentinelFindingLocation,
                Remediation: string.IsNullOrWhiteSpace(f.SentinelFindingRemediation) ? null : f.SentinelFindingRemediation,
                QuoteEligible: f.SentinelFindingQuoteEligible)).ToArray());
    }
}

// Client contract — field names + types must match Client/src/contact/ContactTemplate.tsx.

public sealed class ContactClientProperties : TemplateClientProperties
{
    public ScanOptionDto[] AvailableScans { get; set; } = Array.Empty<ScanOptionDto>();
    public bool DefaultIncludeContext { get; set; }
    public string PrefilledEmail { get; set; } = string.Empty;
    public string ContactEndpoint { get; set; } = string.Empty;
}

public sealed class ScanOptionDto
{
    public int RunId { get; set; }
    public string Label { get; set; } = string.Empty;
    public int FindingsCount { get; set; }
}

public sealed class ContactSubmitData
{
    public int ScanRunId { get; set; }
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IncludeContext { get; set; }
}

public sealed class ContactSubmitResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
