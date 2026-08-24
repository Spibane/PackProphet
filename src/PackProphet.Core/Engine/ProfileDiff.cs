namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.State;

/// <summary>
/// One collection as the swap finder needs to see it: what it holds, what it counts as done, what
/// it still wants, and what it can pay with.
///
/// The plan travels with the side rather than being shared. Two accounts are rarely collected to
/// the same standard — an alt is often a diamonds-only farm while the main chases stars — and using
/// the main's plan to decide what the alt can spare would offer up cards the alt's own screens
/// still report as missing.
/// </summary>
public sealed record DiffSide(
    string Id,
    string Name,
    Collection Owned,
    Func<string, RarityPlan> PlanFor,
    IReadOnlyList<Demand> Outstanding,
    Resources Resources);

/// <param name="Copies">
/// How many copies could actually change hands: the spare copies held, capped by the copies the
/// other side is short of. A fourth copy of a card the receiver needs once is not three trades.
/// </param>
/// <param name="PacksSaved">Expected packs this spares the RECEIVER, from the card's pull rate.</param>
public sealed record DiffGift(PocketCard Card, int Copies, double PacksSaved, int Dust)
{
    public string Rarity => Card.Rarity;

    /// <summary>No pack can yield it, so a trade is the only route.</summary>
    public bool Unpullable => double.IsPositiveInfinity(PacksSaved);
}

/// <summary>
/// One complete self-trade: a card each way, at the same rarity, with both sides' gates checked.
/// </summary>
/// <param name="OutgoingWanted">
/// False means the return card is filler - a spare handed over only because the game demands a
/// card back. The trade is still worth making, but it closes one want rather than two.
/// </param>
public sealed record SelfSwap(
    string Rarity,
    int Dust,
    PocketCard Incoming,
    double IncomingSaves,
    PocketCard Outgoing,
    double OutgoingSaves,
    bool OutgoingWanted,
    TradeGate MineBlocking = TradeGate.None,
    TradeGate TheirsBlocking = TradeGate.None)
{
    /// <summary>Both sides gain something they wanted.</summary>
    public bool Mutual => OutgoingWanted;

    public bool Ready => MineBlocking == TradeGate.None && TheirsBlocking == TradeGate.None;

    /// <summary>
    /// Packs saved across both accounts. Filler contributes nothing, so a mutual swap of two
    /// 200-pack cards ranks above a one-sided one.
    /// </summary>
    public double Value => IncomingSaves + (OutgoingWanted ? OutgoingSaves : 0);
}

/// <param name="Unpayable">
/// Cards the other side holds spare and this one wants, with nothing at that rarity to pay for
/// them. Reported rather than dropped, since the fix — pull or buy any spare at that rung — is
/// actionable.
/// </param>
/// <param name="Unwanted">
/// Cards this side holds spare that the other side wants, left over after pairing. They are what
/// makes the next swap possible once the other side has something to send back.
/// </param>
/// <param name="IncomingShares">
/// Cards the other account can send outright, ranked by what they save. No card back, no dust, no
/// stamina, so these come first; each costs a day of the receiving allowance.
/// </param>
/// <param name="OutgoingShares">The same the other way, out of this account's spares.</param>
public sealed record ProfileSwaps(
    DiffSide Mine,
    DiffSide Theirs,
    IReadOnlyList<SelfSwap> Swaps,
    IReadOnlyList<DiffGift> Unpayable,
    IReadOnlyList<DiffGift> Unwanted,
    IReadOnlyList<DiffGift> IncomingShares,
    IReadOnlyList<DiffGift> OutgoingShares)
{
    public int Mutual => Swaps.Count(s => s.Mutual);
    public int ReadyNow => Swaps.Count(s => s.Ready);

    /// <summary>
    /// Copies to be shared in, which is also the number of DAYS it takes: the receiving allowance
    /// is one a day and there is nothing to spend to go faster.
    /// </summary>
    public int ShareDays => IncomingShares.Sum(g => g.Copies);

    /// <summary>
    /// Days to move everything both ways. Not the sum: each account has its own daily allowance,
    /// so the two directions run in parallel and the slower one sets the pace.
    /// </summary>
    public int AllSharesDays =>
        Math.Max(ShareDays, OutgoingShares.Sum(g => g.Copies)) / GameRules.SharesReceivedPerDay;

    public bool Nothing =>
        Swaps.Count == 0 && Unpayable.Count == 0 && Unwanted.Count == 0
        && IncomingShares.Count == 0 && OutgoingShares.Count == 0;
}

