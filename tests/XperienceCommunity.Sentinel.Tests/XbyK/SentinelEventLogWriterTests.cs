using CMS.Core;

using Microsoft.Extensions.Options;

using XperienceCommunity.Sentinel.Core;
using XperienceCommunity.Sentinel.Module.Configuration;
using XperienceCommunity.Sentinel.Module.Notifications;

namespace XperienceCommunity.Sentinel.Tests.XbyK;

/// <summary>
/// Event-log mirror is the default notification channel — every scan fires through it, so the
/// threshold filter + per-scan entry cap need to be exercised regardless of the downstream
/// (email / admin UI) notifiers. Tests use a stub <see cref="IEventLogService"/> that captures
/// raw <see cref="EventLogData"/> rather than depending on Kentico's real logger.
/// </summary>
public class SentinelEventLogWriterTests
{
    [Fact]
    public void Warning_threshold_skips_info_findings()
    {
        var sut = WriterWith(threshold: "Warning", maxEntries: 50);
        var run = Run(id: 1, total: 3, errors: 0, warnings: 1, infos: 2);
        var findings = new[]
        {
            Finding(Severity.Warning, "CHK001"),
            Finding(Severity.Info, "CHK002"),
            Finding(Severity.Info, "CHK003"),
        };

        sut.Writer.Write(run, findings);

        // Summary + the one warning only — info findings are below threshold.
        Assert.Equal(2, sut.Captured.Count);
        Assert.Contains(sut.Captured, d => d.EventCode == "SCAN_COMPLETED");
        Assert.Contains(sut.Captured, d => d.EventCode == "CHK001");
        Assert.DoesNotContain(sut.Captured, d => d.EventCode == "CHK002" || d.EventCode == "CHK003");
    }

    [Fact]
    public void Error_threshold_skips_warning_and_info_findings()
    {
        var sut = WriterWith(threshold: "Error", maxEntries: 50);
        var run = Run(id: 2);
        var findings = new[]
        {
            Finding(Severity.Error, "CHK010"),
            Finding(Severity.Warning, "CHK011"),
            Finding(Severity.Info, "CHK012"),
        };

        sut.Writer.Write(run, findings);

        Assert.Equal(2, sut.Captured.Count);
        Assert.Contains(sut.Captured, d => d.EventCode == "SCAN_COMPLETED");
        Assert.Contains(sut.Captured, d => d.EventCode == "CHK010");
    }

    [Fact]
    public void Info_threshold_writes_everything()
    {
        var sut = WriterWith(threshold: "Info", maxEntries: 50);
        var run = Run(id: 3);
        var findings = new[]
        {
            Finding(Severity.Error, "CHK100"),
            Finding(Severity.Warning, "CHK101"),
            Finding(Severity.Info, "CHK102"),
        };

        sut.Writer.Write(run, findings);

        // Summary + 3 findings.
        Assert.Equal(4, sut.Captured.Count);
    }

    [Fact]
    public void Invalid_threshold_string_falls_back_to_Warning()
    {
        // Resilience: if an admin typos "Warn" into appsettings, don't crash the scan — degrade
        // to the documented default instead of silently skipping everything.
        var sut = WriterWith(threshold: "Critical", maxEntries: 50);
        var run = Run(id: 4);
        var findings = new[]
        {
            Finding(Severity.Warning, "CHK200"),
            Finding(Severity.Info, "CHK201"),
        };

        sut.Writer.Write(run, findings);

        Assert.Equal(2, sut.Captured.Count);
        Assert.Contains(sut.Captured, d => d.EventCode == "CHK200");
        Assert.DoesNotContain(sut.Captured, d => d.EventCode == "CHK201");
    }

