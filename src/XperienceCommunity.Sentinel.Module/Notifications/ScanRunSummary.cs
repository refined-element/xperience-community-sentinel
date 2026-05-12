namespace XperienceCommunity.Sentinel.Module.Notifications;

/// <summary>
/// Lean, immutable snapshot of a completed scan-run that notifiers (event log, email digest,
/// future admin UI dashboard) consume. Deliberately decoupled from the Kentico
/// <c>SentinelScanRunInfo</c> so notification logic can be unit-tested without bootstrapping the
/// Kentico IoC container, and so a future embedding that streams findings before a scan-run
/// record exists can still fan out events.
/// </summary>
/// <param name="RunId">Auto-increment id from the XperienceCommunity_SentinelScanRun table.</param>
/// <param name="TotalFindings">Count of findings across all severities.</param>
/// <param name="ErrorCount">Findings with <c>Severity.Error</c>.</param>
/// <param name="WarningCount">Findings with <c>Severity.Warning</c>.</param>
/// <param name="InfoCount">Findings with <c>Severity.Info</c>.</param>
/// <param name="Trigger">"Scheduled" | "Manual" | "Startup".</param>
/// <param name="SentinelVersion">Version string of the XbyK integration package that ran the scan.</param>
/// <param name="CompletedAtUtc">Wall-clock completion time in UTC.</param>
public readonly record struct ScanRunSummary(
    int RunId,
    int TotalFindings,
    int ErrorCount,
    int WarningCount,
    int InfoCount,
    string Trigger,
    string SentinelVersion,
    DateTime CompletedAtUtc);
