namespace PackProphet.Tests;

using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Engine;

public class TradeBoardAdvisorTests
{
    private static CardIndex Ix => Snapshot.Index();

    private static readonly DateOnly Today = new(2026, 8, 23);

    private static TradeBoardAdvisor Advisor => new(
        Ix,
        new RouteCost(Ix, Snapshot.Odds(), Snapshot.Rarities()),
        new TradeQueue(Ix, Snapshot.Odds(), Snapshot.Rarities()),
        Snapshot.Odds(),
        new SetCatalog(Snapshot.PublishedSets(), Ix.BySet.Keys));

    private static Func<string, RarityPlan> Everything =>
        _ => RarityPlan.Uniform(Ix.Ladder.Everything);

    private static IReadOnlyList<Demand> DemandsFor(IEnumerable<PocketCard> cards) =>
        cards.DistinctBy(c => c.OwnershipKey)
             .Select(c => new Demand(c.OwnershipKey, 1, [c]))
             .ToArray();

    /// <summary>Every card of a set as outstanding demand, which is the ordinary case.</summary>
    private static IReadOnlyList<Demand> WholeSet(string set) => DemandsFor(Ix.BySet[set]);

    [Fact]
    public void Never_fills_more_than_the_games_twenty_slots()
    {
        var plan = Advisor.Recommend(
            WholeSet("A1"), new Collection(), Everything, [], Today);

        Assert.Equal(GameRules.TradeBoardSlots, plan.Slots.Count);
    }

    [Fact]
    public void Names_what_it_had_to_leave_out_rather_than_dropping_it()
    {
        var plan = Advisor.Recommend(
            WholeSet("A1"), new Collection(), Everything, [], Today);

        // A1 has Crowns and Immersives, which cannot be traded at any price. Someone chasing one
        // and seeing it absent would read that as a bug, so the reason is reported.
        Assert.NotEmpty(plan.Excluded);
        Assert.All(plan.Excluded, x => Assert.False(
            new TradeQueue(Ix, Snapshot.Odds(), Snapshot.Rarities()).IsTradeable(x.Card)));
        Assert.Contains(plan.Excluded, x => x.Reason.Contains("cannot be traded"));

        // And nothing untradeable slipped into the board itself.
        Assert.All(plan.Slots, s => Assert.True(GameRules.IsTradeable(s.Card.Rarity)));
    }

    [Fact]
    public void Spends_a_slot_on_one_card_however_many_copies_are_wanted()
    {
        var cards = Ix.BySet["A1"].Where(c => GameRules.IsTradeable(c.Rarity))
                                  .DistinctBy(c => c.OwnershipKey)
                                  .Take(3).ToArray();

        // A slot is a standing request, not a trade, so two copies wanted is still one slot.
        var doubled = cards.Select(c => new Demand(c.OwnershipKey, 2, [c])).ToArray();

        var plan = Advisor.Recommend(doubled, new Collection(), Everything, [], Today);

        Assert.Equal(3, plan.Slots.Count);
    }

    [Fact]
    public void The_unobtainable_case_is_dormant_until_promos_can_be_traded()
    {
        // Pins a measured fact the ranking rests on. A card with no route at all would be the
        // best possible use of a slot — and today no TRADEABLE card is in that position. A set
        // without published pull rates is not unobtainable: it still sells packs, so its points
        // shop still works, and its commons price at seven packs rather than infinity.
        var routes = new RouteCost(Ix, Snapshot.Odds(), Snapshot.Rarities());
        var queue = new TradeQueue(Ix, Snapshot.Odds(), Snapshot.Rarities());
        var owned = new Collection();

        var tradeableWithNoRoute = Ix.All.DistinctBy(c => c.OwnershipKey)
            .Where(queue.IsTradeable)
            .Where(c => routes.For(c, owned).Cheapest is null)
            .ToArray();

        Assert.Empty(tradeableWithNoRoute);

        // The cards that genuinely have no route are promos, and they are exactly what the switch
        // will let in. When it flips, this stops being dormant on its own.
        Assert.Contains(Ix.All.DistinctBy(c => c.OwnershipKey)
            .Where(c => routes.For(c, owned).Cheapest is null), c => c.IsPromo);
        Assert.False(GameRules.PromosTradeable);
    }

    [Fact]
    public void Ranks_the_dearest_card_into_the_first_slot()
    {
        var rates = Snapshot.Odds().BestRatesByCard();

        // A 2-star costs 250 packs by points against a common's seven, so the ordering is not
        // subtle — which is the point: cost-descending is the whole default.
        var dear = Ix.BySet["A1"].First(c => c.Rarity == "SR");
        var cheap = Ix.BySet["A1"].First(c => c.Rarity == "C" && rates.GetValueOrDefault(c.Key) > 0);

        var plan = Advisor.Recommend(
            DemandsFor([cheap, dear]), new Collection(), Everything, [], Today);

        Assert.Equal(dear.OwnershipKey, plan.Slots[0].Card.OwnershipKey);
        Assert.True(plan.Slots[0].NormalCost > plan.Slots[1].NormalCost);
    }

