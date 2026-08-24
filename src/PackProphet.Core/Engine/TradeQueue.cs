namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.State;

/// <summary>
/// Which of the four gates is stopping a trade. Named rather than collapsed into a yes or no,
/// because the four are fixed by entirely different things and the advice differs completely:
/// dust accumulates on its own, stamina arrives on a clock, a spare has to be pulled, and an
/// untradeable rarity never becomes tradeable at all.
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
/// Expected packs this trade spares you, from the card's own pull rate. With stamina costing a
/// flat one per trade, this IS the value per stamina - there is nothing to divide by.
/// </param>
/// <param name="CouldOffer">
/// Surplus cards at the same rarity that could pay for it, by name. Not by value: every card at a
/// rarity costs the same dust, so there is no dearest one to lead with - only the one you are
/// willing to part with, which the app cannot know.
/// </param>
/// <param name="Points">The card's price in that set's pack points, or zero if it has none.</param>
/// <param name="PointsShort">
/// Points still to earn before the shop could sell it, given the balance in ITS set. A second,
/// genuinely different measure of "far away": packs-saved says how hard the card is to pull,
/// while this says how far the guaranteed route is from being affordable. They disagree often -
/// a Crown is cheap to pull relative to its point price, and an Immersive is the reverse.
/// </param>
/// <param name="Shareable">
/// A friend could simply SEND this one - free, nothing back, one a day. It changes the advice
/// rather than decorating it: spending 5,000 dust and a stamina on a 4-diamond someone could hand
/// over is a bad trade even though every gate on it is green.
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
/// The framing is the whole point. Stamina regenerates one per 12 hours and every trade costs
/// exactly one, so trading is capped at roughly two a day - while shinedust merely accumulates.
/// The scarce thing is therefore TRADES, not currency, which inverts the obvious question: not
/// "can I afford this?" but "of everything I could trade for, which is worth the next slot?".
/// So this is a ranked queue, not a feasibility checklist.
///
/// Value is expected packs saved, from the card's own pull rate. Because the stamina cost is flat,
/// value per stamina and value are the same number here - and that is worth knowing rather than
/// dividing by one and pretending otherwise. It also means the dust price never enters the
/// ranking: dust and packs are different currencies, and converting between them would be
/// fiction. Dust decides whether a trade is possible, not whether it is worth making.
///
/// Surplus is defined by the user's PLAN, not by "more than one". A second copy is spare where
/// the plan wants one and required where it wants two, so a tracker that treats every duplicate
/// as fodder will offer up cards its own completion figures still count as missing.
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
    /// payment, no dust and no stamina, so the two answer different questions - and the rarities
    /// disagree in both directions: a 2-star can be traded but never shared.
    /// </summary>
    public bool IsShareable(PocketCard card) => GameRules.CanBeShared(card);

    /// <summary>
    /// Copies of one card held beyond what the plan asks for, which is how many trades it could
    /// pay for. Zero for anything untradeable, unheld, or still wanted.
    ///
    /// Separate from <see cref="Surplus"/> because a spare COUNT and a spare LIST answer different
    /// questions - a card held four times can fund three trades, but appears once in any list of
    /// what you could offer - and both must agree on what "spare" means.
    /// </summary>
    public int SpareCopies(PocketCard card, Collection owned, Func<string, RarityPlan> planFor)
    {
        if (!IsTradeable(card)) return 0;

        var held = owned.Of(card);
        if (held <= 0) return 0;

        if (_index.Ladder.IndexOf(card.Rarity) is not int rung) return 0;

        // Required by the user's own definition of done. A rung they do not collect wants zero, so
        // every copy of it is spare - which is correct, and is exactly the pile people trade from.
        return Math.Max(0, held - planFor(card.Set).Copies(rung));
    }

    /// <summary>
    /// Cards you hold beyond what your plan asks for, grouped by rarity code - the pool a trade
    /// can be paid from.
    ///
    /// By rarity CODE rather than ladder rung, matching the rest of the engine: SR and SAR share
    /// a rung but are different rarities, and offering one for the other is a trade the game may
    /// well refuse. Under-claiming here costs a suggestion; over-claiming costs a wasted trip to
    /// the trade screen.
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
    /// Blocked candidates are RETURNED, not filtered out: "you have the dust and a spare, but no
    /// stamina until tonight" is the answer to the question, and dropping the row would leave the
    /// user wondering why their best target vanished.
    /// </summary>
    /// <param name="max">
    /// How many rows to keep. Ranking walks every demand regardless, so a caller that groups or
    /// filters afterwards should ask for everything rather than the top few - the best trade in
    /// each pack is usually nowhere near the top of a global ranking.
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
            // The cheapest printing to ask for: any printing satisfies the demand, and rarity is
            // what a trade is priced by, so asking for the dearest one is pointless.
            var card = demand.SuppliedBy
                .OrderBy(c => _rarities.TryGetValue(c.Rarity, out var r) ? r.TradePrice ?? int.MaxValue : int.MaxValue)
                .First();

            var points = _rarities.TryGetValue(card.Rarity, out var rarity) ? rarity.Points : 0;

            // Points are earned and spent inside ONE set, so the gap is measured against that
            // set's balance and nowhere else.
            var pointsShort = Math.Max(
                0, points - resources.PackPointsBySet.GetValueOrDefault(card.Set));

            if (!IsTradeable(card))
            {
                // A card that cannot be traded may still be shareable - and for the diamonds that
                // is the ordinary case once promos are involved, so the row has advice to give
                // even with every trade gate shut.
                result.Add(new TradeCandidate(card, demand.Remaining, 0, 0, [],
                                              TradeGate.NotTradeable, points, pointsShort,
                                              IsShareable(card)));
                continue;
            }

            var price = _rarities[card.Rarity].TradePrice ?? 0;
            var offerable = surplus.GetValueOrDefault(card.Rarity) ?? [];

            // Packs saved, from the pooled rate across every pack that can yield it. Zero rate
            // means no pack can - which makes a trade the ONLY route, so it is treated as the
            // most valuable case rather than the least.
            var rate = rates.GetValueOrDefault(card.Key);
            var packsSaved = rate > 0 ? 1.0 / rate : double.PositiveInfinity;

            var gate = offerable.Count == 0 ? TradeGate.NoSpareAtRarity
                : dust < price ? TradeGate.NotEnoughDust
                : stamina < GameRules.TradeStaminaPerTrade ? TradeGate.NoStamina
                : TradeGate.None;

            result.Add(new TradeCandidate(card, demand.Remaining, packsSaved, price, offerable,
                                          gate, points, pointsShort, IsShareable(card)));
        }

        // Ready trades first, then by what they save. A blocked trade worth 300 packs is still
        // not something you can do today, and burying the one you CAN do under it would answer a
        // different question than the one asked.
        return result
            .OrderByDescending(c => c.Ready)
            .ThenByDescending(c => c.PacksSaved)
            .Take(max)
            .ToArray();
    }

    /// <summary>
    /// Whether dust is genuinely the constraint, at a given rarity's price.
    ///
    /// Usually it is not, and saying so plainly is more useful than showing a cost: stamina
    /// allows about two trades a day, so a balance funding twenty of them is not what is stopping
    /// anyone. The comparison is against stamina the pool can reach, not against the balance now,
    /// or a pool that happens to be empty this minute would make dust look sufficient forever.
    /// </summary>
    public static bool DustIsTheConstraint(int shinedust, int pricePerTrade, int staminaReachable) =>
        pricePerTrade > 0 && ResourcePlan.DustRunway(shinedust, pricePerTrade) < staminaReachable;
}
