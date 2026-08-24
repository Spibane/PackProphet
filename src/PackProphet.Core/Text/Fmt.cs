namespace PackProphet.Text;

using PackProphet.Data;
using PackProphet.Domain;

/// <summary>
/// Display formatting shared across pages.
///
/// It exists because the same quantity was being formatted five different ways. Expected packs
/// read as "16.0" on the pack ranking, "16" on card detail, "16" with a different infinity label
/// on the trade queue, and "1.6k" past ten thousand on two pages but not the others - so the same
/// card's cost looked like a different number depending on which screen you were on. A shared
/// formatter is not tidiness here, it is the only way those pages can agree.
/// </summary>
public static class Fmt
{
    /// <summary>
    /// Expected packs. Precision falls away as the figure grows, which is the honest shape for an
    /// expectation: 3.4 packs is a real difference from 3.9, while 1,847 and 1,850 are the same
    /// answer and printing both digits implies a confidence the model does not have.
    /// </summary>
    /// <param name="whenUnreachable">
    /// What to print when no pack can ever yield the card. Defaults to "never", which suits a
    /// timeline; a packs-saved column wants <see cref="NoPackRoute"/> instead, because there the
    /// point is not that it takes forever but that opening is not a route at all.
    /// </param>
    public static string Packs(double packs, string? whenUnreachable = null) =>
        double.IsInfinity(packs) || double.IsNaN(packs) ? whenUnreachable ?? Never
        : packs >= 10_000 ? $"{packs / 1000:N0}k"
        : packs >= 100 ? packs.ToString("N0")
        : packs.ToString("0.#");

    /// <summary>Null renders as an em dash: "not applicable" is not the same as "never".</summary>
    public static string Packs(double? packs) => packs is null ? "—" : Packs(packs.Value);

    /// <summary>
    /// The same figure with its unit attached, for running prose.
    ///
    /// It exists because "about {Packs(x)} packs" produces a broken sentence at infinity - the old
    /// pages rendered "Saves about no pack has it packs" - and the fix cannot be a different label,
    /// since no noun fits both a numeric column and the middle of a sentence. Here the unbounded
    /// case gets a clause instead of a word.
    /// </summary>
    public static string PacksPhrase(double packs) =>
        double.IsInfinity(packs) || double.IsNaN(packs)
            ? "more packs than any pack can give"
            : $"{Packs(packs)} packs";

    /// <summary>Where a timeline is meant.</summary>
    public const string Never = "never";

    /// <summary>Where the point is that opening packs is not a route to this card at all.</summary>
    public const string NoPackRoute = "no pack has it";

    /// <summary>
    /// A duration, at the coarsest useful precision. Days past a day, because "2.4 days" is what
    /// a regeneration wait means to someone deciding whether to check back tonight.
    /// </summary>
    public static string Span(TimeSpan? t) => t is not { } v
        ? "—"
        : v.TotalHours >= 24 ? $"{v.TotalDays:0.#} days"
        : v.TotalHours >= 1 ? $"{(int)v.TotalHours}h {v.Minutes}m"
        : $"{v.Minutes}m";

    /// <summary>Plural helper, for the many places a count is read aloud in a sentence.</summary>
    public static string S(int n, string singular, string? plural = null) =>
        n == 1 ? singular : plural ?? singular + "s";
}

/// <summary>
/// Rarity as the UI shows it. Four one-line helpers that had been copy-pasted into three pages
/// apiece; the ladder lookup behind them is a linear scan, so having one place also means having
/// one place to fix if that ever matters.
/// </summary>
public static class RarityDisplay
{
    public static RarityRung? Rung(this CardIndex index, PocketCard card) =>
        index.Ladder.Rungs.FirstOrDefault(r => r.Codes.Contains(card.Rarity));

    /// <summary>The in-game symbol, e.g. "3◆". Falls back to the raw code for unknown rarities.</summary>
    public static string Symbol(this CardIndex index, PocketCard card) =>
        index.Rung(card)?.Symbol ?? card.Rarity;

    public static string Glyphs(this CardIndex index, PocketCard card) =>
        index.Rung(card)?.Glyphs ?? card.Rarity;

    public static string GlyphClass(this CardIndex index, PocketCard card) =>
        index.Rung(card)?.GlyphClass ?? "";
}

/// <summary>
/// Reading numbers a person typed or pasted.
///
/// It exists because the app runs with <c>InvariantGlobalization</c>, so a plain
/// <c>int.TryParse</c> refuses "12,345" - and refusing means the field silently becomes ZERO and
/// then gets saved. The game prints its own figures with separators, so copying a shinedust or
/// hourglass balance out of it and pasting it in is the ordinary way these fields get filled, and
/// it destroyed the value. The same shape of bug as seeding a field from a floored division.
/// </summary>
public static class Num
{
    /// <summary>
    /// Whitespace of every kind, plus the group separators people actually paste. A non-breaking
    /// space is the one that catches you: copying from a rendered page brings it along invisibly,
    /// and no number parser accepts it.
    /// </summary>
    private static string Clean(string? text) =>
        text is null ? "" : new string(text.Where(c =>
            !char.IsWhiteSpace(c) && c != ' ' && c != ',' && c != '\'' && c != '_').ToArray());

    /// <summary>
    /// A whole count. Refuses a decimal point rather than truncating it: for hourglasses or dust a
    /// fractional figure means the input was misread, and guessing at it is worse than declining.
    /// </summary>
    public static bool TryCount(string? text, out int value) =>
        int.TryParse(Clean(text), System.Globalization.NumberStyles.Integer,
                     System.Globalization.CultureInfo.InvariantCulture, out value);

    /// <summary>A count, or zero when the text is not a number. For fields that cannot be blank.</summary>
    public static int Count(string? text) => TryCount(text, out var v) ? v : 0;

    /// <summary>
    /// A possibly-fractional amount. The decimal separator stays the invariant point, since the
    /// only such field is generated by the app rather than typed freely.
    /// </summary>
    public static bool TryAmount(string? text, out double value) =>
        double.TryParse(Clean(text), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value);
}
