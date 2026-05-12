using XperienceCommunity.Sentinel.Commands;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Tests.Core;

/// <summary>
/// Pins down the <c>sentinel scan --fail-on</c> threshold-evaluation contract. This is
/// the hinge CI PR-gates swing on, so we assert each severity band against each threshold
/// explicitly rather than table-driving it — a regression here turns every pipeline into
/// either a false-green or a perma-red.
/// </summary>
public class ScanExitCodeTests
{
    private static Finding MakeFinding(Severity severity) => new(
        RuleId: "TST000",
        RuleTitle: "Test rule",
        Category: "Test",
        Severity: severity,
        Message: "Synthetic finding.");

    [Theory]
    [InlineData(FailOnThreshold.None)]
    [InlineData(FailOnThreshold.Info)]
    [InlineData(FailOnThreshold.Warning)]
    [InlineData(FailOnThreshold.Error)]
    public void No_findings_returns_success_for_every_threshold(FailOnThreshold threshold)
    {
        var result = ScanExitCode.Evaluate(Array.Empty<Finding>(), threshold);

        Assert.Equal(ScanExitCode.Success, result);
    }

    [Fact]
    public void Error_only_findings_with_error_threshold_trip()
    {
        var findings = new[] { MakeFinding(Severity.Error) };

        var result = ScanExitCode.Evaluate(findings, FailOnThreshold.Error);

        Assert.Equal(ScanExitCode.ThresholdTripped, result);
    }

    [Fact]
    public void Warning_only_findings_with_error_threshold_do_not_trip()
    {
        // A --fail-on Error gate must stay green when the scan only found warnings.
        // This is the whole point of having a threshold knob in the first place.
        var findings = new[] { MakeFinding(Severity.Warning), MakeFinding(Severity.Warning) };

        var result = ScanExitCode.Evaluate(findings, FailOnThreshold.Error);

        Assert.Equal(ScanExitCode.Success, result);
    }

    [Fact]
    public void Warning_only_findings_with_warning_threshold_trip()
    {
        var findings = new[] { MakeFinding(Severity.Warning) };

        var result = ScanExitCode.Evaluate(findings, FailOnThreshold.Warning);

        Assert.Equal(ScanExitCode.ThresholdTripped, result);
    }

    [Fact]
    public void Info_only_findings_with_info_threshold_trip()
    {
        var findings = new[] { MakeFinding(Severity.Info) };

        var result = ScanExitCode.Evaluate(findings, FailOnThreshold.Info);

        Assert.Equal(ScanExitCode.ThresholdTripped, result);
    }

    [Fact]
    public void Info_only_findings_with_warning_threshold_do_not_trip()
    {
        var findings = new[] { MakeFinding(Severity.Info), MakeFinding(Severity.Info) };

        var result = ScanExitCode.Evaluate(findings, FailOnThreshold.Warning);

        Assert.Equal(ScanExitCode.Success, result);
    }

    [Fact]
    public void Error_finding_with_none_threshold_does_not_trip()
    {
        // --fail-on None is the explicit opt-out. The scan may find everything under the
        // sun and the exit code still has to be zero.
        var findings = new[]
        {
            MakeFinding(Severity.Error),
            MakeFinding(Severity.Warning),
            MakeFinding(Severity.Info),
        };

        var result = ScanExitCode.Evaluate(findings, FailOnThreshold.None);

        Assert.Equal(ScanExitCode.Success, result);
    }

    [Fact]
    public void Mixed_findings_with_warning_threshold_trip_on_warning_or_error()
    {
        // Warning threshold must catch both the Warning and the Error — the latter is
        // "severity >= Warning" too.
        var findings = new[] { MakeFinding(Severity.Info), MakeFinding(Severity.Warning), MakeFinding(Severity.Error) };

        var result = ScanExitCode.Evaluate(findings, FailOnThreshold.Warning);

        Assert.Equal(ScanExitCode.ThresholdTripped, result);
    }

    [Fact]
    public void CountAtOrAboveThreshold_matches_the_filter_semantics()
    {
        var findings = new[]
        {
            MakeFinding(Severity.Info),
            MakeFinding(Severity.Warning),
            MakeFinding(Severity.Warning),
            MakeFinding(Severity.Error),
        };

        Assert.Equal(0, ScanExitCode.CountAtOrAboveThreshold(findings, FailOnThreshold.None));
        Assert.Equal(4, ScanExitCode.CountAtOrAboveThreshold(findings, FailOnThreshold.Info));
        Assert.Equal(3, ScanExitCode.CountAtOrAboveThreshold(findings, FailOnThreshold.Warning));
        Assert.Equal(1, ScanExitCode.CountAtOrAboveThreshold(findings, FailOnThreshold.Error));
    }

    [Fact]
    public void Exit_code_constants_match_the_spec()
    {
        // These constants are the public contract with CI systems. Changing them is a
        // breaking change to every pipeline that consumes the tool — hence the lock-in test.
        Assert.Equal(0, ScanExitCode.Success);
        Assert.Equal(1, ScanExitCode.Error);
        Assert.Equal(2, ScanExitCode.ThresholdTripped);
    }
}