    [Fact]
    public void Cap_limits_per_finding_entries_and_emits_truncation_summary()
    {
        var sut = WriterWith(threshold: "Info", maxEntries: 2);
        var run = Run(id: 5);
        var findings = Enumerable.Range(0, 5)
            .Select(i => Finding(Severity.Warning, $"CHK{i:D3}"))
            .ToArray();

        sut.Writer.Write(run, findings);

        // Expected: SCAN_COMPLETED + 2 per-finding entries + FINDINGS_TRUNCATED summary = 4.
        Assert.Equal(4, sut.Captured.Count);
        Assert.Contains(sut.Captured, d => d.EventCode == "SCAN_COMPLETED");
        Assert.Contains(sut.Captured, d => d.EventCode == "FINDINGS_TRUNCATED");
        var findingEntries = sut.Captured.Where(d => d.EventCode.StartsWith("CHK", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, findingEntries.Length);
    }

    [Fact]
    public void Truncation_summary_is_skipped_when_cap_is_not_exceeded()
    {
        var sut = WriterWith(threshold: "Info", maxEntries: 10);
        var run = Run(id: 6);
        var findings = new[]
        {
            Finding(Severity.Warning, "CHK300"),
            Finding(Severity.Warning, "CHK301"),
        };

        sut.Writer.Write(run, findings);

        // Just summary + 2 findings — no FINDINGS_TRUNCATED row.
        Assert.Equal(3, sut.Captured.Count);
        Assert.DoesNotContain(sut.Captured, d => d.EventCode == "FINDINGS_TRUNCATED");
    }

    [Fact]
    public void Summary_entry_always_written_even_for_clean_scans()
    {
        // A scan with zero findings should still pulse the event log so admins watching the log
        // see that Sentinel ran and the site is healthy, not that Sentinel silently stopped.
        var sut = WriterWith(threshold: "Info", maxEntries: 50);
        var run = Run(id: 7, total: 0);

        sut.Writer.Write(run, Array.Empty<Finding>());

        var summary = Assert.Single(sut.Captured);
        Assert.Equal("SCAN_COMPLETED", summary.EventCode);
    }

    [Fact]
    public void Zero_cap_emits_only_summary_and_truncation_rows_for_non_empty_scan()
    {
        // MaxEntriesPerScan = 0 should still truncate cleanly rather than dividing by zero or
        // writing a negative-count summary. Guards against a paranoid admin zeroing the cap.
        var sut = WriterWith(threshold: "Info", maxEntries: 0);
        var run = Run(id: 8);
        var findings = new[] { Finding(Severity.Warning, "CHK400") };

        sut.Writer.Write(run, findings);

        Assert.Equal(2, sut.Captured.Count);
        Assert.Contains(sut.Captured, d => d.EventCode == "SCAN_COMPLETED");
        Assert.Contains(sut.Captured, d => d.EventCode == "FINDINGS_TRUNCATED");
    }

    // --- helpers ---

    private static (SentinelEventLogWriter Writer, List<EventLogData> Captured) WriterWith(string threshold, int maxEntries)
    {
        var options = Options.Create(new SentinelOptions
        {
            EventLogIntegration = new SentinelOptions.EventLogOptions
            {
                Enabled = true,
                SeverityThreshold = threshold,
                MaxEntriesPerScan = maxEntries,
            },
        });
        var captured = new List<EventLogData>();
        var writer = new SentinelEventLogWriter(new CapturingEventLogService(captured), options);
        return (writer, captured);
    }

    private static Finding Finding(Severity severity, string ruleId) =>
        new(RuleId: ruleId,
            RuleTitle: "Rule " + ruleId,
            Category: "Test",
            Severity: severity,
            Message: "Test finding " + ruleId,
            Location: null);

    private static ScanRunSummary Run(int id, int total = 0, int errors = 0, int warnings = 0, int infos = 0) => new(
        RunId: id,
        TotalFindings: total,
        ErrorCount: errors,
        WarningCount: warnings,
        InfoCount: infos,
        Trigger: "Test",
        SentinelVersion: "0.0.0-test",
        CompletedAtUtc: DateTime.UtcNow);

    private sealed class CapturingEventLogService(List<EventLogData> sink) : IEventLogService
    {
        public void LogEvent(EventLogData eventLogData) => sink.Add(eventLogData);
    }
}
