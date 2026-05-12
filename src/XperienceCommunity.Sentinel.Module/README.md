# XperienceCommunity.Sentinel.Module

> **Sentinel for Xperience by Kentico, embedded in your XbyK site.** Headless integration — scheduled scans, persisted findings, event-log mirror, optional email digest.

This package is the turn-key [Sentinel for Xperience by Kentico](https://github.com/refined-element/xperience-community-sentinel) integration for **Xperience by Kentico 31.x**. One NuGet reference + one line in `Program.cs` and Kentico takes care of scheduling, persistence, and the event-log mirror.

What you get:

- **`AddSentinel()`** DI extension — registers checks, scan runner, persistence, notifiers
- **Module installer** — upserts three custom tables (`XperienceCommunity_SentinelScanRun`, `…Finding`, `…FindingAck`)
- **Scheduled task** — `XperienceCommunity.SentinelScan`, cadence controlled by Kentico's Scheduled Tasks UI
- **Event log mirror** — summary + qualifying findings written to `CMS_EventLog` (source = `Sentinel`)
- **Email digest** — opt-in HTML digest via Kentico's email service
- **Contact flow** — sanitized "request a quote" channel to Refined Element
- **Finding ack / snooze state** — persists across scans by stable fingerprint

Pair with **`XperienceCommunity.Sentinel.Admin`** to get the in-admin Dashboard, Scan History, Findings, and Request-a-quote UI.

## Install

```xml
<PackageReference Include="XperienceCommunity.Sentinel.Module" Version="0.4.5-alpha" />
```

Targets `.NET 9`, pins `Kentico.Xperience.Core` / `Kentico.Xperience.WebApp` to `[31.0.0, 32.0.0)`.

## Wire it up

In `Program.cs`, after `builder.Services.AddKentico(...)`:

```csharp
using XperienceCommunity.Sentinel.Module.DependencyInjection;

builder.Services.AddSentinel(builder.Configuration);
```

On the next app start, Sentinel's module installer provisions its tables and registers the scheduled task class. Open **Configuration → Scheduled tasks** in the Kentico admin, create a task with implementation `XperienceCommunity.SentinelScan`, pick a cadence, save, enable, and hit **Execute now**.

## Optional config

Every field has a sensible default — omit any key to take the default. See the [main repo README](https://github.com/refined-element/xperience-community-sentinel#install-in-an-xbyk-site-recommended) for the full `Sentinel` config block (runtime checks, event-log severity threshold, email digest recipients).

## Uninstall

Removing the package **leaves your scan history intact** by design. For a clean teardown (drop tables, remove the scheduled task), see the [Uninstall section](https://github.com/refined-element/xperience-community-sentinel#uninstall) in the main repo README.

## License

MIT © [Refined Element](https://refinedelement.com)
