namespace PackProphet.Vision;

/// <summary>
/// A Pokémon's energy type, read off the badge the game prints in a card's top-right corner.
///
/// Why the badge and not the card's colour
/// ==============================================================================================
/// This exists as a fallback for a set whose card DETAIL has not been published yet: the card data
/// arrives from a CDN within days of a release and the detail table can be weeks behind it, so a
/// new set's type column is blank while its art is already on screen. The art is the only thing
/// available at that point, so the type has to come out of the art or not at all.
///
/// The obvious reading is the card's background colour, and it does not work. Measured over 140
/// cards balanced across all ten types, leave-one-out:
///
///     median of a ring of frame samples          74.5%   Metal read as Colorless 10/14
///     modal colour outside the illustration      worse   everything drifts to Colorless, because
///                                                        the biggest region on a card is pale
///                                                        text and not the type accent
///     most saturated quarter of the frame        73.6%   Metal fixed, Dragon collapsed into
///                                                        Lightning 10/14 -- both gold-yellow
///     the badge, sampled across its whole disc   96.4%   Colorless read as Metal 5/14
///     the badge's FILL RING                     100.0%   every type, every card
///
/// The frame is the wrong thing to look at on every count: it is large, it is perturbed by
/// artwork and foil treatments, and the palette has hue-adjacent pairs -- Dragon and Lightning are
/// both gold, Metal and Colorless are both near-neutral, Darkness and Water are both dark and
/// cool. The badge is the opposite of all of that, and it is what the game prints BECAUSE it
/// identifies the type.
///
/// The fill ring is the last refinement and it is what closes the final pair. Sampling the whole
/// disc averages the coloured fill together with the glyph drawn on top of it, and for Colorless
/// and Metal -- whose fills are nearly the same near-neutral -- the answer then depends on how
/// much ink their star and gear happen to cover. The annulus at 0.78 of the radius is inside the
/// rim and outside the glyph, so it is only ever the fill.
/// </summary>
/// <param name="Type">
/// The energy name as the card data spells it: Grass, Fire, Water, Lightning, Psychic, Fighting,
/// Darkness, Metal, Dragon, Colorless.
/// </param>
/// <param name="Distance">
/// How far the sampled fill was from that type's reference colour, in CIELAB. Reported so a caller
/// can be stricter than <see cref="TypeBadge.SameTypeWithin"/> if it wants to.
/// </param>
public readonly record struct TypeBadgeReading(string Type, double Distance);

public static class TypeBadge
{
    /// <summary>
    /// Where the badge is, as fractions of the card's size. Read off a magnified header crop with
    /// a fractional grid drawn over it, not derived from a detector.
    ///
    /// A detector was tried and abandoned. Searching a band for "a circular region of uniform
    /// colour bounded by an edge" wandered between 0.819 and 0.960 and produced a second, false
    /// badge on 136 of 140 cards -- because a card header contains plenty of round, flat, bounded
    /// things. The position does not actually vary: the game renders these deterministically, and
    /// the spread in an earlier attempt was a bad Y estimate of mine rather than the badge moving.
    ///
    /// The radius is a fraction of the card's WIDTH. The Y offsets are scaled by the aspect ratio
    /// so the sampled circle is round in pixels rather than in fractions.
    /// </summary>
    public const double CentreX = 0.915;

    public const double CentreY = 0.056;

    public const double Radius = 0.030;

    /// <summary>
    /// The annulus that is only ever the fill: inside the rim, outside the glyph. Moving it out
    /// towards the rim or in towards the glyph is what costs Colorless against Metal.
    /// </summary>
    public const double FillRing = 0.78;

    /// <summary>
    /// How far a fill may be from a reference colour and still be called that type.
    ///
    /// Measured rather than chosen. Across 140 cards the fill landed a median of 0.9 and at worst
    /// 3.7 from its own type's centroid, while the closest two references -- Colorless and Metal --
    /// sit about 14 apart. Four accepts every real badge with the nearest wrong answer three times
    /// further away than the threshold.
    /// </summary>
    public const double SameTypeWithin = 4.0;

