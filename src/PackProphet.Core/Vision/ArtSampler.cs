namespace PackProphet.Vision;

/// <summary>
/// Reduces a card to the <see cref="ArtHash.Grid"/>-square luma grid a fingerprint is computed from,
/// by box-averaging its <em>art window</em> — the illustration panel, not the whole card.
///
/// This is the generator's half of a mirrored pair; the other is <c>measure()</c> in
/// wwwroot/js/cardshot.js, which does the same to a card cut out of a screenshot. Both have to
/// arrive at the same grid for the same card, and three things go into that.
///
/// <list type="bullet">
/// <item><b>Resolution.</b> The art file is 367x512 and the same card in a screenshot might be sixty
/// pixels wide or six hundred; averaging over the whole of each cell, rather than sampling a point
/// in it, gives the same grid from all of them. Measured: the same art at 2x, 3x and 4x fingerprints
/// within a bit of itself.</item>
///
/// <item><b>Framing.</b> Not handled here, deliberately. Finding where a card sits inside a
/// screenshot belongs to the screenshot. An earlier version trimmed uniform margins off both sides
/// hoping to meet in the middle, and failed exactly as badly as content-dependent framing deserves
/// to — see the note in cardshot.js.</item>
///
/// <item><b>What a screenshot adds to a card.</b> This is why only the window is sampled, and it was
/// learned the hard way: sampling the whole card recognised <em>one real card in nine</em> on an
/// actual phone screenshot while scoring 0 bits against screenshots built from the artwork files.
/// A card on screen is not the artwork file. The game draws a gold flair border over any card held
/// ten times or more, prints a copy-count badge across the bottom-left corner, and clips the bottom
/// row at the edge of the screen. Every one of those lands on the card's border or corner, so all
/// three miss a window that is inset from the edges — and so does a few pixels of error in locating
/// the card, which no detector avoids entirely. The cost is discriminative area, and the
/// illustration is the most distinctive part of a card anyway.</item>
/// </list>
///
/// The window is recorded in <see cref="WindowLeft"/> and friends, and the evidence for the change is
/// in KNOWN-ISSUES.md.
/// </summary>
public static class ArtSampler
{
    /// <summary>
    /// Rec. 601 luma. The exact coefficients do not matter to a hash built from the signs of
    /// differences; that both implementations use these ones does.
    /// </summary>
    public static byte Luma(byte r, byte g, byte b) => (byte)((r * 77 + g * 150 + b * 29) >> 8);

    /// <summary>
    /// The window, as fractions of the card's width and height: everything but the outer frame and
    /// the bottom strip. Two things have to stay outside it and both are measured, not guessed.
    ///
    /// The <b>frame</b>, because gold flair replaces it on any card held ten times or more. The
    /// <b>bottom sixth</b>, because the copy-count badge starts at about 0.90 of the card's height,
    /// so a bound of 0.85 clears it.
    ///
    /// Measured against the real screenshot in tests/…/fixtures/IMG_1152.jpeg, which holds three
    /// flair cards and three plain ones. This window puts all six within 4 to 12 bits of their
    /// artwork files, the flair cards at 7, 12 and 7 — so flair no longer decides anything.
    ///
    /// A much tighter window was tried first — the illustration panel alone, 0.10-0.90 by
    /// 0.11-0.48 — on the theory that a smaller window is a safer one. It matched about as well
    /// (4 to 10 bits) and was worse in a way that mattered: a <b>foil printing differs from its
    /// plain twin mostly outside the panel</b>, so under the tight window the two sat 0 to 2 bits
    /// apart and became one indistinguishable answer for 58 cards. This window puts them 18 to 25
    /// bits apart. Bigger turned out to be safer, for a reason no amount of reasoning about flair
    /// would have found.
    ///
    /// Note what insetting does <em>not</em> buy: robustness to a badly located card. Shifting the
    /// box by 8px on a 192px card costs about 40 bits either way, whichever window is used, because
    /// a shift moves the sampling grid regardless of where its edges are. Locating the card to
    /// within about 2px is the detector's job and cannot be papered over here.
    /// </summary>
    public const double WindowLeft = 0.06;
    public const double WindowRight = 0.94;
    public const double WindowTop = 0.06;
    public const double WindowBottom = 0.85;

    /// <summary>The art window's pixel box within a card of the given size.</summary>
    public static (int X, int Y, int Width, int Height) Window(int width, int height)
    {
        var x = (int)Math.Round(width * WindowLeft);
        var y = (int)Math.Round(height * WindowTop);
        var w = Math.Max(1, (int)Math.Round(width * WindowRight) - x);
        var h = Math.Max(1, (int)Math.Round(height * WindowBottom) - y);
        return (x, y, w, h);
    }

    /// <summary>
    /// Fingerprints one card, given as one luma byte per pixel, row-major, spanning the whole card
    /// edge to edge. Only the art window is sampled.
    /// </summary>
    public static ArtHash Fingerprint(ReadOnlySpan<byte> luma, int width, int height)
    {
        if (width <= 0 || height <= 0 || luma.Length < width * height)
            throw new ArgumentException("luma is smaller than the stated dimensions", nameof(luma));

        var (x, y, w, h) = Window(width, height);
        return ArtHash.From(Sample(luma, width, x, y, w, h));
    }

    private static double[] Sample(
        ReadOnlySpan<byte> luma, int width, int x0, int y0, int w, int h)
    {
        const int n = ArtHash.Grid;
        var grid = new double[n * n];

        for (var j = 0; j < n; j++)
        {
            var ya = y0 + j * h / n;
            var yb = Math.Max(ya + 1, y0 + (j + 1) * h / n);

            for (var i = 0; i < n; i++)
            {
                var xa = x0 + i * w / n;
                var xb = Math.Max(xa + 1, x0 + (i + 1) * w / n);

                double sum = 0;
                var count = 0;
                for (var y = ya; y < yb; y++)
                for (var x = xa; x < xb; x++) { sum += luma[y * width + x]; count++; }

                grid[j * n + i] = count > 0 ? sum / count : 0;
            }
        }

        return grid;
    }
}
