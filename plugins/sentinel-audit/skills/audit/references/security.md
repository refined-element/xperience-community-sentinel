# Security checklist

These checks form the AI pass for the `security` dimension. Run every check in this file against the target repository. A check fires a finding only when its "Pass when" criteria objectively fail — when the evidence is ambiguous, don't fire the finding; note the uncertainty in the report narrative instead (see the determinism rule in `grading.md`).

Some checks here inspect the same surface area as one of Sentinel's own static `CFG` rules (plaintext secrets, hash salt). That overlap is intentional — see the note at the top of `architecture-config.md` for why.

### AUD-SEC-001: No plaintext secrets in tracked files (judgment pass beyond Sentinel's pattern match)

- **Inspect:** Every tracked file that can hold configuration or secrets, not only the repo-root `appsettings.json` that Sentinel's own plaintext-secrets rule inspects: `appsettings.*.json` in any project folder, `launchSettings.json`, `docker-compose*.yml`, `.env` files, publish profiles (`*.pubxml`), and CI/CD pipeline definitions (GitHub Actions YAML, Azure Pipelines YAML). Also re-examine values in `appsettings.json` that Sentinel's key-name heuristic wouldn't flag — a value that looks like a live credential by shape (a `sk_live_` / `whsec_` prefix, a long base64 or hex string, a connection string with an embedded password) assigned to a key whose name doesn't contain "password", "secret", "key", or "token".
- **Pass when:** no file in this broader surface contains a live-looking secret value. Placeholder values (empty strings, `CHANGEME`, obviously fake sample keys clearly marked as such in a code comment), Key Vault references (`@Microsoft.KeyVault(...)`), and environment-variable interpolation syntax (`${VAR}`, `$(VAR)`) all count as a pass.
- **Severity when failed:** Critical · **Effort:** S · **Fixable:** assisted
- **Remediation:** Remove the value from the tracked file, rotate the credential (assume it's compromised the moment it's committed, even if the commit is later removed from the branch tip), and move the real value to user secrets (development) or the hosting platform's secret store (staging/production).
- **Reference:** https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets

### AUD-SEC-002: `CMSHashStringSalt` is configured and non-empty in every environment

- **Inspect:** The `CMSHashStringSalt` key across every `appsettings*.json` file, and — where accessible — the actual configured value in each deployed environment (Azure App Settings, Key Vault, or equivalent), not just whether the key exists in source.
- **Pass when:** `CMSHashStringSalt` resolves to a non-empty, sufficiently random value (not a short or guessable string) in every environment the project runs in, even though the value committed to source control is empty by design (see AUD-ARC-004). A missing key, or a key present but empty at runtime with no override supplied anywhere in the configuration chain, is a fail — the application would either fail to start or silently use a weak/default salt.
- **Severity when failed:** High · **Effort:** S · **Fixable:** assisted
- **Remediation:** Confirm a real, randomly generated value (a GUID is sufficient) is set using `dotnet user-secrets` locally and through the hosting platform's app settings or Key Vault in every deployed environment, and keep the same value across all instances that need to validate each other's macro/preview signatures.
- **Reference:** https://docs.kentico.com/developers-and-admins/configuration/reference-configuration-keys

### AUD-SEC-003: Admin surface is protected (no default accounts, admin path not exposed unauthenticated)

- **Inspect:** The Xperience administration application's authentication configuration, the set of user accounts and their assigned roles (where the CMS database is reachable for the audit), and any reverse-proxy/hosting configuration that might route the admin path (`/admin` or the project's configured administration path) without authentication in front of it.
- **Pass when:** no account uses a well-known default username with a default or blank password, the built-in Administrator role is assigned only to accounts that genuinely need full system access (not a shared or service account used for everyday editing), and the administration path requires authentication at every layer that fronts it (application, reverse proxy, CDN) — none of them pass requests through anonymously.
- **Severity when failed:** Critical · **Effort:** M · **Fixable:** manual
- **Remediation:** Remove or disable default/shared administrator accounts, assign editors and content managers to restricted roles (user manager, channel manager, content editor) instead of Administrator, and confirm every layer in front of the admin path enforces authentication — don't rely on obscurity of the path alone.
- **Reference:** https://docs.kentico.com/documentation/developers-and-admins/security-guidelines

