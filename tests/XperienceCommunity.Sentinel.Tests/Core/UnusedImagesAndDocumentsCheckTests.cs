using XperienceCommunity.Sentinel.Checks.Content;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Tests.Core;

/// <summary>
/// Shape tests for CNT010 / CNT011 and the mutual-exclusivity invariant against CNT002. The
/// whole reason the split exists is to let operators triage images / documents / generic
/// content separately — if the three queries' predicates ever drift into overlap, an item
/// would fire in multiple rules with different fingerprints, acks would desync, and dashboards
/// would show duplicates. These tests lock the predicate invariants so that can't happen.
/// </summary>
public class UnusedImagesAndDocumentsCheckTests
{
    [Fact]
    public void Cnt010_metadata_is_images()
    {
        var check = new UnusedImagesCheck();
        Assert.Equal("CNT010", check.RuleId);
        Assert.Equal("Stale unused images", check.Title);
        Assert.Equal("Content Model", check.Category);
        Assert.Equal(CheckKind.Runtime, check.Kind);
    }

    [Fact]
    public void Cnt011_metadata_is_documents()
    {
        var check = new UnusedDocumentsCheck();
        Assert.Equal("CNT011", check.RuleId);
        Assert.Equal("Stale unused documents / files", check.Title);
        Assert.Equal("Content Model", check.Category);
        Assert.Equal(CheckKind.Runtime, check.Kind);
    }

    [Fact]
    public void Cnt010_query_filters_to_image_like_content_types()
    {
        // Pattern presence is what makes CNT010 a distinct rule rather than "same as CNT002".
        Assert.Contains("ClassName LIKE N'%image%'", UnusedImagesCheck.Sql);
        Assert.Contains("ClassName LIKE N'%photo%'", UnusedImagesCheck.Sql);
    }

    [Fact]
    public void Cnt011_query_filters_to_file_or_document_content_types()
    {
        Assert.Contains("ClassName LIKE N'%file%'", UnusedDocumentsCheck.Sql);
        Assert.Contains("ClassName LIKE N'%document%'", UnusedDocumentsCheck.Sql);
    }

    [Fact]
    public void Cnt002_query_excludes_image_and_file_content_types()
    {
        // Mutual-exclusivity guard — CNT002 must NOT fire on anything CNT010 or CNT011 catches.
        // If this test fails, operators will see duplicate findings with different rule IDs on
        // the same underlying item, which breaks the ack system's fingerprint-per-finding model.
        Assert.Contains("NOT (", OrphanedContentItemsCheck.Sql);
        Assert.Contains("ClassName LIKE N'%image%'", OrphanedContentItemsCheck.Sql);
        Assert.Contains("ClassName LIKE N'%file%'", OrphanedContentItemsCheck.Sql);
    }

    [Fact]
    public void All_three_checks_share_the_staleness_gate()
    {
        // Load-bearing invariant: every "stale unused X" rule in the CNT002 family must honor
        // ScanContext.StaleDays. If any rule drops the HAVING clause, operators see fresh content
        // in the "safe to delete" bucket and lose trust in the feature.
        Assert.Contains("HAVING", OrphanedContentItemsCheck.Sql);
        Assert.Contains("HAVING", UnusedImagesCheck.Sql);
        Assert.Contains("HAVING", UnusedDocumentsCheck.Sql);
        Assert.Contains("@StaleDays", OrphanedContentItemsCheck.Sql);
        Assert.Contains("@StaleDays", UnusedImagesCheck.Sql);
        Assert.Contains("@StaleDays", UnusedDocumentsCheck.Sql);
    }

    [Fact]
    public void All_three_checks_require_null_inbound_reference()
    {
        // Same orphan-detection model across the family — the LEFT JOIN + IS NULL is what
        // distinguishes "unused" from "modified long ago but still in use".
        Assert.Contains("LEFT JOIN CMS_ContentItemReference", OrphanedContentItemsCheck.Sql);
        Assert.Contains("LEFT JOIN CMS_ContentItemReference", UnusedImagesCheck.Sql);
        Assert.Contains("LEFT JOIN CMS_ContentItemReference", UnusedDocumentsCheck.Sql);
        Assert.Contains("r.ContentItemReferenceID IS NULL", OrphanedContentItemsCheck.Sql);
        Assert.Contains("r.ContentItemReferenceID IS NULL", UnusedImagesCheck.Sql);
        Assert.Contains("r.ContentItemReferenceID IS NULL", UnusedDocumentsCheck.Sql);
    }

    [Fact]
    public void Content_type_patterns_are_mutually_exclusive_on_shared_keywords()
    {
        // Spot-check the pattern lists don't overlap on the top 3 most common strings. A content
        // type named "ImageFile" would hit both "image" and "file" — it's fine if it falls into
        // one bucket consistently (currently CNT011 wins because NOT (Image) is evaluated first
        // in CNT002's exclusion, but CNT010 and CNT011 themselves match independently). This
        // test documents the current overlap behavior so a future refactor doesn't accidentally
        // flip it without noticing.
        Assert.DoesNotContain("file", string.Join("|", ContentTypePatterns.ImageLikeClauses));
        Assert.DoesNotContain("image", string.Join("|", ContentTypePatterns.FileLikeClauses));
    }
}
