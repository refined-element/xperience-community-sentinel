namespace XperienceCommunity.Sentinel.Checks.Dependencies;

/// <summary>
/// Abstraction over the NuGet "latest version for a package" lookup. Exists so <see cref="OutdatedPackagesCheck"/>
/// can ask for the latest *prerelease* when the installed version is itself a prerelease — which `dotnet list
/// package --outdated` silently refuses to do, reporting "Not found at the sources" instead. Injectable so
/// tests can swap in a stub that doesn't hit nuget.org.
/// </summary>
public interface INuGetVersionLookup
{
    /// <summary>
    /// Returns the latest version of <paramref name="packageId"/> published to nuget.org, or null when the
    /// package is not found or the lookup fails. When <paramref name="includePrerelease"/> is true, prerelease
    /// versions (those with a `-` suffix like `0.4.3-alpha`) are considered.
    /// </summary>
    Task<string?> GetLatestVersionAsync(string packageId, bool includePrerelease, CancellationToken cancellationToken);

    /// <summary>
    /// Same as <see cref="GetLatestVersionAsync(string, bool, CancellationToken)"/>, but scopes the lookup to
    /// the declared NuGet <paramref name="sources"/> (typically the <c>sources</c> array from
    /// <c>dotnet list package --format json</c>). When <paramref name="sources"/> is <c>null</c> or empty,
    /// implementations fall back to nuget.org so callers that don't plumb source information through keep
    /// their existing behavior. Querying only declared sources avoids (a) returning a stale "latest" when a
    /// newer version exists on a private feed, and (b) leaking private package IDs to nuget.org.
    /// </summary>
    Task<string?> GetLatestVersionAsync(
        string packageId,
        bool includePrerelease,
        IReadOnlyList<string>? sources,
        CancellationToken cancellationToken)
#pragma warning disable CA1033 // Default interface implementation keeps existing implementations source-compatible.
        => GetLatestVersionAsync(packageId, includePrerelease, cancellationToken);
#pragma warning restore CA1033
}
