using CMS.DataEngine;
using CMS.Membership;
using CMS.Scheduler;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.Authentication;
using Kentico.Xperience.Admin.Base.UIPages;

using Microsoft.Extensions.Options;

using XperienceCommunity.Sentinel.Core;
using XperienceCommunity.Sentinel.Quoting;
using XperienceCommunity.Sentinel.Admin;
using XperienceCommunity.Sentinel.Admin.UIPages;
using XperienceCommunity.Sentinel.Module.Configuration;
using XperienceCommunity.Sentinel.Module.Scheduling;
using XperienceCommunity.Sentinel.Module.SettingsOverride;

[assembly: UIPage(
    parentType: typeof(SentinelApplicationPage),
    slug: "settings",
    uiPageType: typeof(SentinelSettingsPage),
    name: "Settings",
    templateName: "@xperiencecommunity/sentinel-admin/Settings",
    order: 400)]

namespace XperienceCommunity.Sentinel.Admin.UIPages;

/// <summary>
/// Admin page for the Sentinel configuration. A curated subset of <see cref="SentinelOptions"/>
/// is editable inline — Save writes to the <c>SentinelSettingsOverride</c> row which the
/// <see cref="SentinelOptionsOverrideApplier"/> <c>PostConfigure</c> layers on top of the base
/// options. Changes apply on the next scan without an app restart.
///
/// <para>
/// What's editable:
/// <list type="bullet">
///   <item>Master <c>Enabled</c> switch</item>
///   <item><c>Checks.Excluded</c> — comma-separated list of rule IDs to skip</item>
///   <item><c>RuntimeChecks.StaleDays</c> / <c>EventLogDays</c></item>
///   <item><c>EventLogIntegration.*</c> — Enabled, SeverityThreshold, MaxEntriesPerScan</item>
///   <item><c>EmailDigest.*</c> — Enabled, Recipients (list), SeverityThreshold, OnlyWhenThreshold</item>
///   <item>Scheduled-task cadence (via preset dropdown; writes to <c>CMS_ScheduledTaskConfiguration</c>)</item>
/// </list>
/// </para>
///
/// <para>
/// What's locked (config-file only):
/// <list type="bullet">
///   <item><c>RuntimeChecks.ConnectionString</c> — security: a UI-editable connection string is a privilege-escalation vector</item>
///   <item><c>Contact.Endpoint</c> — operational: changing this mid-flight breaks outstanding quote submissions</item>
/// </list>
/// </para>
/// </summary>
[UIEvaluatePermission(SystemPermissions.VIEW)]
public class SentinelSettingsPage(
    IOptionsSnapshot<SentinelOptions> options,
    ISentinelSettingsOverrideStore overrideStore,
    IInfoProvider<ScheduledTaskConfigurationInfo> scheduledTaskProvider,
    IAuthenticatedUserAccessor userAccessor)
    : Page<SettingsClientProperties>
{
    // Preset cadence options the admin sees as a dropdown. Values are the Kentico
    // pipe-delimited format (matching the SchedulingHelper output format — see
    // SentinelModuleInstaller.DefaultDailyInterval for the daily default). Labels are
    // human-facing. An operator wanting an exotic cadence (e.g. every 37 minutes) still
    // clicks through to the Scheduled Tasks admin app; this covers 95% of real cases.
    internal static readonly IReadOnlyList<SchedulePresetDto> SchedulePresets =
    [
        new("hour;1;00:00:00", "Every hour"),
        new("hour;6;00:00:00", "Every 6 hours"),
        new("hour;12;00:00:00", "Every 12 hours"),
        new("day;1;00:00:00", "Every day"),
        new("day;7;00:00:00", "Every week"),
    ];

    public override async Task<SettingsClientProperties> ConfigureTemplateProperties(SettingsClientProperties properties)
    {
        var opts = options.Value;
        PopulateEditableSettings(properties, opts);
        PopulateReadOnlyInfo(properties, opts);
        PopulateSchedule(properties);
        PopulateRuleCatalog(properties);

        var user = await userAccessor.Get();
        properties.CurrentUserId = user?.UserID ?? 0;

        return properties;
    }

    /// <summary>
    /// Save handler for the editable form. Validates the incoming snapshot, writes to the
    /// override store, and returns the effective snapshot back so the client can reconcile
    /// state (e.g., a malformed list silently normalized — no surprises, what's shown is
    /// what applies).
    /// </summary>
    [PageCommand(Permission = SystemPermissions.UPDATE)]
    public async Task<ICommandResponse<SaveSettingsResponse>> SaveSettings(SaveSettingsData data)
    {
        if (data is null)
        {
            return ResponseFrom(new SaveSettingsResponse { Success = false, Message = "No settings payload." });
        }

        var (valid, error) = ValidateAndNormalize(data);
        if (!valid || error is not null)
        {
            return ResponseFrom(new SaveSettingsResponse { Success = false, Message = error ?? "Validation failed." });
        }

        var user = await userAccessor.Get();
        if (user is null)
        {
            return ResponseFrom(new SaveSettingsResponse
            {
                Success = false,
                Message = "Cannot save settings without an authenticated admin user.",
            });
        }

        var snapshot = new SentinelSettingsSnapshot(
            Enabled: data.Enabled,
            ExcludedChecks: data.ExcludedChecks ?? Array.Empty<string>(),
            StaleDays: data.StaleDays,
            EventLogDays: data.EventLogDays,
            EventLogEnabled: data.EventLogEnabled,
            EventLogSeverityThreshold: data.EventLogSeverityThreshold,
            EventLogMaxEntriesPerScan: data.EventLogMaxEntriesPerScan,
            EmailDigestEnabled: data.EmailDigestEnabled,
            EmailDigestRecipients: data.EmailDigestRecipients ?? Array.Empty<string>(),
            EmailDigestSeverityThreshold: data.EmailDigestSeverityThreshold,
            EmailDigestOnlyWhenThresholdFindings: data.EmailDigestOnlyWhenThresholdFindings);
        overrideStore.Save(snapshot, user.UserID);

        // Apply the schedule preset too — pipe-delimited interval + enabled flag on the
        // scheduled-task row. Write failure logs but doesn't fail the overall Save, because
        // settings-override wrote successfully and the operator shouldn't have to re-click.
        var scheduleMessage = TryApplySchedulePreset(data.ScheduleIntervalRaw, data.ScheduleEnabled);

        return ResponseFrom(new SaveSettingsResponse
        {
            Success = true,
            Message = scheduleMessage is null
                ? "Settings saved. New values apply on the next scan."
                : $"Settings saved. New values apply on the next scan. Note: {scheduleMessage}",
        });
    }

    /// <summary>
    /// Clears the override row, reverting SentinelOptions to its appsettings.json / env-var
    /// / delegate-overload values. Useful when an admin wants to "roll back" a UI-driven
    /// configuration change without knowing the original values by heart.
    /// </summary>
    [PageCommand(Permission = SystemPermissions.UPDATE)]
    public Task<ICommandResponse<SaveSettingsResponse>> ResetToDefaults()
    {
        // Kentico's [PageCommand] dispatch requires async signatures — a synchronous return
        // type blows up with NotSupportedException at request time. No await needed internally
        // (the store's Clear is sync over an IInfoProvider), so we wrap in Task.FromResult.
        overrideStore.Clear();
        return Task.FromResult(ResponseFrom(new SaveSettingsResponse
        {
            Success = true,
            Message = "Override cleared. Settings reverted to appsettings values on the next scan.",
        }));
    }

    private void PopulateEditableSettings(SettingsClientProperties p, SentinelOptions opts)
    {
        p.Enabled = opts.Enabled;
        p.ExcludedChecks = opts.Checks.Excluded.ToArray();
        p.StaleDays = opts.RuntimeChecks.StaleDays;
        p.EventLogDays = opts.RuntimeChecks.EventLogDays;
        p.EmailDigestEnabled = opts.EmailDigest.Enabled;
        p.EmailDigestRecipients = opts.EmailDigest.Recipients.ToArray();
        p.EmailDigestSeverityThreshold = opts.EmailDigest.SeverityThreshold;
        p.EmailDigestOnlyWhenThresholdFindings = opts.EmailDigest.OnlyWhenThresholdFindings;
        p.EventLogEnabled = opts.EventLogIntegration.Enabled;
        p.EventLogSeverityThreshold = opts.EventLogIntegration.SeverityThreshold;
        p.EventLogMaxEntriesPerScan = opts.EventLogIntegration.MaxEntriesPerScan;
        p.HasOverride = overrideStore.Get() is not null;
    }

    private static void PopulateReadOnlyInfo(SettingsClientProperties p, SentinelOptions opts)
    {
        p.RuntimeConnectionString = string.IsNullOrWhiteSpace(opts.RuntimeChecks.ConnectionString)
            ? "(empty — falling back to CMSConnectionString)"
            : "(configured — value redacted)";
        p.ContactEndpoint = !string.IsNullOrWhiteSpace(opts.Contact.Endpoint)
            ? opts.Contact.Endpoint
            : QuoteClient.DefaultEndpoint;
        p.ContactIncludeContextByDefault = opts.Contact.IncludeContextByDefault;
        // Direct admin app URL; see SentinelDashboardPage for rationale.
        p.ScheduledTasksUrl = "/admin/scheduled-tasks";
    }

    private void PopulateSchedule(SettingsClientProperties p)
    {
        p.SchedulePresets = SchedulePresets.ToArray();

        var task = scheduledTaskProvider.Get()
            .WhereEquals(nameof(ScheduledTaskConfigurationInfo.ScheduledTaskConfigurationScheduledTaskIdentifier), SentinelScanTask.TaskName)
            .TopN(1)
            .FirstOrDefault();
        if (task is null)
        {
            p.ScheduleState = "missing";
            p.ScheduleEnabled = false;
            p.ScheduleIntervalRaw = string.Empty;
            p.ScheduleIntervalHint = "No scheduled task row found — automated scans are not running. Create one in Scheduled tasks or save settings here to let Sentinel pick the default.";
            p.ScheduleLastRunUtc = null;
            p.ScheduleNextRunUtc = null;
            return;
        }

        p.ScheduleState = task.ScheduledTaskConfigurationEnabled ? "enabled" : "disabled";
        p.ScheduleEnabled = task.ScheduledTaskConfigurationEnabled;
        p.ScheduleIntervalRaw = task.ScheduledTaskConfigurationInterval ?? string.Empty;
        p.ScheduleIntervalHint = HumanizeInterval(task.ScheduledTaskConfigurationInterval);
        p.ScheduleLastRunUtc = task.ScheduledTaskConfigurationLastRunTime == default
            ? null
            : DateTime.SpecifyKind(task.ScheduledTaskConfigurationLastRunTime, DateTimeKind.Utc).ToString("O");
        p.ScheduleNextRunUtc = task.ScheduledTaskConfigurationNextRunTime == default
            ? null
            : DateTime.SpecifyKind(task.ScheduledTaskConfigurationNextRunTime, DateTimeKind.Utc).ToString("O");
    }

    private static void PopulateRuleCatalog(SettingsClientProperties p)
    {
        // Surface every known rule so the client can render the "Excluded checks" multi-select
        // as a recognizable list of checks (rule ID + title) rather than asking the admin to
        // type rule IDs by hand. Source-of-truth is the registered check set in CheckRegistry.
        p.KnownRules = CheckRegistry.BuiltIn()
            .Select(c => new RuleDto { RuleId = c.RuleId, Title = c.Title, Category = c.Category })
            .OrderBy(r => r.Category)
            .ThenBy(r => r.RuleId)
            .ToArray();
    }

    private (bool valid, string? error) ValidateAndNormalize(SaveSettingsData data)
    {
        if (data.StaleDays < 1 || data.StaleDays > 3650)
        {
            return (false, "Stale content threshold must be between 1 and 3650 days.");
        }
        if (data.EventLogDays < 1 || data.EventLogDays > 3650)
        {
            return (false, "Event log recency window must be between 1 and 3650 days.");
        }
        if (data.EventLogMaxEntriesPerScan < 0 || data.EventLogMaxEntriesPerScan > 10_000)
        {
            return (false, "Event log max entries per scan must be between 0 and 10000.");
        }
        if (!IsValidSeverity(data.EventLogSeverityThreshold))
        {
            return (false, "Event log severity threshold must be Info, Warning, or Error.");
        }
        if (!IsValidSeverity(data.EmailDigestSeverityThreshold))
        {
            return (false, "Email digest severity threshold must be Info, Warning, or Error.");
        }
        foreach (var email in data.EmailDigestRecipients ?? Array.Empty<string>())
        {
            if (!System.Net.Mail.MailAddress.TryCreate(email, out _))
            {
                return (false, $"Recipient '{email}' is not a valid email address.");
            }
        }
        foreach (var ruleId in data.ExcludedChecks ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(ruleId) || ruleId.Length > 32)
            {
                return (false, $"Rule ID '{ruleId}' is invalid — must be 1–32 non-blank characters.");
            }
        }
        // Schedule validation accepts three shapes:
        //   - empty / null → "don't change cadence" (admin is only editing other settings)
        //   - a preset value → trivially valid
        //   - the CURRENT db value (even if custom, e.g. hand-tuned 'day;3;00:00:00') → valid
        //     so operators with an existing custom cadence can Save without being forced onto
        //     a preset. Anything else is rejected to avoid arbitrary free-text injection.
        if (!string.IsNullOrWhiteSpace(data.ScheduleIntervalRaw))
        {
            var isPreset = SchedulePresets.Any(p => p.IntervalRaw == data.ScheduleIntervalRaw);
            var isCurrentDbValue = GetCurrentScheduleIntervalRaw() == data.ScheduleIntervalRaw;
            if (!isPreset && !isCurrentDbValue)
            {
                return (false, "Schedule preset must match a known preset or the current scheduled-task interval.");
            }
        }
        return (true, null);
    }

    private string? GetCurrentScheduleIntervalRaw() =>
        scheduledTaskProvider.Get()
            .WhereEquals(nameof(ScheduledTaskConfigurationInfo.ScheduledTaskConfigurationScheduledTaskIdentifier), SentinelScanTask.TaskName)
            .TopN(1)
            .FirstOrDefault()?
            .ScheduledTaskConfigurationInterval;

    private string? TryApplySchedulePreset(string? intervalRaw, bool enabled)
    {
        try
        {
            var task = scheduledTaskProvider.Get()
                .WhereEquals(nameof(ScheduledTaskConfigurationInfo.ScheduledTaskConfigurationScheduledTaskIdentifier), SentinelScanTask.TaskName)
                .TopN(1)
                .FirstOrDefault();
            if (task is null)
            {
                // No task row yet — create one using the admin's chosen cadence (or a daily
                // default if they left the field empty). Matches the UI hint "save settings
                // here to let Sentinel pick the default" — skipping the INSERT would have
                // been a lie. See SentinelModuleInstaller.DefaultDailyInterval for the rest
                // of the NOT NULL columns we populate.
                task = new ScheduledTaskConfigurationInfo
                {
                    ScheduledTaskConfigurationName = SentinelScanTask.TaskName,
                    ScheduledTaskConfigurationDisplayName = "Sentinel scan",
                    ScheduledTaskConfigurationScheduledTaskIdentifier = SentinelScanTask.TaskName,
                    ScheduledTaskConfigurationEnabled = enabled,
                    ScheduledTaskConfigurationDeleteAfterLastRun = false,
                    ScheduledTaskConfigurationInterval = string.IsNullOrWhiteSpace(intervalRaw) ? "day;1;00:00:00" : intervalRaw,
                    ScheduledTaskConfigurationData = string.Empty,
                    ScheduledTaskConfigurationGUID = Guid.NewGuid(),
                    ScheduledTaskConfigurationLastModified = DateTime.UtcNow,
                };
                scheduledTaskProvider.Set(task);
                return null;
            }
            if (!string.IsNullOrWhiteSpace(intervalRaw))
            {
                task.ScheduledTaskConfigurationInterval = intervalRaw;
            }
            task.ScheduledTaskConfigurationEnabled = enabled;
            scheduledTaskProvider.Set(task);
            return null;
        }
        catch (Exception ex)
        {
            return $"Schedule update failed — {ex.GetType().Name}. Edit it manually in Scheduled tasks.";
        }
    }

    private static bool IsValidSeverity(string? value) =>
        value is "Info" or "Warning" or "Error";

    /// <summary>
    /// See the previous-revision comment block for the full rationale — "every day" / "every 3
    /// days" / "runs once", pipe-delimited parsing limited to the first two fields.
    /// </summary>
    private static string HumanizeInterval(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "(no interval configured)";
        }
        var parts = raw.Split(';');
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
        {
            return $"raw: {raw}";
        }
        var period = parts[0].ToLowerInvariant();
        var every = int.TryParse(parts[1], out var n) ? n : 0;
        if (every <= 0)
        {
            return $"raw: {raw}";
        }
        if (period == "once")
        {
            return "runs once";
        }
        var unit = period switch
        {
            "second" => "second",
            "minute" => "minute",
            "hour" => "hour",
            "day" => "day",
            "week" => "week",
            "month" => "month",
            "year" => "year",
            _ => period,
        };
        return every == 1 ? $"every {unit}" : $"every {every} {unit}s";
    }
}

