using PackProphet.Data;
using PackProphet.Engine;

namespace PackProphet.Tests;

public class TargetsTests
{
    private static CardIndex Ix => Snapshot.Index();

    [Fact]
    public void EmptyCollection_WantsEveryCardOnceUnderACollectorPolicy()
    {
        var target = RarityLadderTarget.UpTo("A1", 3, Snapshot.Index(), 1);
        var outstanding = target.Outstanding(Ix, new Collection());

        Assert.All(outstanding, d => Assert.Equal(1, d.Remaining));
        // Diamonds only, and nothing unobtainable.
        Assert.All(outstanding, d => Assert.All(d.SuppliedBy, c => Assert.True(c.IsPackObtainable)));
    }

    [Fact]
    public void PlayerPolicy_DoublesTheRequirement()
    {
        var collector = RarityLadderTarget.UpTo("A1", 3, Snapshot.Index(), 1)
            .Outstanding(Ix, new Collection());
        var player = RarityLadderTarget.UpTo("A1", 3, Snapshot.Index(), 2)
            .Outstanding(Ix, new Collection());

        Assert.Equal(collector.Count, player.Count);            // same cards
        Assert.All(player, d => Assert.Equal(2, d.Remaining));  // twice each
    }

    [Fact]
    public void APlanCanWantTwoDiamondsButOneChaseArt()
    {
        // The shape people actually mean: two of the playable tiers, one of the pretty ones.
        var plan = new RarityPlan(Ix.Ladder.Rungs.ToDictionary(
            r => r.Index, r => r.Index <= 3 ? 2 : 1));

        var outstanding = new RarityLadderTarget("A1", plan).Outstanding(Ix, new Collection());

        foreach (var d in outstanding)
        {
            var tier = Ix.Ladder.IndexOf(d.SuppliedBy[0].Rarity)!.Value;
            Assert.Equal(tier <= 3 ? 2 : 1, d.Remaining);
        }
    }

    [Fact]
    public void AndTheReverse_OneOfTheDiamondsButTwoOfTheStars()
    {
        var plan = new RarityPlan(Ix.Ladder.Rungs.ToDictionary(
            r => r.Index, r => r.Index <= 3 ? 1 : 2));

        var outstanding = new RarityLadderTarget("A1", plan).Outstanding(Ix, new Collection());

        foreach (var d in outstanding)
        {
            var tier = Ix.Ladder.IndexOf(d.SuppliedBy[0].Rarity)!.Value;
            Assert.Equal(tier <= 3 ? 1 : 2, d.Remaining);
        }
    }

    [Fact]
    public void OwningCardsReducesThenClearsDemand()
    {
        var target = RarityLadderTarget.UpTo("A1", 0, Snapshot.Index(), 2);   // 1-diamond, 2 copies
        var first = target.Outstanding(Ix, new Collection());
        Assert.NotEmpty(first);

        var one = new Collection().With(first[0].Key, 1);
        Assert.Equal(1, target.Outstanding(Ix, one).Single(d => d.Key == first[0].Key).Remaining);

        var two = new Collection().With(first[0].Key, 2);
        Assert.DoesNotContain(target.Outstanding(Ix, two), d => d.Key == first[0].Key);
    }

    [Fact]
    public void ReprintedCards_AreDemandedOnce_EvenAcrossSets()
    {
        // A4b re-lists 214 earlier cards. Demand must be per ownable card, not per entry.
        var target = RarityLadderTarget.UpTo("A4b", 9, Snapshot.Index(), 1);
        var outstanding = target.Outstanding(Ix, new Collection());

        Assert.Equal(outstanding.Count, outstanding.Select(d => d.Key).Distinct().Count());
    }

    [Fact]
    public void OwningAnOriginal_SatisfiesTheDeluxeReprint()
    {
        var shared = Ix.BySet["A4b"].First(c =>
            Ix.ByOwnershipKey[c.OwnershipKey].Any(e => e.Set != "A4b") && c.IsPackObtainable);

        var target = RarityLadderTarget.UpTo("A4b", 9, Snapshot.Index(), 1);
        Assert.Contains(target.Outstanding(Ix, new Collection()), d => d.Key == shared.OwnershipKey);

        var owned = new Collection().With(shared.OwnershipKey, 1);
        Assert.DoesNotContain(target.Outstanding(Ix, owned), d => d.Key == shared.OwnershipKey);
    }

    [Fact]
    public void DeckTarget_DemandsCopies_AndAnyPrintingSatisfiesThem()
    {
        // Two copies of one identity, exactly as a deck code would list it.
        var nr = Ix.ByDeckBuilderNr.First(kv => kv.Value.Count > 1).Key;
        var printings = Ix.ByDeckBuilderNr[nr].DistinctBy(p => p.OwnershipKey).ToList();

        var deck = new DeckTarget("test", [nr, nr]);

        Assert.Equal(2, deck.Outstanding(Ix, new Collection()).Single().Remaining);

        // One copy of printing A plus one of printing B satisfies both slots.
        var mixed = new Collection()
            .With(printings[0].OwnershipKey, 1)
            .With(printings[1].OwnershipKey, 1);
        Assert.Empty(deck.Outstanding(Ix, mixed));

        // And two copies of the SAME printing works just as well.
        var doubled = new Collection().With(printings[0].OwnershipKey, 2);
        Assert.Empty(deck.Outstanding(Ix, doubled));
    }

    [Fact]
    public void CompositeTarget_TakesTheMaximum_NeverTheSum()
    {
        // Two targets each wanting one copy still need only one copy.
        var a = RarityLadderTarget.UpTo("A1", 0, Snapshot.Index(), 1);
        var b = RarityLadderTarget.UpTo("A1", 0, Snapshot.Index(), 1);
        var composite = new CompositeTarget([a, b]);

        var single = a.Outstanding(Ix, new Collection());
        var both = composite.Outstanding(Ix, new Collection());

        Assert.Equal(single.Count, both.Count);
        Assert.All(both, d => Assert.Equal(1, d.Remaining));
    }

    [Fact]
    public void CompositeTarget_UnionsDistinctSets()
    {
        var composite = new CompositeTarget([
            RarityLadderTarget.UpTo("A1", 0, Snapshot.Index(), 1),
            RarityLadderTarget.UpTo("A2", 0, Snapshot.Index(), 1)]);

        var count = composite.Outstanding(Ix, new Collection()).Count;
        var a1 = RarityLadderTarget.UpTo("A1", 0, Snapshot.Index(), 1).Outstanding(Ix, new Collection()).Count;
        var a2 = RarityLadderTarget.UpTo("A2", 0, Snapshot.Index(), 1).Outstanding(Ix, new Collection()).Count;

        Assert.True(count > a1 && count > a2);
        Assert.True(count <= a1 + a2);
    }

    [Fact]
    public void WishlistTarget_PricesArbitraryCards()
    {
        var keys = Ix.All.Take(3).Select(c => c.OwnershipKey).ToList();
        var wishlist = new WishlistTarget("cool", keys.ToDictionary(k => k, _ => 2));

        var outstanding = wishlist.Outstanding(Ix, new Collection());
        Assert.Equal(3, outstanding.Count);
        Assert.All(outstanding, d => Assert.Equal(2, d.Remaining));
    }

    [Fact]
    public void CompletedTarget_YieldsNothing()
    {
        var target = RarityLadderTarget.UpTo("A1", 0, Snapshot.Index(), 1);
        var all = target.Outstanding(Ix, new Collection());
        var owned = all.Aggregate(new Collection(), (c, d) => c.With(d.Key, d.Remaining));

        Assert.Empty(target.Outstanding(Ix, owned));
    }
}
