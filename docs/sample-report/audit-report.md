# Xperience by Kentico health audit — Sample: production Xperience by Kentico site

> **Published sample** — a real audit of a production site, with identifying detail generalized.

| | |
|---|---|
| Xperience by Kentico version | 31.7.3 |
| Scan mode | Full |
| Sentinel version | 0.4.3-alpha |
| Audit skill version | 0.1.0 |
| Date | August 26, 2026 |

## Executive summary

The site earns an overall score of 63, a D. It is a mature, actively maintained Xperience by Kentico 31.7.3 application running on a supported release, and several parts of it are genuinely well built: output caching is preview-aware, response compression and long-lived static asset headers are in place, background services keep external pricing and catalog data warm, every stored credential is compared in constant time, and no secret is committed anywhere in the repository. Security is nevertheless the weakest area and the one to act on first. Editor-supplied rich text from the content hub is rendered into pages without passing through the HTML sanitizer the project already ships, the shopping-cart endpoints accept state-changing requests without an anti-forgery token, the public catalog API that AI agents call carries no rate limit, the blog image upload trusts a file's declared type and its client-supplied name, and one third-party library resolves to a version with a published security advisory. Two further risks sit outside security: the continuous-delivery repository is configured to track every object in the system with no restore mode set, so an unscoped restore against production could delete content that environment added on its own, and one outbound integration call runs with no timeout, letting a slow third party hold a visitor's request for over a minute. The content model is sound in shape - shared entities are modelled once and linked - but field naming and types have drifted between types, several page-builder widgets store article-grade content inside widget properties where nothing else can reach it, and a large backlog of content has gone untouched for more than six months. Performance findings cluster around data access: the site navigation and most page controllers run content queries on every request with no caching, and no query anywhere limits the columns it retrieves. None of this is structural; the remediation list is long but almost every item is a contained, well-understood change.

## Scorecard

| Dimension | Score | Grade |
|---|---|---|
| Architecture & configuration | 72 | C |
| Content model | 79 | C |
| Security | 32 | F |
| Performance | 70 | C |
| **Overall** | **63** | **D** |

## Priority findings

| ID | Severity | Title | Effort | Fixable |
|---|---|---|---|---|
| AUD-ARC-009 | High | CI/CD repository tracks every object type | M | manual |
| CFG002 | High | Kentico middleware pipeline order | S | auto |
| AUD-SEC-006 | High | CMS rich text renders through Html.Raw unsanitized | M | assisted |
| AUD-SEC-007 | High | Cart endpoints validate no antiforgery token | M | assisted |
| AUD-SEC-008 | High | Public agent-facing endpoints have no rate limit | M | assisted |
| AUD-SEC-009 | High | Blog image upload skips content and filename checks | M | assisted |
| AUD-SEC-010 | High | Transitive OpenTelemetry.Api carries a known advisory | S | auto |
| AUD-PRF-007 | High | Outbound HTTP client has no timeout or retry | M | assisted |
| AUD-PRF-008 | High | Request path blocks on a Task with .Result | S | auto |

Effort — S (small), M (medium), or L (large). Fixable — auto (a scripted or
mechanical fix), assisted (a developer applies the remediation text directly),
or manual (requires human judgment or a stakeholder decision).

## Architecture & configuration

Architecture and configuration scores 72, a C, on four findings: two High and two Medium. The two High findings are independent of each other. The continuous-delivery repository configuration tracks every object type and every content item type through blanket include directives and sets no restore mode, while the deployment workflow ships that repository to the production App Service and its own comment warns that a restore deletes objects the target does not contain. Separately, the Kentico middleware trio is not contiguous: a development-only authentication and management block is registered between the static-file and Kentico calls, which is the exact pattern that breaks Page Builder preview URL rewriting. Sentinel's static scan flagged that middleware ordering defect independently, so the deduction for it sits with the Sentinel rule rather than with the equivalent AI check, which stood down. The two Medium findings are contained: one transactional email cache invalidates only when the content type's structure changes rather than when an editor edits a template, and two raw-SQL cleanup statements discard their exceptions with no log entry. Everything else in this dimension passed - development-only surfaces are correctly gated, no secret is committed, page-builder components are registered and their view imports are in place, content-tree routing is enabled with attribute routes as a documented fallback, generated content-type classes carry no hand edits, and the project runs a Kentico release supported through October 2027 on a matching .NET target.

