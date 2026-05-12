using XperienceCommunity.Sentinel.Checks.Content;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Tests.Core;

/// <summary>
/// Shape tests for CNT002's SQL query. Asserts the load-bearing clauses are present so
/// accidental regressions (someone removes the staleness gate, swaps LEFT JOIN to INNER,
/// drops the @StaleDays parameter) fail CI instead of silently changing rule semantics.
///
/// <para>
/// True execution correctness gets verified by running the check against a live Kentico
/// database during dogfood — we can't unit-test raw SQL without either a real SQL Server
/// instance or a dialect-compatible shim, neither of which is worth the overhead for one
/// check. The shape tests below are a reasonable middle ground: they catch the specific
/// regressions we care about (filter/threshold correctness) without requiring DB access.
/// </para>
/// </summary>
public class OrphanedContentItemsCheckTests
{
    private static readonly string Sql = OrphanedContentItemsCheck.Sql;

    [Fact]
    public void Query_filters_to_reusable_content_items_only()
    {
        // Non-reusable items are web pages — they're expected to be "unused as content" in the
        // reference sense, so the check must not fire on them.
        Assert.Contains("ci.ContentItemIsReusable = 1", Sql);
    }

    [Fact]
    public void Query_requires_null_inbound_reference()
    {
        // The LEFT JOIN + IS NULL is how we detect "nothing points at this item". If an
        // inner join or an existence-check (EXISTS / COUNT) slips in, the check either
        // misses orphans or false-positives on ones that have references.
        Assert.Contains("LEFT JOIN CMS_ContentItemReference", Sql);
        Assert.Contains("r.ContentItemReferenceID IS NULL", Sql);
    }

    [Fact]
    public void Query_gates_on_staleness_threshold()
    {
        // The HAVING clause is the practical difference between "orphaned yesterday" (fresh
        // content the editor hasn't wired up yet) and "orphaned for months" (safe to delete).
        // Without the gate, the check floods with false positives on any active content-hub
        // workflow — the exact regression the v0.3.4-alpha enhancement fixed.
        Assert.Contains("HAVING", Sql);
        Assert.Contains("DATEADD(day, -@StaleDays", Sql);
    }

    [Fact]
    public void Query_surfaces_items_with_no_recorded_modifications()
    {
        // Items that were created but never edited have NULL in ModifiedWhen. These are a
        // common "test content uploaded during a spike and forgotten" signal — the OR IS NULL
        // branch of the HAVING clause pulls them into the finding set alongside genuinely
        // stale items.
        Assert.Contains("MAX(m.ContentItemLanguageMetadataModifiedWhen) IS NULL", Sql);
    }

    [Fact]
    public void Query_groups_multi_language_items_to_one_row()
    {
        // Group-by ContentItemID collapses multi-language metadata joins to one row per item.
        // Without this, a reusable item with 4 translations would fire 4 findings — noisy and
        // confusing at the acknowledgment layer (each language has different fingerprints).
        Assert.Contains("GROUP BY ci.ContentItemID", Sql);
    }

    [Fact]
    public void Query_parameterizes_the_staleness_threshold()
    {
        // @StaleDays comes from ScanContext.StaleDays — admin-configurable via
        // Sentinel:RuntimeChecks:StaleDays. Hard-coding the number here would silently detach
        // this check from the operator's configured threshold.
        Assert.Contains("@StaleDays", Sql);
    }

    [Fact]
    public void Check_metadata_remains_stable_for_fingerprinting()
    {
        // FindingFingerprint is hash of rule-id + category + digit-stripped message + location.
        // Changing RuleId or Category invalidates all existing acknowledgments in operator DBs —
        // catch the drift here so it's a conscious decision, not a silent break.
        var check = new OrphanedContentItemsCheck();
        Assert.Equal("CNT002", check.RuleId);
        Assert.Equal("Content Model", check.Category);
        Assert.Equal(CheckKind.Runtime, check.Kind);
    }
}
