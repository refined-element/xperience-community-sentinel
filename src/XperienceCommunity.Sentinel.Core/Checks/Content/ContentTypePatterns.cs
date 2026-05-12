namespace XperienceCommunity.Sentinel.Checks.Content;

/// <summary>
/// Class-name patterns that let us split the "stale unused content hub items" finding into
/// images / documents / generic content. Used by CNT002, CNT010, and CNT011 to share the same
/// join shape with mutually-exclusive <c>LIKE</c> filters.
///
/// <para>
/// The detection is intentionally a heuristic on class name — the admin-hub "Content types"
/// in Xperience by Kentico don't carry a machine-readable "this is an image" flag, and
/// inspecting each type's form schema for an asset field would require joining extra metadata
/// tables at scan time. Pattern-match on the display/class name catches the naming convention
/// most installs follow (ReXBK.Image, Site.Document, CMS.File, etc.) at the cost of missing
/// any team that named their image content type "Visual" or "Graphic". Cheap to tune per
/// install via the editable-settings excluded-checks override if a particular rule is noisy.
/// </para>
/// </summary>
internal static class ContentTypePatterns
{
    /// <summary>
    /// SQL fragments (pre-parameterized) that match image-shaped content types. The check's SQL
    /// concatenates these into a single parenthesized OR block in the WHERE clause — keeps the
    /// query plan flat and SQL Server parses it as a simple LIKE tree.
    /// </summary>
    public static readonly string[] ImageLikeClauses =
    [
        "c.ClassName LIKE N'%image%'",
        "c.ClassName LIKE N'%photo%'",
        "c.ClassName LIKE N'%picture%'",
        "c.ClassName LIKE N'%thumbnail%'",
        "c.ClassDisplayName LIKE N'%image%'",
        "c.ClassDisplayName LIKE N'%photo%'",
    ];

    /// <summary>
    /// SQL fragments for file / document / generic-media content types. Distinct from
    /// <see cref="ImageLikeClauses"/> so a content type never matches both buckets.
    /// </summary>
    public static readonly string[] FileLikeClauses =
    [
        "c.ClassName LIKE N'%file%'",
        "c.ClassName LIKE N'%document%'",
        "c.ClassName LIKE N'%pdf%'",
        "c.ClassName LIKE N'%attachment%'",
        "c.ClassName LIKE N'%media%'",
        "c.ClassDisplayName LIKE N'%document%'",
        "c.ClassDisplayName LIKE N'%file%'",
    ];

    /// <summary>
    /// OR-joined subclause that CNT010 uses to INCLUDE image-shaped content types.
    /// </summary>
    public static string ImageMatchClause => "(" + string.Join(" OR ", ImageLikeClauses) + ")";

    /// <summary>
    /// OR-joined subclause that CNT011 uses to INCLUDE file/document-shaped content types.
    /// </summary>
    public static string FileMatchClause => "(" + string.Join(" OR ", FileLikeClauses) + ")";

    /// <summary>
    /// Inverted — the clause CNT002 uses to EXCLUDE anything that would fire under CNT010 or
    /// CNT011. Ensures the three rules are mutually exclusive (an item never fires twice).
    /// </summary>
    public static string GenericExclusionClause => "NOT " + ImageMatchClause + " AND NOT " + FileMatchClause;
}
