namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.State;

/// <summary>
/// Which of the four gates is stopping a trade. Named rather than collapsed into a yes or no,
/// since the advice differs: dust accumulates on its own, stamina arrives on a clock, a spare has
/// to be pulled, and an untradeable rarity never becomes tradeable.
/// </summary>
public enum TradeGate
{
    /// <summary>Nothing is in the way.</summary>
    None,

    /// <summary>3-star, Crown, or a promo. No amount of dust or waiting changes this.</summary>
    NotTradeable,

    /// <summary>Trades are same-rarity, and there is no surplus copy at that rarity to offer.</summary>
    NoSpareAtRarity,

    /// <summary>The rarity's shinedust price is more than the balance.</summary>
    NotEnoughDust,

    /// <summary>No Trade Stamina. Costs exactly one, whatever the rarity.</summary>
    NoStamina
}

/// <param name="PacksSaved">
/// Expected packs this trade spares you, from the card's own pull rate. Stamina costs a flat one
/// per trade, so this is also the value per stamina.
/// </param>
/// <param name="CouldOffer">
/// Surplus cards at the same rarity that could pay for it, by name. Not by value: every card at a
/// rarity costs the same dust, so which one to part with is the user's call.
/// </param>
/// <param name="Points">The card's price in that set's pack points, or zero if it has none.</param>
/// <param name="PointsShort">
/// Points still to earn before the shop could sell it, given the balance in its set. A different
/// measure of "far away" from packs-saved: that says how hard the card is to pull, this says how
/// far the guaranteed route is from being affordable. They disagree often — a Crown is cheap to
/// pull relative to its point price, and an Immersive is the reverse.
/// </param>
/// <param name="Shareable">
/// A friend could send this one outright - free, nothing back, one a day. It changes the advice:
/// spending 5,000 dust and a stamina on a 4-diamond someone could hand over is a bad trade even
/// with every gate green.
/// </param>
public sealed record TradeCandidate(
    PocketCard Want,
    int CopiesShort,
    double PacksSaved,
    int Dust,
    IReadOnlyList<PocketCard> CouldOffer,
    TradeGate Blocking,
    int Points = 0,
    int PointsShort = 0,
    bool Shareable = false)
{
    public bool Ready => Blocking == TradeGate.None;

    public int Stamina => GameRules.TradeStaminaPerTrade;

    /// <summary>Packs of its own set still to open to afford it from the shop.</summary>
    public int PacksOfPointsShort =>
        (int)Math.Ceiling(PointsShort / (double)GameRules.PackPointsPerPack);
}

/// <summary>
/// Which single trade deserves your next Trade Stamina.
///
/// Stamina regenerates one per 12 hours and every trade costs exactly one, so trading is capped at
/// roughly two a day while shinedust accumulates. Trades rather than currency are therefore the
/// scarce resource, and this is a ranked queue rather than a feasibility checklist.
///
/// Value is expected packs saved, from the card's own pull rate. The flat stamina cost makes value
/// and value-per-stamina the same number. The dust price does not enter the ranking: dust and
/// packs are separate currencies, so dust decides whether a trade is possible rather than whether
/// it is worth making.
///
/// Surplus is defined by the user's plan rather than by "more than one". A second copy is spare
/// where the plan wants one and required where it wants two.
/// </summary>
public sealed class TradeQueue
{
    private readonly CardIndex _index;
    private readonly PackOdds _odds;
    private readonly IReadOnlyDictionary<string, Rarity> _rarities;

    public TradeQueue(CardIndex index, PackOdds odds, IReadOnlyDictionary<string, Rarity> rarities)
    {
        _index = index;
        _odds = odds;
        _rarities = rarities;
    }

    /// <summary>
    /// Whether a card may be traded at all: rarity, and the promo rule that rarity cannot express.
    /// </summary>
    public bool IsTradeable(PocketCard card) =>
        GameRules.CanBeTraded(card)
        && _rarities.TryGetValue(card.Rarity, out var rarity)
        && rarity.TradePrice is not null;

    /// <summary>
    /// Whether a friend could send this card outright: 1 to 4 diamonds, and the promo rule again.
    ///
    /// Separate from <see cref="IsTradeable"/> and not derived from it. A Share has no same-rarity
    /// payment, no dust and no stamina, and the rarities disagree in both directions: a 2-star can
    /// be traded but never shared.
    /// </summary>
    public bool IsShareable(PocketCard card) => GameRules.CanBeShared(card);

    /// <summary>
    /// Copies of one card held beyond what the plan asks for, which is how many trades it could
    /// pay for. Zero for anything untradeable, unheld, or still wanted.
    ///
    /// Separate from <see cref="Surplus"/>, which answers a different question: a card held four
    /// times can fund three trades but appears once in a list of what you could offer. Both use
    /// the same definition of spare.
    /// </summary>
    public int SpareCopies(PocketCard card, Collection owned, Func<string, RarityPlan> planFor)
    {
        if (!IsTradeable(card)) return 0;

        var held = owned.Of(card);
        if (held <= 0) return 0;

        if (_index.Ladder.IndexOf(card.Rarity) is not int rung) return 0;

        // Required by the user's own definition of done. A rung they do not collect wants zero, so
        // every copy of it is spare — which is the pile people trade from.
        return Math.Max(0, held - planFor(card.Set).Copies(rung));
    }

