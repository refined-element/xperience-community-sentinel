# Performance checklist

These checks form the AI pass for the `performance` dimension. Run every check in this file against the target repository. A check fires a finding only when its "Pass when" criteria objectively fail — when the evidence is ambiguous, don't fire the finding; note the uncertainty in the report narrative instead (see the determinism rule in `grading.md`).

Unlike some checks in `content-model.md`, every check in this file is evaluable from the repository alone. Caching wiring, compression middleware, HTTP client configuration, async usage, and asset-pipeline setup are all things the source code either does or doesn't do — none of these checks need a live environment, a database connection, or a runtime scan to reach a verdict, so none of them carry the repo-only/environment-dependent split used elsewhere in this audit.

### AUD-PRF-001: Output caching is enabled for public pages with a preview-aware bypass

- **Inspect:** `Program.cs` for `AddOutputCache()`/`UseOutputCache()` registration and any custom cache policy (for example a `KenticoPreviewCachePolicy` or equivalent). Check what condition, if any, causes cached responses to be bypassed when the request is in Page Builder preview/edit mode.
- **Pass when:** output caching is registered and applied to public-facing pages, and the caching policy explicitly checks preview/edit-mode state (an `IWebsiteChannelContext.IsPreview`-style check, a query-string check for the preview/edit-mode parameter, or an equivalent condition) and skips or bypasses the cache when that state is true. A cache policy with no such check is a fail even if output caching is otherwise correctly wired up, because it serves stale or preview-only content to live visitors, or serves production output while an editor is trying to preview a draft.
- **Severity when failed:** Medium · **Effort:** M · **Fixable:** assisted
- **Remediation:** Add an explicit preview-mode check to the output cache policy (or to every `[OutputCache]` attribute usage) that disables caching for the current request when the site is in preview/edit mode, following the same pattern Xperience's own MVC integration uses to skip caching automatically for preview requests.
- **Reference:** https://docs.kentico.com/documentation/developers-and-admins/development/caching/output-caching

### AUD-PRF-002: Content query results are cached with proper cache dependencies

