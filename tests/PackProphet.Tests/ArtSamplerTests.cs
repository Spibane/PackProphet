using PackProphet.Vision;

namespace PackProphet.Tests;

/// <summary>
/// The invariance the whole method rests on: a screenshot hands the browser a card at whatever size
/// the device drew it, while the generator is handed a 367x512 art file. If the fingerprint moved
/// when the size changed, nothing would ever match — and it would fail silently, as an unread slot
/// rather than an error.
///
/// Framing is deliberately not an invariance of this class. It samples the whole image, and finding
/// where the card sits inside a screenshot slot is the screenshot side's job, measured once from the
/// whole grid. An earlier version trimmed uniform margins here in the hope of meeting the other side
/// in the middle; the note in cardshot.js records how badly that went and why.
/// </summary>
public class ArtSamplerTests
{
    /// <summary>
    /// Synthetic art with large-scale structure, like a card and unlike noise — and with that
    /// structure where a card keeps it, in the upper middle. That matters now that only the art
    /// window is fingerprinted: a fixture whose only distinguishing feature sat in the lower half
    /// would be testing a region the sampler deliberately never looks at.
    /// </summary>
    private static byte[] Art(int w, int h, int seed = 3)
    {
        var px = new byte[w * h];

        // Inside the window: ArtSampler samples y from 0.11 to 0.48 of the card.
        var cx = 0.25 + 0.5 * (seed % 7) / 7.0;
        var cy = ArtSampler.WindowTop + (ArtSampler.WindowBottom - ArtSampler.WindowTop)
                                        * (0.2 + 0.6 * (seed % 5) / 5.0);

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var fx = (double)x / w;
            var fy = (double)y / h;
            var v = 40 + 170 * Math.Exp(-14 * ((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy)))
                       + 40 * fy + 25 * Math.Sin(fx * (2 + seed % 5) * Math.PI);
            px[y * w + x] = (byte)Math.Clamp(v, 0, 255);
        }

        return px;
    }

    /// <summary>Nearest-neighbour upscale, which is the worst case: it adds no information to average away.</summary>
    private static byte[] Scaled(byte[] art, int w, int h, int factor)
    {
        var px = new byte[w * factor * h * factor];
        for (var y = 0; y < h * factor; y++)
        for (var x = 0; x < w * factor; x++)
            px[y * w * factor + x] = art[y / factor * w + x / factor];
        return px;
    }

    /// <summary>
    /// How far apart the same art may fingerprint when only its size changed. Not zero: the cell
    /// boundaries of the sampling grid land on different sub-pixel positions at different sizes, so
    /// two cells whose averages are all but tied can order differently and flip a bit. A handful of
    /// bits out of 128 is the shape of that effect, and it is what
    /// <see cref="ArtHashTable.MaxDistance"/> exists to absorb — measured at 0 to 1 bit here, with
    /// the tolerance set well above that so a real regression is still caught. Confirmed against
    /// real art too: thirteen A1 cards, decoded in a browser and fingerprinted by the JavaScript
    /// half, landed 0 bits from what this generated for the same files.
    /// </summary>
    private const int ScaleTolerance = 4;

    [Fact]
    public void TheWindowIsInsetFromEveryEdge()
    {
        // The three things a screenshot adds to a card all land on its frame or a corner: the gold
        // flair border, the copy-count badge at bottom-left, and the clipped bottom row. The window
        // has to miss all of them, so it must not touch any edge.
        var (x, y, w, h) = ArtSampler.Window(367, 512);

        Assert.True(x > 0 && y > 0, "the window starts on an edge");
        Assert.True(x + w < 367, "the window reaches the right edge");

        // And specifically: it must stop short of the copy-count badge, which begins at about 0.90
        // of the card's height — measured off the real screenshot fixtures.
        Assert.True(y + h < 512 * 0.88, "the window reaches the copy-count badge");
    }

    [Fact]
    public void TheWindowScalesWithTheCardRatherThanBeingFixedInPixels()
    {
        // The same proportion of a card at any size, which is the whole reason a fixed window can
        // work across an art file and a screenshot that drew the card a third of the size.
        var big = ArtSampler.Window(367, 512);
        var small = ArtSampler.Window(367 / 3, 512 / 3);

        Assert.Equal(big.X / 3.0, small.X, 1.0);
        Assert.Equal(big.Width / 3.0, small.Width, 1.0);
    }

    [Fact]
    public void WhatHappensOutsideTheWindowDoesNotChangeTheFingerprint()
    {
        // The property the change was made for. Painting over the card's border and its bottom-left
        // corner stands in for gold flair and the copy-count badge, and must not move the hash.
        var art = Art(367, 512);
        var before = ArtSampler.Fingerprint(art, 367, 512);

        // Everything strictly outside the window, computed from the window itself so the test cannot
        // drift out of step with it: the frame all the way round, which is what flair replaces, and
        // the bottom strip, which is where the copy-count badge sits.
        var (wx, wy, ww, wh) = ArtSampler.Window(367, 512);
        var defaced = (byte[])art.Clone();

        for (var y = 0; y < 512; y++)
        for (var x = 0; x < 367; x++)
        {
            var inside = x >= wx && x < wx + ww && y >= wy && y < wy + wh;
            if (!inside) defaced[y * 367 + x] = (byte)((x * 7 + y * 3) % 256);
        }

        Assert.Equal(before, ArtSampler.Fingerprint(defaced, 367, 512));
    }

    [Fact]
    public void TheSameArtAtFourTimesTheSizeFingerprintsAlmostIdentically()
    {
        var art = Art(120, 168);
        var small = ArtSampler.Fingerprint(art, 120, 168);

        foreach (var factor in new[] { 2, 3, 4 })
        {
            var big = ArtSampler.Fingerprint(Scaled(art, 120, 168, factor), 120 * factor, 168 * factor);
            var distance = small.DistanceTo(big);

            Assert.True(distance <= ScaleTolerance,
                        $"at {factor}x the fingerprint moved {distance} bits");
        }
    }

    [Fact]
    public void DifferentArtFingerprintsDifferently()
    {
        var a = ArtSampler.Fingerprint(Art(200, 280, seed: 3), 200, 280);
        var b = ArtSampler.Fingerprint(Art(200, 280, seed: 9), 200, 280);

        Assert.True(a.DistanceTo(b) > ArtHashTable.MaxDistance,
                    $"two unrelated images landed {a.DistanceTo(b)} bits apart");
    }

    [Fact]
    public void AnImageSmallerThanItsStatedSizeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => ArtSampler.Fingerprint(new byte[10], 200, 280));
    }
}
