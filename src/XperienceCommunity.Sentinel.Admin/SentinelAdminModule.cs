using CMS;

using Kentico.Xperience.Admin.Base;

[assembly: RegisterModule(typeof(XperienceCommunity.Sentinel.Admin.SentinelAdminModule))]

namespace XperienceCommunity.Sentinel.Admin;

/// <summary>
/// Kentico-admin module entry point. Inheriting <see cref="AdminModule"/> + calling
/// <see cref="AdminModule.RegisterClientModule"/> wires the embedded React bundle
/// (<c>Client/dist/**</c> via <c>&lt;AdminClientPath&gt;</c>) so templates like
/// <c>@xperiencecommunity/sentinel-admin/Dashboard</c> resolve at runtime.
///
/// <para>
/// The <c>orgName</c> + <c>projectName</c> arguments MUST match, verbatim, in all four
/// locations — any drift renders a silent blank page:
/// <list type="bullet">
///   <item><see cref="AdminModule"/> constructor name below</item>
///   <item><c>&lt;AdminOrgName&gt;</c> in <c>XperienceCommunity.Sentinel.Admin.csproj</c></item>
///   <item><c>&lt;AdminClientPath ProjectName="..."/&gt;</c> in the same csproj</item>
///   <item><c>orgName</c> + <c>projectName</c> in <c>Client/webpack.config.js</c></item>
///   <item>The <c>templateName</c> prefix on every <c>[UIPage]</c> attribute</item>
/// </list>
/// </para>
/// </summary>
internal sealed class SentinelAdminModule : AdminModule
{
    public SentinelAdminModule() : base(nameof(SentinelAdminModule)) { }

    protected override void OnInit()
    {
        base.OnInit();
        RegisterClientModule("xperiencecommunity", "sentinel-admin");
    }
}
