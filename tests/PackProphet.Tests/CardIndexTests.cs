using PackProphet.Data;

namespace PackProphet.Tests;

public class CardIndexTests
{
    private static CardIndex Ix => Snapshot.Index();

    [Fact]
    public void IndexesEveryCard_AndKeysAreUnique()
    {
        Assert.Equal(3761, Ix.All.Count);
        Assert.Equal(Ix.All.Count, Ix.ByKey.Count); // no duplicate set+number upstream
        Assert.Equal("A1-1", Ix.All[0].Key);
    }

    [Fact]
    public void A1PackMembership_MatchesTheObservedStructure()
    {
        // Genetic Apex is the shape the odds engine is designed around: mostly
        // pack-exclusive cards, plus a block of secret rares in ALL three packs.
        var a1 = Ix.BySet["A1"];
        Assert.Equal(286, a1.Count);

        // Not equal across packs: Mewtwo has 79 exclusives, the other two have 80.
        Assert.Equal(125, Ix.ByPack[CardIndex.PackKey("A1", "Mewtwo")].Count);
        Assert.Equal(126, Ix.ByPack[CardIndex.PackKey("A1", "Charizard")].Count);
        Assert.Equal(126, Ix.ByPack[CardIndex.PackKey("A1", "Pikachu")].Count);

        var inAllThree = a1.Count(c => c.Packs is { Length: 3 });
        Assert.Equal(46, inAllThree);
    }

    [Fact]
    public void CardsWithNoPack_AreExcludedFromPackMembership()
    {
        // Counting unobtainable cards toward a set target would misreport progress
        // forever, because no amount of pack opening can ever finish them.
        var unobtainable = Ix.All.Where(c => !c.IsPackObtainable).ToList();
        Assert.NotEmpty(unobtainable);

        foreach (var c in unobtainable)
            Assert.Empty(Ix.PacksContaining(c));

        // Both promo sets are entirely unobtainable from packs, plus one A1 Immersive.
        var bySet = unobtainable.GroupBy(c => c.Set).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(50, bySet["PROMO-A"]);
        Assert.Equal(31, bySet["PROMO-B"]);
        Assert.Equal(1, bySet["A1"]);
    }

    [Fact]
    public void PromoSets_ArePartlyPackObtainable_NotWhollyUnobtainable()
    {
        // Corrects an earlier wrong assumption. Promo sets are NOT entirely outside
        // packs: most promo cards belong to "Vol. N" promo packs. Only some have no
        // pack at all. The two groups need different handling, so assert the split.
        foreach (var set in new[] { "PROMO-A", "PROMO-B" })
        {
            var cards = Ix.BySet[set];
            Assert.Contains(cards, c => c.IsPackObtainable);
            Assert.Contains(cards, c => !c.IsPackObtainable);
        }

        var promoPacks = Ix.AllPackKeys.Where(k => k.StartsWith("PROMO-")).ToList();
        Assert.NotEmpty(promoPacks);
        Assert.All(promoPacks, k => Assert.Contains("Vol.", k));
    }

    [Fact]
    public void AllDiamondsTarget_IsASubsetOfTheWholeSet_AndSkipsStars()
    {
        var all = Ix.WantedInSet("A1", Ix.Ladder.UpTo(99)).ToList();
        var diamonds = Ix.WantedInSet("A1", Ix.Ladder.UpTo(3)).ToList();

        Assert.True(diamonds.Count < all.Count, "diamonds should be a strict subset");
        Assert.All(diamonds, c => Assert.Contains(c.Rarity, new[] { "C", "U", "R", "RR" }));

        // Every wanted card must be pack-obtainable, at every target level.
        Assert.All(all, c => Assert.True(c.IsPackObtainable));
    }

    [Fact]
    public void EveryPackKey_ResolvesToCardsFromItsOwnSet()
    {
        foreach (var key in Ix.AllPackKeys)
        {
            var set = key.Split(':')[0];
            Assert.All(Ix.ByPack[key], c => Assert.Equal(set, c.Set));
            Assert.NotEmpty(Ix.ByPack[key]);
        }
    }

    [Fact]
    public void EverySetWithPacks_HasEveryCardReachableFromSomePack()
    {
        // Guards the odds engine's core assumption: within a pack-bearing set, any card
        // the user can want is obtainable from at least one pack of that set.
        foreach (var (set, cards) in Ix.BySet)
        {
            var obtainable = cards.Where(c => c.IsPackObtainable).ToList();
            if (obtainable.Count == 0) continue; // promo-only set

            foreach (var c in obtainable)
            {
                var packs = Ix.PacksContaining(c).ToList();
                Assert.NotEmpty(packs);
                Assert.All(packs, p => Assert.True(Ix.ByPack.ContainsKey(p),
                    $"{c.Key} claims pack '{p}' which is not in the index"));
            }
        }
    }

    [Fact]
    public void OwnershipIsKeyedByCard_NotBySetEntry()
    {
        // The game treats owning a card as owning it for every set it appears in.
        var ix = Ix;
        Assert.Equal(3761, ix.All.Count);
        Assert.Equal(3546, ix.DistinctOwnableCards);
        Assert.Equal(215, ix.All.Count - ix.DistinctOwnableCards);
    }

    [Fact]
    public void A4bMostlyReprintsEarlierSets_SoItsRealCostIsFarLowerThanItsSize()
    {
        // A4b lists 379 entries, but 214 are reprints of cards from earlier sets. Treating
        // entries as ownership would price "complete A4b" at nearly double its true cost.
        var a4b = Ix.BySet["A4b"];
        Assert.Equal(379, a4b.Count);

        var firstPrintedElsewhere = a4b.Count(c =>
            Ix.ByOwnershipKey[c.OwnershipKey].Any(e => e.Set != "A4b"));
        Assert.Equal(214, firstPrintedElsewhere);

        // And the wanted-set query must not double-count them.
        var wanted = Ix.WantedInSet("A4b", Ix.Ladder.UpTo(99)).ToList();
        Assert.Equal(wanted.Count, wanted.Select(c => c.OwnershipKey).Distinct().Count());
    }

    [Fact]
    public void OwningAReprintedCardOnce_SatisfiesEverySetItAppearsIn()
    {
        var shared = Ix.ByOwnershipKey.First(kv =>
            kv.Value.Select(c => c.Set).Distinct().Count() > 1);

        var owned = new Collection().With(shared.Key, 1);

        // Same single acquisition, counted as owned from every set that lists it.
        foreach (var entry in shared.Value)
            Assert.Equal(1, owned.Of(entry));

        Assert.True(shared.Value.Select(c => c.Key).Distinct().Count() > 1,
            "expected the same card under more than one set-number");
    }

    [Fact]
    public void ReprintedCards_PoolTheirPacksAcrossSets()
    {
        // A reprint is obtainable from its original set's packs AND the reprinting set's,
        // so demand for it must accumulate rates from all of them.
        var shared = Ix.ByOwnershipKey.First(kv =>
            kv.Value.Select(c => c.Set).Distinct().Count() > 1 &&
            kv.Value.All(c => c.IsPackObtainable));

        var packs = Ix.PacksYielding(shared.Key).ToList();
        Assert.True(packs.Select(p => p.Split(':')[0]).Distinct().Count() > 1,
            $"expected packs from multiple sets, got {string.Join(", ", packs)}");
    }
}
