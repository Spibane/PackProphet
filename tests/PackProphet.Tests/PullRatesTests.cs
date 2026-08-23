using PackProphet.Data;
using PackProphet.Engine;

namespace PackProphet.Tests;

/// <summary>
/// Pins the SHAPE of pullRates.json. Every assumption below was wrong in an earlier
/// draft of this project's plan, and each would have silently produced wrong odds
/// rather than an error.
/// </summary>
public class PullRatesTests
{
    [Fact]
    public void NormalisedVariantWeights_SumToExactlyOne()
    {
        // Upstream appearance rates sum to 99.999 for several sets, not 100.
        foreach (var set in Snapshot.Rates().ModelledSets)
        {
            var weight = Snapshot.Rates().Variants(set).Sum(v => v.Weight);
            Assert.Equal(1.0, weight, precision: 9);
        }
    }

    [Fact]
    public void RawAppearanceRates_DoNotAlwaysSumTo100_WhichIsWhyWeNormalise()
    {
        var sloppy = Snapshot.Rates().ModelledSets
            .Where(s => Math.Abs(Snapshot.Rates().Variants(s)
                        .Sum(v => v.Variant.AppearanceRate) - 100.0) > 1e-6)
            .ToList();

        Assert.NotEmpty(sloppy); // if upstream ever cleans this up, we can drop normalising
    }

    [Fact]
    public void SetsHaveTwoToFourVariants_NotJustRegularAndRare()
    {
        var counts = Snapshot.Rates().ModelledSets
            .Select(s => Snapshot.Rates().Variants(s).Count)
            .Distinct()
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(new[] { 2, 3, 4 }, counts);

        var names = Snapshot.Rates().ModelledSets
            .SelectMany(s => Snapshot.Rates().Variants(s).Select(v => v.Name))
            .Distinct().OrderBy(n => n).ToArray();

        Assert.Equal(
            new[] { "Rare Pack", "Regular Pack", "Regular Pack +1", "Themed Rare Pack" },
            names);
    }

    [Fact]
    public void CardsPerPack_IsNotAlways5()
    {
        // A4b has a 4-card variant and several sets have 6-card variants, so nothing
        // may assume a 5-slot pack.
        var sizes = Snapshot.Rates().ModelledSets
            .SelectMany(s => Snapshot.Rates().Variants(s).Select(v => v.Variant.Slots.Count))
            .Distinct().OrderBy(n => n).ToArray();

        Assert.Equal(new[] { 4, 5, 6 }, sizes);
    }

    [Fact]
    public void SlotKeys_AreNotConsistentlyOneBased()
    {
        // "Themed Rare Pack" numbers slots 0..4 while everything else uses 1..n.
        // Anything that indexes slots by position rather than enumerating them is broken.
        var zeroBased = Snapshot.Rates().ModelledSets
            .SelectMany(s => Snapshot.Rates().Variants(s)
                .Where(v => v.Variant.Slots.ContainsKey("0"))
                .Select(v => $"{s}/{v.Name}"))
            .ToList();

        Assert.NotEmpty(zeroBased);
    }

    [Fact]
    public void EverySlotDistribution_SumsTo100()
    {
        foreach (var set in Snapshot.Rates().ModelledSets)
        foreach (var (name, _, variant) in Snapshot.Rates().Variants(set))
        foreach (var (slot, dist) in variant.Slots)
            Assert.Equal(100.0, dist.Values.Sum(), precision: 1);
    }

    [Fact]
    public void SomeRateCodes_AreNotRealRarities_AndMatchNoCards()
    {
        // A4b (the Deluxe set) has slot entries for CF/RF/UF — foil codes that appear in
        // NO rarity table and on NO card. They carry ~61% of one slot's probability.
        // The engine must treat probability mass it cannot resolve to cards as "yields
        // nothing identifiable" rather than dividing by a zero card count.
        var known = Snapshot.Rarities().Keys.ToHashSet();
        var phantom = Snapshot.Rates().ModelledSets
            .SelectMany(s => Snapshot.Rates().Variants(s)
                .SelectMany(v => v.Variant.Slots.Values)
                .SelectMany(d => d.Keys))
            .Distinct().Where(c => !known.Contains(c))
            .OrderBy(c => c).ToArray();

        Assert.Equal(new[] { "CF", "RF", "UF" }, phantom);

        var cardRarities = Snapshot.Index().All.Select(c => c.Rarity).ToHashSet();
        Assert.All(phantom, c => Assert.DoesNotContain(c, cardRarities));
    }

    [Fact]
    public void SarIsNeverNamedInARate_SoSlotCodesMeanRungsNotExactCodes()
    {
        // 113 SAR cards exist and are obviously pullable, yet no slot distribution ever
        // names SAR — upstream folds it into the "SR" entry. Both sit on the same 2★
        // rung (same group, count, points and dust price), so a slot's rarity code must
        // be resolved to a LADDER RUNG and N counted across that whole rung. Counting
        // only exact-code matches would price every SAR card as unobtainable.
        var named = Snapshot.Rates().ModelledSets
            .SelectMany(s => Snapshot.Rates().Variants(s)
                .SelectMany(v => v.Variant.Slots.Values)
                .SelectMany(d => d.Keys))
            .ToHashSet();

        Assert.DoesNotContain("SAR", named);
        Assert.Contains("SR", named);

        var ladder = Snapshot.Index().Ladder;
        Assert.Equal(ladder.IndexOf("SR"), ladder.IndexOf("SAR"));

        Assert.Contains(Snapshot.Index().All, c => c.Rarity == "SAR");
    }

