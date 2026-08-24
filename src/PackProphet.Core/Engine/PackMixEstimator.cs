namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;

/// <param name="Packs">Estimated packs of this set, out of the total the estimate was given.</param>
/// <param name="Copies">Copies owned that were attributed to this set.</param>
/// <param name="CardsPerPack">
/// Cards attributable to this set from one of its packs - not the pack size. For an ordinary set
/// the two are nearly the same; for a Deluxe set they differ widely, because most of what a Deluxe
/// pack contains is reprints that count towards the set they came from.
/// </param>
public sealed record SetShare(string Set, double Packs, int Copies, double CardsPerPack)
{
    public double Share { get; init; }
}

/// <summary>
/// Splits a total pack count across sets, inferred from what the collection holds.
///
/// The game reports how many packs you have opened in your life, never which ones. Without a
/// split, a lifetime figure can only be priced with the mix of packs logged in this app, which is
/// wrong for anyone whose first thousand packs were sets they no longer open.
///
/// The inference is arithmetic: a pack of a set yields a known average number of cards that only
/// that set can give you, so copies held of those cards, divided by that average, estimates packs
/// opened of it. Only the ratios are used. The absolute figures are inflated — trades, Wonder
/// Picks and the points shop all add copies no pack produced — but that inflation is broadly
/// similar across sets and cancels when the shares are rescaled to a total the game has already
/// reported. So this answers "which sets, in what proportion", never "how many".
///
/// Attribution: a copy is credited to the set its card first appeared in, because a reprint shares
/// one ownership key across every set that lists it and cannot be told apart. That one rule also
/// covers Deluxe packs:
///
///   A Deluxe pack is mostly reprints of earlier sets, and those copies credit the set they were
///   printed in. What is left crediting a Deluxe set is its own cards - the parallel foils and its
///   handful of new ones - which no other pack can produce, so Deluxe packs are estimated from the
///   only evidence that points at them uniquely.
///
///   A Deluxe pack does not guarantee a foil: the foil-bearing slot carries about 61% of its
///   probability there. Dividing by the expected count of attributable cards rather than by one
///   keeps the rate right; assuming one foil per pack would understate Deluxe packs opened by
///   roughly a third.
///
/// Promos are dropped, since no pack yields them, as are sets with no published rates: a set in
/// the split contributing no expectation would shrink every other set's share.
/// </summary>
public sealed class PackMixEstimator
{
    private readonly CardIndex _index;
    private readonly PackOdds _odds;
    private readonly SetCatalog _sets;

    public PackMixEstimator(CardIndex index, PackOdds odds, SetCatalog sets)
    {
        _index = index;
        _odds = odds;
        _sets = sets;
    }

    /// <summary>
    /// One representative priceable pack per set. Which pack does not matter: pull rates are
    /// published per set, so every pack of a set shares the same rarity distribution and the same
    /// pack size, and they differ only in which cards they can hand you.
    /// </summary>
    private Dictionary<string, string> PackBySet() => _packBySet ??=
        _odds.PriceablePacks
            .GroupBy(key => key.Split(':')[0], StringComparer.OrdinalIgnoreCase)
            .Where(g => !CardIndex.IsPromoSet(g.Key))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, string>? _packBySet;

    /// <summary>
    /// The set a card first appeared in, which is the set a copy of it is credited to. Public so
    /// callers breaking a figure down by set use the same attribution as the split itself; two
    /// rules would put a card's copies in one bucket and its packs in another. Memoised: it is
    /// asked once per owned card and again for every card in every pack.
    /// </summary>
    public string? OriginSet(string ownershipKey)
    {
        if (_origin.TryGetValue(ownershipKey, out var cached)) return cached;

        if (!_index.ByOwnershipKey.TryGetValue(ownershipKey, out var printings))
            return _origin[ownershipKey] = null;

        return _origin[ownershipKey] = printings
            .Select(p => p.Set)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(_sets.SortKey, StringComparer.Ordinal)
            .First();
    }

    private readonly Dictionary<string, string?> _origin = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Expected copies, from one pack of a set, of the cards credited to that set. The divisor that
    /// turns copies held into packs opened, and what lets Deluxe packs share the general rule.
    /// </summary>
    public double AttributablePerPack(string set)
    {
        if (_perPack.TryGetValue(set, out var cached)) return cached;
        if (!PackBySet().TryGetValue(set, out var packKey)) return _perPack[set] = 0;

        var total = 0.0;
        foreach (var (cardKey, rate) in _odds.ExpectedCopies(packKey))
        {
            if (_index.ByKey.GetValueOrDefault(cardKey) is not { } card) continue;
            if (!string.Equals(OriginSet(card.OwnershipKey), set, StringComparison.OrdinalIgnoreCase))
                continue;

            total += rate;
        }

        return _perPack[set] = total;
    }

    private readonly Dictionary<string, double> _perPack = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How <paramref name="totalPacks"/> packs most plausibly split across sets, largest first.
    /// Empty when the collection says nothing - no copies, or none from a priceable set - which
    /// callers must treat as "no estimate available" rather than as a split of zero.
    /// </summary>
    public IReadOnlyList<SetShare> Estimate(Collection owned, int totalPacks)
    {
        var packBySet = PackBySet();
        var copies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (ownershipKey, _) in _index.ByOwnershipKey)
        {
            var held = owned[ownershipKey];
            if (held <= 0) continue;

            if (OriginSet(ownershipKey) is not { } set || !packBySet.ContainsKey(set)) continue;

            copies[set] = copies.GetValueOrDefault(set) + held;
        }

        var weighted = new List<SetShare>();
        foreach (var (set, held) in copies)
        {
            var perPack = AttributablePerPack(set);
            if (perPack <= 0) continue;

            weighted.Add(new SetShare(set, held / perPack, held, perPack));
        }

        var total = weighted.Sum(w => w.Packs);
        if (total <= 0) return [];

        return weighted
            .Select(w => w with { Packs = totalPacks * (w.Packs / total), Share = w.Packs / total })
            .OrderByDescending(w => w.Packs)
            .ToArray();
    }

    /// <summary>
    /// Expected copies per rarity rung from one pack of a set, over everything the pack can give -
    /// reprints included, unlike <see cref="AttributablePerPack"/>. Attribution answers "whose
    /// packs were these"; this answers "what does opening one produce".
    /// </summary>
    public IReadOnlyDictionary<int, double> RungRatesPerPack(string set)
    {
        var result = new Dictionary<int, double>();
        if (!PackBySet().TryGetValue(set, out var packKey)) return result;

        foreach (var (cardKey, rate) in _odds.ExpectedCopies(packKey))
        {
            if (_index.ByKey.GetValueOrDefault(cardKey) is not { } card) continue;
            if (_index.Ladder.IndexOf(card.Rarity) is not int rung) continue;

            result[rung] = result.GetValueOrDefault(rung) + rate;
        }

        return result;
    }
}
