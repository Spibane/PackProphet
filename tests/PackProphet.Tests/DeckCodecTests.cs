using PackProphet.Deck;

namespace PackProphet.Tests;

public class DeckCodecTests
{
    [Fact]
    public void RoundTripsASimpleDeck()
    {
        var code = DeckCodec.Create([1, 1, 4, 4], [EnergyType.Fire, EnergyType.Grass]);
        var parsed = DeckCodec.Parse(code);

        Assert.Equal([1, 1, 4, 4], parsed.Pokemon);
        Assert.Empty(parsed.Trainers);
        Assert.Equal([EnergyType.Fire, EnergyType.Grass], parsed.Energies);
        Assert.Equal(4, parsed.CardCount);
    }

    [Fact]
    public void SeparatesTrainersFromPokemonIntoTheirOwnSegments()
    {
        var trainer = DeckBuilderNr.TrainerOffset + 8;
        var code = DeckCodec.Create([4, trainer, 1], []);
        var parsed = DeckCodec.Parse(code);

        Assert.Equal([trainer], parsed.Trainers);
        Assert.Equal([1, 4], parsed.Pokemon);       // emitted ascending
    }

    [Fact]
    public void EncodingIsStable_SoTheSameDeckAlwaysProducesTheSameCode()
    {
        // Order-independence matters: the builder should not produce a different code just
        // because cards were added in a different order.
        var a = DeckCodec.Create([4, 1, 4, 1], [EnergyType.Water]);
        var b = DeckCodec.Create([1, 1, 4, 4], [EnergyType.Water]);
        Assert.Equal(a, b);
    }

    [Fact]
    public void RoundTripsAFullTwentyCardDeck()
    {
        var cards = new List<int>();
        for (var i = 1; i <= 10; i++) { cards.Add(i); cards.Add(i); }
        var trainers = new List<int> { DeckBuilderNr.TrainerOffset + 1, DeckBuilderNr.TrainerOffset + 1 };

        var code = DeckCodec.Create([.. cards, .. trainers],
            [EnergyType.Psychic, EnergyType.Metal, EnergyType.Darkness]);
        var parsed = DeckCodec.Parse(code);

        Assert.Equal(22, parsed.CardCount);
        Assert.Equal(2, parsed.Trainers.Count);
        Assert.Equal(3, parsed.Energies.Count);
        Assert.Equal(code, DeckCodec.Create(parsed.AllCards, parsed.Energies));
    }

    [Fact]
    public void EveryEnergyTypeRoundTrips()
    {
        foreach (var e in Enum.GetValues<EnergyType>())
        {
            var parsed = DeckCodec.Parse(DeckCodec.Create([1], [e]));
            Assert.Equal([e], parsed.Energies);
        }
    }

    [Fact]
    public void EnergyIdsMatchTheDocumentedFormat()
    {
        // Grass 1 .. Metal 8. Wrong ids would produce codes the game silently misreads.
        Assert.Equal(1, DeckCodec.EnergyIds[EnergyType.Grass]);
        Assert.Equal(2, DeckCodec.EnergyIds[EnergyType.Fire]);
        Assert.Equal(3, DeckCodec.EnergyIds[EnergyType.Water]);
        Assert.Equal(4, DeckCodec.EnergyIds[EnergyType.Lightning]);
        Assert.Equal(5, DeckCodec.EnergyIds[EnergyType.Psychic]);
        Assert.Equal(6, DeckCodec.EnergyIds[EnergyType.Fighting]);
        Assert.Equal(7, DeckCodec.EnergyIds[EnergyType.Darkness]);
        Assert.Equal(8, DeckCodec.EnergyIds[EnergyType.Metal]);
    }

    [Fact]
    public void EmptySegmentsAreASingleZeroByte()
    {
        var code = DeckCodec.Create([], []);
        var raw = Convert.FromBase64String(code);
        Assert.Equal(new byte[] { 0, 0, 0 }, raw);   // no trainers, no pokemon, no energy
    }

    [Fact]
    public void CardValuesAreThreeByteBigEndianTimesTen()
    {
        var raw = Convert.FromBase64String(DeckCodec.Create([1], []));
        // trainers=0, pokemon count=1, then 0x00 0x00 0x0A (= 10 = 1 x 10), energy count=0
        Assert.Equal(new byte[] { 0, 1, 0x00, 0x00, 0x0A, 0 }, raw);
    }

