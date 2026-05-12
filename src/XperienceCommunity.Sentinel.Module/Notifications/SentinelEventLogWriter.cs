using CMS.Core;

using Microsoft.Extensions.Options;

using XperienceCommunity.Sentinel.Core;
using XperienceCommunity.Sentinel.Module.Configuration;

namespace XperienceCommunity.Sentinel.Module.Notifications;

internal sealed class SentinelEventLogWriter(IEventLogService eventLog, IOptions<SentinelOptions> options) : ISentinelEventLogWriter
{
    private const string EventSource = "Sentinel";

    private readonly SentinelOptions options = options.Value;

    public void Write(ScanRunSummary run, IReadOnlyList<Finding> findings)
    {
        // One summary entry per scan so admins see a pulse even when everything is clean.
        eventLog.LogInformation(
            source: EventSource,
            eventCode: "SCAN_COMPLETED",
            eventDescription:
                $"Sentinel scan #{run.RunId} completed — " +
                $"{run.TotalFindings} findings " +
                $"({run.ErrorCount}E/{run.WarningCount}W/{run.InfoCount}I), " +
                $"trigger={run.Trigger}, version={run.SentinelVersion}.");

        if (!Enum.TryParse<Severity>(options.EventLogIntegration.SeverityThreshold, ignoreCase: true, out var threshold))
        {
            threshold = Severity.Warning;
        }

        var qualifying = findings.Where(f => f.Severity >= threshold).ToArray();
        var cap = Math.Max(0, options.EventLogIntegration.MaxEntriesPerScan);
        var toWrite = qualifying.Take(cap);
        var suppressed = Math.Max(0, qualifying.Length - cap);

        // One event per severity-qualifying finding, capped by MaxEntriesPerScan so a single
        // thousand-finding scan can't balloon the event log / slow the admin's Event log pager.
        foreach (var f in toWrite)
        {
            var desc = string.IsNullOrWhiteSpace(f.Location)
                ? $"{f.RuleId}: {f.Message}"
                : $"{f.RuleId}: {f.Message} ({f.Location})";
            switch (f.Severity)
            {
                case Severity.Error:
                    eventLog.LogError(EventSource, f.RuleId, desc);
                    break;
                case Severity.Warning:
                    eventLog.LogWarning(EventSource, f.RuleId, desc);
                    break;
                default:
                    eventLog.LogInformation(EventSource, f.RuleId, desc);
                    break;
            }
        }

        if (suppressed > 0)
        {
            // Single summary line so the admin sees the scale of what was dropped without the
            // noise of writing every remaining row.
            eventLog.LogInformation(
                source: EventSource,
                eventCode: "FINDINGS_TRUNCATED",
                eventDescription:
                    $"Sentinel scan #{run.RunId} had {qualifying.Length} findings at or above " +
                    $"{threshold} severity; {suppressed} not written to the event log (MaxEntriesPerScan={cap}). " +
                    $"Full list is available in the XperienceCommunity_SentinelFinding table.");
        }
    }
}
