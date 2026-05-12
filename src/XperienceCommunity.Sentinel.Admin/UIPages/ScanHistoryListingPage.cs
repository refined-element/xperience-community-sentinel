using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

using XperienceCommunity.Sentinel.Admin;
using XperienceCommunity.Sentinel.Admin.UIPages;
using XperienceCommunity.Sentinel.Module.InfoModels.SentinelScanRun;

[assembly: UIPage(
    parentType: typeof(SentinelApplicationPage),
    slug: "scans",
    uiPageType: typeof(ScanHistoryListingPage),
    name: "Scan history",
    templateName: TemplateNames.LISTING,
    order: UIPageOrder.First)]

namespace XperienceCommunity.Sentinel.Admin.UIPages;

/// <summary>
/// Lists every <see cref="SentinelScanRunInfo"/> row — including in-progress, failed, and
/// cancelled runs — newest first. The <c>Status</c> column is exposed so admins can tell at a
/// glance which executions actually completed vs. which bailed. Reuses Kentico's built-in
/// LISTING template so this page needs no client-side React bundle; the admin shell renders
/// columns + filter + sort out of the box, and we only configure which columns show.
/// </summary>
[UIEvaluatePermission(SystemPermissions.VIEW)]
public class ScanHistoryListingPage : ListingPage
{
    protected override string ObjectType => SentinelScanRunInfo.OBJECT_TYPE;

    public override Task ConfigurePage()
    {
        // Only ONE column carries a default sort so Kentico's listing framework picks the right
        // default unambiguously. SentinelScanRunID is monotonically increasing with StartedAt
        // (scan rows are only written by the service, never backdated), so sorting by ID desc
        // gives the same "newest first" ordering an admin would expect from "Started desc" —
        // without the ambiguity of two defaultSortDirection declarations.
        PageConfiguration.ColumnConfigurations
            .AddColumn(nameof(SentinelScanRunInfo.SentinelScanRunID), "#", sortable: true, defaultSortDirection: SortTypeEnum.Desc)
            .AddColumn(nameof(SentinelScanRunInfo.SentinelScanRunStartedAt), "Started", sortable: true)
            .AddColumn(nameof(SentinelScanRunInfo.SentinelScanRunStatus), "Status", sortable: true, searchable: true)
            .AddColumn(nameof(SentinelScanRunInfo.SentinelScanRunTrigger), "Trigger", sortable: true, searchable: true)
            .AddColumn(nameof(SentinelScanRunInfo.SentinelScanRunTotalFindings), "Total", sortable: true)
            .AddColumn(nameof(SentinelScanRunInfo.SentinelScanRunErrorCount), "Errors", sortable: true)
            .AddColumn(nameof(SentinelScanRunInfo.SentinelScanRunWarningCount), "Warnings", sortable: true)
            .AddColumn(nameof(SentinelScanRunInfo.SentinelScanRunInfoCount), "Info", sortable: true)
            .AddColumn(nameof(SentinelScanRunInfo.SentinelScanRunDurationSeconds), "Duration (s)", sortable: true)
            .AddColumn(nameof(SentinelScanRunInfo.SentinelScanRunSentinelVersion), "Version", sortable: true);

        // No row-click edit action — scan-run rows are read-only history. Admins drill into
        // findings via the sibling Findings listing page (filtered by Scan run on demand).
        return base.ConfigurePage();
    }
}
