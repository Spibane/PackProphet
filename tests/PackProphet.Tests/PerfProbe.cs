namespace PackProphet.Tests;

using System.Diagnostics;
using PackProphet.Engine;
using PackProphet.State;
using PackProphet.Sync;

public class PerfProbe
{
    [Fact]
    public void RankingAndAdvisorAreFastEnoughToRunOnEveryEdit()
    {
        var ix = Snapshot.Index();
        var odds = Snapshot.Odds();
        var ranker = new PackRanker(ix, odds);
        var target = ix.AllPackKeys.Select(k => k.Split(':')[0]).Distinct()
            .Select(s => (ICompletionTarget)RarityLadderTarget.UpTo(s, 9, Snapshot.Index(), 2))
            .ToArray();
        var everything = new CompositeTarget(target);
        var owned = new Collection();

        var warm = ranker.Rank(everything, owned);          // primes the rate caches
        Assert.NotEmpty(warm);

        var sw = Stopwatch.StartNew();
        ranker.Rank(everything, owned);
        var rank = sw.ElapsedMilliseconds;

        sw.Restart();
        ranker.BestCasePacksToFinish(everything, owned);
        var price = sw.ElapsedMilliseconds;

        // Generous ceilings: these run on every collection edit, so anything near a second
        // would make tapping feel broken. Recorded so a future change cannot quietly
        // regress it.
        Assert.True(rank < 2000, $"ranking took {rank} ms");
        Assert.True(price < 500, $"pricing took {price} ms");
    }

    [Fact]
    public void MergingTwoLongHistoriesIsFastEnoughToRunOnEveryLaunch()
    {
        // A sync runs on launch and six seconds after every edit, on a phone, with the collection
        // page waiting on it. The log is the one part of the state that only ever grows, so it is
        // the part where a per-row scan stops being free: three thousand rows a side compared
        // pairwise is nine million comparisons.
        const int rows = 3000;
        var at = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        var ancestor = AppState.Fresh();
        for (var i = 0; i < rows; i++)
            ancestor.Active.PackLog.Add(
                new PackOpenEvent(at.AddMinutes(i), "A1", "A1:pikachu", "std", [$"c{i}.webp"])
                {
                    Id = LogId.Derive($"row-{i}", 0),
                });

        var phone = Copy(ancestor);
        phone.Active.PackLog.Add(
            new PackOpenEvent(at.AddDays(9), "A1", "A1:mewtwo", "std", ["p.webp"]) { Id = "phone-1" });

        var laptop = Copy(ancestor);
        laptop.Active.PackLog.Add(
            new PackOpenEvent(at.AddDays(9), "A1", "A1:mewtwo", "std", ["l.webp"]) { Id = "laptop-1" });

        var sw = Stopwatch.StartNew();
        var merged = StateMerge.Merge(phone, laptop, ancestor).State;
        var took = sw.ElapsedMilliseconds;

        Assert.Equal(rows + 2, merged.Active.PackLog.Count);

        // Generous, and on a machine far faster than the phone this has to be quick on. The
        // ceiling is here to catch a return to scanning, which would be seconds rather than
        // milliseconds at this size.
        Assert.True(took < 2000, $"merging two {rows}-row logs took {took} ms");
    }

    private static AppState Copy(AppState state) =>
        StateSerializer.Deserialize(StateSerializer.Serialize(state))!;
}
