using CMS;
using CMS.Base;
using CMS.Core;
using CMS.DataEngine;

using Microsoft.Extensions.DependencyInjection;

// Gate for Kentico's assembly scanner. Without this attribute, Kentico skips the DLL on startup
// scan, so [RegisterScheduledTask] below never populates the "Task implementation" dropdown in
// the admin Scheduled Tasks app — even though the module + installer paths still work via the
// Module base class. Learned the hard way in v0.2.0-alpha on refinedelement.com.
[assembly: AssemblyDiscoverable]
[assembly: RegisterModule(typeof(XperienceCommunity.Sentinel.Module.SentinelModule))]

namespace XperienceCommunity.Sentinel.Module;

/// <summary>
/// Entry point Kentico discovers at startup. Hooks <see cref="ApplicationEvents.Initialized"/>
/// and asks the installer to upsert the module's resource + data-class rows. The installer is
/// idempotent, so repeat runs are no-ops once the tables exist.
/// </summary>
internal sealed class SentinelModule : CMS.DataEngine.Module
{
    private SentinelModuleInstaller? installer;
    private IEventLogService? eventLog;

    public SentinelModule() : base(nameof(SentinelModule)) { }

    protected override void OnInit(ModuleInitParameters parameters)
    {
        base.OnInit(parameters);

        // Optional resolution: if a consumer installs the package but forgets
        // `builder.Services.AddSentinel(...)` in Program.cs, don't crash startup. Degrade
        // gracefully with a single event-log warning and leave the rest of Kentico alone.
        installer = parameters.Services.GetService<SentinelModuleInstaller>();
        eventLog = parameters.Services.GetService<IEventLogService>();

        if (installer is null)
        {
            eventLog?.LogWarning(
                source: "Sentinel",
                eventCode: "MODULE_NOT_REGISTERED",
                eventDescription:
                    "Sentinel module discovered but SentinelModuleInstaller is not registered in DI. " +
                    "Add `builder.Services.AddSentinel(builder.Configuration)` in Program.cs to enable Sentinel. " +
                    "The rest of the application is unaffected.");
            return;
        }

        ApplicationEvents.Initialized.Execute += OnInitialized;
    }

    private void OnInitialized(object? sender, EventArgs e)
    {
        try
        {
            installer?.Install();
        }
        catch (Exception ex)
        {
            // Don't let installer failures cascade into app startup failures. Log loudly and
            // continue — the site remains available; Sentinel just won't scan this run.
            eventLog?.LogException("Sentinel", "INSTALL_FAILED", ex);
        }
    }
}
