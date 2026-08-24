namespace PackProphet.Tests;

using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Engine;
using PackProphet.State;

/// <summary>
/// Every engine against a brand-new profile: nothing owned, nothing logged, no resources.
///
/// This is the state every user starts in and the one least exercised while building, and it is
/// where the crash on the compare page came from — a list that is empty in exactly this case being
/// indexed at [0]. The build cannot catch that, so it is pinned here instead.
///
/// The other half of the contract is NaN. An empty collection means dividing by zero counts in
/// several places, and a NaN does not throw — it reaches the screen as "NaN packs" and every
/// comparison against it is silently false.
/// </summary>
public class EmptyStateTests
{
    private static readonly CardIndex Ix = Snapshot.Index();
    private static readonly PackOdds Odds = Snapshot.Odds();

    private static Collection Nothing => new();

    private static RarityPlan Diamonds => RarityPlan.Uniform(Snapshot.Tiers("C", "U", "R", "RR"));

    private static Func<string, RarityPlan> Plan => _ => Diamonds;

    private static ICompletionTarget Everything =>
        new CompositeTarget(Ix.OpenableSets
            .Select(s => (ICompletionTarget)new RarityLadderTarget(s, Diamonds)).ToArray());

    private static void Finite(double value, string what)
    {
        Assert.False(double.IsNaN(value), $"{what} is NaN");
        Assert.False(value < 0, $"{what} is negative: {value}");
    }

    [Fact]
    public void The_pack_ranking_holds_up_with_nothing_owned()
    {
        var ranked = new PackRanker(Ix, Odds).Rank(Everything, Nothing);

        Assert.NotEmpty(ranked);
        foreach (var row in ranked)
        {
            Finite(row.ChanceUseful, $"{row.PackKey} chance useful");
            Assert.InRange(row.ChanceUseful, 0, 1);
            Finite(row.PacksToNextUseful, $"{row.PackKey} packs to next");
            Finite(row.PacksToFinishItsShare, $"{row.PackKey} packs to finish");
        }
    }

    [Fact]
    public void The_trade_queue_returns_nothing_rather_than_throwing()
    {
        var queue = new TradeQueue(Ix, Odds, Snapshot.Rarities());

        var ranked = queue.Rank(Everything.Outstanding(Ix, Nothing), Nothing,
                                Resources.Empty, Plan, max: 10);

        // Every gate is shut with no cards and no dust, so nothing is ready — but the rows still
        // have to be well-formed, since the page reads row zero to write its headline.
        Assert.All(ranked, r => Assert.False(r.Ready));
        Assert.Empty(queue.Surplus(Nothing, Plan));
    }

    [Fact]
    public void The_board_advisor_recommends_nothing_it_cannot_pay_for()
    {
        var routes = new RouteCost(Ix, Odds, Snapshot.Rarities());
        var queue = new TradeQueue(Ix, Odds, Snapshot.Rarities());
        var sets = new SetCatalog(Snapshot.PublishedSets(), Ix.BySet.Keys);
        var advisor = new TradeBoardAdvisor(Ix, routes, queue, Odds, sets);

        var plan = advisor.Recommend(Everything.Outstanding(Ix, Nothing), Nothing, Plan,
                                     board: [], today: new DateOnly(2026, 8, 23));

        // A board CAN be filled from nothing owned — that is the point of it, you are advertising
        // for what you lack — so the guarantee here is well-formedness, not emptiness.
        Assert.True(plan.Slots.Count <= GameRules.TradeBoardSlots);
        Assert.Empty(plan.Drop);
        foreach (var slot in plan.Slots) Finite(slot.NormalCost, $"{slot.Card.Name} cost");
    }

    [Fact]
    public void A_self_trade_between_two_empty_collections_offers_nothing()
    {
        // The exact shape that crashed the compare page: no swaps, no unpayable wants, no shares.
        // The page then read Swaps[0] because its guard tested the wrong list.
        var queue = new TradeQueue(Ix, Odds, Snapshot.Rarities());
        var diff = new ProfileDiff(Ix, Odds, Snapshot.Rarities(), queue);

        var side = new DiffSide("a", "A", Nothing, Plan, Everything.Outstanding(Ix, Nothing),
                               Resources.Empty);

        var swaps = diff.Compare(side, side);

        Assert.Empty(swaps.Swaps);
        Assert.Empty(swaps.IncomingShares);
        Assert.Empty(swaps.OutgoingShares);
        Assert.True(swaps.Nothing);
        Assert.Equal(0, swaps.AllSharesDays);
    }

    [Fact]
    public void Evolution_gaps_are_empty_before_anything_is_owned()
    {
        var report = new EvolutionGaps(Ix, Snapshot.Facts(), Odds).Find(Nothing);

        Assert.True(report.Complete);
        Assert.Equal(0, report.Checked);
        Assert.Equal(0, report.BlockedCards);
    }

    [Fact]
    public void The_pack_mix_estimator_divides_by_no_packs_without_producing_NaN()
    {
        var mix = new PackMixEstimator(Ix, Odds, new SetCatalog(Snapshot.PublishedSets(), Ix.BySet.Keys));

        foreach (var total in new[] { 0, 1, 5000 })
        {
            var shares = mix.Estimate(Nothing, total);
            foreach (var share in shares)
            {
                Finite(share.Share, $"{share.Set} share at {total} packs");
                Finite(share.CardsPerPack, $"{share.Set} cards per pack");
                Assert.True(share.Packs >= 0);
            }
        }
    }

    [Fact]
    public void The_resource_projection_survives_an_untouched_profile()
    {
        var now = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

        var wonder = ResourcePlan.Project(Resources.Empty.Wonder, now);
        Assert.Equal(0, wonder.Balance);
        Assert.Equal(0, wonder.WastedSinceFull);
        Assert.False(wonder.AtCap);

        var packs = ResourcePlan.Packs(Resources.Empty, now);
        Finite(packs.PerDay, "packs per day");
        Finite(packs.DaysFor(100), "days for 100 packs");
    }

    [Fact]
    public void The_completion_estimate_over_nothing_owned_is_finite()
    {
        // The ranker owns the priceable/unpriceable split, so ask it rather than reaching past it.
        var packs = new PackRanker(Ix, Odds).BestCasePacksToFinish(Everything, Nothing);

        Finite(packs, "expected packs to finish everything");
        Assert.True(packs > 0, "an empty collection cannot be finished in zero packs");
    }

    [Fact]
    public void The_estimator_integral_is_well_behaved_at_the_degenerate_ends()
    {
        // No needs at all must be zero, not NaN from an empty integral; a need no pack can supply
        // must be infinity rather than a large finite lie.
        Assert.Equal(0, CompletionEstimator.ExpectedPacks([]));

        var unobtainable = new[] { new CompletionEstimator.Need(0.0, 1) };
        Assert.True(double.IsPositiveInfinity(CompletionEstimator.ExpectedPacks(unobtainable)));
    }
}
