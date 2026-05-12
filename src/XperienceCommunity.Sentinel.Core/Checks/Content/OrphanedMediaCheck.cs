using Microsoft.Data.SqlClient;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Content;

/// <summary>
/// CNT004 — Media files whose library no longer exists, or whose FileSize is zero (an incomplete upload).
/// Both are referential-integrity bugs that the Kentico admin UI doesn't surface.
/// </summary>
public sealed class OrphanedMediaCheck : ICheck
{
    public string RuleId => "CNT004";
    public string Title => "Broken media file references";
    public string Category => "Assets";
    public CheckKind Kind => CheckKind.Runtime;

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();

        const string orphanLibrarySql = @"
            SELECT f.FileID, f.FileName, f.FileExtension, f.FileLibraryID
            FROM Media_File f
            LEFT JOIN Media_Library l ON l.LibraryID = f.FileLibraryID
            WHERE l.LibraryID IS NULL;";

        const string zeroSizeSql = @"
            SELECT f.FileID, f.FileName, f.FileExtension
            FROM Media_File f
            WHERE f.FileSize = 0;";

        await using var connection = new SqlConnection(context.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var cmd = new SqlCommand(orphanLibrarySql, connection))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var fileId = reader.GetInt32(0);
                var name = reader.GetString(1);
                var ext = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var libraryId = reader.GetInt32(3);

                findings.Add(new Finding(
                    RuleId, Title, Category, Severity.Warning,
                    $"Media file '{name}{ext}' points to library ID {libraryId}, which no longer exists.",
                    Location: $"Media_File.FileID={fileId}",
                    Remediation: "Delete the orphaned Media_File row or restore its library."));
            }
        }

        await using (var cmd = new SqlCommand(zeroSizeSql, connection))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var fileId = reader.GetInt32(0);
                var name = reader.GetString(1);
                var ext = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);

                findings.Add(new Finding(
                    RuleId, Title, Category, Severity.Warning,
                    $"Media file '{name}{ext}' has FileSize = 0 — likely an incomplete upload.",
                    Location: $"Media_File.FileID={fileId}",
                    Remediation: "Re-upload the file or delete the placeholder record."));
            }
        }

        return findings;
    }
}
