using System.Text.RegularExpressions;

namespace XperienceCommunity.Sentinel.Cloning;

/// <summary>
/// Normalizes repo identifiers accepted by <c>sentinel scan --repo</c>. Accepts:
///   * <c>owner/name</c>                        → https://github.com/owner/name.git
///   * <c>github.com/owner/name</c>             → https://github.com/owner/name.git
///   * <c>https://github.com/owner/name[.git]</c> → normalized HTTPS URL
///   * <c>git@github.com:owner/name.git</c>    → passed through untouched
/// </summary>
public static partial class GitRepoUrl
{
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Repository identifier is empty.", nameof(input));
        }

        input = input.Trim();

        if (input.StartsWith("git@", StringComparison.Ordinal))
        {
            return input;
        }

        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return input.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? input : input + ".git";
        }

        if (input.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
        {
            input = input["github.com/".Length..];
        }

        var match = ShorthandRegex().Match(input);
        if (!match.Success)
        {
            throw new ArgumentException($"Cannot parse repository identifier '{input}'. Use owner/name or a full URL.", nameof(input));
        }

        return $"https://github.com/{match.Groups["owner"].Value}/{match.Groups["name"].Value}.git";
    }

    [GeneratedRegex(@"^(?<owner>[A-Za-z0-9][A-Za-z0-9\-_.]*)/(?<name>[A-Za-z0-9][A-Za-z0-9\-_.]*?)(?:\.git)?$")]
    private static partial Regex ShorthandRegex();
}
