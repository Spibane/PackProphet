namespace PackProphet.Vision;

/// <summary>
/// One digit cut out of a copy-count badge, as the browser measured it.
/// </summary>
/// <param name="Grey">
/// Ink coverage on a <see cref="CountReader.GlyphWidth"/> by <see cref="CountReader.GlyphHeight"/>
/// grid, one hex digit per cell, row-major. Grey rather than black and white on purpose: a glyph on
/// screen is about ten pixels across, so how much of a cell the stroke covers carries most of the
/// information, and a one-bit threshold throws it away. Measured both ways — binary gets the digits
/// wrong, grey does not.
/// </param>
/// <param name="Aspect">
/// The glyph's width over its height. Carried separately because normalising every glyph into the
/// same box discards it, and it is the one feature that separates a 1 from everything else
/// outright: a 1 measures 0.33 to 0.43 where every other digit measures 0.63 to 0.79.
/// </param>
public sealed class DigitGlyph
{
    public string Grey { get; set; } = "";
    public double Aspect { get; set; }
}

/// <summary>
/// Reads the number printed on a card's copy-count badge.
///
/// The badge is a dark ribbon across the bottom-left of a card carrying one to three white digits in
/// the game's own fixed-pitch font. That makes this a far easier problem than recognising card art:
/// ten shapes, always the same, always the same size relative to the card. It is nearest-exemplar
/// matching against glyphs cut out of the reference screenshots, and the exemplars below are exactly
/// that — real digits, not a font this project drew.
///
/// It refuses rather than guesses, on the same principle as the artwork matching. A count is either
/// read completely or reported as unknown: a card recorded as holding 1 copy when it holds 14 is a
/// silent wrong answer, and there is no way for the user to spot it afterwards.
/// </summary>
public static class CountReader
{
    /// <summary>The grid a glyph is normalised onto. Close to the size the digits actually are on screen.</summary>
    public const int GlyphWidth = 8;
    public const int GlyphHeight = 14;

    /// <summary>
    /// How far a glyph may sit from an exemplar and still be called that digit, as mean ink
    /// difference per cell, 0 to 1.
    ///
    /// Measured across the exemplars: two samples of the same digit sit up to 0.277 apart, and the
    /// closest two exemplars of DIFFERENT digits — an 8 and a 3 — sit 0.170 apart. Those overlap, so
    /// an absolute cutoff cannot separate them on its own and <see cref="Margin"/> does the real
    /// work. This only rejects a glyph that looks like nothing at all.
    /// </summary>
    public const double MaxDistance = 0.34;

    /// <summary>
    /// How much closer the winning digit must be than the best rival digit. The 8-against-3 pair
    /// above is why this exists: with the margin, a glyph that lands between two digits is reported
    /// as unreadable instead of being assigned the nearer one by a hair.
    /// </summary>
    public const double Margin = 0.04;

    /// <summary>
    /// How far apart two glyphs' aspects may be before they cannot be the same digit. Wide enough to
    /// absorb a thresholding difference, narrow enough that a 1 is never confused with anything.
    /// </summary>
    public const double AspectTolerance = 0.18;

