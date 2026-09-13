namespace PackProphet.Engine;

using System.Collections.Concurrent;

using PackProphet.Data;
using PackProphet.Domain;

/// <summary>Per-card expected copies from one pack, plus the chance a pack is useful.</summary>
public sealed class PackOdds
{
    private readonly CardIndex _index;
    private readonly PullRates _rates;

    /// <summary>
    /// The rate table this engine was built over. Exposed so callers can report which sets are
    /// priced with borrowed rates, since only the table knows.
    /// </summary>
    public PullRates Rates => _rates;

    // Expected copies per pack, by pack key then card key. Built once per pack on demand, and
    // depends only on the data rather than the collection, so caching is safe.
    //
    // Concurrent, even though Blazor WebAssembly is single-threaded: the caches fill lazily and
    // the test suite runs classes in parallel over a shared instance, where a plain Dictionary
    // corrupts itself.
    private readonly ConcurrentDictionary<string, Dictionary<string, double>> _cache = new();
    private readonly ConcurrentDictionary<string, HashSet<int>> _foilRungs = new();

    public PackOdds(CardIndex index, PullRates rates)
    {
        _index = index;
        _rates = rates;
    }


    /// <summary>
    /// Rungs for which a set's pull rates name a foil code (CF/UF/RF). Only Deluxe sets do this,
    /// and its presence is what splits a rung into plain and foil printings. A non-zero variant
    /// index means "alternate art" in every other set, so the index alone cannot decide it.
    /// </summary>
    private HashSet<int> FoilRungs(string set)
    {
        if (_foilRungs.TryGetValue(set, out var cached)) return cached;

        var rungs = new HashSet<int>();
        foreach (var (_, _, variant) in _rates.Variants(set))
        foreach (var slot in variant.Slots.Values)
        foreach (var code in slot.Keys)
            if (BaseCodeOfFoil(code) is string b && _index.Ladder.IndexOf(b) is int r)
                rungs.Add(r);

        return _foilRungs.GetOrAdd(set, rungs);
    }

    /// <summary>"CF" -&gt; "C" when the trimmed code is a real rarity; null otherwise.</summary>
    private string? BaseCodeOfFoil(string code) =>
        code.Length > 1 && code[^1] == 'F' && _index.Ladder.IndexOf(code[..^1]) is not null
            ? code[..^1]
            : null;

    /// <summary>
    /// Cards in a pack that a slot's rarity code can actually yield.
    ///
    /// The code denotes a rarity rung rather than an exact rarity: upstream never names SAR,
    /// folding it into "SR", and the two share the 2-star rung. Within a Deluxe set the rung is
    /// additionally split by printing — plain codes yield only variant 0, foil codes only the
    /// variants above it.
    /// </summary>
    private IReadOnlyList<PocketCard> Candidates(
        string set, string code,
        IReadOnlyDictionary<int, IReadOnlyList<PocketCard>> byRung)
    {
        if (BaseCodeOfFoil(code) is string baseCode)
        {
            if (_index.Ladder.IndexOf(baseCode) is not int fr || !byRung.TryGetValue(fr, out var fc))
                return [];
            return fc.Where(c => c.VariantIndex > 0).ToArray();
        }

        if (_index.Ladder.IndexOf(code) is not int rung || !byRung.TryGetValue(rung, out var cards))
            return [];

        return FoilRungs(set).Contains(rung)
            ? cards.Where(c => c.VariantIndex == 0).ToArray()
            : cards;
    }

    /// <summary>
    /// Ownership keys of every foil printing: the second print of a 1-3 diamond card in a set
    /// whose pull rates name foil slot codes, which today means the Deluxe set.
    ///
    /// A foil is a separate collectible with its own ownership key, so a target like "all
    /// diamonds in A4b" demands both printings — 139 extra cards, obtainable only from a
    /// limited-time pack. This is the set the UI offers to exclude.
    /// </summary>
    public IReadOnlySet<string> FoilOwnershipKeys =>
        _foilKeys ??= BuildFoilKeys();

    private IReadOnlySet<string>? _foilKeys;

