namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;

/// <param name="AtCap">
/// True when further packs of this set earn NOTHING. The most actionable warning in the app:
/// every pack opened while capped wastes its points entirely.
/// </param>
public sealed record SetPoints(
    string Set,
    int Balance,
    bool AtCap,
    int PacksUntilCap,
    IReadOnlyList<PocketCard> AffordableNow);

/// <summary>
/// Per-set pack-point balances. Points are earned at a flat rate per pack, are spendable
/// ONLY within the set whose packs earned them, and stop accruing at a hard cap.
/// </summary>
public sealed class PointsLedger
{
    private readonly CardIndex _index;
    private readonly IReadOnlyDictionary<string, Rarity> _rarities;

    public PointsLedger(CardIndex index, IReadOnlyDictionary<string, Rarity> rarities)
    {
        _index = index;
        _rarities = rarities;
    }

    /// <summary>Points earned from a log of opened packs. Promo packs award none.</summary>
    public IReadOnlyDictionary<string, int> EarnedFrom(IEnumerable<(string Set, int Packs)> opened)
    {
        var result = new Dictionary<string, int>();
        foreach (var (set, packs) in opened)
        {
            if (set.StartsWith("PROMO", StringComparison.OrdinalIgnoreCase)) continue;
            var earned = packs * GameRules.PackPointsPerPack;
            result[set] = Math.Min(GameRules.PackPointsCap,
                (result.TryGetValue(set, out var c) ? c : 0) + earned);
        }
        return result;
    }

    /// <summary>
    /// State of one set's points, including which missing cards the balance could buy right
    /// now — the whole reason to track this rather than just show a number.
    /// </summary>
    public SetPoints Describe(string set, int balance, Collection owned, int copiesWanted = 1)
    {
        var capped = balance >= GameRules.PackPointsCap;
        var untilCap = capped
            ? 0
            : (int)Math.Ceiling((GameRules.PackPointsCap - balance) / (double)GameRules.PackPointsPerPack);

        var affordable = _index.BySet.TryGetValue(set, out var cards)
            ? cards.DistinctBy(c => c.OwnershipKey)
                   .Where(c => owned.Of(c) < copiesWanted)
                   .Where(c => _rarities.TryGetValue(c.Rarity, out var r)
                               && r.Points > 0 && r.Points <= balance)
                   .OrderByDescending(c => _rarities[c.Rarity].Points)
                   .ToArray()
            : [];

        return new SetPoints(set, balance, capped, untilCap, affordable);
    }

    /// <summary>
    /// Sets where points are being wasted, or about to be. Ordered worst-first so the UI can
    /// lead with the set that is actively losing value on every pack.
    /// </summary>
    public IReadOnlyList<SetPoints> Warnings(
        IReadOnlyDictionary<string, int> balances, Collection owned, int copiesWanted = 1)
    {
        return balances
            .Select(kv => Describe(kv.Key, kv.Value, owned, copiesWanted))
            .Where(s => s.AtCap || s.PacksUntilCap <= 20)
            .OrderBy(s => s.PacksUntilCap)
            .ToArray();
    }
}
