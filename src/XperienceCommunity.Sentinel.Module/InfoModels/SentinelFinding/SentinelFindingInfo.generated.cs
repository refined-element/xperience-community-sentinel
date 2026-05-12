using System.Data;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XperienceCommunity.Sentinel.Module.InfoModels.SentinelFinding;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelScanRun;

[assembly: RegisterObjectType(typeof(SentinelFindingInfo), SentinelFindingInfo.OBJECT_TYPE)]

namespace XperienceCommunity.Sentinel.Module.InfoModels.SentinelFinding;

/// <summary>
/// One row per finding produced by a scan. FKs to the owning <see cref="SentinelScanRunInfo"/>.
/// Cascades on scan-run delete (so if an admin prunes scan history, findings clean up too).
/// </summary>
public partial class SentinelFindingInfo : AbstractInfo<SentinelFindingInfo, IInfoProvider<SentinelFindingInfo>>
{
    public const string OBJECT_TYPE = "xperiencecommunity.sentinelfinding";

    public static readonly ObjectTypeInfo TYPEINFO = new(
        typeof(IInfoProvider<SentinelFindingInfo>),
        OBJECT_TYPE,
        "XperienceCommunity.SentinelFinding",
        nameof(SentinelFindingID),
        null,
        nameof(SentinelFindingGuid),
        null,
        null,
        null,
        null,
        null)
    {
        TouchCacheDependencies = true,
        DependsOn = new List<ObjectDependency>
        {
            new(nameof(SentinelFindingScanRunID), SentinelScanRunInfo.OBJECT_TYPE, ObjectDependencyEnum.Required),
        },
        ContinuousIntegrationSettings = { Enabled = false },
    };

    [DatabaseField]
    public virtual int SentinelFindingID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelFindingID)), 0);
        set => SetValue(nameof(SentinelFindingID), value);
    }

    [DatabaseField]
    public virtual Guid SentinelFindingGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(SentinelFindingGuid)), default);
        set => SetValue(nameof(SentinelFindingGuid), value);
    }

    [DatabaseField]
    public virtual int SentinelFindingScanRunID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SentinelFindingScanRunID)), 0);
        set => SetValue(nameof(SentinelFindingScanRunID), value);
    }

    /// <summary>Rule identifier e.g. "CFG001".</summary>
    [DatabaseField]
    public virtual string SentinelFindingRuleID
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelFindingRuleID)), string.Empty);
        set => SetValue(nameof(SentinelFindingRuleID), value);
    }

    [DatabaseField]
    public virtual string SentinelFindingRuleTitle
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelFindingRuleTitle)), string.Empty);
        set => SetValue(nameof(SentinelFindingRuleTitle), value);
    }

    [DatabaseField]
    public virtual string SentinelFindingCategory
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelFindingCategory)), string.Empty);
        set => SetValue(nameof(SentinelFindingCategory), value);
    }

    /// <summary>Canonical Title-Case severity: "Error" | "Warning" | "Info".</summary>
    [DatabaseField]
    public virtual string SentinelFindingSeverity
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelFindingSeverity)), string.Empty);
        set => SetValue(nameof(SentinelFindingSeverity), value);
    }

    [DatabaseField]
    public virtual string SentinelFindingMessage
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelFindingMessage)), string.Empty);
        set => SetValue(nameof(SentinelFindingMessage), value);
    }

    [DatabaseField]
    public virtual string SentinelFindingLocation
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelFindingLocation)), string.Empty);
        set => SetValue(nameof(SentinelFindingLocation), value);
    }

    [DatabaseField]
    public virtual string SentinelFindingRemediation
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelFindingRemediation)), string.Empty);
        set => SetValue(nameof(SentinelFindingRemediation), value);
    }

    [DatabaseField]
    public virtual bool SentinelFindingQuoteEligible
    {
        get => ValidationHelper.GetBoolean(GetValue(nameof(SentinelFindingQuoteEligible)), true);
        set => SetValue(nameof(SentinelFindingQuoteEligible), value);
    }

    /// <summary>
    /// SHA-256 hex (64 chars total) over rule ID + category + normalized location + canonical
    /// message. The message is digit-stripped so counts that drift scan-to-scan don't shift the
    /// hash. Used to cross-reference acknowledgments: the same underlying issue across repeated
    /// scans shares a fingerprint, so an ack in one scan persists across the next.
    /// </summary>
    [DatabaseField]
    public virtual string SentinelFindingFingerprintHash
    {
        get => ValidationHelper.GetString(GetValue(nameof(SentinelFindingFingerprintHash)), string.Empty);
        set => SetValue(nameof(SentinelFindingFingerprintHash), value);
    }

    protected override void DeleteObject() => Provider.Delete(this);
    protected override void SetObject() => Provider.Set(this);

    public SentinelFindingInfo() : base(TYPEINFO) { }
    public SentinelFindingInfo(DataRow dr) : base(TYPEINFO, dr) { }
}