    /// <summary>
    /// Digits cut from the reference screenshots in tests/PackProphet.Tests/fixtures: IMG_1152 and
    /// IMG_1154 (the three-across card list) and IMG_1151 (a Wonder Pick line-up, which is where the
    /// only 7 came from). Between them they cover all ten digits.
    ///
    /// Several digits have one exemplar and several have four. That is uneven and it is honest —
    /// these are the digits those screenshots happened to contain. Leave-one-out over every digit
    /// that has a second sample classifies 16 of 16 correctly.
    /// </summary>
    private static readonly (char Digit, string Grey, double Aspect)[] Exemplars =
    [
        ('0', "027ddc6115dfffc339fdaef75ce749fa8ed316fc9fc204edbfa103debfa103deafb103dd8fc205fc6de527fb4afa7cf826efefe5127ccb61", 0.69),
        ('1', "88efffd6ccfffffaaafffffa226ffffa003dfffa003dfffa003dfffa003dfffa003dfffa003dfffa003dfffa003dfffa003dfffa0029ddd7", 0.38),
        ('1', "55cffff988fffffe77ffffff223dffff000affff000affff000affff000affff000affff000affff000affff000affff000affff0007ccea", 0.38),
        ('1', "eeffffd5fffffff8fffffff855dffff9119ffff9119ffff9119ffff9119ffff9119ffff9119ffff9119ffff8119ffff8119ffff9116deed6", 0.38),
        ('1', "66bbbcca88fffffe66bbffff2255bfff00229fff00229fff00229fff00229fff00229fff00229fff00229fff00229fff00229fff00229ffe", 0.33),
        ('1', "fffffff8fffffffaccfffffb229ffffb006ffffb006ffffb006ffffb006ffffb006ffffb006ffffb006ffffb006ffffb006ffffb006ffffa", 0.43),
        ('1', "66bccca599fffff877dffff9226cfff9003bfff9003bfff9003afff9003afff9003afff9003afff9003afff9003afff9003afff9003afff8", 0.40),
        ('2', "14addb5138efffa36dfcbef58fd54cf87b921af934311afa00014de80004bfa40029fd52005dfa21019fe62115dfe9744afffff859bbbbb6", 0.69),
        ('3', "039fffb23afffff77ffffffb8ffa7ffd48934dfd11128ffc0006fffa0005effc00015cff565118ffcfe318ffeffb7fff9ffffffc27befec5", 0.63),
        ('3', "04afffa23bfffff77ffffffa9ff98ffc59934ffc11128ffb0007fff90005fffb00015efe56510affdfe31afffffa7ffeaffffffb28cefdb4", 0.63),
        ('3', "15bfffb33bfffff77ffedffa7ff86ffb47734efb11129ffa0006fff80004cffb11114cfd686119fecfe42afedffb9ffd8ffffffb26bddca3", 0.63),
        ('4', "00003bd300017ff30004bff40019fff4003dfff4006fcef401af8df404db4cf428f72cf45de74df69ffcbffbbffffffd799aaefa11111cf4", 0.73),
        ('4', "00018fb10003ffe20007fff2001bfff2004efff2018ffff205ee9ff21bfa7ff24efa9ff48ffffffaaffffffb6bccfff823348ff400004b91", 0.75),
        ('4', "00003cf600017ff60004bff60018fff6003cfff6006feef601afacf614dd6cf628fa4bf75cf96cf99ffedffdaffffffe6999aefb11112bf7", 0.79),
        ('5', "029ffffa03dffffb05fffdd817fe532119fe85202bfffd923cfffff639a8dffb13215efd00001afe22102afe67425dfdcfc9dffbdffffff8", 0.71),
        ('5', "005deeea008ffffd01bffffb02df844304ff842007fffd9209edeffa03313bff000005ff010004ff463118ff9fc68ffeaffffffb39ceedb4", 0.63),
        ('6', "00028b400006ef71002bfe50005ffb2001aff81003dffc613afffff97ffc7bfe9ff615ffafd403ffafe504ff7ffa49fe4cfffffc13adedb4", 0.69),
        ('7', "accccccbeffffffdadddfffc3445bff90003cff50005ffc20019ff81002cfc40015ef71003afb30016ef71003afd30006ffa10009fe50000", 0.67),
        ('8', "149efea339fffff75efdaffb7ff84bfc8ff83afc6efc8efb29fffff75dffeffaaffa6bfdcff627fedff628ffbffb7dfe6efffffb159cdcb4", 0.63),
        ('9', "03bffd404cffffe3affedff8ffd43bfbffb108fcffa008fcefd64cfb7ffffff728fffff3016dffa1002cfe40005ffb1001aff600018d9100", 0.69),
        ('9', "028effa416effff94cffbdfd6ff946ff8ff513ef8fe513ef6efc79fe28fffffb03affff80038eff40016efa2002bfe50004ffb20003bb400", 0.69),
    ];

