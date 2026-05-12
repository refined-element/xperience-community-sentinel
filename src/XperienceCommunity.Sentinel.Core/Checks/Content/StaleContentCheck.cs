using Microsoft.Data.SqlClient;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Content;

/// <summary>
/// CNT003 — Content items whose last modification is older than <see cref="ScanContext.StaleDays"/>.
/// Stale content drifts out of brand voice, accrues dead links, and quietly harms SEO.
/// </summary>
public sealed class StaleContentCheck : ICheck
{
    public string RuleId => "CNT003";
    public string Title => "Stale content";
    public string Category => "Content Model";
    public CheckKind Kind => CheckKind.Runtime;

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();

        const string sql = @"
            SELECT
                ci.ContentItemID,
                m.ContentItemLanguageMetadataDisplayName,
                c.ClassDisplayName,
                m.ContentItemLanguageMetadataModifiedWhen
            FROM CMS_ContentItemLanguageMetadata m
            INNER JOIN CMS_ContentItem ci ON ci.ContentItemID = m.ContentItemLanguageMetadataContentItemID
            INNER JOIN CMS_Class c ON c.ClassID = ci.ContentItemContentTypeID
            WHERE m.ContentItemLanguageMetadataModifiedWhen < DATEADD(day, -@StaleDays, SYSUTCDATETIME())
            ORDER BY m.ContentItemLanguageMetadataModifiedWhen ASC;";

        await using var connection = new SqlConnection(context.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@StaleDays", context.StaleDays);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var itemId = reader.GetInt32(0);
            var displayName = reader.GetString(1);
            var typeName = reader.GetString(2);
            var modifiedWhen = reader.GetDateTime(3);
            var ageDays = (int)(DateTime.UtcNow - modifiedWhen).TotalDays;

            findings.Add(new Finding(
                RuleId, Title, Category, Severity.Info,
                $"'{displayName}' ({typeName}) has not been edited in {ageDays} days (threshold: {context.StaleDays}).",
                Location: $"CMS_ContentItem.ContentItemID={itemId}",
                Remediation: "Review and refresh, or mark intentionally evergreen. Paid tier can draft updates automatically from analytics signals."));
        }

        return findings;
    }
}
