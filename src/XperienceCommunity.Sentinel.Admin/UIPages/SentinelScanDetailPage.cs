using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.Authentication;
using Kentico.Xperience.Admin.Base.UIPages;

using XperienceCommunity.Sentinel.Core.Remediation;
using XperienceCommunity.Sentinel.Module.Acknowledgment;
using XperienceCommunity.Sentinel.Admin;
using XperienceCommunity.Sentinel.Admin.UIPages;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelFinding;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelScanRun;

[assembly: UIPage(
    parentType: typeof(SentinelApplicationPage),
    slug: "scan-detail",
    uiPageType: typeof(SentinelScanDetailPage),
    name: "Scan detail",
    templateName: "@xperiencecommunity/sentinel-admin/ScanDetail",
    order: 150)]

namespace XperienceCommunity.Sentinel.Admin.UIPages;

/// <summary>
/// Drill-in view for a single scan run. Loads the most recent scan by default; a dropdown lets
/// the admin pivot to any other run. For each finding exposes the ack state + a one-click
/// acknowledge/snooze/revoke action via the <c>SetAckState</c> page command. Rule IDs with a
/// <see cref="RemediationGuide"/> entry render an expandable "how to fix" panel.
/// </summary>
[UIEvaluatePermission(SystemPermissions.VIEW)]
public class SentinelScanDetailPage(
    IInfoProvider<SentinelScanRunInfo> scanRunProvider,
    IInfoProvider<SentinelFindingInfo> findingProvider,
    ISentinelFindingAckService ackService,
    IAuthenticatedUserAccessor userAccessor)
    : Page<ScanDetailClientProperties>
{
    private const int ScanDropdownSize = 50;

    public override async Task<ScanDetailClientProperties> ConfigureTemplateProperties(ScanDetailClientProperties properties)
    {
        // "Initial scan" is whichever run the URL points to via ?runId=, falling back to the
        // most recent completed run. Kentico admin doesn't expose query params as typed page
        // properties without additional plumbing; clients that deep-link set initialRunId via
        // a separate page command if they need to jump. For now: latest wins on first load.
        var recentScans = scanRunProvider.Get()
            .OrderByDescending(nameof(SentinelScanRunInfo.SentinelScanRunID))
            .TopN(ScanDropdownSize)
            .ToList();
        properties.AvailableScans = recentScans.Select(r => new ScanOptionDto
        {
            RunId = r.SentinelScanRunID,
            Label = $"#{r.SentinelScanRunID} — {r.SentinelScanRunStartedAt:yyyy-MM-dd HH:mm} ({r.SentinelScanRunTrigger})",
            FindingsCount = r.SentinelScanRunTotalFindings,
        }).ToArray();

        var initial = recentScans.FirstOrDefault();
        properties.Detail = initial is null ? BuildEmptyDetail() : await BuildDetail(initial.SentinelScanRunID);

        var user = await userAccessor.Get();
        properties.CurrentUserId = user?.UserID ?? 0;

        return properties;
    }

    [PageCommand]
    public async Task<ICommandResponse<ScanDetailResponse>> LoadScanDetail(LoadScanDetailData data)
    {
        var detail = await BuildDetail(data.RunId);
        return ResponseFrom(new ScanDetailResponse { Detail = detail });
    }

    /// <summary>
    /// Returns the current scan's findings serialized as CSV or JSON. The client triggers a
    /// browser download after receiving the payload — keeps the admin shell inside its page
    /// boundary (no cross-origin file endpoint to configure) at the cost of holding the blob
    /// in memory briefly on the client. Worth it for scans under ~1000 findings; above that,
    /// admins typically query the XperienceCommunity_SentinelFinding table directly anyway.
    /// </summary>
    [PageCommand]
    public async Task<ICommandResponse<ExportFindingsResponse>> ExportFindings(ExportFindingsData data)
    {
        var detail = await BuildDetail(data.RunId);
        if (detail.Run is null)
        {
            return ResponseFrom(new ExportFindingsResponse { Success = false, Message = "Scan not found." });
        }

        var format = (data.Format ?? "csv").Trim().ToLowerInvariant();
        string content;
        string mimeType;
        string extension;

        switch (format)
        {
            case "csv":
                content = RenderCsv(detail);
                mimeType = "text/csv";
                extension = "csv";
                break;
            case "json":
                content = System.Text.Json.JsonSerializer.Serialize(detail, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                });
                mimeType = "application/json";
                extension = "json";
                break;
            default:
                return ResponseFrom(new ExportFindingsResponse
                {
                    Success = false,
                    Message = $"Unknown format '{format}'. Supported: csv, json.",
                });
        }

        return ResponseFrom(new ExportFindingsResponse
        {
            Success = true,
            Content = content,
            MimeType = mimeType,
            FileName = $"sentinel-scan-{detail.Run.RunId}.{extension}",
            Message = $"Exported {detail.Findings.Length} finding{(detail.Findings.Length == 1 ? string.Empty : "s")}.",
        });
    }

    private static string RenderCsv(ScanDetailDto detail)
    {
        // Minimal CSV: one row per finding with the columns operators actually share externally.
        // RFC 4180 quoting: double every embedded quote, wrap fields that contain commas / quotes
        // / newlines. Keep the format readable without a library dependency.
        var sb = new System.Text.StringBuilder(16 * 1024);
        sb.AppendLine("ScanRunId,Severity,RuleId,RuleTitle,Category,Message,Location,AckState,SnoozeUntilUtc,ScanOccurrences,FirstSeenUtc");
        foreach (var f in detail.Findings)
        {
            sb.Append(detail.Run!.RunId).Append(',');
            sb.Append(Csv(f.Severity)).Append(',');
            sb.Append(Csv(f.RuleId)).Append(',');
            sb.Append(Csv(f.RuleTitle)).Append(',');
            sb.Append(Csv(f.Category)).Append(',');
            sb.Append(Csv(f.Message)).Append(',');
            sb.Append(Csv(f.Location ?? string.Empty)).Append(',');
            sb.Append(Csv(f.AckState)).Append(',');
            sb.Append(Csv(f.SnoozeUntilUtc ?? string.Empty)).Append(',');
            sb.Append(f.ScanOccurrences).Append(',');
            sb.AppendLine(Csv(f.FirstSeenUtc ?? string.Empty));
        }
        return sb.ToString();
    }

    // Cached needle for IndexOfAny — allocating `new[] {...}` per Csv() call would make the
    // hot path of exporting a 10k-finding scan produce 110k needless char[] instances.
    private static readonly char[] CsvSpecialChars = [',', '"', '\n', '\r'];

    // Leading characters Excel / Google Sheets interpret as formula starts — CSV injection vector.
    // A cell that starts with any of these gets apostrophe-prefixed so the spreadsheet renders it
    // as literal text instead of evaluating `=HYPERLINK(...)` / `+SUM(...)` / etc.
    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@'];

    private static string Csv(string value)
    {
        // Defuse CSV/Excel formula-injection: a finding message (operator-controlled) that reads
        // like "=HYPERLINK(...)" would execute in a spreadsheet client. Prefix with apostrophe;
        // Excel strips it on display while still treating the cell as text. Applied BEFORE the
        // RFC 4180 quoting step so the apostrophe is inside the quoted value.
        //
        // Leading-whitespace bypass: Excel will still evaluate `  =HYPERLINK(...)` / `\t=cmd`
        // in enough configurations that naively checking value[0] alone lets the attacker
        // defeat the defuser by prepending a space. Scan past ASCII whitespace + tab to find
        // the first "real" character and trigger on THAT. We preserve the original value
        // verbatim (prepending the apostrophe) — don't mutate the displayed content.
        var defused = StartsWithFormulaTrigger(value) ? "'" + value : value;
        return defused.IndexOfAny(CsvSpecialChars) >= 0
            ? "\"" + defused.Replace("\"", "\"\"") + "\""
            : defused;
    }

    private static bool StartsWithFormulaTrigger(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == ' ' || c == '\t')
            {
                continue;
            }
            return Array.IndexOf(FormulaTriggers, c) >= 0;
        }
        return false;
    }

    /// <summary>
    /// Bulk variant of <see cref="SetAckState"/>. Applies the same action + note to every
    /// fingerprint in the payload. Returns per-fingerprint updated state so the client can patch
    /// its per-row ack chips without reloading the whole scan. Noisy checks (CNT006 event-log
    /// repeats, dep-pin warnings) are the primary driver — clicking Acknowledge 47 times
    /// individually doesn't scale.
    /// </summary>
    [PageCommand(Permission = SystemPermissions.UPDATE)]
    public async Task<ICommandResponse<BulkAckResponse>> SetAckStateMany(BulkAckData data)
    {
        // Guard against a null payload BEFORE dereferencing data.Fingerprints — Kentico's page
        // command pipeline deserializes the body to BulkAckData but doesn't guarantee non-null.
        if (data is null)
        {
            return ResponseFrom(new BulkAckResponse { Success = false, Message = "No findings selected." });
        }

        var user = await userAccessor.Get();
        if (user is null)
        {
            return ResponseFrom(new BulkAckResponse
            {
                Success = false,
                Message = "Cannot record acknowledgments without an authenticated admin user.",
            });
        }
        var userId = user.UserID;

        if (data.Fingerprints is null || data.Fingerprints.Length == 0)
        {
            return ResponseFrom(new BulkAckResponse { Success = false, Message = "No findings selected." });
        }

        // Filter to fingerprints that satisfy the 64-hex-char SHA-256 invariant AND de-duplicate
        // (case-insensitive) BEFORE handing to the service. We do this here — rather than relying
        // solely on the service's Deduplicate helper — because the response Updates[] array is
        // built from this same collection. Without a pre-dedupe, a payload with duplicate
        // fingerprints would produce: (a) `written < valid.Length` because the service merges
        // them, and (b) duplicate entries in Updates[], both of which desync the client UI.
        //
        // Track malformed vs duplicate separately so the response message is accurate — saying
        // "N skipped — malformed" when the real cause was duplication would send an admin on a
        // goose chase looking for bad data.
        var wellFormed = data.Fingerprints
            .Where(FingerprintFormat.IsValid)
            .ToArray();
        var malformedCount = data.Fingerprints.Length - wellFormed.Length;
        var valid = wellFormed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var duplicateCount = wellFormed.Length - valid.Length;
        if (valid.Length == 0)
        {
            return ResponseFrom(new BulkAckResponse
            {
                Success = false,
                Message = "No valid fingerprints in request — all entries were malformed or duplicates.",
                RequestedCount = data.Fingerprints.Length,
            });
        }

        int written;
        switch (data.Action)
        {
            case "acknowledge":
                written = ackService.AcknowledgeMany(valid, userId, data.Note);
                break;
            case "snooze":
                if (!SnoozeDateParser.TryParse(data.SnoozeUntilUtc, DateTime.UtcNow, out var until, out var snoozeError))
                {
                    return ResponseFrom(new BulkAckResponse { Success = false, Message = snoozeError });
                }
                written = ackService.SnoozeMany(valid, until, userId, data.Note);
                break;
            case "revoke":
                written = ackService.RevokeMany(valid);
                break;
            default:
                return ResponseFrom(new BulkAckResponse { Success = false, Message = $"Unknown action '{data.Action}'." });
        }

        // Re-fetch state for each affected fingerprint so the client can patch per-row chips
        // without reloading the whole scan.
        var newStates = ackService.GetAll(valid);
        var updates = valid
            .Select(fp =>
            {
                newStates.TryGetValue(fp, out var state);
                return new BulkAckUpdate
                {
                    Fingerprint = fp,
                    NewState = state?.State.ToString() ?? "Active",
                    SnoozeUntilUtc = state?.SnoozeUntil?.ToString("O"),
                    Note = state?.Note,
                };
            })
            .ToArray();

        // Report malformed vs duplicate separately when present so the admin can act on the
        // information: malformed suggests client-side data corruption; duplicate suggests a
        // UI bug where the same row was added to the selection twice.
        var skippedSuffix = (malformedCount, duplicateCount) switch
        {
            (0, 0) => string.Empty,
            ( > 0, 0) => $" ({malformedCount} skipped — malformed fingerprint)",
            (0, > 0) => $" ({duplicateCount} skipped — duplicate)",
            _ => $" ({malformedCount} malformed, {duplicateCount} duplicate skipped)",
        };
        return ResponseFrom(new BulkAckResponse
        {
            Success = true,
            AffectedCount = written,
            RequestedCount = data.Fingerprints.Length,
            Updates = updates,
            Message = data.Action switch
            {
                "acknowledge" => $"Acknowledged {written} finding{(written == 1 ? string.Empty : "s")}{skippedSuffix}.",
                "snooze" => $"Snoozed {written} finding{(written == 1 ? string.Empty : "s")}{skippedSuffix}.",
                "revoke" => $"Revoked ack on {written} finding{(written == 1 ? string.Empty : "s")}{skippedSuffix}.",
                _ => $"Processed {written} finding(s){skippedSuffix}.",
            },
        });
    }

    [PageCommand(Permission = SystemPermissions.UPDATE)]
    public async Task<ICommandResponse<AckMutationResponse>> SetAckState(AckMutationData data)
    {
        // Validate fingerprint format BEFORE handing to the service. The ack table column is
        // sized for exactly 64 hex chars (SHA-256 digest); passing anything else would either
        // escape as a 500 from the service's invariant check or truncate on DB write. Return a
        // clean failure result so the admin UI renders a usable error instead of a stack trace.
        if (data is null || !FingerprintFormat.IsValid(data.Fingerprint))
        {
            return ResponseFrom(new AckMutationResponse
            {
                Success = false,
                Message = "Finding fingerprint is missing or malformed.",
            });
        }

        var user = await userAccessor.Get();
        if (user is null)
        {
            // Don't persist an ack attributed to user 0 — kills auditability and could conflict
            // with future assumptions about who dismissed what. UIEvaluatePermission should
            // block unauthenticated access already; defence in depth in case something slips
            // through.
            return ResponseFrom(new AckMutationResponse
            {
                Success = false,
                Message = "Cannot record an acknowledgment without an authenticated admin user.",
            });
        }
        var userId = user.UserID;

        switch (data.Action)
        {
            case "acknowledge":
                ackService.Acknowledge(data.Fingerprint, userId, data.Note);
                break;
            case "snooze":
                if (!SnoozeDateParser.TryParse(data.SnoozeUntilUtc, DateTime.UtcNow, out var until, out var snoozeError))
                {
                    return ResponseFrom(new AckMutationResponse { Success = false, Message = snoozeError });
                }
                ackService.Snooze(data.Fingerprint, until, userId, data.Note);
                break;
            case "revoke":
                ackService.Revoke(data.Fingerprint);
                break;
            default:
                return ResponseFrom(new AckMutationResponse { Success = false, Message = $"Unknown action '{data.Action}'." });
        }

        var state = ackService.Get(data.Fingerprint);
        return ResponseFrom(new AckMutationResponse
        {
            Success = true,
            Fingerprint = data.Fingerprint,
            NewState = state?.State.ToString() ?? "Active",
            SnoozeUntilUtc = state?.SnoozeUntil?.ToString("O"),
            Note = state?.Note,
        });
    }

    private async Task<ScanDetailDto> BuildDetail(int runId)
    {
        var run = scanRunProvider.Get()
            .WhereEquals(nameof(SentinelScanRunInfo.SentinelScanRunID), runId)
            .TopN(1)
            .FirstOrDefault();
        if (run is null)
        {
            return BuildEmptyDetail();
        }

        var findings = findingProvider.Get()
            .WhereEquals(nameof(SentinelFindingInfo.SentinelFindingScanRunID), runId)
            .ToList();

        var acks = ackService.GetAll(findings.Select(f => f.SentinelFindingFingerprintHash));

        // Cross-scan history — for each fingerprint in this scan, count how many other scans it
        // appears in and when it was first detected. Lets the admin triage "this has been open
        // for 3 weeks across 8 scans" vs "new in this run" at a glance. One query pre-computed
        // so the per-row loop is O(1) lookup. Distinct() BEFORE passing to WhereIn so the SQL
        // IN clause size is bounded by the number of unique fingerprints, not the scan's
        // finding count (a 5000-finding scan with 40 unique rules would otherwise hit SQL's
        // 2100-parameter limit and fail the whole page load).
        var uniqueFingerprints = findings
            .Select(f => f.SentinelFindingFingerprintHash)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var history = BuildFingerprintHistory(uniqueFingerprints);

        var findingDtos = findings.Select(f =>
        {
            var ackState = acks.TryGetValue(f.SentinelFindingFingerprintHash, out var a) ? a : null;
            var remediation = RemediationGuide.TryFor(f.SentinelFindingRuleID);
            history.TryGetValue(f.SentinelFindingFingerprintHash, out var hist);
            return new FindingDetailDto
            {
                FindingId = f.SentinelFindingID,
                Fingerprint = f.SentinelFindingFingerprintHash,
                RuleId = f.SentinelFindingRuleID,
                RuleTitle = f.SentinelFindingRuleTitle,
                Category = f.SentinelFindingCategory,
                Severity = f.SentinelFindingSeverity,
                Message = f.SentinelFindingMessage,
                Location = string.IsNullOrWhiteSpace(f.SentinelFindingLocation) ? null : f.SentinelFindingLocation,
                Remediation = string.IsNullOrWhiteSpace(f.SentinelFindingRemediation) ? null : f.SentinelFindingRemediation,
                QuoteEligible = f.SentinelFindingQuoteEligible,
                AckState = ackState?.State.ToString() ?? "Active",
                SnoozeUntilUtc = ackState?.SnoozeUntil?.ToString("O"),
                AckNote = ackState?.Note,
                RemediationTitle = remediation?.Title,
                RemediationSummary = remediation?.Summary,
                RemediationSteps = remediation?.Steps,
                ScanOccurrences = hist.ScanCount,
                FirstSeenUtc = hist.FirstSeenUtc,
            };
        }).ToArray();

        return await Task.FromResult(new ScanDetailDto
        {
            Run = new ScanSummaryDto
            {
                RunId = run.SentinelScanRunID,
                // SQL datetime round-trips as DateTimeKind.Unspecified via Kentico's Info framework;
                // SpecifyKind(UTC) before formatting ensures the "Z" designator is present so the
                // React client doesn't re-interpret the value in local time.
                StartedAt = DateTime.SpecifyKind(run.SentinelScanRunStartedAt, DateTimeKind.Utc).ToString("O"),
                Status = run.SentinelScanRunStatus,
                Trigger = run.SentinelScanRunTrigger,
                TotalFindings = run.SentinelScanRunTotalFindings,
                ErrorCount = run.SentinelScanRunErrorCount,
                WarningCount = run.SentinelScanRunWarningCount,
                InfoCount = run.SentinelScanRunInfoCount,
                DurationSeconds = (double)run.SentinelScanRunDurationSeconds,
                SentinelVersion = run.SentinelScanRunSentinelVersion,
            },
            Findings = findingDtos,
        });
    }

    private static ScanDetailDto BuildEmptyDetail() => new()
    {
        Run = null,
        Findings = Array.Empty<FindingDetailDto>(),
    };

    /// <summary>
    /// For each fingerprint in the input, return the total number of scan runs it has appeared
    /// in and the earliest scan-start timestamp. Pre-computed with a single query so the
    /// per-finding render loop stays O(1) — querying per-finding would be N round-trips and on
    /// a scan with 500 findings that's a visible freeze.
    /// </summary>
    // SQL Server's 2100-parameter cap bites hard when a mature install has years of scan
    // history — a single fingerprint can appear in hundreds of scan runs. Chunk at 1000 so
    // even a large scan-detail load stays well under the cap.
    private const int SqlInClauseBatchSize = 1_000;

    private Dictionary<string, FingerprintHistory> BuildFingerprintHistory(string[] fingerprints)
    {
        if (fingerprints.Length == 0)
        {
            return new Dictionary<string, FingerprintHistory>(StringComparer.OrdinalIgnoreCase);
        }

        // Pull every finding that shares any of these fingerprints, along with its scan-run
        // start time. Grouping happens in memory because Kentico's ObjectQuery doesn't expose
        // GROUP BY on the IInfoProvider<T> surface — but the JOIN via scan-run ID is cheap
        // because we fetch only the columns we aggregate on. Both IN-clause lookups are
        // batched below so a large fingerprint/scan set can't blow past SQL's parameter cap.
        var rows = QueryFindingsInBatches(fingerprints);

        var scanIds = rows.Select(r => r.SentinelFindingScanRunID).Distinct().ToArray();
        var scanStartTimes = QueryScanStartTimesInBatches(scanIds);

        return rows
            .GroupBy(r => r.SentinelFindingFingerprintHash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var scanIdSet = g.Select(r => r.SentinelFindingScanRunID).Distinct().ToArray();
                    var firstSeen = scanIdSet
                        .Select(id => scanStartTimes.TryGetValue(id, out var t) ? t : DateTime.MaxValue)
                        .Min();
                    return new FingerprintHistory(
                        ScanCount: scanIdSet.Length,
                        FirstSeenUtc: DateTime.SpecifyKind(firstSeen, DateTimeKind.Utc).ToString("O"));
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private List<SentinelFindingInfo> QueryFindingsInBatches(string[] fingerprints)
    {
        var rows = new List<SentinelFindingInfo>(fingerprints.Length);
        for (var offset = 0; offset < fingerprints.Length; offset += SqlInClauseBatchSize)
        {
            var batch = fingerprints.AsSpan(offset, Math.Min(SqlInClauseBatchSize, fingerprints.Length - offset)).ToArray();
            rows.AddRange(findingProvider.Get()
                .WhereIn(nameof(SentinelFindingInfo.SentinelFindingFingerprintHash), batch)
                .Columns(
                    nameof(SentinelFindingInfo.SentinelFindingFingerprintHash),
                    nameof(SentinelFindingInfo.SentinelFindingScanRunID))
                .ToList());
        }
        return rows;
    }

    private Dictionary<int, DateTime> QueryScanStartTimesInBatches(int[] scanIds)
    {
        var result = new Dictionary<int, DateTime>(scanIds.Length);
        for (var offset = 0; offset < scanIds.Length; offset += SqlInClauseBatchSize)
        {
            var batch = scanIds.AsSpan(offset, Math.Min(SqlInClauseBatchSize, scanIds.Length - offset)).ToArray();
            foreach (var run in scanRunProvider.Get()
                .WhereIn(nameof(SentinelScanRunInfo.SentinelScanRunID), batch)
                .Columns(
                    nameof(SentinelScanRunInfo.SentinelScanRunID),
                    nameof(SentinelScanRunInfo.SentinelScanRunStartedAt))
                .ToList())
            {
                result[run.SentinelScanRunID] = run.SentinelScanRunStartedAt;
            }
        }
        return result;
    }

    private readonly record struct FingerprintHistory(int ScanCount, string FirstSeenUtc);
}

public sealed class ScanDetailClientProperties : TemplateClientProperties
{
    public ScanOptionDto[] AvailableScans { get; set; } = Array.Empty<ScanOptionDto>();
    public ScanDetailDto Detail { get; set; } = new();
    public int CurrentUserId { get; set; }
}

public sealed class ScanDetailResponse
{
    public ScanDetailDto Detail { get; set; } = new();
}

public sealed class ScanDetailDto
{
    public ScanSummaryDto? Run { get; set; }
    public FindingDetailDto[] Findings { get; set; } = Array.Empty<FindingDetailDto>();
}

public sealed class FindingDetailDto
{
    public int FindingId { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string RuleTitle { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Remediation { get; set; }
    public bool QuoteEligible { get; set; }
    public string AckState { get; set; } = "Active";
    public string? SnoozeUntilUtc { get; set; }
    public string? AckNote { get; set; }
    public string? RemediationTitle { get; set; }
    public string? RemediationSummary { get; set; }
    public string? RemediationSteps { get; set; }
    /// <summary>Total distinct scan runs this fingerprint has appeared in (includes the current one).</summary>
    public int ScanOccurrences { get; set; }
    /// <summary>ISO-8601 UTC timestamp of the earliest scan run that produced this fingerprint.</summary>
    public string? FirstSeenUtc { get; set; }
}

public sealed class ExportFindingsData
{
    public int RunId { get; set; }
    public string Format { get; set; } = "csv"; // "csv" | "json"
}

public sealed class ExportFindingsResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

public sealed class LoadScanDetailData
{
    public int RunId { get; set; }
}

public sealed class AckMutationData
{
    public string Fingerprint { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // "acknowledge" | "snooze" | "revoke"
    public string? SnoozeUntilUtc { get; set; }
    public string? Note { get; set; }
}

public sealed class AckMutationResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string NewState { get; set; } = "Active";
    public string? SnoozeUntilUtc { get; set; }
    public string? Note { get; set; }
}

public sealed class BulkAckData
{
    public string[] Fingerprints { get; set; } = Array.Empty<string>();
    public string Action { get; set; } = string.Empty; // "acknowledge" | "snooze" | "revoke"
    public string? SnoozeUntilUtc { get; set; }
    public string? Note { get; set; }
}

public sealed class BulkAckResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int AffectedCount { get; set; }
    public int RequestedCount { get; set; }
    public BulkAckUpdate[] Updates { get; set; } = Array.Empty<BulkAckUpdate>();
}

public sealed class BulkAckUpdate
{
    public string Fingerprint { get; set; } = string.Empty;
    public string NewState { get; set; } = "Active";
    public string? SnoozeUntilUtc { get; set; }
    public string? Note { get; set; }
}
