namespace XperienceCommunity.Sentinel.Core;

/// <summary>
/// Inputs made available to every check during a scan. Immutable after construction.
/// </summary>
public sealed record ScanContext
{
    /// <summary>Absolute path to the Xperience by Kentico project root (folder containing the .csproj).</summary>
    public required string RepoPath { get; init; }

    /// <summary>SQL Server connection string. When null, runtime checks are skipped.</summary>
    public string? ConnectionString { get; init; }

    /// <summary>Stale-content threshold in days. Items not edited in this window are flagged by CNT003.</summary>
    public int StaleDays { get; init; } = 180;

    /// <summary>EventLog lookback window in days. CNT006 groups errors/warnings from CMS_EventLog within this period.</summary>
    public int EventLogDays { get; init; } = 30;

    /// <summary>HTTP client factory for checks that reach out to external services (NuGet feed, Kentico release feed).</summary>
    public required IHttpClientFactory HttpClientFactory { get; init; }

    /// <summary>
    /// True when the scan is running embedded inside a deployed Xperience by Kentico site (via
    /// <c>XperienceCommunity.Sentinel.Module</c>'s scheduled task) rather than from the standalone CLI
    /// against a source repo. Source-file checks (CFG002, DEP001, VER001) skip themselves entirely when
    /// this is true because a deployed site has only compiled DLLs — no <c>Program.cs</c>, no <c>.csproj</c>.
    /// Emitting "source file not found" findings in that environment is technically accurate but alarms
    /// non-technical operators reading the admin dashboard. Defaults to <c>false</c> so the CLI keeps
    /// behaving exactly as before.
    /// </summary>
    public bool IsEmbeddedHost { get; init; }

    /// <summary>True when a connection string is present and runtime checks should execute.</summary>
    public bool RuntimeEnabled => !string.IsNullOrWhiteSpace(ConnectionString);
}
