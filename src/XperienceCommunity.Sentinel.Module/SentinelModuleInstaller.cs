using CMS.Core;
using CMS.DataEngine;
using CMS.FormEngine;
using CMS.Membership;
using CMS.Modules;
using CMS.Scheduler;

using XperienceCommunity.Sentinel.Module.InfoModels.SentinelFinding;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelFindingAck;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelScanRun;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelSettingsOverride;
using XperienceCommunity.Sentinel.Module.Scheduling;

namespace XperienceCommunity.Sentinel.Module;

/// <summary>
/// Creates the Sentinel module's resource + data-class rows on every app startup. Each
/// <c>Install*</c> method is idempotent — <c>Get ?? New</c> + <c>if (HasChanged) Set</c> — so
/// running on every cold start is a no-op once the tables exist.
/// </summary>
public class SentinelModuleInstaller(
    IInfoProvider<ResourceInfo> resourceProvider,
    IInfoProvider<ScheduledTaskConfigurationInfo> scheduledTaskProvider,
    IEventLogService eventLogService)
{
    private const string ResourceName = "XperienceCommunity.Sentinel";

    private readonly IInfoProvider<ResourceInfo> resourceProvider = resourceProvider;
    private readonly IInfoProvider<ScheduledTaskConfigurationInfo> scheduledTaskProvider = scheduledTaskProvider;
    private readonly IEventLogService eventLogService = eventLogService;

    public void Install()
    {
        var resource = InitializeResource(resourceProvider.Get(ResourceName) ?? new ResourceInfo());
        InstallScanRun(resource);
        InstallFinding(resource);
        InstallFindingAck(resource);
        InstallSettingsOverride(resource);
        TryInstallDefaultScheduledTask();
    }

    /// <summary>
    /// Isolates default-task creation from the rest of the installer. The scheduled task row is
    /// a UX convenience (saves the admin from opening "New scheduled task" on fresh installs) —
    /// not a correctness requirement. Kentico's Info framework always registers the injected
    /// <see cref="ScheduledTaskConfigurationInfo"/> provider so DI resolution itself is safe; the
    /// guard covers runtime failures — column-level validation Kentico may add in a future
    /// refresh, a transient DB issue during startup, or an unexpected exception from the
    /// provider's Set. Any failure logs a clear DEFAULT_SCHEDULE_SKIPPED warning so the admin
    /// knows to create the task manually, and the rest of the install continues.
    /// </summary>
    private void TryInstallDefaultScheduledTask()
    {
        try
        {
            InstallDefaultScheduledTask();
        }
        catch (Exception ex)
        {
            // Positional call — LogException's named params are (source, eventCode, exception,
            // additionalMessage, loggingPolicy); the last two are optional. Kentico prefers
            // positional here to avoid the CS1739 trap on param name drift.
            eventLogService.LogException(
                "Sentinel",
                "DEFAULT_SCHEDULE_SKIPPED",
                ex,
                "Could not create the default Sentinel scheduled-task row. " +
                "This is non-fatal — Sentinel installed its tables and the task class is " +
                "registered; an admin can still create the scheduled task manually in " +
                "Configuration → Scheduled tasks (task implementation/display name = " +
                "\"Sentinel scan\", identifier = \"" + SentinelScanTask.TaskName + "\").");
        }
    }

    private ResourceInfo InitializeResource(ResourceInfo resource)
    {
        resource.ResourceDisplayName = "Sentinel for Xperience by Kentico";
        // "XperienceCommunity." prefix (as opposed to "CMS.") keeps Kentico from generating C#
        // code for these classes — we own the Info types in the package, not the app.
        resource.ResourceName = ResourceName;
        resource.ResourceDescription = "Sentinel scan history, findings, and acknowledgments.";
        resource.ResourceIsInDevelopment = false;
        if (resource.HasChanged)
        {
            resourceProvider.Set(resource);
        }
        return resource;
    }

    private static void InstallScanRun(ResourceInfo resource)
    {
        var info = DataClassInfoProvider.GetDataClassInfo(SentinelScanRunInfo.OBJECT_TYPE)
            ?? DataClassInfo.New(SentinelScanRunInfo.OBJECT_TYPE);
        info.ClassName = SentinelScanRunInfo.TYPEINFO.ObjectClassName;
        info.ClassTableName = SentinelScanRunInfo.TYPEINFO.ObjectClassName.Replace(".", "_");
        info.ClassDisplayName = "Sentinel scan run";
        info.ClassType = ClassType.OTHER;
        info.ClassResourceID = resource.ResourceID;

        var form = FormHelper.GetBasicFormDefinition(nameof(SentinelScanRunInfo.SentinelScanRunID));
        form.AddFormItem(TextField(nameof(SentinelScanRunInfo.SentinelScanRunGuid), "guid", allowEmpty: false));
        form.AddFormItem(Field(nameof(SentinelScanRunInfo.SentinelScanRunStartedAt), "datetime", allowEmpty: false));
        form.AddFormItem(Field(nameof(SentinelScanRunInfo.SentinelScanRunCompletedAt), "datetime", allowEmpty: true));
        form.AddFormItem(TextField(nameof(SentinelScanRunInfo.SentinelScanRunTrigger), "text", size: 32));
        form.AddFormItem(TextField(nameof(SentinelScanRunInfo.SentinelScanRunSentinelVersion), "text", size: 64));
        form.AddFormItem(Field(nameof(SentinelScanRunInfo.SentinelScanRunTotalFindings), "integer"));
        form.AddFormItem(Field(nameof(SentinelScanRunInfo.SentinelScanRunErrorCount), "integer"));
        form.AddFormItem(Field(nameof(SentinelScanRunInfo.SentinelScanRunWarningCount), "integer"));
        form.AddFormItem(Field(nameof(SentinelScanRunInfo.SentinelScanRunInfoCount), "integer"));
        form.AddFormItem(Field(nameof(SentinelScanRunInfo.SentinelScanRunDurationSeconds), "decimal", precision: 10, scale: 3));
        form.AddFormItem(TextField(nameof(SentinelScanRunInfo.SentinelScanRunStatus), "text", size: 32));
        form.AddFormItem(TextField(nameof(SentinelScanRunInfo.SentinelScanRunErrorMessage), "longtext", allowEmpty: true));

        SetFormDefinition(info, form);
        if (info.HasChanged) DataClassInfoProvider.SetDataClassInfo(info);
    }

    private static void InstallFinding(ResourceInfo resource)
    {
        var info = DataClassInfoProvider.GetDataClassInfo(SentinelFindingInfo.OBJECT_TYPE)
            ?? DataClassInfo.New(SentinelFindingInfo.OBJECT_TYPE);
        info.ClassName = SentinelFindingInfo.TYPEINFO.ObjectClassName;
        info.ClassTableName = SentinelFindingInfo.TYPEINFO.ObjectClassName.Replace(".", "_");
        info.ClassDisplayName = "Sentinel finding";
        info.ClassType = ClassType.OTHER;
        info.ClassResourceID = resource.ResourceID;

        var form = FormHelper.GetBasicFormDefinition(nameof(SentinelFindingInfo.SentinelFindingID));
        form.AddFormItem(TextField(nameof(SentinelFindingInfo.SentinelFindingGuid), "guid", allowEmpty: false));
        form.AddFormItem(new FormFieldInfo
        {
            Name = nameof(SentinelFindingInfo.SentinelFindingScanRunID),
            AllowEmpty = false,
            Visible = true,
            Enabled = true,
            DataType = "integer",
            ReferenceToObjectType = SentinelScanRunInfo.OBJECT_TYPE,
            ReferenceType = ObjectDependencyEnum.Required,
        });
        form.AddFormItem(TextField(nameof(SentinelFindingInfo.SentinelFindingRuleID), "text", size: 32));
        form.AddFormItem(TextField(nameof(SentinelFindingInfo.SentinelFindingRuleTitle), "text", size: 200));
        form.AddFormItem(TextField(nameof(SentinelFindingInfo.SentinelFindingCategory), "text", size: 100));
        form.AddFormItem(TextField(nameof(SentinelFindingInfo.SentinelFindingSeverity), "text", size: 16));
        form.AddFormItem(TextField(nameof(SentinelFindingInfo.SentinelFindingMessage), "longtext", allowEmpty: true));
        form.AddFormItem(TextField(nameof(SentinelFindingInfo.SentinelFindingLocation), "longtext", allowEmpty: true));
        form.AddFormItem(TextField(nameof(SentinelFindingInfo.SentinelFindingRemediation), "longtext", allowEmpty: true));
        form.AddFormItem(Field(nameof(SentinelFindingInfo.SentinelFindingQuoteEligible), "boolean"));
        form.AddFormItem(TextField(nameof(SentinelFindingInfo.SentinelFindingFingerprintHash), "text", size: 64));

        SetFormDefinition(info, form);
        if (info.HasChanged) DataClassInfoProvider.SetDataClassInfo(info);
    }

    private static void InstallFindingAck(ResourceInfo resource)
    {
        var info = DataClassInfoProvider.GetDataClassInfo(SentinelFindingAckInfo.OBJECT_TYPE)
            ?? DataClassInfo.New(SentinelFindingAckInfo.OBJECT_TYPE);
        info.ClassName = SentinelFindingAckInfo.TYPEINFO.ObjectClassName;
        info.ClassTableName = SentinelFindingAckInfo.TYPEINFO.ObjectClassName.Replace(".", "_");
        info.ClassDisplayName = "Sentinel finding acknowledgment";
        info.ClassType = ClassType.OTHER;
        info.ClassResourceID = resource.ResourceID;

        var form = FormHelper.GetBasicFormDefinition(nameof(SentinelFindingAckInfo.SentinelFindingAckID));
        form.AddFormItem(TextField(nameof(SentinelFindingAckInfo.SentinelFindingAckGuid), "guid", allowEmpty: false));
        form.AddFormItem(TextField(nameof(SentinelFindingAckInfo.SentinelFindingAckFingerprintHash), "text", size: 64));
        form.AddFormItem(TextField(nameof(SentinelFindingAckInfo.SentinelFindingAckState), "text", size: 16));
        form.AddFormItem(Field(nameof(SentinelFindingAckInfo.SentinelFindingAckSnoozeUntil), "datetime", allowEmpty: true));
        form.AddFormItem(UserField(nameof(SentinelFindingAckInfo.SentinelFindingAckUserID)));
        form.AddFormItem(Field(nameof(SentinelFindingAckInfo.SentinelFindingAckAckedAt), "datetime"));
        form.AddFormItem(TextField(nameof(SentinelFindingAckInfo.SentinelFindingAckNote), "longtext", allowEmpty: true));

        SetFormDefinition(info, form);
        if (info.HasChanged) DataClassInfoProvider.SetDataClassInfo(info);
    }

    private static void InstallSettingsOverride(ResourceInfo resource)
    {
        var info = DataClassInfoProvider.GetDataClassInfo(SentinelSettingsOverrideInfo.OBJECT_TYPE)
            ?? DataClassInfo.New(SentinelSettingsOverrideInfo.OBJECT_TYPE);
        info.ClassName = SentinelSettingsOverrideInfo.TYPEINFO.ObjectClassName;
        info.ClassTableName = SentinelSettingsOverrideInfo.TYPEINFO.ObjectClassName.Replace(".", "_");
        info.ClassDisplayName = "Sentinel settings override";
        info.ClassType = ClassType.OTHER;
        info.ClassResourceID = resource.ResourceID;

        var form = FormHelper.GetBasicFormDefinition(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideID));
        form.AddFormItem(TextField(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideGuid), "guid", allowEmpty: false));
        form.AddFormItem(Field(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideEnabled), "boolean"));
        form.AddFormItem(TextField(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideExcludedChecks), "longtext", allowEmpty: true));
        form.AddFormItem(Field(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideStaleDays), "integer"));
        form.AddFormItem(Field(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideEventLogDays), "integer"));
        form.AddFormItem(Field(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideEventLogEnabled), "boolean"));
        form.AddFormItem(TextField(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideEventLogSeverityThreshold), "text", size: 16));
        form.AddFormItem(Field(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideEventLogMaxEntriesPerScan), "integer"));
        form.AddFormItem(Field(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideEmailDigestEnabled), "boolean"));
        form.AddFormItem(TextField(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideEmailDigestRecipients), "longtext", allowEmpty: true));
        form.AddFormItem(TextField(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideEmailDigestSeverityThreshold), "text", size: 16));
        form.AddFormItem(Field(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideEmailDigestOnlyWhenThresholdFindings), "boolean"));
        form.AddFormItem(UserField(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideLastModifiedBy)));
        form.AddFormItem(Field(nameof(SentinelSettingsOverrideInfo.SentinelSettingsOverrideLastModifiedAt), "datetime"));

        SetFormDefinition(info, form);
        if (info.HasChanged) DataClassInfoProvider.SetDataClassInfo(info);
    }

    private static FormFieldInfo Field(string name, string dataType, bool allowEmpty = false, int precision = 0, int scale = 0) => new()
    {
        Name = name,
        DataType = dataType,
        AllowEmpty = allowEmpty,
        Visible = true,
        Enabled = true,
        // Kentico's FormFieldInfo -> SQL DDL mapping for decimals is counterintuitive:
        //   FormFieldInfo.Size      -> SQL precision (total digit count)
        //   FormFieldInfo.Precision -> SQL scale     (digits after decimal)
        // Previously swapped, which produced ALTER TABLE … decimal(3,10) and SQL rejected
        // the install transaction ("scale must be within 0..precision"). No tables got
        // created on v0.2.0 / v0.2.1 despite the module loading.
        Size = precision,
        Precision = scale,
    };

    private static FormFieldInfo TextField(string name, string dataType, int size = 0, bool allowEmpty = false) => new()
    {
        Name = name,
        DataType = dataType,
        AllowEmpty = allowEmpty,
        Visible = true,
        Enabled = true,
        Size = size,
    };

    /// <summary>
    /// Integer FK column that references a Kentico user (<c>cms.user</c>). Use for audit
    /// columns like "acknowledged by" or "last modified by" so the admin UI can resolve the
    /// int to a user display name / link instead of showing a raw ID, and so Kentico's
    /// dependency tracking understands the relationship.
    /// </summary>
    /// <remarks>
    /// <see cref="ObjectDependencyEnum.NotRequired"/> (soft reference) is intentional: if a
    /// user is deleted we keep the audit row with the now-orphaned user ID rather than
    /// cascade-deleting the ack / override history. Required would take the row with the
    /// user, which is the wrong semantic for an audit trail — removing an admin should not
    /// rewrite the record of what they acknowledged.
    /// </remarks>
    private static FormFieldInfo UserField(string name) => new()
    {
        Name = name,
        DataType = "integer",
        AllowEmpty = false,
        Visible = true,
        Enabled = true,
        ReferenceToObjectType = UserInfo.OBJECT_TYPE,
        ReferenceType = ObjectDependencyEnum.NotRequired,
    };

    private static void SetFormDefinition(DataClassInfo info, FormInfo form)
    {
        if (info.ClassID > 0)
        {
            var existing = new FormInfo(info.ClassFormDefinition);
            existing.CombineWithForm(form, new());
            info.ClassFormDefinition = existing.GetXmlDefinition();
        }
        else
        {
            info.ClassFormDefinition = form.GetXmlDefinition();
        }
    }

    /// <summary>
    /// Ensures a disabled <c>CMS_ScheduledTaskConfiguration</c> row exists for
    /// <see cref="SentinelScanTask"/> on fresh installs — backed at runtime by
    /// <see cref="ScheduledTaskConfigurationInfo"/> via the injected provider — so the task
    /// surfaces in the Scheduled Tasks admin app with one click to enable, instead of forcing
    /// the admin to fill a multi-field "New task" form. Creates-once: if a row with the matching
    /// identifier already exists (manual or from a prior install) we leave it alone — admins may
    /// have customized the display name, cadence, or enabled state, and we do NOT want to
    /// overwrite their changes on a cold restart.
    ///
    /// Created disabled with a sane default interval (every 24 hours). Admin can change the
    /// cadence + enable state via the Scheduled Tasks app; we just need SOMETHING in the
    /// Interval column because the DB schema is NOT NULL. v0.3.0/0.3.1-alpha shipped without
    /// the default value and every install silently failed the INSERT with a NULL-constraint
    /// violation, leaving admins with no task row at all.
    /// </summary>
    /// <remarks>
    /// Concurrency note: two app instances starting at the same time could both observe "no row"
    /// and both attempt to insert. The outer <c>TryInstallDefaultScheduledTask</c> wraps this
    /// method in a try/catch that logs DEFAULT_SCHEDULE_SKIPPED; if the second insert races in,
    /// it's harmless — admin sees either one row (winner) or two rows that both route to the
    /// same SentinelScanTask, and they can delete the dupe in the admin UI. The concern is
    /// logged-visible rather than silently corrupting state.
    /// </remarks>
    private void InstallDefaultScheduledTask()
    {
        // WhereEquals on the discriminating Identifier column covers both "never installed" and
        // "installed then manually renamed" cases. We match on the stable code identifier from
        // [RegisterScheduledTask], not on ScheduledTaskConfigurationName which an admin can edit.
        var existing = scheduledTaskProvider.Get()
            .WhereEquals(nameof(ScheduledTaskConfigurationInfo.ScheduledTaskConfigurationScheduledTaskIdentifier), SentinelScanTask.TaskName)
            .TopN(1)
            .FirstOrDefault();
        if (existing is not null)
        {
            return;
        }

        // The CMS_ScheduledTaskConfiguration table has 8 NOT NULL columns we don't get for free:
        // Name, DisplayName, Interval, Data, GUID, LastModified, ScheduledTaskIdentifier, and
        // several bit flags. C# value-type defaults cover the bits and timestamps; we explicitly
        // populate every string + GUID field so the INSERT doesn't trip a NULL constraint. Each
        // missing column would have shipped as a separate hotfix (v0.3.2 only covered Interval,
        // v0.3.3 picks up Data / GUID / LastModified) — cheaper to set them all now even though
        // some would auto-default.
        var task = new ScheduledTaskConfigurationInfo
        {
            ScheduledTaskConfigurationName = SentinelScanTask.TaskName,
            ScheduledTaskConfigurationDisplayName = "Sentinel scan",
            ScheduledTaskConfigurationScheduledTaskIdentifier = SentinelScanTask.TaskName,
            ScheduledTaskConfigurationEnabled = false,
            ScheduledTaskConfigurationDeleteAfterLastRun = false,
            ScheduledTaskConfigurationInterval = DefaultDailyInterval,
            // Empty string (not null) satisfies the NOT NULL constraint on the nvarchar(max)
            // Data column. Kentico uses this for task-specific XML payload when the task class
            // wants custom serialized state at schedule time — the Sentinel scan needs none, so
            // empty is semantically correct.
            ScheduledTaskConfigurationData = string.Empty,
            // Kentico's AbstractInfo base class SHOULD populate GUID + LastModified on Set(),
            // but we set them explicitly as defence in depth — a silent default-to-null here
            // would bubble back up as another "column X cannot be NULL" INSERT error next boot.
            ScheduledTaskConfigurationGUID = Guid.NewGuid(),
            ScheduledTaskConfigurationLastModified = DateTime.UtcNow,
        };
        scheduledTaskProvider.Set(task);
    }

    /// <summary>
    /// Pipe-delimited interval string Kentico's scheduler parses into a "runs every 1 day,
    /// starting at midnight" cadence. We hand-roll the format because <c>SchedulingHelper</c>
    /// and <c>TaskInterval</c> are internal in Xperience 31.x (not in the public API surface).
    /// Admin can override this with any cadence they want via the Scheduled Tasks admin app;
    /// all we need is a non-null value that satisfies the DB's NOT NULL constraint. The
    /// "day;1;HH:mm:ss" shape has been stable across the Xperience 30-31 line — verified by
    /// inspecting existing rows in CMS_ScheduledTaskConfiguration on an active install.
    /// </summary>
    private const string DefaultDailyInterval = "day;1;00:00:00";
}
