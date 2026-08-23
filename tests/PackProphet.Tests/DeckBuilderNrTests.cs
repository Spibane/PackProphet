using PackProphet.Deck;

namespace PackProphet.Tests;

public class DeckBuilderNrTests
{
    [Theory]
    // The two documented reference cases from the reverse-engineered format.
    [InlineData("cPK_10_000010_00_FUSHIGIDANE_C.webp", 1)]
    [InlineData("cTR_10_000080_00_KAINOKASEKI_C.webp", 1_000_008)]
    public void RecoversTheDocumentedReferenceCases(string image, int expected) =>
        Assert.Equal(expected, DeckBuilderNr.FromImage(image));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-card.png")]
    [InlineData("cPK_10_00001_00_X.webp")]      // five digits, not six
    [InlineData("cPK_10_000011_00_X.webp")]     // not a multiple of ten
    [InlineData("cPK_10_000000_00_X.webp")]     // would be id zero
    public void MalformedNames_ReturnNullRatherThanThrowing(string? image) =>
        // One unparseable filename upstream should cost that card's deck support, not
        // take down the whole data load.
        Assert.Null(DeckBuilderNr.FromImage(image));

    [Fact]
    public void TrainersAndPokemon_ShareNumbersUntilTheOffsetSeparatesThem()
    {
        // Both namespaces start at 1; the offset is what keeps them distinct in one int.
        var pokemon = DeckBuilderNr.FromImage("cPK_10_000010_00_A.webp");
        var trainer = DeckBuilderNr.FromImage("cTR_10_000010_00_B.webp");

        Assert.Equal(1, pokemon);
        Assert.Equal(DeckBuilderNr.TrainerOffset + 1, trainer);
        Assert.NotEqual(pokemon, trainer);

        Assert.False(DeckBuilderNr.IsTrainer(pokemon!.Value));
        Assert.True(DeckBuilderNr.IsTrainer(trainer!.Value));
    }

    [Fact]
    public void EveryCardInTheDataset_YieldsANumber()
    {
        // A gap here means deck import silently cannot match those cards.
        var missing = Snapshot.Index().WithoutDeckBuilderNr;
        Assert.Empty(missing.Select(c => $"{c.Key} {c.Name} ({c.Image})"));
    }

    [Fact]
    public void AlternateArts_ShareOneIdentity()
    {
        // The whole reason deck membership is compared by identity and never by
        // set+number: owning any printing satisfies the slot.
        var shared = Snapshot.Index().ByDeckBuilderNr
            .Where(kv => kv.Value.Count > 1)
            .ToList();

        Assert.NotEmpty(shared);

        foreach (var (_, printings) in shared.Take(50))
        {
            // Same card, so one name — but genuinely different printings.
            Assert.Single(printings.Select(p => p.Name).Distinct());
            Assert.True(printings.Select(p => p.Key).Distinct().Count() > 1);
        }
    }
}