    /// <summary>
    /// The ten reference fills, as CIELAB centroids of 14 cards each, sampled from the fill ring.
    ///
    /// The sRGB each was measured at, for anyone checking these by eye against a card:
    /// Colorless #C3C1BF, Darkness #334A53, Dragon #6A6125, Fighting #9F5E32, Fire #AD4E40,
    /// Grass #457234, Lightning #EACC4A, Metal #9D9B93, Psychic #806280, Water #2E7FA1.
    ///
    /// Worst distance from a card to its own centroid, per type: Psychic 1.1, Metal 1.3, Darkness
    /// 1.9, Dragon 1.8, Colorless 2.1, Lightning 2.2, Fighting 2.3, Grass 2.4, Water 3.2, Fire 3.7.
    /// </summary>
    private static readonly (string Type, double L, double A, double B)[] Palette =
    [
        ("Colorless", 76.81,   0.28,   1.18),
        ("Darkness",  28.92,  -5.15,  -6.69),
        ("Dragon",    39.43,  -3.56,  30.02),
        ("Fighting",  45.36,  21.73,  32.42),
        ("Fire",      45.70,  36.12,  28.95),
        ("Grass",     41.83, -24.51,  25.57),
        ("Lightning", 82.72,  -0.09,  62.40),
        ("Metal",     62.86,  -0.73,   4.34),
        ("Psychic",   43.93,  16.42, -11.31),
        ("Water",     48.37,  -8.44, -23.65),
    ];

    /// <summary>Every type this can name, for a caller that wants to check its own list against it.</summary>
    public static IReadOnlyList<string> KnownTypes { get; } = Palette.Select(p => p.Type).ToArray();

    /// <summary>
    /// The type printed on this card, or null if the badge could not be read.
    ///
    /// <paramref name="rgb"/> is the whole card, row-major, <paramref name="stride"/> bytes per
    /// pixel with red first — which is what an ordinary decode gives, RGB or RGBA alike.
    ///
    /// Null rather than a guess. A card whose art failed to decode, an image that is not a card,
    /// and a future frame that moves the badge all arrive here as a fill that matches nothing, and
    /// all three want the same answer: say nothing and let the type column stay blank. That is the
    /// state the app already renders while it waits for card detail.
    /// </summary>
    public static TypeBadgeReading? Read(ReadOnlySpan<byte> rgb, int width, int height, int stride = 4)
    {
        if (width <= 0 || height <= 0 || stride < 3) return null;
        if (rgb.Length < (long)width * height * stride) return null;

        var lab = FillColour(rgb, width, height, stride, CentreX);
        if (lab is null) return null;

        var best = "";
        var bestDistance = double.MaxValue;

        foreach (var (type, l, a, b) in Palette)
        {
            var d = Math.Sqrt(Sq(lab[0] - l) + Sq(lab[1] - a) + Sq(lab[2] - b));
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = type;
        }

        return bestDistance <= SameTypeWithin ? new TypeBadgeReading(best, bestDistance) : null;
    }

