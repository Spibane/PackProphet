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
    public void EveryRungHasGlyphsAndAColourClass()
    {
        Assert.All(Ladder.Rungs, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Glyphs));
            Assert.Contains(r.GlyphClass, new[] { "diamond", "star", "shiny", "crown" });
        });
    }
}
