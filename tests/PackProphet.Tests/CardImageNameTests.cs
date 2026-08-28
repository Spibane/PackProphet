using PackProphet.Domain;

namespace PackProphet.Tests;

/// <summary>
/// The artwork filename parser, which replaced two regular expressions that described the same
/// format in two places.
///
/// The rejections matter more than the successes. The old patterns anchored the id group at
/// exactly six digits, so a filename whose format had moved failed to match and the card lost its
/// deck support — the safe outcome. Hand-written scanning could easily be sloppier and decode such
/// a name as a different card instead, which is why the shapes that must NOT parse are pinned
/// here alongside the ones that must.
/// </summary>
public class CardImageNameTests
{
    private static CardImageName Parse(string image)
    {
        Assert.True(CardImageName.TryParse(image, out var name), $"should parse: {image}");
        return name;
    }

    [Theory]
    [InlineData("cPK_10_000010_00_FUSHIGIDANE_C.webp", "PK", 10, 0)]
    [InlineData("cTR_10_000080_00_KAINOKASEKI_C.webp", "TR", 80, 0)]
    // A non-zero variant: the foil or alternate-art printing.
    [InlineData("cPK_10_000010_01_FUSHIGIDANE_C.webp", "PK", 10, 1)]
    [InlineData("cPK_10_001234_12_SOMETHING_R.webp", "PK", 1234, 12)]
    public void ReadsKindIdAndVariant(string image, string kind, int id, int variant)
    {
        var name = Parse(image);
        Assert.Equal(kind, name.Kind);
        Assert.Equal(id, name.Id);
        Assert.Equal(variant, name.Variant);
    }

    [Theory]
    [InlineData("")]
    [InlineData("PK_10_000010_00_X.webp")]      // no leading c
    [InlineData("c_10_000010_00_X.webp")]       // no kind
    [InlineData("cpk_10_000010_00_X.webp")]     // kind must be capitals
    [InlineData("cPK_xx_000010_00_X.webp")]     // middle group is not digits
    [InlineData("cPK_10_00010_00_X.webp")]      // five-digit id
    [InlineData("cPK_10_0000100_00_X.webp")]    // seven-digit id
    [InlineData("cPK_10_000010")]               // truncated before the id separator
    public void RefusesAnythingThatIsNotTheFormat(string image) =>
        Assert.False(CardImageName.TryParse(image, out _), $"should not parse: {image}");

    [Fact]
    public void RefusesNull() => Assert.False(CardImageName.TryParse(null, out _));

    [Fact]
    public void AVariantWithNoTrailingSeparatorIsTheZerothPrinting()
    {
        // The old pattern required a separator after the variant, so a name ending there matched
        // nothing and VariantIndex fell back to 0. Same answer, reached deliberately.
        Assert.Equal(0, Parse("cPK_10_000010_07").Variant);
    }

    [Theory]
    // The id group is the deck-builder number times ten.
    [InlineData("cPK_10_000010_00_X.webp", 1)]
    [InlineData("cTR_10_000080_00_X.webp", 8)]
    public void DeckIdIsTheIdGroupOverTen(string image, int expected) =>
        Assert.Equal(expected, Parse(image).DeckId);

    [Theory]
    // Not a tenfold, so the format moved and there is no id to report.
    [InlineData("cPK_10_000011_00_X.webp")]
    // Zero is not an id either.
    [InlineData("cPK_10_000000_00_X.webp")]
    public void DeckIdIsNullWhenTheGroupIsNotATenfoldId(string image) =>
        Assert.Null(Parse(image).DeckId);

    [Fact]
    public void EveryCardInTheRealSnapshotStillParses()
    {
        // The guard that actually matters: the parser has to agree with the shipped data, not
        // just with the examples above.
        var cards = Snapshot.Index().All;
        Assert.NotEmpty(cards);

        var unreadable = cards
            .Where(c => !string.IsNullOrEmpty(c.Image) && !CardImageName.TryParse(c.Image, out _))
            .Select(c => c.Image)
            .ToArray();

        Assert.Empty(unreadable);
    }
}
