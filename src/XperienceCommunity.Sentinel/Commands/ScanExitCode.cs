using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Commands;

/// <summary>
/// Threshold for <c>sentinel scan --fail-on</c>. Selects the lowest <see cref="Severity"/>
/// that should flip the process exit code to <see cref="ScanExitCode.ThresholdTripped"/>.
/// <see cref="None"/> is the explicit opt-out — the scan always exits 0 unless it crashed.
/// </summary>
public enum FailOnThreshold
{
    /// <summary>Any finding (Info, Warning, or Error) trips the threshold.</summary>
    Info,

    /// <summary>Warning or Error findings trip the threshold.</summary>
    Warning,

    /// <summary>Only Error findings trip the threshold.</summary>
    Error,

    /// <summary>Disabled — the scan never returns a non-zero exit code based on findings.</summary>
    None,
}

/// <summary>
/// Exit codes for <c>sentinel scan</c>. CI systems distinguish between a clean pass,
/// a tool crash, and a threshold breach by inspecting the process exit code.
/// </summary>
/// <remarks>
/// We deliberately keep threshold-tripped separate from crash/usage errors so a PR gate
/// script can tell "the scan ran fine and found issues you care about" apart from
/// "the scan failed to execute" — those are very different incidents in CI.
/// </remarks>
public static class ScanExitCode
{
    /// <summary>Scan completed successfully and the <c>--fail-on</c> threshold was not tripped.</summary>
    public const int Success = 0;

    /// <summary>
    /// Scan crashed, a CLI argument was invalid, or an I/O operation failed
    /// (e.g. path not found, clone failure, malformed connection string).
    /// </summary>
    public const int Error = 1;

    /// <summary>
    /// Scan ran to completion but the findings met or exceeded the <c>--fail-on</c> threshold.
    /// Distinct from <see cref="Error"/> so CI can differentiate "the tool crashed" from
    /// "the tool found issues the pipeline should fail on."
    /// </summary>
    public const int ThresholdTripped = 2;

    /// <summary>
    /// Pure evaluator: decides whether the collected findings trip the given threshold,
    /// returning either <see cref="Success"/> or <see cref="ThresholdTripped"/>.
    /// Unit-testable without spinning up a full scan.
    /// </summary>
    /// <param name="findings">All findings produced by the scan.</param>
    /// <param name="threshold">The lowest severity that should trip the build.</param>
    /// <returns><see cref="Success"/> (0) or <see cref="ThresholdTripped"/> (2).</returns>
    public static int Evaluate(IReadOnlyList<Finding> findings, FailOnThreshold threshold)
    {
        ArgumentNullException.ThrowIfNull(findings);

        if (threshold == FailOnThreshold.None || findings.Count == 0)
        {
            return Success;
        }

        // Map the threshold to the minimum Severity that should count. Info catches everything,
        // Warning catches Warning+Error, Error catches only Error.
        var minSeverity = threshold switch
        {
            FailOnThreshold.Info => Severity.Info,
            FailOnThreshold.Warning => Severity.Warning,
            FailOnThreshold.Error => Severity.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "Unknown FailOnThreshold."),
        };

        var trippedCount = 0;
        foreach (var finding in findings)
        {
            if (finding.Severity >= minSeverity)
            {
                trippedCount++;
            }
        }

        return trippedCount > 0 ? ThresholdTripped : Success;
    }

    /// <summary>
    /// Count the findings at or above the given threshold. Used to build the human-readable
    /// summary line we print before returning a non-zero exit code, so CI logs tell you
    /// <em>why</em> the build failed rather than just "exit 2".
    /// </summary>
    public static int CountAtOrAboveThreshold(IReadOnlyList<Finding> findings, FailOnThreshold threshold)
    {
        ArgumentNullException.ThrowIfNull(findings);

        if (threshold == FailOnThreshold.None || findings.Count == 0)
        {
            return 0;
        }

        var minSeverity = threshold switch
        {
            FailOnThreshold.Info => Severity.Info,
            FailOnThreshold.Warning => Severity.Warning,
            FailOnThreshold.Error => Severity.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "Unknown FailOnThreshold."),
        };

        var count = 0;
        foreach (var finding in findings)
        {
            if (finding.Severity >= minSeverity)
            {
                count++;
            }
        }
        return count;
    }
}
