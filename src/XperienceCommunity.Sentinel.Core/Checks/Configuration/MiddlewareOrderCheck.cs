using System.Text.RegularExpressions;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Configuration;

/// <summary>
/// CFG002 — The Kentico trio (InitKentico → UseStaticFiles → UseKentico) must have no middleware between them,
/// and UseWebOptimizer must be after UseKentico. Page Builder preview URL rewriting depends on this order.
/// </summary>
public sealed partial class MiddlewareOrderCheck : ICheck
{
    public string RuleId => "CFG002";
    public string Title => "Kentico middleware pipeline order";
    public string Category => "Configuration";
    public CheckKind Kind => CheckKind.Static;

    public Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        // Embedded-mode no-op: a deployed XbyK site ships compiled DLLs only — Program.cs is not
        // present at runtime on the app server. Emitting a "No Program.cs found" INFO finding here
        // is technically accurate but reads as alarming to non-technical operators browsing the
        // admin dashboard, and the middleware order was already verified at build time. The CLI path
        // (IsEmbeddedHost == false) keeps running the full check against a source repo.
        if (context.IsEmbeddedHost)
        {
            return Task.FromResult<IReadOnlyList<Finding>>(Array.Empty<Finding>());
        }

        var findings = new List<Finding>();
        var programCs = Path.Combine(context.RepoPath, "Program.cs");
        if (!File.Exists(programCs))
        {
            findings.Add(new Finding(
                RuleId, Title, Category,
                Severity.Info,
                "No Program.cs found at the repo root — middleware order could not be verified.",
                Location: programCs));
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        var lines = File.ReadAllLines(programCs);
        int initLine = FindCall(lines, "InitKentico");
        int staticLine = FindCall(lines, "UseStaticFiles");
        int kenticoLine = FindCall(lines, "UseKentico");
        int webOptLine = FindCall(lines, "UseWebOptimizer");

        if (initLine == -1 || staticLine == -1 || kenticoLine == -1)
        {
            findings.Add(new Finding(
                RuleId, Title, Category,
                Severity.Warning,
                $"Could not locate the full Kentico trio in Program.cs (InitKentico={initLine != -1}, UseStaticFiles={staticLine != -1}, UseKentico={kenticoLine != -1}).",
                Location: programCs,
                Remediation: "Ensure Program.cs calls InitKentico(), UseStaticFiles(), and UseKentico() in that order with nothing between them."));
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        // Order check: init < static < kentico
        if (!(initLine < staticLine && staticLine < kenticoLine))
        {
            findings.Add(new Finding(
                RuleId, Title, Category,
                Severity.Error,
                $"Kentico trio is out of order in Program.cs (InitKentico line {initLine + 1}, UseStaticFiles line {staticLine + 1}, UseKentico line {kenticoLine + 1}). Required order: InitKentico → UseStaticFiles → UseKentico.",
                Location: programCs,
                Remediation: "Reorder the three calls so they appear as InitKentico() → UseStaticFiles() → UseKentico() with nothing between them."));
        }

        // Adjacency check: nothing between the three (only whitespace/comments tolerated)
        if (HasBlockersBetween(lines, initLine, staticLine) || HasBlockersBetween(lines, staticLine, kenticoLine))
        {
            findings.Add(new Finding(
                RuleId, Title, Category,
                Severity.Error,
                "Additional middleware calls appear between InitKentico, UseStaticFiles, and UseKentico. Page Builder preview URL rewriting requires the trio to be contiguous.",
                Location: programCs,
                Remediation: "Move any middleware registered between the three Kentico calls either before InitKentico or after UseKentico."));
        }

        // UseWebOptimizer must be after UseKentico (if present at all)
        if (webOptLine != -1 && webOptLine < kenticoLine)
        {
            findings.Add(new Finding(
                RuleId, Title, Category,
                Severity.Error,
                $"UseWebOptimizer is called on line {webOptLine + 1}, before UseKentico on line {kenticoLine + 1}. Page Builder preview bundles will have empty MIME types.",
                Location: programCs,
                Remediation: "Move UseWebOptimizer() to run after UseKentico()."));
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static int FindCall(string[] lines, string methodName)
    {
        var pattern = new Regex($@"\b{Regex.Escape(methodName)}\s*\(", RegexOptions.Compiled);
        for (int i = 0; i < lines.Length; i++)
        {
            if (pattern.IsMatch(lines[i]))
            {
                return i;
            }
        }
        return -1;
    }

    private static bool HasBlockersBetween(string[] lines, int fromExclusive, int toExclusive)
    {
        for (int i = fromExclusive + 1; i < toExclusive; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("//")) continue;
            if (trimmed.StartsWith("/*") || trimmed.StartsWith("*") || trimmed.EndsWith("*/")) continue;
            // Treat "var builder.Services..." lines as non-middleware (unlikely here but safe)
            if (AppUseCallRegex().IsMatch(trimmed))
            {
                return true;
            }
        }
        return false;
    }

    [GeneratedRegex(@"\b(app|application)\s*\.\s*(Use|Map)\w*\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex AppUseCallRegex();
}
