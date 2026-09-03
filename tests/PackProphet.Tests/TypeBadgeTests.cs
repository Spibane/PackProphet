namespace PackProphet.Tests;

using PackProphet.Vision;

/// <summary>
/// Reading a Pokémon's type off the badge in a card's corner.
///
/// The images here are synthesised rather than loaded: a flat background with a disc of a measured
/// colour where the badge goes. That is deliberate, and it is testing the right thing. The
/// classifier's accuracy was established against 140 real cards while it was being built (see the
/// numbers on <see cref="TypeBadge"/>); what these pin is the part that could silently rot -- the
/// geometry, the palette, and the refusal to answer when it should not.
/// </summary>
public class TypeBadgeTests
{
    /// <summary>
    /// Each type's reference colour rendered back into sRGB, so a synthetic badge can be painted
    /// in it.
    ///
    /// Not the mean sRGB of a real card's fill ring, which is what these were first written as and
    /// which does not work: the ring is not uniform -- it clips the edge of the glyph -- so the
    /// mean of its pixels' Lab values, which is what the classifier compares, is a different point
    /// from the Lab of the mean of its pixels. A flat disc cannot reproduce the first from the
    /// second, and Dragon came out 4 away from its own reference and was refused while the other
    /// nine passed.
    ///
    /// So these are the palette's own centroids, converted. That makes this test a check on the
    /// geometry and the nearest-match rather than on the palette's accuracy, which is the right
    /// division: accuracy was established against 140 real cards, and no unit test can re-derive
    /// it from a flat fill.
    /// </summary>
    private static readonly (string Type, byte R, byte G, byte B)[] Measured =
    [
        ("Colorless", 0xBF, 0xBD, 0xBB),
        ("Darkness",  0x35, 0x47, 0x4E),
        ("Dragon",    0x67, 0x5D, 0x2B),
        ("Fighting",  0x9A, 0x5C, 0x36),
        ("Fire",      0xAD, 0x51, 0x3E),
        ("Grass",     0x46, 0x6C, 0x37),
        ("Lightning", 0xEF, 0xCB, 0x53),
        ("Metal",     0x9A, 0x98, 0x90),
        ("Psychic",   0x7B, 0x5F, 0x7B),
        ("Water",     0x40, 0x79, 0x9A),
    ];

    private const int W = 367, H = 512;      // the size the community CDN publishes card art at

    /// <summary>
    /// A card-shaped image: a background, and a filled disc where the badge is. The disc is drawn
    /// at the full badge radius, so the ring the reader samples at 0.78 of it lands inside.
    /// </summary>
    private static byte[] WithBadge(byte r, byte g, byte b,
                                   byte bgR = 0x77, byte bgG = 0x55, byte bgB = 0x33,
                                   double cx = TypeBadge.CentreX)
    {
        var px = new byte[W * H * 4];

        for (var i = 0; i < W * H; i++)
        {
            px[i * 4] = bgR;
            px[i * 4 + 1] = bgG;
            px[i * 4 + 2] = bgB;
            px[i * 4 + 3] = 255;
        }

        // Round in pixels, not in fractions -- the same aspect correction the reader makes.
        var radius = TypeBadge.Radius * W;
        var centreX = cx * W;
        var centreY = TypeBadge.CentreY * H;

        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var dx = x - centreX;
                var dy = y - centreY;
                if (dx * dx + dy * dy > radius * radius) continue;

                var at = (y * W + x) * 4;
                px[at] = r;
                px[at + 1] = g;
                px[at + 2] = b;
            }

