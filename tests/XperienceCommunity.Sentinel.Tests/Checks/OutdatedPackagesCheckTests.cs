using XperienceCommunity.Sentinel.Tests.Support;
using XperienceCommunity.Sentinel.Checks.Dependencies;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Tests.Checks;

public class OutdatedPackagesCheckTests
{
    /// <summary>
    /// Regression for the DEP001 prerelease bug: when `dotnet list package --outdated --format json`
    /// emits <c>"Not found at the sources"</c> for a package whose resolved version is a prerelease
    /// (e.g. <c>0.4.3-alpha</c>), the check should fall back to an <see cref="INuGetVersionLookup"/>
    /// with <c>includePrerelease: true</c>. If the prerelease lookup returns the same version that's
    /// installed, the package is on the latest prerelease and no finding should be emitted.
    /// </summary>
    [Fact]
    public async Task Prerelease_installed_on_latest_prerelease_emits_no_finding()
    {
        const string json = """
        {
          "version": 1,
          "parameters": "--outdated",
          "sources": [ "https://api.nuget.org/v3/index.json" ],
          "projects": [
            {
              "path": "C:/repo/example.csproj",
              "frameworks": [
                {
                  "framework": "net9.0",
                  "topLevelPackages": [
                    {
                      "id": "XperienceCommunity.Sentinel.Module",
                      "requestedVersion": "0.4.3-alpha",
                      "resolvedVersion": "0.4.3-alpha",
                      "latestVersion": "Not found at the sources"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var lookup = new StubVersionLookup(latestPrerelease: "0.4.3-alpha");
        var check = new OutdatedPackagesCheck(lookup);

        var findings = await check.ParseAndBuildFindingsAsync(json, "C:/repo", CancellationToken.None);

        Assert.Empty(findings);
        Assert.True(lookup.WasCalledWithPrerelease,
            "When resolved version is a prerelease, the lookup must be called with includePrerelease: true.");
    }

    /// <summary>
    /// When the installed prerelease version is older than the latest prerelease on nuget.org,
    /// the check should report drift using the prerelease-aware latest — NOT the raw
    /// "Not found at the sources" sentinel.
    /// </summary>
    [Fact]
    public async Task Prerelease_installed_with_newer_prerelease_available_reports_drift()
    {
        const string json = """
        {
          "projects": [
            {
              "path": "C:/repo/example.csproj",
              "frameworks": [
                {
                  "framework": "net9.0",
                  "topLevelPackages": [
                    {
                      "id": "XperienceCommunity.Sentinel.Module",
                      "requestedVersion": "0.4.3-alpha",
                      "resolvedVersion": "0.4.3-alpha",
                      "latestVersion": "Not found at the sources"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var lookup = new StubVersionLookup(latestPrerelease: "0.5.0-alpha");
        var check = new OutdatedPackagesCheck(lookup);

        var findings = await check.ParseAndBuildFindingsAsync(json, "C:/repo", CancellationToken.None);

        Assert.Single(findings);
        Assert.Contains("0.5.0-alpha", findings[0].Message);
        Assert.DoesNotContain("Not found at the sources", findings[0].Message);
    }

    /// <summary>
    /// Guards against regression: when the installed version is stable, the prerelease fallback
    /// must NOT be triggered. Stable consumers should see the existing behavior (the raw latest
    /// from dotnet list, including the "Not found" sentinel when applicable — that scenario is
    /// a genuine "package gone" case for stable-installed packages).
    /// </summary>
    [Fact]
    public async Task Stable_installed_does_not_invoke_prerelease_fallback()
    {
        const string json = """
        {
          "projects": [
            {
              "path": "C:/repo/example.csproj",
              "frameworks": [
                {
                  "framework": "net9.0",
                  "topLevelPackages": [
                    {
                      "id": "Some.Stable.Package",
                      "requestedVersion": "1.0.0",
                      "resolvedVersion": "1.0.0",
                      "latestVersion": "2.0.0"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var lookup = new StubVersionLookup(latestPrerelease: "3.0.0-preview");
        var check = new OutdatedPackagesCheck(lookup);

        var findings = await check.ParseAndBuildFindingsAsync(json, "C:/repo", CancellationToken.None);

        Assert.False(lookup.WasCalledWithPrerelease,
            "Prerelease fallback must only fire when the installed version itself is a prerelease.");
        Assert.Single(findings);
        Assert.Contains("1.0.0 → 2.0.0", findings[0].Message);
    }

    /// <summary>
    /// When the `dotnet list` JSON declares multiple NuGet sources, the prerelease fallback must
    /// query them (not just nuget.org) so that private-feed package IDs aren't leaked to nuget.org
    /// and private-feed "latest" versions aren't missed.
    /// </summary>
    [Fact]
    public async Task Prerelease_fallback_forwards_declared_sources_to_lookup()
    {
        const string json = """
        {
          "version": 1,
          "parameters": "--outdated",
          "sources": [
            "https://api.nuget.org/v3/index.json",
            "https://private-feed.example.com/v3/index.json"
          ],
          "projects": [
            {
              "path": "C:/repo/example.csproj",
              "frameworks": [
                {
                  "framework": "net9.0",
                  "topLevelPackages": [
                    {
                      "id": "XperienceCommunity.Sentinel.Module",
                      "requestedVersion": "0.4.3-alpha",
                      "resolvedVersion": "0.4.3-alpha",
                      "latestVersion": "Not found at the sources"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var lookup = new StubVersionLookup(latestPrerelease: "0.4.3-alpha");
        var check = new OutdatedPackagesCheck(lookup);

        await check.ParseAndBuildFindingsAsync(json, "C:/repo", CancellationToken.None);

        Assert.NotNull(lookup.LastSources);
        Assert.Equal(2, lookup.LastSources!.Count);
        Assert.Contains("https://api.nuget.org/v3/index.json", lookup.LastSources);
        Assert.Contains("https://private-feed.example.com/v3/index.json", lookup.LastSources);
    }

    /// <summary>
    /// Embedded mode (scheduled task running inside a deployed XbyK site) must skip DEP001 entirely.
    /// A deployed site has no .csproj next to its DLLs, so `dotnet list package` would produce
    /// nothing useful and the check would emit a misleading INFO about a missing project. The CLI
    /// path is unchanged.
    /// </summary>
    [Fact]
    public async Task Embedded_mode_skips_check_entirely()
    {
        using var repo = new TempRepo(); // no .csproj

        var embeddedCtx = new ScanContext
        {
            RepoPath = repo.Path,
            HttpClientFactory = new FakeHttpClientFactory(),
            IsEmbeddedHost = true,
        };

        var findings = await new OutdatedPackagesCheck().RunAsync(embeddedCtx, CancellationToken.None);

        Assert.Empty(findings);
    }

    private sealed class StubVersionLookup : INuGetVersionLookup
    {
        private readonly string? _latestPrerelease;

        public StubVersionLookup(string? latestPrerelease)
        {
            _latestPrerelease = latestPrerelease;
        }

        public bool WasCalledWithPrerelease { get; private set; }

        public IReadOnlyList<string>? LastSources { get; private set; }

        public Task<string?> GetLatestVersionAsync(string packageId, bool includePrerelease, CancellationToken cancellationToken)
            => GetLatestVersionAsync(packageId, includePrerelease, sources: null, cancellationToken);

        public Task<string?> GetLatestVersionAsync(
            string packageId,
            bool includePrerelease,
            IReadOnlyList<string>? sources,
            CancellationToken cancellationToken)
        {
            LastSources = sources;
            if (includePrerelease)
            {
                WasCalledWithPrerelease = true;
                return Task.FromResult<string?>(_latestPrerelease);
            }
            return Task.FromResult<string?>(null);
        }
    }
}
