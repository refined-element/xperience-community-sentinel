using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

using XperienceCommunity.Sentinel.Admin;
using XperienceCommunity.Sentinel.Admin.UIPages;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelFinding;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelScanRun;

[assembly: UIPage(
    parentType: typeof(SentinelApplicationPage),
    slug: "diff",
    uiPageType: typeof(SentinelDiffPage),
    name: "Compare scans",
    templateName: "@xperiencecommunity/sentinel-admin/Diff",
    order: 250)]

namespace XperienceCommunity.Sentinel.Admin.UIPages;

/// <summary>
/// Side-by-side diff between two scan runs. Uses <see cref="SentinelFindingInfo.SentinelFindingFingerprintHash"/>
/// as the identity key so a finding that drifts in exact wording between scans (digits / timestamps
/// mutated by the fingerprint normalizer) still matches — a resolution is one that GENUINELY
/// disappeared, not one that happens to read slightly differently this week.
///
/// <para>Output is three buckets: <b>Resolved</b> (in "before" but not "after"),
/// <b>New</b> (in "after" but not "before"), and <b>Still open</b> (in both).</para>
/// </summary>
[UIEvaluatePermission(SystemPermissions.VIEW)]
public class SentinelDiffPage(
    IInfoProvider<SentinelScanRunInfo> scanRunProvider,
    IInfoProvider<SentinelFindingInfo> findingProvider)
    : Page<DiffClientProperties>
{
    private const int ScanDropdownSize = 50;

    public override Task<DiffClientProperties> ConfigureTemplateProperties(DiffClientProperties properties)
    {
        var scans = scanRunProvider.Get()
            .OrderByDescending(nameof(SentinelScanRunInfo.SentinelScanRunID))
            .TopN(ScanDropdownSize)
            .ToList();
        properties.AvailableScans = scans.Select(r => new ScanOptionDto
        {
            RunId = r.SentinelScanRunID,
            Label = $"#{r.SentinelScanRunID} — {r.SentinelScanRunStartedAt:yyyy-MM-dd HH:mm} ({r.SentinelScanRunTotalFindings} findings)",
            FindingsCount = r.SentinelScanRunTotalFindings,
        }).ToArray();

        // Sensible defaults: after = most recent, before = next-most-recent. Admins reviewing
        // "what changed since last scan" get a useful result on page load without picking.
        if (scans.Count >= 2)
        {
            properties.DefaultBeforeRunId = scans[1].SentinelScanRunID;
            properties.DefaultAfterRunId = scans[0].SentinelScanRunID;
        }
        else if (scans.Count == 1)
        {
            properties.DefaultAfterRunId = scans[0].SentinelScanRunID;
            properties.DefaultBeforeRunId = scans[0].SentinelScanRunID;
        }

        return Task.FromResult(properties);
    }

    [PageCommand]
    public Task<ICommandResponse<DiffResponse>> ComputeDiff(DiffRequestData data)
    {
        if (data is null || data.BeforeRunId <= 0 || data.AfterRunId <= 0)
        {
            return Task.FromResult(ResponseFrom(new DiffResponse { Message = "Both scan selections are required." }));
        }

        var before = LoadFindings(data.BeforeRunId);
        var after = LoadFindings(data.AfterRunId);

        // HashSet of fingerprints is enough for presence checks — we don't need the full row on
        // the lookup side. Using a set also sidesteps the ToDictionary-throws-on-dupes case:
        // fingerprints are stable but not guaranteed unique within a scan (checks can emit
        // multiple findings whose location normalizes to the same hash), and duplicate rows in
        // the DB would otherwise crash this page on every render.
        var beforeHashes = new HashSet<string>(
            before.Select(f => f.SentinelFindingFingerprintHash),
            StringComparer.OrdinalIgnoreCase);
        var afterHashes = new HashSet<string>(
            after.Select(f => f.SentinelFindingFingerprintHash),
            StringComparer.OrdinalIgnoreCase);

        // Dedupe the projection to one row per fingerprint for the display payload; presence is
        // what matters for the categorization, and the admin doesn't need to see the same
        // finding twice in a column.
        var resolved = before
            .Where(f => !afterHashes.Contains(f.SentinelFindingFingerprintHash))
            .GroupBy(f => f.SentinelFindingFingerprintHash, StringComparer.OrdinalIgnoreCase)
            .Select(g => ToDiff(g.First()))
            .ToArray();
        var introduced = after
            .Where(f => !beforeHashes.Contains(f.SentinelFindingFingerprintHash))
            .GroupBy(f => f.SentinelFindingFingerprintHash, StringComparer.OrdinalIgnoreCase)
            .Select(g => ToDiff(g.First()))
            .ToArray();
        var stillOpen = after
            .Where(f => beforeHashes.Contains(f.SentinelFindingFingerprintHash))
            .GroupBy(f => f.SentinelFindingFingerprintHash, StringComparer.OrdinalIgnoreCase)
            .Select(g => ToDiff(g.First()))
            .ToArray();

        return Task.FromResult(ResponseFrom(new DiffResponse
        {
            Resolved = resolved,
            Introduced = introduced,
            StillOpen = stillOpen,
            Message = string.Empty,
        }));
    }

    private List<SentinelFindingInfo> LoadFindings(int runId) =>
        // Only the columns ToDiff reads — keeps the SQL payload tight for scans with thousands
        // of findings where the unused columns (Remediation body, QuoteEligible flag, timestamps)
        // would otherwise pull unused bytes across the wire.
        findingProvider.Get()
            .WhereEquals(nameof(SentinelFindingInfo.SentinelFindingScanRunID), runId)
            .Columns(
                nameof(SentinelFindingInfo.SentinelFindingFingerprintHash),
                nameof(SentinelFindingInfo.SentinelFindingRuleID),
                nameof(SentinelFindingInfo.SentinelFindingRuleTitle),
                nameof(SentinelFindingInfo.SentinelFindingCategory),
                nameof(SentinelFindingInfo.SentinelFindingSeverity),
                nameof(SentinelFindingInfo.SentinelFindingMessage),
                nameof(SentinelFindingInfo.SentinelFindingLocation))
            .ToList();

    private static DiffFindingDto ToDiff(SentinelFindingInfo f) => new()
    {
        Fingerprint = f.SentinelFindingFingerprintHash,
        RuleId = f.SentinelFindingRuleID,
        RuleTitle = f.SentinelFindingRuleTitle,
        Category = f.SentinelFindingCategory,
        Severity = f.SentinelFindingSeverity,
        Message = f.SentinelFindingMessage,
        Location = string.IsNullOrWhiteSpace(f.SentinelFindingLocation) ? null : f.SentinelFindingLocation,
    };
}

public sealed class DiffClientProperties : TemplateClientProperties
{
    public ScanOptionDto[] AvailableScans { get; set; } = Array.Empty<ScanOptionDto>();
    public int DefaultBeforeRunId { get; set; }
    public int DefaultAfterRunId { get; set; }
}

public sealed class DiffRequestData
{
    public int BeforeRunId { get; set; }
    public int AfterRunId { get; set; }
}

public sealed class DiffResponse
{
    public DiffFindingDto[] Resolved { get; set; } = Array.Empty<DiffFindingDto>();
    public DiffFindingDto[] Introduced { get; set; } = Array.Empty<DiffFindingDto>();
    public DiffFindingDto[] StillOpen { get; set; } = Array.Empty<DiffFindingDto>();
    public string Message { get; set; } = string.Empty;
}

public sealed class DiffFindingDto
{
    public string Fingerprint { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string RuleTitle { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Location { get; set; }
}
