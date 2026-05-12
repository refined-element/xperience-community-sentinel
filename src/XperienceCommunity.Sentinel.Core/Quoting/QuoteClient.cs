using System.Net.Http.Json;
using System.Text.Json;

namespace XperienceCommunity.Sentinel.Quoting;

/// <summary>
/// POSTs a <see cref="QuoteSubmission"/> to the Refined Element quote endpoint on KDaaS.
/// </summary>
public sealed class QuoteClient
{
    public const string DefaultEndpoint = "https://kentico-developer.com/api/scanner/submit";
    public const string EndpointEnvVar = "SENTINEL_QUOTE_ENDPOINT";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;

    public QuoteClient(HttpClient http)
    {
        _http = http;
    }

    public static string ResolveEndpoint(string? overrideUrl) =>
        !string.IsNullOrWhiteSpace(overrideUrl) ? overrideUrl
        : Environment.GetEnvironmentVariable(EndpointEnvVar) is { Length: > 0 } env ? env
        : DefaultEndpoint;

    public async Task<QuoteResult> SubmitAsync(string endpoint, QuoteSubmission submission, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(endpoint, submission, JsonOptions, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? new QuoteResult(true, (int)response.StatusCode, body, null)
                : new QuoteResult(false, (int)response.StatusCode, body, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (HttpRequestException ex)
        {
            return new QuoteResult(false, 0, null, ex.Message);
        }
        catch (TaskCanceledException)
        {
            return new QuoteResult(false, 0, null, "Request timed out.");
        }
    }
}

public sealed record QuoteResult(bool Success, int StatusCode, string? ResponseBody, string? ErrorMessage);
