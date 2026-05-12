using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using XperienceCommunity.Sentinel.Module.Acknowledgment;
using XperienceCommunity.Sentinel.Module.Configuration;
using XperienceCommunity.Sentinel.Module.Contact;
using XperienceCommunity.Sentinel.Module.Notifications;
using XperienceCommunity.Sentinel.Module.Retention;
using XperienceCommunity.Sentinel.Module.Services;
using XperienceCommunity.Sentinel.Module.SettingsOverride;

namespace XperienceCommunity.Sentinel.Module.DependencyInjection;

/// <summary>
/// DI entry point. Call once in <c>Program.cs</c>:
/// <code>
/// builder.Services.AddSentinel(builder.Configuration);
/// </code>
/// Does NOT modify middleware — the consumer still controls <c>Program.cs</c> and must preserve
/// the Kentico trio ordering (<c>InitKentico → UseStaticFiles → UseKentico</c>).
/// </summary>
public static class SentinelServiceCollectionExtensions
{
    public static IServiceCollection AddSentinel(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = SentinelOptions.SectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SentinelOptions>(configuration.GetSection(sectionName));
        RegisterSharedServices(services);
        return services;
    }

    /// <summary>
    /// Overload for callers who'd rather configure via a delegate than bind from configuration.
    /// </summary>
    public static IServiceCollection AddSentinel(
        this IServiceCollection services,
        Action<SentinelOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        RegisterSharedServices(services);
        return services;
    }

    private static void RegisterSharedServices(IServiceCollection services)
    {
        services.AddHttpClient();

        // Installer lifetime: Singleton. Kentico resolves it once during startup from the root
        // provider (inside SentinelModule.OnInit) and keeps the reference for the full app
        // lifetime, so the service registration must not be scoped — otherwise scope validation
        // throws, or (without validation) we quietly leak a scoped instance.
        services.AddSingleton<SentinelModuleInstaller>();

        services.AddScoped<SentinelScanService>();
        services.AddScoped<ISentinelEventLogWriter, SentinelEventLogWriter>();
        services.AddScoped<ISentinelEmailDigestSender, SentinelEmailDigestSender>();
        services.AddScoped<ISentinelFindingAckService, SentinelFindingAckService>();

        // Retention is stateless: resolve-per-use is fine, and keeping it Transient avoids
        // coupling the scan-completion pipeline's scope lifetime to the trim pass. The scan
        // service is Scoped and resolves this inline when it fires a trim after each run.
        services.AddTransient<ISentinelRetentionService, SentinelRetentionService>();

        // Settings-override store — reads the single-row admin-UI override and layers it on top
        // of SentinelOptions via PostConfigure.
        //
        // Lifetime: Singleton, not Scoped. IOptions<T>.Value is cached at the root provider
        // scope; when resolved it invokes every IPostConfigureOptions<T>. A scoped applier
        // registered against a root-scope resolution throws "scoped-from-root" under strict
        // scope validation (and silently leaks scoped resources without it). We keep the store
        // singleton-safe by creating a fresh scope per call inside the store itself (see
        // SentinelSettingsOverrideStore — it asks IServiceScopeFactory for a scope, resolves
        // the IInfoProvider<T> there, disposes on exit).
        services.AddSingleton<ISentinelSettingsOverrideStore, SentinelSettingsOverrideStore>();
        services.AddSingleton<IPostConfigureOptions<SentinelOptions>, SentinelOptionsOverrideApplier>();

        // Typed HttpClient for the Refined Element quote intake. 30s timeout leaves headroom for
        // KDaaS cold-start while still bounding a hung dependency — the admin UI surface is
        // synchronous-feeling from the operator's perspective, so we'd rather fail fast than
        // leave them staring at a spinner.
        services.AddHttpClient<ISentinelContactService, SentinelContactService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            // Valid RFC 7231 User-Agent product token: `<name>/<version>` pair using the assembly
            // InformationalVersion (matches what SentinelScanService persists on each scan run).
            // A bare token like "XperienceCommunity-Sentinel-Module" (no slash + version) makes ParseAdd throw
            // FormatException at typed-client construction time, which would tank the whole DI
            // graph.
            client.DefaultRequestHeaders.UserAgent.Add(
                new System.Net.Http.Headers.ProductInfoHeaderValue(
                    productName: "XperienceCommunity-Sentinel-Module",
                    productVersion: SentinelVersion.Current));
        });
    }
}
