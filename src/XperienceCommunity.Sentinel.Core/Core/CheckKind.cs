namespace XperienceCommunity.Sentinel.Core;

public enum CheckKind
{
    /// <summary>Runs against the source tree only. Safe without a database connection.</summary>
    Static,

    /// <summary>Requires a live connection string or Management API credentials.</summary>
    Runtime,
}
