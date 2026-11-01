using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Engine;
using PackProphet.Import;
using PackProphet.State;

namespace PackProphet.Tests;

/// <summary>
/// What 2.13.1 brought that nothing before it had: a Special Pack set (SP1), a rarity with no
/// symbol count (Anomaly), cards the game prints without a number, a third promo set, and a
/// detail table that arrived without its identities.
/// </summary>
public class SpecialPackTests
{
    private static CardIndex Ix => Snapshot.Index();

    [Theory]
    [InlineData("A1-1", "A1", 1)]
    [InlineData("PROMO-A-12", "PROMO-A", 12)]
    [InlineData("P-A-5", "P-A", 5)]
    [InlineData("SP1--3", "SP1", -3)]
    [InlineData("PROMO-C--1", "PROMO-C", -1)]
    public void AKeySplitsBackIntoItsSetAndNumber(string key, string set, int number)
    {
        Assert.True(PocketCard.TrySplitKey(key, out var s, out var n));
        Assert.Equal(set, s);
        Assert.Equal(number, n);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A1")]
    [InlineData("A1-")]
    [InlineData("-3")]
    [InlineData("A1-x")]
    public void WhatIsNotAKeyDoesNotSplit(string key) =>
        Assert.False(PocketCard.TrySplitKey(key, out _, out _));

    [Fact]
    public void EveryKeyInTheDataRoundTrips()
    {
        foreach (var card in Ix.All)
        {
            Assert.True(PocketCard.TrySplitKey(card.Key, out var set, out var number), card.Key);
            Assert.Equal((card.Set, card.Number), (set, number));
        }
    }

    [Fact]
    public void TheSecretMewsAreUnnumbered_AndSortAfterTheRestOfTheirSet()
    {
        var sets = new SetCatalog(Snapshot.PublishedSets(), Ix.BySet.Keys);
        var sp1 = Ix.BySet["SP1"].OrderBy(sets.SortKey, StringComparer.Ordinal).ToArray();

        var mews = sp1.Where(c => c.Rarity == "AN").ToArray();
        Assert.Equal(3, mews.Length);
        Assert.All(mews, m => Assert.True(m.IsUnnumbered));
        Assert.All(mews, m => Assert.Equal("—", m.NumberLabel));

        // The three Mews last, in upstream's order, after every numbered card.
        Assert.Equal(new[] { -3, -2, -1 }, sp1[^3..].Select(c => c.Number));
        Assert.All(sp1[..^3], c => Assert.False(c.IsUnnumbered));
    }

    [Fact]
    public void AnomalyIsAnUntradeableRungOfItsOwn_AtTheTopOfTheLadder()
    {
        var rung = Ix.Ladder.RungOf("AN");

        Assert.NotNull(rung);
        Assert.Equal(Ix.Ladder.Rungs.Count - 1, rung!.Index);
        Assert.Equal("anomaly", rung.GlyphClass);
        Assert.Equal(RarityRung.AnomalyMark, rung.Glyphs);
        Assert.False(GameRules.IsTradeable("AN"));
    }

    [Fact]
    public void ATrackerFileNamingTheSecretMewsReadsThemBack()
    {
        var csv = "card_id,quantity\nSP1--3,1\nSP1--2,2\n";
        var back = TrackerImport.FromCsv(csv, Ix);

        Assert.Empty(back.Problems);
        Assert.Equal(2, back.DistinctOwned);
    }

    [Theory]
    [InlineData("pa", "PROMO-A")]
    [InlineData("PB", "PROMO-B")]
    [InlineData("pc", "PROMO-C")]
    [InlineData("P-C", "PROMO-C")]
    [InlineData("C1", null)]
    [InlineData("SP1", null)]
    [InlineData("P1", null)]
    [InlineData("PXC", null)]
    public void APromoSetIsRecognisedHoweverItIsSpelled(string code, string? ours) =>
        Assert.Equal(ours, CardIndex.PromoSetFromShortCode(code));

    [Theory]
    [InlineData("SP1", true)]
    [InlineData("SP12", true)]
    [InlineData("SP", false)]
    [InlineData("SPA", false)]
    [InlineData("C1", false)]
    [InlineData("PROMO-C", false)]
    public void ASpecialPackSetIsRecognisedByItsCode(string set, bool special) =>
        Assert.Equal(special, CardIndex.IsSpecialSet(set));

    [Fact]
    public void CardDetailPublishedWithoutItsIdentity_IsStillJoined()
    {
        // Upstream published C1 and Promo C with every deckBuilderNr at 0. The records are
        // otherwise complete, and the card list knows which card each printing is.
        var c1 = Ix.ByKey["C1-1"];
        var nr = Ix.DeckNrOf(c1);

        Assert.NotNull(nr);
        Assert.True(Snapshot.Facts().Knows(nr!.Value));
        Assert.Equal("Basic", Snapshot.Facts().For(nr.Value)!.Stage);

        // It is the card list doing it: without one the record is reachable by printing only.
        var record = Snapshot.Facts().ForPrinting("C1-1")!;
        Assert.Equal(0, record.DeckBuilderNr);
        Assert.Null(new CardFacts([record]).For(nr.Value));
        Assert.NotNull(new CardFacts([record], Ix).For(nr.Value));
    }

    [Fact]
    public void ASpecialPackBorrowsNoOrdinaryPacksRates()
    {
        var sets = new SetCatalog(Snapshot.PublishedSets(), Ix.BySet.Keys);

        Assert.Null(Snapshot.Rates().DonorFor("SP1", deluxe: false, sets));
        Assert.NotNull(Snapshot.Rates().DonorFor("C1", deluxe: false, sets));
    }

    // ---- off the hourglass ------------------------------------------------------------
    //
    // A Special Pack opens on Special Pack Hourglasses, which events hand out and nothing earns
    // or buys. So it is opened, logged and collected like any pack, and never planned or paid
    // for with the pack hourglasses everything else shares.

    [Fact]
    public void ASpecialPackFollowsPromoRules()
    {
        // Events hand it out, so it is no pack to choose, and its cards are still collected.
        Assert.True(CardIndex.IsPromoSet("SP1"));
        Assert.False(CardIndex.IsPromoRun("SP1"));
        Assert.DoesNotContain(Ix.OpenablePackKeys, k => k.StartsWith("SP1:", StringComparison.Ordinal));
        Assert.DoesNotContain("SP1", Ix.OpenableSets);
        Assert.Contains("C1", Ix.OpenableSets);
        Assert.NotEmpty(Ix.BySet["SP1"]);
    }

    [Fact]
    public void TheNewestPromosAreStillPromoC_NotSP1()
    {
        // SP1 sorts after every series, and the newest-promo list is about the run being issued.
        var sets = new SetCatalog(Snapshot.PublishedSets(), Ix.BySet.Keys);
        Assert.Equal("PROMO-C", RecentPromos.CurrentSet(Ix, sets));
    }

    [Fact]
    public void ASpecialPackIsNeverRanked_EvenOnceItHasRates()
    {
        // Rates for it would not make it something to spend hourglasses on.
        var rates = Snapshot.Rates().Assuming(["SP1"], "B4a");
        Assert.True(rates.Covers("SP1"));

        var odds = new PackOdds(Ix, rates);
        Assert.DoesNotContain(odds.PriceablePacks, k => k.StartsWith("SP1:", StringComparison.Ordinal));
        Assert.DoesNotContain(odds.UnpriceablePacks, k => k.StartsWith("SP1:", StringComparison.Ordinal));
    }

    [Fact]
    public void ACardOnlyInASpecialPackHasNoPullRoute_AndSaysWhy()
    {
        var routes = new RouteCost(Ix, Snapshot.Odds(), Snapshot.Rarities());
        var mew = Ix.BySet["SP1"].First(c => c.Rarity == "AN");

        var pull = routes.For(mew, new Collection()).Options.First(o => o.Route == AcquisitionRoute.Pull);

        Assert.False(pull.Available);
        Assert.Equal(RouteBlock.EventPacksOnly, pull.Block);
    }

    [Fact]
    public void ALoggedSpecialPackTakesNothingFromTheDaysFreePacks()
    {
        var at = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var day = DateOnly.FromDateTime(at.ToLocalTime().DateTime);
        var log = new[]
        {
            new PackOpenEvent(at, "C1", "C1:Dual Fusion Pikachu", "unknown", []),
            new PackOpenEvent(at, "SP1", "SP1:First Partners", "unknown", []),
        };

        Assert.Equal(1, ResourcePlan.PacksOpenedOn(log, day));
    }

    // ---- no pack points ---------------------------------------------------------------

    [Fact]
    public void ASpecialPackEarnsNoPackPoints()
    {
        Assert.False(CardIndex.EarnsPackPoints("SP1"));
        Assert.False(CardIndex.EarnsPackPoints("PROMO-C"));
        Assert.True(CardIndex.EarnsPackPoints("C1"));
    }

    [Fact]
    public void ASpecialPackCardCannotBeBoughtWithPoints_AndSaysWhy()
    {
        var routes = new RouteCost(Ix, Snapshot.Odds(), Snapshot.Rarities());
        var starter = Ix.BySet["SP1"].First(c => c.Rarity == "AR");

        var points = routes.For(starter, new Collection()).Options.First(o => o.Route == AcquisitionRoute.PackPoints);

        Assert.False(points.Available);
        Assert.Equal(RouteBlock.EventPacksOnly, points.Block);
    }

    [Fact]
    public void AStrayBalanceAgainstASpecialPackRaisesNoCapWarning()
    {
        // Logged before Special Packs were known to earn nothing: a figure for a shop that does
        // not exist, so not something to warn about reaching the cap of.
        var ledger = new PointsLedger(Ix, Snapshot.Rarities());
        var balances = new Dictionary<string, int> { ["SP1"] = GameRules.PackPointsCap };

        Assert.Empty(ledger.Warnings(balances, new Collection(), _ => RarityPlan.Default(Ix.Ladder)));
    }
}
