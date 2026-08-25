# Grading reference

This file defines how the audit skill assigns severity, maps findings to a dimension, scores each dimension, and computes the `instanceKey` that identifies a finding across runs. Apply these rules exactly — do not improvise a severity, score, or key by any other method.

## Unified severity scale

Every finding — whether it comes from Sentinel or from the AI checklist pass — carries exactly one of these four severities:

| Severity | Criteria |
|---|---|
| Critical | The finding describes an exploitable vulnerability or a data-loss risk in production. |
| High | The finding describes behavior that is incorrect or insecure and is likely to cause a real problem. |
| Medium | The finding describes a best-practice violation with a tangible cost (performance, maintainability, or content-model integrity). |
| Low | The finding describes a hygiene issue. |

## Sentinel severity mapping

Sentinel reports each of its own findings at one of three levels: Error, Warning, or Info. Convert a Sentinel finding to a unified severity with this base mapping:

| Sentinel level | Unified severity |
|---|---|
| Error | High |
| Warning | Medium |
| Info | Low |

### Overrides

Some Sentinel rules carry a fixed unified severity regardless of the level Sentinel assigns them. Check the rule ID against this table before falling back to the base mapping above.

| Sentinel rule | Override severity | Reason |
|---|---|---|
| `CFG003` | Critical | The rule flags a plaintext secret in configuration. |

This table is append-only. Add new rows as new override rules are identified. Never remove or reinterpret an existing row.

## Sentinel rule to dimension mapping

Every finding belongs to exactly one dimension: `architecture`, `contentModel`, `security`, or `performance`. Map a Sentinel rule to a dimension with this table:

| Sentinel rule | Dimension |
|---|---|
| `VER001`, `DEP001` | security |
| `CFG003` and any secret-related config rule | security |
| Other `CFG` rules (middleware order, salt configuration) | architecture |
| Widget-registration rules (registered but never placed) | architecture |
| Content-type/field rules and every `CNT`-prefixed rule — unused content types, unused and stale content items, stale content, broken assets, widget property data, and the EventLog rule that shares the prefix | contentModel |
| `SYS`-prefixed rules (Sentinel internals — a Sentinel check that threw during the scan) | *(none)* — excluded from grading; report in the Methodology section as a check that didn't execute |
| *(unmapped/new Sentinel rules)* | architecture (default) — flag in the report appendix |

When a Sentinel rule doesn't appear in this table, assign it to `architecture` and add a note in the report's Methodology appendix identifying the unmapped rule ID so the mapping table can be extended later.

`SYS`-prefixed rules are the one exception to that default. They describe a failure in the scanner rather than a defect in the audited project, so they never enter the graded set, never receive a dimension, and never appear in `audit-findings.json`. The report accounts for them in Methodology as missing coverage instead.

AI-sourced findings (`source: "ai"`) don't need this table — each check in `architecture-config.md`, `content-model.md`, `security.md`, and `performance.md` already declares its own dimension by which reference file it lives in.

## Scoring formula

Compute each dimension's score independently:

1. Start the dimension at 100.
2. For every finding assigned to that dimension, subtract a deduction based on its unified severity: Critical 25, High 10, Medium 4, Low 1.
3. Floor the result at 0 — a dimension score never goes negative.
4. Convert the floored score to a letter grade: A for 90 or above, B for 80 or above, C for 70 or above, D for 60 or above, F for anything below 60.

Compute the overall score as the unweighted mean of the four dimension scores (`architecture`, `contentModel`, `security`, `performance`), rounded half-up — ties round away from zero — to the nearest integer. Convert the overall score to a letter grade using the same bands as step 4.

### Worked example

The security dimension has 1 Critical, 2 High, 3 Medium, and 4 Low findings:

```
100 − 25 (1 × 25, Critical)
    − 20 (2 × 10, High)
    − 12 (3 × 4, Medium)
    −  4 (4 × 1, Low)
= 39
```

39 is below 60, so the security dimension scores 39 and grades **F**.

## instanceKey recipe

Every finding carries an `instanceKey`: the lowercase hexadecimal SHA-256 digest of a UTF-8 string built from the finding's own fields. The string differs by `source`:

- `source: "sentinel"` — digest `"{id}|{location or "project"}|{message}"`.
- `source: "ai"` — digest `"{id}|{location or "project"}"` (no message component).

In both cases, substitute the literal string `project` for `{location}` when the finding has no location (a repo-wide finding rather than a specific file or line).

AI-sourced findings omit the message from the key because the wording an AI check produces for the same underlying condition can vary between runs. Including the message would give the same finding a different key every run, breaking identity tracking across audits. Sentinel's messages are deterministic templates, so including the message for `source: "sentinel"` findings is safe and adds specificity.

### Worked example

For an AI finding with `id: "AUD-SEC-004"` and `location: "Program.cs:42"`, hash the string `AUD-SEC-004|Program.cs:42`:

```
sha256("AUD-SEC-004|Program.cs:42")
= 5087d4091d0cc9bb79ce9c1dd14c1e2486cb23183274acd6141a0fa388b394ce
```

`5087d4091d0cc9bb79ce9c1dd14c1e2486cb23183274acd6141a0fa388b394ce` is the finding's `instanceKey`.

## Determinism rule

A check either fires or it doesn't, based strictly on the "Pass when" criteria written in its checklist entry. When the evidence is ambiguous, do not fire the finding — note the uncertainty in the report's narrative instead of guessing at a severity or outcome.

Two consecutive audit runs against an unchanged repository must produce identical finding IDs, severities, and grades. `generatedAt` and free-text message wording may differ between runs; nothing else should.
