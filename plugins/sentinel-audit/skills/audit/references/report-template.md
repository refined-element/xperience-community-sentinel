# Report template reference

This file governs `audit-report.md` and `audit-report.html`, the two human-readable
deliverables the audit skill writes to `<target>/sentinel-audit/` (alongside
`audit-findings.json`, which is the data source for both). Follow it exactly: the
section order is fixed, the two copy blocks are mandatory and verbatim, and the
HTML must reuse the palette in "Visual identity" so the two reports a client
receives — the raw Sentinel scan HTML and this audit report — read as one family
of documents rather than two unrelated tools.

Everything both reports state must trace back to `audit-findings.json` (see
`findings-schema.json`). Don't introduce a fact, a score, or a finding that isn't
in that file.

Write narrative prose — the executive summary and each dimension's narrative — in
clear, direct, professional language for a stakeholder audience: active voice,
present tense, no unexplained jargon, no filler words ("simply," "just," "easily").
The instructions in this file are for the agent writing the report; the report's
own prose is for the client reading it. Keep both registers clean, but don't
confuse them — instructions here stay imperative, report prose stays in the third
person about the audited project.

## Visual identity

Source: `src/XperienceCommunity.Sentinel.Core/Reporting/HtmlReportWriter.cs`, which
renders the Sentinel CLI's own `report.html`. Reuse this palette, font stack, and
layout approach for `audit-report.html` — don't invent a new one.

### Palette

CSS custom properties defined on `:root` in `HtmlReportWriter.cs`:

| Token | Hex | Used for |
|---|---|---|
| `--bg` | `#0a0a0f` | Page background; also the text color on solid/filled pills |
| `--panel` | `#161b22` | Card, table, and section backgrounds |
| `--panel-border` | `#30363d` | Borders on cards, tables, and the hero divider |
| `--text` | `#e6edf3` | Primary body text |
| `--muted` | `#8b949e` | Secondary text, labels, captions |
| `--accent` | `#afd66d` | Defined but not referenced elsewhere in the stylesheet — a reserved/duller lime, not the one actually applied to links or buttons |
| `--accent-2` | `#D6F08D` | The lime actually applied: links, the hero logo mark, the CTA button, and focus outlines |
| `--error` | `#f85149` | Error-severity pills, error-severity finding left borders |
| `--warning` | `#d29922` | Warning-severity pills and left borders |
| `--info` | `#8b949e` | Info-severity pills and left borders (same value as `--muted`) |
| `--ok` | `#4ade80` | The "no issues found" success state |

Note the `--accent`/`--accent-2` split above: it's a real inconsistency in the
source file (one token is declared, a brighter second one is what's actually
used), not a design decision to replicate. Use `--accent-2` (`#D6F08D`) wherever
the source uses it — links, the primary CTA, focus rings — and treat `--accent`
as unused, matching the source. The button hover state in the source is a
hardcoded `#c4de7b`, not a token; carry that value forward as-is if the report
needs a hover state.

### Typography

- Body: `-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif`, base
  size 15px, line height 1.55.
- Code, IDs, file paths, rule IDs: `ui-monospace, SFMono-Regular, Consolas, monospace`,
  13px (the source's `.mono` / `code` rule).
- No external font requests (no Google Fonts, no CDN) — the stack above is
  system fonts only, so it needs nothing to load.

### Layout

- Single column, `max-width: 1100px`, centered, `padding: 32px 24px 64px`.
- Dark page background (`--bg`) full-bleed; content sits in `--panel` cards with
  a 1px `--panel-border`, `border-radius` 8–10px.
- Metadata (the cover block) renders as a label/value grid, mirroring the
  source's `.hero .meta` pattern: muted label, bold-ish value, wrapping into
  columns on narrow viewports (`repeat(auto-fit, minmax(220px, 1fr))`).
- Status and severity render as small pill badges: `border-radius: 999px`,
  uppercase text, `font-size: 11px`, `font-weight: 700`, `letter-spacing: 0.04em`.
- Section headings: `h2` at 18px with generous top margin (32px) for the major
  report sections; `h3` at 15px, uppercase, muted color, letter-spacing 0.06em
  for sub-groupings (the source uses this pattern for grouping findings by
  category).
- Tables: `border-collapse: collapse`, `--panel` background, `--panel-border`
  border and row dividers, header row text in `--muted`.

### Extending the palette to four severities

`HtmlReportWriter.cs` only styles Sentinel's own three-level scale (Error /
Warning / Info). This report uses the unified four-level scale from
`grading.md` (Critical / High / Medium / Low), so extend the palette rather than
introduce new colors: reuse the same three hues, and give Critical a heavier
visual weight than High so the two don't look identical.

