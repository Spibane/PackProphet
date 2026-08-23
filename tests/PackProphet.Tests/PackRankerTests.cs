using PackProphet.Engine;

namespace PackProphet.Tests;

public class PackRankerTests
{
    private static PackRanker Ranker => new(Snapshot.Index(), Snapshot.Odds());

    [Fact]
    public void CompletedTarget_RanksNothing()
    {
        var target = RarityLadderTarget.UpTo("A1", 0, Snapshot.Index(), 1);
        var owned = target.Outstanding(Snapshot.Index(), new Collection())
            .Aggregate(new Collection(), (c, d) => c.With(d.Key, d.Remaining));

        Assert.Empty(Ranker.Rank(target, owned));
        Assert.Equal(0.0, Ranker.BestCasePacksToFinish(target, owned));
    }

    [Fact]
    public void ASetTarget_AlsoRanksOtherSetsPacksThatReprintItsCards()
    {
        // Not just A1's own packs: Deluxe reprints A1 cards, so it genuinely can advance an
        // A1 target. Restricting the ranking to the target's own set would hide a real and
        // sometimes better route — the reason demand pools rates across every supplying set.
        var standings = Ranker.Rank(RarityLadderTarget.UpTo("A1", 3, Snapshot.Index(), 1), new Collection());

        Assert.NotEmpty(standings);
        Assert.All(standings, s => Assert.True(s.ChanceUseful > 0));
        Assert.Contains(standings, s => s.Set == "A1");
        Assert.Contains(standings, s => s.PackKey == "A4b:Deluxe");
    }

    [Fact]
    public void RankingIsOrderedByChanceOfAHit()
    {
        var standings = Ranker.Rank(RarityLadderTarget.UpTo("A1", 9, Snapshot.Index(), 1), new Collection());
        var chances = standings.Select(s => s.ChanceUseful).ToList();
        Assert.Equal(chances.OrderByDescending(c => c), chances);
    }

    [Fact]
    public void WantingTwoCopies_CostsMoreThanOne()
    {
        var one = Ranker.BestCasePacksToFinish(
            RarityLadderTarget.UpTo("A1", 3, Snapshot.Index(), 1), new Collection());
        var two = Ranker.BestCasePacksToFinish(
            RarityLadderTarget.UpTo("A1", 3, Snapshot.Index(), 2), new Collection());

        Assert.True(two > one, $"two copies ({two}) should cost more than one ({one})");
        // Not merely double: later copies of a card you already have arrive alongside the
        // rest, so the second copy is cheaper than the first.
        Assert.True(two < one * 2.5);
    }

    [Fact]
    public void AHigherRarityTarget_CostsMore()
    {
        var diamonds = Ranker.BestCasePacksToFinish(
            RarityLadderTarget.UpTo("A1", 3, Snapshot.Index(), 1), new Collection());
        var crown = Ranker.BestCasePacksToFinish(
            RarityLadderTarget.UpTo("A1", 9, Snapshot.Index(), 1), new Collection());

        Assert.True(crown > diamonds * 2,
            $"Crown ({crown}) should be far dearer than diamonds ({diamonds})");
    }

    [Fact]
    public void OwningMoreNeverRaisesTheEstimate()
    {
        var target = RarityLadderTarget.UpTo("A1", 3, Snapshot.Index(), 1);
        var empty = Ranker.BestCasePacksToFinish(target, new Collection());

        var some = target.Outstanding(Snapshot.Index(), new Collection())
            .Take(30).Aggregate(new Collection(), (c, d) => c.With(d.Key, d.Remaining));

        Assert.True(Ranker.BestCasePacksToFinish(target, some) <= empty);
    }

    [Fact]
    public void PromoOnlyTarget_IsEntirelyUnreachable()
    {
        // Promo cards without packs can never be pulled. They must surface as unreachable
        // rather than inflate the estimate or vanish from the report.
        var promos = Snapshot.Index().BySet["PROMO-A"]
            .Where(c => !c.IsPackObtainable)
            .ToDictionary(c => c.OwnershipKey, _ => 1);
        var target = new WishlistTarget("promos", promos);

        Assert.NotEmpty(target.Outstanding(Snapshot.Index(), new Collection()));
        Assert.Empty(Ranker.Rank(target, new Collection()));
        Assert.Equal(target.Outstanding(Snapshot.Index(), new Collection()).Count,
                     Ranker.Unreachable(target, new Collection()).Count);
        Assert.True(double.IsPositiveInfinity(Ranker.BestCasePacksToFinish(target, new Collection())));
    }

    [Fact]
    public void DeluxeIsTheStandoutForFourDiamonds_BecauseEveryPackGuaranteesOne()
    {
        // A4b's last slot is 100% RR, so for a 4-diamond target it should hit every time.
        var target = RarityLadderTarget.UpTo("A4b", 3, Snapshot.Index(), 1);
        var top = Ranker.Rank(target, new Collection()).First();

        Assert.Equal("A4b:Deluxe", top.PackKey);

        // Not quite certain: 99.95% of the time you open the Regular Pack, whose final slot
        // is 100% RR. The remaining 0.05% is the Rare Pack, which has no guaranteed RR slot.
        Assert.InRange(top.ChanceUseful, 0.999, 0.9996);
    }

    [Fact]
    public void EverythingTarget_SpansManySets()
    {
        var sets = Snapshot.Index().AllPackKeys.Select(k => k.Split(':')[0]).Distinct();
        var everything = new CompositeTarget(
            sets.Select(s => (ICompletionTarget)RarityLadderTarget.UpTo(s, 3, Snapshot.Index(), 1)).ToArray());

        var standings = Ranker.Rank(everything, new Collection());
        Assert.True(standings.Select(s => s.Set).Distinct().Count() > 5);

        // No single pack can finish a multi-set target, which is why the ranking compares
        // packs on the share they can actually supply.
        Assert.All(standings, s => Assert.True(double.IsFinite(s.PacksToFinishItsShare)));
    }
}
