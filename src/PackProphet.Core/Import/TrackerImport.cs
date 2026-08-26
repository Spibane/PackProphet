namespace PackProphet.Import;

using PackProphet.Data;
using PackProphet.Domain;

/// <summary>Why one row of an import did not become a count.</summary>
public enum ImportProblemKind
{
    /// <summary>The row's card identifier matches nothing in the card database.</summary>
    UnknownCard,

    /// <summary>The row has no identifier at all, or a blank one.</summary>
    MissingId,

    /// <summary>The count column held something that is not a whole number.</summary>
    BadCount,
}

/// <param name="Line">1-based line in the source file, so the user can go and look at it.</param>
public sealed record ImportProblem(int Line, ImportProblemKind Kind, string Value, string Detail);

/// <summary>
/// What an import would do, computed without touching the user's data.
///
/// Separated from applying it because an import is the one operation that can silently destroy a
/// collection built up over months: a mis-detected column, a file from a tracker whose set codes
/// have drifted, or the wrong file entirely all parse successfully and produce a plausible-looking
/// result. The user sees these figures first and decides.
/// </summary>
/// <param name="Counts">Copy counts keyed by <see cref="PocketCard.OwnershipKey"/>, ready to become a Collection.</param>
/// <param name="RowsRead">Data rows in the file, excluding the header.</param>
/// <param name="RowsMatched">Rows that resolved to a known card, whether or not the count was above zero.</param>
/// <param name="RowsCollapsed">
/// Rows that landed on an ownership key another row had already claimed. Expected and harmless —
/// the other trackers key by set entry and 215 entries are re-listings — but reported because the
/// figure is also how a genuinely duplicated file announces itself.
/// </param>
public sealed record ImportPreview(
    IReadOnlyDictionary<string, int> Counts,
    int RowsRead,
    int RowsMatched,
    int RowsCollapsed,
    IReadOnlyList<ImportProblem> Problems)
{
    public static readonly ImportPreview Empty =
        new(new Dictionary<string, int>(), 0, 0, 0, Array.Empty<ImportProblem>());

    /// <summary>Distinct ownable cards this import claims at least one copy of.</summary>
    public int DistinctOwned => Counts.Count;

    /// <summary>Total copies across every card.</summary>
    public int TotalCopies => Counts.Values.Sum();

    public int RowsUnmatched => RowsRead - RowsMatched;

    /// <summary>
    /// The imported counts as a collection in their own right. Replaces rather than merges: an
    /// export is a complete statement of what someone owns, so a card absent from it is a card
    /// they do not have, and merging would leave anything the old collection wrongly claimed.
    /// Merging is offered separately, at the point the user chooses a destination.
    /// </summary>
    public Collection AsCollection() => new(Counts.ToDictionary(kv => kv.Key, kv => kv.Value));

    /// <summary>
    /// The imported counts folded into an existing collection, keeping the larger count for any
    /// card both claim. Max rather than sum, for the same reason reprints collapse with max: two
    /// statements about the same card are two opinions about one number, not two piles of cards.
    /// </summary>
    public Collection MergedInto(Collection existing)
    {
        var merged = new Dictionary<string, int>(existing.Raw);
        foreach (var (key, count) in Counts)
            merged[key] = Math.Max(count, merged.TryGetValue(key, out var had) ? had : 0);

        return new Collection(merged);
    }
}

/// <summary>
/// Which columns of an import file carry the card and the count.
/// </summary>
/// <param name="CardColumn">
/// Index of the column naming the card. Holds either a whole "A1-1" identifier or, when
/// <paramref name="SetColumn"/> is also present, just the number within that set. Which of the
/// two it is cannot be settled from the header alone — one tracker's <c>card_id</c> is "A1-1" and
/// another's is <c>1</c> — so it is decided per row by what actually resolves.
/// </param>
/// <param name="SetColumn">
/// Index of a column holding the set code on its own, or -1. Only consulted when the card column
/// does not resolve by itself, which keeps a file carrying both an "A1-1" id and a redundant
/// "A1" expansion column from being read as "A1-A1-1".
/// </param>
/// <param name="CountColumn">
/// Index of the column holding copies owned, or -1 when the file records only whether a card is
/// held. Some trackers track presence and not quantity; those import as one copy each rather than
/// failing, since one copy is what "collected" asserts.
/// </param>
/// <param name="CollectedColumn">Index of a boolean owned/not-owned column, or -1.</param>
public sealed record ColumnMap(int CardColumn, int SetColumn, int CountColumn, int CollectedColumn)
{
    public bool IsUsable => CardColumn >= 0 && (CountColumn >= 0 || CollectedColumn >= 0);
}

