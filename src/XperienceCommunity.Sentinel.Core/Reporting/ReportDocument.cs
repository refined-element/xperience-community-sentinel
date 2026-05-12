using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Reporting;

/// <summary>
/// The canonical, serializable representation of a scan's output. Report writers (JSON, HTML) and the
/// quote submitter all consume this shape — never <see cref="ScanResult"/> directly — so the wire
/// format stays stable as internal types evolve.
/// </summary>
public sealed record ReportDocument(
    string SentinelVersion,
    ReportScan Scan,
    ReportSummary Summary,
    IReadOnlyList<ReportExecution> Executions,
    IReadOnlyList<ReportFinding> Findings);

public sealed record ReportScan(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    double DurationSeconds,
    string RepoPath,
    bool RuntimeEnabled);

public sealed record ReportSummary(int Total, int Errors, int Warnings, int Info);

public sealed record ReportExecution(
    string RuleId,
    string Title,
    string Kind,
    string Status,
    double DurationMs,
    string? ErrorMessage);

public sealed record ReportFinding(
    string RuleId,
    string RuleTitle,
    string Category,
    string Severity,
    string Message,
    string? Location,
    string? Remediation,
    bool QuoteEligible);

public static class ReportBuilder
{
    public const string SentinelVersion = "0.1.0-alpha";

    public static ReportDocument Build(ScanResult result) => new(
        SentinelVersion: SentinelVersion,
        Scan: new ReportScan(
            StartedAt: result.StartedAt,
            CompletedAt: result.CompletedAt,
            DurationSeconds: Math.Round(result.Duration.TotalSeconds, 3),
            RepoPath: result.RepoPath,
            RuntimeEnabled: result.RuntimeEnabled),
        Summary: new ReportSummary(
            Total: result.Findings.Count,
            Errors: result.ErrorCount,
            Warnings: result.WarningCount,
            Info: result.InfoCount),
        Executions: result.Executions
            .Select(e => new ReportExecution(
                e.RuleId, e.Title, e.Kind.ToString(), e.Status.ToString(),
                Math.Round(e.Duration.TotalMilliseconds, 1),
                e.ErrorMessage))
            .ToArray(),
        Findings: result.Findings
            .Select(f => new ReportFinding(
                f.RuleId, f.RuleTitle, f.Category, f.Severity.ToString(),
                f.Message, f.Location, f.Remediation, f.QuoteEligible))
            .ToArray());
}