    [Fact]
    public void RoundTripsTheLargestNumberThatFitsInThreeBytes()
    {
        var max = 0xFFFFFF / 10;    // 1,677,721 — comfortably above any real trainer id
        var parsed = DeckCodec.Parse(DeckCodec.Create([max], []));
        Assert.Equal([max], parsed.Trainers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 !!")]
    public void MalformedInput_IsRejectedWithFormatException(string code) =>
        Assert.Throws<FormatException>(() => DeckCodec.Parse(code));

    [Fact]
    public void TruncatedCode_IsRejectedRatherThanSilentlyMisread()
    {
        // A code claiming two cards but carrying only one must fail loudly: silently
        // importing half a deck is worse than refusing it.
        var truncated = Convert.ToBase64String(new byte[] { 0, 2, 0x00, 0x00, 0x0A });
        Assert.Throws<FormatException>(() => DeckCodec.Parse(truncated));
    }

    [Fact]
    public void CardValueNotAMultipleOfTen_IsRejectedAsAFormatChange()
    {
        var odd = Convert.ToBase64String(new byte[] { 0, 1, 0x00, 0x00, 0x0B, 0 });
        var ex = Assert.Throws<FormatException>(() => DeckCodec.Parse(odd));
        Assert.Contains("multiple of ten", ex.Message);
    }

    [Fact]
    public void TooManyEnergyTypes_AreRejectedOnBothEncodeAndDecode()
    {
        Assert.Throws<ArgumentException>(() => DeckCodec.Create([1],
            [EnergyType.Fire, EnergyType.Water, EnergyType.Grass, EnergyType.Metal]));

        var overLimit = Convert.ToBase64String(new byte[] { 0, 1, 0, 0, 10, 4, 1, 2, 3, 4 });
        Assert.Throws<FormatException>(() => DeckCodec.Parse(overLimit));
    }

    [Fact]
    public void UnknownEnergyId_IsSurfacedRatherThanThrowing()
    {
        // A new energy type must not make every deck using it unimportable.
        var withUnknown = Convert.ToBase64String(new byte[] { 0, 1, 0, 0, 10, 2, 2, 99 });
        var parsed = DeckCodec.Parse(withUnknown);

        Assert.Equal([EnergyType.Fire], parsed.Energies);
        Assert.Equal([99], parsed.UnknownEnergyIds);
    }

    [Fact]
    public void TryParse_ReturnsNullInsteadOfThrowing()
    {
        Assert.Null(DeckCodec.TryParse("nonsense!!"));
        Assert.NotNull(DeckCodec.TryParse(DeckCodec.Create([1], [])));
    }

    [Fact]
    public void NonPositiveCardNumbers_AreRejected() =>
        Assert.Throws<ArgumentException>(() => DeckCodec.Create([0], []));

    [Fact]
    public void RoundTripsEveryRealCardIdentityInTheDatabase()
    {
        // The strongest guarantee available without the game itself: every identity the
        // card database contains survives an encode/decode cycle intact.
        var nrs = Snapshot.Index().ByDeckBuilderNr.Keys.ToArray();
        Assert.NotEmpty(nrs);

        foreach (var chunk in nrs.Chunk(20))
        {
            var code = DeckCodec.Create(chunk, [EnergyType.Fire]);
            var parsed = DeckCodec.Parse(code);
            Assert.Equal(chunk.OrderBy(n => n), parsed.AllCards.OrderBy(n => n));
        }
    }

    [Fact]
    public void TrainersAndPokemonSurviveTogether_UsingRealData()
    {
        var ix = Snapshot.Index();
        var trainerNr = ix.ByDeckBuilderNr.Keys.First(DeckBuilderNr.IsTrainer);
        var pokemonNr = ix.ByDeckBuilderNr.Keys.First(n => !DeckBuilderNr.IsTrainer(n));

        var parsed = DeckCodec.Parse(DeckCodec.Create([trainerNr, pokemonNr, trainerNr], []));

        Assert.Equal([trainerNr, trainerNr], parsed.Trainers);
        Assert.Equal([pokemonNr], parsed.Pokemon);
    }
}
