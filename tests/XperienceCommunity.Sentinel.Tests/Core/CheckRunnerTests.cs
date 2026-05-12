using XperienceCommunity.Sentinel.Tests.Support;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Tests.Core;

public class CheckRunnerTests
{
    private static ScanContext Context(string? connectionString = null, IHttpClientFactory? factory = null) => new()
    {
        RepoPath = Path.GetTempPath(),
        ConnectionString = connectionString,
        HttpClientFactory = factory ?? new FakeHttpClientFactory(),
    };

    [Fact]
    public async Task Runtime_check_is_skipped_when_no_connection_string()
    {
        var check = new RecordingCheck("CNT001", "Runtime", CheckKind.Runtime);
        var runner = new CheckRunner([check]);

        var result = await runner.RunAsync(Context(connectionString: null));

        Assert.False(check.WasRun);
        var execution = Assert.Single(result.Executions);
        Assert.Equal(CheckExecutionStatus.SkippedRuntime, execution.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Runtime_check_runs_when_connection_string_present()
    {
        var check = new RecordingCheck("CNT001", "Runtime", CheckKind.Runtime);
        var runner = new CheckRunner([check]);

        var result = await runner.RunAsync(Context(connectionString: "Server=(local);Database=test"));

        Assert.True(check.WasRun);
        Assert.Equal(CheckExecutionStatus.Ran, Assert.Single(result.Executions).Status);
    }

    [Fact]
    public async Task Throwing_check_is_recorded_as_failed_and_emits_a_warning_finding()
    {
        var check = new ThrowingCheck("CFG999", "Broken");
        var runner = new CheckRunner([check]);

        var result = await runner.RunAsync(Context());

        var exec = Assert.Single(result.Executions);
        Assert.Equal(CheckExecutionStatus.Failed, exec.Status);
        Assert.Contains("boom", exec.ErrorMessage);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("SYS001", finding.RuleId);
        Assert.Equal(Severity.Warning, finding.Severity);
    }

    [Fact]
    public async Task Findings_from_multiple_checks_are_aggregated()
    {
        var a = new FixedFindingsCheck("A", Severity.Warning, count: 2);
        var b = new FixedFindingsCheck("B", Severity.Info, count: 3);
        var runner = new CheckRunner([a, b]);

        var result = await runner.RunAsync(Context());

        Assert.Equal(5, result.Findings.Count);
        Assert.Equal(2, result.WarningCount);
        Assert.Equal(3, result.InfoCount);
    }

    private sealed class RecordingCheck(string id, string title, CheckKind kind) : ICheck
    {
        public bool WasRun { get; private set; }
        public string RuleId => id;
        public string Title => title;
        public string Category => "Test";
        public CheckKind Kind => kind;
        public Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
        {
            WasRun = true;
            return Task.FromResult<IReadOnlyList<Finding>>([]);
        }
    }

    private sealed class ThrowingCheck(string id, string title) : ICheck
    {
        public string RuleId => id;
        public string Title => title;
        public string Category => "Test";
        public CheckKind Kind => CheckKind.Static;
        public Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    private sealed class FixedFindingsCheck(string id, Severity severity, int count) : ICheck
    {
        public string RuleId => id;
        public string Title => id;
        public string Category => "Test";
        public CheckKind Kind => CheckKind.Static;
        public Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
        {
            var findings = Enumerable.Range(0, count)
                .Select(i => new Finding(id, id, "Test", severity, $"msg {i}"))
                .ToArray();
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }
    }
}
