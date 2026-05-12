using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Module.Notifications;

public interface ISentinelEmailDigestSender
{
    Task SendAsync(ScanRunSummary run, IReadOnlyList<Finding> findings, CancellationToken cancellationToken);
}
