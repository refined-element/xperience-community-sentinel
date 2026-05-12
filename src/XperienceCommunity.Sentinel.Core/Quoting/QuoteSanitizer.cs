using XperienceCommunity.Sentinel.Reporting;

namespace XperienceCommunity.Sentinel.Quoting;

/// <summary>
/// Converts a <see cref="ReportDocument"/> into a <see cref="QuoteSubmission"/> suitable for sending
/// to Refined Element. By default it drops finding <c>Location</c> and <c>Remediation</c> — those are
/// the fields most likely to leak file paths, connection-string paths, or configuration values.
/// The <c>includeContext</c> opt-in preserves them for users who want a more accurate quote.
/// </summary>
public static class QuoteSanitizer
{
    public static QuoteSubmission Sanitize(ReportDocument report, string contactEmail, bool includeContext)
    {
        // Drop findings the producing check marked as informational-only — they show up in the
        // report (useful context) but the submitter isn't asking Refined Element to quote on them.
        var eligible = report.Findings.Where(f => f.QuoteEligible).ToArray();

        var findings = eligible
            .Select(f => new QuoteFinding(
                RuleId: f.RuleId,
                RuleTitle: f.RuleTitle,
                Category: f.Category,
                Severity: f.Severity,
                Message: f.Message,
                Location: includeContext ? f.Location : null,
                Remediation: includeContext ? f.Remediation : null))
            .ToArray();

        var summary = new ReportSummary(
            Total: eligible.Length,
            Errors: eligible.Count(f => f.Severity == "Error"),
            Warnings: eligible.Count(f => f.Severity == "Warning"),
            Info: eligible.Count(f => f.Severity == "Info"));

        return new QuoteSubmission(
            ContactEmail: contactEmail,
            SentinelVersion: report.SentinelVersion,
            Scan: report.Scan with { RepoPath = includeContext ? report.Scan.RepoPath : "(redacted)" },
            Summary: summary,
            Findings: findings,
            IncludesContext: includeContext);
    }
}