    /// <summary>
    /// The fill ring's colour in CIELAB, or null if the ring falls outside the image.
    ///
    /// Averaged in Lab, one conversion per sample, rather than averaged in sRGB and converted
    /// once. That is more arithmetic for the same idea and it is not interchangeable: Lab is a
    /// non-linear function of sRGB, so the mean of the conversions is not the conversion of the
    /// mean. <see cref="Palette"/> was built and validated the first way, and mixing the two put
    /// Dragon far enough out to be refused outright while the other nine still read correctly --
    /// which is the shape of bug that would have looked like a bad reference colour rather than a
    /// bad average.
    ///
    /// A mean rather than a median: the ring is one flat colour by construction, so there is no
    /// outlier for a median to protect against.
    /// </summary>
    private static double[]? FillColour(
        ReadOnlySpan<byte> rgb, int width, int height, int stride, double cx)
    {
        var radius = Radius * FillRing;
        var aspect = (double)width / height;

        double sl = 0, sa = 0, sb = 0;
        var n = 0;

        for (var degrees = 0; degrees < 360; degrees += 10)
        {
            var t = degrees * Math.PI / 180;
            var x = (int)((cx + Math.Cos(t) * radius) * width);
            var y = (int)((CentreY + Math.Sin(t) * radius * aspect) * height);

            if (x < 0 || y < 0 || x >= width || y >= height) continue;

            var at = (y * width + x) * stride;
            var lab = ToLab(rgb[at], rgb[at + 1], rgb[at + 2]);
            sl += lab[0];
            sa += lab[1];
            sb += lab[2];
            n++;
        }

        return n < 24 ? null : [sl / n, sa / n, sb / n];
    }

    private static double Sq(double x) => x * x;

    /// <summary>
    /// sRGB to CIELAB, so "how far apart are these two colours" matches how different they look.
    /// Euclidean distance in RGB does not: it makes two dark colours look closer than they are,
    /// which is the whole Darkness-against-Water problem.
    /// </summary>
    private static double[] ToLab(double r8, double g8, double b8)
    {
        static double Linear(double c)
        {
            c /= 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var r = Linear(r8);
        var g = Linear(g8);
        var b = Linear(b8);

        var x = (r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047;
        var y = r * 0.2126 + g * 0.7152 + b * 0.0722;
        var z = (r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883;

        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;

        var fx = F(x);
        var fy = F(y);
        var fz = F(z);

        return [116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)];
    }

    // ==============================================================================================
    // Dual types, and why only one badge is read
    // ==============================================================================================
    //
    // Dual-typed Pokémon arrive in October and they print TWO badges, side by side, right-aligned
    // after the HP -- confirmed against promotional art of Mega Mewtwo X ex, which shows Psychic
    // then Fighting in that order, primary first. Because they are right-aligned, the rightmost
    // badge is at the same place a single card's badge is, so reading the PRIMARY type needs
    // nothing added: the numbers above already cover a dual card.
    //
    // Reading the second one is a different problem, and it is not the classifier's. The question
    // there is not "which type is this" but "is there a badge here at all", and on a single-typed
    // card that slot lands on the header -- HP digits, frame, or on a full-art card whatever the
    // illustration is doing. Four presence tests were measured and three were useless:
    //
    //     dark rim around the fill          scored NEGATIVE at real badges; the model was wrong,
    //                                      the badge has a LIGHT outer ring rather than a dark one
    //     fill-ring uniformity             ~75 stdev at real badges: the ring crosses the glyph,
    //                                      so it is not uniform and cannot be tested for it
    //     halo step, luma(1.10r)-luma(0.85r)   +37.7 median at badges against +0.2 at empty slots,
    //                                      but the tails overlap: 5th percentile -16.7 against a
    //                                      95th of +27.1, so no threshold separates them
    //     distance to the nearest reference    the one that works, because it asks the classifier
    //                                      rather than inventing a second model: 0.9 median and
    //                                      3.7 worst at a badge, 14.7 median at an empty slot
    //
    // So presence is answerable. What is not answerable yet is the PITCH -- how far left the
    // second badge sits -- because no dual-typed card's art exists to measure it on. Sweeping the
    // plausible range on single-typed cards, at a strict distance of 3.0, false positives run 0%
    // at most offsets and 2-3% at 0.065-0.070, which is exactly where an adjacent badge would be.
    //
    // Three percent of every card in the game gaining a type it does not have is worse than a
    // blank column, which is why this reads one badge and stops. When a real dual-typed card is
    // published the pitch becomes measurable and the threshold can be calibrated against actual
    // positives instead of inferred from their absence; that is a constant and a second call to
    // FillColour, which already takes the centre as a parameter for exactly this reason.
}
