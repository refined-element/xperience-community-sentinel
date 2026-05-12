using XperienceCommunity.Sentinel.Tests.Support;
using XperienceCommunity.Sentinel.Checks.Dependencies;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Tests.Checks;

public class KenticoVersionCheckTests
{
    /// <summary>
    /// Embedded mode (scheduled task running inside a deployed XbyK site) must skip VER001 entirely.
    /// A deployed site has no .csproj next to its DLLs, so the PackageReference scan would always come
    /// up empty and the check would emit a misleading "No Kentico.Xperience.* PackageReference found"
    /// INFO. The CLI path is unchanged.
    /// </summary>
    [Fact]
    public async Task Embedded_mode_skips_check_entirely_even_when_csproj_missing()
    {
        using var repo = new TempRepo(); // deliberately no .csproj written

        var embeddedCtx = new ScanContext
        {
            RepoPath = repo.Path,
            HttpClientFactory = new FakeHttpClientFactory(),
            IsEmbeddedHost = true,
        };

        var findings = await new KenticoVersionCheck().RunAsync(embeddedCtx, CancellationToken.None);

        Assert.Empty(findings);
    }

    /// <summary>
    /// CLI mode (IsEmbeddedHost == false) against a repo with no .csproj should still surface the
    /// informational "no PackageReference found" finding — that signal is useful for devs scanning
    /// a folder that isn't actually an XbyK project.
    /// </summary>
    [Fact]
    public async Task Cli_mode_emits_info_when_csproj_missing()
    {
        using var repo = new TempRepo(); // no .csproj

        var cliCtx = new ScanContext
        {
            RepoPath = repo.Path,
            HttpClientFactory = new FakeHttpClientFactory(),
            // IsEmbeddedHost defaults to false
        };

        var findings = await new KenticoVersionCheck().RunAsync(cliCtx, CancellationToken.None);

        Assert.Single(findings);
        Assert.Equal(Severity.Info, findings[0].Severity);
        Assert.Contains("No Kentico.Xperience.*", findings[0].Message);
    }
}
