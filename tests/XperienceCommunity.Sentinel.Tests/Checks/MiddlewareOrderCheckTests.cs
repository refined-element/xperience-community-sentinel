using XperienceCommunity.Sentinel.Tests.Support;
using XperienceCommunity.Sentinel.Checks.Configuration;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Tests.Checks;

public class MiddlewareOrderCheckTests
{
    private static ScanContext Ctx(string repoPath) => new()
    {
        RepoPath = repoPath,
        HttpClientFactory = new FakeHttpClientFactory(),
    };

    [Fact]
    public async Task Correct_trio_order_produces_no_findings()
    {
        using var repo = new TempRepo();
        repo.Write("Program.cs", """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddKentico();
            var app = builder.Build();
            app.InitKentico();
            app.UseStaticFiles();
            app.UseKentico();
            app.UseWebOptimizer();
            app.Run();
            """);

        var findings = await new MiddlewareOrderCheck().RunAsync(Ctx(repo.Path), CancellationToken.None);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task UseWebOptimizer_before_UseKentico_is_error()
    {
        using var repo = new TempRepo();
        repo.Write("Program.cs", """
            var app = builder.Build();
            app.InitKentico();
            app.UseWebOptimizer();
            app.UseStaticFiles();
            app.UseKentico();
            """);

        var findings = await new MiddlewareOrderCheck().RunAsync(Ctx(repo.Path), CancellationToken.None);

        Assert.Contains(findings, f => f.Severity == Severity.Error && f.Message.Contains("UseWebOptimizer"));
    }

    [Fact]
    public async Task Middleware_between_trio_is_error()
    {
        using var repo = new TempRepo();
        repo.Write("Program.cs", """
            var app = builder.Build();
            app.InitKentico();
            app.UseAuthentication();
            app.UseStaticFiles();
            app.UseKentico();
            """);

        var findings = await new MiddlewareOrderCheck().RunAsync(Ctx(repo.Path), CancellationToken.None);

        Assert.Contains(findings, f => f.Severity == Severity.Error && f.Message.Contains("contiguous"));
    }

    /// <summary>
    /// Embedded mode (scheduled task running inside a deployed XbyK site) must skip CFG002 entirely.
    /// A deployed site has only compiled DLLs — no Program.cs — and the check's "No Program.cs found"
    /// INFO reads as alarming to operators browsing the admin dashboard. The CLI path is unchanged.
    /// </summary>
    [Fact]
    public async Task Embedded_mode_skips_check_entirely_even_when_program_cs_missing()
    {
        using var repo = new TempRepo(); // deliberately no Program.cs written

        var embeddedCtx = new ScanContext
        {
            RepoPath = repo.Path,
            HttpClientFactory = new FakeHttpClientFactory(),
            IsEmbeddedHost = true,
        };

        var findings = await new MiddlewareOrderCheck().RunAsync(embeddedCtx, CancellationToken.None);

        Assert.Empty(findings);
    }
}
