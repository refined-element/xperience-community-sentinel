using System.Diagnostics;
using System.Text.Json;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Dependencies;

/// <summary>
/// DEP001 — Shells out to `dotnet list package --outdated --format json` in the repo root and reports
/// every outdated package. Major-version drift is a Warning; minor/patch is Info.
/// </summary>
/// <remarks>
/// `dotnet list package --outdated` filters to stable versions only. When the installed version is itself a
/// prerelease (e.g. `0.4.3-alpha`), the command reports <c>"Not found at the sources"</c> instead of the actual
/// latest prerelease. We detect that case and fall back to an <see cref="INuGetVersionLookup"/> with
/// <c>includePrerelease: true</c> so the check reports accurate results for prerelease-installed packages.
/// </remarks>
public sealed class OutdatedPackagesCheck : ICheck
{
    // The sentinel string `dotnet list package --outdated --format json` emits for packages it can't resolve
    // on the configured sources (most commonly: prerelease-installed packages, because the default search
    // excludes prereleases).
    internal const string NotFoundSentinel = "Not found at the sources";

    private readonly INuGetVersionLookup _versionLookup;

    public OutdatedPackagesCheck() : this(new NuGetOrgVersionLookup()) { }

    public OutdatedPackagesCheck(INuGetVersionLookup versionLookup)
    {
        _versionLookup = versionLookup;
    }

    public string RuleId => "DEP001";
    public string Title => "Outdated NuGet packages";
    public string Category => "Dependencies";
    public CheckKind Kind => CheckKind.Static;

    // NuGet patch/minor drift is informational — "update Stripe.net" isn't work Refined Element quotes on.
    // Kentico platform version bumps live in VER001, which stays QuoteEligible.
    public bool QuoteEligible => false;

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        // Embedded-mode no-op: a deployed XbyK site has no .csproj next to the running DLLs, so
        // `dotnet list package` would produce nothing useful. Rather than surface a confusing INFO
        // about a missing .csproj to operators browsing the admin dashboard, we skip the check
        // entirely — package versions are verified at build time on the dev/CI side where the CLI
        // runs. The CLI path (IsEmbeddedHost == false) retains full behavior against a source repo.
        if (context.IsEmbeddedHost)
        {
            return Array.Empty<Finding>();
        }

        var findings = new List<Finding>();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "list", "package", "--outdated", "--format", "json" },
            WorkingDirectory = context.RepoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            findings.Add(new Finding(
                RuleId, Title, Category, Severity.Info,
                "Could not start `dotnet list package`. Install the .NET SDK to enable this check.",
                Location: context.RepoPath));
            return findings;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            // Non-fatal: a fresh repo without restore, or no projects found. Surface as Info.
            findings.Add(new Finding(
                RuleId, Title, Category, Severity.Info,
                "`dotnet list package --outdated` produced no output. Run `dotnet restore` first, or verify a .sln/.csproj is at the repo root.",
                Location: context.RepoPath));
            return findings;
        }

        return await ParseAndBuildFindingsAsync(stdout, context.RepoPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses the JSON output of <c>dotnet list package --outdated --format json</c> and produces findings.
    /// Internal so tests can drive the parsing + prerelease-fallback logic without shelling out to
    /// <c>dotnet list</c> (which requires a real restored .csproj).
    /// </summary>
    internal async Task<IReadOnlyList<Finding>> ParseAndBuildFindingsAsync(
        string dotnetListJson,
        string repoPath,
        CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();
        try
        {
            using var doc = JsonDocument.Parse(dotnetListJson);

            // `dotnet list package --format json` emits the feed URLs the CLI actually resolved against
            // at the document root. Scoping the prerelease fallback to these sources prevents two
            // bugs: leaking private package IDs to nuget.org when the package only exists on an
            // internal feed, and returning a stale "latest" from nuget.org when a newer version lives
            // on that internal feed.
            var declaredSources = ExtractSources(doc.RootElement);

            if (!doc.RootElement.TryGetProperty("projects", out var projects)) return findings;

            foreach (var project in projects.EnumerateArray())
            {
                var projectPath = project.TryGetProperty("path", out var p) ? p.GetString() : null;
                if (!project.TryGetProperty("frameworks", out var frameworks)) continue;

                foreach (var fw in frameworks.EnumerateArray())
                {
                    if (!fw.TryGetProperty("topLevelPackages", out var packages)) continue;

                    foreach (var pkg in packages.EnumerateArray())
                    {
                        var id = pkg.GetProperty("id").GetString() ?? "(unknown)";
                        var resolved = pkg.GetProperty("resolvedVersion").GetString() ?? "(unknown)";
                        var latest = pkg.GetProperty("latestVersion").GetString() ?? "(unknown)";

                        // Workaround for `dotnet list package --outdated` filtering out prereleases:
                        // when the installed version is a prerelease, ask the repo's declared sources
                        // directly for the latest prerelease and use that instead of the "Not found
                        // at the sources" sentinel.
                        if (string.Equals(latest, NotFoundSentinel, StringComparison.Ordinal)
                            && IsPrerelease(resolved))
                        {
                            var prereleaseLatest = await _versionLookup
                                .GetLatestVersionAsync(id, includePrerelease: true, declaredSources, cancellationToken)
                                .ConfigureAwait(false);

                            if (!string.IsNullOrEmpty(prereleaseLatest))
                            {
                                if (string.Equals(prereleaseLatest, resolved, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Already on the latest prerelease — nothing to report.
                                    continue;
                                }

                                latest = prereleaseLatest;
                            }
                        }

                        var severity = ClassifyDrift(resolved, latest);

                        findings.Add(new Finding(
                            RuleId, Title, Category,
                            severity,
                            $"{id}: {resolved} → {latest}",
                            Location: projectPath,
                            Remediation: $"Update with `dotnet add {projectPath} package {id} --version {latest}` after validating compatibility."));
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            findings.Add(new Finding(
                RuleId, Title, Category, Severity.Warning,
                $"Could not parse `dotnet list package` output: {ex.Message}",
                Location: repoPath));
        }

        return findings;
    }

    private static IReadOnlyList<string>? ExtractSources(JsonElement root)
    {
        if (!root.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<string>();
        foreach (var s in sources.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.String) continue;
            var value = s.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                list.Add(value);
            }
        }

        return list.Count == 0 ? null : list;
    }

    private static bool IsPrerelease(string version) =>
        !string.IsNullOrEmpty(version) && version.Contains('-', StringComparison.Ordinal);

    private static Severity ClassifyDrift(string current, string latest)
    {
        // Extract leading major version from each. Bail out to Info on parse failure.
        if (!TryGetMajor(current, out int curMajor) || !TryGetMajor(latest, out int latMajor))
        {
            return Severity.Info;
        }

        return latMajor > curMajor ? Severity.Warning : Severity.Info;
    }

    private static bool TryGetMajor(string version, out int major)
    {
        major = 0;
        var dot = version.IndexOf('.');
        var leading = dot >= 0 ? version[..dot] : version;
        return int.TryParse(leading, out major);
    }
}
