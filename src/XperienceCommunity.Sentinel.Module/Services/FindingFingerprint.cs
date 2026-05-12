using System.Security.Cryptography;
using System.Text;

using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Module.Services;

/// <summary>
/// Produces a stable 64-character fingerprint for a finding so that acknowledgments/dismissals
/// survive across scan runs. Hash inputs: rule ID, category, a normalized location, and the
/// finding message (with volatile numbers stripped so a drifting count doesn't change the hash).
/// </summary>
internal static class FindingFingerprint
{
    public static string Compute(Finding finding)
    {
        var payload =
            $"{Normalize(finding.RuleId)}|" +
            $"{Normalize(finding.Category)}|" +
            $"{Normalize(finding.Location ?? string.Empty)}|" +
            $"{StripVolatile(Normalize(finding.Message))}";
        return Sha256Hex(payload);
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    /// <summary>
    /// Replace every digit run in the message with a single '#' placeholder so volatile counts
    /// don't churn fingerprints across scans. Note: this strips digits embedded inside tokens too
    /// (e.g. "rule001" → "rule#"), which is intentional — the structured RuleId/Category/Location
    /// inputs already carry the stable identifiers, so the message is free to normalize broadly.
    /// Example: "found 14 unused content types" and "found 17 unused content types" hash identically.
    /// </summary>
    private static string StripVolatile(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsDigit(value[i]))
            {
                sb.Append('#');
                while (i + 1 < value.Length && char.IsDigit(value[i + 1])) i++;
            }
            else sb.Append(value[i]);
        }
        return sb.ToString();
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