/// <summary>
/// What one profile could trade to another, and what it must send back.
///
/// A PTCGP trade is a swap: both sides hand over a card, and the two must be the same rarity. A
/// list of the alt's spares that the main wants is therefore only half a trade, and the other half
/// is often the binding one — an alt full of spare 2-stars is useless to a main that holds no spare
/// 2-star to send back.
///
/// That makes this a pairing problem per rarity rather than a set difference, with three outcomes:
///
///   - a mutual swap, where each side receives something it wanted. Costs the dust and one stamina
///     each.
///   - a one-sided swap paid with filler, where the return card is a spare nobody wanted. Closes
///     one want rather than two.
///   - an unpayable want, where the rarity has nothing to send back at all. Reported rather than
///     omitted.
///
/// Both sides' dust and stamina are checked, because both are spent. A self-trade is the one place
/// in the app where the resources of a profile other than the active one matter.
/// </summary>
public sealed class ProfileDiff
{
    private readonly CardIndex _index;
    private readonly TradeQueue _trades;
    private readonly IReadOnlyDictionary<string, Rarity> _rarities;
    private readonly IReadOnlyDictionary<string, double> _rates;

    public ProfileDiff(
        CardIndex index,
        PackOdds odds,
        IReadOnlyDictionary<string, Rarity> rarities,
        TradeQueue trades)
    {
        _index = index;
        _trades = trades;
        _rarities = rarities;
        _rates = odds.BestRatesByCard();
    }

