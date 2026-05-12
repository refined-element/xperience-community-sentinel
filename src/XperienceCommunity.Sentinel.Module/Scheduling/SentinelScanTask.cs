using CMS;
using CMS.Scheduler;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using XperienceCommunity.Sentinel.Module.Services;

// Fully-qualified here so the file doesn't need a `using` of its own namespace (CS8019 / noise).
// The attribute runs during assembly scan, before the `namespace` declaration below takes effect.
[assembly: RegisterScheduledTask(
    XperienceCommunity.Sentinel.Module.Scheduling.SentinelScanTask.TaskName,
    typeof(XperienceCommunity.Sentinel.Module.Scheduling.SentinelScanTask))]

namespace XperienceCommunity.Sentinel.Module.Scheduling;

/// <summary>
/// Kentico's scheduled-task runner invokes this on its cron. Appears in the admin Scheduled tasks
/// app — admins can enable/disable, change the interval, and click "Execute now". In multi-instance
/// deployments the task fires on exactly one instance per tick.
/// </summary>
public sealed class SentinelScanTask : IScheduledTask
{
    public const string TaskName = "XperienceCommunity.SentinelScan";

    public async Task<ScheduledTaskExecutionResult> Execute(
        ScheduledTaskConfigurationInfo task,
        CancellationToken cancellationToken)
    {
        var services = CMS.Core.Service.ResolveOptional<IServiceProvider>();
        if (services is null)
        {
            return new ScheduledTaskExecutionResult("Sentinel: IServiceProvider unavailable — scan skipped.");
        }

        using var scope = services.CreateScope();

        // Optional resolution — same reasoning as SentinelModule.OnInit. If a consumer installed
        // the package but forgot `builder.Services.AddSentinel(...)`, don't throw on every
        // scheduler tick. Return a clean "not registered" message once; the admin sees it in the
        // Scheduled Tasks list's Last-run column and knows exactly what's missing.
        var scanService = scope.ServiceProvider.GetService<SentinelScanService>();
        var logger = scope.ServiceProvider.GetService<ILogger<SentinelScanTask>>();

        if (scanService is null)
        {
            return new ScheduledTaskExecutionResult(
                "Sentinel services are not registered. Add `builder.Services.AddSentinel(builder.Configuration)` to Program.cs.");
        }

        try
        {
            var run = await scanService.RunAsync(trigger: "Scheduled", cancellationToken).ConfigureAwait(false);
            if (run is null)
            {
                // Sentinel:Enabled is false in config.
                return new ScheduledTaskExecutionResult("Sentinel scan skipped: integration is disabled in configuration.");
            }

            logger?.LogInformation("Sentinel scheduled scan completed: run #{RunId}, {Total} findings.",
                run.SentinelScanRunID, run.SentinelScanRunTotalFindings);
            // Successful runs return the singleton; the admin UI shows "Succeeded" without a message.
            // Detailed run summary is persisted on the XperienceCommunity_SentinelScanRun row (+ related
            // XperienceCommunity_SentinelFinding rows) and mirrored to CMS_EventLog in headless mode. A
            // dedicated admin UI arrives in a follow-up release.
            return ScheduledTaskExecutionResult.Success;
        }
        catch (OperationCanceledException)
        {
            return new ScheduledTaskExecutionResult("Sentinel scan cancelled.");
        }
        catch (Exception ex)
        {
            // Log the exception with full detail internally, but return a generic message so that
            // connection strings, server names, paths, etc. don't leak to admins who might only
            // glance at the Scheduled Tasks UI.
            logger?.LogError(ex, "Sentinel scheduled scan failed.");
            return new ScheduledTaskExecutionResult("Sentinel scan failed. Check the Event log / application logs for details.");
        }
    }
}