    private IReadOnlySet<string> BuildFoilKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var set in _rates.ModelledSets)
        {
            var rungs = FoilRungs(set);
            if (rungs.Count == 0) continue;

            if (!_index.BySet.TryGetValue(set, out var cards)) continue;

            foreach (var card in cards)
            {
                // Same test the odds engine uses: a non-zero variant index is only a foil on a
                // rung the set's rates name a foil code for. Anywhere else it is an alternate
                // art, and treating those as foils would drop real cards from targets.
                if (card.VariantIndex == 0) continue;
                if (_index.Ladder.IndexOf(card.Rarity) is not int rung || !rungs.Contains(rung)) continue;

                keys.Add(card.OwnershipKey);
            }
        }

        return keys;
    }

    /// <summary>Sets that have foil printings at all, so the UI can offer the choice only there.</summary>
    public IEnumerable<string> SetsWithFoils =>
        _rates.ModelledSets.Where(set => FoilRungs(set).Count > 0);

    /// <summary>Pack keys the engine can actually price, i.e. those whose set has rate data.</summary>
    public IEnumerable<string> PriceablePacks =>
        _index.OpenablePackKeys.Where(k => _rates.Covers(k.Split(':')[0]));

    /// <summary>
    /// Packs a player could open but which cannot be priced, because their set has no published
    /// pull rates yet — currently just the newest set. Callers disclose these rather than
    /// omitting them, since a set missing from a ranking reads as "nothing to gain here".
    ///
    /// Promo groupings are not included. They have no rates either, but they are not packs
    /// anyone can choose to open.
    /// </summary>
    public IEnumerable<string> UnpriceablePacks =>
        _index.OpenablePackKeys.Where(k => !_rates.Covers(k.Split(':')[0]));

    /// <summary>
    /// Expected copies of each card from one pack of <paramref name="packKey"/>.
    ///
    /// Expected count, not probability-of-at-least-one. The two are nearly identical at real
    /// pull rates, but count is the arrival rate the Poisson model for multi-copy demand needs,
    /// and it is cheaper to compute.
    /// </summary>
    public IReadOnlyDictionary<string, double> ExpectedCopies(string packKey)
    {
        if (_cache.TryGetValue(packKey, out var cached)) return cached;

        var result = new Dictionary<string, double>();
        var set = packKey.Split(':')[0];
        if (!_index.ByPack.TryGetValue(packKey, out var inPack))
            return _cache.GetOrAdd(packKey, result);

        // A slot names a rarity code, but that code denotes a rarity rung. Upstream never names
        // SAR anywhere, folding it into "SR", and SR and SAR share the 2-star rung. Grouping by
        // rung is therefore required: counting only exact-code matches prices every SAR card as
        // unobtainable and misprices every other 2-star card.
        var byRung = inPack
            .GroupBy(c => _index.Ladder.IndexOf(c.Rarity))
            .Where(g => g.Key is not null)
            .ToDictionary(g => g.Key!.Value, g => (IReadOnlyList<PocketCard>)g.ToArray());

        foreach (var (_, weight, variant) in _rates.Variants(set))
        {
            // Enumerate slots. Keys are opaque and not consistently 1-based: the "Themed Rare
            // Pack" numbers its slots 0..4 while every other variant uses 1..n, so indexing by
            // position would read the wrong slot.
            foreach (var slot in variant.Slots.Values)
            {
                // A slot always yields exactly one card, so its distribution must sum to 1.
                // Upstream leaves 90 of 254 slots summing to 99.996-99.999 (rounding), which
                // would imply a chance of an empty slot. Normalise, as variant weights are.
                var slotTotal = slot.Values.Sum();
                if (slotTotal <= 0) continue;

                foreach (var (code, pct) in slot)
                {
                    // Mass that resolves to no cards is dropped rather than redistributed;
                    // redistributing would invent chances at cards that do not exist.
                    var candidates = Candidates(set, code, byRung);
                    if (candidates.Count == 0) continue;

                    var perCard = weight * (pct / slotTotal) / candidates.Count;
                    if (perCard <= 0) continue;

                    foreach (var c in candidates)
                        result[c.Key] = result.TryGetValue(c.Key, out var v) ? v + perCard : perCard;
                }
            }
        }

        return _cache.GetOrAdd(packKey, result);
    }

    private readonly ConcurrentDictionary<string, Dictionary<string, double>> _bestRatesCache = new();

    /// <summary>
    /// For every card, the best per-pack rate available anywhere — the rate you get by always
    /// opening the pack most likely to yield it.
    ///
    /// Precomputed in one pass and cached. Computing it per demand made the target advisor
    /// O(demands x packs), about 78k lookups per estimate on every tap; this reduces each
    /// estimate to O(demands). Depends only on the data, so caching is safe.
    /// </summary>
    /// <param name="unavailablePacks">
    /// Packs not currently purchasable, e.g. a limited-time Deluxe pack that is out of rotation.
    /// Deluxe reprints earlier sets, so while it is away those reprinted cards fall back to
    /// their original packs' odds.
    /// </param>
    public IReadOnlyDictionary<string, double> BestRatesByCard(
        IReadOnlySet<string>? unavailablePacks = null)
    {
        // THE KEY COST MORE THAN THE LOOKUP.
        // ------------------------------------------------------------------------------
        // Every call used to enumerate PriceablePacks -- which splits each pack key and asks the
        // rate table whether it covers that set -- copy the result to an array, sort it, and join
        // it into a string, purely to find out whether the answer was already cached. On a hit,
        // which is nearly every call, all of that was thrown away.
        //
        // Which would be a rounding error if this were called once per estimate, and it is not:
        // RouteCost.Pull calls it once per CARD. Recommending a board for a fresh profile prices
        // every outstanding demand, so opening the wishlist rebuilt that key about 3,500 times and
        // blocked the browser's one thread for 5.4 seconds -- on a desktop.
        //
        // No unavailable packs is the overwhelmingly common case and needs no key at all, so it
        // gets a field. Publishing a reference is atomic, and the dictionary is complete before it
        // is assigned, so a reader either sees the finished table or builds its own -- the same
        // guarantee GetOrAdd gives, whose factory can also run twice.
        if (unavailablePacks is not { Count: > 0 })
            return _allPacksRates ??= BestRates(PriceablePacks.ToArray());

        var packs = PriceablePacks.Where(p => !unavailablePacks.Contains(p)).ToArray();
        var cacheKey = string.Join('|', packs.OrderBy(p => p, StringComparer.Ordinal));

        // Built and published atomically, so a concurrent reader never sees a half-filled
        // dictionary and reads a zero rate as "unobtainable".
        return _bestRatesCache.GetOrAdd(cacheKey, _ => BestRates(packs));
    }

    /// <summary>The table itself, for one set of packs. Shared by both paths above.</summary>
    private Dictionary<string, double> BestRates(string[] packs)
    {
        var best = new Dictionary<string, double>();
        foreach (var pack in packs)
        foreach (var (cardKey, rate) in ExpectedCopies(pack))
            if (!best.TryGetValue(cardKey, out var current) || rate > current)
                best[cardKey] = rate;
        return best;
    }

    private Dictionary<string, double>? _allPacksRates;

    /// <summary>Priceable packs that are limited-time, so the caller can ask about them.</summary>
    public IEnumerable<string> LimitedTimePacks =>
        PriceablePacks.Where(p => GameRules.IsLimitedTimePack(p.Split(':')[1]));

    /// <summary>Expected copies of one card from one pack; 0 if that pack cannot yield it.</summary>
    public double ExpectedCopiesOf(string packKey, string cardKey) =>
        ExpectedCopies(packKey).TryGetValue(cardKey, out var v) ? v : 0.0;

    /// <summary>
    /// Chance that one pack contains at least one card that advances the given demands.
    ///
    /// Computed per variant as 1 − Π_slots(1 − Σ useful), treating a pack's slots as separate
    /// draws. A card stops being useful once its demand is met, so the ranking does not chase
    /// cards you already have enough of.
    /// </summary>
    public double ChanceOfUseful(string packKey, IReadOnlyCollection<Demand> outstanding)
    {
        if (outstanding.Count == 0) return 0.0;

        var set = packKey.Split(':')[0];
        if (!_index.ByPack.TryGetValue(packKey, out var inPack)) return 0.0;

        var wanted = outstanding
            .SelectMany(d => d.SuppliedBy)
            .Select(c => c.Key)
            .ToHashSet();
        if (wanted.Count == 0) return 0.0;

        var byRung = inPack
            .GroupBy(c => _index.Ladder.IndexOf(c.Rarity))
            .Where(g => g.Key is not null)
            .ToDictionary(g => g.Key!.Value, g => (IReadOnlyList<PocketCard>)g.ToArray());

        var miss = 0.0;
        foreach (var (_, weight, variant) in _rates.Variants(set))
        {
            var variantMiss = 1.0;
            foreach (var slot in variant.Slots.Values)
            {
                var slotTotal = slot.Values.Sum();
                if (slotTotal <= 0) continue;

                var hit = 0.0;
                foreach (var (code, pct) in slot)
                {
                    var candidates = Candidates(set, code, byRung);
                    if (candidates.Count == 0) continue;

                    var useful = candidates.Count(c => wanted.Contains(c.Key));
                    if (useful == 0) continue;

                    hit += (pct / slotTotal) * useful / candidates.Count;
                }
                variantMiss *= 1.0 - Math.Clamp(hit, 0.0, 1.0);
            }
            miss += weight * variantMiss;
        }

        return Math.Clamp(1.0 - miss, 0.0, 1.0);
    }

    /// <summary>
    /// Expected packs until the next card that advances the target. Infinite when the pack can
    /// never help; callers render that as "nothing here" rather than a number.
    /// </summary>
    public double ExpectedPacksToNextUseful(string packKey, IReadOnlyCollection<Demand> outstanding)
    {
        var p = ChanceOfUseful(packKey, outstanding);
        return p <= 0 ? double.PositiveInfinity : 1.0 / p;
    }

    /// <summary>
    /// Where in the pack the chance actually is, position by position.
    ///
    /// <see cref="ChanceOfUseful"/> answers "does this pack help", which is the right figure for
    /// ranking and hides something worth knowing: a five-card pack's first three slots are
    /// one-diamond commons and its last two carry everything rare, so a headline 7% can be 7% in
    /// the fifth card alone. That changes what a pack is worth to someone who has finished the
    /// commons, and it is the reason the same headline means different things in two sets.
    ///
    /// Adjacent positions with the same chance are merged into one range, which is what produces
    /// the "1st-3rd card" reading. Merged from the numbers rather than hardcoded: which slots
    /// share a distribution is a property of each set's published rates, and a set that breaks the
    /// three-commons pattern would otherwise be reported wrongly rather than merely unmerged.
    /// </summary>
    /// <returns>
    /// One entry per group of positions, in pack order. Empty when the pack has no published
    /// rates, exactly as the other odds methods return zero rather than guessing.
    /// </returns>
    public IReadOnlyList<SlotOdds> ChanceOfUsefulBySlot(
        string packKey, IReadOnlyCollection<Demand> outstanding)
    {
        if (outstanding.Count == 0) return [];

        var set = packKey.Split(':')[0];
        if (!_index.ByPack.TryGetValue(packKey, out var inPack)) return [];

        var wanted = outstanding
            .SelectMany(d => d.SuppliedBy)
            .Select(c => c.Key)
            .ToHashSet();
        if (wanted.Count == 0) return [];

        var byRung = inPack
            .GroupBy(c => _index.Ladder.IndexOf(c.Rarity))
            .Where(g => g.Key is not null)
            .ToDictionary(g => g.Key!.Value, g => (IReadOnlyList<PocketCard>)g.ToArray());

        // Per position: the weighted chance, and the weight that voted on it. Two variants of the
        // same pack can hold different numbers of cards -- a rare-pack variant is one draw, the
        // ordinary one five -- so a position present in only some of them must be averaged over
        // those, not over every variant. Dividing by the full weight would report a fifth-card
        // chance diluted by variants that have no fifth card.
        var chance = new Dictionary<int, double>();
        var voted = new Dictionary<int, double>();

        foreach (var (_, weight, variant) in _rates.Variants(set))
        {
            // Ordered by key, not by dictionary order: the keys are opaque strings and the "Themed
            // Rare Pack" numbers its slots from zero where everything else starts at one, so
            // position is the rank of the key rather than the key itself.
            var slots = variant.Slots
                .OrderBy(kv => int.TryParse(kv.Key, out var n) ? n : int.MaxValue)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Value)
                .ToArray();

            for (var i = 0; i < slots.Length; i++)
            {
                var slotTotal = slots[i].Values.Sum();
                if (slotTotal <= 0) continue;

                var hit = 0.0;
                foreach (var (code, pct) in slots[i])
                {
                    var candidates = Candidates(set, code, byRung);
                    if (candidates.Count == 0) continue;

                    var useful = candidates.Count(c => wanted.Contains(c.Key));
                    if (useful == 0) continue;

                    hit += (pct / slotTotal) * useful / candidates.Count;
                }

                var position = i + 1;
                chance[position] = chance.GetValueOrDefault(position) + weight * Math.Clamp(hit, 0, 1);
                voted[position] = voted.GetValueOrDefault(position) + weight;
            }
        }

        if (chance.Count == 0) return [];

        var perPosition = chance.Keys
            .OrderBy(k => k)
            .Select(k => (Position: k, Chance: voted[k] > 0 ? chance[k] / voted[k] : 0.0))
            .ToArray();

        // Merge runs of equal chance. The tolerance is there because these are sums of normalised
        // percentages: two slots with identical published distributions can differ in the last
        // bit, and reporting "1st card / 2nd card / 3rd card" with three identical figures would
        // be noise dressed as detail.
        var groups = new List<SlotOdds>();
        var start = perPosition[0].Position;
        var value = perPosition[0].Chance;
        var last = start;

        foreach (var (position, c) in perPosition.Skip(1))
        {
            if (position == last + 1 && Math.Abs(c - value) < 1e-9) { last = position; continue; }

            groups.Add(new SlotOdds(start, last, value));
            start = last = position;
            value = c;
        }
        groups.Add(new SlotOdds(start, last, value));

        return groups;
    }
}

/// <summary>
/// The chance that one group of card positions in a pack yields something still wanted.
/// </summary>
/// <param name="First">1-based position of the first card in the group.</param>
/// <param name="Last">Same as <paramref name="First"/> for a group of one.</param>
/// <param name="Chance">Probability that this group of positions supplies a wanted card.</param>
public sealed record SlotOdds(int First, int Last, double Chance)
{
    /// <summary>"1st card", "1st-3rd card" — the reading a player recognises from the pack.</summary>
    public string Label => First == Last ? $"{Ordinal(First)} card" : $"{Ordinal(First)}-{Ordinal(Last)} card";

    private static string Ordinal(int n) => n switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{n}th",
    };
}
