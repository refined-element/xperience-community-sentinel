using Microsoft.Data.SqlClient;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Content;

/// <summary>
/// CNT006 — Recent Kentico EventLog errors and warnings. Groups CMS_EventLog rows from the last
/// <see cref="ScanContext.EventLogDays"/> days by Source + EventCode. A single recurring exception
/// appears as one finding with its count, not N copies. The Kentico admin UI surfaces this table,
/// but no one actually goes looking unless something is obviously on fire.
/// </summary>
public sealed class EventLogCheck : ICheck
{
    private const int WarningQuoteThreshold = 20;

    public string RuleId => "CNT006";
    public string Title => "Recent Kentico EventLog errors";
    public string Category => "Observability";
    public CheckKind Kind => CheckKind.Runtime;

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();

        const string sql = @"
            SELECT
                el.EventType,
                el.Source,
                el.EventCode,
                COUNT_BIG(*)              AS Occurrences,
                MIN(el.EventTime)         AS FirstSeen,
                MAX(el.EventTime)         AS LastSeen,
                MAX(el.EventDescription)  AS SampleDescription
            FROM CMS_EventLog el
            WHERE el.EventTime > DATEADD(day, -@Days, SYSUTCDATETIME())
              AND el.EventType IN ('E', 'W')
            GROUP BY el.EventType, el.Source, el.EventCode
            ORDER BY
                CASE el.EventType WHEN 'E' THEN 0 ELSE 1 END,
                COUNT_BIG(*) DESC;";

        await using var connection = new SqlConnection(context.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Days", context.EventLogDays);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var eventType = reader.GetString(0);
            var source = reader.IsDBNull(1) ? "(unknown)" : reader.GetString(1);
            var eventCode = reader.IsDBNull(2) ? "(none)" : reader.GetString(2);
            var occurrences = reader.GetInt64(3);
            var firstSeen = reader.GetDateTime(4);
            var lastSeen = reader.GetDateTime(5);
            var sample = reader.IsDBNull(6) ? null : Truncate(reader.GetString(6), 300);

            var (severity, quoteEligible) = Classify(eventType, occurrences);
            var isError = eventType == "E";
            var label = isError ? "error" : "warning";

            var message = $"{source} / {eventCode}: {occurrences} {label}{(occurrences == 1 ? "" : "s")} in the last {context.EventLogDays} days " +
                          $"(first {firstSeen:yyyy-MM-dd}, latest {lastSeen:yyyy-MM-dd}).";

            var remediation = occurrences >= 10
                ? "A recurring issue at this volume usually indicates a bug or misconfiguration. Investigate the sample description and fix at the source."
                : "Low-volume — verify whether it's transient (deployment, cold start) or the leading edge of a larger problem.";

            findings.Add(new Finding(
                RuleId: RuleId,
                RuleTitle: Title,
                Category: Category,
                Severity: severity,
                Message: message,
                Location: sample,
                Remediation: remediation,
                QuoteEligible: quoteEligible));
        }

        return findings;
    }

    private static (Severity severity, bool quoteEligible) Classify(string eventType, long occurrences) =>
        eventType switch
        {
            "E" when occurrences > 10  => (Severity.Error,   true),
            "E"                        => (Severity.Warning, true),
            "W" when occurrences > 50  => (Severity.Warning, true),
            "W" when occurrences > WarningQuoteThreshold => (Severity.Info, true),
            _                          => (Severity.Info,   false),
        };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
