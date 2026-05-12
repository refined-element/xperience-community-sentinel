using System.ComponentModel;
using System.Text.Json;
using XperienceCommunity.Sentinel.Infrastructure;
using XperienceCommunity.Sentinel.Quoting;
using XperienceCommunity.Sentinel.Reporting;
using Spectre.Console;
using Spectre.Console.Cli;

namespace XperienceCommunity.Sentinel.Commands;

public sealed class QuoteCommand : AsyncCommand<QuoteCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("Path to the JSON report produced by `sentinel scan`.")]
        [CommandOption("-r|--report")]
        public string ReportPath { get; init; } = "./sentinel-report/report.json";

        [Description("Contact email to use for the quote reply. Prompts interactively if omitted.")]
        [CommandOption("-e|--email")]
        public string? Email { get; init; }

        [Description("Include finding context (file paths, config snippets, repo path) in the submission. Off by default — send sanitized counts only.")]
        [CommandOption("--include-context")]
        public bool IncludeContext { get; init; }

        [Description("Skip the confirmation prompt. Use in CI.")]
        [CommandOption("-y|--yes")]
        public bool SkipConfirmation { get; init; }

        [Description("Override the quote endpoint. Also reads the SENTINEL_QUOTE_ENDPOINT env var.")]
        [CommandOption("--endpoint")]
        public string? Endpoint { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(settings.ReportPath))
        {
            AnsiConsole.MarkupLine($"[red]Report not found:[/] {settings.ReportPath}");
            AnsiConsole.MarkupLine("[dim]Run `sentinel scan` first.[/]");
            return 2;
        }

        ReportDocument? report;
        try
        {
            await using var stream = File.OpenRead(settings.ReportPath);
            report = await JsonSerializer.DeserializeAsync<ReportDocument>(stream, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            AnsiConsole.MarkupLine($"[red]Could not parse report:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }

        if (report is null)
        {
            AnsiConsole.MarkupLine("[red]Report deserialized to null.[/]");
            return 2;
        }

        var email = settings.Email ?? AnsiConsole.Ask<string>("[cyan]Contact email:[/]");

        var submission = QuoteSanitizer.Sanitize(report, email, settings.IncludeContext);
        var endpoint = QuoteClient.ResolveEndpoint(settings.Endpoint);

        PreviewSubmission(submission, endpoint);

        if (!settings.SkipConfirmation && !AnsiConsole.Confirm("Send this submission to Refined Element?"))
        {
            AnsiConsole.MarkupLine("[yellow]Aborted.[/]");
            return 1;
        }

        using var httpFactory = new SingleHttpClientFactory();
        var client = new QuoteClient(httpFactory.CreateClient(""));
        var result = await client.SubmitAsync(endpoint, submission, cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            AnsiConsole.MarkupLine("[green]✓ Submission received.[/] Refined Element will reply with an itemized quote.");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Submission failed:[/] {Markup.Escape(result.ErrorMessage ?? "unknown error")}");
        if (!string.IsNullOrEmpty(result.ResponseBody))
        {
            AnsiConsole.MarkupLine($"[dim]Response body: {Markup.Escape(result.ResponseBody)}[/]");
        }
        return 1;
    }

    private static void PreviewSubmission(QuoteSubmission submission, string endpoint)
    {
        AnsiConsole.WriteLine();
        var panel = new Panel(new Rows(
            new Markup($"[bold]Endpoint:[/] {Markup.Escape(endpoint)}"),
            new Markup($"[bold]Contact email:[/] {Markup.Escape(submission.ContactEmail)}"),
            new Markup($"[bold]Repo path:[/] {Markup.Escape(submission.Scan.RepoPath)}"),
            new Markup($"[bold]Includes context:[/] {(submission.IncludesContext ? "[yellow]yes[/] — location + remediation text will be sent" : "[green]no[/] — sanitized counts only")}"),
            new Markup($"[bold]Findings:[/] {submission.Findings.Count} total ([red]{submission.Summary.Errors}[/] errors, [yellow]{submission.Summary.Warnings}[/] warnings, [grey]{submission.Summary.Info}[/] info)")))
        {
            Header = new PanelHeader(" Quote submission preview ", Justify.Left),
            Border = BoxBorder.Rounded,
        };
        AnsiConsole.Write(panel);
    }
}
