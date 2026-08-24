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
    /// The rate table this engine was built over. Exposed so callers can report WHICH sets are
    /// priced with borrowed rates — a number derived from an assumption has to be labelled as
    /// one, and only the table knows.
    /// </summary>
    public PullRates Rates => _rates;

    // Expected copies per pack, by pack key then card key. Built once per pack on demand:
    // depends only on the data, never on the collection, so caching is safe.
    //
    // Concurrent, even though Blazor WebAssembly is single-threaded: these caches are
    // populated lazily, and a plain Dictionary corrupts itself under concurrent lazy fill.
    // The test suite runs classes in parallel over a shared instance and found exactly that.
    private readonly ConcurrentDictionary<string, Dictionary<string, double>> _cache = new();
    private readonly ConcurrentDictionary<string, HashSet<int>> _foilRungs = new();

    public PackOdds(CardIndex index, PullRates rates)
    {
        _index = index;
        _rates = rates;
    }


    /// <summary>
    /// Rungs for which a set's pull rates name a foil code (CF/UF/RF). Only Deluxe sets do
    /// this. Its presence is what licenses splitting a rung into plain and foil printings —
    /// a non-zero variant index means "alternate art" in every other set, so keying off the
    /// index alone would wrongly exclude legitimate alternate arts elsewhere.
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
    /// The code denotes a rarity RUNG, not an exact rarity: upstream never names SAR, folding
    /// it into "SR", and the two share the 2-star rung. Within a Deluxe set the rung is
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
    /// Ownership keys of every FOIL printing: the second print of a 1-3 diamond card in a set
    /// whose pull rates name foil slot codes, which today means the Deluxe set.
    ///
    /// Exposed because a foil is a separate collectible with its own ownership key, so a target
    /// like "all diamonds in A4b" silently demands both printings — 139 extra cards, obtainable
    /// only from a limited-time pack, that plenty of collectors do not chase. Whether to want
    /// them is the user's call, and this is the set they need to be able to exclude.
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
                // Same test the odds engine uses: a non-zero variant index is only a FOIL on a
                // rung the set's rates name a foil code for. Anywhere else it is an alternate
                // art, and treating those as foils would quietly drop real cards from targets.
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
    /// Packs a player could open but which cannot be priced, because their set has no
    /// published pull rates yet — currently just the newest set. Callers must DISCLOSE these
    /// rather than omit them: a set silently missing from a ranking reads as "nothing to gain
    /// here", which is the opposite of the truth.
    ///
    /// Promo groupings are NOT included. They have no rates either, but they are not packs
    /// anyone can choose to open, so reporting them as a gap would be telling the user about
    /// a problem they can do nothing about.
    /// </summary>
    public IEnumerable<string> UnpriceablePacks =>
        _index.OpenablePackKeys.Where(k => !_rates.Covers(k.Split(':')[0]));

    /// <summary>
    /// Expected copies of each card from one pack of <paramref name="packKey"/>.
    ///
    /// Expected COUNT, not probability-of-at-least-one. The two are nearly identical at
    /// real pull rates, but count is the correct arrival rate for the Poisson model that
    /// multi-copy demand needs, and it is cheaper to compute.
    /// </summary>
    public IReadOnlyDictionary<string, double> ExpectedCopies(string packKey)
    {
        if (_cache.TryGetValue(packKey, out var cached)) return cached;

        var result = new Dictionary<string, double>();
        var set = packKey.Split(':')[0];
        if (!_index.ByPack.TryGetValue(packKey, out var inPack))
            return _cache.GetOrAdd(packKey, result);

        // A slot names a rarity CODE, but that code denotes a rarity RUNG. Upstream never
        // names SAR anywhere, folding it into "SR" — and SR and SAR share the 2-star rung.
        // Grouping by rung is therefore mandatory: counting only exact-code matches would
        // price every SAR card as unobtainable and misprice every other 2-star card.
        var byRung = inPack
            .GroupBy(c => _index.Ladder.IndexOf(c.Rarity))
            .Where(g => g.Key is not null)
            .ToDictionary(g => g.Key!.Value, g => (IReadOnlyList<PocketCard>)g.ToArray());

        foreach (var (_, weight, variant) in _rates.Variants(set))
        {
            // Enumerate slots. Keys are opaque and NOT consistently 1-based: the
            // "Themed Rare Pack" numbers its slots 0..4 while every other variant uses
            // 1..n, so indexing by position would silently read the wrong slot.
            foreach (var slot in variant.Slots.Values)
            {
                // A slot ALWAYS yields exactly one card, so its distribution must sum to 1.
                // Upstream leaves 90 of 254 slots summing to 99.996-99.999 (rounding), which
                // would otherwise imply a small chance of an empty slot — physically
                // impossible. Normalise, exactly as variant weights are normalised.
                var slotTotal = slot.Values.Sum();
                if (slotTotal <= 0) continue;

                foreach (var (code, pct) in slot)
                {
                    // Mass that resolves to no cards is dropped, never redistributed:
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
    /// For every card, the best per-pack rate available anywhere — i.e. the rate you get by
    /// always opening the pack most likely to yield it.
    ///
    /// Precomputed in one pass and cached. Computing it per demand instead made the target
    /// advisor O(demands x packs), which is ~78k lookups per estimate and ran on every tap;
    /// this reduces each estimate to O(demands). Depends only on the data, never on the
    /// collection, so caching is safe.
    /// </summary>
    /// <param name="unavailablePacks">
    /// Packs not currently purchasable, e.g. a limited-time Deluxe pack that is out of
    /// rotation. Excluding them matters beyond the pack itself: Deluxe reprints earlier sets,
    /// so while it is away those reprinted cards fall back to their original packs' odds.
    /// </param>
    public IReadOnlyDictionary<string, double> BestRatesByCard(
        IReadOnlySet<string>? unavailablePacks = null)
    {
        var packs = (unavailablePacks is { Count: > 0 }
            ? PriceablePacks.Where(p => !unavailablePacks.Contains(p))
            : PriceablePacks).ToArray();

        var cacheKey = string.Join('|', packs.OrderBy(p => p, StringComparer.Ordinal));

        // Built and published atomically: handing out a half-filled dictionary would let a
        // concurrent reader see zero rates and conclude a card is unobtainable.
        return _bestRatesCache.GetOrAdd(cacheKey, _ =>
        {
            var best = new Dictionary<string, double>();
            foreach (var pack in packs)
            foreach (var (cardKey, rate) in ExpectedCopies(pack))
                if (!best.TryGetValue(cardKey, out var current) || rate > current)
                    best[cardKey] = rate;
            return best;
        });
    }

    /// <summary>Priceable packs that are limited-time, so the caller can ask about them.</summary>
    public IEnumerable<string> LimitedTimePacks =>
        PriceablePacks.Where(p => GameRules.IsLimitedTimePack(p.Split(':')[1]));

    /// <summary>Expected copies of one card from one pack; 0 if that pack cannot yield it.</summary>
    public double ExpectedCopiesOf(string packKey, string cardKey) =>
        ExpectedCopies(packKey).TryGetValue(cardKey, out var v) ? v : 0.0;

    /// <summary>
    /// Chance that one pack contains at least one card that advances the given demands.
    ///
    /// Computed per variant as 1 − Π_slots(1 − Σ useful), which respects the fact that a
    /// pack's slots are separate draws. A card stops being useful once its demand is met,
    /// which is what keeps the ranking from chasing cards you already have enough of.
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
    /// Expected packs until the next card that advances the target. Infinite when the pack
    /// can never help, which callers should render as "nothing here" rather than a number.
    /// </summary>
    public double ExpectedPacksToNextUseful(string packKey, IReadOnlyCollection<Demand> outstanding)
    {
        var p = ChanceOfUseful(packKey, outstanding);
        return p <= 0 ? double.PositiveInfinity : 1.0 / p;
    }
}
