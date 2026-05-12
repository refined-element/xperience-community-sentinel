using XperienceCommunity.Sentinel.Core;
using XperienceCommunity.Sentinel.Module.Services;

namespace XperienceCommunity.Sentinel.Tests.XbyK;

/// <summary>
/// The fingerprint is how Sentinel maps a finding in scan N to an acknowledgment made during
/// scan N-1. Stability across scans is the whole point, so these tests lock down the invariants.
/// </summary>
public class FindingFingerprintTests
{
    private static Finding Sample(string message = "Content type 'LandingPage' has zero content items.",
                                  string? location = "CMS_Class.ClassName=ReXBK.LandingPage") =>
        new(RuleId: "CNT001",
            RuleTitle: "Unused content types",
            Category: "Content Model",
            Severity: Severity.Info,
            Message: message,
            Location: location);

    [Fact]
    public void Same_finding_produces_same_hash()
    {
        Assert.Equal(FindingFingerprint.Compute(Sample()), FindingFingerprint.Compute(Sample()));
    }

    [Fact]
    public void Hash_is_64_lowercase_hex_chars()
    {
        var hash = FindingFingerprint.Compute(Sample());
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Different_rule_changes_hash()
    {
        var a = Sample();
        var b = a with { RuleId = "CNT002" };
        Assert.NotEqual(FindingFingerprint.Compute(a), FindingFingerprint.Compute(b));
    }

    [Fact]
    public void Different_location_changes_hash()
    {
        var a = Sample();
        var b = a with { Location = "CMS_Class.ClassName=ReXBK.DifferentType" };
        Assert.NotEqual(FindingFingerprint.Compute(a), FindingFingerprint.Compute(b));
    }

    [Fact]
    public void Volatile_digits_in_message_do_NOT_change_hash()
    {
        // "14 unused content types" and "17 unused content types" should fingerprint identically
        // so an ack placed when the count was 14 still suppresses when it drifts to 17.
        var fourteen = Sample(message: "14 unused content types detected.");
        var seventeen = Sample(message: "17 unused content types detected.");
        Assert.Equal(FindingFingerprint.Compute(fourteen), FindingFingerprint.Compute(seventeen));
    }

    [Fact]
    public void Case_and_trim_are_normalized()
    {
        var a = Sample(message: "Content type 'LandingPage' has zero content items.");
        var b = a with { Message = "  CONTENT TYPE 'LANDINGPAGE' HAS ZERO CONTENT ITEMS.  " };
        Assert.Equal(FindingFingerprint.Compute(a), FindingFingerprint.Compute(b));
    }

    [Fact]
    public void Null_or_empty_location_hashes_consistently()
    {
        var withNull = Sample(location: null);
        var withEmpty = Sample(location: "");
        Assert.Equal(FindingFingerprint.Compute(withNull), FindingFingerprint.Compute(withEmpty));
    }
}
