using XperienceCommunity.Sentinel.Module.Acknowledgment;

namespace XperienceCommunity.Sentinel.Tests.XbyK;

/// <summary>
/// Locks down the SHA-256-hex invariant for fingerprints at the ack-service boundary. The DB
/// column holding these values is sized for exactly 64 hex chars, so a truthful validator is
/// the thing standing between a tampered page-command payload and a confusing 500.
/// </summary>
public class FingerprintFormatTests
{
    [Fact]
    public void Accepts_64_char_lowercase_hex()
    {
        Assert.True(FingerprintFormat.IsValid(new string('a', 64)));
        Assert.True(FingerprintFormat.IsValid("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"));
    }

    [Fact]
    public void Accepts_mixed_case_hex()
    {
        Assert.True(FingerprintFormat.IsValid("0123456789ABCDEF0123456789abcdef0123456789ABCDEF0123456789abcdef"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_null_or_blank(string? value)
    {
        Assert.False(FingerprintFormat.IsValid(value));
    }

    [Fact]
    public void Rejects_wrong_length()
    {
        Assert.False(FingerprintFormat.IsValid(new string('a', 63))); // too short
        Assert.False(FingerprintFormat.IsValid(new string('a', 65))); // too long
        Assert.False(FingerprintFormat.IsValid(new string('a', 128))); // double length
    }

    [Fact]
    public void Rejects_non_hex_characters()
    {
        // One non-hex char at the end (64 chars total).
        Assert.False(FingerprintFormat.IsValid(new string('a', 63) + "z"));
        // Whitespace inside (63 hex chars + a space).
        Assert.False(FingerprintFormat.IsValid(new string('a', 63) + " "));
        // Unicode that happens to be 64 chars long.
        Assert.False(FingerprintFormat.IsValid(new string('é', 64)));
    }

    [Fact]
    public void Length_constant_is_64()
    {
        Assert.Equal(64, FingerprintFormat.Length);
    }
}