    /// <summary>The number of exemplars, so a test can assert the table did not lose any.</summary>
    public static int ExemplarCount => Exemplars.Length;

    /// <summary>Digits with at least one exemplar. Expected to be all ten.</summary>
    public static IReadOnlySet<char> Covered { get; } = Exemplars.Select(e => e.Digit).ToHashSet();

    /// <summary>
    /// The number a badge's glyphs spell, or null when any one of them could not be read.
    ///
    /// All or nothing by design. A partly-read count is not a smaller answer, it is a different
    /// number: dropping an unreadable leading digit turns 14 into 4.
    /// </summary>
    public static int? Read(IReadOnlyList<DigitGlyph>? glyphs)
    {
        if (glyphs is null || glyphs.Count == 0 || glyphs.Count > 3) return null;

        var value = 0;
        foreach (var glyph in glyphs)
        {
            if (Classify(glyph) is not char digit) return null;
            value = value * 10 + (digit - '0');
        }

        // A count of zero is not something the game draws — a card you hold none of is not on this
        // list at all — so reading one means the glyphs were misread.
        return value > 0 ? value : null;
    }

    /// <summary>
    /// The exemplar table, for the leave-one-out test. Classifying each exemplar against the table
    /// with itself removed is the only real evidence of generalisation available — these are the
    /// digits three screenshots happened to contain, so there is no held-out set — and it runs in CI
    /// so that adding a bad exemplar fails the build.
    /// </summary>
    internal static IReadOnlyList<(char Digit, string Grey, double Aspect)> Table => Exemplars;

    /// <summary>The digit a glyph is, or null when nothing fits well enough or two things fit equally.</summary>
    public static char? Classify(DigitGlyph glyph) => Classify(glyph, -1);

    internal static char? Classify(DigitGlyph glyph, int skip)
    {
        if (glyph.Grey.Length != GlyphWidth * GlyphHeight) return null;

        // The winner is chosen only from exemplars of a compatible width, because the aspect is the
        // one feature normalising into a fixed grid throws away and it separates a 1 from every
        // other digit outright.
        var best = double.MaxValue;
        var winner = '\0';

        for (var i = 0; i < Exemplars.Length; i++)
        {
            if (i == skip) continue;
            var (digit, grey, aspect) = Exemplars[i];
            if (Math.Abs(aspect - glyph.Aspect) > AspectTolerance) continue;

            var distance = Distance(glyph.Grey, grey);
            if (distance < best) { best = distance; winner = digit; }
        }

        if (winner == '\0' || best > MaxDistance) return null;

        // The rival is searched for across the WHOLE table, aspect ignored. That asymmetry is
        // deliberate and it was a bug first: when only one digit's exemplars pass the aspect filter
        // there is no rival to compare against, the margin test says nothing, and the nearest
        // exemplar of that one digit wins by default however unlike it the glyph is. Searching
        // rivals globally means a glyph that is plainly some other digit — measured narrow by a bad
        // cut, say — is refused rather than forced into the only shape on offer.
        var rival = double.MaxValue;

        for (var i = 0; i < Exemplars.Length; i++)
        {
            if (i == skip) continue;
            var (digit, grey, _) = Exemplars[i];
            if (digit == winner) continue;

            var distance = Distance(glyph.Grey, grey);
            if (distance < rival) rival = distance;
        }

        return rival - best >= Margin ? winner : null;
    }

    /// <summary>Mean ink difference per cell, 0 for identical and 1 for exact opposites.</summary>
    private static double Distance(string a, string b)
    {
        var total = 0;
        for (var i = 0; i < a.Length; i++) total += Math.Abs(Nibble(a[i]) - Nibble(b[i]));
        return total / (15.0 * a.Length);
    }

    private static int Nibble(char c) =>
        c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10 : 0;
}
