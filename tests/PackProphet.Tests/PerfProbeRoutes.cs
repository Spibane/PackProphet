using System.Diagnostics;
using PackProphet.Domain;
using PackProphet.Engine;

namespace PackProphet.Tests;

/// <summary>
/// Guards the cost of pricing a card's acquisition routes, which the wishlist pays once per
/// outstanding demand.
///
/// THE REGRESSION THIS REPLACES
/// ==================================================================================
/// RouteCost.Trade answered "have I a spare of this rarity to offer" by scanning the whole card
/// index — every printing in the game — and allocating a DistinctBy set, once per card it was
/// asked about. Recommending a board for a profile that owns little prices every outstanding
/// demand, so opening the wishlist ran that scan 2,373 times over 3,700 cards and blocked the
/// browser's one thread for about five and a half seconds. On a phone, where the WebAssembly
/// interpreter is roughly an order of magnitude slower than desktop .NET, it was worse.
///
/// The sum never depended on the card. It is "spares I hold at this rarity", whose only per-card
/// part is that a card cannot be offered against itself — so it is now counted once per rarity and
/// this card's own contribution taken back off.
///
/// MEASURED AGAINST THIS MACHINE, NOT AGAINST A NUMBER
/// ----------------------------------------------------------------------------------
/// Same reasoning as PerfProbeOrder next door, and for the same reason: a wall-clock budget
/// measures how busy the machine is, and this suite runs beside a dev server. So the control here
/// is ONE index scan of the shape Trade used to do per card, the halves are timed in alternating
/// rounds so a spike lands on both, and they are compared on their fastest round.
///
/// The budget is calibrated against both outcomes rather than guessed, by measuring with the
/// per-rarity memo in place and with it taken out:
///
///                          memoised     per-card scan     budget
///   200 cards priced       x4.2         x178.5            x25
///
/// Forty times between the two outcomes, with the budget six times clear of the good one, so a
/// busy machine has no honest way to confuse them.
/// </summary>
public class PerfProbeRoutes
{
    private static readonly int Sample = 200;

    /// <summary>
    /// What <paramref name="subject"/> costs as a multiple of <paramref name="control"/>. Both are
    /// warmed first, so the figure is the code's cost rather than the JIT's; then timed in
    /// alternating rounds and compared on the fastest of each, which is the one least interrupted.
    /// </summary>
    private static double Ratio(int rounds, Action control, Action subject)
    {
        control();
        subject();

        var controlBest = long.MaxValue;
        var subjectBest = long.MaxValue;

        for (var round = 0; round < rounds; round++)
        {
            var sw = Stopwatch.StartNew();
            control();
            controlBest = Math.Min(controlBest, sw.ElapsedTicks);

            sw = Stopwatch.StartNew();
            subject();
            subjectBest = Math.Min(subjectBest, sw.ElapsedTicks);
        }

        return controlBest <= 0 ? 0 : (double)subjectBest / controlBest;
    }

    [Fact]
    public void Pricing_a_card_does_not_cost_a_scan_of_every_card()
    {
        var index = Snapshot.Index();

        // Something owned, so the spares count has real work to do rather than short-circuiting
        // on an empty collection.
        var owned = new Collection(
            index.All.DistinctBy(c => c.OwnershipKey).Take(400)
                 .ToDictionary(c => c.OwnershipKey, _ => 3));

        var cards = index.All.DistinctBy(c => c.OwnershipKey).Take(Sample).ToArray();
        var rarity = cards[0].Rarity;

        // A fresh RouteCost per round: the memo is per instance and keyed on the collection, so
        // reusing one would time an empty dictionary lookup after the first round rather than the
        // work being guarded.
        var ratio = Ratio(12,
            // The control is ONE scan of the index for one rarity — exactly what Trade used to do
            // for every single card. What is left over is what pricing 200 cards costs beyond it.
            () => _ = index.All
                .Where(c => c.Rarity == rarity)
                .DistinctBy(c => c.OwnershipKey)
                .Sum(c => Math.Max(0, owned.Of(c) - 1)),
            () =>
            {
                var routes = new RouteCost(index, Snapshot.Odds(), Snapshot.Rarities());
                foreach (var card in cards) _ = routes.For(card, owned);
            });

        Assert.True(ratio < 25,
            $"pricing {Sample} cards cost x{ratio:N1} a single index scan; with the per-rarity "
            + "memo it is about x4, and with a scan per card about x180");
    }

    /// <summary>
    /// The memo has to survive a collection that has not changed and be dropped by one that has,
    /// or it is either useless or wrong. Wrong is the one that matters: a stale spares count makes
    /// a trade look available that is not.
    /// </summary>
    [Fact]
    public void The_spares_count_follows_the_collection_it_was_counted_from()
    {
        var index = Snapshot.Index();
        var routes = new RouteCost(index, Snapshot.Odds(), Snapshot.Rarities());

        // A tradeable card with a dust price, and another of its rarity to be the spare.
        var card = index.All.First(c =>
            GameRules.CanBeTraded(c)
            && Snapshot.Rarities().TryGetValue(c.Rarity, out var r) && r.TradePrice is not null);

        var other = index.All.First(c =>
            c.Rarity == card.Rarity && c.OwnershipKey != card.OwnershipKey);

        static RouteOption TradeOf(CardRoutes r) =>
            r.Options.First(o => o.Route == AcquisitionRoute.Trade);

        var empty = new Collection();
        Assert.False(TradeOf(routes.For(card, empty)).Available);

        // Three copies of a different card of the same rarity is two spares to offer.
        var withSpare = empty.With(other.OwnershipKey, 3);
        Assert.True(TradeOf(routes.For(card, withSpare)).Available);

        // And back, on the same instance, so this fails if the table outlives its collection.
        Assert.False(TradeOf(routes.For(card, empty)).Available);

        // A card cannot be offered against itself: copies of the card being acquired are not
        // spares for acquiring it.
        var onlyItself = empty.With(card.OwnershipKey, 5);
        Assert.False(TradeOf(routes.For(card, onlyItself)).Available);
    }
}
