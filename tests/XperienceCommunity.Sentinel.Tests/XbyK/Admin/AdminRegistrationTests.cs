using System.Reflection;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

using XperienceCommunity.Sentinel.Admin;
using XperienceCommunity.Sentinel.Admin.UIPages;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelFinding;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelScanRun;

namespace XperienceCommunity.Sentinel.Tests.XbyK.Admin;

/// <summary>
/// Reflection-only tests: Kentico admin pages need the full admin runtime to evaluate, but the
/// attribute metadata that drives the left-nav + page tree is inspectable without bootstrapping
/// anything. Locks the critical registration surface (identifier, slug, parent wiring) against
/// silent regressions — the kind that would make Sentinel's nav entry disappear from admin.
/// </summary>
public class AdminRegistrationTests
{
    private static readonly Assembly AdminAssembly = typeof(SentinelApplicationPage).Assembly;

    [Fact]
    public void Admin_assembly_is_discoverable_by_Kentico_scanner()
    {
        // CMS.AssemblyDiscoverableAttribute injected via csproj <AssemblyAttribute>. Without it,
        // Kentico's startup scan skips the DLL and none of the [UIApplication] / [UIPage]
        // registrations below ever surface in admin. Same foot-gun that hid the scheduled task
        // type in v0.2.0-alpha.
        var discoverable = AdminAssembly.GetCustomAttributes()
            .Any(a => a.GetType().FullName == "CMS.AssemblyDiscoverableAttribute");
        Assert.True(discoverable,
            "XperienceCommunity.Sentinel.Admin must carry [assembly: CMS.AssemblyDiscoverable] or Kentico will ignore its UI registrations.");
    }

    [Fact]
    public void Sentinel_application_is_registered_once()
    {
        var apps = AdminAssembly.GetCustomAttributes<UIApplicationAttribute>()
            .Where(a => a.Type == typeof(SentinelApplicationPage))
            .ToArray();
        var app = Assert.Single(apps);

        Assert.Equal(SentinelApplicationPage.IDENTIFIER, app.Identifier);
        Assert.Equal("sentinel", app.Slug);
        Assert.Equal("Sentinel", app.Name);
        Assert.Equal(BaseApplicationCategories.CONFIGURATION, app.Category);
        Assert.Equal(TemplateNames.SECTION_LAYOUT, app.TemplateName);
    }

    [Fact]
    public void Scan_history_page_registers_under_Sentinel_app_with_listing_template()
    {
        var page = AdminAssembly.GetCustomAttributes<UIPageAttribute>()
            .Single(p => p.Type == typeof(ScanHistoryListingPage));

        Assert.Equal(typeof(SentinelApplicationPage), page.ParentType);
        Assert.Equal("scans", page.Slug);
        Assert.Equal("Scan history", page.Name);
        Assert.Equal(TemplateNames.LISTING, page.TemplateName);
    }

    [Fact]
    public void Findings_page_registers_under_Sentinel_app_with_listing_template()
    {
        var page = AdminAssembly.GetCustomAttributes<UIPageAttribute>()
            .Single(p => p.Type == typeof(FindingsListingPage));

        Assert.Equal(typeof(SentinelApplicationPage), page.ParentType);
        Assert.Equal("findings", page.Slug);
        Assert.Equal("Findings", page.Name);
        Assert.Equal(TemplateNames.LISTING, page.TemplateName);
    }