| ID | Severity | Title | Effort | Fixable |
|---|---|---|---|---|
| AUD-ARC-009 | High | CI/CD repository tracks every object type | M | manual |
| CFG002 | High | Kentico middleware pipeline order | S | auto |
| AUD-ARC-003 | Medium | Email template cache keyed only on the type schema | M | assisted |
| AUD-ARC-008 | Medium | Exceptions swallowed by empty catch blocks | M | assisted |

## Content model

Content model scores 79, a C. The shape of the model is right: shared entities such as team members, testimonials, products, services and articles are each modelled once as a reusable content type and linked into pages, only one language is configured so variant consistency does not apply, linked-item retrieval is bounded everywhere, and no legacy media-library API remains in the code. The three Medium findings are consistency and modelling problems rather than defects. Field names and types have drifted because no reusable field schema exists - the SEO block is redeclared on every page type with inconsistent membership, and the same concept appears under different names and even different data types on different types. FAQ grouping runs on a free-text string matched against a URL slug, with no controlled vocabulary behind it. And a handful of page-builder widgets store content that belongs in content items - descriptive and promotional copy - inside widget properties, where it is invisible to search and cannot be reused. The volume in this dimension comes from hygiene: a batch of findings from the runtime scan cover a couple of content types with no items, a single unreferenced reusable item, and a large batch of content items untouched for more than six months. Per-rule caps keep that volume from dominating the grade, which is the intent - it is a cleanup backlog, not a defect. One Low finding notes that one content-creation endpoint files new items at the workspace root instead of into a folder, unlike other similar endpoints in the codebase, which do.

| ID | Severity | Title | Effort | Fixable |
|---|---|---|---|---|
| AUD-CM-002 | Medium | Field names and types diverge across content types | M | manual |
| AUD-CM-003 | Medium | FAQ grouping uses a free-text field | M | assisted |
| AUD-CM-008 | Medium | Widgets store structured content in their properties | L | manual |
| AUD-CM-004 | Low | Admin content-creation endpoint files items at the workspace root | M | manual |
| CNT001 | Low | Unused content types (a couple of findings) | S | assisted |
| CNT002 | Low | Stale unused reusable content | S | assisted |
| CNT003 | Low | Stale content (more than 30 findings) | M | manual |

## Security

Security scores 32, an F, and is the dimension to address first. Five High findings come from the review and more than a dozen dependency findings from the scan. The most exposed of the five is unsanitized output: the project ships a configured HTML sanitizer, but no view calls it, so rich text an editor types in the content hub renders straight into pages - including one field emitted verbatim as embed markup and several interpolated into a structured-data script block. Next, the shopping-cart controller changes server-side state on four POST actions with no anti-forgery token and no documented exemption; no version of ASP.NET Core validates a controller action automatically. The public catalog API that AI agents call, and another public endpoint that makes an outbound third-party call on every anonymous request, both run with no rate-limiting policy at all. The blog image upload checks only the file extension - not the file's actual content or its name - before storing the upload as a published asset. Finally, one transitive dependency resolves to a version with a Moderate published advisory, which is a different problem from the version-currency findings the scan reports and is counted separately. The rest of the dimension is in good order: no secret is committed in any tracked file, the hash salt key is declared and empty as it should be, no seed script creates a default administrator account, HTTPS redirection and HSTS are wired correctly for their environments, the full set of security response headers is present with a restrictive content security policy, and every credential comparison uses a constant-time check. Two environment-dependent items could not be verified from the repository and are noted in Methodology.

