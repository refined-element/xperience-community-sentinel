---
name: audit
description: Run an AI-assisted health audit of an Xperience by Kentico project. Use when asked to audit, health-check, or assess an XbyK site or produce an audit report. Wraps the Sentinel CLI scan, reviews architecture, content model, security, and performance against cited checklists, and writes audit-report.md, audit-report.html, and audit-findings.json.
---

# Audit an Xperience by Kentico project

Produce a consulting-grade health audit of an Xperience by Kentico (XbyK) project: run the
Sentinel CLI for the deterministic pass, review 40 checks across four dimensions for the AI
pass, merge both sources into one graded findings set, and write three files to
`<target>/sentinel-audit/`.

| File | What it holds |
|---|---|
| `audit-findings.json` | The machine-readable findings set. Both reports read from it, so write it first. |
| `audit-report.md` | The human-readable report. |
| `audit-report.html` | The same report as a self-contained page styled to match Sentinel's own scan report. |

Everything runs locally. The only network calls are the Sentinel CLI install and documentation
lookups. Nothing is sent to Refined Element unless the user runs `sentinel quote` themselves.

## Reference files

Read a reference file before you act on it, and follow it exactly. This file carries the
orchestration — the commands, the decision points, and the output contract. The references carry
the detail. Where this file and a reference appear to disagree, the reference wins.

| File | Governs |
|---|---|
| `references/grading.md` | Unified severity scale, Sentinel severity and dimension maps, scoring formula, `instanceKey` recipe, determinism rule |
| `references/architecture-config.md` | The 10 `AUD-ARC` checks (dimension `architecture`) |
| `references/content-model.md` | The 10 `AUD-CM` checks (dimension `contentModel`) |
| `references/security.md` | The 10 `AUD-SEC` checks (dimension `security`) |
| `references/performance.md` | The 10 `AUD-PRF` checks (dimension `performance`) |
| `references/report-template.md` | Report structure, required copy, ordering rule, visual identity |
| `references/findings-schema.json` | The `audit-findings.json` contract |

Two terms appear throughout:

- `<target>` — the absolute path to the XbyK project root under audit.
- `<skill-dir>` — the directory holding this file. The reference files live in
  `<skill-dir>/references/`.

Two rules apply to every step. State nothing you can't trace to the scan output, the repository,
or a checklist. Honor `grading.md`'s determinism rule: two runs against an unchanged repository
produce identical finding IDs, severities, and grades.

## 1. Preflight: confirm the target is Xperience by Kentico

1. Resolve `<target>` to an absolute path. Use the path the user names; when they name none, use
   the current working directory.
2. Find every `.csproj` file under `<target>`, skipping `bin/`, `obj/`, and `node_modules/`. Read
   the package references in each.
3. Classify the target by package identifier. Compare whole identifiers with case-insensitive
   string equality — NuGet treats package IDs as case-insensitive, and a substring match confuses
   `Kentico.Xperience.AspNetCore.WebApp` with `kentico.xperience.webapp`.
   - **Xperience by Kentico:** a package reference to `kentico.xperience.webapp`. Record the
     project file and the `Version` value on that reference. That value is `target.xbykVersion`.
   - **Kentico Xperience 13 or older:** a package reference to
     `Kentico.Xperience.AspNetCore.WebApp`, or any reference to `CMS.Core` — a package reference,
     an assembly reference, or a `packages.config` entry — resolving to major version 12 or lower.
4. Decide what happens next:
   - **An XbyK reference exists.** Finish steps 5 and 6 below, then continue to section 2. When a
     KX13 marker also appears in a different project of the same solution, audit only the projects
     that reference `kentico.xperience.webapp`, and tell the user the KX13 project is out of scope.
   - **No XbyK reference and a KX13 marker exists.** Print this message verbatim and stop. Don't
     run the scan, don't run the checklists, and don't write any file:

     > This project is Kentico Xperience 13 or older, which Sentinel doesn't support. For KX13 projects, the [Automated KX13 Assessment](https://foundry.refinedelement.com) covers upgrade readiness.

   - **Neither exists.** Tell the user the path holds no Xperience by Kentico project, ask for the
     correct path, and stop.
5. Derive the site name once here and use that one value in both reports. Take the derivation
   order from `report-template.md`: a name the user gave explicitly, then the last path segment of
   `target.repoPath`, then the solution or `.csproj` file name with its extension stripped.
