using XperienceCommunity.Sentinel.Tests.Support;
using XperienceCommunity.Sentinel.Checks.Configuration;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Tests.Checks;

public class PlaintextSecretsCheckTests
{
    private static ScanContext Ctx(string repoPath) => new()
    {
        RepoPath = repoPath,
        HttpClientFactory = new FakeHttpClientFactory(),
    };

    [Fact]
    public async Task KeyVault_reference_is_not_flagged()
    {
        using var repo = new TempRepo();
        repo.Write("appsettings.json", """
            { "Stripe": { "ApiKey": "@Microsoft.KeyVault(VaultName=x;SecretName=y)" } }
            """);

        var findings = await new PlaintextSecretsCheck().RunAsync(Ctx(repo.Path), CancellationToken.None);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Hardcoded_password_is_flagged()
    {
        using var repo = new TempRepo();
        repo.Write("appsettings.json", """
            { "Smtp": { "Password": "not-a-placeholder-real-secret" } }
            """);

        var findings = await new PlaintextSecretsCheck().RunAsync(Ctx(repo.Path), CancellationToken.None);

        Assert.Single(findings, f => f.Severity == Severity.Warning && f.Message.Contains("Smtp.Password"));
    }

    [Fact]
    public async Task Underscore_prefixed_keys_are_skipped()
    {
        using var repo = new TempRepo();
        repo.Write("appsettings.json", """
            { "_comment_apikey": "This is a human comment about ApiKey configuration" }
            """);

        var findings = await new PlaintextSecretsCheck().RunAsync(Ctx(repo.Path), CancellationToken.None);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Connection_string_with_embedded_password_is_flagged()
    {
        using var repo = new TempRepo();
        repo.Write("appsettings.json", """
            { "ConnectionStrings": { "Default": "Server=.;Database=x;User ID=sa;Password=hunter2" } }
            """);

        var findings = await new PlaintextSecretsCheck().RunAsync(Ctx(repo.Path), CancellationToken.None);

        Assert.Single(findings, f => f.Severity == Severity.Warning);
    }
}
