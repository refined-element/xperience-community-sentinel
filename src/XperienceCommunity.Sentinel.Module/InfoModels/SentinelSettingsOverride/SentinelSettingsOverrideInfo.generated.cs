using System.Data;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XperienceCommunity.Sentinel.Module.InfoModels.SentinelSettingsOverride;

[assembly: RegisterObjectType(typeof(SentinelSettingsOverrideInfo), SentinelSettingsOverrideInfo.OBJECT_TYPE)]

namespace XperienceCommunity.Sentinel.Module.InfoModels.SentinelSettingsOverride;

/// <summary>
/// Single-row "override" state layered on top of <c>SentinelOptions</c> so admins can tune
/// the live config from the Settings admin page without redeploying. The DI container's
/// <c>PostConfigure&lt;SentinelOptions&gt;</c> reads this row on every options resolve and
/// overwrites the corresponding properties.
///
/// <para>
/// Semantics: "all-or-nothing" — a Save from the admin UI persists the full editable snapshot
/// (every column non-null), and PostConfigure applies all columns atomically. There's no
/// per-field "HasValue" flag because a half-applied override is worse UX than "edit them all
/// together or don't edit at all". No row = no overrides; the appsettings.json / env-var
/// / delegate-overload chain wins.
/// </para>
///
/// <para>
/// Single-row invariant is enforced at the service layer: the store always operates on the row
/// with the smallest ID, and the installer doesn't seed a default row (absence = no override).
/// </para>
/// </summary>
public partial class SentinelSettingsOverrideInfo : AbstractInfo<SentinelSettingsOverrideInfo, IInfoProvider<SentinelSettingsOverrideInfo>>
{
    public const string OBJECT_TYPE = "xperiencecommunity.sentinelsettingsoverride";

    public static readonly ObjectTypeInfo TYPEINFO = new(
        typeof(IInfoProvider<SentinelSettingsOverrideInfo>),
        OBJECT_TYPE,
        "XperienceCommunity.SentinelSettingsOverride",
        nameof(SentinelSettingsOverrideID),
        null,
        nameof(SentinelSettingsOverrideGuid),
        null,
        null,
        null,
        null,
        null)
    {
        TouchCacheDependencies = true,
        ContinuousIntegrationSettings = { Enabled = false },
    };