6. Record `target.repoPath` (the resolved `<target>`) and `target.xbykVersion` for the findings
   file.

## 2. Deterministic pass: run the Sentinel scan

1. Run `sentinel --version`. When the command isn't found, install the CLI and check again:

   ```
   dotnet tool install -g XperienceCommunity.Sentinel --prerelease
   ```

2. Resolve a database connection string for the runtime checks. Stop at the first source that
   yields a real value:
   1. `dotnet user-secrets list --project <target>` — take the value of `CMSConnectionString` or
      `ConnectionStrings:CMSConnectionString`. Pass the `.csproj` path from step 1 instead of
      `<target>` when the folder holds more than one project.
   2. `ConnectionStrings:CMSConnectionString` in `appsettings.json` or any `appsettings.*.json`
      under the project. Accept a populated value only — an empty string or a placeholder counts
      as no value.
   3. Ask the user once. Say that without a connection string the audit skips the runtime checks
      and ships as a partial audit. Accept a decline and move on.

   Never echo the connection string into chat, the reports, or `audit-findings.json`.
3. Run the scan with `<target>` as the working directory, so the CLI writes its output under the
   project rather than wherever the session started:

   ```
   sentinel scan --path <target> --connection-string "<cs>"
   ```

   Omit `--connection-string` when step 2 found no value. The scan then runs static checks only,
   `scan.runtimeEnabled` comes back `false`, and the report carries the partial-audit banner.
4. Read `<target>/sentinel-report/report.json`. Its shape is
   `{ sentinelVersion, scan, summary, executions, findings }`, and each entry in `findings` carries
   `ruleId`, `ruleTitle`, `category`, `severity` (`Error`, `Warning`, or `Info`), `message`,
   `location`, `remediation`, and `quoteEligible`.
5. Record `sentinel.version` from `sentinelVersion` and `sentinel.runtimeEnabled` from
   `scan.runtimeEnabled`. Both go into the findings file unchanged. `report-template.md` keys the
   partial-audit banner off `runtimeEnabled`, so never add or suppress that banner by hand.
6. When the scan exits with code 1, or writes no `report.json`, it didn't complete. Show the CLI
   output, stop, and don't fall back to an AI-only audit — the deliverables assume both passes
   ran. Exit code 2 appears only with `--fail-on`, which this flow doesn't pass.

## 3. AI pass: run the four checklists

1. Read all four checklist files in full before you evaluate anything, in this order:
   `architecture-config.md`, `content-model.md`, `security.md`, `performance.md`. That order is
   also the tie-break order in step 4.
2. Execute every check in every file against `<target>`. Fire a finding only when the check's
   "Pass when" criteria objectively fail. Ambiguous evidence never fires a finding — carry it into
   the dimension's narrative as a stated uncertainty instead, per `grading.md`'s determinism rule.
3. Honor each check's evidence split. Where a check separates a repo-only criterion from an
   environment-dependent part, only the repo-only criterion can fire the finding; report the
   environment-dependent part as "unverified" in the narrative. The checks in `performance.md`
   carry no such split — each one is evaluable from the repository alone.
4. Take `severity`, `effort`, and `fixable` from the check's own locked defaults. Escalate the
   severity only when the evidence clearly warrants it, and say why in the finding's `message`.
   Never de-escalate below the default.
5. Fire each check at most once per audit. When a check fails at several locations, set `location`
   to the lexicographically first affected location (the path, plus `:line` when a line number
   adds precision) and list the remaining locations in `message`. One finding per failed check
   keeps the score and the `instanceKey` stable across runs.
6. Record these fields for each fired finding:
   - `id` — the check ID, such as `AUD-SEC-004`.
   - `source` — `"ai"`.
   - `dimension` — the dimension the checklist file owns.
   - `title` — the failed condition as a short noun phrase, based on the check's own heading. For
     `AUD-SEC-004`, write `HSTS is not enabled`, not `HTTPS is enforced with HSTS outside
     development`. Keep it under roughly 60 characters and free of file paths; `location` carries
     those.
   - `message` — what you found and where, including the evidence and any escalation rationale.
   - `location`, `severity`, `effort`, `fixable`, and `remediation` — the check's remediation text
     written against the evidence you actually found.
   - `references` — the check's Reference URL. Omit the array when the check cites
     `project-convention` rather than a URL; the schema accepts URIs only.
