using XperienceCommunity.Sentinel.Module.Acknowledgment;

namespace XperienceCommunity.Sentinel.Tests.XbyK;

/// <summary>
/// Regression tests for the ISO-8601 parse behavior that broke v0.3.0 / v0.3.1-alpha snooze.
/// JS's <c>Date.toISOString()</c> emits 3-digit fractional seconds; <c>TryParseExact("O", ...)</c>
/// requires 7; so every snooze from the admin UI silently failed validation and no ack row was
/// written. These tests lock down the fix: <see cref="SnoozeDateParser.TryParse"/> must accept
/// every ISO-8601 variant a browser might send.
/// </summary>
public class SnoozeDateParserTests
{
    private static readonly DateTime Now = new(2026, 4, 22, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    // JS toISOString() — the actual format that broke the ship.
    [InlineData("2026-04-29T12:00:00.000Z")]
    // C# ToString("O") — 7-digit fractional seconds + Z.
    [InlineData("2026-04-29T12:00:00.0000000Z")]
    // No fractional seconds + Z.
    [InlineData("2026-04-29T12:00:00Z")]
    // Explicit positive UTC offset (effectively UTC).
    [InlineData("2026-04-29T12:00:00+00:00")]
    // Non-UTC offset — parser should normalize to UTC.
    [InlineData("2026-04-29T08:00:00-04:00")]
    public void Accepts_common_iso8601_variants_a_browser_might_send(string input)
    {
        var ok = SnoozeDateParser.TryParse(input, Now, out var utc, out var err);

        Assert.True(ok, $"Parse should succeed for {input}. Got error: {err}");
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(string.Empty, err);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    // Missing UTC designator / offset — RoundtripKind rejects ambiguous timestamps.
    [InlineData("2026-04-29T12:00:00.000")]
    [InlineData("29/04/2026")]
    public void Rejects_malformed_or_ambiguous_input(string? input)
    {
        var ok = SnoozeDateParser.TryParse(input, Now, out var utc, out var err);

        Assert.False(ok);
        Assert.Equal(default, utc);
        Assert.Equal("Invalid snooze date — expected ISO-8601 format with UTC designator or offset.", err);
    }

    [Fact]
    public void Rejects_past_timestamp()
    {
        var ok = SnoozeDateParser.TryParse("2020-01-01T00:00:00.000Z", Now, out var utc, out var err);

        Assert.False(ok);
        Assert.Equal(default, utc);
        Assert.Equal("Snooze date must be at least one minute in the future.", err);
    }

    [Fact]
    public void Rejects_near_now_timestamp_within_the_one_minute_grace_window()
    {
        // 30 seconds in the future — inside the 1-minute grace — rejected.
        var thirtySecondsOut = Now.AddSeconds(30).ToString("O");
        var ok = SnoozeDateParser.TryParse(thirtySecondsOut, Now, out _, out var err);

        Assert.False(ok);
        Assert.Equal("Snooze date must be at least one minute in the future.", err);
    }

    [Fact]
    public void Accepts_timestamp_just_past_the_grace_window()
    {
        // 90 seconds in the future — outside the 1-minute grace — accepted.
        var ninetySecondsOut = Now.AddSeconds(90).ToString("O");
        var ok = SnoozeDateParser.TryParse(ninetySecondsOut, Now, out var utc, out _);

        Assert.True(ok);
        Assert.True(utc > Now.AddMinutes(1));
    }

    [Fact]
    public void Non_utc_offset_is_normalized_to_utc()
    {
        // 08:00 at -04:00 offset == 12:00 UTC. Verifies ToUniversalTime() applies.
        var ok = SnoozeDateParser.TryParse("2026-05-01T08:00:00-04:00", Now, out var utc, out _);

        Assert.True(ok);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc), utc);
    }
}
