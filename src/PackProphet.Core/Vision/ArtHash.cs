using System.Numerics;

namespace PackProphet.Vision;

/// <summary>
/// A perceptual fingerprint of one piece of card art: 128 bits of difference hash, 64 comparing
/// each pixel to its right-hand neighbour and 64 comparing it to the one below.
///
/// Two axes rather than the usual one because the thing being told apart is not arbitrary
/// photographs but card art, where near-duplicates are the norm: the same Pokémon drawn twice,
/// a full art beside the plain printing, a foil beside its non-foil. A single 64-bit row hash
/// put pairs like those close enough together to be confused. Adding the column hash doubles the
/// signal for eight more bytes a card, which at 4,306 cards is 34 KB — cheaper than being wrong.
///
/// Both halves are gradient signs, not brightnesses, so the hash survives what a screenshot does
/// to art: rescaling, JPEG/WebP ringing, and the uniform darkening the game applies to a card the
/// player does not own. That last one is the whole reason a brightness-based fingerprint was not
/// an option — see <see cref="ShotCandidate"/> for how darkening is read separately, as the
/// ownership signal it is.
/// </summary>
/// <param name="Rows">Left-to-right gradient signs, bit i set when sample i is brighter than the sample to its right.</param>
/// <param name="Cols">Top-to-bottom gradient signs, laid out the same way.</param>
public readonly record struct ArtHash(ulong Rows, ulong Cols)
{
    /// <summary>The sampling grid the hash is computed from: 9 wide by 9 tall yields 8x8 of each gradient.</summary>
    public const int Grid = 9;

    /// <summary>Bits in a full hash. Distances are quoted against this, not against 64.</summary>
    public const int Bits = 128;

    /// <summary>Bit positions where the two hashes disagree. 0 is identical art, 128 is its exact inverse.</summary>
    public int DistanceTo(ArtHash other) =>
        BitOperations.PopCount(Rows ^ other.Rows) + BitOperations.PopCount(Cols ^ other.Cols);

    /// <summary>
    /// A hash with no gradients at all, which is what a flat region of screenshot produces: an
    /// empty slot, a letterbox bar, a panel background. Such a region is equidistant from a great
    /// deal of art and would otherwise match whichever entry happened to be blandest, so callers
    /// reject it up front rather than letting the nearest-neighbour search have it.
    /// </summary>
    public bool IsFeatureless => Rows == 0 || Cols == 0 || Rows == ulong.MaxValue || Cols == ulong.MaxValue;

    /// <summary>
    /// The hash of one card, from a <see cref="Grid"/>-by-<see cref="Grid"/> box-averaged luma grid
    /// in row-major order. Bit <c>j * 8 + i</c> of each half compares sample (i, j) to its
    /// neighbour, right for <see cref="Rows"/> and below for <see cref="Cols"/>.
    ///
    /// This is the authoritative definition, and it has a second implementation: the same bits are
    /// computed in the browser by <c>hash()</c> in wwwroot/js/cardshot.js, because the reference
    /// fingerprints are generated here from the art files while the ones being looked up are
    /// computed there from a screenshot. The two must agree exactly — a fingerprint computed a
    /// different way is not a near miss, it is a different card — so
    /// <c>ArtHashTests.TheGoldenVectorMatchesTheBrowserImplementation</c> pins one known grid to
    /// one known value, and cardshot.js repeats that vector in a comment. Change one side and both
    /// have to move.
    /// </summary>
    public static ArtHash From(ReadOnlySpan<double> grid)
    {
        if (grid.Length != Grid * Grid)
            throw new ArgumentException($"expected a {Grid}x{Grid} grid", nameof(grid));

        ulong rows = 0, cols = 0;

        for (var j = 0; j < Grid - 1; j++)
        for (var i = 0; i < Grid - 1; i++)
        {
            var here = grid[j * Grid + i];
            var bit = j * (Grid - 1) + i;

            if (here > grid[j * Grid + i + 1]) rows |= 1UL << bit;
            if (here > grid[(j + 1) * Grid + i]) cols |= 1UL << bit;
        }

        return new ArtHash(rows, cols);
    }

    /// <summary>32 lowercase hex digits, rows first. The form used in the committed table and over interop.</summary>
    public override string ToString() => Rows.ToString("x16") + Cols.ToString("x16");

    public static bool TryParse(ReadOnlySpan<char> text, out ArtHash hash)
    {
        hash = default;
        if (text.Length != 32) return false;
        if (!ulong.TryParse(text[..16], System.Globalization.NumberStyles.HexNumber, null, out var rows)) return false;
        if (!ulong.TryParse(text[16..], System.Globalization.NumberStyles.HexNumber, null, out var cols)) return false;
        hash = new ArtHash(rows, cols);
        return true;
    }
}
