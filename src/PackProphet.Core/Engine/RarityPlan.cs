namespace PackProphet.Engine;

using PackProphet.Data;

/// <summary>
/// How many copies of each rarity count as "complete": a map from ladder rung to copies,
/// where absent or zero means "I do not collect this rarity".
///
/// Selection and quantity are one fact. A rarity you want zero copies of is a rarity you are
/// ignoring, and a separate "which rarities" set plus a separate copy count could not express "two
/// of every diamond but only one of the stars".
/// </summary>
public sealed record RarityPlan(IReadOnlyDictionary<int, int> CopiesByTier)
{
    /// <summary>Copies wanted at a rung; zero when the rarity is not collected.</summary>
    public int Copies(int tierIndex) =>
        CopiesByTier.TryGetValue(tierIndex, out var n) && n > 0 ? n : 0;

    public bool Wants(int tierIndex) => Copies(tierIndex) > 0;

    public IReadOnlySet<int> WantedTiers =>
        CopiesByTier.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToHashSet();

    /// <summary>
    /// True when nothing is collected. Callers refuse to save this: a plan wanting nothing makes
    /// every screen read "complete".
    /// </summary>
    public bool IsEmpty => WantedTiers.Count == 0;

    /// <summary>The same number of copies across the given rungs.</summary>
    public static RarityPlan Uniform(IEnumerable<int> tiers, int copies = 1) =>
        new(tiers.Distinct().ToDictionary(t => t, _ => Math.Max(1, copies)));

    /// <summary>Every rung up to and including <paramref name="topIndex"/>.</summary>
    public static RarityPlan UpTo(RarityLadder ladder, int topIndex, int copies = 1) =>
        Uniform(ladder.UpTo(topIndex), copies);

    /// <summary>All diamonds, one copy each — the default a new collection starts with.</summary>
    public static RarityPlan Default(RarityLadder ladder) =>
        Uniform(ladder.ByGroup("Diamond"), 1);

    public RarityPlan With(int tierIndex, int copies)
    {
        var next = new Dictionary<int, int>(CopiesByTier);
        if (copies > 0) next[tierIndex] = copies; else next.Remove(tierIndex);
        return new RarityPlan(next);
    }

    /// <summary>
    /// Step a rung through none, one, two, none. Two is the useful ceiling: a deck may hold
    /// at most two copies of a name, so more than that is only ever spare stock.
    /// </summary>
    public RarityPlan Cycle(int tierIndex, int max = 2) =>
        With(tierIndex, Copies(tierIndex) >= max ? 0 : Copies(tierIndex) + 1);
}