| ID | Severity | Title | Effort | Fixable |
|---|---|---|---|---|
| AUD-SEC-006 | High | CMS rich text renders through Html.Raw unsanitized | M | assisted |
| AUD-SEC-007 | High | Cart endpoints validate no antiforgery token | M | assisted |
| AUD-SEC-008 | High | Public agent-facing endpoints have no rate limit | M | assisted |
| AUD-SEC-009 | High | Blog image upload skips content and filename checks | M | assisted |
| AUD-SEC-010 | High | Transitive OpenTelemetry.Api carries a known advisory | S | auto |
| DEP001 | Medium | Outdated NuGet packages (several findings) | S | auto |
| DEP001 | Low | Outdated NuGet packages (about a dozen findings) | S | auto |
| VER001 | Low | Xperience by Kentico version | M | manual |

## Performance

Performance scores 70, a C, on six findings. The two High findings are both single points of failure with contained fixes. One named HTTP client used on a public submission path sets no timeout at all, so it runs at the hundred-second default with no retry while a visitor waits; every other outbound client in the project pairs an explicit timeout with a retry strategy, so this one is an omission rather than a pattern. The other is a single blocking call on a Task inside a controller action, the only such call in the codebase. The two Medium findings share a root: data access. The site navigation renders on every page and runs its content queries with no caching, as do most page controllers, even though the blog and site-content repositories already demonstrate the correct caching pattern with item-level dependencies. And no content query anywhere limits the columns it retrieves, so listing pages pull full article bodies to render cards. The two Low findings are front-end hygiene - store pages load web fonts render-blocking while the main site layout loads them correctly, and images ship in legacy formats at a single size with no responsive sizing anywhere. The dimension also has real strengths: output caching is registered with an explicit preview-mode bypass, compression is configured with both Brotli and Gzip ahead of the response-generating middleware, static assets carry immutable year-long cache headers, many stylesheets are bundled into one request, and a background service keeps external pricing and catalog data warm so requests never wait on those lookups.

| ID | Severity | Title | Effort | Fixable |
|---|---|---|---|---|
| AUD-PRF-007 | High | Outbound HTTP client has no timeout or retry | M | assisted |
| AUD-PRF-008 | High | Request path blocks on a Task with .Result | S | auto |
| AUD-PRF-002 | Medium | Navigation and page controllers query content uncached | M | assisted |
| AUD-PRF-003 | Medium | Content queries select every column | M | assisted |
| AUD-PRF-006 | Low | Images ship in legacy formats at a single size | M | manual |
| AUD-PRF-010 | Low | Store layout loads web fonts render-blocking | S | assisted |

## Remediation roadmap

**Quick wins**

- **High** — CFG002: Kentico middleware pipeline order (Architecture & configuration)
- **High** — AUD-SEC-010: Transitive OpenTelemetry.Api carries a known advisory (Security)
- **High** — AUD-PRF-008: Request path blocks on a Task with .Result (Performance)
- **Medium** — DEP001: Outdated NuGet packages (several findings) (Security)
- **Low** — CNT001: Unused content types (a couple of findings) (Content model)
- **Low** — CNT002: Stale unused reusable content (Content model)
- **Low** — DEP001: Outdated NuGet packages (about a dozen findings) (Security)
- **Low** — AUD-PRF-010: Store layout loads web fonts render-blocking (Performance)

**Projects**

