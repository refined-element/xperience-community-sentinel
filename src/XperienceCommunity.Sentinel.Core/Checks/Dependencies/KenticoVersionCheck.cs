using System.Text.Json;
using System.Text.RegularExpressions;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Dependencies;

/// <summary>
/// VER001 — Detects the Xperience by Kentico version in use (via Kentico.Xperience.WebApp or .Core PackageReference)
/// and compares it against the latest on NuGet.
/// </summary>
public sealed partial class KenticoVersionCheck : ICheck
{
    // The canonical packages that track the platform version. The first one we find, we use.
    private static readonly string[] AnchorPackages =
    [
        "Kentico.Xperience.WebApp",
        "Kentico.Xperience.Core",
        "Kentico.Xperience.Admin",
    ];

    public string RuleId => "VER001";
    public string Title => "Xperience by Kentico version";
    public string Category => "Dependencies";
    public CheckKind Kind => CheckKind.Static;

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        // Embedded-mode no-op: a deployed XbyK site has no .csproj next to the running DLLs, so
        // the PackageReference scan would always come up empty and the check would emit a misleading
        // "No Kentico.Xperience.* PackageReference found" INFO. The version is already verified at
        // build time on the dev/CI side where the CLI runs, and emitting it to operators browsing
        // the admin dashboard is noise. The CLI path (IsEmbeddedHost == false) keeps full behavior.
        if (context.IsEmbeddedHost)
        {
            return Array.Empty<Finding>();
        }

        var findings = new List<Finding>();
        var (anchorPackage, detectedVersion, sourceFile) = FindAnchorReference(context.RepoPath);

        if (anchorPackage is null)
        {
            findings.Add(new Finding(
                RuleId, Title, Category, Severity.Info,
                "No Kentico.Xperience.* PackageReference found. Either this isn't an XbyK project or the package references live in a non-standard location.",
                Location: context.RepoPath));
            return findings;
        }

        var client = context.HttpClientFactory.CreateClient();
        var url = $"https://api.nuget.org/v3-flatcontainer/{anchorPackage.ToLowerInvariant()}/index.json";

        string? latest;
        try
        {
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                findings.Add(new Finding(
                    RuleId, Title, Category, Severity.Info,
                    $"Could not reach NuGet to verify {anchorPackage} latest version (HTTP {(int)response.StatusCode}).",
                    Location: sourceFile));
                return findings;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            latest = ExtractLatestStableVersion(body);
        }
        catch (HttpRequestException ex)
        {
            findings.Add(new Finding(
                RuleId, Title, Category, Severity.Info,
                $"Could not reach NuGet to verify {anchorPackage} latest version: {ex.Message}",
                Location: sourceFile));
            return findings;
        }

        if (string.IsNullOrEmpty(latest))
        {
            findings.Add(new Finding(
                RuleId, Title, Category, Severity.Info,
                $"NuGet returned no versions for {anchorPackage}.",
                Location: sourceFile));
            return findings;
        }

        if (string.Equals(latest, detectedVersion, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new Finding(
                RuleId, Title, Category, Severity.Info,
                $"Xperience by Kentico is on the latest version ({detectedVersion}) via {anchorPackage}.",
                Location: sourceFile));
            return findings;
        }

        var severity = ClassifyDrift(detectedVersion!, latest);
        findings.Add(new Finding(
            RuleId, Title, Category, severity,
            $"Xperience by Kentico {anchorPackage} is on {detectedVersion}; latest on NuGet is {latest}.",
            Location: sourceFile,
            Remediation: $"Update via `dotnet add package {anchorPackage} --version {latest}` after reviewing the release notes at https://docs.kentico.com."));

        return findings;
    }

    private static (string? Anchor, string? Version, string? SourceFile) FindAnchorReference(string repoPath)
    {
        var csprojFiles = Directory.GetFiles(repoPath, "*.csproj", SearchOption.TopDirectoryOnly);
        foreach (var csproj in csprojFiles)
        {
            var content = File.ReadAllText(csproj);
            foreach (var anchor in AnchorPackages)
            {
                var match = PackageReferenceRegex(anchor).Match(content);
                if (match.Success)
                {
                    return (anchor, match.Groups["version"].Value, csproj);
                }
            }
        }
        return (null, null, null);
    }

    private static string? ExtractLatestStableVersion(string nugetIndexJson)
    {
        using var doc = JsonDocument.Parse(nugetIndexJson);
        if (!doc.RootElement.TryGetProperty("versions", out var versions)) return null;

        string? latest = null;
        foreach (var v in versions.EnumerateArray())
        {
            var s = v.GetString();
            if (string.IsNullOrEmpty(s)) continue;
            if (s.Contains('-', StringComparison.Ordinal)) continue; // skip prerelease
            latest = s; // versions are returned in ascending order
        }
        return latest;
    }

    private static Severity ClassifyDrift(string current, string latest)
    {
        var curMajor = ParseMajor(current);
        var latMajor = ParseMajor(latest);
        if (curMajor < 0 || latMajor < 0) return Severity.Info;
        if (latMajor - curMajor >= 2) return Severity.Error;   // two majors behind = no longer supported
        if (latMajor - curMajor == 1) return Severity.Warning; // one major behind
        return Severity.Info;
    }

    private static int ParseMajor(string version)
    {
        var dot = version.IndexOf('.');
        var head = dot >= 0 ? version[..dot] : version;
        return int.TryParse(head, out var m) ? m : -1;
    }

    private static Regex PackageReferenceRegex(string packageId) =>
        new(
            $"<PackageReference\\s+Include=\"{Regex.Escape(packageId)}\"\\s+Version=\"(?<version>[^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