    [Fact]
    public void Scan_history_page_binds_to_SentinelScanRun_object_type()
    {
        // ObjectType is a protected override; read via reflection. Kentico's listing framework
        // reads this to pick the data source — a drift here would silently list the wrong table.
        var prop = typeof(ScanHistoryListingPage)
            .GetProperty("ObjectType", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var instance = (ScanHistoryListingPage)RuntimeHelpers.GetUninitializedObject(typeof(ScanHistoryListingPage));
        var objectType = (string?)prop.GetValue(instance);
        Assert.Equal(SentinelScanRunInfo.OBJECT_TYPE, objectType);
    }

    [Fact]
    public void Findings_page_binds_to_SentinelFinding_object_type()
    {
        var prop = typeof(FindingsListingPage)
            .GetProperty("ObjectType", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var instance = (FindingsListingPage)RuntimeHelpers.GetUninitializedObject(typeof(FindingsListingPage));
        var objectType = (string?)prop.GetValue(instance);
        Assert.Equal(SentinelFindingInfo.OBJECT_TYPE, objectType);
    }

    [Fact]
    public void Dashboard_page_is_first_in_nav_order_and_uses_custom_template()
    {
        var page = AdminAssembly.GetCustomAttributes<UIPageAttribute>()
            .Single(p => p.Type == typeof(SentinelDashboardPage));

        Assert.Equal(typeof(SentinelApplicationPage), page.ParentType);
        Assert.Equal("dashboard", page.Slug);
        // Dashboard must be first so it's the default destination when an admin clicks the
        // Sentinel nav entry. Listing pages are First (100) and 200; dashboard takes First - 1.
        Assert.True(page.Order < UIPageOrder.First);
        // Custom React template name — must begin "@<orgName>/<projectName>/" to resolve to the
        // client bundle. Drift from the csproj or webpack orgName/projectName = blank page.
        Assert.Equal("@xperiencecommunity/sentinel-admin/Dashboard", page.TemplateName);
    }

    [Fact]
    public void Contact_page_registers_with_custom_template()
    {
        var page = AdminAssembly.GetCustomAttributes<UIPageAttribute>()
            .Single(p => p.Type == typeof(SentinelContactPage));

        Assert.Equal(typeof(SentinelApplicationPage), page.ParentType);
        Assert.Equal("contact", page.Slug);
        Assert.Equal("@xperiencecommunity/sentinel-admin/Contact", page.TemplateName);
    }

    [Fact]
    public void Settings_page_uses_custom_template()
    {
        var page = AdminAssembly.GetCustomAttributes<UIPageAttribute>()
            .Single(p => p.Type == typeof(SentinelSettingsPage));
        Assert.Equal(typeof(SentinelApplicationPage), page.ParentType);
        Assert.Equal("settings", page.Slug);
        Assert.Equal("@xperiencecommunity/sentinel-admin/Settings", page.TemplateName);
    }

    [Fact]
    public void Scan_detail_page_uses_custom_template()
    {
        var page = AdminAssembly.GetCustomAttributes<UIPageAttribute>()
            .Single(p => p.Type == typeof(SentinelScanDetailPage));
        Assert.Equal(typeof(SentinelApplicationPage), page.ParentType);
        Assert.Equal("scan-detail", page.Slug);
        Assert.Equal("@xperiencecommunity/sentinel-admin/ScanDetail", page.TemplateName);
    }

    [Fact]
    public void Diff_page_uses_custom_template()
    {
        var page = AdminAssembly.GetCustomAttributes<UIPageAttribute>()
            .Single(p => p.Type == typeof(SentinelDiffPage));
        Assert.Equal(typeof(SentinelApplicationPage), page.ParentType);
        Assert.Equal("diff", page.Slug);
        Assert.Equal("@xperiencecommunity/sentinel-admin/Diff", page.TemplateName);
    }

    [Fact]
    public void Sentinel_listing_pages_are_siblings_under_the_same_parent()
    {
        var parents = AdminAssembly.GetCustomAttributes<UIPageAttribute>()
            .Where(p => p.Type == typeof(ScanHistoryListingPage) || p.Type == typeof(FindingsListingPage))
            .Select(p => p.ParentType)
            .Distinct()
            .ToArray();

        // Both pages must resolve to SentinelApplicationPage for the nav tree to render them as
        // children of the single "Sentinel" app rather than leaking to another category.
        var parent = Assert.Single(parents);
        Assert.Equal(typeof(SentinelApplicationPage), parent);
    }

    // Helper sourced locally so the test file doesn't leak a using directive just for one static
    // method. RuntimeHelpers.GetUninitializedObject sidesteps the ListingPage base ctor, which
    // would otherwise try to resolve admin services we don't have in a unit-test harness.
    private static class RuntimeHelpers
    {
        public static object GetUninitializedObject(Type type) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
    }
}