    [Fact]
    public void CoverageIsIncomplete_AndTheGapMustBeDisclosed()
    {
        // The newest set and both promo sets have NO rate data. The engine cannot price
        // them; the UI has to say so rather than omit them and imply there is nothing
        // to gain. If this list shrinks upstream, that is good news worth noticing.
        var setsWithCards = Snapshot.Index().BySet.Keys.ToHashSet();
        var unpriceable = setsWithCards.Where(s => !Snapshot.Rates().Covers(s))
                                      .OrderBy(s => s).ToArray();

        Assert.Equal(new[] { "B4", "PROMO-A", "PROMO-B" }, unpriceable);
    }

    // ---- borrowed rates ---------------------------------------------------------------

    [Fact]
    public void StandardDonor_IsTheNewestMeasuredOrdinarySet()
    {
        var rates = Snapshot.Rates();
        var sets = new SetCatalog(Snapshot.PublishedSets(), Snapshot.Index().BySet.Keys);

        var donor = rates.StandardDonor(sets);

        Assert.NotNull(donor);
        Assert.True(rates.Covers(donor!));
        // B3b (2026-06-30) is the newest measured set. B4 is newer but has no rates to lend, and
        // the Deluxe set is excluded on principle: four cards and a guaranteed 4-diamond would
        // price an ordinary set as far better than it is.
        Assert.Equal("B3b", donor);
        Assert.NotEqual("A4b", donor);
    }

    [Fact]
    public void Assuming_PricesASetThatHadNoRates()
    {
        var rates = Snapshot.Rates();
        Assert.False(rates.Covers("B4"));

        var assumed = rates.Assuming(["B4"], "B3b");

        Assert.True(assumed.Covers("B4"));
        Assert.Equal("B3b", assumed.AssumedFrom["B4"]);
        Assert.True(assumed.IsAssumed("B4"));
        // The original is untouched: callers hold onto it to tell measured from borrowed.
        Assert.False(rates.Covers("B4"));
    }

    [Fact]
    public void Assuming_NeverOverwritesPublishedRates()
    {
        var rates = Snapshot.Rates();
        var before = rates.Variants("A1");

        var assumed = rates.Assuming(["A1"], "B3b");

        Assert.False(assumed.IsAssumed("A1"));
        Assert.Equal(before.Count, assumed.Variants("A1").Count);
        Assert.Equal(before[0].Weight, assumed.Variants("A1")[0].Weight, 12);
    }

    [Fact]
    public void Assuming_MakesTheSetsPacksPriceable_AndItsCardsPullable()
    {
        // The point of the whole feature: a released set with no published rates goes from
        // "nothing here can be pulled" to a real, labelled estimate.
        var index = Snapshot.Index();
        var assumed = Snapshot.Rates().Assuming(["B4"], "B3b");
        var odds = new PackOdds(index, assumed);

        var b4Packs = odds.PriceablePacks.Where(p => p.StartsWith("B4:")).ToArray();
        Assert.NotEmpty(b4Packs);

        var rates = odds.BestRatesByCard();
        var b4Cards = index.BySet["B4"].Where(c => c.IsPackObtainable).ToArray();
        Assert.NotEmpty(b4Cards);
        Assert.Contains(b4Cards, c => rates.GetValueOrDefault(c.Key) > 0);
    }

    [Fact]
    public void Assuming_AdaptsToTheNewSetsOwnCardCounts()
    {
        // Only the slot SHAPE is borrowed. Per-card odds come from dividing a slot's share by the
        // number of cards of that rung the pack holds, so two sets with the same donor still
        // price their commons differently when they hold different numbers of them.
        var index = Snapshot.Index();
        var odds = new PackOdds(index, Snapshot.Rates().Assuming(["B4"], "B3b"));

        var pack = odds.PriceablePacks.First(p => p.StartsWith("B4:"));
        var expected = odds.ExpectedCopies(pack);

        // Mass is spread over B4's own cards, not A4a's.
        Assert.All(expected.Keys, key => Assert.StartsWith("B4-", key));
        // A slot always yields exactly one card, so expected copies per pack cannot exceed the
        // pack's card count — the check that a borrowed distribution has not been double counted.
        Assert.InRange(expected.Values.Sum(), 1.0, 6.0);
    }

    [Fact]
    public void Assuming_AnUnknownDonor_ChangesNothing()
    {
        var rates = Snapshot.Rates();
        var assumed = rates.Assuming(["B4"], "NOPE");

        Assert.False(assumed.Covers("B4"));
        Assert.Empty(assumed.AssumedFrom);
    }
}
