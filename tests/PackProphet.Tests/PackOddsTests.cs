using PackProphet.Data;
using PackProphet.Engine;

namespace PackProphet.Tests;

public class PackOddsTests
{
    /// <summary>
    /// Hybrid absolute-or-relative comparison. These sums accumulate thousands of terms, so
    /// they differ from a reference computation by an ULP or two purely through summation
    /// order — but a purely relative bound is meaningless when the expected value is itself
    /// float noise near zero (comparing 9e-16 to -2e-15 gives a "relative error" of 3).
    /// </summary>
    private static void AssertClose(double expected, double actual,
                                    double relTol = 1e-12, double absTol = 1e-12)
    {
        var allowed = Math.Max(absTol, relTol * Math.Abs(expected));
        var error = Math.Abs(actual - expected);
        Assert.True(error <= allowed,
            $"expected {expected:R}, got {actual:R} (error {error:E3} > allowed {allowed:E3})");
    }

    private static PackOdds Odds => Snapshot.Odds();

    /// <summary>Weighted slots per pack — the total probability mass a pack distributes.</summary>
    private static double ExpectedSlots(string set) =>
        Snapshot.Rates().Variants(set).Sum(v => v.Weight * v.Variant.Slots.Count);

    /// <summary>Total probability a pack distributes: each slot always yields exactly one card.</summary>
    private static double TotalMass(string set) =>
        Snapshot.Rates().Variants(set).Sum(v => v.Weight * v.Variant.Slots.Count);

    [Fact]
    public void EveryPriceablePack_DistributesAllOfItsMassToRealCards()
    {
        // Each slot yields exactly one card, so summing expected copies over every card
        // must recover the slot count exactly — for ALL 26 packs, with nothing left over.
        //
        // This became exact only once foil printings were modelled. Before that, A4b lost
        // ~15% of its mass to CF/UF/RF, which looked like bad upstream data but was really
        // the foil variants of its 1-3 diamond cards. Held across every pack, this single
        // invariant catches essentially any weighting, grouping or slot-indexing mistake.
        foreach (var pack in Odds.PriceablePacks)
            AssertClose(TotalMass(pack.Split(':')[0]), Odds.ExpectedCopies(pack).Values.Sum());
    }

    [Fact]
    public void A4bSplitsEachDiamondRungIntoPlainAndFoilPrintings()
    {
        // A4b lists plain and foil versions as separate entries whose artwork filename
        // differs only in its variant index. The slot codes distinguish them: "R" yields
        // only the 25 plain rares, "RF" only the 25 foils. Spreading the plain share over
        // all 50 would halve the odds for every plain rare and give the foils none at all.
        var inPack = Snapshot.Index().ByPack["A4b:Deluxe"];
        var ec = Odds.ExpectedCopies("A4b:Deluxe");

        foreach (var rarity in new[] { "C", "U", "R" })
        {
            var plain = inPack.Where(c => c.Rarity == rarity && c.VariantIndex == 0).ToList();
            var foil = inPack.Where(c => c.Rarity == rarity && c.VariantIndex > 0).ToList();

            Assert.Equal(plain.Count, foil.Count);          // paired exactly
            Assert.All(plain, c => Assert.True(ec.GetValueOrDefault(c.Key) > 0,
                $"plain {c.Key} {c.Name} was priced as unobtainable"));
            Assert.All(foil, c => Assert.True(ec.GetValueOrDefault(c.Key) > 0,
                $"foil {c.Key} {c.Name} was priced as unobtainable"));
        }

        // RF's share (20.3295%) exactly equals R's in the same slot, over equal card
        // counts — so a plain rare and its foil twin must come out identically priced.
        var plainR = inPack.First(c => c.Rarity == "R" && c.VariantIndex == 0);
        var foilR = inPack.First(c => c.Rarity == "R" && c.VariantIndex > 0);
        AssertClose(ec[plainR.Key], ec[foilR.Key], 1e-9, 1e-15);

        // Commons appear in slots 1 and 2 as well, so a plain common must beat its foil.
        var plainC = inPack.First(c => c.Rarity == "C" && c.VariantIndex == 0);
        var foilC = inPack.First(c => c.Rarity == "C" && c.VariantIndex > 0);
        Assert.True(ec[plainC.Key] > ec[foilC.Key] * 2);
    }

