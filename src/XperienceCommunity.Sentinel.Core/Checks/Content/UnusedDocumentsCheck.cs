using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Content;

/// <summary>
/// CNT011 — Stale unused file / document content items. Splits off from CNT002 so operators
/// can triage documents separately — the deletion approval chain for a PDF attached to a page
/// historically differs from deleting structured content, and tracking binary files separately
/// gives a clearer storage-impact picture.
///
/// <para>
/// Detection: content types whose class name or display name contains file / document / pdf /
/// attachment / media. See <see cref="ContentTypePatterns.FileMatchClause"/>. Overlaps with
/// CNT010's image-match would fire in both buckets — mutually exclusive predicates prevent
/// that (we pick one or the other, never both).
/// </para>
/// </summary>
public sealed class UnusedDocumentsCheck : ICheck
{
    public string RuleId => "CNT011";
    public string Title => "Stale unused documents / files";
    public string Category => "Content Model";
    public CheckKind Kind => CheckKind.Runtime;

    internal static string Sql => UnusedContentItemsQueryBuilder.Build(ContentTypePatterns.FileMatchClause);

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken) =>
        await UnusedContentItemsQueryBuilder.RunAsync(
            context,
            Sql,
            RuleId, Title, Category,
            findingDescriptor: "document / file",
            defaultRemediationExtra:
                "Review in Content Hub → Files / Documents section. Check for offline references — a " +
                "contract PDF linked from an email template would appear orphaned in the DB but still " +
                "be served by a template. Bulk workflow: select CNT011 findings, sort by age, " +
                "batch-delete the oldest untouched tier.",
            cancellationToken);
}
