namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;

/// <param name="AtCap">
/// True when further packs of this set earn NOTHING. The most actionable warning in the app:
/// every pack opened while capped wastes its points entirely.
/// </param>
/// <param name="RarestWanted">
/// The rarest card from this set the user still needs - the thing the balance is being saved
/// for. Null when nothing is outstanding, or when the set has no shop prices.
/// </param>
/// <param name="RarestPoints">Its price, so the gap can be shown rather than just the target.</param>
/// <param name="PacksToAfford">
/// Packs OF THIS SET still to open before that card is affordable, zero when it already is.
/// Of this set specifically: points cannot cross-fund, so packs of anything else do not count.
/// </param>
public sealed record SetPoints(
    string Set,
    int Balance,
    bool AtCap,
    int PacksUntilCap,
    IReadOnlyList<PocketCard> AffordableNow,
    PocketCard? RarestWanted = null,
    int RarestPoints = 0,
    int PacksToAfford = 0);

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
    /// <param name="plan">
    /// What the user actually collects. Load-bearing, not a refinement: a rung they want zero
    /// copies of is a rung they are ignoring, so a card on it is not "still wanted" at any price.
    /// Without this the shop advice recommended saving for a Crown to someone who collects
    /// diamonds - the most expensive card in the set, and one they had told us not to chase.
    /// </param>
    public SetPoints Describe(string set, int balance, Collection owned, RarityPlan plan)
    {
        var capped = balance >= GameRules.PackPointsCap;
        var untilCap = capped
            ? 0
            : (int)Math.Ceiling((GameRules.PackPointsCap - balance) / (double)GameRules.PackPointsPerPack);

        _index.BySet.TryGetValue(set, out var cards);

        // Outstanding under the user's own plan, computed once and used by both figures below so
        // they can never disagree about what "still wanted" means.
        var wanted = (cards ?? [])
            .DistinctBy(c => c.OwnershipKey)
            .Select(c => (Card: c, Rung: _index.Ladder.IndexOf(c.Rarity)))
            .Where(x => x.Rung is not null && plan.Copies(x.Rung.Value) > 0)
            .Where(x => owned.Of(x.Card) < plan.Copies(x.Rung!.Value))
            .Where(x => _rarities.TryGetValue(x.Card.Rarity, out var r) && r.Points > 0)
            .ToArray();

        var affordable = wanted
            .Where(x => _rarities[x.Card.Rarity].Points <= balance)
            .OrderByDescending(x => _rarities[x.Card.Rarity].Points)
            .Select(x => x.Card)
            .ToArray();

        // The rarest card still wanted, which is what a balance is actually being saved for.
        // Rarest by LADDER RUNG rather than by price, because the two disagree: an Immersive
        // costs 1,500 against a 2-star's 1,250 while sitting a rung above it, and a Shiny 2-star
        // costs more than a Crown's rung would suggest. "The rarest one I still need" is the
        // question a collector asks; the price is then whatever it happens to be.
        var best = wanted
            .Select(x => (x.Card, Rung: x.Rung!.Value, Points: _rarities[x.Card.Rarity].Points))
            .OrderByDescending(x => x.Rung)
            .ThenByDescending(x => x.Points)
            .FirstOrDefault();

        var rarest = best.Card;
        var rarestPoints = best.Card is null ? 0 : best.Points;

        var packsToAfford = rarest is null
            ? 0
            : (int)Math.Ceiling(Math.Max(0, rarestPoints - balance) / (double)GameRules.PackPointsPerPack);

        return new SetPoints(set, balance, capped, untilCap, affordable,
                             rarest, rarestPoints, packsToAfford);
    }

    /// <summary>
    /// Sets where points are being wasted, or about to be. Ordered worst-first so the UI can
    /// lead with the set that is actively losing value on every pack.
    /// </summary>
    /// <param name="planFor">
    /// The plan for a given set, since targets are per set: someone chasing stars in the newest
    /// set and diamonds everywhere else must not have one plan applied to both.
    /// </param>
    public IReadOnlyList<SetPoints> Warnings(
        IReadOnlyDictionary<string, int> balances, Collection owned, Func<string, RarityPlan> planFor)
    {
        return balances
            .Select(kv => Describe(kv.Key, kv.Value, owned, planFor(kv.Key)))
            .Where(s => s.AtCap || s.PacksUntilCap <= 20)
            .OrderBy(s => s.PacksUntilCap)
            .ToArray();
    }
}
