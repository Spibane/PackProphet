namespace PackProphet.Data;

/// <summary>One entry from sets.json.</summary>
public sealed class SetInfo
{
    public string Code { get; set; } = "";
    public string? ReleaseDate { get; set; }
    public int Count { get; set; }

    /// <summary>Localised names, keyed by language. "en" is the one used here.</summary>
    public Dictionary<string, string>? Name { get; set; }

    public string[]? Packs { get; set; }

    public string DisplayName => Name is not null && Name.TryGetValue("en", out var n) ? n : Code;

    /// <summary>
    /// Release date, parsed. Exact-format only: upstream writes ISO dates, and a loose parse
    /// under InvariantGlobalization would be a guess rather than a reading.
    /// </summary>
    public DateOnly? ReleasedOn =>
        DateOnly.TryParseExact(ReleaseDate, "yyyy-MM-dd", out var date) ? date : null;

    /// <summary>
    /// Whether the set is out. Distinguishing this from "we have no odds for it" matters: a set
    /// that is out but unpriced means the community has not published pull rates YET and the
    /// cards are already droppable, while an unreleased set means nobody can have them at all.
    /// Reporting both as "not pullable" told the user their B4 cards were unobtainable when they
    /// were simply unpriced.
    ///
    /// An absent or unparseable date counts as released. Sets.json lags cards.json for a new
    /// set, so treating a missing date as "not out" would hide a set that is in fact on sale.
    /// </summary>
    public bool IsReleased(DateOnly today) => ReleasedOn is not DateOnly d || d <= today;
}

/// <summary>
/// Sets grouped into series (A, B, …), so the UI can offer a series at a time instead of one
/// ever-growing row of every set ever printed.
///
/// Upstream's own grouping is authoritative — it already places promo sets with their series
/// — but it is NOT relied upon exclusively: sets.json lags cards.json for a brand-new set, so
/// any set code the catalogue does not know is placed by deriving the series from its code.
/// That way a future series C appears the day its cards do, without a code change.
/// </summary>
public sealed class SetCatalog
{
    private readonly Dictionary<string, SetInfo> _byCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _bySeries = new(StringComparer.OrdinalIgnoreCase);

    public SetCatalog(
        IReadOnlyDictionary<string, List<SetInfo>>? published,
        IEnumerable<string> setCodesInUse)
    {
        foreach (var (series, sets) in published ?? new Dictionary<string, List<SetInfo>>())
        foreach (var set in sets)
        {
            _byCode[set.Code] = set;
            Add(series, set.Code);
        }

        // Sets present in the card data but absent from the published grouping — a new set
        // upstream has not catalogued yet. Place them rather than dropping them, or they
        // would silently vanish from the set picker.
        foreach (var code in setCodesInUse)
        {
            if (_byCode.ContainsKey(code)) continue;
            _byCode[code] = new SetInfo { Code = code };
            Add(SeriesFromCode(code), code);
        }

        foreach (var sets in _bySeries.Values) sets.Sort(CompareSetCodes);
    }

    private void Add(string series, string code)
    {
        if (!_bySeries.TryGetValue(series, out var list))
            _bySeries[series] = list = [];
        if (!list.Contains(code)) list.Add(code);
    }

    /// <summary>Series keys, in order. "A" before "B", and so on.</summary>
    public IReadOnlyList<string> Series =>
        _bySeries.Keys.OrderBy(s => s, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<string> SetsIn(string series) =>
        _bySeries.TryGetValue(series, out var sets) ? sets : [];

    public string SeriesOf(string setCode) =>
        _bySeries.FirstOrDefault(kv => kv.Value.Contains(setCode, StringComparer.OrdinalIgnoreCase)).Key
        ?? SeriesFromCode(setCode);

    public SetInfo? Info(string setCode) =>
        _byCode.TryGetValue(setCode, out var info) ? info : null;

    public string DisplayName(string setCode) => Info(setCode)?.DisplayName ?? setCode;

    /// <summary>
    /// Whether the set is out. Unknown sets count as released, for the same reason an unparseable
    /// date does: the card list runs ahead of the set list.
    /// </summary>
    public bool IsReleased(string setCode, DateOnly today) =>
        Info(setCode)?.IsReleased(today) ?? true;

    public DateOnly? ReleaseDateOf(string setCode) => Info(setCode)?.ReleasedOn;

    /// <summary>
    /// Series implied by a set code: the leading letters, except that "PROMO-A" belongs to
    /// series A rather than to a series called "PROMO".
    /// </summary>
    public static string SeriesFromCode(string code)
    {
        if (code.StartsWith("PROMO-", StringComparison.OrdinalIgnoreCase))
            return code["PROMO-".Length..].ToUpperInvariant();

        var letters = new string(code.TakeWhile(char.IsLetter).ToArray());
        return letters.Length > 0 ? letters.ToUpperInvariant() : code.ToUpperInvariant();
    }

    /// <summary>
    /// Release order within a series: A1, A1a, A2, A2a … with promos last. Ordinal string
    /// sorting alone would put "A1a" after "A10" once a series reaches ten sets.
    /// </summary>
    private static int CompareSetCodes(string a, string b)
    {
        var (aPromo, bPromo) = (IsPromo(a), IsPromo(b));
        if (aPromo != bPromo) return aPromo ? 1 : -1;

        var (aNum, aSuffix) = Split(a);
        var (bNum, bSuffix) = Split(b);

        var byNumber = aNum.CompareTo(bNum);
        return byNumber != 0 ? byNumber : string.CompareOrdinal(aSuffix, bSuffix);
    }

    private static bool IsPromo(string code) =>
        code.StartsWith("PROMO", StringComparison.OrdinalIgnoreCase);

    private static (int Number, string Suffix) Split(string code)
    {
        var digits = new string(code.SkipWhile(char.IsLetter).TakeWhile(char.IsDigit).ToArray());
        var suffix = new string(code.SkipWhile(char.IsLetter).SkipWhile(char.IsDigit).ToArray());
        return (int.TryParse(digits, out var n) ? n : int.MaxValue, suffix);
    }
}