        return px;
    }

    [Fact]
    public void Every_type_is_read_back_from_its_own_measured_fill()
    {
        var wrong = new List<string>();

        foreach (var (type, r, g, b) in Measured)
        {
            var reading = TypeBadge.Read(WithBadge(r, g, b), W, H);

            if (reading is null) { wrong.Add($"{type}: refused"); continue; }
            if (reading.Value.Type != type)
                wrong.Add($"{type}: read as {reading.Value.Type} at {reading.Value.Distance:F1}");
            else
                wrong.Add($"{type}: ok at {reading.Value.Distance:F1}");
        }

        Assert.DoesNotContain(wrong, w => w.Contains("refused") || w.Contains("read as"));
    }

    [Fact]
    public void The_two_near_neutral_types_are_still_told_apart()
    {
        // Colorless and Metal are the pair every earlier approach failed on: both fills are
        // near-neutral and differ mostly in lightness, and sampling across the whole badge averages
        // each fill with its own glyph's ink. This is the assertion that would have caught that.
        var colorless = TypeBadge.Read(WithBadge(0xBF, 0xBD, 0xBB), W, H);
        var metal = TypeBadge.Read(WithBadge(0x9A, 0x98, 0x90), W, H);

        Assert.Equal("Colorless", colorless!.Value.Type);
        Assert.Equal("Metal", metal!.Value.Type);
    }

    [Fact]
    public void A_card_with_no_badge_is_not_given_a_type()
    {
        // The failure that matters most. This is a fallback for cards whose detail has not been
        // published, so it runs on whatever art exists -- and a confident wrong answer is worse
        // than the blank column the app already knows how to render.
        foreach (var (r, g, b) in new (byte, byte, byte)[]
                 {
                     (0x77, 0x55, 0x33),    // a mid brown, like a frame
                     (0xFF, 0xFF, 0xFF),    // paper
                     (0x00, 0x00, 0x00),    // ink
                     (0xFF, 0x00, 0xFF),    // nothing on any card
                 })
        {
            var flat = new byte[W * H * 4];
            for (var i = 0; i < W * H; i++)
            {
                flat[i * 4] = r;
                flat[i * 4 + 1] = g;
                flat[i * 4 + 2] = b;
                flat[i * 4 + 3] = 255;
            }

            Assert.Null(TypeBadge.Read(flat, W, H));
        }
    }

    [Fact]
    public void A_badge_somewhere_else_is_not_read_as_one_here()
    {
        // Guards the geometry. If the sampled ring drifted, a badge painted off-position would
        // still be found and the reader would look correct while reading the wrong pixels.
        Assert.Null(TypeBadge.Read(WithBadge(0x46, 0x6C, 0x37, cx: 0.5), W, H));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 10)]
    [InlineData(10, 0)]
    public void A_degenerate_image_is_refused_rather_than_indexed(int w, int h)
    {
        Assert.Null(TypeBadge.Read(new byte[400], w, h));
    }

    [Fact]
    public void A_buffer_too_small_for_its_stated_size_is_refused()
    {
        // The reader indexes by arithmetic on width, height and stride, so a mismatch here is an
        // out-of-range read rather than a wrong answer.
        Assert.Null(TypeBadge.Read(new byte[100], W, H));
    }

    [Fact]
    public void Three_byte_pixels_read_the_same_as_four()
    {
        // An RGB decode and an RGBA one must agree, or the answer depends on how the caller
        // happened to decode the file.
        var rgba = WithBadge(0x46, 0x6C, 0x37);
        var rgb = new byte[W * H * 3];
        for (var i = 0; i < W * H; i++)
        {
            rgb[i * 3] = rgba[i * 4];
            rgb[i * 3 + 1] = rgba[i * 4 + 1];
            rgb[i * 3 + 2] = rgba[i * 4 + 2];
        }

        Assert.Equal(TypeBadge.Read(rgba, W, H)!.Value.Type,
                     TypeBadge.Read(rgb, W, H, stride: 3)!.Value.Type);
    }

    [Fact]
    public void No_two_reference_colours_are_close_enough_for_the_threshold_to_be_ambiguous()
    {
        // The structural invariant behind the accuracy. A threshold only means anything if the
        // nearest wrong answer is further away than it: were two references within 2x the
        // threshold, a fill halfway between them would be accepted as whichever came first in the
        // list. The closest pair is Colorless against Metal, about 14 apart, against a threshold
        // of 4.
        var types = TypeBadge.KnownTypes;
        Assert.Equal(10, types.Count);

        var closest = double.MaxValue;
        var pair = "";

        for (var i = 0; i < Measured.Length; i++)
            for (var j = i + 1; j < Measured.Length; j++)
            {
                var a = TypeBadge.Read(WithBadge(Measured[i].R, Measured[i].G, Measured[i].B), W, H);
                var b = TypeBadge.Read(WithBadge(Measured[j].R, Measured[j].G, Measured[j].B), W, H);

                // Each lands on its own reference, so the sum of their distances bounds how much
                // of the gap between the two references is used up by sampling error.
                var slack = a!.Value.Distance + b!.Value.Distance;
                if (slack >= closest) continue;
                closest = slack;
                pair = $"{Measured[i].Type}/{Measured[j].Type}";
            }

        Assert.True(closest < TypeBadge.SameTypeWithin,
            $"the tightest pair {pair} uses {closest:F1} of the {TypeBadge.SameTypeWithin} threshold");
    }

    [Fact]
    public void The_types_it_can_name_are_the_ones_the_card_data_uses()
    {
        // A typo in the palette would show up as a type the rest of the app has never heard of,
        // which would reach the UI as a pip with no glyph and no colour.
        string[] fromTheData =
        [
            "Grass", "Fire", "Water", "Lightning", "Psychic",
            "Fighting", "Darkness", "Metal", "Dragon", "Colorless",
        ];

        Assert.Equal(fromTheData.OrderBy(t => t), TypeBadge.KnownTypes.OrderBy(t => t));
    }
}