| Severity | Token | Treatment | Example CSS |
|---|---|---|---|
| Critical | `--error` (`#f85149`) | Solid fill — the strongest weight on the page | `background: var(--error); color: var(--bg);` |
| High | `--error` (`#f85149`) | Tinted, same as the source's own `.pill.error` | `background: rgba(248,81,73,0.14); color: var(--error);` |
| Medium | `--warning` (`#d29922`) | Tinted, same as the source's own `.pill.warning` | `background: rgba(210,153,34,0.15); color: var(--warning);` |
| Low | `--info` (`#8b949e`) | Tinted, same as the source's own `.pill.info` | `background: rgba(139,148,158,0.15); color: var(--muted);` |

Apply the same four-way mapping to a finding's left border (the source's
`.finding.warning` / `.finding.error` pattern) at `border-left: 3px solid <token>`.

### What not to carry over from `HtmlReportWriter.cs`

- **No embedded submission form.** The source embeds a live "Request a fix
  quote" form that POSTs to a Refined Element endpoint with an inline `<script>`.
  `audit-report.html` is a static document — its call to action is the CTA
  block in "Next steps" (plain text and links, pointing at the `sentinel quote`
  CLI command and the Architecture Review & Roadmap URL), not a second embedded
  submission flow. Don't add a `<form>`, a `<script>` that calls `fetch`, or any
  JSON payload island to this report.
- **`<title>` differs.** The source always emits `Sentinel for Xperience by
  Kentico Report`. This report's `<title>` is `Health audit — <site name>` (see
  "HTML report" below).

## Markdown report: `audit-report.md`

Write these sections, in this exact order, with nothing inserted before, between,
or after them except the conditional partial-audit banner:

```markdown
# Xperience by Kentico health audit — <site name>

<cover block>

<partial-audit banner — only when sentinel.runtimeEnabled is false>

## Executive summary
## Scorecard
## Priority findings
## Architecture & configuration
## Content model
## Security
## Performance
## Remediation roadmap
## Methodology
## Next steps
```

The four dimension sections use these exact display names as headings, in this
order, always — even a dimension with zero findings still gets its section
(state that it had no findings rather than omitting the heading):

| Dimension key (in `audit-findings.json`) | Display name (heading) |
|---|---|
| `architecture` | Architecture & configuration |
| `contentModel` | Content model |
| `security` | Security |
| `performance` | Performance |

### Cover block

Directly under the H1, a label/value block, in this order, each value read
straight from `audit-findings.json` — don't recompute or re-derive a value that's
already in the file:

| Label | Value comes from |
|---|---|
| Repo | `target.repoPath` |
| Xperience by Kentico version | `target.xbykVersion` |
| Scan mode | `Full` when `sentinel.runtimeEnabled` is `true`, `Partial` when `false` |
| Sentinel version | `sentinel.version` |
| Audit skill version | `auditSkillVersion` |
| Date | `generatedAt`, formatted as an unambiguous date (for example `August 25, 2026`), not the raw ISO timestamp and not a relative phrase like "today" |

`<site name>` (in the H1 and, for the HTML report, the `<title>`) isn't a field
in `audit-findings.json`. Derive it in this order of preference: a name the user
gave explicitly, the last path segment of `target.repoPath`, or the solution or
`.csproj` file name with its extension stripped.

### Partial-audit banner

Include this line immediately after the cover block, and only when
`sentinel.runtimeEnabled` is `false` in `audit-findings.json` — never when it's
`true`, and never anywhere else in the report:

```markdown
> ⚠ Partial audit — runtime checks skipped.
```

