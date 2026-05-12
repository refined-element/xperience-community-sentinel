using System.Text;

using CMS.EmailEngine;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using XperienceCommunity.Sentinel.Core;
using XperienceCommunity.Sentinel.Module.Configuration;

namespace XperienceCommunity.Sentinel.Module.Notifications;

internal sealed class SentinelEmailDigestSender(
    IEmailService emailService,
    IOptions<SentinelOptions> options,
    ILogger<SentinelEmailDigestSender> logger) : ISentinelEmailDigestSender
{
    private readonly SentinelOptions options = options.Value;

    public async Task SendAsync(ScanRunSummary run, IReadOnlyList<Finding> findings, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Severity>(options.EmailDigest.SeverityThreshold, ignoreCase: true, out var threshold))
        {
            threshold = Severity.Warning;
        }

        var digestWorthy = findings.Where(f => f.Severity >= threshold).ToArray();
        if (options.EmailDigest.OnlyWhenThresholdFindings && digestWorthy.Length == 0)
        {
            logger.LogInformation("Sentinel digest suppressed — no findings at or above {Threshold} severity.", threshold);
            return;
        }

        var html = RenderHtml(run, findings, digestWorthy);
        var subject = $"Sentinel scan #{run.RunId}: " +
                      $"{run.TotalFindings} findings " +
                      $"({run.ErrorCount}E/{run.WarningCount}W/{run.InfoCount}I)";

        foreach (var recipient in options.EmailDigest.Recipients.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            // Observe cancellation between recipients so a cancelled scheduled task doesn't keep
            // emailing. Kentico's IEmailService.SendEmail doesn't accept a CancellationToken in the
            // current XbyK 31.x API; the gap here is between sends, not during one.
            cancellationToken.ThrowIfCancellationRequested();

            var message = new EmailMessage
            {
                Recipients = recipient,
                Subject = subject,
                Body = html,
                Priority = EmailPriorityEnum.Normal,
                MailoutGuid = Guid.NewGuid(),
            };

            try
            {
                await emailService.SendEmail(message).ConfigureAwait(false);
                logger.LogInformation("Sentinel digest sent to {Recipient} for run {RunId}.", recipient, run.RunId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sentinel digest send to {Recipient} failed for run {RunId}.", recipient, run.RunId);
            }
        }
    }

    internal static string RenderHtml(ScanRunSummary run, IReadOnlyList<Finding> findings, IReadOnlyList<Finding> highlighted)
    {
        var sb = new StringBuilder(8 * 1024);

        sb.Append("<!DOCTYPE html><html><body style=\"font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;color:#1a1a2e;background:#f5f5f7;margin:0;padding:32px 0;\">");
        sb.Append("<div style=\"max-width:600px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px;\">");

        // Schedule is owned by Kentico's Scheduled Tasks app — cadence may not be weekly.
        sb.Append("<h2 style=\"color:#1a1a2e;margin:0 0 12px;\">Sentinel scan digest</h2>");
        sb.Append($"<p style=\"color:#555;margin:0 0 24px;font-size:14px;\">Scan #{run.RunId} completed {run.CompletedAtUtc:yyyy-MM-dd HH:mm} UTC.</p>");

        sb.Append("<table cellspacing=\"0\" cellpadding=\"0\" style=\"width:100%;border-collapse:collapse;margin:0 0 24px;\">");
        AppendRow(sb, "Total findings", run.TotalFindings.ToString());
        AppendRow(sb, "Errors", run.ErrorCount.ToString(), color: "#c0392b");
        AppendRow(sb, "Warnings", run.WarningCount.ToString(), color: "#b98422");
        AppendRow(sb, "Info", run.InfoCount.ToString(), color: "#555");
        AppendRow(sb, "Trigger", run.Trigger);
        sb.Append("</table>");

        if (highlighted.Count > 0)
        {
            sb.Append("<h3 style=\"font-size:15px;margin:24px 0 12px;\">Sample findings</h3>");
            foreach (var f in highlighted.Take(10))
            {
                var color = f.Severity switch { Severity.Error => "#c0392b", Severity.Warning => "#b98422", _ => "#555" };
                sb.Append($"<div style=\"padding:10px 12px;margin:6px 0;background:#f9f9fb;border-left:3px solid {color};border-radius:4px;\">");
                sb.Append($"<strong style=\"color:{color};\">[{f.Severity.ToString().ToUpperInvariant()}]</strong> ");
                sb.Append($"<code style=\"color:#555;\">{HtmlEncode(f.RuleId)}</code> — {HtmlEncode(f.Message)}");
                sb.Append("</div>");
            }
            if (highlighted.Count > 10)
            {
                sb.Append($"<p style=\"color:#555;font-size:13px;margin:10px 0 0;\">… and {highlighted.Count - 10} more at or above threshold. See the Kentico Event log (source = <code>Sentinel</code>) or the <code>XperienceCommunity_Sentinel*</code> tables in the CMS database for the full list.</p>");
            }
        }

        // Headless mode only in this release — findings are persisted in the XperienceCommunity_Sentinel*
        // tables and mirrored into CMS_EventLog. Admin UI with acknowledgment + contact flow arrives
        // in a follow-up release; this message is rewritten then.
        sb.Append("<p style=\"color:#555;font-size:13px;margin:24px 0 0;\">Full findings are in the Kentico Event log (source = <code>Sentinel</code>) and the <code>XperienceCommunity_Sentinel*</code> tables in the CMS database.</p>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string label, string value, string? color = null)
    {
        // Encode both cells — label is always a literal in our code today, but value can be a
        // user-supplied Trigger string and future rows may inline anything.
        sb.Append("<tr>");
        sb.Append($"<td style=\"padding:6px 0;color:#8b8b95;width:140px;font-size:13px;\">{HtmlEncode(label)}</td>");
        sb.Append($"<td style=\"padding:6px 0;color:{color ?? "#1a1a2e"};font-weight:600;\">{HtmlEncode(value)}</td>");
        sb.Append("</tr>");
    }

    private static string HtmlEncode(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}
