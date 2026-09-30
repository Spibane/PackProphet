using PackProphet.Data;

namespace PackProphet.Tests;

/// <summary>
/// A pack is shown under its set's name when the set has one pack, and under its own name after the
/// set's initials when it has several. Upstream's raw names are shortened for some single-pack sets
/// and not others.
/// </summary>
public class PackLabelTests
{
    private static readonly SetCatalog Sets =
        new(Snapshot.PublishedSets(), Snapshot.Index().BySet.Keys);

    [Theory]
    [InlineData("B2a:Paldean", "Paldean Wonders")]
    [InlineData("A4a:Secluded", "Secluded Springs")]
    [InlineData("A1a:Mew", "Mythical Island")]
    [InlineData("B2:Gardevoir", "Fantastical Parade")]
    [InlineData("B3:Pulsing Aura", "Pulsing Aura")]
    [InlineData("A4b:Deluxe", "Deluxe Pack: ex")]
    [InlineData("B4b:Deluxe Pack Mega", "Deluxe Pack: Mega")]
    [InlineData("A1:Charizard", "GA: Charizard")]
    [InlineData("A1:Mewtwo", "GA: Mewtwo")]
    [InlineData("A2:Dialga", "STS: Dialga")]
    [InlineData("A3:Solgaleo", "CG: Solgaleo")]
    [InlineData("A4:Lugia", "WSS: Lugia")]
    [InlineData("B1:Mega Blaziken", "MR: Mega Blaziken")]
    public void PackLabels(string packKey, string expected) =>
        Assert.Equal(expected, Snapshot.Index().PackLabel(packKey, Sets));

    /// <summary>
    /// A pack key for a set neither the card index nor the catalogue has heard of.
    ///
    /// This used "B4b:Deluxe", written while B4b was announced and unpublished. 2.11.0 published
    /// it, as "Deluxe Pack: Mega" with a pack of its own name, so the key stopped being unknown
    /// and the label became the set's. An invented code stays unknown.
    /// </summary>
    private const string Unpublished = "Z9z:Deluxe";

    [Fact]
    public void UncataloguedSetKeepsThePackName() =>
        Assert.Equal("Deluxe", Snapshot.Index().PackLabel(Unpublished, Sets));

    [Fact]
    public void EveryMultiPackPrefixNamesOneSet()
    {
        // Chosen by how many packs a set sells rather than by a colon in the label. Set names can
        // carry a colon of their own -- A4b is "Deluxe Pack: ex", B4b "Deluxe Pack: Mega" -- and
        // those two single-pack labels read as one "Deluxe Pack" prefix naming two sets, which is
        // a fault in the test rather than in the labels. The prefix rule applies only where a set
        // has several packs.
        var index = Snapshot.Index();
        var multiPack = index.OpenablePackKeys
            .GroupBy(k => k.Split(':')[0])
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        var setsPerPrefix = index.OpenablePackKeys
            .Where(k => multiPack.Contains(k.Split(':')[0]))
            .GroupBy(k => index.PackLabel(k, Sets).Split(':')[0])
            .Select(g => g.Select(k => k.Split(':')[0]).Distinct().Count())
            .ToArray();

        Assert.NotEmpty(setsPerPrefix);
        Assert.All(setsPerPrefix, n => Assert.Equal(1, n));
    }
}
