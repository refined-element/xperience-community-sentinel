namespace XperienceCommunity.Sentinel.Core;

/// <summary>
/// Every check implements this interface. Adding a new check means dropping one class into Checks/
/// and registering it in <see cref="CheckRegistry"/>.
/// </summary>
public interface ICheck
{
    /// <summary>Stable rule identifier (e.g. "CFG001"). Never reused, never renumbered.</summary>
    string RuleId { get; }

    /// <summary>Short title shown in reports.</summary>
    string Title { get; }

    /// <summary>Category for grouping in the HTML report.</summary>
    string Category { get; }

    /// <summary>Whether the check runs against the source tree only (Static) or requires a DB connection (Runtime).</summary>
    CheckKind Kind { get; }

    /// <summary>
    /// True when findings from this check should be included in a `sentinel quote` submission.
    /// Informational-only checks (e.g. "your Stripe.net is one minor behind") set this to false:
    /// the finding still surfaces in the report, but it's not billed work for Refined Element to quote on.
    /// </summary>
    bool QuoteEligible => true;

    /// <summary>
    /// Execute the check. Must not throw for expected failure modes — encode them as findings.
    /// Unexpected exceptions are caught by the runner and reported as a CheckFailed finding.
    /// </summary>
    Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken);
}
