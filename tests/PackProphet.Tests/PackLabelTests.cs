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
    [InlineData("A1:Charizard", "GA: Charizard")]
    [InlineData("A1:Mewtwo", "GA: Mewtwo")]
    [InlineData("A2:Dialga", "STS: Dialga")]
    [InlineData("A3:Solgaleo", "CG: Solgaleo")]
    [InlineData("A4:Lugia", "WSS: Lugia")]
    [InlineData("B1:Mega Blaziken", "MR: Mega Blaziken")]
    public void PackLabels(string packKey, string expected) =>
        Assert.Equal(expected, Snapshot.Index().PackLabel(packKey, Sets));

    [Fact]
    public void UncataloguedSetKeepsThePackName() =>
        Assert.Equal("Deluxe", Snapshot.Index().PackLabel("B4b:Deluxe", Sets));

    [Fact]
    public void EveryMultiPackPrefixNamesOneSet()
    {
        var index = Snapshot.Index();
        var setsPerPrefix = index.OpenablePackKeys
            .Where(k => index.PackLabel(k, Sets).Contains(':'))
            .GroupBy(k => index.PackLabel(k, Sets).Split(':')[0])
            .Select(g => g.Select(k => k.Split(':')[0]).Distinct().Count())
            .ToArray();

        Assert.NotEmpty(setsPerPrefix);
        Assert.All(setsPerPrefix, n => Assert.Equal(1, n));
    }
}