/// <summary>
/// Reads a collection exported by another Pokémon TCG Pocket tracker.
///
/// Every tracker surveyed identifies a card by the same two facts — set code and number — but not
/// in the same shape. tcgpocketcollectiontracker.com writes one column of "A1-1"; PTCGP Tracker
/// writes a "set_id" of "A1" beside a "card_id" of 1, using the same column name for a different
/// thing. So there is one pipeline and no per-tracker code, but the identifier is reassembled
/// rather than read: the header row is matched against known column names, and a tracker we have
/// never seen imports too as long as it names its columns something recognisable.
///
/// The identifier has to be translated, not copied. Other trackers key ownership by set entry;
/// this one keys it by artwork (see <see cref="PocketCard.OwnershipKey"/>), because the game counts
/// a card as owned in every set it appears in. 3,761 entries collapse to 3,546 ownable cards, so
/// an import necessarily merges rows.
/// </summary>
public static class TrackerImport
{
    /// <summary>
    /// Column names that name a card, lowercased, in preference order. "id" is what
    /// tcgpocketcollectiontracker.com writes and holds a whole "A1-1"; "card_id" is what PTCGP
    /// Tracker writes and holds a bare number. Both are read the same way — see
    /// <see cref="ColumnMap.CardColumn"/> — so the collision costs nothing.
    /// </summary>
    private static readonly string[] IdNames =
        ["id", "card_id", "cardid", "card id", "card", "number", "card_number", "card number"];

    /// <summary>
    /// Column names holding a set code on its own. "expansion" appears here even though the
    /// tracker that writes it also writes a complete id, because it costs nothing: a complete id
    /// resolves first and the set column is never reached.
    /// </summary>
    private static readonly string[] SetNames =
        ["set_id", "set id", "setid", "set", "set_code", "set code", "expansion", "expansion_id"];

    private static readonly string[] CountNames =
        ["numberowned", "number_owned", "number owned", "amount_owned", "amount owned",
         "owned", "count", "quantity", "qty", "copies"];

    private static readonly string[] CollectedNames = ["collected", "have", "obtained", "owned?"];

    /// <summary>
    /// Read a CSV export. Returns a preview; nothing is written anywhere.
    ///
    /// Never throws on bad input. A file that is not a collection export at all comes back as an
    /// empty preview with every row reported, which is a thing the UI can show, unlike an
    /// exception.
    /// </summary>
    public static ImportPreview FromCsv(string? text, CardIndex index) =>
        FromRows(CsvReader.Parse(text ?? ""), index);

    /// <summary>Read an .xlsx export. Same contract as <see cref="FromCsv"/>.</summary>
    public static ImportPreview FromXlsx(byte[]? bytes, CardIndex index) =>
        FromRows(bytes is null ? [] : XlsxReader.Parse(bytes), index);

    /// <summary>
    /// Read an export of either kind, deciding from the bytes rather than the file name.
    ///
    /// A workbook renamed to .csv and a CSV renamed to .xlsx are both things people do, and the
    /// extension is the one part of a file the user can change by accident. The zip signature is
    /// not.
    /// </summary>
    public static ImportPreview From(byte[]? bytes, CardIndex index)
    {
        if (bytes is null or { Length: 0 }) return ImportPreview.Empty;

        return LooksLikeZip(bytes)
            ? FromXlsx(bytes, index)
            : FromCsv(System.Text.Encoding.UTF8.GetString(bytes), index);
    }

    /// <summary>The local file header every zip, and so every .xlsx, opens with.</summary>
    private static bool LooksLikeZip(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B
        && bytes[2] == 0x03 && bytes[3] == 0x04;

    private static ImportPreview FromRows(List<string[]> rows, CardIndex index)
    {
        if (rows.Count < 2) return ImportPreview.Empty;

        var map = DetectColumns(rows[0]);
        if (!map.IsUsable) return ImportPreview.Empty;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var problems = new List<ImportProblem>();
        var read = 0;
        var matched = 0;
        var collapsed = 0;

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var line = r + 1;
            read++;

            var id = Field(row, map.CardColumn);
            if (id.Length == 0)
            {
                problems.Add(new ImportProblem(line, ImportProblemKind.MissingId, "", "no card id"));
                continue;
            }

            var set = Field(row, map.SetColumn);
            var card = Resolve(id, set, index);
            if (card is null)
            {
                problems.Add(new ImportProblem(
                    line, ImportProblemKind.UnknownCard, Shown(id, set),
                    "no card with this set and number"));
                continue;
            }

            if (!TryCount(row, map, out var count))
            {
                problems.Add(new ImportProblem(
                    line, ImportProblemKind.BadCount, Field(row, map.CountColumn), "not a whole number"));
                continue;
            }

            matched++;
            if (count <= 0) continue;

            var key = card.OwnershipKey;
            if (counts.TryGetValue(key, out var had))
            {
                collapsed++;
                counts[key] = Math.Max(had, count);
            }
            else counts[key] = count;
        }

        return new ImportPreview(counts, read, matched, collapsed, problems);
    }

    /// <summary>
    /// Match the header row against known column names. Case- and space-insensitive, because the
    /// same tracker writes "NumberOwned" in its CSV and "amount_owned" in its database.
    /// </summary>
    internal static ColumnMap DetectColumns(IReadOnlyList<string> header)
    {
        var names = header.Select(h => h.Trim().ToLowerInvariant()).ToArray();
        return new ColumnMap(
            IndexOfAny(names, IdNames),
            IndexOfAny(names, SetNames),
            IndexOfAny(names, CountNames),
            IndexOfAny(names, CollectedNames));
    }

