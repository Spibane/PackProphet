namespace PackProphet.Data;

using PackProphet.Domain;

/// <summary>
/// Per-set pack pull rates: the irreplaceable dependency that makes every recommendation
/// real math rather than a heuristic.
///
/// Coverage is NOT total. In the current snapshot the newest set (B4) and both promo
/// sets have no rate data at all, so the odds engine can price neither. That must be
/// disclosed to the user rather than silently dropped — a set quietly missing from a
/// ranking looks like "nothing to gain here", which is the opposite of the truth.
/// </summary>
public sealed class PullRates
{
    private readonly IReadOnlyDictionary<string, Dictionary<string, PackVariant>> _bySet;

    public PullRates(
        IReadOnlyDictionary<string, Dictionary<string, PackVariant>> bySet,
        IReadOnlyDictionary<string, string>? assumedFrom = null)
    {
        _bySet = bySet;
        AssumedFrom = assumedFrom ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Sets the engine can actually compute odds for.</summary>
    public IEnumerable<string> ModelledSets => _bySet.Keys;

    /// <summary>True when this set has rate data. False for brand-new and promo sets.</summary>
    public bool Covers(string set) => _bySet.ContainsKey(set);

    /// <summary>
    /// Sets whose rates are BORROWED from another set, mapped to the set they came from.
    ///
    /// Every surface that reports a number derived from one of these must say so. A borrowed
    /// distribution is a good estimate — the standard five-card pack has had the same shape for
    /// two years — but it is still an assumption, and an assumption presented as measured data
    /// is how a tool loses the user's trust the first time it is wrong.
    /// </summary>
    public IReadOnlyDictionary<string, string> AssumedFrom { get; }

    public bool IsAssumed(string set) => AssumedFrom.ContainsKey(set);

    /// <summary>The variants exactly as published, for copying. Empty for an uncovered set.</summary>
    public IReadOnlyDictionary<string, PackVariant> Published(string set) =>
        _bySet.TryGetValue(set, out var vs) ? vs : new Dictionary<string, PackVariant>();

    /// <summary>
    /// A copy of this table with <paramref name="sets"/> priced using <paramref name="donor"/>'s
    /// distributions.
    ///
    /// Only the slot SHAPE is borrowed, never per-card probabilities: the engine divides each
    /// slot's rarity share by how many cards of that rung the pack actually holds, so a borrowed
    /// distribution automatically adapts to the new set's own contents. Any share naming a rarity
    /// the new set does not have is dropped rather than redistributed, exactly as it is for
    /// published rates — inventing chances at cards that do not exist would be worse than being
    /// slightly conservative.
    ///
    /// A set that already has published rates is never overwritten. Real data always wins.
    /// </summary>
    public PullRates Assuming(IEnumerable<string> sets, string donor)
    {
        if (!_bySet.TryGetValue(donor, out var template) || template.Count == 0) return this;

        var next = new Dictionary<string, Dictionary<string, PackVariant>>(_bySet, StringComparer.OrdinalIgnoreCase);
        var assumed = new Dictionary<string, string>(AssumedFrom, StringComparer.OrdinalIgnoreCase);

        foreach (var set in sets)
        {
            if (string.IsNullOrWhiteSpace(set) || next.ContainsKey(set)) continue;

            next[set] = template;
            assumed[set] = donor;
        }

        return new PullRates(next, assumed);
    }

    /// <summary>
    /// The set to borrow rates from by default: the most recently released set that has published
    /// rates and sells ordinary packs.
    ///
    /// Most recent because pack structure drifts — slot counts and the rarities on offer have both
    /// changed across series — so the newest measured set is the closest thing to "what a pack
    /// looks like now". Limited-time sets are excluded because they are structurally unlike the
    /// rest: a Deluxe pack holds four cards and guarantees a 4-diamond, so copying it would price
    /// an ordinary set as far better than it is.
    /// </summary>
    public string? StandardDonor(SetCatalog sets)
    {
        return ModelledSets
            .Where(set => !IsAssumed(set))
            .Where(set => !CardIndex.IsPromoSet(set))
            .Where(set => sets.Info(set)?.Packs is not { } packs
                          || !packs.Any(GameRules.IsLimitedTimePack))
            .OrderByDescending(set => sets.ReleaseDateOf(set) ?? DateOnly.MinValue)
            .ThenByDescending(set => set, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Variants for a set, with appearance rates normalised to sum to exactly 1.0.
    /// Upstream sums to 99.999 for several sets; normalising keeps probabilities honest
    /// instead of leaking a fraction of a percent of probability mass.
    /// </summary>
    public IReadOnlyList<(string Name, double Weight, PackVariant Variant)> Variants(string set)
    {
        if (!_bySet.TryGetValue(set, out var vs) || vs.Count == 0) return [];

        var total = vs.Values.Sum(v => v.AppearanceRate);
        // A set with no variants is legitimate (uncovered set, handled above). Variants
        // that exist but carry zero total weight is a deserialization bug, not missing
        // data — fail loudly, because the silent version of this returns "no odds" and
        // looks exactly like a complete collection.
        if (total <= 0)
            throw new InvalidOperationException(
                $"Set '{set}' has {vs.Count} pack variant(s) but their appearance rates sum to {total}. " +
                "This usually means appearance_rate failed to deserialize.");

        return vs.Select(kv => (kv.Key, kv.Value.AppearanceRate / total, kv.Value)).ToArray();
    }
}
