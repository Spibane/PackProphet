namespace PackProphet.Tests;

/// <summary>
/// GameRules holds hand-entered game constants. These tests pin them against the
/// shipped data snapshot, so that when upstream adds a rarity or the game changes a
/// rule, a test fails instead of a recommendation quietly going wrong.
/// </summary>
public class GameRulesTests
{
    [Fact]
    public void EveryRarityInTheData_IsClassifiedForWonderPick()
    {
        // A new rarity appearing upstream must be an explicit decision, not a silent
        // default — WonderPickCost throws for anything CanAppearInWonderPick rejects.
        foreach (var code in Snapshot.Rarities().Keys)
        {
            if (GameRules.CanAppearInWonderPick(code))
                Assert.InRange(GameRules.WonderPickCost(code), 1, 4);
            else
                Assert.Throws<ArgumentOutOfRangeException>(() => GameRules.WonderPickCost(code));
        }
    }

    [Fact]
    public void WonderPickCost_CeilingIsFour_BecauseTwoStarIsTheCeiling()
    {
        var costs = Snapshot.Rarities().Keys
            .Where(GameRules.CanAppearInWonderPick)
            .Select(GameRules.WonderPickCost)
            .ToList();

        Assert.Equal(1, costs.Min());
        Assert.Equal(4, costs.Max());
    }

    [Fact]
    public void WonderPickExclusions_MatchTheTopTiers()
    {
        // 3-star, Crown and Shiny never appear. This is load-bearing: it means Wonder
        // Pick cannot contribute to those tiers at all, so RouteCost must not offer it.
        foreach (var code in new[] { "IM", "UR", "S", "SSR" })
            Assert.False(GameRules.CanAppearInWonderPick(code), $"{code} must be excluded");
    }

    [Fact]
    public void IsTradeable_AgreesWithTheDatasTradeableFlag()
    {
        foreach (var (code, r) in Snapshot.Rarities())
            Assert.Equal(r.Tradeable, GameRules.IsTradeable(code));
    }

    [Fact]
    public void UntradeableRarities_HaveNoDustPrice()
    {
        foreach (var (code, r) in Snapshot.Rarities())
        {
            if (GameRules.IsTradeable(code)) Assert.NotNull(r.TradePrice);
            else Assert.Null(r.TradePrice);
        }
    }

    [Fact]
    public void DustLadder_IsNotMonotonicInRarity()
    {
        // Documents a real, counter-intuitive fact the trade helper depends on:
        // 1-star (AR) is CHEAPER than 4-diamond (RR), and Shiny undercuts 2-star.
        // If upstream ever "fixes" this into a monotonic ladder, the trade advice
        // changes meaningfully and we want to be told.
        var r = Snapshot.Rarities();
        Assert.True(r["AR"].TradePrice < r["RR"].TradePrice,
            "AR was expected to be cheaper than RR");
        Assert.True(r["S"].TradePrice < r["SR"].TradePrice,
            "Shiny was expected to undercut 2-star");
    }

    [Fact]
    public void PacksPerDay_RespectsPremium()
    {
        Assert.Equal(2, GameRules.PacksPerDay(premium: false));
        Assert.Equal(3, GameRules.PacksPerDay(premium: true));
    }

    [Fact]
    public void EmptyStaminaPool_TakesSixtyHoursToRefill()
    {
        // Underpins the "no rush until Thursday" framing of the at-cap warning.
        Assert.Equal(TimeSpan.FromHours(60), GameRules.StaminaFullRefill);
    }
}