    /// <summary>
    /// Swaps between two profiles, best first. Direction is from <paramref name="mine"/>'s point of
    /// view: incoming cards are the ones it gains.
    /// </summary>
    public ProfileSwaps Compare(DiffSide mine, DiffSide theirs)
    {
        // Not guarded against comparing a profile with itself: everything below then reports zero,
        // because a card cannot be both spare and outstanding on the same side.
        var incoming = Gifts(giver: theirs, receiver: mine);
        var outgoing = Gifts(giver: mine, receiver: theirs);

        // Anything a Share can carry leaves the pairing entirely. A Share needs no card back, no
        // dust and no stamina, so between two of your own accounts it costs less than a trade
        // wherever it applies — trading a spare 3-diamond for another 3-diamond would spend 1,200
        // dust and two stamina to move cards that could be sent. What remains in the pairing is
        // what a Share cannot carry: stars, shinies and above.
        var incomingShares = TakeShareable(incoming);
        var outgoingShares = TakeShareable(outgoing);

        // Both pools, because a card routed to a Share is not filler: offering it as payment as
        // well would have the page hand the same card over twice.
        var wantedByThem = outgoing.SelectMany(kv => kv.Value).Concat(outgoingShares)
            .Select(g => g.Card.OwnershipKey).ToHashSet();
        var filler = Filler(mine, wantedByThem);

        var swaps = new List<SelfSwap>();
        var unpayable = new List<DiffGift>();

        foreach (var (rarity, wants) in incoming)
        {
            var dust = _rarities.TryGetValue(rarity, out var r) ? r.TradePrice ?? 0 : 0;

            // Highest value first on the incoming side, so the cards that matter most are the ones
            // that get paired at all when payment runs short.
            var pay = new List<(PocketCard Card, double Saves, bool Wanted)>();
            pay.AddRange(Units(outgoing.GetValueOrDefault(rarity) ?? [])
                .Select(u => (u.Card, u.PacksSaved, true))
                .OrderByDescending(u => u.PacksSaved));

            // Filler last, and the least valuable filler first, so the interesting spares stay
            // available for a mutual swap later.
            pay.AddRange(Units(filler.GetValueOrDefault(rarity) ?? [])
                .Select(u => (u.Card, u.PacksSaved, false))
                .OrderBy(u => u.PacksSaved));

            var next = 0;
            foreach (var want in Units(wants).OrderByDescending(u => u.PacksSaved))
            {
                if (next >= pay.Count)
                {
                    unpayable.Add(want with { Copies = 1 });
                    continue;
                }

                var (card, saves, isWanted) = pay[next++];
                swaps.Add(new SelfSwap(rarity, dust, want.Card, want.PacksSaved,
                                       card, saves, isWanted));
            }
        }

        // Ranked before the gates are applied, because dust and stamina decide when a swap happens
        // rather than whether it is the best one. Then walked in that order, deducting as it goes,
        // so the second trade of the day is blocked by the first having spent the stamina.
        var ordered = swaps
            .OrderByDescending(s => s.Value)
            .ThenByDescending(s => s.Mutual)
            .ThenBy(s => s.Incoming.Key, StringComparer.Ordinal)
            .ToList();

        var result = new List<SelfSwap>(ordered.Count);
        var myDust = Math.Max(0, mine.Resources.Shinedust);
        var theirDust = Math.Max(0, theirs.Resources.Shinedust);
        var myStamina = Math.Max(0, mine.Resources.Trade.Balance);
        var theirStamina = Math.Max(0, theirs.Resources.Trade.Balance);

        foreach (var swap in ordered)
        {
            var mineGate = myDust < swap.Dust ? TradeGate.NotEnoughDust
                : myStamina < GameRules.TradeStaminaPerTrade ? TradeGate.NoStamina
                : TradeGate.None;

            var theirsGate = theirDust < swap.Dust ? TradeGate.NotEnoughDust
                : theirStamina < GameRules.TradeStaminaPerTrade ? TradeGate.NoStamina
                : TradeGate.None;

            if (mineGate == TradeGate.None && theirsGate == TradeGate.None)
            {
                myDust -= swap.Dust;
                theirDust -= swap.Dust;
                myStamina -= GameRules.TradeStaminaPerTrade;
                theirStamina -= GameRules.TradeStaminaPerTrade;
            }

            result.Add(swap with { MineBlocking = mineGate, TheirsBlocking = theirsGate });
        }

        // Spares the other side wants and this side never got to send, because the rarity ran out
        // of incoming cards to pair them with. The mirror of Unpayable, from their side.
        var sent = result.GroupBy(s => s.Outgoing.OwnershipKey)
            .ToDictionary(g => g.Key, g => g.Count());

        var unwanted = new List<DiffGift>();
        foreach (var gift in outgoing.SelectMany(kv => kv.Value))
        {
            // Copies, not membership: two spares of a card the other side wants twice, with only
            // one paired, leaves one still to send.
            var left = gift.Copies - sent.GetValueOrDefault(gift.Card.OwnershipKey);
            if (left > 0) unwanted.Add(gift with { Copies = left });
        }

        return new ProfileSwaps(mine, theirs, result,
                                Merge(unpayable), Merge(unwanted),
                                Merge(incomingShares), Merge(outgoingShares));
    }

    /// <summary>
    /// What one side holds spare that the other is short of, grouped by rarity code.
    ///
    /// By code rather than by ladder rung, matching <see cref="TradeQueue.Surplus"/>: SR and SAR
    /// share a rung, and the game may refuse one as payment for the other.
    /// </summary>
    private Dictionary<string, List<DiffGift>> Gifts(DiffSide giver, DiffSide receiver)
    {
        var byRarity = new Dictionary<string, List<DiffGift>>(StringComparer.OrdinalIgnoreCase);

        foreach (var demand in receiver.Outstanding)
        {
            var left = demand.Remaining;

            foreach (var card in demand.SuppliedBy.DistinctBy(c => c.OwnershipKey))
            {
                if (left <= 0) break;

                var spare = _trades.SpareCopies(card, giver.Owned, giver.PlanFor);
                if (spare <= 0) continue;

                var copies = Math.Min(spare, left);
                left -= copies;

                if (!byRarity.TryGetValue(card.Rarity, out var list))
                    byRarity[card.Rarity] = list = [];

                list.Add(new DiffGift(card, copies, Saves(card), DustFor(card)));
            }
        }

        return byRarity;
    }

