namespace PackProphet.Text;

using PackProphet.Data;
using PackProphet.Domain;

/// <summary>
/// Display formatting shared across pages.
///
/// The same quantity had been formatted five different ways: expected packs read as "16.0" on the
/// pack ranking, "16" on card detail, "16" with a different infinity label on the trade queue, and
/// "1.6k" past ten thousand on two pages but not the others, so one card's cost looked like a
/// different number depending on the screen.
/// </summary>
public static class Fmt
{
    /// <summary>
    /// Expected packs. Precision falls away as the figure grows: 3.4 packs differs from 3.9, while
    /// 1,847 and 1,850 are the same answer and printing both digits implies a confidence the model
    /// does not have.
    /// </summary>
    /// <param name="whenUnreachable">
    /// What to print when no pack can ever yield the card. Defaults to "never", which suits a
    /// timeline; a packs-saved column wants <see cref="NoPackRoute"/>, where the point is that
    /// opening is not a route at all.
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
    /// "about {Packs(x)} packs" produces a broken sentence at infinity — the earlier pages
    /// rendered "Saves about no pack has it packs" — and no single noun fits both a numeric column
    /// and the middle of a sentence, so the unbounded case gets a clause here instead of a word.
    /// </summary>
    public static string PacksPhrase(double packs) =>
        double.IsInfinity(packs) || double.IsNaN(packs)
            ? "more packs than any pack can give"
            : $"{Packs(packs)} packs";

    /// <summary>Where a timeline is meant.</summary>
    public const string Never = "never";

    /// <summary>
    /// Expected packs as calendar time, which is the figure that changes decisions: 61 packs means
    /// nothing until it is a month.
    ///
    /// Here rather than on the page that wrote it first. The Packs page and the Progress page both
    /// state a pack figure as time, and two copies of the banding would be two pages disagreeing
    /// about when "days" becomes "months".
    /// </summary>
    public static string Days(double packs, bool premium)
    {
        if (double.IsPositiveInfinity(packs) || double.IsNaN(packs)) return Never;

        var days = packs / GameRules.PacksPerDay(premium);

        // Written as plain comparisons on purpose: Razor's parser reads a line-leading '<' in a
        // relational pattern as the start of a markup tag, and this is called from markup.
        if (days < 1) return "today";
        if (days < 60) return $"{days:N0} days";
        if (days < 730) return $"{days / 30.44:N0} months";
        return $"{days / 365.25:N1} years";
    }

    /// <summary>Where the point is that opening packs is not a route to this card at all.</summary>
    public const string NoPackRoute = "No pack has it";

    /// <summary>
    /// A duration, at the coarsest useful precision. Days past a day, since "2.4 days" is what a
    /// regeneration wait means to someone deciding whether to check back tonight.
    /// </summary>
    public static string Span(TimeSpan? t) => t is not { } v
        ? "—"
        : v.TotalHours >= 24 ? $"{v.TotalDays:0.#} days"
        : v.TotalHours >= 1 ? $"{(int)v.TotalHours}h {v.Minutes}m"
        : $"{v.Minutes}m";

    /// <summary>Plural helper, for the many places a count is read aloud in a sentence.</summary>
    public static string S(int n, string singular, string? plural = null) =>
        n == 1 ? singular : plural ?? singular + "s";

    /// <summary>
    /// The same, for a figure that is printed with a decimal. It agrees with what is SHOWN rather
    /// than with the underlying double: 1.04 packs prints as "1" at one decimal place, and "1
    /// packs" is the bug this exists to stop.
    /// </summary>
    public static string S(double n, string singular, string? plural = null) =>
        Math.Round(n, 1) == 1d ? singular : plural ?? singular + "s";
}

/// <summary>
/// Rarity as the UI shows it. Four one-line helpers that had been copy-pasted into three pages
/// apiece.
/// </summary>
public static class RarityDisplay
{
    /// <summary>
    /// The rung a card sits on, or null for an unknown rarity.
    ///
    /// Was a linear scan of every rung's code list, which is what seven pages were also each
    /// doing by hand. The ladder already builds a code-to-rung dictionary for
    /// <see cref="RarityLadder.IndexOf"/>, so this is a lookup.
    /// </summary>
    public static RarityRung? Rung(this CardIndex index, PocketCard card) =>
        index.Ladder.RungOf(card.Rarity);

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
/// The app runs with <c>InvariantGlobalization</c>, so a plain <c>int.TryParse</c> refuses
/// "12,345" — and refusing means the field becomes zero and then gets saved. The game prints its
/// own figures with separators, so pasting a shinedust or hourglass balance out of it is the
/// ordinary way these fields get filled.
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
    /// fractional figure means the input was misread.
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