### AUD-SEC-004: HTTPS is enforced with HSTS outside development

- **Inspect:** `Program.cs` for `UseHttpsRedirection()` and `UseHsts()` calls, and the environment guard around `UseHsts()`.
- **Pass when:** `UseHttpsRedirection()` runs in every environment, and `UseHsts()` runs in every non-development environment (guarded by `!app.Environment.IsDevelopment()` or an equivalent check) — not called unconditionally (which breaks local HTTP development) and not omitted entirely (which leaves production without the `Strict-Transport-Security` header).
- **Severity when failed:** High · **Effort:** S · **Fixable:** auto
- **Remediation:** Add `if (!app.Environment.IsDevelopment()) { app.UseHsts(); }` alongside the existing `app.UseHttpsRedirection()` call, placed before the Kentico middleware trio or wherever the project's existing exception-handling middleware sits. Start with a short `HstsOptions.MaxAge` (hours, not the eventual one-year value) the first time HSTS is enabled in production, in case the HTTPS configuration needs to be rolled back.
- **Reference:** https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl

### AUD-SEC-005: Security headers are set (CSP, X-Content-Type-Options, X-Frame-Options, Referrer-Policy)

- **Inspect:** The middleware (custom or from a package) that sets response headers on every request, typically registered early in `Program.cs`.
- **Pass when:** every response carries `X-Content-Type-Options: nosniff`, `X-Frame-Options` (`DENY` or `SAMEORIGIN`), a `Referrer-Policy` value that isn't `unsafe-url` or absent, and a `Content-Security-Policy` header that at minimum restricts `script-src` and `object-src` (a CSP that's present but set to `default-src *` with no further restriction is a fail — it provides no real protection).
- **Severity when failed:** Medium · **Effort:** S · **Fixable:** auto
- **Remediation:** Add or extend the header-setting middleware to include the missing header(s) with a restrictive value, and verify the CSP doesn't block legitimate first-party scripts/styles/fonts the site actually needs (test in a report-only mode first if the project has no existing CSP).
- **Reference:** https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html

### AUD-SEC-006: `Html.Raw` on CMS or user content is sanitized

- **Inspect:** Every `Html.Raw(...)` call (and equivalent unescaped-output patterns such as `@Html.Raw` in Razor or a custom "trusted HTML" helper) across views and components. For each, trace where the string value originates — a rich-text CMS field, a user-submitted form field, or a hardcoded/compile-time constant.
- **Pass when:** every `Html.Raw` call whose source value can contain content from a CMS rich-text field, a user submission, or any other non-compile-time-constant source passes through an HTML sanitizer (such as `HtmlSanitizer`) before reaching `Html.Raw`. A call whose input is a hardcoded string literal or a value built entirely from trusted, non-user-influenced application data is not required to be sanitized.
- **Severity when failed:** High · **Effort:** M · **Fixable:** assisted
- **Remediation:** Pass the value through a sanitizer configured to allow only the HTML the rendering context actually needs (semantic tags, `class`, safe `style` properties) before calling `Html.Raw`, and cover the sanitizer configuration with a test asserting that a `<script>` payload is stripped.
- **Reference:** https://cheatsheetseries.owasp.org/cheatsheets/Cross_Site_Scripting_Prevention_Cheat_Sheet.html

### AUD-SEC-007: State-changing endpoints validate antiforgery tokens

- **Inspect:** Every controller action or minimal-API endpoint that accepts `POST`, `PUT`, `PATCH`, or `DELETE` and changes server-side state (form submissions, cart/checkout actions, admin actions). Check whether it's covered by `[ValidateAntiForgeryToken]`, `[AutoValidateAntiforgeryToken]` applied at the controller or global level, or the framework's automatic CSRF protection (where the ASP.NET Core version in use provides it by default), and whether an `[IgnoreAntiforgeryToken]` override is justified.
- **Pass when:** every browser-facing, cookie-authenticated, state-changing endpoint is covered by antiforgery validation through one of the mechanisms above. An endpoint intentionally exempted (a webhook receiver validated by an HMAC signature instead, or a machine-to-machine API authenticated by an API key rather than a cookie) is a pass as long as the exemption is deliberate and the endpoint has its own independent authentication.
- **Severity when failed:** High · **Effort:** M · **Fixable:** assisted
- **Remediation:** Add `[ValidateAntiForgeryToken]` to the action (or `[AutoValidateAntiforgeryToken]` at the controller/global level) and ensure the form or fetch call submits the token the framework's tag helpers generate. For endpoints that can't use cookie-based antiforgery tokens (pure JSON APIs consumed by non-browser clients), authenticate them by another means instead and document why antiforgery validation doesn't apply.
- **Reference:** https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery

