using XperienceCommunity.Sentinel.Reporting;

namespace XperienceCommunity.Sentinel.Quoting;

/// <summary>
/// Payload POSTed to the Refined Element quote endpoint. Always derived from a <see cref="ReportDocument"/>
/// via <see cref="QuoteSanitizer"/> so sensitive fields are stripped by default.
/// </summary>
/// <remarks>
/// The positional parameters stay backward-compatible for CLI callers that have always passed
/// the original six fields. Name / Company / Message are init-only additions so admin-UI
/// callers can enrich the submission without breaking the older positional contract.
/// </remarks>
public sealed record QuoteSubmission(
    string ContactEmail,
    string SentinelVersion,
    ReportScan Scan,
    ReportSummary Summary,
    IReadOnlyList<QuoteFinding> Findings,
    bool IncludesContext)
{
    /// <summary>Optional contact name from the admin UI form. Ignored by older consumers.</summary>
    public string? ContactName { get; init; }

    /// <summary>Optional company name from the admin UI form.</summary>
    public string? Company { get; init; }

    /// <summary>Free-text context the operator provides — deadlines, rule emphasis, constraints.</summary>
    public string? Message { get; init; }
}

public sealed record QuoteFinding(
    string RuleId,
    string RuleTitle,
    string Category,
    string Severity,
    string Message,
    string? Location,
    string? Remediation);
