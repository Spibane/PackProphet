namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;

/// <summary>Why a slot was chosen, so the two bands can be told apart on screen.</summary>
public enum BoardBand
{
    /// <summary>Chosen purely for what it would save. Most of the board.</summary>
    Chase,

    /// <summary>Chosen because someone plausibly has a spare, so the board converts something.</summary>
    Liquid
}

/// <param name="NormalCost">
/// Packs-equivalent to get it the ordinary way, by pulling or by points - whichever is cheaper.
/// Infinite when neither route exists.
/// </param>
/// <param name="Liquidity">
/// A rough estimate of copies in circulation: the card's pull rate times how long its set has
/// been out. Meaningless as an absolute number and only ever compared against other candidates.
/// </param>
/// <param name="SparesAtRarity">
/// Surplus cards you hold at this rarity. A trade needs a same-rarity card to give, so a slot at
/// a rarity you cannot pay for is a request you cannot honour even when someone bites.
/// </param>
/// <param name="CostRoute">
/// Which route that cost came from. The two mean different things: a pull figure is an average,
/// while a points figure is a guarantee after that many packs of the set.
/// </param>
public sealed record BoardSlot(
    PocketCard Card,
    double NormalCost,
    int Dust,
    double Liquidity,
    int SparesAtRarity,
    BoardBand Band,
    AcquisitionRoute CostRoute = AcquisitionRoute.Pull)
{
    public bool Unpullable => double.IsInfinity(NormalCost);
}

/// <param name="Keep">Already on the board and still earning its slot.</param>
/// <param name="Add">Recommended additions.</param>
/// <param name="Drop">On the board but no longer worth a slot.</param>
/// <param name="Excluded">
/// Cards the user wants that cannot go on the board, with the reason. Named rather than dropped,
/// so a Crown missing from the board reads as a rule rather than a bug.
/// </param>
public sealed record BoardPlan(
    IReadOnlyList<BoardSlot> Slots,
    IReadOnlyList<BoardSlot> Keep,
    IReadOnlyList<BoardSlot> Add,
    IReadOnlyList<PocketCard> Drop,
    IReadOnlyList<(PocketCard Card, string Reason)> Excluded)
{
    /// <summary>
    /// Nothing to retype. A rerun after a few packs usually asks for two swaps rather than
    /// twenty entries.
    /// </summary>
    public bool Unchanged => Add.Count == 0 && Drop.Count == 0;
}

/// <summary>
/// What to put on the game's own 20-slot wishlist - the public board other players browse when
/// looking for a trade.
///
/// The board is an advertisement rather than a tracking list: listing a card only makes other
/// people offer it. So this is a selection problem over a hard budget of twenty slots, separate
/// from the app's own chase lists, which are quantified completion targets and are not capped,
/// public, or restricted to tradeable rarities.
///
/// Ranking is by what a card costs to get normally, descending. Multiplying that by the chance
/// anyone offers it cancels out: offer likelihood is proportional to copies in circulation, i.e.
/// to pull rate times set age, while cost is one over the pull rate, so the product is just set
/// age and the ranking becomes oldest-set-first.
///
/// Cost-descending works because a listing costs nothing but a slot and slots do not expire, so a
/// request nobody accepts loses nothing. The failure case is a board where every slot is a card
/// no one will ever offer, so liquidity enters as a floor on a minority of slots rather than a
/// multiplier on all of them.
/// </summary>
public sealed class TradeBoardAdvisor
{
    private readonly CardIndex _index;
    private readonly RouteCost _routes;
    private readonly TradeQueue _trades;
    private readonly PackOdds _odds;
    private readonly SetCatalog _sets;

    public TradeBoardAdvisor(
        CardIndex index, RouteCost routes, TradeQueue trades, PackOdds odds, SetCatalog sets)
    {
        _index = index;
        _routes = routes;
        _trades = trades;
        _odds = odds;
        _sets = sets;
    }

    /// <summary>
    /// Slots kept for cards someone plausibly has spare, so the board reliably converts something.
    /// Four of twenty by default: enough that the board is not purely lottery tickets, few enough
    /// that it is not mostly commons.
    /// </summary>
    public const int DefaultLiquidSlots = 4;

