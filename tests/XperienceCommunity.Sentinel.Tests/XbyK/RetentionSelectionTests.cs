using XperienceCommunity.Sentinel.Module.Retention;

namespace XperienceCommunity.Sentinel.Tests.XbyK;

/// <summary>
/// Pure-function tests for the retention "which scan-run IDs should be deleted" rule. The
/// behavior is surprisingly easy to get wrong (off-by-one on the keep window, flipped
/// ascending/descending sort silently keeping the OLDEST runs, unbounded-keep disabled
/// short-circuit dropping everything), so the math lives in <see cref="RetentionSelection"/>
/// as a static helper and this suite locks the edges down without touching Kentico's
/// <c>IInfoProvider&lt;T&gt;</c> surface.
///
/// <para>Not covered here: the actual Info-provider round-trip in
/// <see cref="SentinelRetentionService"/> — that needs a mocking framework against Kentico's
/// runtime container. The batching + delete loops are straightforward forwarders once the
/// "which IDs" question has been answered correctly.</para>
/// </summary>
public class RetentionSelectionTests
{
    [Fact]
    public void Default_threshold_of_100_keeps_newest_100_and_trims_the_rest()
    {
        // 150 runs, default threshold = 100. Expected: the 50 oldest IDs (1..50) get deleted,
        // the newest 100 (51..150) survive. Descending-ID ordering matches how scan runs grow
        // in the DB (IDENTITY column, monotonically increasing).
        var existing = Enumerable.Range(1, 150);

        var doomed = RetentionSelection.SelectIdsToDelete(existing, maxToKeep: 100);

        Assert.Equal(50, doomed.Count);
        Assert.Equal(Enumerable.Range(1, 50).OrderByDescending(x => x), doomed);
    }

    [Fact]
    public void Threshold_of_one_keeps_only_the_newest_run()
    {
        // Smallest "trim is active" window — useful when integrators want the absolute minimum
        // history (a sanity check that the math doesn't keep zero runs at threshold=1).
        var existing = new[] { 5, 10, 15, 20, 25 };

        var doomed = RetentionSelection.SelectIdsToDelete(existing, maxToKeep: 1);

        Assert.Equal(new[] { 20, 15, 10, 5 }, doomed);
    }

    [Fact]
    public void Empty_input_returns_empty_result()
    {
        // Fresh install, no runs yet. A naive "Skip(100)" on an empty enumerable would still
        // return empty — this is a sanity check, not a bug lurking in today's code, but locks
        // the behavior down so a future rewrite that switches to TopN + WhereNotIn doesn't
        // accidentally delete everything on an empty table.
        var doomed = RetentionSelection.SelectIdsToDelete(Array.Empty<int>(), maxToKeep: 100);

        Assert.Empty(doomed);
    }

    [Fact]
    public void Count_at_or_below_threshold_deletes_nothing()
    {
        // Exactly at threshold — no trim. The 100th scan shouldn't delete anything; trim only
        // kicks in on the 101st.
        var existing = Enumerable.Range(1, 100);

        var doomed = RetentionSelection.SelectIdsToDelete(existing, maxToKeep: 100);

        Assert.Empty(doomed);
    }

    [Fact]
    public void One_over_threshold_deletes_exactly_one_oldest_run()
    {
        // The smoke test for "retention runs on each scan" — after the 101st scan, the oldest
        // single run should be the only row deleted.
        var existing = Enumerable.Range(1, 101);

        var doomed = RetentionSelection.SelectIdsToDelete(existing, maxToKeep: 100);

        Assert.Single(doomed);
        Assert.Equal(1, doomed[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Non_positive_threshold_disables_trimming(int maxToKeep)
    {
        // Integrators can set MaxScanRunsToKeep <= 0 to opt out of retention entirely ("keep
        // forever"). Must return empty regardless of how many runs are in the DB — otherwise
        // misconfiguring the option could wipe the entire scan history.
        var existing = Enumerable.Range(1, 1000);

        var doomed = RetentionSelection.SelectIdsToDelete(existing, maxToKeep);

        Assert.Empty(doomed);
    }

    [Fact]
    public void Duplicate_ids_are_collapsed_before_the_keep_window_is_applied()
    {
        // Defensive — the PK prevents real duplicates in the DB, but if a caller passed a non-
        // distinct list (bad test fake, a manual SELECT that joined to findings, etc.), the
        // math must not count duplicates as distinct slots. Two 5s + two 10s should read as
        // [10, 5] after Distinct.
        var existing = new[] { 5, 5, 10, 10, 15, 20 };

        var doomed = RetentionSelection.SelectIdsToDelete(existing, maxToKeep: 2);

        // Newest two = 20, 15. Doomed = 10, 5 (each once, not twice).
        Assert.Equal(new[] { 10, 5 }, doomed);
    }

    [Fact]
    public void Null_input_throws()
    {
        // Caller contract — an IEnumerable<int>? signature is a footgun; we'd rather throw
        // loudly than silently return an empty list and mask a caller bug.
        Assert.Throws<ArgumentNullException>(() =>
            RetentionSelection.SelectIdsToDelete(null!, maxToKeep: 100));
    }
}
