using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Content;

/// <summary>
/// CNT010 — Stale unused image content items. Splits off from CNT002 so operators can triage
/// images separately (different ack/snooze workflow, different deletion approval chain,
/// different storage impact per item).
///
/// <para>
/// Detection: content types whose class name or display name contains image / photo / picture
/// / thumbnail. See <see cref="ContentTypePatterns.ImageMatchClause"/> for the full list. A
/// content type named "Visual" or "Graphic" would be missed by this heuristic and fall into
/// CNT002; operators with unusual naming conventions can add the specific rule to
/// <c>Sentinel:Checks:Excluded</c> if either bucket is noisy.
/// </para>
/// </summary>
public sealed class UnusedImagesCheck : ICheck
{
    public string RuleId => "CNT010";
    public string Title => "Stale unused images";
    public string Category => "Content Model";
    public CheckKind Kind => CheckKind.Runtime;

    internal static string Sql => UnusedContentItemsQueryBuilder.Build(ContentTypePatterns.ImageMatchClause);

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken) =>
        await UnusedContentItemsQueryBuilder.RunAsync(
            context,
            Sql,
            RuleId, Title, Category,
            findingDescriptor: "image",
            defaultRemediationExtra:
                "Review in Content Hub → Images section. Deleting stale images has the biggest storage " +
                "impact — binary assets accrue more bytes than structured content. Bulk workflow: select " +
                "all CNT010 findings, sort by age, batch-delete oldest first.",
            cancellationToken);
}
