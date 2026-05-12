using System.Text.Json;

using CMS.DataEngine;

using Microsoft.Extensions.DependencyInjection;

using XperienceCommunity.Sentinel.Module.InfoModels.SentinelSettingsOverride;

namespace XperienceCommunity.Sentinel.Module.SettingsOverride;

/// <summary>
/// Info-provider-backed implementation of the override store. The "single row" invariant is
/// enforced by always targeting <c>OrderBy(ID).TopN(1)</c> on read/write and deleting any
/// stragglers on save. If a concurrent insert slips in (two admins saving at once), the loser's
/// row becomes orphaned and is cleaned up by the next save.
///
/// <para>
/// Lifetime: Singleton. The consumer of this abstraction —
/// <see cref="SentinelOptionsOverrideApplier"/> — must itself be resolvable from the root
/// provider because <c>IConfigureOptions</c> / <c>IPostConfigureOptions</c> are discovered
/// when <c>IOptions.Value</c> is cached at the singleton scope. Holding an
/// <c>IInfoProvider&lt;T&gt;</c> directly would fail the scope-validation check; we ask the
/// <see cref="IServiceScopeFactory"/> for a fresh scope per call instead. The scope is cheap
/// (no DB connection until <c>provider.Get()</c> is called) and disposed at the end of the
/// operation, so we don't leak scoped resources.
/// </para>
/// </summary>
internal sealed class SentinelSettingsOverrideStore(
    IServiceScopeFactory scopeFactory) : ISentinelSettingsOverrideStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public SentinelSettingsSnapshot? Get()
    {
        using var scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IInfoProvider<SentinelSettingsOverrideInfo>>();
        var row = LoadRow(provider);
        return row is null ? null : ToSnapshot(row);
    }

    public void Save(SentinelSettingsSnapshot snapshot, int userId)
    {
        using var scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IInfoProvider<SentinelSettingsOverrideInfo>>();

        var row = LoadRow(provider) ?? new SentinelSettingsOverrideInfo
        {
            SentinelSettingsOverrideGuid = Guid.NewGuid(),
        };
        row.SentinelSettingsOverrideEnabled = snapshot.Enabled;
        row.SentinelSettingsOverrideExcludedChecks = JsonSerializer.Serialize(snapshot.ExcludedChecks, JsonOpts);
        row.SentinelSettingsOverrideStaleDays = snapshot.StaleDays;
        row.SentinelSettingsOverrideEventLogDays = snapshot.EventLogDays;
        row.SentinelSettingsOverrideEventLogEnabled = snapshot.EventLogEnabled;
        row.SentinelSettingsOverrideEventLogSeverityThreshold = snapshot.EventLogSeverityThreshold;
        row.SentinelSettingsOverrideEventLogMaxEntriesPerScan = snapshot.EventLogMaxEntriesPerScan;
        row.SentinelSettingsOverrideEmailDigestEnabled = snapshot.EmailDigestEnabled;
        row.SentinelSettingsOverrideEmailDigestRecipients = JsonSerializer.Serialize(snapshot.EmailDigestRecipients, JsonOpts);
        row.SentinelSettingsOverrideEmailDigestSeverityThreshold = snapshot.EmailDigestSeverityThreshold;
        row.SentinelSettingsOverrideEmailDigestOnlyWhenThresholdFindings = snapshot.EmailDigestOnlyWhenThresholdFindings;
        row.SentinelSettingsOverrideLastModifiedBy = userId;
        row.SentinelSettingsOverrideLastModifiedAt = DateTime.UtcNow;
        provider.Set(row);

        // Clean up any stragglers from a past concurrent-insert race.
        var rows = provider.Get()
            .OrderBy(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideID))
            .ToList();
        foreach (var stale in rows.Skip(1))
        {
            provider.Delete(stale);
        }
    }

    public void Clear()
    {
        using var scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IInfoProvider<SentinelSettingsOverrideInfo>>();
        var rows = provider.Get().ToList();
        foreach (var row in rows)
        {
            provider.Delete(row);
        }
    }

    private static SentinelSettingsOverrideInfo? LoadRow(IInfoProvider<SentinelSettingsOverrideInfo> provider) =>
        provider.Get()
            .OrderBy(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideID))
            .TopN(1)
            .FirstOrDefault();

    internal static SentinelSettingsSnapshot ToSnapshot(SentinelSettingsOverrideInfo row) => new(
        Enabled: row.SentinelSettingsOverrideEnabled,
        ExcludedChecks: ParseStringList(row.SentinelSettingsOverrideExcludedChecks),
        StaleDays: row.SentinelSettingsOverrideStaleDays,
        EventLogDays: row.SentinelSettingsOverrideEventLogDays,
        EventLogEnabled: row.SentinelSettingsOverrideEventLogEnabled,
        EventLogSeverityThreshold: row.SentinelSettingsOverrideEventLogSeverityThreshold,
        EventLogMaxEntriesPerScan: row.SentinelSettingsOverrideEventLogMaxEntriesPerScan,
        EmailDigestEnabled: row.SentinelSettingsOverrideEmailDigestEnabled,
        EmailDigestRecipients: ParseStringList(row.SentinelSettingsOverrideEmailDigestRecipients),
        EmailDigestSeverityThreshold: row.SentinelSettingsOverrideEmailDigestSeverityThreshold,
        EmailDigestOnlyWhenThresholdFindings: row.SentinelSettingsOverrideEmailDigestOnlyWhenThresholdFindings);

    /// <summary>
    /// Parses a JSON-serialized string array, tolerating empty / null / malformed input by
    /// returning an empty list. A corrupted JSON column shouldn't brick the whole options
    /// resolve — degrading to "no override for this field" is safer than throwing.
    /// </summary>
    internal static IReadOnlyList<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }
        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOpts) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
