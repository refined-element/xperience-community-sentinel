using XperienceCommunity.Sentinel.Quoting;
using XperienceCommunity.Sentinel.Reporting;

namespace XperienceCommunity.Sentinel.Tests.Quoting;

/// <summary>
/// The sanitizer is the privacy boundary between a developer's machine and Refined Element's inbox.
/// These tests lock down the boundary so a future refactor cannot quietly widen what we send.
/// </summary>
public class QuoteSanitizerTests
{
    private static ReportDocument SampleReport() => new(
        SentinelVersion: "0.1.0-alpha",
        Scan: new ReportScan(
            StartedAt: DateTimeOffset.UtcNow.AddSeconds(-2),
            CompletedAt: DateTimeOffset.UtcNow,
            DurationSeconds: 2.0,
            RepoPath: @"C:\secrets\my-site",
            RuntimeEnabled: true),
        Summary: new ReportSummary(Total: 1, Errors: 0, Warnings: 1, Info: 0),
        Executions: [],
        Findings: [
            new ReportFinding(
                RuleId: "CFG003",
                RuleTitle: "Plaintext secrets",
                Category: "Configuration",
                Severity: "Warning",
                Message: "'Smtp.Password' contains a plaintext secret.",
                Location: @"C:\secrets\my-site\appsettings.json",
                Remediation: "Move to user secrets or Key Vault.",
                QuoteEligible: true),
        ]);

    [Fact]
    public void Default_mode_drops_location_and_remediation()
    {
        var submission = QuoteSanitizer.Sanitize(SampleReport(), contactEmail: "user@example.com", includeContext: false);

        var finding = Assert.Single(submission.Findings);
        Assert.Null(finding.Location);
        Assert.Null(finding.Remediation);
    }

    [Fact]
    public void Default_mode_redacts_repo_path()
    {
        var submission = QuoteSanitizer.Sanitize(SampleReport(), contactEmail: "user@example.com", includeContext: false);

        Assert.Equal("(redacted)", submission.Scan.RepoPath);
        Assert.False(submission.IncludesContext);
    }

    [Fact]
    public void IncludeContext_preserves_location_remediation_and_repo_path()
    {
        var submission = QuoteSanitizer.Sanitize(SampleReport(), contactEmail: "user@example.com", includeContext: true);

        var finding = Assert.Single(submission.Findings);
        Assert.Equal(@"C:\secrets\my-site\appsettings.json", finding.Location);
        Assert.Equal("Move to user secrets or Key Vault.", finding.Remediation);
        Assert.Equal(@"C:\secrets\my-site", submission.Scan.RepoPath);
        Assert.True(submission.IncludesContext);
    }

    [Fact]
    public void Message_text_is_always_preserved()
    {
        var sanitized = QuoteSanitizer.Sanitize(SampleReport(), contactEmail: "a@b.co", includeContext: false);
        var withContext = QuoteSanitizer.Sanitize(SampleReport(), contactEmail: "a@b.co", includeContext: true);

        var expected = "'Smtp.Password' contains a plaintext secret.";
        Assert.Equal(expected, sanitized.Findings.Single().Message);
        Assert.Equal(expected, withContext.Findings.Single().Message);
    }

    [Fact]
    public void Quote_ineligible_findings_are_excluded()
    {
        var report = SampleReport() with
        {
            Findings =
            [
                new ReportFinding("DEP001", "Outdated NuGet packages", "Dependencies", "Warning",
                    "Stripe.net: 50.1.0 → 51.0.0", Location: null, Remediation: null, QuoteEligible: false),
                new ReportFinding("VER001", "Xperience by Kentico version", "Dependencies", "Warning",
                    "Kentico.Xperience.WebApp is on 31.0.1; latest is 31.4.0", Location: null, Remediation: null, QuoteEligible: true),
            ],
        };

        var submission = QuoteSanitizer.Sanitize(report, contactEmail: "a@b.co", includeContext: false);

        var finding = Assert.Single(submission.Findings);
        Assert.Equal("VER001", finding.RuleId);
        Assert.Equal(1, submission.Summary.Total);
        Assert.Equal(1, submission.Summary.Warnings);
    }
}
