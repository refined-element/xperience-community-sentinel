using System.Data;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XperienceCommunity.Sentinel.Module.InfoModels.SentinelScanRun;

[assembly: RegisterObjectType(typeof(SentinelScanRunInfo), SentinelScanRunInfo.OBJECT_TYPE)]

namespace XperienceCommunity.Sentinel.Module.InfoModels.SentinelScanRun;

/// <summary>
/// One row per scan execution — the header record that owns a set of <see cref="SentinelFindingInfo"/>s.
/// </summary>
public partial class SentinelScanRunInfo : AbstractInfo<SentinelScanRunInfo, IInfoProvider<SentinelScanRunInfo>>
{
    public const string OBJECT_TYPE = "xperiencecommunity.sentinelscanrun";

    public static readonly ObjectTypeInfo TYPEINFO = new(
        typeof(IInfoProvider<SentinelScanRunInfo>),
        OBJECT_TYPE,
        "XperienceCommunity.SentinelScanRun",
        nameof(SentinelScanRunID),
        null,
        nameof(SentinelScanRunGuid),
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
    public virtual int SentinelScanRunID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelScanRunID)), 0);
        set => SetValue(nameof(SentinelScanRunID), value);
    }

    [DatabaseField]
    public virtual Guid SentinelScanRunGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(SentinelScanRunGuid)), default);
        set => SetValue(nameof(SentinelScanRunGuid), value);
    }

    [DatabaseField]
    public virtual DateTime SentinelScanRunStartedAt
    {
        get => ValidationHelper.GetDateTime(GetValue(nameof(SentinelScanRunStartedAt)), default);
        set => SetValue(nameof(SentinelScanRunStartedAt), value);
    }

    [DatabaseField]
    public virtual DateTime SentinelScanRunCompletedAt
    {
        get => ValidationHelper.GetDateTime(GetValue(nameof(SentinelScanRunCompletedAt)), default);
        set => SetValue(nameof(SentinelScanRunCompletedAt), value);
    }

    /// <summary>"Scheduled" | "Manual" | "Startup" — source that initiated the scan.</summary>
    [DatabaseField]
    public virtual string SentinelScanRunTrigger
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelScanRunTrigger)), string.Empty);
        set => SetValue(nameof(SentinelScanRunTrigger), value);
    }

    [DatabaseField]
    public virtual string SentinelScanRunSentinelVersion
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelScanRunSentinelVersion)), string.Empty);
        set => SetValue(nameof(SentinelScanRunSentinelVersion), value);
    }

    [DatabaseField]
    public virtual int SentinelScanRunTotalFindings
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelScanRunTotalFindings)), 0);
        set => SetValue(nameof(SentinelScanRunTotalFindings), value);
    }

    [DatabaseField]
    public virtual int SentinelScanRunErrorCount
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelScanRunErrorCount)), 0);
        set => SetValue(nameof(SentinelScanRunErrorCount), value);
    }

    [DatabaseField]
    public virtual int SentinelScanRunWarningCount
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelScanRunWarningCount)), 0);
        set => SetValue(nameof(SentinelScanRunWarningCount), value);
    }

    [DatabaseField]
    public virtual int SentinelScanRunInfoCount
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelScanRunInfoCount)), 0);
        set => SetValue(nameof(SentinelScanRunInfoCount), value);
    }

    [DatabaseField]
    public virtual decimal SentinelScanRunDurationSeconds
    {
        get => ValidationHelper.GetDecimal(GetValue(nameof(SentinelScanRunDurationSeconds)), 0m);
        set => SetValue(nameof(SentinelScanRunDurationSeconds), value);
    }

    /// <summary>"Running" | "Completed" | "Failed" | "Cancelled".</summary>
    [DatabaseField]
    public virtual string SentinelScanRunStatus
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelScanRunStatus)), string.Empty);
        set => SetValue(nameof(SentinelScanRunStatus), value);
    }

    [DatabaseField]
    public virtual string SentinelScanRunErrorMessage
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelScanRunErrorMessage)), string.Empty);
        set => SetValue(nameof(SentinelScanRunErrorMessage), value);
    }

    protected override void DeleteObject() => Provider.Delete(this);
    protected override void SetObject() => Provider.Set(this);

    public SentinelScanRunInfo() : base(TYPEINFO) { }
    public SentinelScanRunInfo(DataRow dr) : base(TYPEINFO, dr) { }
}
