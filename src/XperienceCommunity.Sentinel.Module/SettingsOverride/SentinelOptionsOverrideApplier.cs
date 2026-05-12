using Microsoft.Extensions.Options;

using XperienceCommunity.Sentinel.Module.Configuration;

namespace XperienceCommunity.Sentinel.Module.SettingsOverride;

/// <summary>
/// DI-registered <see cref="IPostConfigureOptions{TOptions}"/> that layers the admin-UI-edited
/// override on top of <see cref="SentinelOptions"/>. Runs after the framework's
/// configuration-source binding (appsettings → env vars → delegate overloads), so the override
/// wins — which is exactly what "edit from admin UI" should mean.
///
/// <para>
/// Lifetime note: <see cref="IOptions{T}"/> caches; <see cref="IOptionsSnapshot{T}"/> doesn't.
/// The rest of Sentinel uses the latter where "settings apply on the next scan" matters — so a
/// Save from the admin UI takes effect on the next scheduled tick without a restart. IOptions
/// consumers would still need an app restart to pick up new values, but we don't have any of
/// those on the hot path (checked: scan runner uses IOptionsSnapshot, notifiers resolve per
/// run, admin page resolves per request).
/// </para>
/// </summary>
internal sealed class SentinelOptionsOverrideApplier(
    ISentinelSettingsOverrideStore store) : IPostConfigureOptions<SentinelOptions>
{
    public void PostConfigure(string? name, SentinelOptions options)
    {
        var snapshot = store.Get();
        if (snapshot is null)
        {
            return;
        }

        // Every field here is "all or nothing" — absence of a row means no override, presence
        // means every property below gets written. Keeps the save payload and the applied
        // state perfectly aligned: admin clicks Save with what they see in the UI, next scan
        // reads exactly those values.
        options.Enabled = snapshot.Enabled;
        options.Checks.Excluded = snapshot.ExcludedChecks.ToList();
        options.RuntimeChecks.StaleDays = snapshot.StaleDays;
        options.RuntimeChecks.EventLogDays = snapshot.EventLogDays;
        options.EventLogIntegration.Enabled = snapshot.EventLogEnabled;
        options.EventLogIntegration.SeverityThreshold = snapshot.EventLogSeverityThreshold;
        options.EventLogIntegration.MaxEntriesPerScan = snapshot.EventLogMaxEntriesPerScan;
        options.EmailDigest.Enabled = snapshot.EmailDigestEnabled;
        options.EmailDigest.Recipients = snapshot.EmailDigestRecipients.ToList();
        options.EmailDigest.SeverityThreshold = snapshot.EmailDigestSeverityThreshold;
        options.EmailDigest.OnlyWhenThresholdFindings = snapshot.EmailDigestOnlyWhenThresholdFindings;
    }
}