7. Don't resolve overlaps here, either between two checks or against a Sentinel rule. Collect
   everything, then deduplicate in step 4.

## 4. Merge and grade

### 4.1 Convert the Sentinel findings

Build a unified finding from each entry in `report.json`'s `findings` array:

- `id` = `ruleId`, `source` = `"sentinel"`, `title` = `ruleTitle`, `message` = `message`,
  `location` = `location` (`null` when absent).
- `severity` — check `grading.md`'s override table first, then fall back to its base
  Error/Warning/Info map.
- `dimension` — `grading.md`'s rule-to-dimension table. A rule the table doesn't list goes to
  `architecture`, and the rule ID goes into the Methodology appendix list that
  `report-template.md` requires.
- `remediation` — the `remediation` value from `report.json`. When it's null, write one sentence
  naming the fix.
- `effort` and `fixable` — `report.json` carries neither, and the schema requires both. Assign
  them from this table so repeated runs agree:

  | Rule | What it flags | Effort | Fixable |
  |---|---|---|---|
  | `CFG001` | CMSHashStringSalt configuration | S | assisted |
  | `CFG002` | Kentico middleware pipeline order | S | auto |
  | `CFG003` | Plaintext secrets in appsettings.json | S | assisted |
  | `VER001` | Xperience by Kentico version | M | manual |
  | `DEP001` | Outdated NuGet packages | S | auto |
  | `CNT001` | Unused content types | S | assisted |
  | `CNT002` | Stale unused reusable content | S | assisted |
  | `CNT003` | Stale content | M | manual |
  | `CNT004` | Broken media file references | S | assisted |
  | `CNT005` | Malformed Page Builder widget data | M | assisted |
  | `CNT006` | Recent Kentico EventLog errors | M | manual |
  | `CNT010` | Stale unused images | S | assisted |
  | `CNT011` | Stale unused documents and files | S | assisted |
  | Any other rule | — | M | manual |

  Record every rule that fell through to the last row next to the unmapped-dimension list, and
  name both in the Methodology appendix.

### 4.2 Deduplicate: one defect scores once

Deduplicate before you score. Each surviving finding deducts points, so a duplicate pair charges
the project twice for one defect.

**A Sentinel finding beats an AI finding.** When an AI check and a Sentinel rule report the same
defect at the same location, keep the Sentinel finding, drop the AI finding, and note the
corroboration in that dimension's narrative — for example, "Sentinel's static scan flagged the
same middleware ordering defect independently." The pairs below are the known overlaps, not the
whole rule; the rule is same defect, same location.

| AI check | Sentinel rule | Shared defect |
|---|---|---|
| `AUD-ARC-001` | `CFG002` | Kentico middleware trio ordering |
| `AUD-SEC-001` | `CFG003` | A plaintext secret in the same tracked file |
| `AUD-SEC-002` | `CFG001` | `CMSHashStringSalt` configuration |
| `AUD-SEC-010` | `VER001`, `DEP001` | The same package at the same version |
| `AUD-CM-005` | `CNT001` | The same unused content type |

The two findings in a pair can sit in different dimensions — `AUD-SEC-002` is `security` and
`CFG001` is `architecture` — so keeping the Sentinel finding moves the deduction to the Sentinel
finding's dimension. Say so in both dimension narratives when it happens.

**Across AI dimensions.** When two AI checks would fire on one piece of evidence, fire only the
check whose dimension owns that evidence, following the checklists' own cross-reference notes:

| Evidence | Fires | Stands down |
|---|---|---|
| Cache dependency correctness on a content query | `AUD-ARC-003` | `AUD-PRF-002`, which then fires only on its own caching-coverage and duration criteria |
| Unbounded or oversized `WithLinkedItems` depth | `AUD-CM-006` | `AUD-PRF-003`, which then fires only on a missing column projection or a missing `TopN` bound |

When the checklists carry no note for an overlap, fire the check whose "Pass when" criteria match
the evidence more narrowly. When that still ties, fire the check from the file that comes first in
the step 3 evaluation order.

### 4.3 Compute each instanceKey

Follow `grading.md`'s recipe for the string to hash, which differs by `source`. Compute the digest
with a command — never estimate a hash:

```powershell
$s = 'AUD-SEC-004|Program.cs:42'
$sha = [Security.Cryptography.SHA256]::Create()
-join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($s)) | ForEach-Object { $_.ToString('x2') })
```

