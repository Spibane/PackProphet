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
    /// set entry that lists it. 3,546 ownable cards across 3,761 entries: 215 entries are
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

    /// <summary>A promo set, whose "Vol. N" groupings are not openable packs.</summary>
    public static bool IsPromoSet(string set) =>
        set.StartsWith("PROMO", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Packs a player can actually decide to open. Promo "Vol. N" groupings are excluded:
    /// they describe how a promo was handed out, not something available to open, so offering
    /// them as choices — or counting them as packs with missing data — is noise.
    /// </summary>
    public IEnumerable<string> OpenablePackKeys =>
        ByPack.Keys.Where(k => !IsPromoSet(k.Split(':')[0]));

    /// <summary>Sets that contain openable packs, in a stable order. Excludes promo sets.</summary>
    public IEnumerable<string> OpenableSets =>
        OpenablePackKeys.Select(k => k.Split(':')[0]).Distinct().OrderBy(s => s, StringComparer.Ordinal);
}