    [DatabaseField]
    public virtual int SentinelSettingsOverrideID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelSettingsOverrideID)), 0);
        set => SetValue(nameof(SentinelSettingsOverrideID), value);
    }

    [DatabaseField]
    public virtual Guid SentinelSettingsOverrideGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(SentinelSettingsOverrideGuid)), default);
        set => SetValue(nameof(SentinelSettingsOverrideGuid), value);
    }

    /// <summary>Master switch — maps to <c>SentinelOptions.Enabled</c>.</summary>
    [DatabaseField]
    public virtual bool SentinelSettingsOverrideEnabled
    {
        get => ValidationHelper.GetBoolean(GetValue(nameof(SentinelSettingsOverrideEnabled)), false);
        set => SetValue(nameof(SentinelSettingsOverrideEnabled), value);
    }

    /// <summary>JSON array of rule IDs to exclude — maps to <c>SentinelOptions.Checks.Excluded</c>.</summary>
    [DatabaseField]
    public virtual string SentinelSettingsOverrideExcludedChecks
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelSettingsOverrideExcludedChecks)), string.Empty);
        set => SetValue(nameof(SentinelSettingsOverrideExcludedChecks), value);
    }

    /// <summary>Maps to <c>SentinelOptions.RuntimeChecks.StaleDays</c>. Default 180 in appsettings.</summary>
    [DatabaseField]
    public virtual int SentinelSettingsOverrideStaleDays
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelSettingsOverrideStaleDays)), 0);
        set => SetValue(nameof(SentinelSettingsOverrideStaleDays), value);
    }

    /// <summary>Maps to <c>SentinelOptions.RuntimeChecks.EventLogDays</c>.</summary>
    [DatabaseField]
    public virtual int SentinelSettingsOverrideEventLogDays
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelSettingsOverrideEventLogDays)), 0);
        set => SetValue(nameof(SentinelSettingsOverrideEventLogDays), value);
    }

    /// <summary>Maps to <c>SentinelOptions.EventLogIntegration.Enabled</c>.</summary>
    [DatabaseField]
    public virtual bool SentinelSettingsOverrideEventLogEnabled
    {
        get => ValidationHelper.GetBoolean(GetValue(nameof(SentinelSettingsOverrideEventLogEnabled)), false);
        set => SetValue(nameof(SentinelSettingsOverrideEventLogEnabled), value);
    }

    /// <summary>"Info" | "Warning" | "Error" — maps to <c>SentinelOptions.EventLogIntegration.SeverityThreshold</c>.</summary>
    [DatabaseField]
    public virtual string SentinelSettingsOverrideEventLogSeverityThreshold
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelSettingsOverrideEventLogSeverityThreshold)), string.Empty);
        set => SetValue(nameof(SentinelSettingsOverrideEventLogSeverityThreshold), value);
    }

    /// <summary>Maps to <c>SentinelOptions.EventLogIntegration.MaxEntriesPerScan</c>.</summary>
    [DatabaseField]
    public virtual int SentinelSettingsOverrideEventLogMaxEntriesPerScan
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelSettingsOverrideEventLogMaxEntriesPerScan)), 0);
        set => SetValue(nameof(SentinelSettingsOverrideEventLogMaxEntriesPerScan), value);
    }

    /// <summary>Maps to <c>SentinelOptions.EmailDigest.Enabled</c>.</summary>
    [DatabaseField]
    public virtual bool SentinelSettingsOverrideEmailDigestEnabled
    {
        get => ValidationHelper.GetBoolean(GetValue(nameof(SentinelSettingsOverrideEmailDigestEnabled)), false);
        set => SetValue(nameof(SentinelSettingsOverrideEmailDigestEnabled), value);
    }

    /// <summary>JSON array of email strings — maps to <c>SentinelOptions.EmailDigest.Recipients</c>.</summary>
    [DatabaseField]
    public virtual string SentinelSettingsOverrideEmailDigestRecipients
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelSettingsOverrideEmailDigestRecipients)), string.Empty);
        set => SetValue(nameof(SentinelSettingsOverrideEmailDigestRecipients), value);
    }

    /// <summary>"Info" | "Warning" | "Error" — maps to <c>SentinelOptions.EmailDigest.SeverityThreshold</c>.</summary>
    [DatabaseField]
    public virtual string SentinelSettingsOverrideEmailDigestSeverityThreshold
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelSettingsOverrideEmailDigestSeverityThreshold)), string.Empty);
        set => SetValue(nameof(SentinelSettingsOverrideEmailDigestSeverityThreshold), value);
    }

    /// <summary>Maps to <c>SentinelOptions.EmailDigest.OnlyWhenThresholdFindings</c>.</summary>
    [DatabaseField]
    public virtual bool SentinelSettingsOverrideEmailDigestOnlyWhenThresholdFindings
    {
        get => ValidationHelper.GetBoolean(GetValue(nameof(SentinelSettingsOverrideEmailDigestOnlyWhenThresholdFindings)), false);
        set => SetValue(nameof(SentinelSettingsOverrideEmailDigestOnlyWhenThresholdFindings), value);
    }

    /// <summary>CMS user ID that last saved the override — audit trail.</summary>
    [DatabaseField]
    public virtual int SentinelSettingsOverrideLastModifiedBy
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelSettingsOverrideLastModifiedBy)), 0);
        set => SetValue(nameof(SentinelSettingsOverrideLastModifiedBy), value);
    }

    /// <summary>UTC timestamp of the last save — paired with LastModifiedBy.</summary>
    [DatabaseField]
    public virtual DateTime SentinelSettingsOverrideLastModifiedAt
    {
        get => ValidationHelper.GetDateTime(GetValue(nameof(SentinelSettingsOverrideLastModifiedAt)), default);
        set => SetValue(nameof(SentinelSettingsOverrideLastModifiedAt), value);
    }

    protected override void DeleteObject() => Provider.Delete(this);
    protected override void SetObject() => Provider.Set(this);

    public SentinelSettingsOverrideInfo() : base(TYPEINFO) { }
    public SentinelSettingsOverrideInfo(DataRow dr) : base(TYPEINFO, dr) { }
}
