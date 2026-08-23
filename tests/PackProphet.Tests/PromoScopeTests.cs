using PackProphet.Data;
using PackProphet.Engine;

namespace PackProphet.Tests;

public class PromoScopeTests
{
    [Fact]
    public void AnEverythingTarget_ExcludesPromoSets_SoItStaysFinishable()
    {
        // Promo cards come from events. Folding them into "everything" would make the whole
        // estimate permanently unfinishable and bury the sets you can actually work on.
        var ix = Snapshot.Index();
        var everything = new CompositeTarget(
            ix.OpenableSets.Select(s => (ICompletionTarget)RarityLadderTarget.UpTo(s, 3, Snapshot.Index(), 1))
              .ToArray());

        var ranker = new PackRanker(ix, Snapshot.Odds());

        Assert.DoesNotContain(ranker.Rank(everything, new Collection()), s => s.Set.StartsWith("PROMO"));

        // Anything still unreachable is a real set awaiting pull rates, never a promo.
        var unreachable = ranker.Unreachable(everything, new Collection());
        Assert.All(unreachable, d => Assert.DoesNotContain("PROMO", d.SuppliedBy[0].Set));
        Assert.All(unreachable, d => Assert.False(Snapshot.Rates().Covers(d.SuppliedBy[0].Set)));

        // And the estimate stays finite despite them: one unpriceable card must not turn the
        // whole projection into "never" and hide the cost of everything else.
        Assert.True(double.IsFinite(ranker.BestCasePacksToFinish(everything, new Collection())));
    }

    [Fact]
    public void PromoCardsRemainTrackable_JustNotRankable()
    {
        // Excluded from pack ranking, still present for collection tracking — you own them.
        var ix = Snapshot.Index();
        Assert.NotEmpty(ix.BySet["PROMO-A"]);
        Assert.NotEmpty(ix.BySet["PROMO-B"]);

        var promo = ix.BySet["PROMO-A"][0];
        var owned = new Collection().With(promo.OwnershipKey, 1);
        Assert.Equal(1, owned.Of(promo));
    }

    [Fact]
    public void IsPromoSet_RecognisesBothPromoSets_AndNothingElse()
    {
        Assert.True(CardIndex.IsPromoSet("PROMO-A"));
        Assert.True(CardIndex.IsPromoSet("PROMO-B"));
        foreach (var set in Snapshot.Index().OpenableSets)
            Assert.False(CardIndex.IsPromoSet(set));
    }
}
