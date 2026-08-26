# AI audit skill for Xperience by Kentico

> Ask Claude Code to audit your Xperience by Kentico project and receive a consulting-grade report.

Free, open-source, and MIT-licensed. `sentinel-audit` wraps the [Sentinel](../../README.md) CLI
scan and layers an AI review on top of it, then writes a report that reads like something a
consultant produced after a day with your codebase, not a linter's output.

## What it is

`sentinel-audit` is a Claude Code skill that runs a deterministic Sentinel scan for the rules Sentinel
already checks, then reviews your project against 40 additional checks across architecture, content
model, security, and performance. It merges both passes into one graded findings set and writes a
report you can hand to a stakeholder as-is.

## What you receive

The audit writes three files into a `sentinel-audit/` folder inside the audited project:

| File | What it holds |
|---|---|
| `audit-findings.json` | The machine-readable findings set that both reports are built from |
| `audit-report.md` | The human-readable report |
| `audit-report.html` | The same report as a self-contained, styled page that matches Sentinel's own scan report |

<!-- sample-report screenshot: audit-report.html cover and scorecard — no screenshot tooling available in this environment; see the linked report instead -->
See a redacted [sample report](../../docs/sample-report/audit-report.md) from a real audit (identifying detail generalized), also available as a [styled HTML page](../../docs/sample-report/audit-report.html).

## Install

Prerequisites:

- Claude Code
- .NET SDK 9 or later
- Optional: a connection string for your project's database, which enables the audit's runtime checks

Add the marketplace, then install the plugin:

```
/plugin marketplace add refined-element/xperience-community-sentinel
/plugin install sentinel-audit@xperience-community-sentinel
```

The skill installs the Sentinel CLI itself the first time you run an audit, if your machine doesn't
already have it.

## Run

Ask Claude Code to `audit this project` from inside an Xperience by Kentico project, or name a
different path.

The Sentinel scan finishes in seconds. The AI review pass that follows takes longer — how long
depends on the size of your project. When the audit finishes, find the three files described above
in the project's `sentinel-audit/` folder. The skill also recommends adding `sentinel-audit/` and
the `sentinel-report/` folder the scan itself writes to your project's `.gitignore`.

Without a database connection string, the audit skips its runtime checks and produces a partial
audit. The report marks this with a partial-audit banner and still covers every check that doesn't
need a database.

## Privacy

The audit runs locally. Three things reach the network: installing the Sentinel CLI, the package
and version metadata lookups the scan makes against nuget.org for its `VER001` and `DEP001` rules
(these send package identifiers only, not code or findings), and documentation lookups during the
AI review pass. Nothing about your code or your findings leaves your machine unless you choose to
run `sentinel quote`, which submits a sanitized summary — rule IDs and counts, not code — to Refined
Element.

## Free and paid

The skill and the report it produces are free and licensed under MIT. Two paid paths follow from
the report, and neither runs automatically:

- **`sentinel quote`** — send a sanitized summary of your findings to Refined Element and receive a
  fixed-price remediation quote.
- **[Architecture Review & Roadmap](https://foundry.refinedelement.com)** — a Refined Element
  architect reviews, extends, and prioritizes the audit's findings with you.

No other product or offer is part of this skill.

## Supported versions

The skill supports what the Sentinel CLI it wraps supports.

| Version | Supported |
|---|---|
| Xperience by Kentico | Yes |
| Kentico Xperience 13 or Kentico 12 and earlier | No |

When the skill finds a Kentico Xperience 13 or older project, it declines to run, explains why, and
points you to the [Automated KX13 Assessment](https://foundry.refinedelement.com), which covers
upgrade readiness instead.