- **High** — AUD-ARC-009: CI/CD repository tracks every object type (Architecture & configuration)
- **High** — AUD-SEC-006: CMS rich text renders through Html.Raw unsanitized (Security)
- **High** — AUD-SEC-007: Cart endpoints validate no antiforgery token (Security)
- **High** — AUD-SEC-008: Public agent-facing endpoints have no rate limit (Security)
- **High** — AUD-SEC-009: Blog image upload skips content and filename checks (Security)
- **High** — AUD-PRF-007: Outbound HTTP client has no timeout or retry (Performance)
- **Medium** — AUD-ARC-003: Email template cache keyed only on the type schema (Architecture & configuration)
- **Medium** — AUD-ARC-008: Exceptions swallowed by empty catch blocks (Architecture & configuration)
- **Medium** — AUD-CM-002: Field names and types diverge across content types (Content model)
- **Medium** — AUD-CM-003: FAQ grouping uses a free-text field (Content model)
- **Medium** — AUD-CM-008: Widgets store structured content in their properties (Content model)
- **Medium** — AUD-PRF-002: Navigation and page controllers query content uncached (Performance)
- **Medium** — AUD-PRF-003: Content queries select every column (Performance)
- **Low** — AUD-CM-004: Admin content-creation endpoint files items at the workspace root (Content model)
- **Low** — CNT003: Stale content (more than 30 findings) (Content model)
- **Low** — VER001: Xperience by Kentico version (Security)
- **Low** — AUD-PRF-006: Images ship in legacy formats at a single size (Performance)

## Methodology

**What ran.** Sentinel CLI 0.4.3-alpha executed a full scan of the repository on August 26, 2026. Runtime checks were enabled, so the database-backed rules covering unused content types, unreferenced reusable content, stale content, broken media references, widget property data and recent event-log errors all executed against the live CMS database; all thirteen scan checks completed and none failed to execute. On top of that deterministic pass, four AI checklists were evaluated against the repository: `architecture-config.md` (10 checks evaluated, 4 fired), `content-model.md` (10 checks evaluated, 4 fired), `security.md` (10 checks evaluated, 5 fired) and `performance.md` (10 checks evaluated, 6 fired) - 40 checks evaluated and 19 fired in total. One of those 19, the middleware-ordering check in the architecture checklist, reported the same defect at the same location as a Sentinel rule and was deduplicated so the defect is charged once, leaving 18 review findings alongside 61 scan findings for a total of 79.

**Unverified items.** Several checks separate what the repository can prove from what only a live environment can. The hash salt is declared and empty in configuration as intended, but whether it resolves to a real random value in each deployed environment is unverified. Administrator account hygiene, role assignments and any routing rules configured in front of the administration path outside the repository are unverified. Whether the upload storage location has execute permissions disabled at the hosting layer is unverified. Live content hub folder organisation, published URL slug cleanliness and the channel's former-URL setting were not queried and are unverified. None of these unverified items fired a finding; each is reported here instead.

**Citations.** The audit skill's public source is [github.com/refined-element/xperience-community-sentinel](https://github.com/refined-element/xperience-community-sentinel). Per-finding documentation references live in each finding's `references` entry in `audit-findings.json` and inform its remediation text.

**Grading formula.** Each dimension starts at 100. Every Critical finding subtracts 25 and every High finding subtracts 10, both uncapped. Medium and Low findings are grouped by rule ID and capped per rule: Medium subtracts 4 points per finding up to 12 points per rule ID, Low subtracts 1 point per finding up to 5 points per rule ID, so a single high-volume hygiene rule cannot outweigh a genuine defect. Scores floor at 0 and convert to letters at 90, 80, 70 and 60. The overall score is the unweighted mean of the four dimension scores, rounded half-up.

**Unmapped-rule appendix.** No unmapped Sentinel rules were encountered during this audit. Every rule that produced a finding appears in the dimension-mapping table and in the effort/fixability table, and no Sentinel Internals findings were recorded, so no scan check went unexecuted.

> This report is an AI-assisted initial pass produced by the open-source Sentinel audit skill. It is not a substitute for a human architect review.
>
> The AI checklist findings are a floor, not a census: a check that isn't listed here wasn't found to fail, which is not the same as a verified pass.

## Next steps

> **Fix these findings at a fixed price.** Run `sentinel quote` to send a sanitized summary of this report to Refined Element and receive an itemized quote.
>
> **Have the architect validate this audit.** The [Architecture Review & Roadmap](https://foundry.refinedelement.com) engagement reviews, extends, and prioritizes these findings with you.
