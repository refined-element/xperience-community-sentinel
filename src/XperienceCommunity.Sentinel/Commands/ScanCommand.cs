using System.ComponentModel;
using XperienceCommunity.Sentinel.Cloning;
using XperienceCommunity.Sentinel.Core;
using XperienceCommunity.Sentinel.Infrastructure;
using XperienceCommunity.Sentinel.Quoting;
using XperienceCommunity.Sentinel.Reporting;
using Spectre.Console;
using Spectre.Console.Cli;

namespace XperienceCommunity.Sentinel.Commands;

public sealed class ScanCommand : AsyncCommand<ScanCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("Path to the Xperience by Kentico project root (folder containing the .csproj).")]
        [CommandOption("-p|--path")]
        public string Path { get; init; } = ".";

        [Description("Repository to scan instead of --path. Accepts owner/name, github.com/owner/name, or a full URL. Shallow-cloned into a temp dir and removed after the scan.")]
        [CommandOption("--repo")]
        public string? Repo { get; init; }

        [Description("Branch, tag, or ref to check out when using --repo. Defaults to the repo's default branch.")]
        [CommandOption("--ref")]
        public string? Ref { get; init; }

        [Description("Keep the temporary clone directory instead of deleting it after the scan. Useful for debugging.")]
        [CommandOption("--keep-clone")]
        public bool KeepClone { get; init; }

        [Description("SQL Server connection string for runtime content checks. If omitted, only static code checks run.")]
        [CommandOption("-c|--connection-string")]
        public string? ConnectionString { get; init; }

        [Description("Directory to write the HTML and JSON reports. Defaults to ./sentinel-report.")]
        [CommandOption("-o|--output")]
        public string OutputDirectory { get; init; } = "./sentinel-report";

        [Description("Stale content threshold in days. Items not edited in this window are flagged.")]
        [CommandOption("--stale-days")]
        public int StaleDays { get; init; } = 180;

        [Description("EventLog lookback window in days. Errors and warnings from CMS_EventLog are grouped within this period (CNT006).")]
        [CommandOption("--event-log-days")]
        public int EventLogDays { get; init; } = 30;

        [Description("Fail the process with exit code 2 if any finding has severity at or above this threshold. One of: Info, Warning, Error, None. Defaults to None (always exit 0 unless the scan crashes) so adding --fail-on is an explicit opt-in to PR-gate behavior.")]
        [CommandOption("--fail-on")]
        public string FailOn { get; init; } = nameof(FailOnThreshold.None);

        [Description("Override the quote endpoint baked into the HTML report's submit button. Also reads the SENTINEL_QUOTE_ENDPOINT env var. Defaults to Refined Element's production endpoint.")]
        [CommandOption("--quote-endpoint")]
        public string? QuoteEndpoint { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<FailOnThreshold>(settings.FailOn, ignoreCase: true, out var failOn))
        {
            AnsiConsole.MarkupLine($"[red]Invalid --fail-on value '{Markup.Escape(settings.FailOn)}'. Use Info, Warning, Error, or None.[/]");
            return ScanExitCode.Error;
        }

        string repoPath;
        string? cloneToCleanup = null;

        if (!string.IsNullOrWhiteSpace(settings.Repo))
        {
            string url;
            try { url = GitRepoUrl.Normalize(settings.Repo); }
            catch (ArgumentException ex)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
                return ScanExitCode.Error;
            }

            AnsiConsole.MarkupLine($"[dim]Cloning [cyan]{Markup.Escape(url)}[/]{(settings.Ref is null ? "" : $" at [cyan]{Markup.Escape(settings.Ref)}[/]")}…[/]");
            var cloner = new GitCloner();
            var clone = await cloner.CloneAsync(url, settings.Ref, cancellationToken).ConfigureAwait(false);
            if (!clone.Success || clone.ClonePath is null)
            {
                AnsiConsole.MarkupLine($"[red]Clone failed:[/] {Markup.Escape(clone.ErrorMessage ?? "unknown error")}");
                return ScanExitCode.Error;
            }

            repoPath = clone.ClonePath;
            if (!settings.KeepClone) cloneToCleanup = repoPath;
        }
        else
        {
            repoPath = System.IO.Path.GetFullPath(settings.Path);
            if (!Directory.Exists(repoPath))
            {
                AnsiConsole.MarkupLine($"[red]Path not found:[/] {repoPath}");
                return ScanExitCode.Error;
            }
        }

        using var httpFactory = new SingleHttpClientFactory();
        var scanContext = new ScanContext
        {
            RepoPath = repoPath,
            ConnectionString = settings.ConnectionString,
            StaleDays = settings.StaleDays,
            EventLogDays = settings.EventLogDays,
            HttpClientFactory = httpFactory,
        };

        var runner = new CheckRunner(CheckRegistry.BuiltIn());

        ScanResult result = null!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Scanning...", async ctx =>
            {
                var progress = new Progress<CheckProgress>(p =>
                {
                    ctx.Status($"[grey]({p.Index}/{p.Total})[/] {p.Check.RuleId} — {p.Check.Title}");
                });
                result = await runner.RunAsync(scanContext, progress, cancellationToken).ConfigureAwait(false);
            })
            .ConfigureAwait(false);

        PrintSummary(result);

        var reportDoc = ReportBuilder.Build(result);
        var outputDir = System.IO.Path.GetFullPath(settings.OutputDirectory);
        var jsonPath = System.IO.Path.Combine(outputDir, "report.json");
        var htmlPath = System.IO.Path.Combine(outputDir, "report.html");

        var quoteEndpoint = QuoteClient.ResolveEndpoint(settings.QuoteEndpoint);
        await JsonReportWriter.WriteAsync(reportDoc, jsonPath, cancellationToken).ConfigureAwait(false);
        await HtmlReportWriter.WriteAsync(reportDoc, htmlPath, quoteEndpoint, cancellationToken).ConfigureAwait(false);

        AnsiConsole.MarkupLine($"\n[green]✓[/] Reports written to [cyan]{outputDir}[/]");
        AnsiConsole.MarkupLine($"  [dim]{jsonPath}[/]");
        AnsiConsole.MarkupLine($"  [dim]{htmlPath}[/]");

        if (cloneToCleanup is not null)
        {
            GitCloner.SafeDelete(cloneToCleanup);
        }
        else if (settings.KeepClone && !string.IsNullOrWhiteSpace(settings.Repo))
        {
            AnsiConsole.MarkupLine($"[dim]Clone kept at {Markup.Escape(repoPath)}[/]");
        }

        var exitCode = ScanExitCode.Evaluate(result.Findings, failOn);
        if (exitCode == ScanExitCode.ThresholdTripped)
        {
            // Tell CI exactly why the build failed. Counting is cheap and the message is
            // the first thing a sleepy on-caller reads in a failed pipeline log.
            var count = ScanExitCode.CountAtOrAboveThreshold(result.Findings, failOn);
            var label = failOn == FailOnThreshold.Info
                ? "finding(s)"
                : $"{failOn}-or-higher finding(s)";
            AnsiConsole.MarkupLine(
                $"\n[red]Threshold '{failOn}' tripped — {count} {label} detected. Exiting with code {ScanExitCode.ThresholdTripped}.[/]");
        }

        return exitCode;
    }

    private static void PrintSummary(ScanResult result)
    {
        AnsiConsole.WriteLine();
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Metric").AddColumn("Value");
        table.AddRow("Repo", result.RepoPath);
        table.AddRow("Runtime checks", result.RuntimeEnabled ? "[green]enabled[/]" : "[yellow]skipped (no connection string)[/]");
        table.AddRow("Duration", $"{result.Duration.TotalSeconds:N2}s");
        table.AddRow("Checks executed", result.Executions.Count(e => e.Status == CheckExecutionStatus.Ran).ToString());
        table.AddRow("Checks skipped (runtime)", result.Executions.Count(e => e.Status == CheckExecutionStatus.SkippedRuntime).ToString());
        table.AddRow("Checks failed", result.Executions.Count(e => e.Status == CheckExecutionStatus.Failed).ToString());
        table.AddRow("[red]Errors[/]", result.ErrorCount.ToString());
        table.AddRow("[yellow]Warnings[/]", result.WarningCount.ToString());
        table.AddRow("[grey]Info[/]", result.InfoCount.ToString());
        AnsiConsole.Write(table);

        if (result.Findings.Count == 0)
        {
            AnsiConsole.MarkupLine("\n[green]No issues found.[/]");
            return;
        }

        var findings = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn("Severity")
            .AddColumn("Rule")
            .AddColumn("Finding");

        foreach (var f in result.Findings.OrderByDescending(x => x.Severity).ThenBy(x => x.RuleId))
        {
            var color = f.Severity switch
            {
                Severity.Error => "red",
                Severity.Warning => "yellow",
                _ => "grey",
            };
            findings.AddRow(
                $"[{color}]{f.Severity.ToString().ToUpperInvariant()}[/]",
                f.RuleId,
                Markup.Escape(f.Message));
        }
        AnsiConsole.Write(findings);

        AnsiConsole.MarkupLine("\n[dim]Fix these automatically — run `sentinel quote` to request a remediation estimate from Refined Element.[/]");
    }
}
