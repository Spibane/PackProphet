using PackProphet.Engine;

namespace PackProphet.Tests;

public class PackAllocatorTests
{
    private static PackAllocator Allocator => new(Snapshot.Index(), Snapshot.Odds());
    private static PackRanker Ranker => new(Snapshot.Index(), Snapshot.Odds());

    [Fact]
    public void CompletedTarget_AllocatesNothing()
    {
        var target = RarityLadderTarget.UpTo("A1", 0, Snapshot.Index(), 1);
        var owned = target.Outstanding(Snapshot.Index(), new Collection())
            .Aggregate(new Collection(), (c, d) => c.With(d.Key, d.Remaining));

        var plan = Allocator.Allocate(target, owned, 100);

        Assert.Empty(plan.Rows);
        Assert.Equal(0.0, plan.ExpectedNewCards);
        Assert.Equal(1.0, plan.ChanceOfFinishing);
    }

    [Fact]
    public void ZeroBudget_AllocatesNothing()
    {
        var target = RarityLadderTarget.UpTo("A1", 3, Snapshot.Index(), 1);
        var plan = Allocator.Allocate(target, new Collection(), 0);

        Assert.Empty(plan.Rows);
        Assert.Equal(0.0, plan.ExpectedNewCards);
    }

    [Fact]
    public void MoreBudget_NeverReducesExpectedNewCards()
    {
        var target = RarityLadderTarget.UpTo("A1", 5, Snapshot.Index(), 1);
        var small = Allocator.Allocate(target, new Collection(), 5);
        var large = Allocator.Allocate(target, new Collection(), 50);

        Assert.True(large.ExpectedNewCards >= small.ExpectedNewCards);
        Assert.True(large.Rows.Sum(r => r.Packs) <= 50);
    }

    [Fact]
    public void AllBudgetInOneKey_NeverBeatsTheAllocator()
    {
        var target = RarityLadderTarget.UpTo("A1", 3, Snapshot.Index(), 1);
        const int budget = 20;

        var plan = Allocator.Allocate(target, new Collection(), budget);

        var bestSingleKey = Ranker
            .Batches(target, new Collection(), budget)
            .Max(b => (double?)b.ExpectedNewCards) ?? 0.0;

        Assert.True(
            plan.ExpectedNewCards >= bestSingleKey - 1e-9,
            $"allocator ({plan.ExpectedNewCards}) should be at least as good as the best single pack ({bestSingleKey})");
    }

    [Fact]
    public void SplitsAcrossMultipleKeysWhenTheyServeDifferentDemands()
    {
        // A whole-series target spans sets no single pack can cover, so a sane split must use
        // more than one pack key.
        var target = new CompositeTarget(
            [
                RarityLadderTarget.UpTo("A1", 3, Snapshot.Index(), 1),
                RarityLadderTarget.UpTo("A2", 3, Snapshot.Index(), 1),
            ],
            "two sets");

        var plan = Allocator.Allocate(target, new Collection(), 40);

        Assert.True(plan.Rows.Count > 1, "expected the budget to be spread across more than one pack key");
    }

    [Fact]
    public void RowsSumToAtMostTheBudget()
    {
        var target = RarityLadderTarget.UpTo("A1", 9, Snapshot.Index(), 2);
        var plan = Allocator.Allocate(target, new Collection(), 30);

        Assert.True(plan.Rows.Sum(r => r.Packs) <= 30);
    }

    [Fact]
    public void ChanceOfFinishing_NeverExceedsExpectedNewCardsFraction()
    {
        var target = RarityLadderTarget.UpTo("A1", 3, Snapshot.Index(), 1);
        var plan = Allocator.Allocate(target, new Collection(), 15);

        var demandCount = target.Outstanding(Snapshot.Index(), new Collection()).Count;
        Assert.True(plan.ChanceOfFinishing <= plan.ExpectedNewCards / demandCount + 1e-9);
    }
}
