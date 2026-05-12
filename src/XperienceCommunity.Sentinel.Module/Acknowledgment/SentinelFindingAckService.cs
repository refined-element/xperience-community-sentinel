using CMS.DataEngine;

using XperienceCommunity.Sentinel.Module.InfoModels.SentinelFindingAck;

namespace XperienceCommunity.Sentinel.Module.Acknowledgment;

/// <summary>
/// Format invariant for <c>SentinelFindingFingerprintHash</c> — a SHA-256 hex digest
/// produced by <c>FindingFingerprint.Compute</c>. The DB column is sized for exactly 64 chars;
/// anything else would truncate on write and produce a confusing 500. Call
/// <see cref="FingerprintFormat.IsValid"/> at every layer that accepts a fingerprint from
/// outside code (page commands, API callers) before handing to the persistence layer.
/// </summary>
public static class FingerprintFormat
{
    /// <summary>Expected fingerprint length (SHA-256 hex digest = 32 bytes × 2 chars).</summary>
    public const int Length = 64;

    /// <summary>Returns true iff the value is a 64-char lowercase-or-uppercase hex string.</summary>
    public static bool IsValid(string? value)
    {
        if (value is null || value.Length != Length)
        {
            return false;
        }
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex)
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// Info-provider backed implementation. Snoozed rows whose <c>SnoozeUntil</c> has passed are
/// reported as <see cref="AckState.Active"/> on read — the row stays in the DB so re-snooze
/// or conversion to Acknowledged preserves the operator note, but the finding visibly
/// re-enters the active set automatically without a scheduled cleanup job.
/// </summary>
internal sealed class SentinelFindingAckService(
    IInfoProvider<SentinelFindingAckInfo> ackProvider) : ISentinelFindingAckService
{
    // Internal so unit tests in XperienceCommunity.Sentinel.Tests can exercise ToAck's snooze-expiry logic
    // directly without having to stub the full IInfoProvider<T> surface.
    internal const string StateAcknowledged = "Acknowledged";
    internal const string StateSnoozed = "Snoozed";

    // SQL Server's parameter cap is 2100 per query (including any framework-injected params).
    // Stay well under that so WhereIn() can safely be called with an unbounded fingerprint /
    // scan-id list: chunk the input, issue one query per chunk, merge results in memory.
    private const int SqlInClauseBatchSize = 1_000;

    public FindingAck? Get(string fingerprint)
    {
        var row = LoadRow(fingerprint);
        return row is null ? null : ToAck(row);
    }

    public IReadOnlyDictionary<string, FindingAck> GetAll(IEnumerable<string> fingerprints)
    {
        var hashes = fingerprints.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (hashes.Length == 0)
        {
            return new Dictionary<string, FindingAck>(StringComparer.OrdinalIgnoreCase);
        }

        // Chunk the WhereIn list — a dashboard querying the 50-scan window can pull 10k+
        // fingerprints, which would exceed SQL Server's 2100-parameter cap on one query.
        var rows = QueryAckRowsInBatches(hashes);

        // Defensive group-by: duplicate ack rows for the same fingerprint can appear from
        // concurrent upserts, manual DB edits, or historical bugs. Picking the most recently
        // acked row keeps dashboard / scan-detail pages rendering — losing a stale ack is
        // preferable to throwing on every page load the way a naked ToDictionary would.
        return rows
            .GroupBy(r => r.SentinelFindingAckFingerprintHash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => ToAck(g.OrderByDescending(r => r.SentinelFindingAckAckedAt).First()),
                StringComparer.OrdinalIgnoreCase);
    }

    private List<SentinelFindingAckInfo> QueryAckRowsInBatches(string[] hashes)
    {
        var rows = new List<SentinelFindingAckInfo>(hashes.Length);
        for (var offset = 0; offset < hashes.Length; offset += SqlInClauseBatchSize)
        {
            var batch = hashes.AsSpan(offset, Math.Min(SqlInClauseBatchSize, hashes.Length - offset)).ToArray();
            rows.AddRange(ackProvider.Get()
                .WhereIn(nameof(SentinelFindingAckInfo.SentinelFindingAckFingerprintHash), batch)
                .ToList());
        }
        return rows;
    }

    public void Acknowledge(string fingerprint, int userId, string? note = null) =>
        Upsert(fingerprint, StateAcknowledged, snoozeUntil: null, userId, note);

    public void Snooze(string fingerprint, DateTime until, int userId, string? note = null)
    {
        if (until.Kind == DateTimeKind.Unspecified)
        {
            until = DateTime.SpecifyKind(until, DateTimeKind.Utc);
        }
        Upsert(fingerprint, StateSnoozed, snoozeUntil: until.ToUniversalTime(), userId, note);
    }

    public void Revoke(string fingerprint)
    {
        var row = LoadRow(fingerprint);
        if (row is null)
        {
            return;
        }
        ackProvider.Delete(row);
    }

    public int CountActive(IEnumerable<string> fingerprints)
    {
        var states = GetAll(fingerprints);
        var total = fingerprints.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var suppressed = states.Values.Count(a => a.State != AckState.Active);
        return total - suppressed;
    }

    public int AcknowledgeMany(IEnumerable<string> fingerprints, int userId, string? note = null)
    {
        var written = 0;
        foreach (var fingerprint in Deduplicate(fingerprints))
        {
            Upsert(fingerprint, StateAcknowledged, snoozeUntil: null, userId, note);
            written++;
        }
        return written;
    }

    public int SnoozeMany(IEnumerable<string> fingerprints, DateTime until, int userId, string? note = null)
    {
        if (until.Kind == DateTimeKind.Unspecified)
        {
            until = DateTime.SpecifyKind(until, DateTimeKind.Utc);
        }
        var utc = until.ToUniversalTime();
        var written = 0;
        foreach (var fingerprint in Deduplicate(fingerprints))
        {
            Upsert(fingerprint, StateSnoozed, snoozeUntil: utc, userId, note);
            written++;
        }
        return written;
    }

    public int RevokeMany(IEnumerable<string> fingerprints)
    {
        // Batched WhereIn fetch (same parameter-cap concern as GetAll), then one Delete call
        // each. Kentico's generic IInfoProvider doesn't expose bulk-delete by predicate, so we
        // eat N round-trips on delete — fine at the 10s-to-100s scale of a batch admin action.
        var hashes = Deduplicate(fingerprints).ToArray();
        if (hashes.Length == 0) return 0;

        var rows = QueryAckRowsInBatches(hashes);
        foreach (var row in rows)
        {
            ackProvider.Delete(row);
        }
        return rows.Count;
    }

    // Strip whitespace-only AND malformed fingerprints, then dedupe. The 64-char SHA-256-hex
    // invariant is enforced here (bulk entry point) and again inside Upsert (single entry point)
    // so a bug in one caller can't slip bad data past the other.
    private static IEnumerable<string> Deduplicate(IEnumerable<string> fingerprints) =>
        fingerprints
            .Where(FingerprintFormat.IsValid)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private void Upsert(string fingerprint, string state, DateTime? snoozeUntil, int userId, string? note)
    {
        if (!FingerprintFormat.IsValid(fingerprint))
        {
            // Caught by the UI command's pre-validation under normal flow. Throwing here is a
            // service-boundary invariant: a malformed fingerprint can't be persisted — the DB
            // column is sized for exactly 64 hex chars and a write would truncate silently.
            throw new ArgumentException(
                $"Fingerprint must be a {FingerprintFormat.Length}-character hex string (SHA-256 digest).",
                nameof(fingerprint));
        }

        var row = LoadRow(fingerprint) ?? new SentinelFindingAckInfo
        {
            SentinelFindingAckGuid = Guid.NewGuid(),
            SentinelFindingAckFingerprintHash = fingerprint,
        };
        row.SentinelFindingAckState = state;
        // DateTime fields on Kentico Info objects are not-null by default; use DateTime.MinValue
        // as a sentinel for "no snooze set" so the DB row accepts the write. Read-side translates
        // MinValue back to null in ToAck below.
        row.SentinelFindingAckSnoozeUntil = snoozeUntil ?? default;
        row.SentinelFindingAckUserID = userId;
        row.SentinelFindingAckAckedAt = DateTime.UtcNow;
        row.SentinelFindingAckNote = note ?? string.Empty;
        ackProvider.Set(row);
    }

    private SentinelFindingAckInfo? LoadRow(string fingerprint) =>
        // Order by AckedAt desc to pick the most recently-set row when duplicates exist (the
        // concurrent-insert edge case GetAll already defends against). Without an explicit
        // OrderBy + TopN(1), the "picked" row is nondeterministic — Upsert could update an
        // arbitrary stale duplicate while the real current ack stays untouched.
        ackProvider.Get()
            .WhereEquals(nameof(SentinelFindingAckInfo.SentinelFindingAckFingerprintHash), fingerprint)
            .OrderByDescending(nameof(SentinelFindingAckInfo.SentinelFindingAckAckedAt))
            .TopN(1)
            .FirstOrDefault();

    internal static FindingAck ToAck(SentinelFindingAckInfo row) => MapRow(
        fingerprint: row.SentinelFindingAckFingerprintHash,
        stateColumn: row.SentinelFindingAckState,
        snoozeUntilRaw: row.SentinelFindingAckSnoozeUntil,
        userId: row.SentinelFindingAckUserID,
        noteColumn: row.SentinelFindingAckNote,
        ackedAtRaw: row.SentinelFindingAckAckedAt,
        nowUtc: DateTime.UtcNow);

    /// <summary>
    /// Pure-function read-side mapping from the raw column values to a <see cref="FindingAck"/>.
    /// Factored out so unit tests can exercise the snooze-expiry + state-normalization logic
    /// without booting Kentico's IoC container (which <see cref="SentinelFindingAckInfo"/>
    /// requires in its constructor).
    /// </summary>
    internal static FindingAck MapRow(
        string fingerprint,
        string stateColumn,
        DateTime snoozeUntilRaw,
        int userId,
        string noteColumn,
        DateTime ackedAtRaw,
        DateTime nowUtc)
    {
        // Normalize: trim + case-insensitive comparison. The write path only ever persists the
        // canonical "Acknowledged" / "Snoozed" strings, but the column is free-text and a manual
        // DB edit (or a legacy row written by a pre-release version) could produce "snoozed"
        // or " Acknowledged "; treating those as Active would silently un-suppress findings the
        // operator has already triaged.
        var normalized = stateColumn?.Trim() ?? string.Empty;
        var isAcknowledged = string.Equals(normalized, StateAcknowledged, StringComparison.OrdinalIgnoreCase);
        var isSnoozed = string.Equals(normalized, StateSnoozed, StringComparison.OrdinalIgnoreCase);
        var isSnoozedNow = isSnoozed && snoozeUntilRaw > nowUtc;
        var state = isAcknowledged
            ? AckState.Acknowledged
            : isSnoozedNow
                ? AckState.Snoozed
                : AckState.Active; // snooze expired OR unknown state — natural reversion, no cleanup job
        // Only surface SnoozeUntil while the snooze is still active. Once it expires the finding
        // reports Active; leaving a non-null SnoozeUntil on an Active result would confuse the
        // UI ("looks snoozed but behaves active"). Acknowledged is permanent so never carries an
        // expiry either.
        var snoozeUntil = isSnoozedNow && snoozeUntilRaw != default
            ? DateTime.SpecifyKind(snoozeUntilRaw, DateTimeKind.Utc)
            : (DateTime?)null;
        return new FindingAck(
            Fingerprint: fingerprint,
            State: state,
            SnoozeUntil: snoozeUntil,
            UserId: userId,
            Note: string.IsNullOrWhiteSpace(noteColumn) ? null : noteColumn,
            AckedAt: DateTime.SpecifyKind(ackedAtRaw, DateTimeKind.Utc));
    }
}
