using System.Globalization;

namespace XperienceCommunity.Sentinel.Module.Acknowledgment;

/// <summary>
/// Parses the ISO-8601 snooze-until string that the admin UI sends with every Snooze action.
/// Extracted from the page command so the (surprisingly nuanced) date-parsing rules are unit
/// testable without booting Kentico infrastructure.
///
/// <para>
/// The original v0.3.0 / v0.3.1-alpha implementation used <c>DateTime.TryParseExact("O", ...)</c>
/// which silently rejected the JS <c>Date.toISOString()</c> output: the "O" format specifier
/// requires exactly 7 fractional-second digits, but JavaScript emits 3. Every snooze from the
/// admin UI bounced with "Invalid snooze date" and the write never happened.
/// </para>
///
/// <para>
/// The fix uses <see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>
/// instead of <c>DateTime.TryParse</c> + <c>.ToUniversalTime()</c>. The difference matters during
/// DST fall-back: <c>DateTime</c> with <see cref="DateTimeKind.Local"/> is ambiguous in the
/// repeated hour, and <c>.ToUniversalTime()</c> picks one interpretation silently. <c>DateTimeOffset</c>
/// carries the source offset verbatim through the parse and converts to UTC via integer
/// arithmetic, producing an unambiguous instant regardless of what the server's clock is
/// doing. <see cref="CultureInfo.InvariantCulture"/> is still pinned so a dd/MM/yyyy locale
/// can't swap month and day.
/// </para>
/// </summary>
public static class SnoozeDateParser
{
    /// <summary>
    /// Attempts to parse an ISO-8601 snooze-until timestamp and validate that it's at least
    /// one minute in the future. One-minute grace covers client/server clock drift — a snooze
    /// expiring "right now" would read back as Active on the very next refresh, giving the
    /// admin misleading "Snoozed" feedback for a finding that's already un-snoozed.
    /// </summary>
    /// <param name="input">Raw value from the page command — typically JS Date.toISOString().</param>
    /// <param name="nowUtc">Injected "now" so tests can exercise the one-minute-grace boundary.</param>
    /// <param name="utcSnoozeUntil">Parsed UTC timestamp on success.</param>
    /// <param name="errorMessage">Human-readable reason for failure on false return.</param>
    public static bool TryParse(string? input, DateTime nowUtc, out DateTime utcSnoozeUntil, out string errorMessage)
    {
        if (!DateTimeOffset.TryParse(
                input,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)
            // Reject inputs that parsed but had no explicit Z / offset. DateTimeOffset.TryParse
            // assumes LOCAL time when no offset is present, silently giving the server's
            // wall-clock offset to a naive timestamp — exactly the off-by-hours trap we're
            // trying to avoid. Cheapest reliable detector: scan the string itself for a 'Z'
            // or a signed hour-offset marker near the end.
            || !HasExplicitTimezoneMarker(input!))
        {
            utcSnoozeUntil = default;
            errorMessage = "Invalid snooze date — expected ISO-8601 format with UTC designator or offset.";
            return false;
        }
        var utc = parsed.UtcDateTime;
        if (utc <= nowUtc.AddMinutes(1))
        {
            utcSnoozeUntil = default;
            errorMessage = "Snooze date must be at least one minute in the future.";
            return false;
        }
        utcSnoozeUntil = utc;
        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// ISO-8601 explicit-timezone forms end with either 'Z' (UTC) or a signed offset like
    /// "+04:00" / "-04:00" / "+0400". Scan the trailing 6 characters — any longer window
    /// would risk a false match against something like "2026-04" in a malformed input.
    /// </summary>
    private static bool HasExplicitTimezoneMarker(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }
        // Trim to the last 6 chars; everything before that is date/time digits. ISO offsets
        // are 6 chars ("+HH:MM"), 5 chars ("+HHMM"), or 1 char ("Z"). Search for any of the
        // three markers in that window.
        var tail = input.Length > 6 ? input[^6..] : input;
        if (tail.Contains('Z'))
        {
            return true;
        }
        // Offset starts with '+' or '-' followed by two digits. Both indices are cheap bounds
        // checks; we're not running a full regex because this is called on every snooze click.
        for (var i = 0; i < tail.Length - 2; i++)
        {
            var c = tail[i];
            if ((c == '+' || c == '-') && char.IsDigit(tail[i + 1]) && char.IsDigit(tail[i + 2]))
            {
                return true;
            }
        }
        return false;
    }
}
