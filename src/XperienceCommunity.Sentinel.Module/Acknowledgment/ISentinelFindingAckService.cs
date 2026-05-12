namespace XperienceCommunity.Sentinel.Module.Acknowledgment;

/// <summary>
/// Ack workflow over the persisted <c>XperienceCommunity_SentinelFindingAck</c> table. Findings are
/// keyed by their stable fingerprint (SHA-256 of rule-id + normalized-location + digit-stripped
/// message) so an ack survives scan regeneration — ack a noisy finding once, it stays suppressed
/// until the admin revokes it or the snooze expires.
///
/// <para>
/// State machine (values map to <see cref="AckState"/>):
/// <list type="bullet">
///   <item><b>Active</b> — no ack row exists; finding shows at full priority.</item>
///   <item><b>Acknowledged</b> — permanent dismissal until an admin revokes.</item>
///   <item><b>Snoozed</b> — dismissed until <c>SnoozeUntil</c>; naturally reverts to Active.</item>
/// </list>
/// </para>
/// </summary>
public interface ISentinelFindingAckService
{
    /// <summary>Fetches the current ack state for a single finding by its fingerprint.</summary>
    FindingAck? Get(string fingerprint);

    /// <summary>Bulk-load ack state for every fingerprint in the collection; missing = Active.</summary>
    IReadOnlyDictionary<string, FindingAck> GetAll(IEnumerable<string> fingerprints);

    /// <summary>Permanently acknowledges a finding. Idempotent — re-calling refreshes the note.</summary>
    void Acknowledge(string fingerprint, int userId, string? note = null);

    /// <summary>Snoozes a finding until <paramref name="until"/> (UTC). Naturally reverts on expiry.</summary>
    void Snooze(string fingerprint, DateTime until, int userId, string? note = null);

    /// <summary>Revokes an ack/snooze — finding reverts to Active immediately.</summary>
    void Revoke(string fingerprint);

    /// <summary>Count of findings currently Active (no ack or ack expired) across all scans.</summary>
    int CountActive(IEnumerable<string> fingerprints);

    /// <summary>
    /// Bulk acknowledge. Skips blank fingerprints and returns the count actually written, so the
    /// caller can distinguish "asked for 20, wrote 18" from a wholesale failure. Note is applied
    /// to every row (operator's batch reason — "bulk ack of deprecated warnings").
    /// </summary>
    int AcknowledgeMany(IEnumerable<string> fingerprints, int userId, string? note = null);

    /// <summary>Bulk snooze. Same semantics as <see cref="AcknowledgeMany"/>.</summary>
    int SnoozeMany(IEnumerable<string> fingerprints, DateTime until, int userId, string? note = null);

    /// <summary>Bulk revoke — removes ack/snooze rows for all matching fingerprints.</summary>
    int RevokeMany(IEnumerable<string> fingerprints);
}

/// <summary>Snapshot of a finding's ack state.</summary>
/// <param name="Fingerprint">SHA-256 fingerprint from <c>FindingFingerprint.Compute</c>.</param>
/// <param name="State">Current state — see <see cref="AckState"/>.</param>
/// <param name="SnoozeUntil">Expiry (UTC) for Snoozed state; null for Acknowledged/Active.</param>
/// <param name="UserId">CMS user who set the ack.</param>
/// <param name="Note">Operator note explaining the decision.</param>
/// <param name="AckedAt">When the ack was set (UTC).</param>
public sealed record FindingAck(
    string Fingerprint,
    AckState State,
    DateTime? SnoozeUntil,
    int UserId,
    string? Note,
    DateTime AckedAt);

public enum AckState
{
    /// <summary>No ack or snooze expired — finding is live.</summary>
    Active,

    /// <summary>Operator has permanently dismissed this finding.</summary>
    Acknowledged,

    /// <summary>Operator has snoozed this finding; expires at SnoozeUntil.</summary>
    Snoozed,
}
