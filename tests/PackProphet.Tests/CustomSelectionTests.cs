using PackProphet.Engine;

namespace PackProphet.Tests;

/// <summary>
/// A completion target is an arbitrary set of rarities. These pin the shapes a threshold
/// could not express, end to end through demands and ranking.
/// </summary>
public class CustomSelectionTests
{
    private static readonly Data.CardIndex Ix = Snapshot.Index();
    private static PackRanker Ranker => new(Ix, Snapshot.Odds());

    private static IReadOnlySet<int> Sel(params string[] codes) => Snapshot.Tiers(codes);

    [Fact]
    public void StarsAndCrownsWithoutDiamonds_DemandsNoDiamonds()
    {
        var target = new RarityLadderTarget("A1", RarityPlan.Uniform(Sel("AR", "SR", "SAR", "IM", "UR")));
        var outstanding = target.Outstanding(Ix, new Collection());

        Assert.NotEmpty(outstanding);
        Assert.All(outstanding, d =>
        {
            var group = Snapshot.Rarities()[d.SuppliedBy[0].Rarity].Group;
            Assert.Contains(group, new[] { "Star", "Crown" });
        });
    }

    [Fact]
    public void AllDiamondsPlusThreeStar_SkipsTheStarTiersBetween()
    {
        // "Everything diamond, and also Immersive" — deliberately excluding 1- and 2-star.
        var selection = Ix.Ladder.UpTo(3).Concat([Ix.Ladder.IndexOf("IM")!.Value]).ToHashSet();
        var target = new RarityLadderTarget("A1", RarityPlan.Uniform(selection));

        var rarities = target.Outstanding(Ix, new Collection())
            .Select(d => d.SuppliedBy[0].Rarity).Distinct().ToHashSet();

        Assert.Contains("C", rarities);
        Assert.Contains("RR", rarities);
        Assert.Contains("IM", rarities);
        Assert.DoesNotContain("AR", rarities);
        Assert.DoesNotContain("SR", rarities);
        Assert.DoesNotContain("UR", rarities);
    }

    [Fact]
    public void IgnoringDiamondsIsFarCheaperThanWantingEverything()
    {
        var everything = new RarityLadderTarget("A1", RarityPlan.Uniform(Ix.Ladder.Everything));
        var starsOnly = new RarityLadderTarget("A1", RarityPlan.Uniform(Ix.Ladder.ByGroup("Star")));

        var all = Ranker.BestCasePacksToFinish(everything, new Collection());
        var stars = Ranker.BestCasePacksToFinish(starsOnly, new Collection());

        // Fewer cards wanted, so never dearer — and the ranking still works.
        Assert.True(stars <= all);
        Assert.NotEmpty(Ranker.Rank(starsOnly, new Collection()));
    }

    [Fact]
    public void ASingleRarity_IsAValidTarget()
    {
        // The advisor prices one rung at a time, so this shape has to work.
        var crownsOnly = new RarityLadderTarget("A1", RarityPlan.Uniform([Ix.Ladder.IndexOf("UR")!.Value]));
        var outstanding = crownsOnly.Outstanding(Ix, new Collection());

        Assert.NotEmpty(outstanding);
        Assert.All(outstanding, d => Assert.Equal("UR", d.SuppliedBy[0].Rarity));
    }

    [Fact]
    public void OneMapExpressesBothWhichRaritiesAndHowManyOfEach()
    {
        // Two of every diamond, one crown, and the star tiers ignored entirely — a shape
        // that needs selection and quantity to be the same fact.
        var plan = new RarityPlan(new Dictionary<int, int>
        {
            [0] = 2, [1] = 2, [2] = 2, [3] = 2,
            [Ix.Ladder.IndexOf("UR")!.Value] = 1
        });

        var outstanding = new RarityLadderTarget("A1", plan).Outstanding(Ix, new Collection());

        foreach (var d in outstanding)
        {
            var group = Snapshot.Rarities()[d.SuppliedBy[0].Rarity].Group;
            Assert.Contains(group, new[] { "Diamond", "Crown" });
            Assert.Equal(group == "Diamond" ? 2 : 1, d.Remaining);
        }
    }

    [Fact]
    public void CyclingARarityStepsThroughNoneOneTwoNone()
    {
        var plan = RarityPlan.Uniform([0], 1);

        Assert.Equal(1, plan.Copies(0));
        Assert.Equal(2, plan.Cycle(0).Copies(0));
        Assert.Equal(0, plan.Cycle(0).Cycle(0).Copies(0));
        Assert.False(plan.Cycle(0).Cycle(0).Wants(0));
    }

    [Fact]
    public void AnEmptyPlanIsRecognisable_SoTheUiCanRefuseToSaveIt()
    {
        Assert.True(new RarityPlan(new Dictionary<int, int>()).IsEmpty);
        Assert.True(new RarityPlan(new Dictionary<int, int> { [0] = 0 }).IsEmpty);
        Assert.False(RarityPlan.Uniform([0]).IsEmpty);
    }

    [Fact]
    public void AnEmptySelectionWantsNothing_WhichIsWhyTheUiForbidsIt()
    {
        // Documented rather than endorsed: with nothing selected every screen reads
        // "complete", so AppSession.ToggleTier refuses to clear the last rung.
        var target = new RarityLadderTarget("A1", new RarityPlan(new Dictionary<int, int>()));
        Assert.Empty(target.Outstanding(Ix, new Collection()));
    }
}
