# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working in this repository.

## Project Overview

**Sentinel for Xperience by Kentico** — a free, open-source CLI that scans Xperience by Kentico projects for health issues. "ESLint for Kentico." Built by Refined Element.

Distributed as a `dotnet tool` (NuGet package `XperienceCommunity.Sentinel`, CLI command `sentinel`).

## Solution Layout

```
xperience-community-sentinel/
├── src/XperienceCommunity.Sentinel/              .NET 9 console app (the tool)
├── tests/XperienceCommunity.Sentinel.Tests/      xUnit test project
├── docs/                             Architecture notes, rule catalog
├── XperienceCommunity.Sentinel.slnx
└── README.md
```

## Commands

```bash
dotnet build               # Build the solution
dotnet test                # Run tests
dotnet run --project src/XperienceCommunity.Sentinel -- scan --path ./some-xbk-repo
dotnet pack src/XperienceCommunity.Sentinel -c Release   # Produce the .nupkg
```

Install the packed tool locally:

```bash
dotnet tool install --global --add-source ./src/XperienceCommunity.Sentinel/bin/Release XperienceCommunity.Sentinel
```

## Architecture Principles

1. **Free CLI, closed paid SaaS.** v1 free tier is MIT-licensed, runs entirely local. v2 paid tier (SaaS) is a separate codebase — do not mix.
2. **Detection first, action second.** v1 only *detects*. Auto-remediation is v2+. Keep the code paths separate so the free tier can never accidentally modify a customer's data.
3. **Checks are plugins.** Each check implements `ICheck`. Adding a check should mean dropping one file into `Checks/` and nothing else.
4. **Runtime checks are opt-in.** If no connection string is supplied, only static checks run. Never assume DB access.
5. **Privacy by default.** The `quote` submission sanitizes the report — no code snippets, no config values, just rule IDs and counts — unless the user opts in with `--include-context`.

## Commands Architecture

- `scan` — runs all applicable checks and emits HTML + JSON reports
- `quote` — reads the JSON report, sanitizes, POSTs to KDaaS (`kentico-developer.com/api/scanner/submit`)

## What's Intentionally Out of Scope for v1

- Auto-fix / auto-remediation (v2 SaaS)
- CI-specific integrations (GitHub Actions, Azure DevOps)
- Hosted dashboard
- Rule config files (`.sentinelrc`) — use sensible defaults
- Multi-project / monorepo scanning

## Business Model

- Free CLI is the lead-gen funnel.
- `sentinel quote` routes to [KDaaS](https://kentico-developer.com) which already has spec + email + admin infrastructure.
- Paid SaaS v2 handles continuous monitoring + auto-remediation.

## Related Projects

- `F:\RefinedElement\re-xbk` — Refined Element marketing site (XbyK 31.0.1, .NET 9)
- `F:\KDaaS` — Kentico Developer as a Service (.NET 10, accepts quote submissions)
- `F:\Vault\projects\kentico-l402-gateway\concept.md` — future L402-gated headless module

## Conventions

- **Namespace:** `XperienceCommunity.Sentinel`
- **Target:** `net9.0` (matches XbyK 31.x runtime)
- **Warnings as errors** is enabled. Fix warnings; don't suppress.
- **Nullable reference types** on. Use `?` explicitly.
- **xUnit** for tests. One test class per `Check`.

## Git Workflow

- `main` is protected. Feature branches for all changes.
- Conventional commits (`feat:`, `fix:`, `chore:`, `docs:`).
- Squash-merge PRs.
