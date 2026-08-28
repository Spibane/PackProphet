using PackProphet.Vision;

namespace PackProphet.Tests;

public class ArtHashTests
{
    /// <summary>
    /// The grid the golden vector is computed from. Arbitrary, and deliberately not symmetrical in
    /// either direction, so a transposed or reversed bit layout cannot pass by coincidence.
    /// </summary>
    private static double[] GoldenGrid()
    {
        var grid = new double[ArtHash.Grid * ArtHash.Grid];
        for (var j = 0; j < ArtHash.Grid; j++)
        for (var i = 0; i < ArtHash.Grid; i++)
            grid[j * ArtHash.Grid + i] = (i * 29 + j * 53) % 251;
        return grid;
    }

    [Fact]
    public void TheGoldenVectorMatchesTheBrowserImplementation()
    {
        // The fingerprints in the table are generated here, from the art files. The fingerprints
        // looked up against them are computed in the browser, from a screenshot, by hash() in
        // wwwroot/js/cardshot.js. Two implementations of one algorithm, and they have to agree bit
        // for bit — a hash computed a different way is not a near miss, it is a different card. So
        // one grid is pinned to one value here, and the same vector is written into cardshot.js as
        // a comment. If this ever fails, the two have drifted and both sides need looking at.
        Assert.Equal("1040000208104000186080030c106080", ArtHash.From(GoldenGrid()).ToString());
    }

    [Fact]
    public void HexRoundTripsThroughToStringAndTryParse()
    {
        var hash = ArtHash.From(GoldenGrid());

        Assert.True(ArtHash.TryParse(hash.ToString(), out var back));
        Assert.Equal(hash, back);

        Assert.False(ArtHash.TryParse("", out _));
        Assert.False(ArtHash.TryParse("1040000208104000186080030c1060", out _));
        Assert.False(ArtHash.TryParse("zzzz000208104000186080030c106080", out _));
    }

    [Fact]
    public void DistanceCountsBothHalvesAndSaturatesAtTheBitCount()
    {
        var a = new ArtHash(0, 0);

        Assert.Equal(0, a.DistanceTo(a));
        Assert.Equal(ArtHash.Bits, a.DistanceTo(new ArtHash(ulong.MaxValue, ulong.MaxValue)));
        Assert.Equal(64, a.DistanceTo(new ArtHash(ulong.MaxValue, 0)));
        Assert.Equal(2, a.DistanceTo(new ArtHash(1, 1)));
    }

    [Fact]
    public void AGridWithNoGradientsIsRecognisedAsFeatureless()
    {
        // A flat region of screenshot — an empty slot, a letterbox bar, a panel. It sits about
        // equally far from thousands of entries, so it must never reach the search.
        var flat = ArtHash.From(new double[ArtHash.Grid * ArtHash.Grid]);

        Assert.True(flat.IsFeatureless);
        Assert.False(ArtHash.From(GoldenGrid()).IsFeatureless);
    }

    [Fact]
    public void AGridOfTheWrongSizeIsRejectedRatherThanTruncated()
    {
        Assert.Throws<ArgumentException>(() => ArtHash.From(new double[64]));
    }
}
