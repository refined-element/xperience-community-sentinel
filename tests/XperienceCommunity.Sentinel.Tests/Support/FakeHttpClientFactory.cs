namespace XperienceCommunity.Sentinel.Tests.Support;

/// <summary>A throwaway <see cref="IHttpClientFactory"/> for tests that don't exercise HTTP paths.</summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
