using System.Text.Json;

namespace XperienceCommunity.Sentinel.Reporting;

public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task WriteAsync(ReportDocument document, string outputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, document, Options, cancellationToken).ConfigureAwait(false);
    }
}
