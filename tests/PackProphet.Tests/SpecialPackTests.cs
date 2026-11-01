using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Import;

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
}
