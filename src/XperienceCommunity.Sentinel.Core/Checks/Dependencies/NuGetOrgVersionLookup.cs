using System.Collections.Concurrent;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace XperienceCommunity.Sentinel.Checks.Dependencies;

/// <summary>
/// Default <see cref="INuGetVersionLookup"/> — talks to NuGet feeds via the NuGet.Protocol metadata resource.
/// Used by <see cref="OutdatedPackagesCheck"/> as a fallback when `dotnet list package --outdated` returns
/// "Not found at the sources" for a prerelease-installed package.
/// </summary>
/// <remarks>
/// When the scanned project declares additional NuGet sources (via <c>NuGet.config</c> or <c>dotnet list
/// package</c>'s <c>sources</c> array) we query every declared source and pick the highest version. This
/// keeps private package IDs off nuget.org when the package only lives on an internal feed, and avoids
/// returning a stale "latest" from nuget.org when a newer prerelease exists on a private feed.
/// </remarks>
public sealed class NuGetOrgVersionLookup : INuGetVersionLookup
{
    private const string NuGetOrgIndexUrl = "https://api.nuget.org/v3/index.json";

    // Cache SourceRepository instances per feed URL. Repository construction does HTTP service-index
    // discovery on first resource request, so reusing the instance across scan packages is the whole
    // point of the factory.
    private static readonly ConcurrentDictionary<string, SourceRepository> RepositoryCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly SourceRepository NuGetOrgRepository = GetOrCreateRepository(NuGetOrgIndexUrl);

    public async Task<string?> GetLatestVersionAsync(string packageId, bool includePrerelease, CancellationToken cancellationToken)
    {
        return await GetLatestVersionAsync(packageId, includePrerelease, sources: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetLatestVersionAsync(
        string packageId,
        bool includePrerelease,
        IReadOnlyList<string>? sources,
        CancellationToken cancellationToken)
    {
        var repositories = ResolveRepositories(sources);
        if (repositories.Count == 0)
        {
            return null;
        }

        NuGetVersion? best = null;

        foreach (var repository in repositories)
        {
            // A canceled scan (Ctrl-C, host shutdown) must unwind the whole operation instead of being
            // silently treated as "no version found". Re-throwing here is enough — the catch below
            // re-throws OperationCanceledException unchanged, but a pre-loop check saves one wasted
            // GetResourceAsync per already-canceled feed.
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var metadata = await repository.GetResourceAsync<MetadataResource>(cancellationToken).ConfigureAwait(false);
                using var cacheContext = new SourceCacheContext();
                var latest = await metadata.GetLatestVersion(
                    packageId,
                    includePrerelease: includePrerelease,
                    includeUnlisted: false,
                    sourceCacheContext: cacheContext,
                    log: NullLogger.Instance,
                    token: cancellationToken).ConfigureAwait(false);

                if (latest is not null && (best is null || latest > best))
                {
                    best = latest;
                }
            }
            catch (OperationCanceledException)
            {
                // Propagate cancellation (Ctrl-C or host shutdown) so the scan unwinds promptly.
                // Without this, the blanket catch below would swallow it and the scanner would keep
                // grinding through the remaining feeds after the user asked to stop.
                throw;
            }
            catch (Exception)
            {
                // Network/protocol failures against a single feed are non-fatal — continue to the next
                // source. If every feed fails, the caller falls back to the original "Not found" message.
            }
        }

        return best?.ToNormalizedString();
    }

    private static IReadOnlyList<SourceRepository> ResolveRepositories(IReadOnlyList<string>? sources)
    {
        if (sources is null || sources.Count == 0)
        {
            // No sources declared — fall back to nuget.org for backwards compatibility with callers
            // that don't plumb source information through.
            return [NuGetOrgRepository];
        }

        var repositories = new List<SourceRepository>(sources.Count);
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            repositories.Add(GetOrCreateRepository(source));
        }

        return repositories;
    }

    private static SourceRepository GetOrCreateRepository(string sourceUrl) =>
        RepositoryCache.GetOrAdd(sourceUrl, static url =>
            Repository.Factory.GetCoreV3(new PackageSource(url)));
}
