using System.Reflection;
using XperienceCommunity.Sentinel.Commands;
using Spectre.Console.Cli;

// Read the version from the assembly so `sentinel --version` always matches the published
// NuGet version. CI sets this via `-p:Version=...` during pack; local Debug builds fall back
// to the csproj default.
var informationalVersion = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? "unknown";
// Strip SourceLink commit suffix ("0.1.2-alpha+abc123") for display.
var cliVersion = informationalVersion.Split('+', 2)[0];

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("sentinel");
    config.SetApplicationVersion(cliVersion);

    config.AddCommand<ScanCommand>("scan")
        .WithDescription("Scan an Xperience by Kentico project (XbyK 29+) for health issues. Does not support Kentico Xperience 13.")
        .WithExample(["scan", "--path", "./MySite"])
        .WithExample(["scan", "--path", ".", "--connection-string", "Server=...;Database=..."])
        .WithExample(["scan", "--repo", "owner/repo"]);

    config.AddCommand<QuoteCommand>("quote")
        .WithDescription("Email a sanitized scan report to Refined Element for a remediation quote.")
        .WithExample(["quote", "--report", "./sentinel-report.json"]);
});

return await app.RunAsync(args);
