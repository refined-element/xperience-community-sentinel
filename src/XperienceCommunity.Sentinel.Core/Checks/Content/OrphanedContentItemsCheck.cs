using Microsoft.Data.SqlClient;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Content;

/// <summary>
/// CNT002 — Stale unused reusable content (text-shaped). Specifically excludes image-like and
/// file-like content types, which are covered by CNT010 and CNT011 respectively. The three
/// rules share a join shape (<see cref="BuildSql"/>) and differ only in the content-type
/// predicate, so an item never fires in more than one bucket.
///
/// <para>
/// Detection model: reusable content items are "used" when some other content item references
/// them via <c>CMS_ContentItemReference</c>. Web pages, reusable-field links, and widget blobs
/// all flow through that join. Combined with "hasn't been touched in <c>ScanContext.StaleDays</c>"
/// (default 180), these are safe-to-delete candidates.
/// </para>
/// </summary>
public sealed class OrphanedContentItemsCheck : ICheck
{
    public string RuleId => "CNT002";
    public string Title => "Stale unused reusable content";
    public string Category => "Content Model";
    public CheckKind Kind => CheckKind.Runtime;

    /// <summary>
    /// SQL exposed internal so shape tests can lock down the load-bearing clauses (reference
    /// null, staleness HAVING, genericy content-type exclusion). Shared with CNT010/CNT011
    /// via <see cref="UnusedContentItemsQueryBuilder"/>.
    /// </summary>
    internal static string Sql => UnusedContentItemsQueryBuilder.Build(ContentTypePatterns.GenericExclusionClause);

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken) =>
        await UnusedContentItemsQueryBuilder.RunAsync(
            context,
            Sql,
            RuleId, Title, Category,
            findingDescriptor: "reusable content item",
            defaultRemediationExtra:
                "Review in Content Hub. If obsolete, delete or archive. For bulk cleanup: select all " +
                "CNT002 findings in Scan detail, sort by age, batch-delete the oldest tier first. " +
                "Images and files live in CNT010/CNT011 respectively.",
            cancellationToken);
}
