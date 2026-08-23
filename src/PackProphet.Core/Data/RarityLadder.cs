namespace PackProphet.Data;

using PackProphet.Domain;

/// <summary>
/// One rung of the completion ladder: the rarity symbol a user points at when they say
/// "I want all diamonds" or "up to 2 star".
/// </summary>
/// <param name="Index">Position in the ladder. A completion target IS this index.</param>
/// <param name="Group">Diamond / Star / Shiny / Crown.</param>
/// <param name="Count">Number of symbols, e.g. 2 for 2-star.</param>
/// <param name="Codes">Rarity codes on this rung — 2-star covers both SR and SAR.</param>
public sealed record RarityRung(int Index, string Group, int Count, IReadOnlyList<string> Codes)
{
    /// <summary>Short label, e.g. "2◆" or "Crown".</summary>
    public string Symbol => Group switch
    {
        "Diamond" => $"{Count}◆",
        "Star" => $"{Count}★",
        "Shiny" => Count > 1 ? $"Shiny {Count}★" : "Shiny",
        "Crown" => "Crown",
        _ => $"{Group} {Count}"
    };

    /// <summary>
    /// The rarity drawn as the game draws it — repeated symbols rather than a code. "◆◆◆"
    /// is recognisable at a glance in a way "R" is not, especially to anyone who has not
    /// memorised the abbreviations.
    /// </summary>
    public string Glyphs => Group switch
    {
        "Diamond" => new string('◆', Count),
        "Star" => new string('★', Count),
        "Shiny" => new string('✦', Count),
        "Crown" => "♛",
        _ => Symbol
    };

    /// <summary>CSS class for colouring the glyphs by family.</summary>
    public string GlyphClass => Group.ToLowerInvariant();
}

/// <summary>
/// The ordered rarity ladder, DERIVED from rarities.json rather than hardcoded, so a
/// rarity added upstream appears without a code change.
///
/// Only the ordering of the symbol families is our own knowledge, since nothing in the
/// data says Star outranks Diamond. An unrecognised family sorts to the end rather than
/// throwing — a data update must not brick the app — but a unit test asserts every group
/// present in the data is known, so the gap surfaces in CI instead of in the UI.
/// </summary>
public sealed class RarityLadder
{
    private static readonly Dictionary<string, int> GroupOrder = new()
    {
        ["Diamond"] = 0,
        ["Star"] = 1,
        ["Shiny"] = 2,
        ["Crown"] = 3,
    };

    public static bool IsKnownGroup(string group) => GroupOrder.ContainsKey(group);

    private readonly Dictionary<string, int> _rungByCode;

    public IReadOnlyList<RarityRung> Rungs { get; }

    public RarityLadder(IReadOnlyDictionary<string, Rarity> rarities)
    {
        Rungs = rarities
            .GroupBy(kv => (kv.Value.Group, kv.Value.Count))
            .OrderBy(g => GroupOrder.TryGetValue(g.Key.Group, out var o) ? o : int.MaxValue)
            .ThenBy(g => g.Key.Count)
            .Select((g, i) => new RarityRung(i, g.Key.Group, g.Key.Count,
                                             g.Select(kv => kv.Key).OrderBy(c => c).ToArray()))
            .ToArray();

        _rungByCode = Rungs
            .SelectMany(r => r.Codes.Select(c => (c, r.Index)))
            .ToDictionary(x => x.c, x => x.Index);
    }

    /// <summary>Ladder index for a rarity code, or null if the code is unknown.</summary>
    public int? IndexOf(string rarityCode) =>
        _rungByCode.TryGetValue(rarityCode, out var i) ? i : null;

    /// <summary>
    /// True when a card's rarity is one of the selected rungs. Unknown rarities are excluded:
    /// better to under-claim than to invent a requirement the user can never satisfy.
    /// </summary>
    public bool IsSelected(string rarityCode, IReadOnlySet<int> tiers) =>
        IndexOf(rarityCode) is int i && tiers.Contains(i);

    /// <summary>
    /// Every rung up to and including <paramref name="topIndex"/> — the "all diamonds" shape.
    /// A convenience for building a selection, not a constraint on one: a target is an
    /// arbitrary SET of rungs, because plenty of collectors want stars and crowns while
    /// ignoring diamonds entirely, or want all diamonds plus 3-star and nothing between.
    /// </summary>
    public IReadOnlySet<int> UpTo(int topIndex) =>
        Rungs.Where(r => r.Index <= topIndex).Select(r => r.Index).ToHashSet();

    /// <summary>Every rung, i.e. complete-the-set.</summary>
    public IReadOnlySet<int> Everything => Rungs.Select(r => r.Index).ToHashSet();

    /// <summary>
    /// Every rung in one symbol family, e.g. all three Star rungs.
    /// </summary>
    /// <remarks>
    /// There are deliberately no named presets. One toggle chip per rung already makes any
    /// shape a couple of taps, so a preset list would be a second way to say the same thing
    /// and another place for the two to disagree. The default selection is all diamonds.
    /// </remarks>
    public IReadOnlySet<int> ByGroup(string group) =>
        Rungs.Where(r => r.Group.Equals(group, StringComparison.OrdinalIgnoreCase))
             .Select(r => r.Index).ToHashSet();
}
