namespace PackProphet.Data;

using PackProphet.Deck;
using PackProphet.Domain;

/// <summary>
/// The normalized, queryable view of the card database. Built once at load and treated
/// as immutable; everything downstream (odds engine, targets, views) reads from here.
/// </summary>
public sealed class CardIndex
{
    /// <summary>Every card, including ones not obtainable from packs.</summary>
    public IReadOnlyList<PocketCard> All { get; }

    /// <summary>Keyed "A1-1".</summary>
    public IReadOnlyDictionary<string, PocketCard> ByKey { get; }

    /// <summary>Keyed "A1:Mewtwo". Only pack-obtainable cards appear.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PocketCard>> ByPack { get; }

    /// <summary>Set code to its cards, in card-number order.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PocketCard>> BySet { get; }

    /// <summary>
    /// Card identity to every printing sharing it. Owning any one of these satisfies a
    /// deck slot needing that card.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<PocketCard>> ByDeckBuilderNr { get; }

    /// <summary>
    /// Ownable cards, keyed by <see cref="PocketCard.OwnershipKey"/>, each mapped to every
    /// set entry that lists it. 3,866 ownable cards across 4,317 entries: 451 entries are
    /// re-listings, since owning a card counts for every set it appears in.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PocketCard>> ByOwnershipKey { get; }

    /// <summary>Number of genuinely distinct cards a completionist has to acquire.</summary>
    public int DistinctOwnableCards => ByOwnershipKey.Count;

    public RarityLadder Ladder { get; }

    /// <summary>
    /// Cards whose artwork filename did not yield a DeckBuilderNr. Expected to be empty;
    /// surfaced rather than swallowed because a non-empty list means deck import will
    /// silently fail to match these cards.
    /// </summary>
    public IReadOnlyList<PocketCard> WithoutDeckBuilderNr { get; }

    private readonly Dictionary<string, int?> _nrByKey;

