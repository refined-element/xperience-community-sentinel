using System.Net;
using System.Text;
using System.Text.Json;
using XperienceCommunity.Sentinel.Quoting;

namespace XperienceCommunity.Sentinel.Reporting;

/// <summary>
/// Emits a self-contained HTML report (inline CSS, no external assets) styled with Refined Element brand
/// accents. Groups findings by category, ordered by severity. Includes an embedded email form that POSTs
/// directly to the Refined Element quote endpoint — no CLI follow-up required.
/// </summary>
public static class HtmlReportWriter
{
    private static readonly JsonSerializerOptions EmbedOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task WriteAsync(ReportDocument doc, string outputPath, string quoteEndpoint, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var html = Render(doc, quoteEndpoint);
        await File.WriteAllTextAsync(outputPath, html, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private static string Render(ReportDocument doc, string quoteEndpoint)
    {
        // Pre-compute both submission shapes at generation time so the privacy boundary lives in C#,
        // not browser JS. The email field is stripped out before embedding — the form injects it.
        var sanitizedPayload = BuildEmbeddedPayload(doc, includeContext: false);
        var fullPayload = BuildEmbeddedPayload(doc, includeContext: true);
        var eligibleCount = doc.Findings.Count(f => f.QuoteEligible);

        var sb = new StringBuilder(32 * 1024);
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>Sentinel for Xperience by Kentico Report</title>");
        sb.Append("<style>").Append(Css).Append("</style>");
        sb.Append("</head><body>");

        RenderHeader(sb, doc);
        RenderSummary(sb, doc);
        RenderExecutions(sb, doc);
        RenderFindings(sb, doc);
        RenderSubmitForm(sb, sanitizedPayload, fullPayload, quoteEndpoint, eligibleCount);
        RenderFooter(sb);

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string BuildEmbeddedPayload(ReportDocument doc, bool includeContext)
    {
        var submission = QuoteSanitizer.Sanitize(doc, contactEmail: string.Empty, includeContext);
        // Serialize without the email — the form JS injects it at submission time.
        var shape = new
        {
            sentinelVersion = submission.SentinelVersion,
            scan = submission.Scan,
            summary = submission.Summary,
            findings = submission.Findings,
            includesContext = submission.IncludesContext,
        };
        return JsonSerializer.Serialize(shape, EmbedOptions);
    }

    // Radar display mark: circular bezel with range rings, faint crosshair, a filled sweep wedge,
    // and a single detected target blip. Uses currentColor so the brand accent of the surrounding
    // context drives the colour.
    private const string LogoSvg =
        "<svg class=\"mark\" viewBox=\"0 0 32 32\" xmlns=\"http://www.w3.org/2000/svg\" aria-hidden=\"true\" fill=\"none\" stroke=\"currentColor\">" +
        // Sweep wedge (filled, ~60deg from 12 to 2 o'clock, drawn first so rings overlay).
        "<path d=\"M 16 16 L 16 2 A 14 14 0 0 1 29 10 Z\" fill=\"currentColor\" opacity=\"0.28\" stroke=\"none\"/>" +
        // Crosshair.
        "<line x1=\"2\" y1=\"16\" x2=\"30\" y2=\"16\" stroke-width=\"0.5\" opacity=\"0.3\"/>" +
        "<line x1=\"16\" y1=\"2\" x2=\"16\" y2=\"30\" stroke-width=\"0.5\" opacity=\"0.3\"/>" +
        // Range rings.
        "<circle cx=\"16\" cy=\"16\" r=\"14\" stroke-width=\"1.75\"/>" +
        "<circle cx=\"16\" cy=\"16\" r=\"9\" stroke-width=\"0.75\" opacity=\"0.55\"/>" +
        "<circle cx=\"16\" cy=\"16\" r=\"4.5\" stroke-width=\"0.75\" opacity=\"0.55\"/>" +
        // Sweep leading edge.
        "<line x1=\"16\" y1=\"16\" x2=\"29\" y2=\"10\" stroke-width=\"1.75\" stroke-linecap=\"round\"/>" +
        // Center dot + detected blip.
        "<circle cx=\"16\" cy=\"16\" r=\"1.5\" fill=\"currentColor\" stroke=\"none\"/>" +
        "<circle cx=\"21\" cy=\"10\" r=\"1.3\" fill=\"currentColor\" stroke=\"none\"/>" +
        "</svg>";

    private static void RenderHeader(StringBuilder sb, ReportDocument doc)
    {
        sb.Append("<header class=\"hero\">");
        sb.Append("<div class=\"brand\">").Append(LogoSvg).Append(" Sentinel for Xperience by Kentico</div>");
        sb.Append("<div class=\"tagline\">Static + runtime health scan for Xperience by Kentico</div>");
        sb.Append("<div class=\"meta\">");
        sb.Append("<div><span>Scanned</span> ").Append(Html(doc.Scan.RepoPath)).Append("</div>");
        sb.Append("<div><span>Completed</span> ").Append(doc.Scan.CompletedAt.ToString("yyyy-MM-dd HH:mm 'UTC'")).Append("</div>");
        sb.Append("<div><span>Duration</span> ").Append(doc.Scan.DurationSeconds.ToString("N2")).Append(" s</div>");
        sb.Append("<div><span>Runtime checks</span> ").Append(doc.Scan.RuntimeEnabled ? "enabled" : "skipped").Append("</div>");
        sb.Append("</div></header>");
    }

    private static void RenderSummary(StringBuilder sb, ReportDocument doc)
    {
        sb.Append("<section class=\"summary\">");
        sb.Append(Card("total", "Total findings", doc.Summary.Total.ToString()));
        sb.Append(Card("errors", "Errors", doc.Summary.Errors.ToString()));
        sb.Append(Card("warnings", "Warnings", doc.Summary.Warnings.ToString()));
        sb.Append(Card("info", "Info", doc.Summary.Info.ToString()));
        sb.Append("</section>");
    }

    private static string Card(string cls, string label, string value) =>
        $"<div class=\"card {cls}\"><div class=\"value\">{WebUtility.HtmlEncode(value)}</div><div class=\"label\">{WebUtility.HtmlEncode(label)}</div></div>";

    private static void RenderExecutions(StringBuilder sb, ReportDocument doc)
    {
        sb.Append("<section class=\"executions\"><h2>Checks</h2><table><thead><tr>");
        sb.Append("<th>Rule</th><th>Title</th><th>Kind</th><th>Status</th><th>Duration</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var e in doc.Executions)
        {
            var statusClass = e.Status.ToLowerInvariant();
            sb.Append("<tr>");
            sb.Append("<td class=\"mono\">").Append(Html(e.RuleId)).Append("</td>");
            sb.Append("<td>").Append(Html(e.Title)).Append("</td>");
            sb.Append("<td>").Append(Html(e.Kind)).Append("</td>");
            sb.Append("<td><span class=\"pill ").Append(statusClass).Append("\">").Append(Html(e.Status)).Append("</span></td>");
            sb.Append("<td class=\"mono\">").Append(e.DurationMs.ToString("N1")).Append(" ms</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table></section>");
    }

    private static void RenderFindings(StringBuilder sb, ReportDocument doc)
    {
        sb.Append("<section class=\"findings\"><h2>Findings</h2>");

        if (doc.Findings.Count == 0)
        {
            sb.Append("<p class=\"empty\">No issues found. Nicely done.</p></section>");
            return;
        }

        var grouped = doc.Findings
            .GroupBy(f => f.Category)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.Append("<h3>").Append(Html(group.Key)).Append("</h3>");
            var ordered = group
                .OrderByDescending(f => SeverityRank(f.Severity))
                .ThenBy(f => f.RuleId);

            foreach (var f in ordered)
            {
                var sev = f.Severity.ToLowerInvariant();
                sb.Append("<article class=\"finding ").Append(sev).Append("\">");
                sb.Append("<div class=\"row\">");
                sb.Append("<span class=\"pill ").Append(sev).Append("\">").Append(f.Severity.ToUpperInvariant()).Append("</span>");
                sb.Append("<span class=\"mono rule\">").Append(Html(f.RuleId)).Append("</span>");
                sb.Append("<span class=\"title\">").Append(Html(f.RuleTitle)).Append("</span>");
                if (!f.QuoteEligible)
                {
                    sb.Append("<span class=\"pill info\" title=\"Informational only — not included in `sentinel quote` submissions.\">INFO-ONLY</span>");
                }
                sb.Append("</div>");
                sb.Append("<p class=\"message\">").Append(Html(f.Message)).Append("</p>");
                if (!string.IsNullOrEmpty(f.Location))
                {
                    sb.Append("<p class=\"location\"><strong>Location:</strong> <span class=\"mono\">").Append(Html(f.Location)).Append("</span></p>");
                }
                if (!string.IsNullOrEmpty(f.Remediation))
                {
                    sb.Append("<p class=\"remediation\"><strong>Remediation:</strong> ").Append(Html(f.Remediation)).Append("</p>");
                }
                sb.Append("</article>");
            }
        }

        sb.Append("</section>");
    }

    private static void RenderSubmitForm(StringBuilder sb, string sanitizedPayload, string fullPayload, string quoteEndpoint, int eligibleCount)
    {
        if (eligibleCount == 0)
        {
            // Nothing quote-worthy — no submit UI.
            return;
        }

        sb.Append("<section class=\"submit\">");
        sb.Append("<h2>Request a fix quote</h2>");
        sb.Append("<p class=\"intro\">Send this report directly to <a href=\"https://refinedelement.com\">Refined Element</a>. ");
        sb.Append("We reply with an itemized, fixed-price remediation estimate within 3–5 business days. ");
        sb.Append($"{eligibleCount} finding{(eligibleCount == 1 ? "" : "s")} will be included (INFO-ONLY items are excluded automatically).</p>");

        sb.Append("<form id=\"sentinel-submit\" autocomplete=\"on\">");
        sb.Append("<label for=\"sentinel-email\">Your email</label>");
        sb.Append("<input type=\"email\" id=\"sentinel-email\" name=\"email\" required placeholder=\"you@yourcompany.com\" autocomplete=\"email\">");
        sb.Append("<label class=\"check\"><input type=\"checkbox\" id=\"sentinel-include-context\"> Include context (file paths + remediation text) — gives a more accurate quote but reveals more of your repo structure.</label>");
        sb.Append("<button type=\"submit\">Send report to Refined Element</button>");
        sb.Append("</form>");
        sb.Append("<div id=\"sentinel-result\" role=\"status\" aria-live=\"polite\"></div>");
        sb.Append("</section>");

        // JSON payload islands — the form JS reads whichever matches the checkbox state.
        sb.Append("<script type=\"application/json\" id=\"sentinel-payload-sanitized\">").Append(sanitizedPayload).Append("</script>");
        sb.Append("<script type=\"application/json\" id=\"sentinel-payload-full\">").Append(fullPayload).Append("</script>");

        // Form script. Endpoint is JSON-encoded to survive quoting.
        var endpointJson = JsonSerializer.Serialize(quoteEndpoint, EmbedOptions);
        sb.Append("<script>(function(){");
        sb.Append("const form=document.getElementById('sentinel-submit');");
        sb.Append("const out=document.getElementById('sentinel-result');");
        sb.Append("const endpoint=").Append(endpointJson).Append(";");
        sb.Append("form.addEventListener('submit',async e=>{e.preventDefault();");
        sb.Append("const email=document.getElementById('sentinel-email').value.trim();");
        sb.Append("if(!email){out.className='err';out.textContent='Email required.';return;}");
        sb.Append("const useCtx=document.getElementById('sentinel-include-context').checked;");
        sb.Append("const id=useCtx?'sentinel-payload-full':'sentinel-payload-sanitized';");
        sb.Append("const payload=JSON.parse(document.getElementById(id).textContent);");
        sb.Append("payload.contactEmail=email;");
        sb.Append("const btn=form.querySelector('button');btn.disabled=true;out.className='pending';out.textContent='Sending…';");
        sb.Append("try{");
        sb.Append("const r=await fetch(endpoint,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});");
        sb.Append("const b=await r.json().catch(()=>({}));");
        sb.Append("if(r.ok){out.className='ok';out.innerHTML='&#x2713; '+(b.message||'Report received.')+(b.id?' <span class=\"mono\">Ref: '+b.id+'</span>':'');form.style.display='none';}");
        sb.Append("else{out.className='err';out.textContent='Error: '+(b.error||r.statusText||('HTTP '+r.status));btn.disabled=false;}");
        sb.Append("}catch(err){out.className='err';out.textContent='Network error: '+err.message;btn.disabled=false;}");
        sb.Append("});})();</script>");
    }

    private static void RenderFooter(StringBuilder sb)
    {
        sb.Append("<footer>");
        sb.Append("<p class=\"cta\">Prefer the CLI? Run <code>sentinel quote</code> to submit from the terminal. ");
        sb.Append("<a href=\"https://refinedelement.com\">Refined Element</a> — Kentico Community Leaders 2025 &amp; 2026.</p>");
        sb.Append("<p class=\"fine\">Report generated by Sentinel for Xperience by Kentico · MIT licensed · ");
        sb.Append("<a href=\"https://github.com/refined-element/xperience-community-sentinel\">github.com/refined-element/xperience-community-sentinel</a></p>");
        sb.Append("</footer>");
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "Error" => 2,
        "Warning" => 1,
        _ => 0,
    };

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private const string Css = """
        :root {
            --bg: #0a0a0f;
            --panel: #161b22;
            --panel-border: #30363d;
            --text: #e6edf3;
            --muted: #8b949e;
            --accent: #afd66d;
            --accent-2: #D6F08D;
            --error: #f85149;
            --warning: #d29922;
            --info: #8b949e;
            --ok: #4ade80;
        }
        * { box-sizing: border-box; }
        html, body { margin: 0; padding: 0; background: var(--bg); color: var(--text); font: 15px/1.55 -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; }
        body { max-width: 1100px; margin: 0 auto; padding: 32px 24px 64px; }
        a { color: var(--accent-2); text-decoration: none; }
        a:hover { text-decoration: underline; }
        code, .mono { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; font-size: 13px; }

        .hero { border-bottom: 1px solid var(--panel-border); padding-bottom: 24px; margin-bottom: 28px; }
        .hero .brand { font-size: 22px; font-weight: 700; letter-spacing: -0.01em; display: flex; align-items: center; gap: 10px; }
        .hero .mark { width: 28px; height: 28px; color: var(--accent-2); flex-shrink: 0; }
        .hero .tagline { color: var(--muted); margin-top: 4px; }
        .hero .meta { margin-top: 18px; display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 10px 24px; color: var(--muted); font-size: 13px; }
        .hero .meta span { color: var(--text); margin-right: 6px; font-weight: 600; }

        .summary { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 32px; }
        @media (max-width: 700px) { .summary { grid-template-columns: repeat(2, 1fr); } }
        .card { background: var(--panel); border: 1px solid var(--panel-border); border-radius: 10px; padding: 18px 20px; }
        .card .value { font-size: 28px; font-weight: 700; letter-spacing: -0.02em; }
        .card .label { color: var(--muted); font-size: 13px; margin-top: 4px; }
        .card.errors .value { color: var(--error); }
        .card.warnings .value { color: var(--warning); }
        .card.info .value { color: var(--info); }

        h2 { font-size: 18px; margin: 32px 0 12px; letter-spacing: -0.01em; }
        h3 { font-size: 15px; color: var(--muted); margin: 18px 0 10px; text-transform: uppercase; letter-spacing: 0.06em; font-weight: 600; }

        table { width: 100%; border-collapse: collapse; background: var(--panel); border: 1px solid var(--panel-border); border-radius: 10px; overflow: hidden; }
        th, td { padding: 10px 14px; text-align: left; font-size: 13px; }
        thead { background: rgba(255,255,255,0.02); }
        th { font-weight: 600; color: var(--muted); }
        tbody tr { border-top: 1px solid var(--panel-border); }

        .pill { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.04em; }
        .pill.ran { background: rgba(63,185,80,0.12); color: var(--ok); }
        .pill.skippedruntime { background: rgba(139,148,158,0.15); color: var(--muted); }
        .pill.failed { background: rgba(248,81,73,0.12); color: var(--error); }
        .pill.error { background: rgba(248,81,73,0.14); color: var(--error); }
        .pill.warning { background: rgba(210,153,34,0.15); color: var(--warning); }
        .pill.info { background: rgba(139,148,158,0.15); color: var(--muted); }

        .finding { background: var(--panel); border: 1px solid var(--panel-border); border-left: 3px solid var(--info); border-radius: 8px; padding: 14px 16px; margin: 10px 0; }
        .finding.warning { border-left-color: var(--warning); }
        .finding.error { border-left-color: var(--error); }
        .finding .row { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; }
        .finding .rule { color: var(--muted); }
        .finding .title { font-weight: 600; }
        .finding .message { margin: 8px 0 6px; color: var(--text); }
        .finding .location, .finding .remediation { margin: 4px 0; color: var(--muted); font-size: 13px; }
        .finding .location strong, .finding .remediation strong { color: var(--text); font-weight: 600; }

        footer { margin-top: 48px; padding-top: 24px; border-top: 1px solid var(--panel-border); color: var(--muted); }
        footer .cta { font-size: 14px; color: var(--text); }
        footer code { background: var(--panel); padding: 2px 6px; border-radius: 4px; border: 1px solid var(--panel-border); }
        footer .fine { margin-top: 10px; font-size: 12px; }
        .empty { color: var(--ok); background: rgba(63,185,80,0.08); border: 1px solid rgba(63,185,80,0.25); padding: 16px; border-radius: 8px; }

        .submit { background: var(--panel); border: 1px solid var(--panel-border); border-radius: 10px; padding: 22px 24px; margin-top: 36px; }
        .submit h2 { margin-top: 0; }
        .submit .intro { color: var(--muted); margin: 0 0 16px; font-size: 14px; }
        .submit form { display: grid; gap: 12px; max-width: 520px; }
        .submit label { font-size: 13px; color: var(--muted); font-weight: 600; }
        .submit label.check { font-weight: 400; display: flex; gap: 8px; align-items: flex-start; line-height: 1.4; }
        .submit label.check input { margin-top: 3px; flex-shrink: 0; }
        .submit input[type=email] { padding: 10px 14px; font-size: 14px; background: var(--bg); color: var(--text); border: 1px solid var(--panel-border); border-radius: 6px; font-family: inherit; }
        .submit input[type=email]:focus { border-color: var(--accent-2); outline: none; }
        .submit button { padding: 10px 18px; font-size: 14px; font-weight: 600; background: var(--accent-2); color: var(--bg); border: 0; border-radius: 6px; cursor: pointer; transition: background 0.15s; }
        .submit button:hover:not(:disabled) { background: #c4de7b; }
        .submit button:disabled { opacity: 0.5; cursor: not-allowed; }
        #sentinel-result { margin-top: 14px; padding: 12px 14px; border-radius: 6px; font-size: 14px; display: none; }
        #sentinel-result.pending { display: block; background: rgba(139,148,158,0.12); color: var(--muted); }
        #sentinel-result.ok { display: block; background: rgba(63,185,80,0.12); color: var(--ok); border: 1px solid rgba(63,185,80,0.3); }
        #sentinel-result.err { display: block; background: rgba(248,81,73,0.12); color: var(--error); border: 1px solid rgba(248,81,73,0.3); }
        """;
}