    private static int IndexOfAny(string[] names, string[] wanted)
    {
        // Ordered by the caller's preference, not by column position: a file carrying both
        // "NumberOwned" and "Collected" should be read for its counts, and a file whose first
        // column happens to be "number" should not beat a later "card_id".
        foreach (var w in wanted)
        {
            var i = Array.IndexOf(names, w);
            if (i >= 0) return i;
        }
        return -1;
    }

    private static string Field(IReadOnlyList<string> row, int column) =>
        column >= 0 && column < row.Count ? row[column].Trim() : "";

    /// <summary>How an unresolved row is quoted back to the user: as the file spelled it.</summary>
    private static string Shown(string card, string set) =>
        set.Length > 0 && !card.Contains('-') ? $"{set}-{card}" : card;

    /// <summary>
    /// The card a row names, from a card column and an optional set column.
    ///
    /// The whole identifier is tried first and the set column only consulted if that fails. The
    /// order matters: tcgpocketcollectiontracker's export carries both an <c>Id</c> of "A1-1" and
    /// an <c>Expansion</c> of "A1", and composing those would ask for "A1-A1-1" on every row of an
    /// otherwise perfect file.
    /// </summary>
    internal static PocketCard? Resolve(string card, string set, CardIndex index) =>
        Resolve(card, index)
        ?? (set.Length > 0 ? Resolve($"{set}-{card}", index) : null);

    private static bool TryCount(IReadOnlyList<string> row, ColumnMap map, out int count)
    {
        count = 0;

        if (map.CountColumn >= 0)
        {
            var raw = Field(row, map.CountColumn);

            // A missing cell is zero, not a malformed one. Exports pad short rows inconsistently
            // and a blank there means the same thing a 0 does.
            if (raw.Length == 0) return true;

            if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var n)) return false;

            // A negative count is not a number to report and refuse; it is a card the user does
            // not have, which is what zero already means.
            count = Math.Max(0, n);
            return true;
        }

        count = IsTrue(Field(row, map.CollectedColumn)) ? 1 : 0;
        return true;
    }

    private static bool IsTrue(string s) =>
        s.Equals("true", StringComparison.OrdinalIgnoreCase)
        || s.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || s == "1";

    /// <summary>
    /// A tracker's card identifier to the card it names.
    ///
    /// The identifier is "set-number", which is this project's <see cref="PocketCard.Key"/>
    /// exactly — but only for the numbered sets. Promos are the one place the two datasets
    /// disagree on spelling: tcgpocketcollectiontracker writes "P-A-12" where this project writes
    /// "PROMO-A-12". With that aliased, all 3,761 of its entries resolve.
    ///
    /// Case and zero padding are normalised too, so an export writing "a1-001" reads the same.
    /// Cheap to do and a silent failure on every row to omit.
    /// </summary>
    internal static PocketCard? Resolve(string id, CardIndex index)
    {
        var trimmed = id.Trim();

        // Split on the LAST dash: a set code can contain one ("P-A"), a card number never does.
        var cut = trimmed.LastIndexOf('-');
        if (cut <= 0 || cut == trimmed.Length - 1) return null;

        if (!int.TryParse(trimmed[(cut + 1)..], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var number)) return null;

        var set = CanonicalSet(trimmed[..cut], index);
        if (set is null) return null;

        return index.ByKey.TryGetValue($"{set}-{number}", out var card) ? card : null;
    }

    /// <summary>
    /// A set code as this project spells it, from however the export spelled it.
    ///
    /// Matched against the card database rather than upper-cased, because set codes are mixed
    /// case — "A1a", "A2b", "B3a" — and upper-casing turns every one of the eight lettered sets
    /// into a code that matches nothing. The whole of A1a imported as unknown rows before this
    /// was matched case-insensitively, with no error anywhere: each row was individually
    /// well-formed and the set simply did not exist.
    ///
    /// Null when no set matches, which is what a file from a tracker that has a set this snapshot
    /// does not yet carry looks like.
    /// </summary>
    private static string? CanonicalSet(string raw, CardIndex index)
    {
        var set = raw.Trim();

        // The promo sets are the only codes the two datasets spell differently.
        if (set.Equals("P-A", StringComparison.OrdinalIgnoreCase)
            || set.Equals("PA", StringComparison.OrdinalIgnoreCase)) set = "PROMO-A";
        else if (set.Equals("P-B", StringComparison.OrdinalIgnoreCase)
            || set.Equals("PB", StringComparison.OrdinalIgnoreCase)) set = "PROMO-B";

        if (index.BySet.ContainsKey(set)) return set;

        foreach (var known in index.BySet.Keys)
            if (string.Equals(known, set, StringComparison.OrdinalIgnoreCase)) return known;

        return null;
    }
}
