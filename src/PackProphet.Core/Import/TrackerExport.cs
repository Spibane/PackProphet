namespace PackProphet.Import;

using System.Text;
using PackProphet.Data;
using PackProphet.Domain;

/// <summary>
/// Writes a collection out as CSV, for a spreadsheet or another tracker.
///
/// One format, aimed at no site in particular. Writing a file shaped for one tracker means
/// tracking that tracker: its column names, its set-code spellings, and — in at least one case —
/// its internal database ids, which are not derivable and would silently write correct-looking
/// counts against the wrong cards the day it renumbered. A file that states the same facts plainly
/// asks nothing of anyone and does not rot.
///
/// So the identifier is written twice, in both of the shapes the trackers surveyed actually use:
/// a combined <c>card_id</c> of "A1-1", and a separate <c>set</c> and <c>number</c>. A reader
/// looking for either finds it, and neither is a guess about what some particular site wants.
///
/// The mirror of <see cref="TrackerImport"/>, and expanding where that collapses. This app keys
/// ownership by artwork; a set list keys it by entry. So an import folds 3,761 rows into 3,546
/// cards, and an export writes those cards back out as 3,761 rows — a card re-listed in a later
/// set appears once per set that lists it, at the same count.
///
/// That is not padding to make the totals agree. The game counts a card as owned in every set it
/// appears in, so a Deluxe re-listing of a card you own is a card you own, and writing only the
/// first printing would report you as missing 214 cards you have.
///
/// Every entry is written, including the ones owned none of. A file listing only what is owned
/// cannot distinguish "none of these" from "no opinion about these", and at least one tracker's
/// import deletes what a file reports as zero — so a complete statement is both the more useful
/// file and the safer one.
/// </summary>
public static class TrackerExport
{
    /// <summary>
    /// The columns, in order.
    ///
    /// <c>card_id</c> and <c>quantity</c> are the names two different trackers already use, so
    /// they are the ones most likely to be recognised; <c>set</c>, <c>number</c>, <c>name</c> and
    /// <c>rarity</c> say what they are. Rarity is the card data's own code — "C", "RR", "CROWN" —
    /// rather than glyphs, since a column that gets sorted and filtered wants a value that
    /// compares.
    /// </summary>
    public const string Header = "set,number,card_id,name,rarity,quantity";

    /// <summary>
    /// The collection as CSV.
    ///
    /// No byte order mark. It would help Excel with the names carrying ♀ and ♂, but a reader that
    /// does not expect one takes it as part of the first column's name and then finds no column it
    /// recognises — a whole-file failure to spare a rendering glitch in one program.
    ///
    /// Set codes are spelled as this app's card data spells them, promos included ("PROMO-A", not
    /// "P-A"). The two spellings are both in use and neither is standard, so this picks the one
    /// that says what it is. <see cref="TrackerImport"/> reads either.
    /// </summary>
    public static string ToCsv(Collection owned, CardIndex index)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');

        foreach (var card in Entries(index))
        {
            sb.Append(Row(card, owned.Of(card))).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>A file name that says what it is and when it was taken.</summary>
    public static string FileName(DateTimeOffset at) => $"packprophet-collection-{at:yyyy-MM-dd}.csv";

    /// <summary>
    /// Every set entry, by set as the card data lists them and then by number — the order a set
    /// list is read in. Not by ownership key, which is this app's own idea of a card and would
    /// file a Deluxe re-listing next to its original rather than in its own set.
    /// </summary>
    private static IEnumerable<PocketCard> Entries(CardIndex index) =>
        index.BySet.SelectMany(set => set.Value);

    private static string Row(PocketCard card, int count)
    {
        var number = card.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return string.Join(',',
            Field(card.Set),
            Field(number),
            Field(card.Key),
            Field(card.Name),
            Field(card.Rarity),
            Field(count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// One CSV field, quoted only when it has to be.
    ///
    /// No card name currently contains a comma or a quote, so this never fires on today's data —
    /// which is why it is here rather than left out. A name that acquired one would shift every
    /// column after it, and the row would still parse: as a different card, with a different
    /// count, reported by nothing.
    /// </summary>
    private static string Field(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;
}
