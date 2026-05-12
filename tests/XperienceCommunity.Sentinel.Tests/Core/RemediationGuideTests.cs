using XperienceCommunity.Sentinel.Core.Remediation;

namespace XperienceCommunity.Sentinel.Tests.Core;

/// <summary>
/// Remediation catalog is a static dictionary — tests lock the lookup contract and the fallback
/// behavior so adding a new check without writing a guide doesn't silently break the admin UI.
/// </summary>
public class RemediationGuideTests
{
    [Fact]
    public void Known_rule_returns_rule_specific_entry()
    {
        var entry = RemediationGuide.For("CFG001");
        Assert.Contains("CMSHashStringSalt", entry.Title);
        Assert.NotEqual(RemediationGuide.GenericFallback, entry);
    }

    [Fact]
    public void Unknown_rule_returns_generic_fallback()
    {
        var entry = RemediationGuide.For("UNKNOWN999");
        Assert.Equal(RemediationGuide.GenericFallback, entry);
    }

    [Fact]
    public void Unknown_rule_TryFor_returns_null()
    {
        Assert.Null(RemediationGuide.TryFor("UNKNOWN999"));
    }

    [Fact]
    public void Rule_lookup_is_case_insensitive()
    {
        var upper = RemediationGuide.For("CFG001");
        var lower = RemediationGuide.For("cfg001");
        var mixed = RemediationGuide.For("cFg001");
        Assert.Same(upper, lower);
        Assert.Same(upper, mixed);
    }

    [Fact]
    public void Null_or_empty_rule_falls_back_to_generic()
    {
        Assert.Equal(RemediationGuide.GenericFallback, RemediationGuide.For(string.Empty));
        Assert.Null(RemediationGuide.TryFor(string.Empty));
        Assert.Equal(RemediationGuide.GenericFallback, RemediationGuide.For(null));
        Assert.Null(RemediationGuide.TryFor(null));
    }

    [Fact]
    public void Every_entry_has_non_empty_title_summary_and_steps()
    {
        // Catalog-integrity check: if someone adds a new entry and forgets a field, CI fails
        // before the blank cell ships to the admin UI.
        foreach (var ruleId in RemediationGuide.KnownRuleIds)
        {
            var entry = RemediationGuide.For(ruleId);
            Assert.False(string.IsNullOrWhiteSpace(entry.Title), $"Rule {ruleId} has a blank Title.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Summary), $"Rule {ruleId} has a blank Summary.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Steps), $"Rule {ruleId} has blank Steps.");
        }
    }

    [Fact]
    public void Catalog_covers_built_in_rule_prefixes()
    {
        // Spot-check that each check-family has at least one remediation entry so the admin UI
        // doesn't show fallback copy for every CFG/CNT/DEP/VER rule.
        var prefixes = new[] { "CFG", "CNT", "DEP", "VER" };
        foreach (var prefix in prefixes)
        {
            var hasEntry = RemediationGuide.KnownRuleIds.Any(r => r.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            Assert.True(hasEntry, $"No remediation entries for rule prefix '{prefix}' — add at least one.");
        }
    }
}