    [Fact]
    public void Reserves_a_few_slots_for_cards_someone_might_actually_offer()
    {
        var plan = Advisor.Recommend(
            WholeSet("A1"), new Collection(), Everything, [], Today, liquidSlots: 4);

        Assert.Equal(4, plan.Slots.Count(s => s.Band == BoardBand.Liquid));
        Assert.Equal(16, plan.Slots.Count(s => s.Band == BoardBand.Chase));

        // The liquid band exists to make the board convert, so its picks must be commoner than
        // the chase band's — otherwise it is doing nothing.
        var chase = plan.Slots.Where(s => s.Band == BoardBand.Chase).Average(s => s.Liquidity);
        var liquid = plan.Slots.Where(s => s.Band == BoardBand.Liquid).Average(s => s.Liquidity);
        Assert.True(liquid > chase, $"liquid band {liquid:0.###} should out-circulate chase {chase:0.###}");
    }

    [Fact]
    public void All_twenty_slots_go_to_the_dearest_when_no_slots_are_reserved()
    {
        var plan = Advisor.Recommend(
            WholeSet("A1"), new Collection(), Everything, [], Today, liquidSlots: 0);

        Assert.All(plan.Slots, s => Assert.Equal(BoardBand.Chase, s.Band));

        // Cost-descending, which is the correct default: a listing costs nothing but a slot.
        var costs = plan.Slots.Select(s => s.NormalCost).ToArray();
        Assert.Equal(costs.OrderByDescending(c => c), costs);
    }

    [Fact]
    public void Asks_for_swaps_rather_than_a_retyped_board()
    {
        var first = Advisor.Recommend(WholeSet("A1"), new Collection(), Everything, [], Today);
        var board = first.Slots.Select(s => s.Card.OwnershipKey).ToArray();

        // Same inputs, so the same answer — and nothing to retype.
        var again = Advisor.Recommend(WholeSet("A1"), new Collection(), Everything, board, Today);

        Assert.True(again.Unchanged);
        Assert.Empty(again.Add);
        Assert.Empty(again.Drop);
        Assert.Equal(GameRules.TradeBoardSlots, again.Keep.Count);

        // Acquire one of them and it should leave the board, with exactly one card taking its
        // place — two edits in the game, not twenty.
        var got = first.Slots[0].Card;
        var owned = new Collection(new Dictionary<string, int> { [got.OwnershipKey] = 1 });
        var outstanding = WholeSet("A1").Where(d => d.Key != got.OwnershipKey).ToArray();

        var third = Advisor.Recommend(outstanding, owned, Everything, board, Today);

        Assert.Single(third.Drop);
        Assert.Equal(got.OwnershipKey, third.Drop[0].OwnershipKey);
        Assert.Single(third.Add);
    }

    [Fact]
    public void Reports_whether_you_could_even_pay_for_a_slot()
    {
        // A trade needs a same-rarity card to give. With nothing owned, no slot is payable, and
        // saying so is the point: a board you cannot honour is worse than a shorter one.
        var broke = Advisor.Recommend(WholeSet("A1"), new Collection(), Everything, [], Today);
        Assert.All(broke.Slots, s => Assert.Equal(0, s.SparesAtRarity));

        // Hold a spare at a rarity the board actually asks for. 2-stars are the dearest thing in
        // A1, so they fill the chase band — and a surplus 2-star is what would pay for one.
        var spare = Ix.BySet["A1"].First(c => c.Rarity == "SR");
        var owned = new Collection(new Dictionary<string, int> { [spare.OwnershipKey] = 2 });

        var funded = Advisor.Recommend(WholeSet("A1"), owned, Everything, [], Today);
        Assert.Contains(funded.Slots, s => s.Card.Rarity == "SR" && s.SparesAtRarity > 0);
    }

    [Fact]
    public void An_absolute_floor_can_empty_the_board_which_is_why_it_is_off()
    {
        // A one-diamond costs seven packs through the points shop, so a 15-pack floor removes
        // every one of them. That is the case that makes the floor opt-in: someone collecting only
        // the low diamonds would otherwise be handed an empty board, which is a worse answer than
        // a board of cheap cards.
        var commons = Ix.BySet["A1"].Where(c => c.Rarity == "C");

        var unfloored = Advisor.Recommend(
            DemandsFor(commons), new Collection(), Everything, [], Today);
        Assert.NotEmpty(unfloored.Slots);

        var floored = Advisor.Recommend(
            DemandsFor(commons), new Collection(), Everything, [], Today, minimumCost: 15);
        Assert.Empty(floored.Slots);
    }
}
