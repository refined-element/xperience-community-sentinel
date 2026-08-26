# Performance checklist

These checks form the AI pass for the `performance` dimension. Run every check in this file against the target repository. A check fires a finding only when its "Pass when" criteria objectively fail — when the evidence is ambiguous, don't fire the finding; note the uncertainty in the report narrative instead (see the determinism rule in `grading.md`).

Unlike some checks in `content-model.md`, every check in this file is evaluable from the repository alone. Caching wiring, compression middleware, HTTP client configuration, async usage, warming/fallback coverage, and asset-pipeline setup are all things the source code either does or doesn't do — none of these checks need a live environment, a database connection, a runtime scan, or a measurement of actual response times to reach a verdict, so none of them carry the repo-only/environment-dependent split used elsewhere in this audit. Where a check's title reads like a runtime property (`AUD-PRF-009`'s "first-hit cost," for example), its "Pass when" criteria are written as a structural, static-reachability test instead — is the call reachable from a request path, and does the codebase contain a warming or fallback mechanism for it — not as a measured latency.

### AUD-PRF-001: Output caching is enabled for public pages with a preview-aware bypass

- **Inspect:** `Program.cs` for `AddOutputCache()`/`UseOutputCache()` registration and any custom cache policy (for example a `KenticoPreviewCachePolicy` or equivalent). Check what condition, if any, causes cached responses to be bypassed when the request is in Page Builder preview/edit mode.
- **Pass when:** output caching is registered and applied to public-facing pages, and the caching policy explicitly checks preview/edit-mode state (an `IWebsiteChannelContext.IsPreview`-style check, a query-string check for the preview/edit-mode parameter, or an equivalent condition) and skips or bypasses the cache when that state is true. A cache policy with no such check is a fail even if output caching is otherwise correctly wired up, because it serves stale or preview-only content to live visitors, or serves production output while an editor is trying to preview a draft.
- **Severity when failed:** Medium · **Effort:** M · **Fixable:** assisted
- **Remediation:** Add an explicit preview-mode check to the output cache policy (or to every `[OutputCache]` attribute usage), following Xperience's own documented pattern of conditioning cache eligibility on `IWebsiteChannelContext.IsPreview` — the developer adds this check; the framework doesn't apply it automatically. The one documented automatic exception is that the system disables output caching for pages containing a Form widget, and that exception doesn't extend to any other preview-mode scenario, so an explicit check is still required everywhere else.
- **Reference:** https://docs.kentico.com/documentation/developers-and-admins/development/caching/output-caching

### AUD-PRF-002: Content query results are cached with proper cache dependencies

- **Inspect:** Every repository/service method that calls a content-query API (`IContentQueryExecutor`, `IContentRetriever`, or equivalent) and is invoked on a request path serving public traffic. For each, check whether the call is wrapped in `IProgressiveCache.LoadAsync`/`Load` (or uses `IContentRetriever`'s built-in caching), and note the configured cache duration.
- **Pass when:** every content-query call reused across requests (not a one-off admin/debug endpoint) is cached — either explicitly through `IProgressiveCache`, or implicitly through an API that caches by default (`IContentRetriever`) without that caching having been disabled — the cache duration is a deliberate, finite value appropriate to how often the underlying content changes, not left at a default that's either too short to be worth caching or long enough to mask content updates for hours, and the cached entry carries a content-invalidating cache dependency (built from the actual items or content type the query touches, not a dependency that never expires or one keyed only on schema). See `AUD-ARC-003` in `architecture-config.md` for the detailed dependency-correctness criteria — don't fire both checks on the same evidence; this check adds the caching-coverage and duration angle on top of what `AUD-ARC-003` already covers for dependency correctness. A content query that runs on every request with no caching at all, on a path that serves meaningful traffic, is a fail.
- **Severity when failed:** Medium · **Effort:** M · **Fixable:** assisted
- **Remediation:** Wrap uncached content-query calls in `IProgressiveCache.LoadAsync(...)` — or switch to `IContentRetriever`, which caches automatically — with a cache duration matched to the content's actual update frequency and a cache dependency built against the items/content type the query touches (see `AUD-ARC-003`'s remediation for the `CacheDependencyBuilder` pattern), and confirm the wrapped call isn't accidentally caching data that should be preview-sensitive.
- **Reference:** https://docs.kentico.com/documentation/developers-and-admins/development/caching/data-caching

### AUD-PRF-003: Content queries select needed columns and bound linked-item depth

- **Inspect:** Every content-query call for its use of `Columns(...)` (or an equivalent explicit column projection) and `WithLinkedItems(depth)`/`TopN(...)`.
- **Pass when:** every content-query call that doesn't need every field on the content type explicitly limits the retrieved columns with `Columns(...)` rather than defaulting to every column on every selected content type, every call retrieving linked items passes an explicit, small depth rather than relying on an implicit or maximal default, and any query that could return an unbounded number of rows applies `TopN(...)` or equivalent pagination rather than fetching everything.
- **Severity when failed:** Medium · **Effort:** M · **Fixable:** assisted
- **Remediation:** Add explicit `Columns(...)` projections listing only the fields the view/template actually reads, set an explicit small value on every `WithLinkedItems` call, and add `TopN(...)` or paginated retrieval to any query whose result set size isn't already naturally bounded.
- **Reference:** https://docs.kentico.com/documentation/developers-and-admins/api/content-item-api/reference-content-item-query

### AUD-PRF-004: Response compression (Brotli/Gzip) is enabled

- **Inspect:** `Program.cs` for `AddResponseCompression()`/`UseResponseCompression()` registration, the configured compression providers, and where `UseResponseCompression()` sits relative to other response-generating middleware.
- **Pass when:** response compression is registered with both a Brotli and a Gzip provider, `UseResponseCompression()` is called before any middleware that generates the response body it needs to compress, and the compression covers the MIME types the site actually serves dynamically (HTML, CSS, JS, JSON, SVG) rather than being left at a default that excludes a type the site relies on.
- **Severity when failed:** Low · **Effort:** S · **Fixable:** auto
- **Remediation:** Register `AddResponseCompression()` with the Brotli and Gzip providers explicitly added, call `UseResponseCompression()` early in the pipeline — ahead of the Kentico middleware trio (`InitKentico`/`UseStaticFiles`/`UseKentico`), consistent with correct Xperience middleware ordering — and extend `ResponseCompressionOptions.MimeTypes` to include any additional response content types the site serves.
- **Reference:** https://learn.microsoft.com/en-us/aspnet/core/performance/response-compression

### AUD-PRF-005: Static assets ship with long-lived cache headers and bundled/minified CSS/JS

- **Inspect:** `StaticFileOptions.OnPrepareResponse` (or equivalent) for `Cache-Control` header configuration on static files, and the CSS/JS build pipeline (a bundler such as WebOptimizer, or an equivalent build step) for minification and bundling of first-party CSS/JS into a small number of requests.
- **Pass when:** static assets served through `UseStaticFiles()` (CSS, JS, images, fonts) carry a long-lived, immutable `Cache-Control` header (`public, max-age=<a large value>, immutable` or equivalent) rather than the framework's short-lived development-style default, and first-party CSS/JS is bundled and minified into a small number of requests rather than served as dozens of unminified individual files.
- **Severity when failed:** Low · **Effort:** S · **Fixable:** assisted
- **Remediation:** Set `Cache-Control: public, max-age=31536000, immutable` (or a similarly long value) in `StaticFileOptions.OnPrepareResponse` for hashed/versioned static assets, and add a bundling/minification step (WebOptimizer or equivalent) to the build or request pipeline for any CSS/JS still served as individual unminified files.
- **Reference:** https://learn.microsoft.com/en-us/aspnet/core/fundamentals/static-files

### AUD-PRF-006: Images are served in modern formats at appropriate sizes

- **Inspect:** Image assets referenced from templates and CMS content — their file format (AVIF/WebP versus JPEG/PNG) and whether the markup requests a size appropriate to where the image is displayed (a `<picture>`/`srcset` pattern, an image-variant/resize parameter, or a fixed-size asset matching its largest rendered dimension) rather than a single oversized original. For each image using a legacy format or no sizing mechanism, check whether it's documented as a known migration item — a code comment, a tracked backlog/TODO entry, or an entry in a migration-tracking doc.
- **Pass when:** every image reference in code and templates either uses a modern format (AVIF or WebP) or is served through an image-variant/resizing mechanism capable of transcoding to one, and requests a size appropriate to its actual rendered dimensions (through `srcset`/`sizes`, defined image variants, or equivalent) rather than always loading the full-resolution original. An image reference using a legacy format with no sizing mechanism is a fail unless it's documented as a tracked migration item (a code comment, backlog entry, or migration-tracking doc entry) — age alone is never the basis for a pass.
- **Severity when failed:** Low · **Effort:** M · **Fixable:** manual
- **Remediation:** Standardize image uploads on AVIF or WebP, falling back to JPEG/PNG only where a `<picture>` element provides an explicit fallback, and define image variants (or an equivalent resize pipeline) sized to each place the image is actually displayed, so pages stop shipping a full-resolution original to every viewport. Where converting an existing image immediately isn't practical, add a tracked note identifying it as a planned migration item.
- **Reference:** https://developer.chrome.com/docs/lighthouse/performance/uses-webp-images

### AUD-PRF-007: External HTTP calls have timeouts and retry policies; none block a hot path unbounded

- **Inspect:** Every named `HttpClient` registration (`AddHttpClient(...)`) and every direct external HTTP call in the codebase, for a configured timeout and retry/circuit-breaker policy (through `Microsoft.Extensions.Http.Resilience`, Polly, or an equivalent). Note whether any of these calls sit on a request path that a user-facing page waits on synchronously.
- **Pass when:** every `HttpClient` calling an external service has an explicit timeout (not left at the 100-second default) and a retry policy — or an explicit, documented reason it deliberately has none, for example a webhook forwarder that must not retry a non-idempotent call — and no user-facing request path makes an external HTTP call with no timeout at all, so a slow or hanging third party can't hang the request indefinitely.
- **Severity when failed:** High · **Effort:** M · **Fixable:** assisted
- **Remediation:** Add `AddStandardResilienceHandler()` (or a custom `AddResilienceHandler` pipeline with an explicit timeout and retry/circuit-breaker strategy) to every named `HttpClient` that calls an external service, sized to that service's expected latency, and move any long-running external call currently awaited synchronously on a request path into a background task or queued job instead.
- **Reference:** https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience

### AUD-PRF-008: Async is used end-to-end (no `.Result`/`.Wait()` on request paths)

- **Inspect:** Every controller action, service method, and repository call reachable from an HTTP request — search for `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`, or a synchronous overload of an API with an async equivalent (`ReadToEnd` instead of `ReadToEndAsync`, `HttpContext.Request.Form` instead of `ReadFormAsync`, and similar).
- **Pass when:** no code reachable from an HTTP request path blocks on a `Task` with `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`, and every async-capable I/O call on a request path (data access, content queries, HTTP calls, form/body reading) uses its asynchronous overload all the way up the call stack to the controller action. A synchronous call in a startup-only code path, executed once before the host starts serving requests, isn't a fail.
- **Severity when failed:** High · **Effort:** S · **Fixable:** auto
- **Remediation:** Replace the blocking call with `await` and make every method in its call chain, up to the controller action, `async Task`/`async Task<T>`, and replace synchronous I/O overloads (`Form`, `ReadToEnd`) with their asynchronous equivalents (`ReadFormAsync`, `ReadToEndAsync`).
- **Reference:** https://learn.microsoft.com/en-us/aspnet/core/fundamentals/best-practices

### AUD-PRF-009: Expensive lookups are warmed or memoized (first-hit cost is controlled)

- **Inspect:** Every external HTTP call and every unbounded or large content/catalog query reachable from a request path (a pricing API call, a full-catalog query with no `TopN`/pagination bound, a computed aggregate with no cached precomputed value). For each, check whether the codebase registers a `BackgroundService`/`IHostedService` that proactively refreshes the same data on an interval, or defines a static fallback value returned when the primary call or query hasn't completed or fails.
- **Pass when:** every external HTTP call or unbounded/large catalog query reachable from a request path is covered by at least one of: a registered `BackgroundService`/`IHostedService` that proactively refreshes the same data on an interval (so the request path only ever reads an already-populated cache), or a documented static fallback value the request path returns when the primary lookup hasn't completed or fails (so the request never depends solely on that lookup succeeding in time). A reachable external call or unbounded query with neither a warming service nor a fallback value defined anywhere in the codebase is a fail.
- **Severity when failed:** Low · **Effort:** M · **Fixable:** manual
- **Remediation:** Add a `BackgroundService` that proactively refreshes the data on an interval shorter than its cache lifetime, so requests read an already-warmed value, or add a static fallback value the request path can return immediately while a background refresh catches up, for every external call or unbounded catalog query reachable from a request path that currently has neither.
- **Reference:** https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services

### AUD-PRF-010: Fonts and third-party scripts load without render-blocking

- **Inspect:** `<link>` tags loading web fonts (Google Fonts or a similar external font service) in the shared layout, and `<script>` tags loading third-party JavaScript (analytics, widgets, embeds) anywhere on the site — whether each uses a non-blocking loading technique (`preload`+`onload` for font stylesheets, `async`/`defer` for scripts) or loads as a plain blocking `<link rel="stylesheet">`/`<script>` in the document `<head>`.
- **Pass when:** every external font stylesheet loads through a non-blocking pattern (a `preload`/`onload` swap, or an equivalent technique) with a `<noscript>` fallback for clients with JavaScript disabled, and every third-party `<script>` tag carries `async` or `defer` (or is injected after page load) rather than loading as a plain blocking script in the document `<head>`. A first-party script the page's initial render genuinely depends on isn't required to be deferred.
- **Severity when failed:** Low · **Effort:** S · **Fixable:** assisted
- **Remediation:** Load external font stylesheets with the `preload`/`onload` pattern (`<link rel="preload" as="style" onload="this.rel='stylesheet'">` plus a `<noscript>` fallback), and add `async` or `defer` to every third-party `<script>` tag that isn't required for first paint.
- **Reference:** https://web.dev/articles/efficiently-load-third-party-javascript
