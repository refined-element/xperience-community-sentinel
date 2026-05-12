# XperienceCommunity.Sentinel

> **Sentinel for Xperience by Kentico CLI — one-shot health scans for Xperience by Kentico projects.** `dotnet tool` install, same check suite as the embedded NuGet.

The CLI form of [Sentinel for Xperience by Kentico](https://github.com/refined-element/xperience-community-sentinel). Use it from a developer workstation or a CI step — no install into your XbyK site required.

What it does in a single command:

- Runs the full Sentinel check suite — config smells, middleware-order bugs, outdated NuGet packages, unused content types, orphaned content items, stale content, broken assets, malformed Page Builder widgets, recent EventLog errors
- Emits a **self-contained HTML report** (`sentinel-report/report.html`) — Refined Element-branded, no external CSS/JS
- Emits a **JSON report** (`sentinel-report/report.json`) — stable schema, CI-friendly
- Optionally POSTs a **sanitized summary** to Refined Element via `sentinel quote` for a fixed-price remediation quote
- Can shallow-clone a **GitHub repo** directly (`--repo owner/repo`) — no local checkout needed

## Install

```bash
# Prerelease until v1.0 — use --prerelease or an explicit version.
dotnet tool install -g XperienceCommunity.Sentinel --prerelease
```

Update:

```bash
dotnet tool update -g XperienceCommunity.Sentinel --prerelease
```

## Use it

```bash
# Static code checks only (works against XbyK 29+)
sentinel scan --path ./MyXperienceSite

# Full scan (code + runtime content checks — requires an XbyK database)
sentinel scan --path ./MyXperienceSite --connection-string "Server=...;Database=..."

# Scan a GitHub repo directly (shallow-cloned to a temp dir, cleaned up after)
sentinel scan --repo owner/your-xbyk-site

# Email the sanitized report to Refined Element for a one-time remediation quote
sentinel quote --report ./sentinel-report/report.json
```

## Supported versions

| Version | Supported |
|---------|-----------|
| Xperience by Kentico 31.x | Full support |
| Xperience by Kentico 29–30 | CLI static checks |
| Kentico Xperience 13 and earlier | **Not supported** |

## Embed instead of CLI?

If you want Sentinel to run on Kentico's scheduler, persist findings to custom tables, mirror summaries to `CMS_EventLog`, and optionally surface an admin UI — reach for the embedded packages:

- **`XperienceCommunity.Sentinel.Module`** — headless integration
- **`XperienceCommunity.Sentinel.Admin`** — admin UI module

## Full docs

See the [main repo README](https://github.com/refined-element/xperience-community-sentinel) for the full check reference, config schema, and sample output.

## License

MIT © [Refined Element](https://refinedelement.com)