The POSIX equivalent is `printf '%s' 'AUD-SEC-004|Program.cs:42' | sha256sum`. Both return
`5087d4091d0cc9bb79ce9c1dd14c1e2486cb23183274acd6141a0fa388b394ce`, the digest in `grading.md`'s
worked example. Reproduce that example once before you hash the real strings, so a shell quoting
problem shows up on a known input.

### 4.4 Score the dimensions

Apply `grading.md`'s formula to the deduplicated set, and show the arithmetic in chat as a working
table before you write any file:

| Dimension | Critical | High | Medium | Low | Deduction | Score | Grade |
|---|---|---|---|---|---|---|---|
| Architecture & configuration | 0 | 2 | 1 | 0 | 24 | 76 | C |

(The row is an illustration. Use the real counts.)

Then state the overall score — the unweighted mean of the four dimension scores, rounded half-up —
and its letter grade.

`findings-schema.json` stores `grades.overall` as an integer score with no letter beside it, while
the Scorecard needs both. Derive the overall letter from the stored score with the same bands
(90/80/70/60) and leave the JSON as the schema defines it: adding a letter field there fails
validation.

## 5. Write the deliverables

1. Create `<target>/sentinel-audit/`.
2. Write `audit-findings.json` first — both reports read from it. Populate exactly the fields
   `findings-schema.json` defines and nothing more. The schema sets `additionalProperties: false`
   at every level, so one extra key fails validation.

   | Field | Value |
   |---|---|
   | `schemaVersion` | `1` |
   | `generatedAt` | The current UTC time in ISO 8601 form, such as `2026-08-25T14:03:00Z` |
   | `auditSkillVersion` | The `version` value in `<skill-dir>/../../.claude-plugin/plugin.json` |
   | `sentinel.version`, `sentinel.runtimeEnabled` | From step 2 |
   | `target.repoPath`, `target.xbykVersion` | From step 1 |
   | `grades` | From step 4.4 |
   | `findings` | The merged, deduplicated set |

   Sort `findings` the way `report-template.md` orders report tables — severity descending
   (Critical, High, Medium, Low), then dimension in display-name order (Architecture &
   configuration, Content model, Security, Performance), then `id` ascending — so the file stays
   stable between runs. Set `location` to `null` for a repo-wide finding, and omit `references`
   when the finding cites no URL.

3. Validate the file, and write no report until the validator exits 0:

   ```
   pwsh <skill-dir>/../../../../scripts/validate-audit-findings.ps1 -Path <target>/sentinel-audit/audit-findings.json
   ```

   The script checks the document against the schema and recomputes every grade from the findings.
   Fix what it reports and run it again. A marketplace install without the repository checkout has
   no `scripts/` directory: in that case recompute the grades by hand with `grading.md`'s formula
   and re-check the document field by field against `references/findings-schema.json` before you
   continue.
4. Write `audit-report.md`, then `audit-report.html`, following `references/report-template.md`
   exactly — its section order, cover block, shared findings-table format, ordering rule,
   conditional partial-audit banner, the verbatim Methodology disclaimer and Next steps CTA block,
   and the palette, typography, and layout rules for the HTML file. Use the site name from step 1
   in the H1 and the HTML `<title>`. Every fact in both files traces to `audit-findings.json`:
   introduce no finding, score, or claim that isn't there, and never let the two files disagree.
5. Tell the user where the three files are, and recommend adding `sentinel-audit/` to the target's
   `.gitignore`, along with the `sentinel-report/` directory the scan wrote in step 2. Edit
   `.gitignore` only if the user asks you to.

## 6. Close

1. Summarize in chat, in this order: the overall score and grade, the four dimension grades, and
   the Critical and High findings in the report's ordering (severity, then dimension, then `id`).
   Name the scan mode when it was partial, and name the checks whose environment-dependent parts
   came back unverified.
2. Present exactly the two follow-on paths from the report's Next steps section — the
   `sentinel quote` fixed-price remediation path and the Architecture Review & Roadmap engagement —
   by reproducing that CTA block from `references/report-template.md` verbatim. Add no third
   option, no lead-in pitch, and no other product.
3. Stop there. The audit sends nothing anywhere. Never offer to submit the findings on the user's
   behalf: `sentinel quote` is the user's to run.