public sealed class SettingsClientProperties : TemplateClientProperties
{
    // Editable
    public bool Enabled { get; set; }
    public string[] ExcludedChecks { get; set; } = Array.Empty<string>();
    public int StaleDays { get; set; }
    public int EventLogDays { get; set; }
    public bool EmailDigestEnabled { get; set; }
    public string[] EmailDigestRecipients { get; set; } = Array.Empty<string>();
    public string EmailDigestSeverityThreshold { get; set; } = string.Empty;
    public bool EmailDigestOnlyWhenThresholdFindings { get; set; }
    public bool EventLogEnabled { get; set; }
    public string EventLogSeverityThreshold { get; set; } = string.Empty;
    public int EventLogMaxEntriesPerScan { get; set; }

    // Indicators
    /// <summary>True when an override row exists (UI shows "overridden from file" chip).</summary>
    public bool HasOverride { get; set; }
    public int CurrentUserId { get; set; }

    // Read-only (for display only)
    public string RuntimeConnectionString { get; set; } = string.Empty;
    public string ContactEndpoint { get; set; } = string.Empty;
    public bool ContactIncludeContextByDefault { get; set; }
    public string ScheduledTasksUrl { get; set; } = string.Empty;

    // Schedule — editable (via preset dropdown) + read-back
    public SchedulePresetDto[] SchedulePresets { get; set; } = Array.Empty<SchedulePresetDto>();
    public bool ScheduleEnabled { get; set; }
    public string ScheduleState { get; set; } = "missing";
    public string ScheduleIntervalRaw { get; set; } = string.Empty;
    public string ScheduleIntervalHint { get; set; } = string.Empty;
    public string? ScheduleLastRunUtc { get; set; }
    public string? ScheduleNextRunUtc { get; set; }

    // Catalog — for the "Excluded checks" multi-select.
    public RuleDto[] KnownRules { get; set; } = Array.Empty<RuleDto>();
}

public sealed record SchedulePresetDto(string IntervalRaw, string Label);

public sealed class RuleDto
{
    public string RuleId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public sealed class SaveSettingsData
{
    public bool Enabled { get; set; }
    public string[]? ExcludedChecks { get; set; }
    public int StaleDays { get; set; }
    public int EventLogDays { get; set; }
    public bool EventLogEnabled { get; set; }
    public string EventLogSeverityThreshold { get; set; } = string.Empty;
    public int EventLogMaxEntriesPerScan { get; set; }
    public bool EmailDigestEnabled { get; set; }
    public string[]? EmailDigestRecipients { get; set; }
    public string EmailDigestSeverityThreshold { get; set; } = string.Empty;
    public bool EmailDigestOnlyWhenThresholdFindings { get; set; }
    // Schedule
    public bool ScheduleEnabled { get; set; }
    public string? ScheduleIntervalRaw { get; set; }
}

public sealed class SaveSettingsResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