A short follow-on sentence explaining why (for example, that no database
connection was available, so Sentinel's runtime-only rules and the AI checks
that depend on live CMS data didn't run) may follow on the next blockquote line,
but the line above must appear verbatim as its own line either way.

### Executive summary

5–8 sentences, written for a stakeholder rather than a developer: no check IDs
(`AUD-SEC-004`), no Sentinel rule codes (`CFG003`), no file paths. Cover the
overall grade, the most urgent risk area, and one or two genuine strengths.
State findings in plain language ("the site stores a database password in a
tracked configuration file") instead of naming the rule that caught it.

### Scorecard

One table: the four dimensions in the display-name order above, each with its
score and letter grade, plus an Overall row. Dimension values come from
`grades.dimensions.<key>.{score,grade}`. The Overall row's score comes from
`grades.overall`, which `findings-schema.json` types as a bare integer; derive
its letter from that score using `grading.md`'s 90/80/70/60 bands.

```markdown
| Dimension | Score | Grade |
|---|---|---|
| Architecture & configuration | 82 | B |
| Content model | 91 | A |
| Security | 61 | D |
| Performance | 88 | B |
| **Overall** | **81** | **B** |
```

(The row values above are an example, not a template to copy literally — use
the real numbers from `audit-findings.json`.)

### Priority findings

Every finding with `severity` of `Critical` or `High`, from every dimension,
in one table using the shared findings-table format (below). Order
deterministically so two runs against an unchanged repo produce the same table:
severity descending (Critical before High), then dimension in the display-name
order from the table above, then `id` ascending within a tied dimension.

Immediately under this table, include the effort/fixable legend (below) once —
don't repeat it in the later dimension sections.

### The dimension sections

For each of the four dimension sections, in order: 2–4 sentences of narrative
naming that dimension's score and grade and describing the standout pattern in
its findings (a cluster of similar issues, a single severe one, or genuinely
clean results), followed by one findings table listing every finding assigned
to that dimension (not only Critical/High — Priority findings already covers
those; this table is the complete record for the dimension). Order the table
severity descending, then `id` ascending. A dimension with zero findings still
gets its narrative sentence and heading — state plainly that no findings were
recorded, don't just skip the table.

### Findings table format (shared)

Every findings table in this report — Priority findings and each dimension
section — uses these columns, in this order:

```markdown
| ID | Severity | Title | Location | Effort | Fixable |
|---|---|---|---|---|---|
| AUD-SEC-004 | High | Contact endpoint has no rate limit | `Controllers/ContactController.cs` | S | assisted |
```

(The row above is an example, not a template to copy literally — the ID/topic
pairing is illustrative, not the real AUD-SEC-004 check; use the real finding
data from `audit-findings.json`.)

- **ID** — `finding.id`.
- **Severity** — `finding.severity`, exactly as stored (`Critical`/`High`/`Medium`/`Low`).
- **Title** — `finding.title`.
- **Location** — `finding.location`; render as an em dash (`—`) when it's `null`
  (a repo-wide finding rather than a specific file or line).
- **Effort** — `finding.effort` (`S`/`M`/`L`).
- **Fixable** — `finding.fixable` (`auto`/`assisted`/`manual`).

`finding.message` and `finding.remediation` aren't table columns — they're
prose. Fold the message into the dimension's narrative when it adds context
beyond the title, and cite `finding.remediation` in the Remediation roadmap
entry for that finding rather than repeating it in every table row.

Legend (include once, directly under the Priority findings table):

```markdown
Effort — S (small), M (medium), or L (large). Fixable — auto (a scripted or
mechanical fix), assisted (a developer applies the remediation text directly),
or manual (requires human judgment or a stakeholder decision).
```

### Remediation roadmap

Two lists, drawn from every finding in the report regardless of severity, split
by `effort`:

- **Quick wins** — every finding with `effort: "S"`.
- **Projects** — every finding with `effort: "M"` or `effort: "L"`.

Order each list the same way as Priority findings (severity descending, then
dimension, then `id`). Format each entry as a single bullet, not a table row:

```markdown
- **High** — AUD-SEC-004: Contact endpoint has no rate limit (Security)
```

(The entry above is an example, not a template to copy literally — the ID/topic
pairing is illustrative, not the real AUD-SEC-004 check; use the real findings
from `audit-findings.json`.)

### Methodology

State, in prose:

- **What ran.** The Sentinel CLI version and scan mode (full or partial, and if
  partial, what that means for the findings below), plus the four AI checklists
  by name (`architecture-config.md`, `content-model.md`, `security.md`,
  `performance.md`) and how many checks from each fired a finding versus how
  many were evaluated — count these directly from the checklist files at audit
  time rather than hardcoding a number here, since the checklists can grow.
- **Citations.** Don't link to the checklist files themselves or to any
  `references/*.md` path — those live inside the audit skill's own plugin
  directory, not in the client's repository, so a link to them won't resolve
  for someone reading the delivered report. Instead, link to the audit skill's
  public source: [github.com/refined-element/xperience-community-sentinel](https://github.com/refined-element/xperience-community-sentinel).
  Per-finding source citations (the Kentico/Microsoft documentation a specific
  check cites) live in that finding's `references` array and belong in its
  remediation text, not in this section.
- **Grading formula summary.** One short paragraph restating `grading.md`'s
  formula: each dimension starts at 100, loses 25 points per Critical finding
  and 10 per High finding (both uncapped), and loses 4 points per Medium
  finding and 1 point per Low finding grouped and capped per rule ID (12
  points per Medium rule, 5 points per Low rule) so high-volume hygiene
  findings from one rule can't outweigh real defects, all floored at 0; scores
  convert to letter grades at 90/80/70/60; the overall score is the unweighted
  mean of the four dimension scores, rounded half-up.
- **Unmapped-rule appendix.** For every Sentinel finding whose rule ID isn't in
  `grading.md`'s dimension-mapping table, state the rule ID, that it was
  defaulted to the Architecture & configuration dimension per that table's
  default rule, and that the mapping table should be extended to cover it
  explicitly. When no unmapped rule occurred during this audit, say so — write
  "No unmapped Sentinel rules were encountered during this audit" rather than
  omitting the appendix silently.

Close the Methodology section with this disclaimer, verbatim:

> This report is an AI-assisted initial pass produced by the open-source Sentinel audit skill. It is not a substitute for a human architect review.
>
> The AI checklist findings are a floor, not a census: a check that isn't listed here wasn't found to fail, which is not the same as a verified pass.

### Next steps

Content: the CTA block below, verbatim, and nothing else — no lead-in sentence,
no summary, no additional offer beyond the two named here. The report offers exactly
the two follow-on paths above — never add any other product, service, or offer.

> **Fix these findings at a fixed price.** Run `sentinel quote` to send a sanitized summary of this report to Refined Element and receive an itemized quote.
>
> **Have the architect validate this audit.** The [Architecture Review & Roadmap](https://foundry.refinedelement.com) engagement reviews, extends, and prioritizes these findings with you.

## HTML report: `audit-report.html`

- **Self-contained, single file.** All CSS inline in a `<style>` block built
  from "Visual identity" above. No external stylesheet, font, script, or image
  request — no CDN, no Google Fonts link, nothing that requires network access
  to render correctly offline.
- **No script.** Unlike `HtmlReportWriter.cs`'s scan report, this report needs
  no JavaScript at all — there's no embedded submission form to wire up (see
  "What not to carry over" above). If nothing in the report needs interactivity,
  don't add a `<script>` tag just because the source file has one.
- **Same section order as the Markdown report,** including the conditional
  partial-audit banner under the same rule (present only when
  `sentinel.runtimeEnabled` is `false`). Style the banner as a callout using the
  warning tokens, mirroring the source's own `.empty` success-callout pattern:
  `background: rgba(210,153,34,0.12); border: 1px solid rgba(210,153,34,0.3); color: var(--warning); border-radius: 8px; padding: 16px;`.
- **Severity color coding** for every pill and finding-card left border must use
  the four-tier mapping in "Extending the palette to four severities" above —
  don't fall back to Sentinel's native three-tier classes for this report.
- **`<title>`** is `Health audit — <site name>`, using the same site-name
  derivation as the Markdown cover block.
- **Tables and cover block** render as HTML `<table>` and a label/value grid
  respectively, following the same content and ordering rules as the Markdown
  sections above — the two report formats must never disagree on a fact, only
  on presentation.
- Written to the same `sentinel-audit/` output directory as `audit-report.md`.
