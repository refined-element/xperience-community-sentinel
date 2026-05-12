using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using XperienceCommunity.Sentinel.Quoting;
using XperienceCommunity.Sentinel.Module.Configuration;

namespace XperienceCommunity.Sentinel.Module.Contact;

/// <summary>
/// Typed-HttpClient implementation — the DI container injects a client whose defaults
/// (30 s timeout, <c>XperienceCommunity-Sentinel-Module/{version}</c> user agent) are set in
/// <c>SentinelServiceCollectionExtensions</c>. The service layer's job is: resolve the endpoint
/// from <see cref="SentinelOptions.ContactOptions.Endpoint"/>, validate it as an absolute
/// http(s) URL, POST JSON, translate exceptions into a result object so callers never have to
/// catch <see cref="HttpRequestException"/> themselves.
/// </summary>
internal sealed class SentinelContactService(
    HttpClient httpClient,
    IOptions<SentinelOptions> options,
    ILogger<SentinelContactService> logger) : ISentinelContactService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SentinelOptions options = options.Value;

    public async Task<QuoteResult> SubmitAsync(QuoteSubmission submission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var endpoint = ResolveEndpoint();

        // Validate BEFORE handing to HttpClient. PostAsJsonAsync throws UriFormatException /
        // InvalidOperationException for malformed URLs and those aren't caught below — they'd
        // escape our "always return a QuoteResult on failure" contract. An admin with a typo'd
        // Sentinel:Contact:Endpoint should see a clean failure result, not a stack trace.
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogWarning(
                "Sentinel contact: Sentinel:Contact:Endpoint is not a valid absolute http(s) URL. Submission aborted.");
            return new QuoteResult(false, 0, null,
                "Sentinel:Contact:Endpoint is not a valid absolute http(s) URL. Update appsettings and retry.");
        }

        logger.LogInformation(
            "Sentinel contact: submitting quote for {FindingsCount} findings to {Host}.",
            submission.Findings.Count,
            SafeHost(endpoint));

        try
        {
            using var response = await httpClient.PostAsJsonAsync(endpoint, submission, JsonOptions, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Sentinel contact: submission succeeded ({Status}).", statusCode);
                return new QuoteResult(true, statusCode, body, null);
            }

            // Non-2xx is still a completed request — surface the body so callers (admin UI) can
            // render "here's what the server said" rather than a generic "submission failed."
            var error = $"HTTP {statusCode} {response.ReasonPhrase}";
            logger.LogWarning("Sentinel contact: submission returned {Status}: {Reason}.", statusCode, response.ReasonPhrase);
            return new QuoteResult(false, statusCode, body, error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller-initiated cancellation — don't log as an error. Let the caller decide.
            throw;
        }
        catch (TaskCanceledException)
        {
            // Distinct from user cancellation: HttpClient timed out (no token cancellation).
            logger.LogWarning("Sentinel contact: submission timed out against {Host}.", SafeHost(endpoint));
            return new QuoteResult(false, 0, null, "Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Sentinel contact: submission failed against {Host}.", SafeHost(endpoint));
            return new QuoteResult(false, 0, null, ex.Message);
        }
    }

    private string ResolveEndpoint() =>
        !string.IsNullOrWhiteSpace(options.Contact.Endpoint)
            ? options.Contact.Endpoint
            : QuoteClient.DefaultEndpoint;

    // Logs only the host to avoid emitting credentials that could appear in a user-configured URL.
    private static string SafeHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "(invalid url)";
}
