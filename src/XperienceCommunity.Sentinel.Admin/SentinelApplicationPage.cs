using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

[assembly: UIApplication(
    identifier: XperienceCommunity.Sentinel.Admin.SentinelApplicationPage.IDENTIFIER,
    type: typeof(XperienceCommunity.Sentinel.Admin.SentinelApplicationPage),
    slug: "sentinel",
    name: "Sentinel",
    category: BaseApplicationCategories.CONFIGURATION,
    icon: Icons.Bug,
    templateName: TemplateNames.SECTION_LAYOUT)]

namespace XperienceCommunity.Sentinel.Admin;

/// <summary>
/// Top-level Sentinel entry in the Kentico admin left-nav. <see cref="ApplicationPage"/> is a
/// stub — Kentico auto-redirects the admin to the first registered sub-page, which by order is
/// <see cref="UIPages.SentinelDashboardPage"/> (ordered at <c>UIPageOrder.First - 1</c>). The
/// class has no body or override surface. The attribute above registers the application with the
/// admin shell; the category (<see cref="BaseApplicationCategories.CONFIGURATION"/>) places it
/// alongside other ops tools like Scheduled tasks and Event log — where admins already look when
/// something is wrong.
/// </summary>
[UIPermission(SystemPermissions.VIEW)]
public class SentinelApplicationPage : ApplicationPage
{
    public const string IDENTIFIER = "XperienceCommunity.Sentinel.Admin";
}
