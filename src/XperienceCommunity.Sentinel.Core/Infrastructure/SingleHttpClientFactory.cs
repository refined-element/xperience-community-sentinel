namespace XperienceCommunity.Sentinel.Infrastructure;

/// <summary>
/// Minimal <see cref="IHttpClientFactory"/> for short-lived hosts (CLI, tests, one-shot scans).
/// One shared <see cref="HttpClient"/> for the lifetime of the factory — no connection rotation
/// or named-client routing needed. XbyK embedded mode should use the real DI-registered factory
/// from <c>AddHttpClient()</c> instead of this one.
/// </summary>
public sealed class SingleHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly HttpClient _client;

    public SingleHttpClientFactory()
    {
        _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("XperienceCommunity.Sentinel/0.1.0");
    }

    public HttpClient CreateClient(string name) => _client;

    public void Dispose() => _client.Dispose();
}
