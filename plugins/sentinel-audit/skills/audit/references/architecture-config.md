# Architecture and configuration checklist

These checks form the AI pass for the `architecture` dimension. Run every check in this file against the target repository. A check fires a finding only when its "Pass when" criteria objectively fail — when the evidence is ambiguous, don't fire the finding; note the uncertainty in the report narrative instead (see the determinism rule in `grading.md`).

Several checks here inspect the same surface area as one of Sentinel's own static `CFG` rules (middleware order, hash salt, plaintext secrets). That overlap is intentional: Sentinel's checks are narrow pattern matches against known file paths, and the AI pass is expected to catch what a fixed pattern can't — non-standard file layouts, values that are only set at deploy time, and judgment calls a regex can't make. Run these checks regardless of what Sentinel already reported.

### AUD-ARC-001: Kentico middleware trio is ordered correctly

- **Inspect:** `Program.cs` (or the app's composition root if middleware is registered elsewhere) — the sequence of `InitKentico()`, `UseStaticFiles()`, `UseKentico()`, and anything registered between them. Also check where asset-pipeline middleware (CSS/JS bundlers, minifiers, output-cache middleware for static assets) is registered relative to `UseKentico()`.
- **Pass when:** the three calls appear in that exact order (`InitKentico` → `UseStaticFiles` → `UseKentico`) with no other middleware registration between them — comments and blank lines don't count as "between" — and any asset-pipeline middleware runs after `UseKentico()`.
- **Severity when failed:** High · **Effort:** S · **Fixable:** auto
- **Remediation:** Reorder the pipeline so the Kentico trio is contiguous and asset middleware follows it. Page Builder preview rewrites resource URLs with a `/cmsctx/` prefix that earlier-registered middleware never sees, so a bundler placed before `UseKentico()` serves empty or wrong MIME types in preview mode.
- **Reference:** https://docs.kentico.com/documentation/developers-and-admins/development/website-development-basics/configure-new-projects

### AUD-ARC-002: Dev-only surfaces are gated behind `IsDevelopment()`

- **Inspect:** Every controller, endpoint, and middleware registration that exposes a debug endpoint, an in-process admin/management API, a Swagger/OpenAPI document, or a diagnostic page (search for `/debug/`, `/swagger`, `ManagementApi`, `DeveloperExceptionPage`, and similarly named routes or services). For each, find the guard condition that decides whether it's registered or reachable.
- **Pass when:** every such surface is registered or made reachable only inside an `env.IsDevelopment()` (or equivalent `IWebHostEnvironment`/`IHostEnvironment` check) branch, evaluated at startup or per-request, with no code path that reaches it when the environment is Staging or Production. A surface that's registered unconditionally but requires a separately-configured secret is still a fail unless that secret check happens in addition to the environment check, not instead of it.
- **Severity when failed:** High · **Effort:** S · **Fixable:** auto
- **Remediation:** Wrap the registration (`app.Map...`, `services.Add...`) or the controller/action in an `if (app.Environment.IsDevelopment())` guard, or apply an `[ApiExplorerSettings(IgnoreApi = true)]`-style attribute plus a runtime environment check inside the action. Don't rely on `#if DEBUG` alone — that's a compile-time switch, and a Release build deployed with a Development `ASPNETCORE_ENVIRONMENT` value would still expose the surface.
- **Reference:** https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments

### AUD-ARC-003: Content queries use `IProgressiveCache` with item-level cache dependencies

- **Inspect:** Every repository/service class that calls `IContentQueryExecutor`, `IWebPageQueryResultMapper`, or equivalent content-query APIs. For each, check whether the result is wrapped in an `IProgressiveCache.LoadAsync`/`Load` call, and inspect the cache key and the `CMSCacheDependency` (or `CacheDependencyBuilder` output) passed alongside it.
- **Pass when:** every content query result that's reused across requests is cached through `IProgressiveCache`, the cache key includes enough specificity to avoid cross-content-type collisions (for example `contentitem|bycontenttype|<type>`, not a single hardcoded key shared by unrelated queries), and the cache dependency is built from the actual items returned (by content type, by item GUID, or by content-tree path) rather than a dependency that never expires or one keyed only on the content type's schema definition (which only invalidates on a type-structure change, not a content edit).
- **Severity when failed:** Medium · **Effort:** M · **Fixable:** assisted
- **Remediation:** Wrap the query in `IProgressiveCache.LoadAsync(...)` with a cache key scoped to the query's actual parameters, and build the cache dependency with `CacheDependencyBuilder` against the content type or specific items the query touches — a fresh `CacheDependencyBuilder` instance per call, since the builder is stateful.
- **Reference:** https://docs.kentico.com/documentation/developers-and-admins/development/caching/cache-dependencies

### AUD-ARC-004: Configuration layering: secrets come from user-secrets/environment, not committed appsettings

- **Inspect:** `appsettings.json` and any `appsettings.*.json` files tracked in source control. For each configuration key that represents a secret (connection strings, API keys, salts, passwords, tokens, webhook secrets), check whether the committed value is empty/absent versus populated.
- **Pass when:** every secret-shaped key in a tracked `appsettings*.json` file is either absent, has an empty-string placeholder, a Key Vault reference string (`@Microsoft.KeyVault(...)`), or an environment-variable placeholder (`${VAR}`, `$(VAR)`) — never a live-looking value. This check fires only on an actual committed secret or committed real value; a repo with zero committed secrets passes regardless of whether it also documents where the real values come from elsewhere.
- **Severity when failed:** High · **Effort:** S · **Fixable:** assisted
- **Remediation:** Remove the committed value, replace it with an empty string (or omit the key entirely if the app tolerates a missing key with a clear startup error), and move the real value to `dotnet user-secrets set` locally and to the hosting platform's app-settings/secret store in every deployed environment. As good practice — not required to pass this check — document in the project's README or CLAUDE.md where each real value comes from, so a future contributor doesn't try to hardcode it back in.
- **Reference:** https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets

### AUD-ARC-005: Page Builder components register via attributes and `_ViewImports.cshtml` covers component view folders

- **Inspect:** Every widget, section, and page template class decorated with (or expected to be decorated with) `RegisterWidget`, `RegisterSection`, or `RegisterPageTemplate` assembly attributes. Separately, check for a `_ViewImports.cshtml` file in the component view folder tree (for example `Components/_ViewImports.cshtml`) and confirm it imports the tag helpers Page Builder needs (`@addTagHelper *, Kentico.PageBuilder.Web.Mvc`, plus the project's own tag helper assembly if applicable).
- **Pass when:** every Page Builder component (widget, section, page template) has exactly one matching registration attribute with a project-prefixed, globally unique identifier, and every folder under the components tree that contains a `.cshtml` view reachable by a registered component either has its own `_ViewImports.cshtml` or inherits one from a parent directory in the same tree. Razor resolves `_ViewImports.cshtml` from a view's own directory upward — a `_ViewImports.cshtml` that only lives under `Views/` or `PageTemplates/` doesn't apply to files under `Components/`.
- **Severity when failed:** Medium · **Effort:** S · **Fixable:** auto
- **Remediation:** Add the missing `RegisterWidget`/`RegisterSection`/`RegisterPageTemplate` attribute (in a dedicated `ComponentRegister.cs` for partial-view-only widgets, or directly on the view component class), and add a `_ViewImports.cshtml` at the top of the components directory tree that imports the Page Builder tag helpers. Without it, `<widget-zone />` and similar tag helpers compile as inert literal elements and Page Builder reports "no widget zones" even though the markup looks correct.
- **Reference:** https://docs.kentico.com/documentation/developers-and-admins/development/builders/page-builder/widgets-for-page-builder

### AUD-ARC-006: Routing uses content-tree-based routing where pages exist in the CMS

- **Inspect:** `Program.cs`/startup code for `UseWebPageRouting` (or the project's content-tree routing registration), and the set of MVC attribute-routed controllers/actions. Cross-reference against which page content types are marked "Include in routing" or otherwise expected to be reachable through the content tree.
- **Pass when:** content-tree-based routing is enabled for the site, every page content type that has real content-tree items relies on it (or on a documented, deliberate exception) for URL generation, and MVC attribute routes exist only as a fallback for content that isn't (or can't yet be) modeled in the CMS — not as the primary mechanism duplicating a route the content tree already serves.
- **Severity when failed:** Medium · **Effort:** M · **Fixable:** manual
- **Remediation:** Enable content-tree-based routing (`UseWebPageRouting` inside `AddKentico`) for content types that should be reachable by the CMS-managed content hierarchy, and remove or clearly scope MVC attribute routes that duplicate a content-tree URL rather than serving as an intentional fallback.
- **Reference:** https://docs.kentico.com/documentation/developers-and-admins/development/routing/content-tree-based-routing

### AUD-ARC-007: Generated content-type classes are regenerated code, not hand-edited

- **Inspect:** Every file matching the codegen output pattern (typically `*.generated.cs` under a content-types folder, or a header comment noting it was produced by the code generator). Diff the property list and attribute values in these files against what the corresponding content type actually defines in the CMS, if that's checkable; otherwise inspect the file for edits that don't look machine-generated (custom logic inside the generated class body itself, rather than in a paired `partial` class file).
- **Pass when:** generated files contain only generator output (properties, constants, and the standard scaffolding the generator produces), and any custom logic for that content type lives in a separate `partial class` file alongside the generated one — never inside the `.generated.cs` file itself.
- **Severity when failed:** Medium · **Effort:** S · **Fixable:** manual
- **Remediation:** Move any custom logic found inside a `.generated.cs` file into a new partial class file (same namespace and class name, without the `.generated` suffix), then re-run `dotnet run -- --kxp-codegen` to confirm the generated file regenerates cleanly without losing the custom logic.
- **Reference:** https://docs.kentico.com/documentation/developers-and-admins/api/generate-code-files-for-system-objects

### AUD-ARC-008: Errors are logged through `IEventLogService` or a registered logging provider, not swallowed

- **Inspect:** Every `catch` block in the codebase, particularly around external HTTP calls, payment/webhook handlers, and content-query code. Check whether the caught exception is logged (through `IEventLogService`, `ILogger<T>`, or an equivalent registered provider) before the catch block returns, rethrows, or falls through.
- **Pass when:** every `catch` block either rethrows (or wraps and rethrows) the exception, or logs it with enough context (exception details plus a source/operation identifier) through `IEventLogService` or an injected `ILogger<T>`, before continuing. An empty `catch { }` block, or a catch block that only sets a boolean/returns a default value with no logging call, is a fail unless a code comment documents why the exception is deliberately and safely ignored (for example, an expected `TaskCanceledException` from a timeout that's handled by a retry policy).
- **Severity when failed:** Medium · **Effort:** M · **Fixable:** assisted
- **Remediation:** Add a logging call inside the catch block — `ILogger<T>.LogError(ex, "...")` for the standard .NET logging pipeline the Xperience API examples now build on, or `IEventLogService`/`EventLogServiceExtensions.LogException` for code that already depends on that interface — before the block returns or continues. Xperience by Kentico's current logging examples are built on `ILogger<TCategoryName>`; `IEventLogService` (from `CMS.Core`) remains available and is still common in code carried over from older Xperience projects, so either is acceptable as long as the call happens and the log destination is a registered provider, not a local file or console write that nothing consumes.
- **Reference:** https://docs.kentico.com/api/development/event-log

### AUD-ARC-009: CI/CD repository configuration is scoped (no blind full-restore against production)

- **Inspect:** `repository.config` (or the CI/CD repository's configuration file) at the root of the CI/CD repository, and any deployment scripts or pipeline definitions that invoke a CI/CD restore. Check the `<RestoreMode>` element and whether `<IncludeAll />` is used instead of explicit `<IncludedObjectTypes>`/`<IncludedContentItemsOfType>` scoping.
- **Pass when:** the repository configuration explicitly lists the object types and content item types it tracks (rather than relying on `<IncludeAll />` for a production-facing repository), and any pipeline step that runs a restore against a production or shared environment uses a restore mode appropriate to that environment (`Create` or `CreateUpdate`, not an unscoped `Full` restore that can delete objects the target environment added independently).
- **Severity when failed:** High · **Effort:** M · **Fixable:** manual
- **Remediation:** Replace `<IncludeAll />` with explicit `<IncludedObjectTypes>`/`<IncludedContentItemsOfType>` elements naming only the object and content types this project manages through CI/CD, and set `<RestoreMode>` deliberately per target environment rather than defaulting to a full synchronization.
- **Reference:** https://docs.kentico.com/documentation/developers-and-admins/ci-cd/configure-ci-cd-repositories

### AUD-ARC-010: Project targets a supported XbyK refresh and .NET version

- **Inspect:** The `.csproj` file(s) for the exact `kentico.xperience.webapp` (or related Kentico package) version and the `<TargetFramework>` element — both always extractable from source. Then cross-reference the extracted package version against the version-range table on Kentico's published support-policy page (see Reference) to determine whether it falls inside a currently supported window.
- **Pass when:** **Repo-only criterion (always evaluable):** record the exact referenced Kentico package version and target framework regardless of what follows — this part never fails on its own. **Support-window criterion (depends on a live lookup, not on the deployment environment):** the extracted package version falls inside a version range the support-policy page currently lists as supported, and the target framework is one Kentico's system-requirements documentation lists as supported for that version. Fire this check only when the lookup succeeds and clearly shows the version or target framework is outside its supported window. Kentico's supported-version ranges and their end dates change every year (the support cycle resets each November), so don't rely on a remembered or previously-cited date — re-check the live page each audit run. When the support-policy page can't be reached during the audit, report the extracted version and target framework in the narrative with the supported-status field marked "unverified" — don't fire a finding based on an assumed support window.
- **Severity when failed:** Medium · **Effort:** M · **Fixable:** manual
- **Remediation:** Plan an upgrade to a supported Xperience by Kentico refresh and the corresponding supported .NET target framework version. Treat this as a scheduled upgrade project, not a hotfix — validate custom code, third-party integrations, and CI/CD repository compatibility before switching the target framework in production.
- **Reference:** https://docs.kentico.com/documentation/developers-and-admins/installation/support-policy