    public CardIndex(IEnumerable<PocketCard> cards, IReadOnlyDictionary<string, Rarity> rarities)
    {
        All = cards.ToArray();
        Ladder = new RarityLadder(rarities);

        // Upstream has been seen to contain duplicate set+number rows; first wins rather
        // than throwing, since one bad row should not prevent the app from starting.
        var byKey = new Dictionary<string, PocketCard>(All.Count);
        foreach (var c in All) byKey.TryAdd(c.Key, c);
        ByKey = byKey;

        ByOwnershipKey = All.GroupBy(c => c.OwnershipKey)
                            .ToDictionary(g => g.Key, g => (IReadOnlyList<PocketCard>)g.ToArray());

        BySet = All.GroupBy(c => c.Set)
                   .ToDictionary(g => g.Key,
                                 g => (IReadOnlyList<PocketCard>)g.OrderBy(c => c.Number).ToArray());

        // A card lists every pack it can come from; secret rares appear in all packs of
        // their set. Cards with no pack are not pack-obtainable and are excluded here.
        var byPack = new Dictionary<string, List<PocketCard>>();
        foreach (var c in All)
        {
            if (!c.IsPackObtainable) continue;
            foreach (var pack in c.Packs!)
            {
                if (!byPack.TryGetValue(PackKey(c.Set, pack), out var list))
                    byPack[PackKey(c.Set, pack)] = list = new List<PocketCard>();
                list.Add(c);
            }
        }
        ByPack = byPack.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<PocketCard>)kv.Value);

        _nrByKey = new Dictionary<string, int?>(All.Count);
        var byNr = new Dictionary<int, List<PocketCard>>();
        var missing = new List<PocketCard>();
        foreach (var c in All)
        {
            var nr = DeckBuilderNr.FromImage(c.Image);
            _nrByKey[c.Key] = nr;
            if (nr is null) { missing.Add(c); continue; }
            if (!byNr.TryGetValue(nr.Value, out var list))
                byNr[nr.Value] = list = new List<PocketCard>();
            list.Add(c);
        }
        ByDeckBuilderNr = byNr.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<PocketCard>)kv.Value);
        WithoutDeckBuilderNr = missing;
    }

    public static string PackKey(string set, string pack) => $"{set}:{pack}";

    /// <summary>Card identity for a card, or null when its artwork name was unparseable.</summary>
    public int? DeckNrOf(PocketCard card) =>
        _nrByKey.TryGetValue(card.Key, out var nr) ? nr : null;

    /// <summary>
    /// The other printing of a Deluxe set's 1-3 diamond card: the foil of a plain card, or the
    /// plain card of a foil. Null where there is none, or where the pairing is not one to one.
    ///
    /// Paired from the card list, not from pull rates, so it holds for a Deluxe set whose rates
    /// are not published yet. A foil is the same name and rarity in the same set under a non-zero
    /// variant index, which is how A4b and B4b both print them. The two look nearly alike in a
    /// screenshot, which is what this is for: a reading can name one and the user the other.
    /// </summary>
    public PocketCard? FoilTwin(PocketCard card)
    {
        if (card.Rarity is not ("C" or "U" or "R")
            || card.Packs is not { Length: > 0 } packs || !packs.Any(GameRules.IsDeluxePack)
            || !BySet.TryGetValue(card.Set, out var set))
            return null;

        var twins = set
            .Where(c => c.Key != card.Key && c.Name == card.Name && c.Rarity == card.Rarity
                        && (c.VariantIndex == 0) != (card.VariantIndex == 0))
            .Take(2)
            .ToArray();

        return twins.Length == 1 ? twins[0] : null;
    }

    /// <summary>Every pack key that can yield the given card.</summary>
    public IEnumerable<string> PacksContaining(PocketCard card) =>
        card.IsPackObtainable ? card.Packs!.Select(p => PackKey(card.Set, p)) : [];

    /// <summary>
    /// Cards a rarity selection wants from one set. Excludes anything not obtainable from
    /// packs — counting promos toward set completion would misreport progress forever, since
    /// no amount of opening packs can ever finish them.
    /// </summary>
    public IEnumerable<PocketCard> WantedInSet(string set, IReadOnlySet<int> tiers) =>
        BySet.TryGetValue(set, out var cards)
            ? cards.Where(c => c.IsPackObtainable && Ladder.IsSelected(c.Rarity, tiers))
                   // One demand per OWNABLE card. A4b re-lists 214 cards from earlier sets,
                   // and counting an entry per set would demand the same card repeatedly.
                   .DistinctBy(c => c.OwnershipKey)
            : [];

    /// <summary>
    /// Every pack, in any set, that can yield the given ownable card. A reprinted card is
    /// obtainable from its original set's packs AND from the reprinting set's, so demand
    /// for it pools rates across all of them.
    /// </summary>
    public IEnumerable<string> PacksYielding(string ownershipKey) =>
        ByOwnershipKey.TryGetValue(ownershipKey, out var entries)
            ? entries.SelectMany(PacksContaining).Distinct()
            : [];

    /// <summary>Every pack key in the data, e.g. "A1:Mewtwo". Includes promo groupings.</summary>
    public IEnumerable<string> AllPackKeys => ByPack.Keys;

    /// <summary>
    /// A set played by promo rules: its cards come from events, so it is not a pack anyone
    /// chooses to open, it earns no pack points, and no target or completion figure counts it.
    /// The PROMO sets, whose "Vol. N" groupings record how a card was handed out, and the Special
    /// Packs, which events hand out whole.
    /// </summary>
    public static bool IsPromoSet(string set) =>
        set.StartsWith("PROMO", StringComparison.OrdinalIgnoreCase) || IsSpecialSet(set);

    /// <summary>
    /// A PROMO set proper, issued a card or two at a time as events run: numbered in the order the
    /// cards arrive and never published whole. Narrower than <see cref="IsPromoSet"/>, which also
    /// takes in the Special Packs, and asked where that order is the point -- the newest promos,
    /// and the art that lags at the end of the run.
    /// </summary>
    public static bool IsPromoRun(string set) => PromoLetter(set) is not null;

    /// <summary>
    /// The series letter of a promo set — 'C' for "PROMO-C" — or null for any other set. The
    /// other datasets spell promo sets from this letter, "P-C" or "pc", and each of the places
    /// translating had "A" and "B" written out, so Promo C arrived spelled in a way none knew.
    /// </summary>
    public static char? PromoLetter(string set) =>
        set.Length == "PROMO-A".Length && set.StartsWith("PROMO-", StringComparison.OrdinalIgnoreCase)
        && char.IsAsciiLetter(set[^1])
            ? char.ToUpperInvariant(set[^1]) : null;

    /// <summary>
    /// This app's spelling of a promo set from another dataset's short one — "pc", "PC" or "P-C"
    /// to "PROMO-C" — or null when <paramref name="code"/> is not one.
    /// </summary>
    public static string? PromoSetFromShortCode(string code)
    {
        var c = code.Trim();
        if (c.Length is not (2 or 3) || char.ToUpperInvariant(c[0]) != 'P' || !char.IsAsciiLetter(c[^1])) return null;
        if (c.Length == 3 && c[1] != '-') return null;
        return $"PROMO-{char.ToUpperInvariant(c[^1])}";
    }

    /// <summary>
    /// A Special Pack set — SP1, "First Partners", the first. Opened on Special Pack Hourglasses
    /// that only events hand out, so it follows promo rules; see <see cref="IsPromoSet"/>.
    ///
    /// By code rather than by upstream's "Special" series, because the card list runs ahead of the
    /// set list and a new SP set has to be recognised the day its cards arrive.
    /// </summary>
    public static bool IsSpecialSet(string set) =>
        set.Length > 2 && set.StartsWith("SP", StringComparison.OrdinalIgnoreCase) && char.IsAsciiDigit(set[2]);

    /// <summary>
    /// Packs a player can actually decide to open. Promo "Vol. N" groupings are excluded: they
    /// describe how a promo was handed out rather than something available to open, so they are
    /// neither offered as choices nor counted as packs with missing data.
    /// </summary>
    public IEnumerable<string> OpenablePackKeys =>
        ByPack.Keys.Where(k => !IsPromoSet(k.Split(':')[0]));

    /// <summary>Sets that contain openable packs, in a stable order. Excludes promo sets, Special Packs included.</summary>
    public IEnumerable<string> OpenableSets =>
        OpenablePackKeys.Select(k => k.Split(':')[0]).Distinct().OrderBy(s => s, StringComparer.Ordinal);

    /// <summary>
    /// Whether opening a pack of <paramref name="set"/> is paid for in pack hourglasses: not for
    /// a promo set, Special Packs included, whose packs events hand out.
    /// </summary>
    public static bool UsesPackHourglasses(string set) => !IsPromoSet(set);

    /// <summary>
    /// Whether opening a pack of <paramref name="set"/> earns pack points, and so whether the set
    /// has a points balance and a points shop at all. Not a promo set, Special Packs included.
    /// The same sets as <see cref="UsesPackHourglasses"/>, but a separate rule in the game, so
    /// asked by name.
    /// </summary>
    public static bool EarnsPackPoints(string set) => !IsPromoSet(set);

    /// <summary>
    /// What a pack is called on screen. A set with one pack shows the set's name, which is what the
    /// game calls it. A set with several shows the pack's own name after the set's initials, as in
    /// "GA: Mewtwo", because a later set can print another Mewtwo pack.
    ///
    /// The raw pack names cannot be shown as they are. Upstream shortens the older single-pack sets
    /// ("Paldean", "Secluded", "Deluxe"), names a few after their cover Pokémon ("Mew",
    /// "Gardevoir"), and spells the newer ones out in full ("Pulsing Aura"). The raw name stays the
    /// key, because saved pack logs and settings are keyed on it.
    /// </summary>
    public string PackLabel(string packKey, SetCatalog sets)
    {
        var at = packKey.IndexOf(':');
        if (at < 0) return packKey;

        var set = packKey[..at];
        var pack = packKey[(at + 1)..];
        _packsPerSet ??= OpenablePackKeys
            .GroupBy(k => k.Split(':')[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        if (!_packsPerSet.TryGetValue(set, out var n)) return pack;
        if (n == 1) return sets.Info(set) is null ? pack : sets.DisplayName(set);

        // C1's packs carry the set's name in front of their own -- "Dual Fusion Mega Mewtwo X" --
        // and the prefix already says Dual Fusion, so it would be said twice.
        var name = sets.DisplayName(set);
        if (pack.Length > name.Length + 1 && pack.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase))
            pack = pack[(name.Length + 1)..];

        return $"{SetPrefix(set, sets)}: {pack}";
    }

    /// <summary>
    /// The set's initials, or its code where it has no catalogued name or where two multi-pack
    /// sets share the same initials, so a prefix never names two sets.
    /// </summary>
    private string SetPrefix(string set, SetCatalog sets)
    {
        _prefixes ??= BuildPrefixes(sets);
        return _prefixes.TryGetValue(set, out var prefix) ? prefix : set;
    }

    private Dictionary<string, string> BuildPrefixes(SetCatalog sets)
    {
        var initials = _packsPerSet!
            .Where(kv => kv.Value > 1 && sets.Info(kv.Key) is not null)
            .ToDictionary(kv => kv.Key, kv => Initials(sets.DisplayName(kv.Key)), StringComparer.OrdinalIgnoreCase);

        var clashing = initials.Values
            .GroupBy(v => v, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        return initials.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Length == 0 || clashing.Contains(kv.Value) ? kv.Key : kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static readonly HashSet<string> MinorWords =
        new(["a", "an", "and", "of", "the", "to", "in", "on", "for"], StringComparer.OrdinalIgnoreCase);

    /// <summary>"Genetic Apex" is "GA", "Space-Time Smackdown" is "STS", "Wisdom of Sea and Sky" is "WSS".</summary>
    internal static string Initials(string name) =>
        new(name.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !MinorWords.Contains(w))
                .Select(w => w.FirstOrDefault(char.IsLetterOrDigit))
                .Where(c => c != default)
                .Select(char.ToUpperInvariant)
                .ToArray());

    private Dictionary<string, int>? _packsPerSet;
    private Dictionary<string, string>? _prefixes;
}
