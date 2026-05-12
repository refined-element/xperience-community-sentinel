# XperienceCommunity.Sentinel.Admin

> **Admin UI for Sentinel for Xperience by Kentico.** Adds a Sentinel application under Configuration in the Kentico admin — Dashboard, Scan history, Findings, Scan detail, Compare scans, Contact, Settings.

Ships alongside [`XperienceCommunity.Sentinel.Module`](https://www.nuget.org/packages/XperienceCommunity.Sentinel.Module/). Headless deploys that don't want a UI surface can skip this package — the core integration runs fine without it.

What shows up in **Configuration → Sentinel**:

- **Dashboard** — latest scan KPIs, 30-day severity trend, recent scans, top rule offenders with inline remediation
- **Scan history** — every scan run, sortable and filterable
- **Findings** — every finding across scans
- **Scan detail** — drill into a single scan, per-finding acknowledge / snooze / revoke (individual and bulk)
- **Compare scans** — fingerprint-keyed diff (Introduced / Resolved / Still open)
- **Request a quote** — in-admin form that submits a sanitized scan snapshot to Refined Element
- **Settings** — editable, DB-backed overrides win over `appsettings.json` (tune thresholds, cadence, recipients without a redeploy)

The client is a React app bundled via webpack and embedded as a Kentico admin client module. The npm build runs automatically on `dotnet build`, so a fresh clone `dotnet build`s without extra steps.

## Install

```xml
<PackageReference Include="XperienceCommunity.Sentinel.Admin" Version="0.4.5-alpha" />
```

**No extra `Program.cs` wiring.** The existing `AddSentinel()` call in the headless package covers DI; this package just lights up the admin pages. Both packages must be on the same version.

Targets `.NET 9`, pins `Kentico.Xperience.Admin` to `[31.0.0, 32.0.0)`.

## Requires

- `XperienceCommunity.Sentinel.Module` (same version) — registered via `AddSentinel()` in `Program.cs`
- Xperience by Kentico 31.x
- An authenticated admin user with a role that grants access to the Configuration application group

## Screenshots and full docs

See the [main repo README](https://github.com/refined-element/xperience-community-sentinel#6-admin-ui-optional) for screenshots, configuration, and the full feature matrix.

## License

MIT © [Refined Element](https://refinedelement.com)
