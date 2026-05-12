using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

using XperienceCommunity.Sentinel.Admin;
using XperienceCommunity.Sentinel.Admin.UIPages;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelFinding;

[assembly: UIPage(
    parentType: typeof(SentinelApplicationPage),
    slug: "findings",
    uiPageType: typeof(FindingsListingPage),
    name: "Findings",
    templateName: TemplateNames.LISTING,
    order: 200)]

namespace XperienceCommunity.Sentinel.Admin.UIPages;

/// <summary>
/// Lists every <see cref="SentinelFindingInfo"/> across all scans with filter + sort. Admins
/// typically drill in by Severity or RuleID. The built-in LISTING template surfaces the
/// searchable columns as a free-text filter bar above the table — no extra plumbing required
/// from us, just mark searchable: true on the columns that make sense.
/// </summary>
[UIEvaluatePermission(SystemPermissions.VIEW)]
public class FindingsListingPage : ListingPage
{
    protected override string ObjectType => SentinelFindingInfo.OBJECT_TYPE;

    public override Task ConfigurePage()
    {
        // Default to newest scan first so the current run's findings are at the top — admins
        // investigating a just-completed scheduled scan don't have to sort to find what
        // triggered the email digest. ScanRunID is monotonically increasing so it sorts the
        // same way as CompletedAt without needing a column join.
        PageConfiguration.ColumnConfigurations
            .AddColumn(nameof(SentinelFindingInfo.SentinelFindingSeverity), "Severity", sortable: true, searchable: true)
            .AddColumn(nameof(SentinelFindingInfo.SentinelFindingRuleID), "Rule", sortable: true, searchable: true)
            .AddColumn(nameof(SentinelFindingInfo.SentinelFindingCategory), "Category", sortable: true, searchable: true)
            .AddColumn(nameof(SentinelFindingInfo.SentinelFindingMessage), "Message", searchable: true)
            .AddColumn(nameof(SentinelFindingInfo.SentinelFindingLocation), "Location", searchable: true)
            .AddColumn(nameof(SentinelFindingInfo.SentinelFindingScanRunID), "Scan #", sortable: true, defaultSortDirection: SortTypeEnum.Desc);

        return base.ConfigurePage();
    }
}
