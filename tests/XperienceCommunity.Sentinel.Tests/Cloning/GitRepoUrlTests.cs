using XperienceCommunity.Sentinel.Cloning;

namespace XperienceCommunity.Sentinel.Tests.Cloning;

public class GitRepoUrlTests
{
    [Theory]
    [InlineData("refined-element/xperience-community-sentinel", "https://github.com/refined-element/xperience-community-sentinel.git")]
    [InlineData("github.com/refined-element/xperience-community-sentinel", "https://github.com/refined-element/xperience-community-sentinel.git")]
    [InlineData("https://github.com/refined-element/xperience-community-sentinel", "https://github.com/refined-element/xperience-community-sentinel.git")]
    [InlineData("https://github.com/refined-element/xperience-community-sentinel.git", "https://github.com/refined-element/xperience-community-sentinel.git")]
    [InlineData("  refined-element/xperience-community-sentinel  ", "https://github.com/refined-element/xperience-community-sentinel.git")]
    public void Normalizes_known_forms(string input, string expected)
    {
        Assert.Equal(expected, GitRepoUrl.Normalize(input));
    }

    [Fact]
    public void SSH_urls_pass_through_unchanged()
    {
        const string ssh = "git@github.com:refined-element/xperience-community-sentinel.git";
        Assert.Equal(ssh, GitRepoUrl.Normalize(ssh));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-repo")]
    [InlineData("/leading-slash/name")]
    public void Rejects_garbage_input(string input)
    {
        Assert.Throws<ArgumentException>(() => GitRepoUrl.Normalize(input));
    }
}