    /// <param name="outstanding">
    /// Demands from the target selected on the Packs page - the same one the ranking uses, not
    /// everything missing. The rarity chips are how the user says what they collect, so a
    /// diamonds-only collector is never told to advertise for a 2-star.
    /// </param>
    /// <param name="board">What is on the board now, so the result can be a set of swaps.</param>
    /// <param name="today">Passed in rather than read, so the estimate is reproducible in tests.</param>
    /// <param name="minimumCost">
    /// Optional floor in packs-equivalent, off by default. Scope comes from the user's plan, and a
    /// diamonds-only plan contains almost nothing above a threshold like 15, so an absolute floor
    /// would produce an empty board. It applies only where the scope reaches the stars.
    /// </param>
    public BoardPlan Recommend(
        IEnumerable<Demand> outstanding,
        Collection owned,
        Func<string, RarityPlan> planFor,
        IReadOnlyList<string> board,
        DateOnly today,
        int liquidSlots = DefaultLiquidSlots,
        double minimumCost = 0)
    {
        var surplus = _trades.Surplus(owned, planFor);
        var rates = _odds.BestRatesByCard();

        var candidates = new List<BoardSlot>();
        var excluded = new List<(PocketCard, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var demand in outstanding)
        {
            // Quantity is discarded here, and only here: a slot is a standing request, not a
            // trade, so wanting two copies still costs exactly one slot.
            var card = demand.SuppliedBy[0];
            if (!seen.Add(card.OwnershipKey)) continue;

            if (!_trades.IsTradeable(card))
            {
                excluded.Add((card, Reason(card)));
                continue;
            }

            var routes = _routes.For(card, owned);
            var cheapest = routes.Cheapest;
            var cost = cheapest?.PacksEquivalent ?? double.PositiveInfinity;

            if (minimumCost > 0 && cost < minimumCost) continue;

            candidates.Add(new BoardSlot(
                card, cost,
                routes.Options.First(o => o.Route == AcquisitionRoute.Trade).Dust ?? 0,
                Liquidity(card, rates, today),
                surplus.GetValueOrDefault(card.Rarity)?.Count ?? 0,
                BoardBand.Chase,
                cheapest?.Route ?? AcquisitionRoute.Pull));
        }

        var slots = Select(candidates, liquidSlots);

        var onBoard = new HashSet<string>(board, StringComparer.OrdinalIgnoreCase);
        var chosen = slots.Select(s => s.Card.OwnershipKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new BoardPlan(
            slots,
            [.. slots.Where(s => onBoard.Contains(s.Card.OwnershipKey))],
            [.. slots.Where(s => !onBoard.Contains(s.Card.OwnershipKey))],
            [.. board.Where(key => !chosen.Contains(key))
                     .Select(key => _index.ByOwnershipKey.GetValueOrDefault(key)?.FirstOrDefault())
                     .Where(c => c is not null)
                     .Select(c => c!)],
            excluded);
    }

    /// <summary>
    /// Fill the chase band by cost, then the liquid band by cost among candidates whose estimated
    /// circulation clears the median. The median rather than a fixed number because circulation is
    /// only ever meaningful relative to the other candidates.
    /// </summary>
    private static IReadOnlyList<BoardSlot> Select(List<BoardSlot> candidates, int liquidSlots)
    {
        var byCost = candidates.OrderByDescending(c => c.NormalCost).ToList();
        if (byCost.Count <= GameRules.TradeBoardSlots) return byCost;

        liquidSlots = Math.Clamp(liquidSlots, 0, GameRules.TradeBoardSlots);
        var chaseSlots = GameRules.TradeBoardSlots - liquidSlots;

        var chosen = byCost.Take(chaseSlots).ToList();
        var taken = chosen.Select(c => c.Card.OwnershipKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (liquidSlots > 0)
        {
            var floor = Median(candidates.Select(c => c.Liquidity).ToArray());

            chosen.AddRange(byCost
                .Where(c => !taken.Contains(c.Card.OwnershipKey))
                .Where(c => c.Liquidity >= floor)
                .Take(liquidSlots)
                .Select(c => c with { Band = BoardBand.Liquid }));
        }

        // Any shortfall in the liquid band (nothing left above the floor) falls back to cost, so
        // the board is never left with empty slots.
        if (chosen.Count < GameRules.TradeBoardSlots)
        {
            var have = chosen.Select(c => c.Card.OwnershipKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            chosen.AddRange(byCost
                .Where(c => !have.Contains(c.Card.OwnershipKey))
                .Take(GameRules.TradeBoardSlots - chosen.Count));
        }

        return chosen;
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0) return 0;
        Array.Sort(values);
        return values[values.Length / 2];
    }

    // Cost is the cheaper of pulling and the points shop, and is infinite only when neither route
    // exists. Against the real data that never fires today: no tradeable card is without a route.
    // A set with no published pull rates still sells packs, so its points shop still works, and a
    // B4 common prices at seven packs rather than infinity. The 203 cards with no route at all are
    // promos, which cannot be traded yet, so the unbounded branch goes live when
    // GameRules.PromosTradeable flips.

    /// <summary>
    /// A rough proxy for copies in circulation: how often the card drops, times how long its set
    /// has been on sale. Never shown as a figure and never used as a multiplier - only to decide
    /// which candidates clear the floor for the liquid band.
    /// </summary>
    private double Liquidity(PocketCard card, IReadOnlyDictionary<string, double> rates, DateOnly today)
    {
        var rate = rates.GetValueOrDefault(card.Key);
        if (rate <= 0) return 0;

        var released = _sets.ReleaseDateOf(card.Set);
        var days = released is null ? 1 : Math.Max(1, today.DayNumber - released.Value.DayNumber);

        return rate * days;
    }

    private static string Reason(PocketCard card) =>
        !GameRules.PromosTradeable && card.IsPromo
            ? "promos cannot be traded yet — the developers have said this will change"
            : $"{card.Rarity} cannot be traded at all";
}
