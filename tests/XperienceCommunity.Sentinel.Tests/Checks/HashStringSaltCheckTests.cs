using XperienceCommunity.Sentinel.Tests.Support;
using XperienceCommunity.Sentinel.Checks.Configuration;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Tests.Checks;

public class HashStringSaltCheckTests
{
    private static ScanContext Ctx(string repoPath) => new()
    {
        RepoPath = repoPath,
        HttpClientFactory = new FakeHttpClientFactory(),
    };

    [Fact]
    public async Task Missing_appsettings_is_info_not_error()
    {
        using var repo = new TempRepo();
        var findings = await new HashStringSaltCheck().RunAsync(Ctx(repo.Path), CancellationToken.None);
        Assert.Single(findings, f => f.Severity == Severity.Info);
    }

    [Fact]
    public async Task Missing_key_in_appsettings_is_error()
    {
        using var repo = new TempRepo();
        repo.Write("appsettings.json", """{ "Logging": {} }""");

        var findings = await new HashStringSaltCheck().RunAsync(Ctx(repo.Path), CancellationToken.None);

        Assert.Single(findings, f => f.Severity == Severity.Error);
    }

    [Fact]
    public async Task Empty_value_is_good()
    {
        using var repo = new TempRepo();
        repo.Write("appsettings.json", """{ "CMSHashStringSalt": "" }""");

        var findings = await new HashStringSaltCheck().RunAsync(Ctx(repo.Path), CancellationToken.None);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Hardcoded_value_is_warning()
    {
        using var repo = new TempRepo();
        repo.Write("appsettings.json", """{ "CMSHashStringSalt": "00000000-1111-2222-3333-444444444444" }""");

        var findings = await new HashStringSaltCheck().RunAsync(Ctx(repo.Path), CancellationToken.None);

        Assert.Single(findings, f => f.Severity == Severity.Warning);
    }
}
