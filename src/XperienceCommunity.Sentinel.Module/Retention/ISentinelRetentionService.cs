namespace XperienceCommunity.Sentinel.Module.Retention;

/// <summary>
/// Trims the Sentinel scan-history tables so a long-running install (daily cadence × years)
/// doesn't accumulate <c>XperienceCommunity_SentinelScanRun</c> / <c>XperienceCommunity_SentinelFinding</c>
/// rows without bound.
///
/// <para>
/// The threshold (N most recent runs to keep) comes from
/// <see cref="Configuration.SentinelOptions.RetentionOptions.MaxScanRunsToKeep"/>. Runs older
/// than the top-N window are deleted together with their findings. Ack rows
/// (<c>XperienceCommunity_SentinelFindingAck</c>) are deliberately left alone — acks are keyed by
/// fingerprint and survive scan regeneration by design; pruning them would unsuppress findings
/// operators have already triaged.
/// </para>
/// </summary>
public interface ISentinelRetentionService
{
    /// <summary>
    /// Runs a single trim pass. Safe to call after every scan — no-ops when the current
    /// scan-run count is already within the configured threshold.
    /// </summary>
    /// <param name="cancellationToken">Observed between the two delete batches (findings, then
    /// scan runs). A cancelled token before the first batch short-circuits; once findings have
    /// been deleted the scan-run delete is allowed to complete to avoid orphaning findings.</param>
    /// <returns>Summary with the count of scan runs + finding rows actually deleted.</returns>
    Task<RetentionSummary> TrimAsync(CancellationToken cancellationToken);
}

/// <summary>Result of a single trim pass.</summary>
/// <param name="ScanRunsDeleted">Number of <c>XperienceCommunity_SentinelScanRun</c> rows removed.</param>
/// <param name="FindingsDeleted">Number of <c>XperienceCommunity_SentinelFinding</c> rows removed.</param>
public sealed record RetentionSummary(int ScanRunsDeleted, int FindingsDeleted);
