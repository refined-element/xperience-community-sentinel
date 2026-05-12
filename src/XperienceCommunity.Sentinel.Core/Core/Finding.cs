namespace XperienceCommunity.Sentinel.Core;

/// <summary>
/// A single issue discovered by a check. Immutable.
/// </summary>
/// <param name="RuleId">Stable identifier (e.g. "CFG001"). Used by report consumers and the quote engine.</param>
/// <param name="RuleTitle">Short human-readable title shown next to the rule ID.</param>
/// <param name="Category">Grouping for the HTML report (e.g. "Configuration", "Content Model", "Dependencies").</param>
/// <param name="Severity">Info, Warning, Error.</param>
/// <param name="Message">Specific, actionable description of this finding instance.</param>
/// <param name="Location">Optional location context (file path + line, or content item GUID). May be redacted on submission.</param>
/// <param name="Remediation">Optional guidance on how to fix the finding.</param>
public sealed record Finding(
    string RuleId,
    string RuleTitle,
    string Category,
    Severity Severity,
    string Message,
    string? Location = null,
    string? Remediation = null,
    bool QuoteEligible = true);