    [Fact]
    public void FoilSplittingAppliesOnlyWhereTheRatesNameFoilCodes()
    {
        // Variant index means "alternate art" in every set except Deluxe, so a non-zero
        // index must NOT exclude a card elsewhere. A1 has two variant-01 commons; both have
        // to stay obtainable, or the split has leaked out of A4b.
        var strays = Snapshot.Index().All
            .Where(c => c.Set != "A4b" && c.VariantIndex > 0 && c.IsPackObtainable
                        && Snapshot.Rates().Covers(c.Set))
            .Where(c => Snapshot.Index().PacksContaining(c)
                        .All(p => Odds.ExpectedCopiesOf(p, c.Key) <= 0))
            .Select(c => $"{c.Key} {c.Name} ({c.Rarity} v{c.VariantIndex})")
            .ToArray();

        Assert.Empty(strays);
    }

    [Fact]
    public void NoPackNameSpansMoreThanOneSet()
    {
        // The engine keys packs as "set:pack", which assumes a pack draws from exactly one
        // set. Deluxe packs actually contain cards from PREVIOUS sets, so this assumption
        // only survives because upstream catalogues all 379 of them under A4b. If a future
        // pack is listed across sets, that key fragments one pack into several and each
        // fragment gets priced against a slice of its real card pool — badly wrong, and
        // silently so. Fail loudly instead.
        var spanning = Snapshot.Index().All
            .Where(c => c.IsPackObtainable)
            .SelectMany(c => c.Packs!.Select(p => (Pack: p, c.Set)))
            .GroupBy(x => x.Pack)
            .Where(g => g.Select(x => x.Set).Distinct().Count() > 1)
            .Select(g => $"{g.Key}: {string.Join("/", g.Select(x => x.Set).Distinct())}")
            .ToArray();

        Assert.Empty(spanning);
    }

    [Fact]
    public void DeluxePacksHaveTheirOwnStructure_IncludingAGuaranteedFourDiamond()
    {
        // Deluxe is unlike every other pack: 4 cards instead of 5-6, contents drawn from
        // earlier sets, and foil variants of the 1-3 diamond cards. Most usefully, its
        // last slot is 100% RR — EVERY Deluxe pack yields a 4-diamond card, which makes it
        // categorically the best pack for finishing a 4-diamond target. The ranking should
        // surface that rather than leaving it buried in the odds.
        var regular = Snapshot.Rates().Variants("A4b").Single(v => v.Name == "Regular Pack");

        Assert.Equal(4, regular.Variant.Slots.Count);
        Assert.Equal(100.0, regular.Variant.Slots["4"]["RR"]);
        Assert.Single(regular.Variant.Slots["4"]);

        // Every A4b card sits in the one Deluxe pack, hence the unusually large set.
        Assert.Equal(379, Snapshot.Index().BySet["A4b"].Count);
        Assert.Equal(379, Snapshot.Index().ByPack["A4b:Deluxe"].Count);
    }

    [Fact]
    public void SarCardsAreObtainable_WhichIsTheWholePointOfGroupingByRung()
    {
        // No slot distribution ever names SAR; upstream folds it into "SR". If N counted
        // exact rarity codes instead of ladder rungs, every SAR card would be priced as
        // permanently unobtainable and every other 2-star card would be overpriced.
        var sar = Snapshot.Index().All
            .Where(c => c.Rarity == "SAR" && c.IsPackObtainable && Snapshot.Rates().Covers(c.Set))
            .ToList();

        Assert.NotEmpty(sar);

        foreach (var card in sar.Take(40))
        {
            var best = Snapshot.Index().PacksContaining(card)
                .Max(p => Odds.ExpectedCopiesOf(p, card.Key));
            Assert.True(best > 0, $"{card.Key} {card.Name} (SAR) was priced as unobtainable");
        }
    }

    [Fact]
    public void SarAndSrOnTheSameRung_ArePricedIdenticallyWithinAPack()
    {
        // They share the 2-star rung, so a pack must treat them as interchangeable.
        var pack = "A1:Mewtwo";
        var inPack = Snapshot.Index().ByPack[pack];
        var ec = Odds.ExpectedCopies(pack);

        var sr = inPack.Where(c => c.Rarity == "SR").Select(c => ec.GetValueOrDefault(c.Key)).ToList();
        var sar = inPack.Where(c => c.Rarity == "SAR").Select(c => ec.GetValueOrDefault(c.Key)).ToList();

        if (sr.Count > 0 && sar.Count > 0)
            Assert.Equal(sr.First(), sar.First(), 12);
    }

