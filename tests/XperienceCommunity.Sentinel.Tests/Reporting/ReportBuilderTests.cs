using System.Reflection;
using XperienceCommunity.Sentinel.Reporting;

namespace XperienceCommunity.Sentinel.Tests.Reporting;

/// <summary>
/// <see cref="ReportBuilder.SentinelVersion"/> used to be a hand-maintained literal
/// ("0.1.0-alpha") that silently drifted from the version the Core package actually ships —
/// every report.json understated its own version by several minor releases. These tests pin
/// it to the Core assembly's own version metadata instead, so a future release can't repeat
/// the drift.
/// </summary>
public class ReportBuilderTests
{
    [Fact]
    public void SentinelVersion_matches_the_Core_assembly_informational_version()
    {
        var assembly = typeof(ReportBuilder).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        // Build metadata (the "+abcdef..." suffix some CI builds stamp on) is not part of the
        // version identity a reader of report.json cares about — strip it before comparing.
        var expected = informationalVersion?.Split('+', 2)[0]
            ?? assembly.GetName().Version?.ToString()
            ?? throw new InvalidOperationException("Core assembly exposes no version metadata at all.");

        Assert.Equal(expected, ReportBuilder.SentinelVersion);
    }

    [Fact]
    public void SentinelVersion_is_not_the_stale_hardcoded_literal_unless_the_assembly_genuinely_says_so()
    {
        var assembly = typeof(ReportBuilder).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?.Split('+', 2)[0];

        var assemblyGenuinelyReportsStaleVersion = informationalVersion == "0.1.0-alpha";

        if (!assemblyGenuinelyReportsStaleVersion)
        {
            Assert.NotEqual("0.1.0-alpha", ReportBuilder.SentinelVersion);
        }
    }
}
