namespace PackProphet.Tests;

using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Engine;
using PackProphet.State;

public class TradeQueueTests
{
    private static CardIndex Ix => Snapshot.Index();

    private static TradeQueue Queue => new(Ix, Snapshot.Odds(), Snapshot.Rarities());

    private static RarityPlan All => RarityPlan.Uniform(Ix.Ladder.Everything);

    private static Func<string, RarityPlan> Everything => _ => All;

    private static PocketCard CardOfRarity(string code) =>
        Ix.All.First(c => c.Rarity == code && !c.IsPromo);

    private static Demand DemandFor(PocketCard card) => new(card.OwnershipKey, 1, [card]);

    private static Resources Rich => Resources.Empty with
    {
        Shinedust = 1_000_000,
        Trade = new ResourcePool(GameRules.StaminaCap, 0)
    };

    [Fact]
    public void Refuses_the_rarities_that_cannot_be_traded()
    {
        // 3-star and Crown have no trade route at any price, so no balance and no waiting helps.
        foreach (var code in new[] { "IM", "UR" })
        {
            var card = CardOfRarity(code);
            var ranked = Queue.Rank([DemandFor(card)], new Collection(), Rich, Everything);

            Assert.Equal(TradeGate.NotTradeable, Assert.Single(ranked).Blocking);
        }
    }

    [Fact]
    public void Refuses_promos_while_the_switch_is_off()
    {
        // Promos carry ordinary rarity codes, so this cannot be inferred from rarity - and it is
        // expected to change, which is why the rule is one flag.
        var promo = Ix.All.First(c => c.IsPromo && GameRules.IsTradeable(c.Rarity));

        var ranked = Queue.Rank([DemandFor(promo)], new Collection(), Rich, Everything);

        Assert.Equal(
            GameRules.PromosTradeable ? TradeGate.NoSpareAtRarity : TradeGate.NotTradeable,
            Assert.Single(ranked).Blocking);
    }

    [Fact]
    public void Names_the_gate_that_is_actually_blocking()
    {
        var want = CardOfRarity("SR");
        var spare = Ix.All.First(c => c.Rarity == "SR" && c.OwnershipKey != want.OwnershipKey);
        var price = Snapshot.Rarities()["SR"].TradePrice!.Value;

        // No spare at that rarity: the first gate, and the only one you cannot wait out.
        var noSpare = Queue.Rank([DemandFor(want)], new Collection(), Rich, Everything);
        Assert.Equal(TradeGate.NoSpareAtRarity, Assert.Single(noSpare).Blocking);

        // Two copies held, one of them surplus under a one-copy plan.
        var owned = new Collection(new Dictionary<string, int> { [spare.OwnershipKey] = 2 });

        var noDust = Queue.Rank([DemandFor(want)], owned,
            Rich with { Shinedust = price - 1 }, Everything);
        Assert.Equal(TradeGate.NotEnoughDust, Assert.Single(noDust).Blocking);

        var noStamina = Queue.Rank([DemandFor(want)], owned,
            Rich with { Trade = ResourcePool.Empty }, Everything);
        Assert.Equal(TradeGate.NoStamina, Assert.Single(noStamina).Blocking);

        var ready = Queue.Rank([DemandFor(want)], owned, Rich, Everything);
        Assert.True(Assert.Single(ready).Ready);
    }

    [Fact]
    public void A_copy_the_plan_still_wants_is_not_spare()
    {
        // The distinction a "more than one" rule gets wrong: with two copies wanted, the second
        // is required, and offering it up would contradict the app's own completion figures.
        var want = CardOfRarity("AR");
        var other = Ix.All.First(c => c.Rarity == "AR" && c.OwnershipKey != want.OwnershipKey);
        var owned = new Collection(new Dictionary<string, int> { [other.OwnershipKey] = 2 });

        var twoOfEach = RarityPlan.Uniform(Ix.Ladder.Everything, 2);

        var underOne = Queue.Rank([DemandFor(want)], owned, Rich, Everything);
        Assert.True(Assert.Single(underOne).Ready);

        var underTwo = Queue.Rank([DemandFor(want)], owned, Rich, _ => twoOfEach);
        Assert.Equal(TradeGate.NoSpareAtRarity, Assert.Single(underTwo).Blocking);
    }

    [Fact]
    public void A_rarity_you_do_not_collect_is_all_spare()
    {
        // Nothing on an uncollected rung is required, so every copy of it is fodder — which is
        // exactly the pile people trade from.
        var want = CardOfRarity("AR");
        var other = Ix.All.First(c => c.Rarity == "AR" && c.OwnershipKey != want.OwnershipKey);
        var owned = new Collection(new Dictionary<string, int> { [other.OwnershipKey] = 1 });

        var diamondsOnly = RarityPlan.Uniform(Ix.Ladder.ByGroup("Diamond"));

        var surplus = Queue.Surplus(owned, _ => diamondsOnly);
        Assert.Contains(other, surplus["AR"]);

        Assert.True(Assert.Single(Queue.Rank([DemandFor(want)], owned, Rich, _ => diamondsOnly)).Ready);
    }

