namespace XperienceCommunity.Sentinel.Module.Configuration;

/// <summary>
/// Strongly-typed options bound from <c>appsettings.json</c> section <c>Sentinel</c>.
/// Every value has a sensible default so the minimal configuration — just calling
/// <c>AddSentinel()</c> — produces a working install.
/// </summary>
public sealed class SentinelOptions
{
    public const string SectionName = "Sentinel";

    /// <summary>
    /// Master kill-switch. Set to <c>false</c> to suspend all scan execution without removing the
    /// package. Honored by <see cref="Services.SentinelScanService"/> — a disabled scan returns
    /// early and the scheduled task reports "Skipped (disabled)" to the admin.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public ChecksOptions Checks { get; set; } = new();
    public RuntimeCheckOptions RuntimeChecks { get; set; } = new();
    public EmailDigestOptions EmailDigest { get; set; } = new();
    public EventLogOptions EventLogIntegration { get; set; } = new();
    public ContactOptions Contact { get; set; } = new();
    public RetentionOptions Retention { get; set; } = new();

    // The run cadence lives in Kentico's Scheduled Tasks app (kentico-admin/applications/
    // scheduled-tasks). The Sentinel task is discovered on startup via the
    // [RegisterScheduledTask] attribute on SentinelScanTask — the module installer handles
    // Sentinel's own data-class tables but does not touch the scheduled-task row. No custom
    // cron option here; admins edit the interval in-situ with the Kentico UI they already know.

    public sealed class ChecksOptions
    {
        /// <summary>Rule IDs to skip, e.g. ["DEP001"].</summary>
        public List<string> Excluded { get; set; } = [];
    }

    public sealed class RuntimeCheckOptions
    {
        /// <summary>Connection string for runtime checks. Empty = fall back to Kentico's CMSConnectionString.</summary>
        public string ConnectionString { get; set; } = string.Empty;
        public int StaleDays { get; set; } = 180;
        public int EventLogDays { get; set; } = 30;
    }

    public sealed class EmailDigestOptions
    {
        public bool Enabled { get; set; } = true;
        public List<string> Recipients { get; set; } = [];

        /// <summary>
        /// Skip the digest when no findings in the current scan meet <see cref="SeverityThreshold"/>.
        /// Does NOT compare against the previous scan; a proper "only when new" mode driven by
        /// fingerprint diff is on the roadmap (see plan doc).
        /// </summary>
        public bool OnlyWhenThresholdFindings { get; set; } = true;

        /// <summary>"Error" | "Warning" | "Info" — findings below this don't trigger the digest.</summary>
        public string SeverityThreshold { get; set; } = "Warning";
    }

    public sealed class EventLogOptions
    {
        public bool Enabled { get; set; } = true;
        public string SeverityThreshold { get; set; } = "Warning";

        /// <summary>
        /// Maximum number of per-finding entries written to <c>CMS_EventLog</c> in a single scan.
        /// A 1000-finding scan would otherwise balloon the event log and slow the admin app that
        /// pages over it. When exceeded, the writer emits one "N more suppressed" summary instead
        /// of the remaining entries. The per-scan summary entry is always written regardless.
        /// </summary>
        public int MaxEntriesPerScan { get; set; } = 50;
    }

    public sealed class RetentionOptions
    {
        /// <summary>
        /// Keep only the N most recent scan runs; older runs (and every
        /// <c>XperienceCommunity_SentinelFinding</c> row that FK's to them) are trimmed after each
        /// successful scan. Default 100 — at a daily cadence that's ~3 months of history, which
        /// is enough for week-over-week / month-over-month diffs without unbounded growth
        /// (730 rows + 50k-500k findings for a 2-year-old install otherwise).
        /// <para>
        /// Ack rows (<c>XperienceCommunity_SentinelFindingAck</c>) are NEVER pruned here — acks are
        /// keyed by fingerprint and survive scan regeneration by design. Trimming them would
        /// unsuppress findings that operators have already triaged.
        /// </para>
        /// <para>
        /// Set to <c>0</c> or a negative value to disable trimming entirely (keeps forever).
        /// </para>
        /// </summary>
        public int MaxScanRunsToKeep { get; set; } = 100;
    }

    public sealed class ContactOptions
    {
        /// <summary>
        /// Endpoint that receives the quote-submission POST. Blank = fall back to the hard-coded
        /// default (<c>https://kentico-developer.com/api/scanner/submit</c>). Override when
        /// pointing at a staging KDaaS or testing the round-trip locally with a fake.
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// Default for sanitizer's <c>includeContext</c> toggle — when the admin UI submits a
        /// quote for a scan, whether to include the full finding messages / locations in the
        /// payload. False by default because finding text can embed site-specific paths or
        /// class names that some operators prefer to keep internal.
        /// </summary>
        public bool IncludeContextByDefault { get; set; }
    }
}
