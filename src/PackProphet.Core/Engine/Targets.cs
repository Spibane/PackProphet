namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;

/// <summary>
/// "Finish these rarities in this set, N copies each." The everyday target.
///
/// The rarities are an arbitrary set rather than a threshold. Collectors want shapes a threshold
/// cannot express — stars and crowns but no diamonds, or all diamonds plus 3-star and nothing in
/// between.
/// </summary>
/// <summary>
/// How many copies of a parallel foil are wanted, independently of its rarity rung.
///
/// Its own number rather than a yes/no, because a foil is not the rarity it shares. Someone
/// chasing two of every diamond may want one of each parallel foil, or none — the printing is a
/// separate collectible sold only in a limited-time pack.
/// </summary>
/// <param name="Keys">Ownership keys that are parallel foils.</param>
/// <param name="Copies">Copies wanted of each; zero leaves them out of the target entirely.</param>
public sealed record FoilPolicy(IReadOnlySet<string> Keys, int Copies)
{
    public bool Applies => Keys.Count > 0;
}

public sealed class RarityLadderTarget : ICompletionTarget
{
    private readonly string _set;
    private readonly RarityPlan _plan;
    private readonly FoilPolicy? _foils;

    /// <param name="foils">
    /// Copies wanted of the parallel foils, which are counted separately from their rung. Null
    /// means they follow the rung, which is what the app did before the setting existed.
    /// </param>
    public RarityLadderTarget(string set, RarityPlan plan, FoilPolicy? foils = null)
    {
        _set = set;
        _plan = plan;
        _foils = foils is { Applies: true } ? foils : null;
    }

    /// <summary>Builds the "everything up to here" shape. A convenience, not the model.</summary>
    public static RarityLadderTarget UpTo(string set, int topTierIndex, CardIndex index, int copies = 1) =>
        new(set, RarityPlan.UpTo(index.Ladder, topTierIndex, copies));

    public string Describe() => $"{_set}, {_plan.WantedTiers.Count} rarity tier(s)";

    /// <summary>
    /// Copies of one card this target asks for, or zero if it wants none.
    ///
    /// Public because the Collection page needs the same answer for its progress counters and its
    /// "short of target" filter. It used to work the rule out for itself, and the two drifted: the
    /// page counted all 139 parallel foils toward the target while the engine, honouring the foil
    /// setting, had dropped them from demand.
    /// </summary>
    /// </summary>
    public int Required(CardIndex index, PocketCard card)
    {
        if (index.Ladder.IndexOf(card.Rarity) is not int tier) return 0;

        // A parallel foil takes its own count, not its rung's.
        return _foils?.Keys.Contains(card.OwnershipKey) == true
            ? _foils!.Copies
            : _plan.Copies(tier);
    }

    /// <summary>
    /// Cards this target asks for, and how many of those are satisfied. Shares
    /// <see cref="Required"/> with <see cref="Outstanding"/> so a progress figure and a demand
    /// list can never disagree.
    /// </summary>
    public (int Wanted, int Satisfied) Progress(CardIndex index, Collection owned)
    {
        var wanted = 0;
        var satisfied = 0;

        foreach (var card in index.WantedInSet(_set, _plan.WantedTiers))
        {
            var required = Required(index, card);
            if (required <= 0) continue;

            wanted++;
            if (owned.Of(card) >= required) satisfied++;
        }

        return (wanted, satisfied);
    }

    public IReadOnlyList<Demand> Outstanding(CardIndex index, Collection owned)
    {
        var result = new List<Demand>();
        // WantedInSet already de-duplicates by ownership key and excludes cards no pack can
        // yield, so a reprint is demanded once and promos never appear as "missing".
        foreach (var card in index.WantedInSet(_set, _plan.WantedTiers))
        {
            var required = Required(index, card);
            if (required <= 0) continue;

            var remaining = required - owned.Of(card);
            if (remaining <= 0) continue;

            // Every entry listing this card, so its pack rates pool across sets: a Deluxe
            // reprint is obtainable from both its original set's packs and A4b's.
            var suppliers = index.ByOwnershipKey[card.OwnershipKey];
            result.Add(new Demand(card.OwnershipKey, remaining, suppliers));
        }
        return result;
    }
}

/// <summary>
/// Several targets at once — "finish everything I care about". Demands are merged by key,
/// taking the largest requirement, so overlapping targets never double-count a card.
/// </summary>
public sealed class CompositeTarget : ICompletionTarget
{
    private readonly IReadOnlyList<ICompletionTarget> _parts;
    private readonly string _label;

    public CompositeTarget(IReadOnlyList<ICompletionTarget> parts, string label = "everything")
    {
        _parts = parts;
        _label = label;
    }

    public string Describe() => _label;

    public IReadOnlyList<Demand> Outstanding(CardIndex index, Collection owned)
    {
        var merged = new Dictionary<string, Demand>();
        foreach (var part in _parts)
        foreach (var d in part.Outstanding(index, owned))
        {
            // Max, not sum: two targets both wanting one copy still need only one copy.
            if (!merged.TryGetValue(d.Key, out var existing) || d.Remaining > existing.Remaining)
                merged[d.Key] = d;
        }
        return merged.Values.ToArray();
    }
}

/// <summary>
/// The cards a specific deck needs. Demand is per card IDENTITY with multiplicity: any
/// printing fills a slot, and two copies of one printing work as well as two different
/// printings, so all printings' rates pool into a single demand.
/// </summary>
public sealed class DeckTarget : ICompletionTarget
{
    private readonly string _name;
    private readonly IReadOnlyList<int> _deckBuilderNrs;

    /// <param name="deckBuilderNrs">One entry per copy, exactly as a deck code lists them.</param>
    public DeckTarget(string name, IReadOnlyList<int> deckBuilderNrs)
    {
        _name = name;
        _deckBuilderNrs = deckBuilderNrs;
    }

    public string Describe() => $"deck \"{_name}\"";

    public IReadOnlyList<Demand> Outstanding(CardIndex index, Collection owned)
    {
        var result = new List<Demand>();
        foreach (var group in _deckBuilderNrs.GroupBy(nr => nr))
        {
            if (!index.ByDeckBuilderNr.TryGetValue(group.Key, out var printings)) continue;

            // De-duplicate by ownership key: a card reprinted in another set is one card.
            var suppliers = printings.DistinctBy(p => p.OwnershipKey).ToArray();

            var remaining = group.Count() - owned.OfIdentity(suppliers);
            if (remaining <= 0) continue;

            result.Add(new Demand($"nr:{group.Key}", remaining, suppliers));
        }
        return result;
    }
}

/// <summary>
/// An arbitrary list of wanted cards, priced by the same engine as every other target.
/// </summary>
public sealed class WishlistTarget : ICompletionTarget
{
    private readonly string _name;
    private readonly IReadOnlyDictionary<string, int> _wanted;

    /// <param name="wanted">Ownership key to desired copies.</param>
    public WishlistTarget(string name, IReadOnlyDictionary<string, int> wanted)
    {
        _name = name;
        _wanted = wanted;
    }

    public string Describe() => $"wishlist \"{_name}\"";

    public IReadOnlyList<Demand> Outstanding(CardIndex index, Collection owned)
    {
        var result = new List<Demand>();
        foreach (var (key, want) in _wanted)
        {
            if (!index.ByOwnershipKey.TryGetValue(key, out var suppliers)) continue;

            var remaining = want - owned[key];
            if (remaining <= 0) continue;

            result.Add(new Demand(key, remaining, suppliers));
        }
        return result;
    }
}
