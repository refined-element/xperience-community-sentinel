using System.Data;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XperienceCommunity.Sentinel.Module.InfoModels.SentinelFindingAck;

[assembly: RegisterObjectType(typeof(SentinelFindingAckInfo), SentinelFindingAckInfo.OBJECT_TYPE)]

namespace XperienceCommunity.Sentinel.Module.InfoModels.SentinelFindingAck;

/// <summary>
/// Persistent acknowledgment/dismissal/snooze state keyed by finding fingerprint rather than by a
/// specific SentinelFindingID. This way the state survives across scan runs: if a user dismisses
/// a finding once, it stays dismissed when the same underlying issue is re-discovered next scan.
/// </summary>
public partial class SentinelFindingAckInfo : AbstractInfo<SentinelFindingAckInfo, IInfoProvider<SentinelFindingAckInfo>>
{
    public const string OBJECT_TYPE = "xperiencecommunity.sentinelfindingack";

    public static readonly ObjectTypeInfo TYPEINFO = new(
        typeof(IInfoProvider<SentinelFindingAckInfo>),
        OBJECT_TYPE,
        "XperienceCommunity.SentinelFindingAck",
        nameof(SentinelFindingAckID),
        null,
        nameof(SentinelFindingAckGuid),
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
    public virtual int SentinelFindingAckID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelFindingAckID)), 0);
        set => SetValue(nameof(SentinelFindingAckID), value);
    }

    [DatabaseField]
    public virtual Guid SentinelFindingAckGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(SentinelFindingAckGuid)), default);
        set => SetValue(nameof(SentinelFindingAckGuid), value);
    }

    /// <summary>Matches <see cref="SentinelFindingInfo.SentinelFindingFingerprintHash"/>.</summary>
    [DatabaseField]
    public virtual string SentinelFindingAckFingerprintHash
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelFindingAckFingerprintHash)), string.Empty);
        set => SetValue(nameof(SentinelFindingAckFingerprintHash), value);
    }

    /// <summary>"Acknowledged" | "Snoozed" — matches <see cref="XperienceCommunity.Sentinel.Module.Acknowledgment.AckState"/>; no "Dismissed" state.</summary>
    [DatabaseField]
    public virtual string SentinelFindingAckState
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelFindingAckState)), string.Empty);
        set => SetValue(nameof(SentinelFindingAckState), value);
    }

    [DatabaseField]
    public virtual DateTime SentinelFindingAckSnoozeUntil
    {
        get => ValidationHelper.GetDateTime(GetValue(nameof(SentinelFindingAckSnoozeUntil)), default);
        set => SetValue(nameof(SentinelFindingAckSnoozeUntil), value);
    }

    [DatabaseField]
    public virtual int SentinelFindingAckUserID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelFindingAckUserID)), 0);
        set => SetValue(nameof(SentinelFindingAckUserID), value);
    }

    [DatabaseField]
    public virtual DateTime SentinelFindingAckAckedAt
    {
        get => ValidationHelper.GetDateTime(GetValue(nameof(SentinelFindingAckAckedAt)), default);
        set => SetValue(nameof(SentinelFindingAckAckedAt), value);
    }

    [DatabaseField]
    public virtual string SentinelFindingAckNote
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelFindingAckNote)), string.Empty);
        set => SetValue(nameof(SentinelFindingAckNote), value);
    }

    protected override void DeleteObject() => Provider.Delete(this);
    protected override void SetObject() => Provider.Set(this);

    public SentinelFindingAckInfo() : base(TYPEINFO) { }
    public SentinelFindingAckInfo(DataRow dr) : base(TYPEINFO, dr) { }
}
