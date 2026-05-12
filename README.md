# Sentinel for Xperience by Kentico

<img src="docs/logo.svg" alt="Sentinel for Xperience by Kentico" width="48" align="left" style="margin-right:12px">

> **Health scanner for Xperience by Kentico projects.** ESLint for XbyK.

Free, open-source. Ships in two forms:

- **Embedded NuGet** for your XbyK site — installs alongside the app, runs on Kentico's scheduler, persists findings to custom tables, mirrors summaries to `CMS_EventLog`. Scans on the default cadence once the admin enables the scheduled task; email digests are opt-in (set `Sentinel:EmailDigest:Recipients`).
- **CLI tool** for one-shot scans from a terminal or CI — same check suite, HTML + JSON reports, remote GitHub-repo mode.

Built by [Refined Element](https://refinedelement.com) — Kentico Community Leaders 2025 & 2026.

### Supported Kentico versions

| Version | Supported | Notes |
|---------|-----------|-------|
| **Xperience by Kentico 31.x** | ✅ Full support | The embedded NuGet targets 31.x explicitly. CLI also supports 29+. |
| **Xperience by Kentico 29–30** | ✅ CLI only | Use the CLI until we ship a 29/30-compatible embedded build. |
| Kentico Xperience 13 | ❌ Not supported | KX13 uses the legacy content tree (`CMS_Document` / `CMS_Tree`) and ASP.NET 4.x config patterns. A separate scanner would be needed; we have no plans to build one. |
| Kentico 12 and earlier | ❌ Not supported | End of mainstream support. |

If you're on KX13 or older, this tool won't help you. Please don't [open an issue](https://github.com/refined-element/xperience-community-sentinel/issues) asking us to backport.

## Install in an XbyK site (recommended)

Drop one NuGet reference into your XbyK project, wire one line in `Program.cs`, and Kentico takes care of scheduling, persistence, and the event-log mirror.

### 1. Reference the package

```xml
<PackageReference Include="XperienceCommunity.Sentinel.Module" Version="0.4.5-alpha" />
```

### 2. Register the services

In `Program.cs`, after `builder.Services.AddKentico(...)`:

```csharp
using XperienceCommunity.Sentinel.Module.DependencyInjection;

builder.Services.AddSentinel(builder.Configuration);
```

### 3. Configure (optional — every field has a sensible default)

In `appsettings.json`:

Values below are the **actual code defaults** — omit a key entirely to get the default, override only what you want to change.

```jsonc
"Sentinel": {
  "Enabled": true,
  "Checks": { "Excluded": [] },
  "RuntimeChecks": {
    "ConnectionString": "",   // blank = reuse CMSConnectionString
    "StaleDays": 180,
    "EventLogDays": 30
  },
  "EventLogIntegration": {
    "Enabled": true,
    "SeverityThreshold": "Warning",   // Info | Warning | Error
    "MaxEntriesPerScan": 50
  },
  "EmailDigest": {
    "Enabled": true,                  // true by default, but digests don't SEND unless Recipients is non-empty
    "Recipients": [],                 // add SMTP addresses here to opt in
    "SeverityThreshold": "Warning",
    "OnlyWhenThresholdFindings": true
  }
}
```

### 4. First run

On the next app-start Sentinel's installer upserts three tables (`XperienceCommunity_SentinelScanRun`, `XperienceCommunity_SentinelFinding`, `XperienceCommunity_SentinelFindingAck`) in the CMS database. The scheduled task class registers automatically.

Open **Configuration → Scheduled tasks** in Kentico admin, create a new task with implementation `XperienceCommunity.SentinelScan` (the dropdown list), set a cadence, save, enable. Hit **Execute now** to run the first scan.

Cadence lives in Kentico's Scheduled Tasks UI — no cron config in code.

### 5. Where output lands

- **`XperienceCommunity_SentinelScanRun`** — one row per scan execution (trigger, duration, error/warning/info counts, status)
- **`XperienceCommunity_SentinelFinding`** — one row per finding with a stable fingerprint for cross-scan acknowledgments
- **`XperienceCommunity_SentinelFindingAck`** — one row per acknowledged/snoozed finding, keyed by fingerprint. The installer provisions the table so deploys don't need a migration step when the Admin UI (v0.3.x) lights up the ack actions; it stays empty until then.
- **`CMS_EventLog`** — summary entry per scan (source = `Sentinel`) + one entry per finding at or above `SeverityThreshold`, up to `EventLogIntegration.MaxEntriesPerScan`; if more findings qualify, Sentinel writes a single additional summary noting the suppressed event-log entries

### 6. Admin UI (optional)

The companion package **`XperienceCommunity.Sentinel.Admin`** adds **Configuration → Sentinel** to the admin left-nav with:

- **Dashboard** — latest scan KPIs, 30-day severity trend, recent scans, top rule offenders with inline remediation
- **Scan history** — every scan run, sortable + filterable
- **Findings** — every finding across scans
- **Scan detail** — drill into a single scan, per-finding acknowledge / snooze / revoke (individual and bulk)
- **Compare scans** — fingerprint-keyed diff: Introduced / Resolved / Still open
- **Request a quote** — in-admin form that submits a sanitized scan snapshot to Refined Element
- **Settings** — editable, DB-backed overrides win over `appsettings.json` (tune thresholds, cadence, recipients without a redeploy)

Install:

```xml
<PackageReference Include="XperienceCommunity.Sentinel.Admin" Version="0.4.5-alpha" />
```

No extra `Program.cs` wiring — the existing `AddSentinel()` call covers DI. The admin pages surface automatically.

#### Screenshots

| Dashboard | Scan detail |
|---|---|
| ![Dashboard](docs/screenshots/Dashboard.png) | ![Scan detail](docs/screenshots/Scan%20Detail.png) |

| Settings (editable) | Compare scans |
|---|---|
| ![Settings](docs/screenshots/Settings.png) | ![Diff view](docs/screenshots/Diff%20View.png) |

| Snooze action |
|---|
| ![Snooze action](docs/screenshots/Snooze%20Action.png) |

## Uninstall

**Removing the NuGet package alone leaves the Sentinel tables and data intact.** This is intentional — operators who uninstall-then-reinstall (e.g. during a pipeline glitch or a version bump) don't want to lose their ack history, snooze notes, and scan baselines. Sentinel treats its data the way Kentico treats CMS data: the app is deletable, the data isn't.

### What stays after `dotnet remove package`

- `XperienceCommunity_SentinelScanRun` — historical scan runs
- `XperienceCommunity_SentinelFinding` — findings from each run
- `XperienceCommunity_SentinelFindingAck` — operator ack / snooze / note state
- `CMS_Class` rows (three `DataClassInfo` registrations for the tables above)
- `CMS_ScheduledTask` row (`TaskName = 'XperienceCommunity.SentinelScan'`) — will fail silently on its next tick because the handler class no longer loads. Not catastrophic, but noisy in the event log.

Reinstalling the package picks up exactly where the previous version left off.

### Clean teardown

If you actually want Sentinel *gone* — gone-gone — remove the package from your csproj and then run:

```sql
-- Stop the scheduled task from firing against a missing handler.
DELETE FROM CMS_ScheduledTask WHERE TaskName = 'XperienceCommunity.SentinelScan';

-- Drop the data tables. IF EXISTS means the script is idempotent — safe to re-run if an
-- earlier step failed partway through.
DROP TABLE IF EXISTS XperienceCommunity_SentinelFindingAck;
DROP TABLE IF EXISTS XperienceCommunity_SentinelFinding;
DROP TABLE IF EXISTS XperienceCommunity_SentinelScanRun;

-- Drop Kentico's metadata about the tables.
DELETE FROM CMS_Class WHERE ClassName IN (
    'xperiencecommunity.sentinelfindingack',
    'xperiencecommunity.sentinelfinding',
    'xperiencecommunity.sentinelscanrun'
);
```

Run against the Kentico database the XbyK project points at. Safe to run even if the tables are already gone (`DROP TABLE` / `DELETE` with a no-match condition are both no-ops).

## CLI (alternative / CI)

Same checks, one-shot mode, no install in your XbyK project.

```bash
# Prerelease until v1.0 — use --prerelease or an explicit version.
dotnet tool install -g XperienceCommunity.Sentinel --prerelease

# Static code checks only (works against XbyK 29+)
sentinel scan --path ./MyXperienceSite

# Full scan (code + runtime content checks — requires an XbyK database)
sentinel scan --path ./MyXperienceSite --connection-string "Server=...;Database=..."

# Scan a GitHub repo directly (shallow-cloned to a temp dir, cleaned up after)
sentinel scan --repo owner/your-xbyk-site

# Email the sanitized report to Refined Element for a one-time remediation quote
sentinel quote --report ./sentinel-report/report.json
```

## Convenience scripts

If your project uses `dotnet user-secrets` for its connection string (the Kentico-recommended default),
the wrapper script resolves it automatically:

```powershell
./scripts/scan.ps1 -Project F:\RefinedElement\re-xbk -StaleDays 365 -OpenReport
```

Iterating on the scanner itself? `scripts/dev-reinstall.ps1` packs the current source, reinstalls
the global tool, and leaves you ready to re-run `sentinel`.

## What you'll see

A real scan against a production XbyK 31.0.1 site takes about **3 seconds** end-to-end and yields
output like:

```
╭──────────────────────────┬──────────────────────────╮
│ Metric                   │ Value                    │
├──────────────────────────┼──────────────────────────┤
│ Repo                     │ F:\RefinedElement\re-xbk │
│ Runtime checks           │ enabled                  │
│ Duration                 │ 3.25s                    │
│ Checks executed          │ 10                       │
│ Errors                   │ 0                        │
│ Warnings                 │ 3                        │
│ Info                     │ 12                       │
╰──────────────────────────┴──────────────────────────╯
  WARNING  CFG003  '_comment_secrets' contains a plaintext secret…  (false-positive — now suppressed)
  WARNING  DEP001  Stripe.net: 50.1.0 → 51.0.0
  WARNING  DEP001  Microsoft.EntityFrameworkCore.SqlServer: 9.0.0 → 10.0.6
  INFO     CNT001  Content type 'LandingPage' has zero content items.
  INFO     CNT002  Reusable content item 'Content_TestBlogPost-…' has no inbound references.
  INFO     VER001  Kentico.Xperience.WebApp is on 31.0.1; latest is 31.4.0.
```

The HTML report is self-contained (no external CSS/JS) and Refined Element-branded.

## What It Checks (v1)

### Static — free, no database needed

| # | Check | Why it matters |
|---|-------|----------------|
| 1 | **XbyK version** vs. latest, with known CVE flags | Catch security-relevant version drift |
| 2 | **Outdated NuGet packages** (severity-ranked) | Stay ahead of patch-level risk |
| 3 | **Config smells** — empty `CMSHashStringSalt`, wrong middleware order, missing Key Vault refs | Prevent subtle prod breakage |
| 4 | **Duplicate / inconsistent content-type field definitions** | Keep the content model clean |
| 5 | **Page Builder widgets registered but never placed** | Remove dead code paths |

### Runtime — free, requires DB connection string or Management API credentials

| # | Check | Why it matters |
|---|-------|----------------|
| 6 | **Unused content types** (0 items) | Find candidates for cleanup |
| 7 | **Orphaned content items** (0 page or reusable references) | Reclaim the content tree |
| 8 | **Stale content** (no edits in N days, configurable) | Identify what needs refreshing |
| 9 | **Broken asset references** | Fix images before users hit them |
| 10 | **Widgets configured with invalid / missing properties** | Catch dead presentation data |

## Output

Every scan produces:

- An **HTML report** (`sentinel-report/report.html`) — human-readable, grouped by severity, with actionable guidance for each finding
- A **JSON report** (`sentinel-report/report.json`) — stable schema, CI-friendly, consumed by the `quote` command

## `sentinel quote` — one-click remediation quote

Every report ends with: *"Want Refined Element to fix these? Run `sentinel quote`."* The command POSTs a **sanitized summary** (counts + rule IDs — no source code excerpts by default) to Refined Element, which replies with an itemized, fixed-price quote based on the findings.

Opt in to richer context with `--include-context` for a more accurate quote.

## Roadmap

**v0.2.x (current alpha)** — embedded-mode NuGet for XbyK 31.x with headless scheduled scanning, custom-table persistence, `CMS_EventLog` mirror, optional HTML email digest. CLI in parity.

**v0.3.x (next alpha)** — admin UI module: Scan history + Findings listing pages, then custom Dashboard + Contact Refined Element form. Separate `…XbyK.Admin` NuGet so headless deploys can skip it.

**v1.x (stable)** — API freeze. Same feature surface, no more breaking changes between minor versions. Free, MIT-licensed.

**v2.x (paid add-ons)** — automatic remediation via PR bot (small refactors, dep bumps, config fixes), multi-site dashboard, Slack / PagerDuty integration. Core checks remain free.

**v3.x (self-healing)** — content-side automation: unpublish stale items on a cadence, broken-link repair, SEO auto-remediation backed by your analytics.

## License

MIT © 2026 [Refined Element](https://refinedelement.com)

## Contributing

Issues and PRs welcome. New check ideas especially — the goal is to be **the** XbyK scanner.

### Dev loop

```bash
dotnet build XperienceCommunity.Sentinel.slnx   # full solution: Core + XbyK + CLI + tests
dotnet test XperienceCommunity.Sentinel.slnx    # unit tests — checks, sanitizer, runner, notifiers
./scripts/dev-reinstall.ps1         # CLI: pack + reinstall the global tool
./scripts/scan.ps1 -Project F:\RefinedElement\re-xbk  # verify against a real site
```

### Project layout

| Project | Purpose |
|---|---|
| `src/XperienceCommunity.Sentinel.Core` | Check engine, registry, sanitizer, reporting. Framework-agnostic. |
| `src/XperienceCommunity.Sentinel.Module` | Embedded XbyK integration — Info models, installer, scheduled task, notifiers. |
| `src/XperienceCommunity.Sentinel.Admin` | Admin UI — Dashboard, Scan history, Findings, Scan detail, Compare scans, Request-a-quote, Settings. Optional — headless deploys can skip it. |
| `src/XperienceCommunity.Sentinel` | CLI tool (`sentinel`). |
| `tests/XperienceCommunity.Sentinel.Tests` | xUnit tests across all packages. |

Each check is a single class in `src/XperienceCommunity.Sentinel.Core/Checks/` implementing `ICheck`. Register it in `Core/CheckRegistry.cs` and it ships in the next run of both the CLI and the embedded scheduled task.

## Links

- [Refined Element](https://refinedelement.com) — the consultancy
- [KDaaS](https://kentico-developer.com) — AI-powered Kentico dev service
- [Xperience by Kentico](https://www.kentico.com/)