    [Fact]
    public void RarerCards_AreNeverMoreLikelyThanCommonerOnes()
    {
        var pack = "A1:Mewtwo";
        var ec = Odds.ExpectedCopies(pack);
        var ladder = Snapshot.Index().Ladder;

        double AvgFor(string code)
        {
            var xs = Snapshot.Index().ByPack[pack]
                .Where(c => c.Rarity == code)
                .Select(c => ec.GetValueOrDefault(c.Key)).ToList();
            return xs.Count == 0 ? double.NaN : xs.Average();
        }

        var (c, u, r, rr, ur) = (AvgFor("C"), AvgFor("U"), AvgFor("R"), AvgFor("RR"), AvgFor("UR"));
        Assert.True(c > u, $"C {c} should beat U {u}");
        Assert.True(u > r, $"U {u} should beat R {r}");
        Assert.True(r > rr, $"R {r} should beat RR {rr}");
        Assert.True(rr > ur, $"RR {rr} should beat UR {ur}");
        Assert.Equal(0, ladder.IndexOf("C"));
    }

    [Fact]
    public void EveryPriceableCard_HasAPositiveChance()
    {
        // Any card in a priceable pack must be reachable. A zero here means a whole rarity
        // is invisible to the engine, which is exactly the SAR bug in a different disguise.
        var unreachable = new List<string>();
        foreach (var pack in Odds.PriceablePacks)
        {
            var ec = Odds.ExpectedCopies(pack);
            foreach (var card in Snapshot.Index().ByPack[pack])
                if (ec.GetValueOrDefault(card.Key) <= 0)
                    unreachable.Add($"{pack} -> {card.Key} {card.Name} ({card.Rarity})");
        }
        Assert.Empty(unreachable);
    }

    [Fact]
    public void UnpriceablePacks_AreOnlyRealSetsAwaitingPullRates()
    {
        var sets = Odds.UnpriceablePacks.Select(k => k.Split(':')[0]).Distinct().OrderBy(s => s);

        // B4b has real, purchasable packs but NO rate data, so the newest set cannot be priced.
        // Omitting it from a ranking would read as "nothing to gain from B4b", the opposite of
        // the truth — so it must be disclosed.
        //
        // One of them again, after two: 2.11.0 published rates for B4 and B4a and brought B4b
        // without any. That is the normal state rather than a problem: card data appears
        // upstream as soon as a set is announced and pull rates are worked out from what people
        // open, so the newest set or two is always unpriced. The list shrinking is the good news
        // worth noticing.
        //
        // Promos are NOT listed. They have no rates either, but they are not packs anyone can
        // choose to open, so warning about them would report a problem the user cannot act on.
        Assert.Equal(new[] { "B4b" }, sets);

        foreach (var set in new[] { "B4b" })
        {
            Assert.False(Snapshot.Rates().Covers(set));
            Assert.NotEmpty(Snapshot.Index().BySet[set]);
        }
    }

    [Fact]
    public void PromoGroupings_AreExcludedFromEveryPackFacingSurface()
    {
        Assert.DoesNotContain(Odds.PriceablePacks, p => p.StartsWith("PROMO"));
        Assert.DoesNotContain(Odds.UnpriceablePacks, p => p.StartsWith("PROMO"));
        Assert.DoesNotContain(Snapshot.Index().OpenablePackKeys, p => p.StartsWith("PROMO"));
        Assert.DoesNotContain(Snapshot.Index().OpenableSets, s => s.StartsWith("PROMO"));

        // Still present in the raw data, because promo cards remain collectible and the
        // Collection view must show them.
        Assert.Contains(Snapshot.Index().AllPackKeys, p => p.StartsWith("PROMO"));
        Assert.NotEmpty(Snapshot.Index().BySet["PROMO-A"]);
    }

    [Fact]
    public void ChanceOfUseful_IsZeroWithNoDemand_AndHighWhenEverythingIsWanted()
    {
        Assert.Equal(0.0, Odds.ChanceOfUseful("A1:Mewtwo", []));

        var everything = Snapshot.Index().ByPack["A1:Mewtwo"]
            .Select(c => new Demand(c.Key, 1, [c]))
            .ToArray();

        var p = Odds.ChanceOfUseful("A1:Mewtwo", everything);
        Assert.Equal(1.0, p, 9); // every slot yields something you need
        Assert.Equal(1.0, Odds.ExpectedPacksToNextUseful("A1:Mewtwo", everything), 6);
    }

    [Fact]
    public void ChanceOfUseful_FallsAsDemandShrinks()
    {
        var inPack = Snapshot.Index().ByPack["A1:Mewtwo"];
        var all = inPack.Select(c => new Demand(c.Key, 1, [c])).ToArray();
        var justOneCrown = inPack.Where(c => c.Rarity == "UR")
                                 .Take(1).Select(c => new Demand(c.Key, 1, [c])).ToArray();

        Assert.True(Odds.ChanceOfUseful("A1:Mewtwo", all) >
                    Odds.ChanceOfUseful("A1:Mewtwo", justOneCrown));
    }
}
