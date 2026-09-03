namespace PackProphet.Tests;

public class RarityGlyphTests
{
    private static Data.RarityLadder Ladder => Snapshot.Index().Ladder;

    [Fact]
    public void GlyphsRepeatOncePerTier_AsTheGameDrawsThem()
    {
        string For(string code) => Ladder.Rungs[Ladder.IndexOf(code)!.Value].Glyphs;

        Assert.Equal("◆", For("C"));
        Assert.Equal("◆◆", For("U"));
        Assert.Equal("◆◆◆", For("R"));
        Assert.Equal("◆◆◆◆", For("RR"));
        Assert.Equal("★", For("AR"));
        Assert.Equal("★★", For("SR"));
        Assert.Equal("★★", For("SAR"));   // shares the 2-star rung
        Assert.Equal("★★★", For("IM"));
        Assert.Equal("✦", For("S"));
        Assert.Equal("✦✦", For("SSR"));
        Assert.Equal("♛", For("UR"));
    }

    [Fact]
    public void EveryRungsLabel_CarriesTheSameMarkItsGlyphsDo()
    {
        // The completion plan's chips and its collapsed summary show Symbol, not Glyphs, so a
        // rung whose label is only a word is a chip with nothing to scan for. Two of the ten
        // were: Crown was the bare word, and the second Shiny rung read "Shiny 2★" -- a star,
        // which is a different family drawn in a different colour.
        Assert.All(Ladder.Rungs, r =>
            Assert.Contains(r.Glyphs[..1], r.Symbol, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryRungHasGlyphsAndAColourClass()
    {
        Assert.All(Ladder.Rungs, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Glyphs));
            Assert.Contains(r.GlyphClass, new[] { "diamond", "star", "shiny", "crown" });
        });
    }
}
