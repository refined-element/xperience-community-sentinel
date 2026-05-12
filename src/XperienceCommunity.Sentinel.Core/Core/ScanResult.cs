namespace XperienceCommunity.Sentinel.Core;

/// <summary>
/// Aggregated output of a scan run. Consumed by the report generators and the quote submitter.
/// </summary>
public sealed record ScanResult
{
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required string RepoPath { get; init; }
    public required bool RuntimeEnabled { get; init; }
    public required IReadOnlyList<Finding> Findings { get; init; }
    public required IReadOnlyList<CheckExecution> Executions { get; init; }

    public TimeSpan Duration => CompletedAt - StartedAt;
    public int ErrorCount => Findings.Count(f => f.Severity == Severity.Error);
    public int WarningCount => Findings.Count(f => f.Severity == Severity.Warning);
    public int InfoCount => Findings.Count(f => f.Severity == Severity.Info);

    /// <summary>
    /// Returns the highest severity represented in <see cref="Findings"/>,
    /// or <see cref="Severity.Info"/> if there are no findings at all.
    /// </summary>
    public Severity MaxSeverity()
    {
        if (Findings.Count == 0)
        {
            return Severity.Info;
        }

        return Findings.Max(f => f.Severity);
    }
}

/// <summary>
/// Per-check execution metadata. Used to surface skipped checks and individual check failures in the report.
/// </summary>
public sealed record CheckExecution(
    string RuleId,
    string Title,
    CheckKind Kind,
    CheckExecutionStatus Status,
    TimeSpan Duration,
    string? ErrorMessage = null);

public enum CheckExecutionStatus
{
    Ran,
    SkippedRuntime,
    Failed,
}