- **Inspect:** Every repository/service method that calls a content-query API (`IContentQueryExecutor`, `IContentRetriever`, or equivalent) and is invoked on a request path serving public traffic. For each, check whether the call is wrapped in `IProgressiveCache.LoadAsync`/`Load` (or uses `IContentRetriever`'s built-in caching), and note the configured cache duration.
- **Pass when:** every content-query call reused across requests (not a one-off admin/debug endpoint) is cached — either explicitly through `IProgressiveCache`, or implicitly through an API that caches by default (`IContentRetriever`) without that caching having been disabled — and the cache duration is a deliberate, finite value appropriate to how often the underlying content changes, not left at a default that's either too short to be worth caching or long enough to mask content updates for hours. A content query that runs on every request with no caching at all, on a path that serves meaningful traffic, is a fail.
- **Severity when failed:** Medium · **Effort:** M · **Fixable:** assisted
- **Remediation:** Wrap uncached content-query calls in `IProgressiveCache.LoadAsync(...)` — or switch to `IContentRetriever`, which caches automatically — with a cache duration matched to the content's actual update frequency, and confirm the wrapped call isn't accidentally caching data that should be preview-sensitive.
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
- **Remediation:** Register `AddResponseCompression()` with the Brotli and Gzip providers explicitly added, call `UseResponseCompression()` early in the pipeline (before the Kentico trio, per this project's own middleware-order convention), and extend `ResponseCompressionOptions.MimeTypes` to include any additional response content types the site serves.
- **Reference:** https://learn.microsoft.com/en-us/aspnet/core/performance/response-compression

### AUD-PRF-005: Static assets ship with long-lived cache headers and bundled/minified CSS/JS

- **Inspect:** `StaticFileOptions.OnPrepareResponse` (or equivalent) for `Cache-Control` header configuration on static files, and the CSS/JS build pipeline (a bundler such as WebOptimizer, or an equivalent build step) for minification and bundling of first-party CSS/JS into a small number of requests.
- **Pass when:** static assets served through `UseStaticFiles()` (CSS, JS, images, fonts) carry a long-lived, immutable `Cache-Control` header (`public, max-age=<a large value>, immutable` or equivalent) rather than the framework's short-lived development-style default, and first-party CSS/JS is bundled and minified into a small number of requests rather than served as dozens of unminified individual files.
- **Severity when failed:** Low · **Effort:** S · **Fixable:** assisted
- **Remediation:** Set `Cache-Control: public, max-age=31536000, immutable` (or a similarly long value) in `StaticFileOptions.OnPrepareResponse` for hashed/versioned static assets, and add a bundling/minification step (WebOptimizer or equivalent) to the build or request pipeline for any CSS/JS still served as individual unminified files.
- **Reference:** https://learn.microsoft.com/en-us/aspnet/core/fundamentals/static-files

### AUD-PRF-006: Images are served in modern formats at appropriate sizes

- **Inspect:** Image assets referenced from templates and CMS content — their file format (AVIF/WebP versus JPEG/PNG) and whether the markup requests a size appropriate to where the image is displayed (a `<picture>`/`srcset` pattern, an image-variant/resize parameter, or a fixed-size asset matching its largest rendered dimension) rather than a single oversized original.
- **Pass when:** images uploaded for new content default to a modern format (AVIF or WebP) or are served through an image-variant/resizing mechanism that can transcode to one, and templates request an image sized for its actual rendered dimensions (through `srcset`/`sizes`, defined image variants, or equivalent) rather than always loading the full-resolution original. A legacy image asset that predates this convention and hasn't been re-touched isn't itself a fail; a newly added image that ignores the convention is.
- **Severity when failed:** Low · **Effort:** M · **Fixable:** manual
- **Remediation:** Standardize new image uploads on AVIF or WebP, falling back to JPEG/PNG only where a `<picture>` element provides an explicit fallback, and define image variants (or an equivalent resize pipeline) sized to each place the image is actually displayed, so pages stop shipping a full-resolution original to every viewport.
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

- **Inspect:** Every dependency whose first call is meaningfully slow (an external pricing API, a large content-catalog query, a computed aggregate) for whether it's proactively refreshed by a background service (`BackgroundService`/`IHostedService`) ahead of user traffic, or memoized after its first successful call, versus being recomputed cold on every cache expiry with no warming.
- **Pass when:** every dependency identified as expensive on first call is either warmed proactively by a registered `BackgroundService`/`IHostedService` running on an interval shorter than its cache expiration, so a request never pays the cold cost itself, or has a documented fallback value returned instead of blocking a request while the expensive lookup completes. A page whose response time depends on a slow first-hit external call with no warming and no fallback is a fail.
- **Severity when failed:** Low · **Effort:** M · **Fixable:** manual
- **Remediation:** Add a `BackgroundService` that proactively refreshes the expensive value on an interval shorter than its cache lifetime, mirroring this project's own cache-warming pattern for its price lookup and catalog data, or add a static fallback value the request path can return immediately while a background refresh catches up.
- **Reference:** https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services

### AUD-PRF-010: Fonts and third-party scripts load without render-blocking

- **Inspect:** `<link>` tags loading web fonts (Google Fonts or a similar external font service) in the shared layout, and `<script>` tags loading third-party JavaScript (analytics, widgets, embeds) anywhere on the site — whether each uses a non-blocking loading technique (`preload`+`onload` for font stylesheets, `async`/`defer` for scripts) or loads as a plain blocking `<link rel="stylesheet">`/`<script>` in the document `<head>`.
- **Pass when:** every external font stylesheet loads through a non-blocking pattern (a `preload`/`onload` swap, or an equivalent technique) with a `<noscript>` fallback for clients with JavaScript disabled, and every third-party `<script>` tag carries `async` or `defer` (or is injected after page load) rather than loading as a plain blocking script in the document `<head>`. A first-party script the page's initial render genuinely depends on isn't required to be deferred.
- **Severity when failed:** Low · **Effort:** S · **Fixable:** assisted
- **Remediation:** Load external font stylesheets with the `preload`/`onload` pattern (`<link rel="preload" as="style" onload="this.rel='stylesheet'">` plus a `<noscript>` fallback), and add `async` or `defer` to every third-party `<script>` tag that isn't required for first paint.
- **Reference:** https://web.dev/articles/efficiently-load-third-party-javascript
