# Sentinel for Xperience by Kentico

<img src="docs/logo.svg" alt="Sentinel for Xperience by Kentico" width="48" align="left" style="margin-right:12px">

> **Health scanner for Xperience by Kentico projects.** ESLint for XbyK.

Free, open-source. Ships in two forms:

- **Embedded NuGet** for your XbyK site — installs alongside the app, runs on Kentico's scheduler, persists findings to custom tables, mirrors summaries to `CMS_EventLog`. Scans on the default cadence once the admin enables the scheduled task; email digests are opt-in (set `Sentinel:EmailDigest:Recipients`).
- **CLI tool** for one-shot scans from a terminal or CI — the full 13-check suite, including three source-tree checks the embedded host skips (see [What It Checks](#what-it-checks)). HTML + JSON reports, remote GitHub-repo mode.

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
- **`XperienceCommunity_SentinelFindingAck`** — one row per acknowledged/snoozed finding, keyed by fingerprint. Written by the Admin UI's acknowledge / snooze actions (section 6); the installer provisions it up front so adding the Admin package later needs no migration step. Stays empty until you use those actions.
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
output like (findings list abridged):

```
╭──────────────────────────┬──────────────────────────╮
│ Metric                   │ Value                    │
├──────────────────────────┼──────────────────────────┤
│ Repo                     │ F:\RefinedElement\re-xbk │
│ Runtime checks           │ enabled                  │
│ Duration                 │ 3.25s                    │
│ Checks executed          │ 13                       │
│ Checks skipped (runtime) │ 0                        │
│ Checks failed            │ 0                        │
│ Errors                   │ 0                        │
│ Warnings                 │ 3                        │
│ Info                     │ 12                       │
╰──────────────────────────┴──────────────────────────╯
  WARNING  CFG003  'Smtp.Password' appears to contain a plaintext secret in appsettings.json.
  WARNING  DEP001  Stripe.net: 50.1.0 → 51.0.0
  WARNING  DEP001  Microsoft.EntityFrameworkCore.SqlServer: 9.0.0 → 10.0.6
  INFO     CNT001  Content type 'Landing page' (ReXBK.LandingPage, Website) has zero content items.
  INFO     CNT002  'Test blog post' (Article content) — reusable content item with no inbound
                   references and last edited 312 days ago (threshold: 180 days).
  INFO     CNT006  Scheduler / EXECUTE: 4 warnings in the last 30 days (first 2026-06-14, latest 2026-07-02).
  INFO     CNT010  'hero-banner-2023' (Image) — image with no inbound references and last edited
                   402 days ago (threshold: 180 days).
  INFO     VER001  Xperience by Kentico Kentico.Xperience.WebApp is on 31.0.1; latest on NuGet is 31.4.0.
  … plus 7 more Info findings — the full list lands in the HTML / JSON report.
```

`Checks executed` counts only checks that actually ran. Run without `--connection-string` and the
eight runtime checks are skipped: the same site reports `Checks executed 5` / `Checks skipped (runtime) 8`
— the expected shape for a static-only scan, not a broken install. `Checks failed` counts checks that
threw an exception; each failure also surfaces as a SYS001 Warning finding so it can't hide.

The HTML report is self-contained (no external CSS/JS) and Refined Element-branded.

## What It Checks

Thirteen checks ship in the default scan — five static (code-only) and eight runtime (database-backed) — all registered in `src/XperienceCommunity.Sentinel.Core/Core/CheckRegistry.cs`.

**Coverage differs by host.** The CLI runs the full suite: all five static checks, plus the eight runtime checks when `--connection-string` is provided. The embedded scheduled task scans the deployed site, where three source-tree checks — **CFG002** (middleware order), **DEP001** (outdated packages), and **VER001** (XbyK version) — skip themselves and report nothing: a deployed site ships compiled DLLs, with no `Program.cs` or `.csproj` to inspect. An embedded scan therefore effectively covers CFG001, CFG003, and the eight runtime checks, minus anything you list in `Sentinel:Checks:Excluded`. For full static coverage — middleware order, package drift, XbyK version — run the CLI against the source repo (for example in CI).

### Static — free, no database needed

| Rule | Check | What it flags |
|------|-------|---------------|
| CFG001 | **CMSHashStringSalt configuration** | `CMSHashStringSalt` missing from `appsettings.json` (Error), or hard-coded there instead of supplied via user secrets / Key Vault (Warning) |
| CFG002 | **Kentico middleware pipeline order** | The `InitKentico → UseStaticFiles → UseKentico` trio out of order or with middleware between the three calls, and `UseWebOptimizer` running before `UseKentico` |
| CFG003 | **Plaintext secrets in appsettings.json** | String values under sensitive-looking keys (password, secret, apikey, token, accesskey, privatekey, …) and connection strings containing `Password=` or `Pwd=`. Exemptions are exact: empty values, values starting with `@Microsoft.KeyVault(`, `$(`, or `${`, and underscore-prefixed keys (the JSON-comment convention). Human-style placeholders such as `CHANGE_ME` or `your-key-here` are still flagged |
| DEP001 | **Outdated NuGet packages** | Packages behind their latest version per `dotnet list package --outdated`; a major-version jump is a Warning, minor/patch is Info. Prerelease-installed packages — which `dotnet list` reports as "Not found at the sources" — fall back to querying the repo's declared NuGet sources directly with prereleases included |
| VER001 | **Xperience by Kentico version** | The detected XbyK version compared against the latest stable on NuGet; two or more majors behind is an Error, one major behind a Warning |

CFG002, DEP001, and VER001 run only from the CLI against a source repo — the embedded scheduled task skips them (see above).

### Runtime — free, requires a database connection string

| Rule | Check | What it flags |
|------|-------|---------------|
| CNT001 | **Unused content types** | Content types with zero content items — candidates for deletion |
| CNT002 | **Stale unused reusable content** | Reusable content items with no inbound references, untouched past the stale-days threshold. Excludes content types whose name matches the CNT010 / CNT011 patterns, so it never overlaps with either |
| CNT003 | **Stale content** | Content items not edited within the stale-days window (default 180) |
| CNT004 | **Broken media file references** | Media files whose library no longer exists, or with a zero-byte size (incomplete upload) |
| CNT005 | **Malformed Page Builder widget data** | Stored widget configurations that fail to parse as JSON, or widgets with no type identifier |
| CNT006 | **Recent Kentico EventLog errors** | Errors and warnings in `CMS_EventLog` over the lookback window (default 30 days), grouped by source and event code |
| CNT010 | **Stale unused images** | Stale, unreferenced content items whose content-type *name* looks image-like (contains image / photo / picture / thumbnail) — intentionally a class-name heuristic; split out from CNT002 for separate triage |
| CNT011 | **Stale unused documents / files** | Stale, unreferenced content items whose content-type *name* looks file-like (contains file / document / pdf / attachment / media) — the same class-name heuristic; split out from CNT002 for separate triage |

CNT002 / CNT010 / CNT011 share one query and differ only in the content-type name predicate. Two behaviors to know:

- **Staleness:** an item is flagged when its most recent language-metadata edit is older than the stale-days threshold (default 180) — **or when it has no language-metadata rows at all**, in which case it's flagged immediately regardless of age (the finding reads "no modifications recorded").
- **Bucketing is by class *name*, and CNT010/CNT011 can overlap:** CNT002's predicate excludes both the image and the file name patterns, so it never overlaps the other two. CNT010 and CNT011 do **not** exclude each other — a content type whose name matches both pattern sets (e.g. `MediaImage` matches `%image%` and `%media%`) reports the same item under **both** rules. And because the match is a name heuristic, a type like `Site.AuthorProfile` (contains "file") lands in CNT011 while an image type named `Visual` falls through to CNT002. If a bucket is noisy for your naming convention, exclude that rule via `Sentinel:Checks:Excluded`.

## Output

Every scan produces:

- An **HTML report** (`sentinel-report/report.html`) — human-readable, grouped by severity, with actionable guidance for each finding
- A **JSON report** (`sentinel-report/report.json`) — stable schema, CI-friendly, consumed by the `quote` command

## `sentinel quote` — one-click remediation quote

Every report ends with: *"Want Refined Element to fix these? Run `sentinel quote`."* The command POSTs a **sanitized summary** (counts + rule IDs — no source code excerpts by default) to Refined Element, which replies with an itemized, fixed-price quote based on the findings.

Opt in to richer context with `--include-context` for a more accurate quote.

## Roadmap

### Planned checks

Check ideas captured but **not yet shipped** — neither is registered in `Core/CheckRegistry.cs` today. Tracked here so the ideas aren't lost:

- **Duplicate / inconsistent content-type field definitions** (static) — flag content types that redefine the same field with a mismatched data type or settings, to keep the content model consistent.
- **Page Builder widgets registered but never placed** (runtime) — find widget types compiled and registered in code but absent from every page's stored widget data, so dead widget code can be removed. Placement lives in the database (`CMS_ContentItemCommonData` — the same data CNT005 inspects), so this needs a connection string; it can't be a static-only check.

### Known behavior notes / follow-ups

Shipped-behavior quirks worth knowing — documented here rather than silently absorbed above, and candidates for future refinement:

- **CNT010 / CNT011 can double-report:** the two name-pattern predicates don't exclude each other, so a content type matching both (e.g. `MediaImage`) fires the same item under both rules — two findings, two ack fingerprints. Several code comments claim all three rules are mutually exclusive; only CNT002's exclusion actually is. Fix candidate: subtract the image patterns from CNT011's predicate.
- **No-metadata items are flagged immediately:** the shared CNT002/CNT010/CNT011 query flags unreferenced items with no language-metadata rows regardless of age (the `MAX(...) IS NULL` branch). Right for genuinely abandoned items, but freshly migrated content whose metadata hasn't landed yet can appear "stale" on day one. Fix candidate: a distinct message (or severity) for the no-metadata case.
- **Embedded host has a static-coverage gap:** CFG002 / DEP001 / VER001 no-op in embedded scans (by design — no source tree on a deployed site), so middleware order, package drift, and XbyK version drift are only caught by CLI runs. Fix candidates: surface "needs the CLI" in the admin dashboard instead of silence, and/or a documented CI recipe.

### Release plan

**v0.4.x (current alpha)** — everything documented above is shipped: the embedded module (`XperienceCommunity.Sentinel.Module`) with headless scheduled scanning, custom-table persistence, `CMS_EventLog` mirror, and opt-in email digest; the admin UI (`XperienceCommunity.Sentinel.Admin`) with Dashboard, Scan history, Findings, Scan detail (acknowledge / snooze / revoke), Compare scans, Request-a-quote, and DB-backed editable Settings; and the CLI with GitHub-repo mode. (Historical: v0.2.x delivered the embedded module, v0.3.x the admin UI.)

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
