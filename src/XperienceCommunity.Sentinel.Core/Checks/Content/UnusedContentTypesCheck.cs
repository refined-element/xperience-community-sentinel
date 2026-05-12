using Microsoft.Data.SqlClient;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Content;

/// <summary>
/// CNT001 — Content types (CMS_Class rows where ClassType = 'Content') that have zero
/// corresponding content items in CMS_ContentItem. Candidates for deletion.
/// </summary>
public sealed class UnusedContentTypesCheck : ICheck
{
    public string RuleId => "CNT001";
    public string Title => "Unused content types";
    public string Category => "Content Model";
    public CheckKind Kind => CheckKind.Runtime;

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();

        const string sql = @"
            SELECT c.ClassName, c.ClassDisplayName, c.ClassContentTypeType
            FROM CMS_Class c
            LEFT JOIN CMS_ContentItem ci ON ci.ContentItemContentTypeID = c.ClassID
            WHERE c.ClassType = 'Content'
              AND ci.ContentItemID IS NULL
            ORDER BY c.ClassDisplayName;";

        await using var connection = new SqlConnection(context.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var className = reader.GetString(0);
            var displayName = reader.GetString(1);
            var typeType = reader.IsDBNull(2) ? "(unknown)" : reader.GetString(2);

            findings.Add(new Finding(
                RuleId, Title, Category, Severity.Info,
                $"Content type '{displayName}' ({className}, {typeType}) has zero content items.",
                Location: $"CMS_Class.ClassName={className}",
                Remediation: "Delete the content type in the Kentico admin if genuinely unused, or create the first item. Refined Element can also sweep these in a paid cleanup engagement."));
        }

        return findings;
    }
}