    /// <summary>
    /// Spares that nobody wants but that a trade can still be paid with, grouped by rarity.
    ///
    /// This is what turns a want into a trade when the rarity has no mutual match. Without it the
    /// app would report a card as unobtainable while the payment for it sat in the binder.
    /// </summary>
    private Dictionary<string, List<DiffGift>> Filler(DiffSide giver, IReadOnlySet<string> wanted)
    {
        var byRarity = new Dictionary<string, List<DiffGift>>(StringComparer.OrdinalIgnoreCase);

        foreach (var card in _index.All.DistinctBy(c => c.OwnershipKey))
        {
            if (wanted.Contains(card.OwnershipKey)) continue;

            var spare = _trades.SpareCopies(card, giver.Owned, giver.PlanFor);
            if (spare <= 0) continue;

            if (!byRarity.TryGetValue(card.Rarity, out var list)) byRarity[card.Rarity] = list = [];
            list.Add(new DiffGift(card, spare, Saves(card), DustFor(card)));
        }

        return byRarity;
    }

    /// <summary>
    /// Pulls the shareable rarities out of a gift pool, mutating it, so the caller is left with
    /// only what still has to be traded.
    ///
    /// They are removed rather than also-listed: leaving a 3-diamond in the pairing would have it
    /// consume a spare as payment and report dust and stamina for a card that can be handed over
    /// for nothing.
    /// </summary>
    private List<DiffGift> TakeShareable(Dictionary<string, List<DiffGift>> gifts)
    {
        var taken = new List<DiffGift>();

        foreach (var rarity in gifts.Keys.ToArray())
        {
            var shareable = gifts[rarity].Where(g => _trades.IsShareable(g.Card)).ToList();
            if (shareable.Count == 0) continue;

            taken.AddRange(shareable);

            // A rarity is shareable or it is not - it never splits, except where a promo carries an
            // ordinary diamond code. So the remainder is usually empty, and dropping the key keeps
            // the pairing loop from walking rarities with nothing left in them.
            var rest = gifts[rarity].Where(g => !_trades.IsShareable(g.Card)).ToList();
            if (rest.Count == 0) gifts.Remove(rarity); else gifts[rarity] = rest;
        }

        return taken;
    }

    /// <summary>
    /// One entry per copy. Pairing is per trade, and a card held three times over is three separate
    /// trades' worth of payment.
    /// </summary>
    private static IEnumerable<DiffGift> Units(IEnumerable<DiffGift> gifts) =>
        gifts.SelectMany(g => Enumerable.Repeat(g with { Copies = 1 }, Math.Max(1, g.Copies)));

    /// <summary>Back to one row per card, for display, after pairing has consumed copies.</summary>
    private static IReadOnlyList<DiffGift> Merge(IEnumerable<DiffGift> gifts) =>
        gifts.GroupBy(g => g.Card.OwnershipKey)
            .Select(g => g.First() with { Copies = g.Sum(x => x.Copies) })
            .OrderByDescending(g => g.PacksSaved)
            .ThenBy(g => g.Card.Key, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Expected packs the receiver is spared. Infinity where no pack yields the card at all, which
    /// is the case where a trade is the only route in existence.
    /// </summary>
    private double Saves(PocketCard card)
    {
        var rate = _rates.GetValueOrDefault(card.Key);
        return rate > 0 ? 1.0 / rate : double.PositiveInfinity;
    }

    private int DustFor(PocketCard card) =>
        _rarities.TryGetValue(card.Rarity, out var r) ? r.TradePrice ?? 0 : 0;
}
