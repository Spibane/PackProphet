namespace PackProphet.Tests;

using System.Diagnostics;
using PackProphet.Engine;

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
}
