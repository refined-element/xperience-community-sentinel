using System.Text.Json;
using Microsoft.Data.SqlClient;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Content;

/// <summary>
/// CNT005 — Page Builder widget configurations stored in
/// CMS_ContentItemCommonData.ContentItemCommonDataVisualBuilderWidgets that fail to parse as JSON,
/// or contain widgets with empty/null identifiers. Corrupt widget data silently breaks page rendering.
/// </summary>
public sealed class MalformedWidgetsCheck : ICheck
{
    public string RuleId => "CNT005";
    public string Title => "Malformed Page Builder widget data";
    public string Category => "Page Builder";
    public CheckKind Kind => CheckKind.Runtime;

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();

        const string sql = @"
            SELECT d.ContentItemCommonDataID, d.ContentItemCommonDataContentItemID, d.ContentItemCommonDataVisualBuilderWidgets
            FROM CMS_ContentItemCommonData d
            WHERE d.ContentItemCommonDataIsLatest = 1
              AND d.ContentItemCommonDataVisualBuilderWidgets IS NOT NULL
              AND LEN(d.ContentItemCommonDataVisualBuilderWidgets) > 2;";

        await using var connection = new SqlConnection(context.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var commonDataId = reader.GetInt32(0);
            var contentItemId = reader.GetInt32(1);
            var widgetsJson = reader.GetString(2);

            try
            {
                using var doc = JsonDocument.Parse(widgetsJson);
                InspectWidgets(doc.RootElement, commonDataId, contentItemId, findings);
            }
            catch (JsonException ex)
            {
                findings.Add(new Finding(
                    RuleId, Title, Category, Severity.Error,
                    $"Widget JSON for ContentItemID {contentItemId} is not valid JSON: {ex.Message}",
                    Location: $"CMS_ContentItemCommonData.ContentItemCommonDataID={commonDataId}",
                    Remediation: "Inspect the page in the admin UI. Version history usually has a clean prior revision to restore from."));
            }
        }

        return findings;
    }

    private void InspectWidgets(JsonElement root, int commonDataId, int contentItemId, List<Finding> findings)
    {
        // Kentico's widget JSON shape has `editableAreas` → `sections` → `zones` → `widgets`, each
        // widget having a `type` or `typeIdentifier`. We walk defensively — shape evolves across versions.
        foreach (var widget in EnumerateWidgets(root))
        {
            var hasType = widget.TryGetProperty("type", out var typeProp) && !IsNullOrWhitespace(typeProp)
                || widget.TryGetProperty("typeIdentifier", out var idProp) && !IsNullOrWhitespace(idProp);

            if (!hasType)
            {
                findings.Add(new Finding(
                    RuleId, Title, Category, Severity.Warning,
                    $"ContentItemID {contentItemId} contains a widget with no type identifier.",
                    Location: $"CMS_ContentItemCommonData.ContentItemCommonDataID={commonDataId}",
                    Remediation: "Open the page in Page Builder; remove or reconfigure the orphan widget."));
            }
        }
    }

    private static IEnumerable<JsonElement> EnumerateWidgets(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                foreach (var w in EnumerateWidgets(prop.Value)) yield return w;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                // Heuristic: a widget object has a "type" or "typeIdentifier" or is clearly inside a "widgets" array
                if (item.ValueKind == JsonValueKind.Object &&
                    (item.TryGetProperty("type", out _) || item.TryGetProperty("typeIdentifier", out _)))
                {
                    yield return item;
                }
                foreach (var w in EnumerateWidgets(item)) yield return w;
            }
        }
    }

    private static bool IsNullOrWhitespace(JsonElement element) =>
        element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString());
}
