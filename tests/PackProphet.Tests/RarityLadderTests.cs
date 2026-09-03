using PackProphet.Data;

namespace PackProphet.Tests;

public class RarityLadderTests
{
    private static RarityLadder Ladder => Snapshot.Index().Ladder;

    [Fact]
    public void EverySymbolGroupInTheData_IsKnownToTheLadder()
    {
        // The ladder degrades gracefully for an unknown group (sorts last) so a data
        // update cannot brick the app. This is the guard that tells us anyway.
        var unknown = Snapshot.Rarities().Values
            .Select(r => r.Group)
            .Distinct()
            .Where(g => !RarityLadder.IsKnownGroup(g))
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void LadderIsOrdered_DiamondsThenStarsThenShinyThenCrown()
    {
        var symbols = Ladder.Rungs.Select(r => r.Symbol).ToArray();
        Assert.Equal(
            new[] { "1◆", "2◆", "3◆", "4◆", "1★", "2★", "3★", "Shiny ✦", "Shiny ✦✦", "Crown ♛" },
            symbols);
    }

    [Fact]
    public void TheTargetsUsersActuallyPick_LandOnTheExpectedRungs()
    {
        // "All diamonds" and "up to 2 star" are the two the UI leads with, so pin them.
        Assert.Equal(3, Ladder.IndexOf("RR"));   // 4-diamond, the top of "all diamonds"
        Assert.Equal(5, Ladder.IndexOf("SR"));   // 2-star
        Assert.Equal(5, Ladder.IndexOf("SAR"));  // SAR shares the 2-star rung with SR
        Assert.Equal(9, Ladder.IndexOf("UR"));   // Crown, the whole ladder
    }

    [Fact]
    public void AllDiamondsSelection_WantsDiamondsAndNothingElse()
    {
        var diamonds = Ladder.UpTo(3);
        foreach (var code in new[] { "C", "U", "R", "RR" })
            Assert.True(Ladder.IsSelected(code, diamonds), $"{code} should be wanted");
        foreach (var code in new[] { "AR", "SR", "SAR", "IM", "S", "SSR", "UR" })
            Assert.False(Ladder.IsSelected(code, diamonds), $"{code} should not be wanted");
    }

    [Fact]
    public void ASelectionNeedNotBeContiguous()
    {
        // The shape a threshold cannot express: stars and crowns, no diamonds.
        var starsAndCrowns = Ladder.ByGroup("Star").Concat(Ladder.ByGroup("Crown")).ToHashSet();

        foreach (var code in new[] { "AR", "SR", "SAR", "IM", "UR" })
            Assert.True(Ladder.IsSelected(code, starsAndCrowns), $"{code} should be wanted");
        foreach (var code in new[] { "C", "U", "R", "RR" })
            Assert.False(Ladder.IsSelected(code, starsAndCrowns), $"{code} should not be wanted");

        // Shinies are their own family and are not swept in by "stars".
        Assert.False(Ladder.IsSelected("S", starsAndCrowns));
    }

    [Fact]
    public void DiamondsPlusOneStarTier_IsExpressible()
    {
        // "All diamonds, and also 3-star" — deliberately skipping 1-star and 2-star.
        var selection = Ladder.UpTo(3).Concat([Ladder.IndexOf("IM")!.Value]).ToHashSet();

        Assert.True(Ladder.IsSelected("RR", selection));
        Assert.True(Ladder.IsSelected("IM", selection));
        Assert.False(Ladder.IsSelected("AR", selection));
        Assert.False(Ladder.IsSelected("SR", selection));
    }

    [Fact]
    public void ByGroupSelectsAWholeSymbolFamily()
    {
        Assert.Equal(4, Ladder.ByGroup("Diamond").Count);
        Assert.Equal(3, Ladder.ByGroup("Star").Count);
        Assert.Equal(2, Ladder.ByGroup("Shiny").Count);
        Assert.Single(Ladder.ByGroup("Crown"));
        Assert.Equal(Ladder.Rungs.Count, Ladder.Everything.Count);
    }

    [Fact]
    public void UnknownRarity_IsNeverWanted()
    {
        // Under-claiming beats inventing a requirement the user can never satisfy.
        Assert.Null(Ladder.IndexOf("NOPE"));
        Assert.False(Ladder.IsSelected("NOPE", Ladder.Everything));
    }

    [Fact]
    public void EveryRarityCodeInTheData_SitsOnExactlyOneRung()
    {
        foreach (var code in Snapshot.Rarities().Keys)
            Assert.NotNull(Ladder.IndexOf(code));

        var placed = Ladder.Rungs.SelectMany(r => r.Codes).ToList();
        Assert.Equal(placed.Count, placed.Distinct().Count());
    }
}
