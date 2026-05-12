# Contributing to Sentinel for Xperience by Kentico

Thanks for considering a contribution! Sentinel for Xperience by Kentico is a free, MIT-licensed health scanner for Xperience by Kentico, maintained by [Refined Element](https://refinedelement.com). We welcome bug reports, rule ideas, UX feedback on the admin UI, new checks, and documentation improvements. Contributions of any size help — a typo fix in the README is just as valuable as a new rule in `XperienceCommunity.Sentinel.Core`.

This document describes the workflows that make it easy for us to review and merge your contribution quickly.

## Table of contents

- [Code of Conduct](#code-of-conduct)
- [Reporting a bug](#reporting-a-bug)
- [Requesting a new check or rule](#requesting-a-new-check-or-rule)
- [Submitting a pull request](#submitting-a-pull-request)
- [Adding a new check](#adding-a-new-check)
- [Local development loop](#local-development-loop)
- [Review and merge expectations](#review-and-merge-expectations)
- [Questions](#questions)

## Code of Conduct

All participation in this project is governed by our [Code of Conduct](./CODE_OF_CONDUCT.md). By participating you agree to uphold it. Report unacceptable behavior to `support@refinedelement.com`.

## Reporting a bug

Please open a [GitHub issue](https://github.com/refined-element/xperience-community-sentinel/issues/new) and include:

- **Repro steps** — the smallest set of actions that reproduces the problem.
- **Kentico version** — e.g. *Xperience by Kentico 31.0.4*. Include the hotfix if relevant.
- **Sentinel version** — the NuGet package version (e.g. `0.4.3-alpha`) or CLI version (`sentinel --version`).
- **Install mode** — embedded NuGet (`XperienceCommunity.Sentinel.Module` / `.Admin`) or CLI global tool.
- **Scan output** — the relevant portion of the HTML or JSON report, or the scheduled-task log. If the scan threw, the stack trace from `CMS_EventLog` or stdout.
- **Expected vs actual** — what you thought would happen, and what actually happened.

If the bug is a false positive or false negative on a specific rule, include the **rule ID** (e.g. `CNT001`, `CFG002`) and a sanitized description of the content or configuration that triggered it.

## Requesting a new check or rule

New rule ideas are one of the most valuable contributions. Open an issue with the `rule-request` label describing:

- **What problem it detects** — the concrete bad state in a Kentico site you want Sentinel to catch.
- **Why it matters** — performance, security, content-model debt, upgrade blocker, cost, DX, etc.
- **Proposed severity tier** — `Info`, `Warning`, or `Error`. (See `src/XperienceCommunity.Sentinel.Core/Core/Severity.cs`.)
- **How it can be detected** — a SQL query against `CMS_*` tables, a file-system check on the source tree, a configuration inspection, etc. A rough sketch is fine; we'll refine during review.

Because the `Core` package is deliberately host-agnostic (no dependency on Kentico assemblies), most new rules are a **single-file PR** — a class in `src/XperienceCommunity.Sentinel.Core/Checks/<Category>/` plus a registration line and a test. That makes rule contributions very accessible.

## Submitting a pull request

1. **Fork** the repo and clone your fork.
2. **Branch** from `main`. Use a conventional prefix:
   - `feat/` — new check, new feature, new UI surface
   - `fix/` — bug fix
   - `docs/` — documentation only
   - `chore/` — build, CI, dependencies, housekeeping
   - `refactor/` — code restructuring with no behavior change
   - `test/` — tests-only changes

   Example: `feat/cnt004-duplicate-content-types`.

3. **Commit** using Conventional-Commits-ish messages, matching the style in `git log`:

   ```
   feat(core): CNT004 — detect duplicate content types
   fix(admin): snooze dropdown z-index regression on Safari
   docs: clarify Sentinel:EmailDigest defaults in README
   chore: bump xUnit to 2.9.2
   ```

   First line under ~72 chars. Body is optional but welcome for non-trivial changes — explain *why*, not *what*.

4. **Tests are required** for new rules and bug fixes. Every `ICheck` in `Core` has a matching test in `tests/XperienceCommunity.Sentinel.Tests/` that exercises at least one positive case (finding produced) and one negative case (clean database, no finding). For bug fixes, add a test that fails on `main` and passes on your branch.

5. **Run the full test suite** before pushing:

   ```bash
   dotnet build
   dotnet test
   ```

   All 147+ tests should pass on `main` and should continue to pass on your branch.

6. **Open a PR** against `main`. In the description, link any related issues, summarize the change, and note anything a reviewer should look at first.

7. **CI runs on every PR** — build, test, and package. If CI fails, the PR can't merge until it's green.

## Adding a new check

Checks live in `src/XperienceCommunity.Sentinel.Core/Checks/` organized by category (`Configuration/`, `Content/`, `Dependencies/`). Each check:

- Implements [`ICheck`](src/XperienceCommunity.Sentinel.Core/Core/ICheck.cs).
- Has a **stable rule ID** (e.g. `CNT004`) that is never reused or renumbered once published.
- Is **registered** in `src/XperienceCommunity.Sentinel.Core/Core/CheckRegistry.cs`.
- Has at least one test in `tests/XperienceCommunity.Sentinel.Tests/`.

Minimal shape:

```csharp
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Content;

/// <summary>
/// CNT004 — short sentence describing what this rule detects and why it matters.
/// </summary>
public sealed class MyNewCheck : ICheck
{
    public string RuleId => "CNT004";
    public string Title => "Human-readable title shown in reports";
    public string Category => "Content Model";
    public CheckKind Kind => CheckKind.Runtime; // or CheckKind.Static

    public async Task<IReadOnlyList<Finding>> RunAsync(
        ScanContext context,
        CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();

        // Query the DB (Runtime) or inspect the source tree (Static).
        // For Runtime checks, use context.ConnectionString with Microsoft.Data.SqlClient.

        // On detection, add a Finding:
        findings.Add(new Finding(
            RuleId, Title, Category, Severity.Warning,
            "Human-readable message describing the specific instance.",
            Location: "Where in the system the problem lives",
            Remediation: "One or two sentences on how to fix it."));

        return findings;
    }
}
```

**Do not throw** for expected failure modes — encode them as `Finding`s. The runner catches unexpected exceptions and reports them as a `CheckFailed` finding, so a bug in your check won't abort the scan, but it *will* be visible.

Look at the existing `Checks/Content/UnusedContentTypesCheck.cs` and `Checks/Dependencies/OutdatedPackagesCheck.cs` for worked examples.

## Local development loop

```bash
# Restore, build, and run tests
dotnet build
dotnet test

# Install your local CLI build over the global tool for end-to-end testing
./scripts/dev-reinstall.ps1

# Run a scan against a local Kentico site
sentinel scan --connection "Server=.;Database=Xperience;Trusted_Connection=True;TrustServerCertificate=True" --output ./reports
```

The `scripts/dev-reinstall.ps1` helper packs the CLI project and reinstalls it as a global tool so `sentinel` on your `PATH` points at your in-progress build. Re-run it after any change to the CLI project.

For the embedded NuGet (`XperienceCommunity.Sentinel.Module` / `.Admin`), build the solution and reference the local `.nupkg` files from a test Kentico app, or use a local NuGet source.

## Review and merge expectations

- We review PRs within a few business days. If a week passes with no response, a friendly ping on the PR is welcome.
- We **squash-merge** to `main` to keep history clean. Your PR description becomes the squashed commit body, so a good description helps everyone.
- Tags matching `v*` trigger the release workflow that publishes all four NuGet packages to nuget.org. Maintainers cut tags; contributors don't need to worry about release mechanics.
- Small, focused PRs merge faster than large ones. When in doubt, split.

## Questions

- **Bug or rule idea:** [open an issue](https://github.com/refined-element/xperience-community-sentinel/issues).
- **Maintainer contact:** `support@refinedelement.com`.
- **Project home:** [refinedelement.com](https://refinedelement.com).

Thank you for contributing to Sentinel for Xperience by Kentico.
