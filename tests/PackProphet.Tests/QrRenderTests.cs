using PackProphet.Deck;

namespace PackProphet.Tests;

public class QrRenderTests
{
    /// <summary>
    /// The one thing that matters about this output: the game's scanner has to read it. That
    /// cannot be asserted here, so these tests pin the properties a scanner depends on —
    /// square geometry, a quiet zone, and standard black-on-white polarity.
    /// </summary>
    [Fact]
    public void ProducesASquareSvgWithAQuietZone()
    {
        var code = DeckCodec.Create(Enumerable.Repeat(1, 20), [EnergyType.Fire]);

        var svg = QrRender.Svg(code, quietZone: 4);

        Assert.NotNull(svg);
        Assert.StartsWith("<svg", svg);
        Assert.EndsWith("</svg>", svg);

        var box = ViewBox(svg!);
        Assert.Equal(box.W, box.H);                     // square
        Assert.True(box.W >= 21 + 8, $"too small for a quiet zone: {box.W}");

        // A code with no quiet zone is 8 units narrower, which is how we know the margin is
        // actually being emitted rather than the value merely being accepted.
        var tight = QrRender.Svg(code, quietZone: 0);
        Assert.Equal(box.W - 8, ViewBox(tight!).W);
    }

    [Fact]
    public void PaintsBlackOnWhiteRegardlessOfTheme()
    {
        var svg = QrRender.Svg(DeckCodec.Create(Enumerable.Repeat(1, 20), []));

        // Hardcoded, deliberately: a themed inversion scans as a different code, or not at all.
        Assert.Contains("fill=\"#fff\"", svg);
        Assert.Contains("fill=\"#000\"", svg);
        Assert.DoesNotContain("var(--", svg);
    }

    [Fact]
    public void DarkModulesAreEmittedAsRuns()
    {
        var svg = QrRender.Svg(DeckCodec.Create(Enumerable.Repeat(1, 20), []))!;

        var rects = svg.Split("<rect").Length - 1;
        Assert.True(rects > 1, "no modules were drawn");

        // Runs, not one rect per module: the finder patterns alone are solid 7-wide bars, so a
        // per-module renderer would emit hundreds more.
        var box = ViewBox(svg);
        Assert.True(rects < box.W * box.H / 4, $"{rects} rects looks per-module, not per-run");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RefusesEmptyInputRatherThanDrawingNothing(string? text)
    {
        // A blank QR would look like a working control and scan as nothing at all.
        Assert.Null(QrRender.Svg(text!));
    }

    private static (int W, int H) ViewBox(string svg)
    {
        var start = svg.IndexOf("viewBox=\"", StringComparison.Ordinal) + 9;
        var parts = svg[start..svg.IndexOf('"', start)].Split(' ');
        return (int.Parse(parts[2]), int.Parse(parts[3]));
    }
}