    /// <summary>
    /// Cards you hold beyond what your plan asks for, grouped by rarity code - the pool a trade
    /// can be paid from.
    ///
    /// By rarity code rather than ladder rung, matching the rest of the engine: SR and SAR share a
    /// rung but are different rarities, and the game may refuse one offered for the other.
    /// </summary>
    public IReadOnlyDictionary<string, List<PocketCard>> Surplus(
        Collection owned, Func<string, RarityPlan> planFor)
    {
        var pools = new Dictionary<string, List<PocketCard>>(StringComparer.OrdinalIgnoreCase);

        foreach (var card in _index.All.DistinctBy(c => c.OwnershipKey))
        {
            if (!IsTradeable(card)) continue;

            if (SpareCopies(card, owned, planFor) <= 0) continue;

            if (!pools.TryGetValue(card.Rarity, out var pool)) pools[card.Rarity] = pool = [];
            pool.Add(card);
        }

        foreach (var pool in pools.Values)
        {
            pool.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        return pools;
    }

    /// <summary>
    /// Outstanding cards ranked by how much a trade for them would save, best first.
    ///
    /// Blocked candidates are returned rather than filtered out, so the queue can say "you have
    /// the dust and a spare, but no stamina until tonight" instead of dropping the row.
    /// </summary>
    /// <param name="max">
    /// How many rows to keep. Ranking walks every demand regardless, so a caller that groups or
    /// filters afterwards should ask for everything: the best trade in each pack is usually
    /// nowhere near the top of a global ranking.
    /// </param>
    public IReadOnlyList<TradeCandidate> Rank(
        IEnumerable<Demand> outstanding,
        Collection owned,
        Resources resources,
        Func<string, RarityPlan> planFor,
        int max = 20)
    {
        var surplus = Surplus(owned, planFor);
        var rates = _odds.BestRatesByCard();
        var stamina = Math.Max(0, resources.Trade.Balance);
        var dust = Math.Max(0, resources.Shinedust);

        var result = new List<TradeCandidate>();

        foreach (var demand in outstanding)
        {
            // The cheapest printing to ask for: any printing satisfies the demand, and a trade is
            // priced by rarity.
            var card = demand.SuppliedBy
                .OrderBy(c => _rarities.TryGetValue(c.Rarity, out var r) ? r.TradePrice ?? int.MaxValue : int.MaxValue)
                .First();

            var points = _rarities.TryGetValue(card.Rarity, out var rarity) ? rarity.Points : 0;

            // Points are earned and spent inside one set, so the gap is measured against that
            // set's balance and nowhere else.
            var pointsShort = Math.Max(
                0, points - resources.PackPointsBySet.GetValueOrDefault(card.Set));

            if (!IsTradeable(card))
            {
                // A card that cannot be traded may still be shareable, which for diamonds is the
                // ordinary case once promos are involved, so the row still has advice to give.
                result.Add(new TradeCandidate(card, demand.Remaining, 0, 0, [],
                                              TradeGate.NotTradeable, points, pointsShort,
                                              IsShareable(card)));
                continue;
            }

            var price = _rarities[card.Rarity].TradePrice ?? 0;
            var offerable = surplus.GetValueOrDefault(card.Rarity) ?? [];

            // Packs saved, from the pooled rate across every pack that can yield it. A zero rate
            // means no pack can, which makes a trade the only route, so it is treated as the most
            // valuable case rather than the least.
            var rate = rates.GetValueOrDefault(card.Key);
            var packsSaved = rate > 0 ? 1.0 / rate : double.PositiveInfinity;

            var gate = offerable.Count == 0 ? TradeGate.NoSpareAtRarity
                : dust < price ? TradeGate.NotEnoughDust
                : stamina < GameRules.TradeStaminaPerTrade ? TradeGate.NoStamina
                : TradeGate.None;

            result.Add(new TradeCandidate(card, demand.Remaining, packsSaved, price, offerable,
                                          gate, points, pointsShort, IsShareable(card)));
        }

        // Ready trades first, then by what they save. A blocked trade worth 300 packs is still not
        // something you can do today.
        return result
            .OrderByDescending(c => c.Ready)
            .ThenByDescending(c => c.PacksSaved)
            .Take(max)
            .ToArray();
    }

    /// <summary>
    /// Whether dust is genuinely the constraint, at a given rarity's price.
    ///
    /// Usually it is not: stamina allows about two trades a day, so a balance funding twenty of
    /// them is not the blocker. The comparison is against stamina the pool can reach rather than
    /// the balance now, since a pool that happens to be empty this minute would otherwise make
    /// dust look sufficient forever.
    /// </summary>
    public static bool DustIsTheConstraint(int shinedust, int pricePerTrade, int staminaReachable) =>
        pricePerTrade > 0 && ResourcePlan.DustRunway(shinedust, pricePerTrade) < staminaReachable;
}
