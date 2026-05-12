using Microsoft.Data.SqlClient;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Content;

/// <summary>
/// Shared SQL + execution logic for the "stale unused content item" family (CNT002 / CNT010 /
/// CNT011). Each check plugs in its own content-type-name predicate and the builder produces
/// the same join shape, the same staleness gate, and the same Finding construction.
///
/// <para>
/// Mutual exclusivity across the three rules is the whole reason to share this — if any two
/// checks accidentally fire on the same item, operators see duplicate findings with different
/// rule IDs and the ack system fingerprints twice. Centralizing the query keeps the predicate
/// variation in one place: ImageMatchClause, FileMatchClause, and the NOT (Image OR File)
/// fallback.
/// </para>
/// </summary>
internal static class UnusedContentItemsQueryBuilder
{
    /// <summary>
    /// Builds the SQL for a content-item staleness rule parameterized by <paramref name="contentTypePredicate"/>.
    /// The predicate is inlined into the WHERE clause (NOT a parameter — it's a SQL fragment
    /// controlled by <see cref="ContentTypePatterns"/>, never user input). <c>@StaleDays</c>
    /// remains a bound parameter.
    /// </summary>
    public static string Build(string contentTypePredicate) => $@"
        SELECT
            ci.ContentItemID,
            MAX(COALESCE(m.ContentItemLanguageMetadataDisplayName, ci.ContentItemName)) AS DisplayName,
            MAX(c.ClassDisplayName) AS TypeName,
            MAX(m.ContentItemLanguageMetadataModifiedWhen) AS LatestModifiedWhen
        FROM CMS_ContentItem ci
        INNER JOIN CMS_Class c
            ON c.ClassID = ci.ContentItemContentTypeID
        LEFT JOIN CMS_ContentItemReference r
            ON r.ContentItemReferenceTargetItemID = ci.ContentItemID
        LEFT JOIN CMS_ContentItemLanguageMetadata m
            ON m.ContentItemLanguageMetadataContentItemID = ci.ContentItemID
        WHERE ci.ContentItemIsReusable = 1
          AND r.ContentItemReferenceID IS NULL
          AND ({contentTypePredicate})
        GROUP BY ci.ContentItemID
        HAVING MAX(m.ContentItemLanguageMetadataModifiedWhen) < DATEADD(day, -@StaleDays, SYSUTCDATETIME())
           OR MAX(m.ContentItemLanguageMetadataModifiedWhen) IS NULL
        ORDER BY MAX(m.ContentItemLanguageMetadataModifiedWhen) ASC;";

    /// <summary>
    /// Execution side — runs the query, emits one finding per row. Message shape and remediation
    /// vary per rule, which is why they're passed in rather than hard-coded.
    /// </summary>
    public static async Task<IReadOnlyList<Finding>> RunAsync(
        ScanContext context,
        string sql,
        string ruleId,
        string title,
        string category,
        string findingDescriptor,
        string defaultRemediationExtra,
        CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();

        await using var connection = new SqlConnection(context.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@StaleDays", context.StaleDays);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var itemId = reader.GetInt32(0);
            var displayName = reader.IsDBNull(1) ? "(unnamed)" : reader.GetString(1);
            var typeName = reader.IsDBNull(2) ? "(unknown)" : reader.GetString(2);
            var latestModified = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3);

            var agePhrase = latestModified.HasValue
                ? $"last edited {(int)(DateTime.UtcNow - latestModified.Value).TotalDays} days ago"
                : "no modifications recorded";

            findings.Add(new Finding(
                ruleId, title, category, Severity.Info,
                $"'{displayName}' ({typeName}) — {findingDescriptor} with no inbound references and {agePhrase} (threshold: {context.StaleDays} days).",
                Location: $"CMS_ContentItem.ContentItemID={itemId}",
                Remediation: defaultRemediationExtra));
        }

        return findings;
    }
}
