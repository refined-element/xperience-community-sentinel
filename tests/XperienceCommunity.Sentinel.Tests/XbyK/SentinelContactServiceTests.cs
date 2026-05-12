using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using XperienceCommunity.Sentinel.Quoting;
using XperienceCommunity.Sentinel.Reporting;
using XperienceCommunity.Sentinel.Module.Configuration;
using XperienceCommunity.Sentinel.Module.Contact;

namespace XperienceCommunity.Sentinel.Tests.XbyK;

/// <summary>
/// Covers the service layer over the quote intake endpoint. The transport itself is stubbed via
/// a captured <see cref="HttpMessageHandler"/> so tests exercise real JSON serialization, URL
/// resolution, and status-code handling — just without the network.
/// </summary>
public class SentinelContactServiceTests
{
    [Fact]
    public async Task Successful_response_returns_Success_true_with_status_and_body()
    {
        var (service, captured) = ServiceReturning(HttpStatusCode.OK, "thanks");
        var result = await service.SubmitAsync(Submission(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("thanks", result.ResponseBody);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(captured.Request);
        Assert.Equal(HttpMethod.Post, captured.Request!.Method);
    }

    [Fact]
    public async Task Non_success_response_returns_Success_false_with_body_preserved()
    {
        // The admin UI needs the server's error body to surface a useful failure message —
        // "something went wrong" helps nobody. Lock that it comes through on 4xx/5xx.
        var (service, _) = ServiceReturning(HttpStatusCode.BadRequest, "validation failed: missing email");
        var result = await service.SubmitAsync(Submission(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("validation failed: missing email", result.ResponseBody);
        Assert.Contains("HTTP 400", result.ErrorMessage);
    }

    [Fact]
    public async Task Transport_exception_surfaces_as_failure_result_with_status_zero()
    {
        var (service, _) = ServiceThatThrows(new HttpRequestException("connection refused"));
        var result = await service.SubmitAsync(Submission(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.StatusCode);
        Assert.Contains("connection refused", result.ErrorMessage);
    }

    [Fact]
    public async Task Timeout_returns_failure_with_timed_out_message()
    {
        // TaskCanceledException without the caller's token cancelling = HttpClient timeout.
        // Must distinguish from caller cancellation (which rethrows).
        var (service, _) = ServiceThatThrows(new TaskCanceledException("deadline exceeded"));
        var result = await service.SubmitAsync(Submission(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Request timed out.", result.ErrorMessage);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_being_swallowed()
    {
        // If the admin UI cancels (e.g., user navigates away), we must not hide that as a "timed
        // out" result — callers rely on OperationCanceledException to unwind their async flows.
        // HttpClient surfaces token cancellation as TaskCanceledException, which is a subtype of
        // OperationCanceledException; ThrowsAnyAsync accepts subtypes, ThrowsAsync would not.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var (service, _) = ServiceReturning(HttpStatusCode.OK, "unused");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SubmitAsync(Submission(), cts.Token));
    }

    [Fact]
    public async Task Endpoint_override_sent_to_configured_url()
    {
        const string overrideEndpoint = "https://staging.kentico-developer.com/api/scanner/submit";
        var (service, captured) = ServiceWith(HttpStatusCode.OK, "ok", overrideEndpoint);
        await service.SubmitAsync(Submission(), CancellationToken.None);

        Assert.Equal(overrideEndpoint, captured.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Default_endpoint_used_when_config_blank()
    {
        // Blank/missing config -> fall through to QuoteClient.DefaultEndpoint. Locks the
        // fallback so admins who never touch Sentinel:Contact:Endpoint still reach production.
        var (service, captured) = ServiceWith(HttpStatusCode.OK, "ok", endpointOverride: "");
        await service.SubmitAsync(Submission(), CancellationToken.None);

        Assert.Equal(QuoteClient.DefaultEndpoint, captured.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Payload_is_camelCase_JSON()
    {
        // The server (KDaaS /api/scanner/submit) consumes camelCase. Without the explicit
        // JsonNamingPolicy.CamelCase in the service, .NET would serialize PascalCase record
        // members as-is and every field would be silently dropped on the server side.
        var (service, captured) = ServiceReturning(HttpStatusCode.OK, "ok");
        await service.SubmitAsync(Submission(), CancellationToken.None);

        Assert.NotNull(captured.Body);
        using var doc = JsonDocument.Parse(captured.Body!);
        Assert.True(doc.RootElement.TryGetProperty("contactEmail", out _), "payload must camelCase 'contactEmail'");
        Assert.False(doc.RootElement.TryGetProperty("ContactEmail", out _), "payload must NOT PascalCase 'ContactEmail'");
    }

    [Fact]
    public async Task Null_submission_throws_argument_null()
    {
        var (service, _) = ServiceReturning(HttpStatusCode.OK, "ok");
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SubmitAsync(null!, CancellationToken.None));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://files.example.com/submit")] // wrong scheme
    [InlineData("/api/scanner/submit")] // relative path, missing scheme + host
    public async Task Invalid_endpoint_url_returns_failure_instead_of_throwing(string badEndpoint)
    {
        // PostAsJsonAsync will throw UriFormatException / InvalidOperationException for these
        // cases and those don't hit any of the catch blocks. Without the pre-flight validator,
        // admins who typo Sentinel:Contact:Endpoint get a 500 instead of a clean failure result.
        // Blank/whitespace values are covered separately by Default_endpoint_used_when_config_blank.
        var (service, captured) = ServiceWith(HttpStatusCode.OK, "unused", endpointOverride: badEndpoint);
        var result = await service.SubmitAsync(Submission(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.StatusCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Sentinel:Contact:Endpoint", result.ErrorMessage);
        // Request should never have been sent — the validator short-circuits before handing to
        // the HttpClient pipeline.
        Assert.Null(captured.Request);
    }

    // --- helpers ---

    private static QuoteSubmission Submission() => new(
        ContactEmail: "ops@example.com",
        SentinelVersion: "0.0.0-test",
        Scan: new ReportScan(
            StartedAt: DateTimeOffset.UtcNow.AddSeconds(-5),
            CompletedAt: DateTimeOffset.UtcNow,
            DurationSeconds: 5.0,
            RepoPath: "/repo",
            RuntimeEnabled: true),
        Summary: new ReportSummary(Total: 1, Errors: 0, Warnings: 1, Info: 0),
        Findings: new[]
        {
            new QuoteFinding(
                RuleId: "CHK001",
                RuleTitle: "Sample",
                Category: "Test",
                Severity: "Warning",
                Message: "example",
                Location: null,
                Remediation: null),
        },
        IncludesContext: false);

    private static (SentinelContactService Service, CapturingHandler Captured) ServiceReturning(HttpStatusCode status, string body) =>
        BuildService(new CapturingHandler(status, body));

    private static (SentinelContactService Service, CapturingHandler Captured) ServiceThatThrows(Exception toThrow) =>
        BuildService(new CapturingHandler(toThrow));

    private static (SentinelContactService Service, CapturingHandler Captured) ServiceWith(HttpStatusCode status, string body, string endpointOverride) =>
        BuildService(new CapturingHandler(status, body), endpointOverride);

    private static (SentinelContactService, CapturingHandler) BuildService(CapturingHandler handler, string endpointOverride = "")
    {
        var http = new HttpClient(handler);
        var options = Options.Create(new SentinelOptions
        {
            Contact = new SentinelOptions.ContactOptions { Endpoint = endpointOverride },
        });
        var service = new SentinelContactService(http, options, NullLogger<SentinelContactService>.Instance);
        return (service, handler);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        // Defaulting both response-shape fields at the declaration so the throw-only constructor
        // below doesn't leave them uninitialized. Without the defaults the compiler refuses to
        // compile either ctor under the project's nullable+treat-warnings-as-errors settings.
        private readonly HttpStatusCode statusCode = HttpStatusCode.OK;
        private readonly string body = string.Empty;
        private readonly Exception? toThrow;

        public CapturingHandler(HttpStatusCode statusCode, string body)
        {
            this.statusCode = statusCode;
            this.body = body;
        }

        public CapturingHandler(Exception toThrow)
        {
            this.toThrow = toThrow;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (toThrow is not null)
            {
                throw toThrow;
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/plain"),
            };
        }
    }
}
