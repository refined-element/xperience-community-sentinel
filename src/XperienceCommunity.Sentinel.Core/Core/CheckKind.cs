namespace XperienceCommunity.Sentinel.Core;

public enum CheckKind
{
    /// <summary>Runs against the source tree only. Safe without a database connection.</summary>
    Static,

    /// <summary>
    /// Requires a live database connection string (<see cref="ScanContext.ConnectionString"/>);
    /// skipped when none is configured — see <see cref="ScanContext.RuntimeEnabled"/>.
    /// </summary>
    Runtime,
}
