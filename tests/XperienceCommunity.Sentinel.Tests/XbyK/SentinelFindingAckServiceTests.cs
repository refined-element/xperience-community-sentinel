using XperienceCommunity.Sentinel.Module.Acknowledgment;

namespace XperienceCommunity.Sentinel.Tests.XbyK;

/// <summary>
/// Covers the pure-function read-mapping half of the ack service: how raw column values map to
/// a <see cref="FindingAck"/>. The subtle rules are (a) snoozes that have expired read back as
/// Active even though the DB row still says "Snoozed", and (b) expired snoozes surface with a
/// null <c>SnoozeUntil</c> so the UI doesn't render "snoozed but active" confusion.
///
/// <para>Not covered here: the write path (Upsert / Acknowledge / Snooze / Revoke) which goes
/// through Kentico's <c>IInfoProvider&lt;T&gt;</c> — that requires a mocking framework. Adding
/// one is tracked as follow-up work; the pure read-mapping is the behavior most likely to
/// silently regress.</para>
/// </summary>
public class SentinelFindingAckServiceTests
{
    private const string Fp = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly DateTime Now = new(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Acknowledged_row_maps_to_Acknowledged_with_null_SnoozeUntil()
    {
        var ack = SentinelFindingAckService.MapRow(
            fingerprint: Fp,
            stateColumn: "Acknowledged",
            snoozeUntilRaw: default,
            userId: 7,
            noteColumn: string.Empty,
            ackedAtRaw: Now,
            nowUtc: Now);

        Assert.Equal(AckState.Acknowledged, ack.State);
        Assert.Null(ack.SnoozeUntil);
    }

    [Fact]
    public void Active_snooze_maps_to_Snoozed_with_UTC_SnoozeUntil()
    {
        var future = Now.AddDays(7);
        var ack = SentinelFindingAckService.MapRow(
            fingerprint: Fp,
            stateColumn: "Snoozed",
            snoozeUntilRaw: future,
            userId: 7,
            noteColumn: string.Empty,
            ackedAtRaw: Now,
            nowUtc: Now);

        Assert.Equal(AckState.Snoozed, ack.State);
        Assert.NotNull(ack.SnoozeUntil);
        Assert.Equal(DateTimeKind.Utc, ack.SnoozeUntil!.Value.Kind);
    }

    [Fact]
    public void Expired_snooze_maps_to_Active_with_null_SnoozeUntil()
    {
        // The DB row still says "Snoozed" but the expiry has passed — MapRow must collapse this
        // to Active and strip SnoozeUntil so the UI doesn't render a paradox ("this finding
        // says Active but also shows a snooze date").
        var past = Now.AddDays(-1);
        var ack = SentinelFindingAckService.MapRow(
            fingerprint: Fp,
            stateColumn: "Snoozed",
            snoozeUntilRaw: past,
            userId: 7,
            noteColumn: string.Empty,
            ackedAtRaw: Now,
            nowUtc: Now);

        Assert.Equal(AckState.Active, ack.State);
        Assert.Null(ack.SnoozeUntil);
    }

    [Fact]
    public void Snooze_at_exactly_now_is_Active()
    {
        // Boundary: a snooze whose expiry is "right now" reads as expired. Gives the read path
        // a consistent tie-breaker — we'd rather let a finding reappear one instant early than
        // keep it suppressed past its declared expiry.
        var ack = SentinelFindingAckService.MapRow(
            fingerprint: Fp,
            stateColumn: "Snoozed",
            snoozeUntilRaw: Now,
            userId: 7,
            noteColumn: string.Empty,
            ackedAtRaw: Now,
            nowUtc: Now);

        Assert.Equal(AckState.Active, ack.State);
    }

    [Theory]
    [InlineData("acknowledged")]
    [InlineData("ACKNOWLEDGED")]
    [InlineData("  Acknowledged ")]
    public void Acknowledged_state_mapping_is_case_insensitive_and_trimmed(string stateColumn)
    {
        // Write path only persists canonical "Acknowledged" / "Snoozed" strings, but the column
        // is free-text — a manual DB edit could produce "acknowledged" and we must still honour
        // the operator's intent instead of silently re-surfacing the finding as Active.
        var ack = SentinelFindingAckService.MapRow(
            fingerprint: Fp,
            stateColumn: stateColumn,
            snoozeUntilRaw: default,
            userId: 7,
            noteColumn: string.Empty,
            ackedAtRaw: Now,
            nowUtc: Now);

        Assert.Equal(AckState.Acknowledged, ack.State);
    }

    [Fact]
    public void Snoozed_state_mapping_is_case_insensitive_and_trimmed()
    {
        var future = Now.AddDays(3);
        var ack = SentinelFindingAckService.MapRow(
            fingerprint: Fp,
            stateColumn: "  snoozed",
            snoozeUntilRaw: future,
            userId: 7,
            noteColumn: string.Empty,
            ackedAtRaw: Now,
            nowUtc: Now);

        Assert.Equal(AckState.Snoozed, ack.State);
    }

    [Fact]
    public void Unknown_state_string_maps_to_Active_safely()
    {
        // If a manual DB edit or a legacy schema puts an unexpected string in the State column,
        // MapRow must NOT throw — that would break the dashboard render for every user. Falling
        // back to Active is the safe behavior (finding reappears and can be re-acked via the UI).
        var ack = SentinelFindingAckService.MapRow(
            fingerprint: Fp,
            stateColumn: "Mystery",
            snoozeUntilRaw: default,
            userId: 7,
            noteColumn: string.Empty,
            ackedAtRaw: Now,
            nowUtc: Now);

        Assert.Equal(AckState.Active, ack.State);
    }

    [Fact]
    public void Whitespace_note_is_normalized_to_null_on_the_DTO()
    {
        // The DB column is NOT NULL text; the service writes string.Empty for "no note" but
        // exposes it as null on read so callers don't have to double-check for blanks.
        var ack = SentinelFindingAckService.MapRow(
            fingerprint: Fp,
            stateColumn: "Acknowledged",
            snoozeUntilRaw: default,
            userId: 7,
            noteColumn: "   ",
            ackedAtRaw: Now,
            nowUtc: Now);

        Assert.Null(ack.Note);
    }

    [Fact]
    public void Non_empty_note_round_trips()
    {
        var ack = SentinelFindingAckService.MapRow(
            fingerprint: Fp,
            stateColumn: "Acknowledged",
            snoozeUntilRaw: default,
            userId: 7,
            noteColumn: "Known MVC-fallback path",
            ackedAtRaw: Now,
            nowUtc: Now);

        Assert.Equal("Known MVC-fallback path", ack.Note);
    }

    [Fact]
    public void AckedAt_is_normalized_to_UTC_kind()
    {
        // SQL returns DateTimeKind.Unspecified; the client would otherwise interpret as local.
        var unspecified = DateTime.SpecifyKind(new DateTime(2026, 4, 15, 12, 0, 0), DateTimeKind.Unspecified);
        var ack = SentinelFindingAckService.MapRow(
            fingerprint: Fp,
            stateColumn: "Acknowledged",
            snoozeUntilRaw: default,
            userId: 7,
            noteColumn: string.Empty,
            ackedAtRaw: unspecified,
            nowUtc: Now);

        Assert.Equal(DateTimeKind.Utc, ack.AckedAt.Kind);
    }
}
