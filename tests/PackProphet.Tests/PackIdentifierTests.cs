using PackProphet.Data;
using PackProphet.Vision;

namespace PackProphet.Tests;

/// <summary>
/// Working out which pack a reveal screenshot came from, using only the cards in it.
///
/// Genetic Apex is the shape this is designed around and the reason it works: three packs, about 80
/// cards exclusive to each and 46 shared across all three. One exclusive card in a hand of five
/// settles the question outright, and a hand of five nearly always has one — the interesting cases
/// are the ones where it does not.
/// </summary>
public class PackIdentifierTests
{
    private static CardIndex Ix => Snapshot.Index();

    private static PocketCard Card(string key) => Ix.ByKey[key];

    private static PackGuess Identify(params string[] keys) =>
        PackIdentifier.Identify(Ix, keys.Select(Card));

    [Fact]
    public void OneExclusiveCardNamesThePack()
    {
        // A1-1 Bulbasaur is only in the Mewtwo pack. The other four are shared across all three,
        // so on their own they would say nothing — the intersection is what does the work.
        var guess = Identify("A1-1", "A1-26", "A1-27", "A1-28", "A1-29");

        Assert.True(guess.IsCertain);
        Assert.Equal("A1:Mewtwo", guess.PackKey);
        Assert.Equal("A1", guess.Set);
        Assert.Null(guess.Note);
    }

    [Fact]
    public void EachOfTheThreePacksIsToldApart()
    {
        Assert.Equal("A1:Mewtwo", Identify("A1-2", "A1-26").PackKey);
        Assert.Equal("A1:Pikachu", Identify("A1-5", "A1-26").PackKey);
        Assert.Equal("A1:Charizard", Identify("A1-11", "A1-26").PackKey);
    }

    [Fact]
    public void AHandOfNothingButSharedCardsReportsTheCandidatesRatherThanPicking()
    {
        // This is a God Pack: five cards of the highest rarities, which are exactly the cards a set
        // shares across all its packs. Guessing one of three here would be wrong two times in three.
        var guess = Identify("A1-26", "A1-27", "A1-28", "A1-29", "A1-30");

        Assert.False(guess.IsCertain);
        Assert.Null(guess.PackKey);
        Assert.Equal(3, guess.Candidates.Count);
        Assert.Equal("A1", guess.Set);
        Assert.Contains("God Pack", guess.Note);
    }

    [Fact]
    public void TwoExclusivesFromDifferentPacksCannotBeOneOpening()
    {
        // Bulbasaur is Mewtwo-only and Caterpie is Pikachu-only. No pack holds both, which means
        // something in the picture was read wrongly — and saying so beats naming a pack.
        var guess = Identify("A1-1", "A1-5");

        Assert.False(guess.IsCertain);
        Assert.Empty(guess.Candidates);
        Assert.Equal("A1", guess.Set);
        Assert.Contains("No single pack", guess.Note);
    }

    [Fact]
    public void CardsFromDifferentSetsAreNotOnePack()
    {
        var guess = Identify("A1-1", "A2-1");

        Assert.False(guess.IsCertain);
        Assert.Null(guess.Set);
        Assert.Contains("different sets", guess.Note);
    }

    [Fact]
    public void ACardSoldInNoPackIsSetAsideRatherThanRuiningTheAnswer()
    {
        // One A1 card is listed with no pack at all. Letting it empty the intersection would report
        // "no pack fits" for a hand that plainly came from one.
        var unobtainable = Ix.BySet["A1"].First(c => !c.IsPackObtainable);
        var guess = PackIdentifier.Identify(Ix, [Card("A1-1"), Card("A1-26"), unobtainable]);

        Assert.Equal("A1:Mewtwo", guess.PackKey);
        Assert.Contains("not sold in any pack", guess.Note);
    }

    [Fact]
    public void APromoIsSetAsideToo()
    {
        // Promos list a "Vol. N" grouping, which records how a card was handed out at an event
        // rather than anything a player can open.
        var promo = Ix.BySet["PROMO-A"][0];
        var guess = PackIdentifier.Identify(Ix, [promo]);

        Assert.False(guess.IsCertain);
        Assert.Contains("no pack to name", guess.Note);
    }

    [Fact]
    public void NoCardsMeansNoGuessAndNoException()
    {
        var guess = PackIdentifier.Identify(Ix, []);

        Assert.False(guess.IsCertain);
        Assert.Null(guess.Set);
        Assert.NotNull(guess.Note);
    }

    [Fact]
    public void TheSameCardTwiceIsStillOneCard()
    {
        // A hand can hold two copies, and a duplicate must not change the intersection.
        Assert.Equal("A1:Mewtwo", Identify("A1-1", "A1-1", "A1-26").PackKey);
    }

    [Fact]
    public void OnlyArtworkMatchesAreUsedAsEvidence()
    {
        // A card placed by its position in a list says nothing about a pack, and a reveal screen has
        // nothing to place by position anyway — but the reading type is shared, so the filter has to
        // be explicit.
        var reading = new ShotReading(true, null, CardScreen.PackReveal, false,
        [
            new ShotMatch(Card("A1-1"), 0, 0, 3, true, MatchSource.Art),
            new ShotMatch(Card("A1-5"), 0, 1, -1, false, MatchSource.GridPosition),
        ], 0, []);

        // A1-5 is Pikachu-exclusive and would break the intersection if it counted.
        Assert.Equal("A1:Mewtwo", PackIdentifier.Identify(Ix, reading).PackKey);
    }

    [Fact]
    public void AWholePackIsRecognisedFromItsSize()
    {
        var rates = Snapshot.Rates();

        Assert.True(PackIdentifier.IsWholePack(rates, "A1", 5));
        Assert.False(PackIdentifier.IsWholePack(rates, "A1", 4));

        // No set named, or a set with no published rates, cannot say — and must not claim to.
        Assert.False(PackIdentifier.IsWholePack(rates, null, 5));
    }
}
