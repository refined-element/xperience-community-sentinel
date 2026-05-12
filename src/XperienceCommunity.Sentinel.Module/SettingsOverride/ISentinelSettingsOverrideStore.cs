namespace XperienceCommunity.Sentinel.Module.SettingsOverride;

/// <summary>
/// Reads and writes the single-row override state layered on top of <c>SentinelOptions</c>.
/// The read path is hot — called by <c>PostConfigure&lt;SentinelOptions&gt;</c> on every options
/// resolve — so implementations should either be cheap per-call or cache with invalidation on
/// write.
/// </summary>
public interface ISentinelSettingsOverrideStore
{
    /// <summary>
    /// Returns the current override snapshot, or <c>null</c> if no override row exists (meaning
    /// the appsettings.json / env-var chain wins for every setting). Read side of the "all or
    /// nothing" invariant.
    /// </summary>
    SentinelSettingsSnapshot? Get();

    /// <summary>
    /// Persists <paramref name="snapshot"/> as the current override, stamped with the calling
    /// admin's user ID for audit. Upsert semantics: overwrites the existing row if one exists,
    /// creates the row otherwise. No concurrency control — last writer wins (acceptable for
    /// admin-UI-driven writes which are rare).
    /// </summary>
    void Save(SentinelSettingsSnapshot snapshot, int userId);

    /// <summary>
    /// Removes the override row entirely, returning SentinelOptions to its base (appsettings /
    /// env-var) values. No-op if no row exists.
    /// </summary>
    void Clear();
}