### AUD-SEC-008: Custom API endpoints authenticate with constant-time comparison and are rate limited

- **Inspect:** Every custom (non-Kentico-built-in) API endpoint that compares a caller-supplied credential (an API key, a shared admin secret, a webhook signature) against a configured value, and every custom public-facing endpoint's rate-limiting policy assignment.
- **Pass when:** every credential comparison uses a constant-time comparison (`CryptographicOperations.FixedTimeEquals` over UTF-8 byte spans, or an equivalent fixed-time HMAC comparison), not a plain `==`, `.Equals()`, or `string.Compare` — those short-circuit on the first differing byte and leak timing information an attacker can use to guess the secret byte-by-byte. Separately, every custom public-facing endpoint that accepts unauthenticated or lightly-authenticated traffic (contact forms, checkout initiation, community submissions, AI-agent-facing APIs) is covered by a rate-limiting policy with a defined limit and window, and 429 responses include a `Retry-After` header.
- **Severity when failed:** High · **Effort:** M · **Fixable:** assisted
- **Remediation:** Replace direct string/byte comparisons of secrets with `CryptographicOperations.FixedTimeEquals`, and add the endpoint to an ASP.NET Core rate limiter policy (`AddRateLimiter`/`UseRateLimiter`, registered before `UseKentico()` per Xperience by Kentico's own rate-limiting guidance) sized to the endpoint's expected legitimate traffic. Xperience by Kentico only rate-limits its own administration sign-in/password-reset endpoints by default — every other endpoint is the project's responsibility.
- **Reference:** https://docs.kentico.com/documentation/developers-and-admins/security-guidelines/rate-limiting

### AUD-SEC-009: File uploads validate extension, content type, magic bytes, and size

- **Inspect:** Every endpoint that accepts a file upload (design submissions, image uploads, document attachments). For each, check what validation runs before the file is persisted or processed: file extension allowlist, `Content-Type` header check, file-signature (magic-byte) check, dimension/size limits, and where the validated file is written.
- **Pass when:** every upload endpoint checks the file extension against an explicit allowlist, checks the actual file content's magic bytes match the claimed type (not just the `Content-Type` header, which a caller can forge), and enforces a maximum file size (through `[RequestSizeLimit]` or an equivalent attribute) before the file is fully buffered or persisted. Storing the file under a server-generated name (not the client-supplied filename) and outside any directory with execute permissions is also required to pass.
- **Severity when failed:** High · **Effort:** M · **Fixable:** assisted
- **Remediation:** Add extension and `Content-Type` allowlist checks, verify the file's magic bytes against the claimed format, enforce a size limit before buffering the full upload, and generate a safe server-side filename rather than trusting the client-supplied one.
- **Reference:** https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads

### AUD-SEC-010: Dependencies carry no known CVEs at the pinned versions (interpret Sentinel VER/DEP findings)

- **Inspect:** Sentinel's `VER`/`DEP` findings from the deterministic scan, plus the output of `dotnet list package --vulnerable --include-transitive` (or `dotnet restore` with NuGet audit enabled) run against the target project.
- **Pass when:** no direct or transitive package resolves to a version NuGet's audit (backed by the GitHub Advisory Database) or Sentinel's `VER`/`DEP` rules flag as having a known vulnerability at `moderate` severity or above. A `low`-severity advisory with a documented mitigating factor (the vulnerable code path isn't reachable in this project's usage) may be noted rather than treated as a hard fail — don't fire the finding if you can't confirm reachability either way; note the uncertainty instead.
- **Severity when failed:** High · **Effort:** S · **Fixable:** auto
- **Remediation:** Update the flagged package to the first version without the known vulnerability (`dotnet package update --vulnerable`), or update the closest direct dependency that pulls in a vulnerable transitive package. If no fixed version exists yet, document the mitigating factor and track the advisory for a follow-up update.
- **Reference:** https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages
