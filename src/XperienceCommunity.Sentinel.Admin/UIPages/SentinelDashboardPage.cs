using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

using XperienceCommunity.Sentinel.Core.Remediation;
using XperienceCommunity.Sentinel.Module.Acknowledgment;
using XperienceCommunity.Sentinel.Admin;
using XperienceCommunity.Sentinel.Admin.UIPages;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelFinding;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelScanRun;
using XperienceCommunity.Sentinel.Module.Services;

[assembly: UIPage(
    parentType: typeof(SentinelApplicationPage),
    slug: "dashboard",
    uiPageType: typeof(SentinelDashboardPage),
    name: "Dashboard",
    templateName: "@xperiencecommunity/sentinel-admin/Dashboard",
    order: UIPageOrder.First - 1)]

namespace XperienceCommunity.Sentinel.Admin.UIPages;

/// <summary>
/// First screen an operator sees on opening Sentinel. KPI tiles for the latest scan, a 30-day
/// severity trend sparkline, a mini history of recent runs, and the top rule offenders ranked
/// across the last 50 scans — each with rule-specific remediation copy from
/// <see cref="RemediationGuide"/> and the count of still-active (un-acked) findings.
///
/// <para>Page commands:
/// <list type="bullet">
///   <item><c>GetDashboardData</c> — refreshes all tiles without a hard page reload.</item>
///   <item><c>RunScanNow</c> — fires <see cref="SentinelScanService.RunAsync"/> inline so admins
///     can kick a manual scan from the dashboard instead of bouncing to Scheduled tasks.</item>
/// </list>
/// </para>
/// </summary>
[UIEvaluatePermission(SystemPermissions.VIEW)]
public class SentinelDashboardPage(
    IInfoProvider<SentinelScanRunInfo> scanRunProvider,
    IInfoProvider<SentinelFindingInfo> findingProvider,
    ISentinelFindingAckService ackService,
    SentinelScanService scanService)
    : Page<DashboardClientProperties>
{
    // Window sizes. Recent-scans list is the eye-level history; top-rule ranking sweeps a much
    // larger window so the list feels stable. Trend chart covers the rolling-30-day view most
    // operators review weekly.
    private const int RecentScanWindow = 10;
    private const int TopRuleWindow = 50;
    private const int TopRulePageSize = 10;
    private const int TrendDays = 30;

    public override Task<DashboardClientProperties> ConfigureTemplateProperties(DashboardClientProperties properties)
    {
        PopulateDashboard(properties);
        return Task.FromResult(properties);
    }

    [PageCommand]
    public Task<ICommandResponse<DashboardRefreshResult>> GetDashboardData()
    {
        var data = new DashboardClientProperties();
        PopulateDashboard(data);
        return Task.FromResult(ResponseFrom(new DashboardRefreshResult { Data = data }));
    }

    /// <summary>
    /// Fires an on-demand scan. Same code path the scheduled task takes, just with a "Manual"
    /// trigger label so the scan-run row carries provenance. Returns immediately once the scan
    /// completes — client refreshes the dashboard to pick up the new row.
    /// </summary>
    [PageCommand(Permission = SystemPermissions.CREATE)]
    public async Task<ICommandResponse<RunNowResult>> RunScanNow(CancellationToken cancellationToken)
    {
        try
        {
            var run = await scanService.RunAsync(trigger: "Manual", cancellationToken).ConfigureAwait(false);
            if (run is null)
            {
                return ResponseFrom(new RunNowResult
                {
                    Success = false,
                    Message = "Sentinel:Enabled is false — scan was skipped. Flip the config flag to run manually.",
                });
            }
            return ResponseFrom(new RunNowResult
            {
                Success = true,
                ScanRunId = run.SentinelScanRunID,
                TotalFindings = run.SentinelScanRunTotalFindings,
                Message = $"Scan #{run.SentinelScanRunID} completed: {run.SentinelScanRunTotalFindings} findings.",
            });
        }
        catch (OperationCanceledException)
        {
            return ResponseFrom(new RunNowResult { Success = false, Message = "Scan cancelled." });
        }
        catch (Exception ex)
        {
            // Generic error text surfaces in the admin toast; full stack is already logged by
            // SentinelScanService's catch block.
            return ResponseFrom(new RunNowResult
            {
                Success = false,
                Message = $"Scan failed: {ex.GetType().Name}. See event log for details.",
            });
        }
    }

    private void PopulateDashboard(DashboardClientProperties properties)
    {
        // Kentico XbyK 31.x routes the Scheduled Tasks admin app at /admin/scheduled-tasks.
        // Earlier we sent admins to /admin (root) worrying the app URL would drift across
        // refreshes, but dogfooding showed "root" is worse UX — the left nav is cluttered,
        // operators don't find Scheduled Tasks without hunting. Direct deep-link is fine;
        // if Kentico renames the app slug in a future refresh, we'll update this one spot.
        properties.ScheduledTasksUrl = "/admin/scheduled-tasks";
        properties.ScanHistoryUrl = "/admin/sentinel/scans";
        properties.FindingsUrl = "/admin/sentinel/findings";

        var recent = scanRunProvider.Get()
            .OrderByDescending(nameof(SentinelScanRunInfo.SentinelScanRunID))
            .TopN(RecentScanWindow)
            .ToList();

        properties.HasScans = recent.Count > 0;
        properties.LatestScan = recent.Count > 0 ? ToSummary(recent[0]) : null;
        // PreviousScan powers the delta chips on the KPI tiles — "+3 errors since last scan"
        // reads at a glance whether the scan trended in the right direction. Only populated when
        // there's an actual earlier scan to compare against.
        properties.PreviousScan = recent.Count > 1 ? ToSummary(recent[1]) : null;
        properties.RecentScans = recent.Select(ToSummary).ToArray();
        properties.Trend = ComputeTrend();

        if (recent.Count == 0)
        {
            properties.TopRules = Array.Empty<RuleCountDto>();
            return;
        }

        // Top-rule ranking sweeps a LARGER window than the "Recent scans" list — 50 most recent
        // runs by default so the ranking feels stable even if the last couple scans were clean.
        // Separate query: RecentScanWindow caps the displayed recent list at 10, TopRuleWindow
        // drives what populates the ranking.
        var topRuleRunIds = scanRunProvider.Get()
            .OrderByDescending(nameof(SentinelScanRunInfo.SentinelScanRunID))
            .TopN(TopRuleWindow)
            .Columns(nameof(SentinelScanRunInfo.SentinelScanRunID))
            .ToList()
            .Select(r => r.SentinelScanRunID)
            .ToArray();

        var findings = findingProvider.Get()
            .WhereIn(nameof(SentinelFindingInfo.SentinelFindingScanRunID), topRuleRunIds)
            .Columns(
                nameof(SentinelFindingInfo.SentinelFindingRuleID),
                nameof(SentinelFindingInfo.SentinelFindingCategory),
                nameof(SentinelFindingInfo.SentinelFindingFingerprintHash))
            .ToList();

        // Pre-load ack states once for every fingerprint in the window — single round-trip
        // instead of one lookup per finding. The dictionary lets us split active vs suppressed
        // counts without a second scan of the findings list.
        var allFingerprints = findings.Select(f => f.SentinelFindingFingerprintHash).ToArray();
        var acks = ackService.GetAll(allFingerprints);

        properties.TopRules = findings
            .GroupBy(f => (f.SentinelFindingRuleID, f.SentinelFindingCategory))
            .Select(g =>
            {
                var total = g.Count();
                var suppressed = g.Count(f => acks.TryGetValue(f.SentinelFindingFingerprintHash, out var a) && a.State != AckState.Active);
                var remediation = RemediationGuide.TryFor(g.Key.SentinelFindingRuleID);
                return new RuleCountDto
                {
                    RuleId = g.Key.SentinelFindingRuleID,
                    Category = g.Key.SentinelFindingCategory,
                    TotalCount = total,
                    ActiveCount = total - suppressed,
                    RemediationTitle = remediation?.Title,
                    RemediationSummary = remediation?.Summary,
                };
            })
            .OrderByDescending(r => r.ActiveCount)
            .ThenByDescending(r => r.TotalCount)
            .ThenBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
            .Take(TopRulePageSize)
            .ToArray();
    }

    /// <summary>
    /// Builds per-day severity counts for the last <see cref="TrendDays"/> days. Days with no
    /// scans get zero-filled so the client-side SVG sparkline renders a continuous axis without
    /// gap logic. Ordered oldest → newest so the sparkline reads left-to-right.
    /// </summary>
    private TrendPointDto[] ComputeTrend()
    {
        var sinceDay = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-TrendDays + 1);
        // WhereGreaterOrEquals still needs a DateTime for the column compare — use midnight UTC
        // on sinceDay so the filter is inclusive of the earliest scan on that day.
        var sinceUtc = sinceDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var recent = scanRunProvider.Get()
            .WhereGreaterOrEquals(nameof(SentinelScanRunInfo.SentinelScanRunStartedAt), sinceUtc)
            .WhereEquals(nameof(SentinelScanRunInfo.SentinelScanRunStatus), "Completed")
            .Columns(
                nameof(SentinelScanRunInfo.SentinelScanRunStartedAt),
                nameof(SentinelScanRunInfo.SentinelScanRunErrorCount),
                nameof(SentinelScanRunInfo.SentinelScanRunWarningCount),
                nameof(SentinelScanRunInfo.SentinelScanRunInfoCount))
            .ToList();

        // Represent each day by the LATEST completed scan rather than summing across runs —
        // otherwise an admin who kicks a manual "run now" twice on the same day produces a 2× spike
        // on the trend line that doesn't reflect an actual severity change. Failed/partial runs are
        // filtered upstream by the WhereEquals("Completed") clause so they can't skew the point.
        //
        // DateOnly keys (not DateTime) — DateTime carries a Kind that can drift from
        // Unspecified (SQL round-trip) to Utc (UtcNow.Date), and while Dictionary lookups happen
        // to still work (Equals/GetHashCode use ticks only), DateOnly removes the ambiguity
        // entirely. Future readers don't need to know the Kind trivia.
        var byDay = recent
            .GroupBy(r => DateOnly.FromDateTime(r.SentinelScanRunStartedAt))
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var latest = g.OrderByDescending(r => r.SentinelScanRunStartedAt).First();
                    return new
                    {
                        Errors = latest.SentinelScanRunErrorCount,
                        Warnings = latest.SentinelScanRunWarningCount,
                        Info = latest.SentinelScanRunInfoCount,
                    };
                });

        var result = new TrendPointDto[TrendDays];
        for (var i = 0; i < TrendDays; i++)
        {
            var day = sinceDay.AddDays(i);
            var label = day.ToString("yyyy-MM-dd");
            if (byDay.TryGetValue(day, out var counts))
            {
                result[i] = new TrendPointDto { Date = label, Errors = counts.Errors, Warnings = counts.Warnings, Info = counts.Info };
            }
            else
            {
                result[i] = new TrendPointDto { Date = label, Errors = 0, Warnings = 0, Info = 0 };
            }
        }
        return result;
    }

    private static ScanSummaryDto ToSummary(SentinelScanRunInfo run) => new()
    {
        RunId = run.SentinelScanRunID,
        // Timestamps are stored as UTC via DateTime.UtcNow but SQL datetime round-trips through
        // Kentico's Info framework as DateTimeKind.Unspecified — without SpecifyKind, ToString("O")
        // omits the "Z" designator and the React client interprets the value as local time.
        StartedAt = DateTime.SpecifyKind(run.SentinelScanRunStartedAt, DateTimeKind.Utc).ToString("O"),
        Status = run.SentinelScanRunStatus,
        Trigger = run.SentinelScanRunTrigger,
        TotalFindings = run.SentinelScanRunTotalFindings,
        ErrorCount = run.SentinelScanRunErrorCount,
        WarningCount = run.SentinelScanRunWarningCount,
        InfoCount = run.SentinelScanRunInfoCount,
        DurationSeconds = (double)run.SentinelScanRunDurationSeconds,
        SentinelVersion = run.SentinelScanRunSentinelVersion,
    };
}

// Client-property shapes serialize to JSON and are consumed by the React template — field names
// and types must line up with the matching TypeScript interfaces in DashboardTemplate.tsx.

public sealed class DashboardClientProperties : TemplateClientProperties
{
    public bool HasScans { get; set; }
    public ScanSummaryDto? LatestScan { get; set; }
    public ScanSummaryDto? PreviousScan { get; set; }
    public ScanSummaryDto[] RecentScans { get; set; } = Array.Empty<ScanSummaryDto>();
    public RuleCountDto[] TopRules { get; set; } = Array.Empty<RuleCountDto>();
    public TrendPointDto[] Trend { get; set; } = Array.Empty<TrendPointDto>();
    public string ScheduledTasksUrl { get; set; } = string.Empty;
    public string ScanHistoryUrl { get; set; } = string.Empty;
    public string FindingsUrl { get; set; } = string.Empty;
}

public sealed class DashboardRefreshResult
{
    public DashboardClientProperties Data { get; set; } = new();
}

public sealed class RunNowResult
{
    public bool Success { get; set; }
    public int ScanRunId { get; set; }
    public int TotalFindings { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class ScanSummaryDto
{
    public int RunId { get; set; }
    public string StartedAt { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public int TotalFindings { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public double DurationSeconds { get; set; }
    public string SentinelVersion { get; set; } = string.Empty;
}

public sealed class RuleCountDto
{
    public string RuleId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int ActiveCount { get; set; }
    public string? RemediationTitle { get; set; }
    public string? RemediationSummary { get; set; }
}

public sealed class TrendPointDto
{
    public string Date { get; set; } = string.Empty;
    public int Errors { get; set; }
    public int Warnings { get; set; }
    public int Info { get; set; }
}
