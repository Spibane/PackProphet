namespace PackProphet.Vision;

/// <summary>
/// What the pixel side of the import hands back: the lattice of card-shaped regions it found in a
/// screenshot, and one fingerprint plus a few statistics per region.
///
/// The split is deliberate and it is the same split the rest of the app uses for JavaScript.
/// wwwroot/js/cardshot.js does only what C# in the browser cannot: getting at pixels and finding a
/// repeating grid in them. Every decision made from those pixels — which card this is, whether it
/// is owned, which in-game screen this even is, what to tell the user — lives here in Core, where
/// it can be tested without a browser. Nothing in this file makes a judgement; it is the wire
/// format between the two halves.
/// </summary>
public sealed class ShotScan
{
    public bool Ok { get; set; }

    /// <summary>Set when <see cref="Ok"/> is false. Already phrased for the user.</summary>
    public string? Error { get; set; }

    /// <summary>The image's own pixel dimensions, before the working downscale.</summary>
    public int Width { get; set; }
    public int Height { get; set; }

    public ShotLattice? Lattice { get; set; }

    public List<ShotCell> Cells { get; set; } = [];

    public static ShotScan Failed(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// The repeating grid of card slots found in the screenshot, in working-scale pixels. A single row
/// of five large cards — a pack's reveal, a Wonder Pick's line-up — is a lattice with one row, so
/// one detector serves every screen the import reads.
/// </summary>
public sealed class ShotLattice
{
    public int Rows { get; set; }
    public int Cols { get; set; }

    /// <summary>Slot pitch, which is the cell plus whatever gutter the game leaves between cards.</summary>
    public double CellWidth { get; set; }
    public double CellHeight { get; set; }

    /// <summary>
    /// How cleanly the detected period explained the screenshot's edge energy, 0 to 1. Low
    /// confidence is not a failure — a reading built from a weak lattice is still checked against
    /// the fingerprint table, which is the stricter test — but it decides how loudly the page
    /// suggests the user picked the wrong screen.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>Slot width as a fraction of the image width. The one cue that separates a collection grid from a pack reveal.</summary>
    public double RelativeCellWidth { get; set; }
}

/// <summary>One slot of the lattice: its place in the grid, its fingerprint, and how it is rendered.</summary>
public sealed class ShotCell
{
    public int Row { get; set; }
    public int Col { get; set; }

    /// <summary>
    /// Where this slot was cut, as x, y, width, height in working-scale pixels. Empty where the
    /// scanner did not say.
    ///
    /// Read for one thing: the horizontal offset between rows, which is what tells a hand of five
    /// from a page of a card list — see <see cref="ScreenshotReader.Infer"/>. The column index will
    /// not do, because it is the position rounded to the nearest slot and the whole signal is the
    /// half a slot that rounding throws away.
    /// </summary>
    public double[] Box { get; set; } = [];

    /// <summary>32 hex digits, or empty when the region was too flat to fingerprint.</summary>
    public string Hash { get; set; } = "";

    /// <summary>
    /// A small JPEG of this slot, as a data URL, or empty where the scanner did not cut one.
    ///
    /// Carried for the slots that come back unnamed, which is the one place the app has to ask a
    /// person what a card is: a name typed into a box is only checkable against the picture it was
    /// typed for. Never used to recognise anything — the fingerprint is computed from the
    /// full-resolution box, and this is a couple of thousand bytes at 96 pixels.
    /// </summary>
    public string Thumb { get; set; } = "";

    /// <summary>
    /// The same card fingerprinted from crops nudged a few pixels each way. The detector locates a
    /// card to within two to four pixels and cannot reliably do better, while the fingerprint has no
    /// tolerance for that — so the reader is given a spread to choose from rather than one box to
    /// trust. Empty is allowed and simply means no alternatives were offered.
    /// </summary>
    public List<string> Nearby { get; set; } = [];

    /// <summary>Every crop of this cell, the centre first.</summary>
    public IEnumerable<string> AllHashes =>
        string.IsNullOrEmpty(Hash) ? Nearby : Nearby.Prepend(Hash);

    /// <summary>Mean brightness, 0 to 1.</summary>
    public double Luma { get; set; }

    /// <summary>Mean saturation, 0 to 1. The signal that separates real art from the game's placeholder for a card you do not own.</summary>
    public double Saturation { get; set; }

    /// <summary>
    /// Gradient energy, 0 to 1: how much is going on in the region. A slot the game left empty, a
    /// letterboxed bar or a flat panel scores near zero, and such a region must never reach the
    /// nearest-neighbour search — it sits about equally far from thousands of entries and would
    /// match whichever art happened to be blandest.
    /// </summary>
    public double Detail { get; set; }

    /// <summary>
    /// The digits on this card's copy-count badge, left to right, where the screen shows one and the
    /// badge was found. Empty everywhere else — the five-across list and a pack's reveal print no
    /// count, and a blank slot has no badge.
    /// </summary>
    public List<DigitGlyph> Digits { get; set; } = [];

    /// <summary>The cell's position in reading order, which for a collection grid is also its position in the set's numbering.</summary>
    public int IndexIn(ShotLattice lattice) => Row * lattice.Cols + Col;
}
