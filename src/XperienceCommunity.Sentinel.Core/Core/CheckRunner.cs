using System.Diagnostics;

namespace XperienceCommunity.Sentinel.Core;

/// <summary>
/// Executes a collection of checks against a <see cref="ScanContext"/> and aggregates the results.
/// A failed check does not abort the run — it's captured as a CheckFailed execution entry.
/// </summary>
public sealed class CheckRunner
{
    private readonly IReadOnlyList<ICheck> _checks;

    public CheckRunner(IReadOnlyList<ICheck> checks)
    {
        _checks = checks;
    }

    public async Task<ScanResult> RunAsync(
        ScanContext context,
        IProgress<CheckProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var findings = new List<Finding>();
        var executions = new List<CheckExecution>(_checks.Count);

        for (var i = 0; i < _checks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var check = _checks[i];
            progress?.Report(new CheckProgress(i + 1, _checks.Count, check));

            if (check.Kind == CheckKind.Runtime && !context.RuntimeEnabled)
            {
                executions.Add(new CheckExecution(
                    check.RuleId,
                    check.Title,
                    check.Kind,
                    CheckExecutionStatus.SkippedRuntime,
                    TimeSpan.Zero));
                continue;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var checkFindings = await check.RunAsync(context, cancellationToken).ConfigureAwait(false);
                sw.Stop();
                // Stamp each finding with the AND of check-level and finding-level eligibility so the
                // sanitizer doesn't need to re-resolve the check later. A check can declare itself
                // info-only (DEP001) AND individual findings can opt out (CNT006 low-count warnings).
                foreach (var f in checkFindings)
                {
                    findings.Add(f with { QuoteEligible = f.QuoteEligible && check.QuoteEligible });
                }
                executions.Add(new CheckExecution(
                    check.RuleId,
                    check.Title,
                    check.Kind,
                    CheckExecutionStatus.Ran,
                    sw.Elapsed));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                executions.Add(new CheckExecution(
                    check.RuleId,
                    check.Title,
                    check.Kind,
                    CheckExecutionStatus.Failed,
                    sw.Elapsed,
                    ex.Message));
                findings.Add(new Finding(
                    RuleId: "SYS001",
                    RuleTitle: "Check failed to execute",
                    Category: "Sentinel Internals",
                    Severity: Severity.Warning,
                    Message: $"Check '{check.RuleId} — {check.Title}' threw an exception: {ex.Message}",
                    Location: check.GetType().FullName,
                    Remediation: "Report this at https://github.com/refined-element/xperience-community-sentinel/issues."));
            }
        }

        return new ScanResult
        {
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            RepoPath = context.RepoPath,
            RuntimeEnabled = context.RuntimeEnabled,
            Findings = findings,
            Executions = executions,
        };
    }
}

/// <summary>Progress event emitted as each check starts.</summary>
public sealed record CheckProgress(int Index, int Total, ICheck Check);
