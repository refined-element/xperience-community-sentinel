namespace XperienceCommunity.Sentinel.Module.SettingsOverride;

/// <summary>
/// Plain snapshot of every editable-from-UI setting. Used for both the save payload (admin ->
/// override store) and the current-value read (override store -> admin UI). Types here are the
/// typed forms we want the React client to consume, not the raw DB column shapes (recipients
/// as a list, not a JSON blob; severity as enum-named strings, not arbitrary text).
///
/// <para>
/// "All or nothing" semantics: when an override is active, every field here wins over the
/// corresponding <c>SentinelOptions</c> property. When no override is active (table empty),
/// appsettings.json / env vars / delegate-overrides flow through untouched.
/// </para>
/// </summary>
public sealed record SentinelSettingsSnapshot(
    bool Enabled,
    IReadOnlyList<string> ExcludedChecks,
    int StaleDays,
    int EventLogDays,
    bool EventLogEnabled,
    string EventLogSeverityThreshold,
    int EventLogMaxEntriesPerScan,
    bool EmailDigestEnabled,
    IReadOnlyList<string> EmailDigestRecipients,
    string EmailDigestSeverityThreshold,
    bool EmailDigestOnlyWhenThresholdFindings);