    [Fact]
    public void Ranks_by_packs_saved_and_puts_doable_trades_first()
    {
        var rates = Snapshot.Odds().BestRatesByCard();

        // Two tradeable cards with clearly different pull rates, both payable for.
        var cards = Ix.All
            .Where(c => !c.IsPromo && GameRules.IsTradeable(c.Rarity))
            .Where(c => rates.GetValueOrDefault(c.Key) > 0)
            .DistinctBy(c => c.OwnershipKey)
            .OrderBy(c => rates[c.Key])
            .ToArray();

        var rare = cards[0];        // hardest to pull, so the most packs saved
        var common = cards[^1];

        // A spare at each of their rarities, so neither is blocked.
        var spares = new Dictionary<string, int>();
        foreach (var rarity in new[] { rare.Rarity, common.Rarity })
        {
            var spare = Ix.All.First(c =>
                c.Rarity == rarity && c.OwnershipKey != rare.OwnershipKey
                && c.OwnershipKey != common.OwnershipKey);
            spares[spare.OwnershipKey] = 2;
        }

        var ranked = Queue.Rank(
            [DemandFor(common), DemandFor(rare)], new Collection(spares), Rich, Everything);

        Assert.All(ranked, c => Assert.True(c.Ready));
        Assert.Equal(rare.OwnershipKey, ranked[0].Want.OwnershipKey);
        Assert.True(ranked[0].PacksSaved > ranked[1].PacksSaved);
    }

    [Fact]
    public void Puts_a_ready_trade_above_a_blocked_one_worth_more()
    {
        var rates = Snapshot.Odds().BestRatesByCard();

        var blocked = CardOfRarity("SR");        // no spare SR held, so unpayable
        var ready = Ix.All.First(c =>
            c.Rarity == "C" && !c.IsPromo && rates.GetValueOrDefault(c.Key) > 0);
        var spare = Ix.All.First(c => c.Rarity == "C" && c.OwnershipKey != ready.OwnershipKey);

        var owned = new Collection(new Dictionary<string, int> { [spare.OwnershipKey] = 2 });

        var ranked = Queue.Rank([DemandFor(blocked), DemandFor(ready)], owned, Rich, Everything);

        // The 2-star saves far more packs, but cannot be done today. The question was which trade
        // deserves the next stamina, and the answer has to be one that can be made.
        Assert.True(ranked[0].Ready);
        Assert.Equal(ready.OwnershipKey, ranked[0].Want.OwnershipKey);
        Assert.True(ranked[1].PacksSaved > ranked[0].PacksSaved);
    }

    [Fact]
    public void Measures_the_points_gap_against_the_cards_own_set()
    {
        var want = CardOfRarity("AR");
        var price = Snapshot.Rarities()["AR"].Points;

        // Points cannot cross-fund, so a balance in another set must not close the gap.
        var elsewhere = Rich with
        {
            PackPointsBySet = new Dictionary<string, int> { ["PROMO-A"] = 2_000 }
        };
        var far = Assert.Single(Queue.Rank([DemandFor(want)], new Collection(), elsewhere, Everything));
        Assert.Equal(price, far.PointsShort);

        // And a balance in its own set does.
        var here = Rich with
        {
            PackPointsBySet = new Dictionary<string, int> { [want.Set] = price - 100 }
        };
        var close = Assert.Single(Queue.Rank([DemandFor(want)], new Collection(), here, Everything));
        Assert.Equal(100, close.PointsShort);
        Assert.Equal(20, close.PacksOfPointsShort);   // 100 points at 5 a pack

        var covered = Rich with
        {
            PackPointsBySet = new Dictionary<string, int> { [want.Set] = price }
        };
        Assert.Equal(0, Assert.Single(
            Queue.Rank([DemandFor(want)], new Collection(), covered, Everything)).PointsShort);
    }

    [Fact]
    public void Dust_is_usually_not_what_is_stopping_you()
    {
        // 80,000 dust funds three 2-star trades against a reachable five stamina, so dust IS the
        // constraint there. At a 3-diamond's price it funds 66, so it plainly is not.
        Assert.True(TradeQueue.DustIsTheConstraint(80_000, 25_000, staminaReachable: 5));
        Assert.False(TradeQueue.DustIsTheConstraint(80_000, 1_200, staminaReachable: 5));
    }
}
