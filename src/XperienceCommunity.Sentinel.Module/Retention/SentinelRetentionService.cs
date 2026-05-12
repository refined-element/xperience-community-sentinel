using CMS.DataEngine;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using XperienceCommunity.Sentinel.Module.Configuration;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelFinding;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelScanRun;

namespace XperienceCommunity.Sentinel.Module.Retention;

/// <summary>
/// Info-provider backed retention trimmer. Follows the query-and-delete pattern used elsewhere
/// (<see cref="Acknowledgment.SentinelFindingAckService"/>) — Kentico's generic
/// <see cref="IInfoProvider{TInfo}"/> doesn't expose a bulk-delete-by-predicate verb, so the
/// trim runs as: read the doomed IDs with a projection, WhereIn-batch delete findings, then
/// delete the scan-run rows themselves.
/// </summary>
internal sealed class SentinelRetentionService(
    IOptions<SentinelOptions> options,
    IInfoProvider<SentinelScanRunInfo> scanRunProvider,
    IInfoProvider<SentinelFindingInfo> findingProvider,
    ILogger<SentinelRetentionService> logger) : ISentinelRetentionService
{
    // Same 2100-parameter ceiling on SQL Server IN() clauses that SentinelFindingAckService
    // defends against. Retention will rarely push past it (you'd have to be trimming 1000+
    // runs in a single pass — only happens the first time after lowering MaxScanRunsToKeep
    // or on a freshly-upgraded install), but chunking keeps us safe.
    private const int SqlInClauseBatchSize = 1_000;

    private readonly SentinelOptions options = options.Value;

    public Task<RetentionSummary> TrimAsync(CancellationToken cancellationToken)
    {
        var keep = options.Retention.MaxScanRunsToKeep;

        // Disabled-by-config short-circuit. Zero or negative means "keep forever"; callers
        // (scan-completion pipeline) still invoke Trim blindly because their decision tree is
        // simpler without a conditional.
        if (keep <= 0)
        {
            logger.LogDebug(
                "Sentinel retention: disabled (MaxScanRunsToKeep={Keep}). No trim performed.",
                keep);
            return Task.FromResult(new RetentionSummary(0, 0));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Step 1: Project only the SentinelScanRunID column for the "keep" window. Columns()
        // keeps the round-trip small — the full Info object has 12 columns including large
        // text fields (ErrorMessage) we don't need here.
        var keepIds = scanRunProvider.Get()
            .OrderByDescending(nameof(SentinelScanRunInfo.SentinelScanRunID))
            .TopN(keep)
            .Columns(nameof(SentinelScanRunInfo.SentinelScanRunID))
            .ToList()
            .Select(r => r.SentinelScanRunID)
            .ToArray();

        // Nothing to compare against — the table is empty or has fewer rows than the keep
        // window. Aborting early avoids the "empty keep set means delete everything" trap and
        // saves a round-trip.
        if (keepIds.Length == 0)
        {
            return Task.FromResult(new RetentionSummary(0, 0));
        }

        // Step 2: Find the doomed scan-run IDs via a cutoff comparison, NOT a NOT-IN over the
        // keep set. The keep set is monotonic (top-N by ID desc = the N largest IDs), so
        // "anything below the smallest keep ID" is equivalent to "anything not in the keep
        // set". Using WhereLessThan dodges SQL Server's ~2100-parameter limit entirely —
        // if an operator misconfigures MaxScanRunsToKeep to 3000, a NOT-IN query would fail
        // with a "too many parameters" SqlException.
        var minKeepId = keepIds.Min();
        var doomedIds = scanRunProvider.Get()
            .WhereLessThan(nameof(SentinelScanRunInfo.SentinelScanRunID), minKeepId)
            .Columns(nameof(SentinelScanRunInfo.SentinelScanRunID))
            .ToList()
            .Select(r => r.SentinelScanRunID)
            .ToArray();

        if (doomedIds.Length == 0)
        {
            return Task.FromResult(new RetentionSummary(0, 0));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Step 3: Delete findings first (in WhereIn-batches to dodge the SQL param cap), then
        // the scan-run headers. Order matters: if we deleted the scan-run rows first and the
        // process died mid-pass, we'd leave orphan findings whose FK target no longer exists.
        var findingsDeleted = DeleteFindingsForScanRuns(doomedIds);
        var scanRunsDeleted = DeleteScanRuns(doomedIds);

        logger.LogInformation(
            "Sentinel retention: trimmed {ScanRuns} scan runs and {Findings} findings (kept latest {Keep}).",
            scanRunsDeleted, findingsDeleted, keep);

        return Task.FromResult(new RetentionSummary(scanRunsDeleted, findingsDeleted));
    }

    private int DeleteFindingsForScanRuns(int[] doomedScanRunIds)
    {
        var deleted = 0;
        for (var offset = 0; offset < doomedScanRunIds.Length; offset += SqlInClauseBatchSize)
        {
            var batch = doomedScanRunIds
                .AsSpan(offset, Math.Min(SqlInClauseBatchSize, doomedScanRunIds.Length - offset))
                .ToArray();
            var rows = findingProvider.Get()
                .WhereIn(nameof(SentinelFindingInfo.SentinelFindingScanRunID), batch)
                .ToList();
            foreach (var row in rows)
            {
                findingProvider.Delete(row);
                deleted++;
            }
        }
        return deleted;
    }

    private int DeleteScanRuns(int[] doomedScanRunIds)
    {
        var deleted = 0;
        for (var offset = 0; offset < doomedScanRunIds.Length; offset += SqlInClauseBatchSize)
        {
            var batch = doomedScanRunIds
                .AsSpan(offset, Math.Min(SqlInClauseBatchSize, doomedScanRunIds.Length - offset))
                .ToArray();
            var rows = scanRunProvider.Get()
                .WhereIn(nameof(SentinelScanRunInfo.SentinelScanRunID), batch)
                .ToList();
            foreach (var row in rows)
            {
                scanRunProvider.Delete(row);
                deleted++;
            }
        }
        return deleted;
    }
}

/// <summary>
/// Pure-function selection logic for retention — given the set of currently-present scan-run
/// IDs and a <c>MaxScanRunsToKeep</c> threshold, decide which IDs should be deleted. Factored
/// out of <see cref="SentinelRetentionService"/> so unit tests can exercise the "top-N keep /
/// rest delete" rule without booting Kentico's <see cref="IInfoProvider{TInfo}"/> surface.
/// </summary>
public static class RetentionSelection
{
    /// <summary>
    /// Returns the subset of <paramref name="existingScanRunIds"/> that should be deleted —
    /// everything outside the top-N (ordered by ID descending, i.e. the newest runs kept).
    /// </summary>
    /// <param name="existingScanRunIds">Every scan-run ID currently in the DB.</param>
    /// <param name="maxToKeep">Threshold from
    /// <see cref="SentinelOptions.RetentionOptions.MaxScanRunsToKeep"/>. Values ≤ 0 are treated
    /// as "disabled" — empty result.</param>
    /// <returns>IDs to delete (stable order, descending).</returns>
    public static IReadOnlyList<int> SelectIdsToDelete(IEnumerable<int> existingScanRunIds, int maxToKeep)
    {
        ArgumentNullException.ThrowIfNull(existingScanRunIds);

        // "Disabled" short-circuit matches the service's runtime behavior: the caller should be
        // able to pass MaxScanRunsToKeep directly without a guard at the call site.
        if (maxToKeep <= 0)
        {
            return Array.Empty<int>();
        }

        // Distinct() before sorting so a DB with duplicate IDs (shouldn't happen — PK enforced —
        // but defensive) doesn't blow through the keep window by counting duplicates as distinct
        // slots. Descending sort = newest first (monotonically increasing IDENTITY column).
        var sorted = existingScanRunIds
            .Distinct()
            .OrderByDescending(id => id)
            .ToArray();

        if (sorted.Length <= maxToKeep)
        {
            return Array.Empty<int>();
        }

        // Skip the first N (the keep set); everything else is doomed. Returning as an array
        // keeps the type stable and avoids re-enumerating the deferred LINQ chain at the
        // call site.
        return sorted.Skip(maxToKeep).ToArray();
    }
}
