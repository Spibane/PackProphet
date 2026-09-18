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

    [Fact]
    public void GoldFlair_NeedsTenCopiesOfAOneToThreeDiamond()
    {
        Assert.True(GameRules.EarnsGoldFlair("Diamond", 1, 10, isPromo: false));
        Assert.True(GameRules.EarnsGoldFlair("Diamond", 3, 25, isPromo: false));
        Assert.False(GameRules.EarnsGoldFlair("Diamond", 3, 9, isPromo: false));
    }

    [Fact]
    public void GoldFlair_StopsAtThreeDiamonds()
    {
        // Four-diamond cards are diamonds and still earn nothing, which is why the rule needs
        // the symbol COUNT and not just the family.
        Assert.False(GameRules.EarnsGoldFlair("Diamond", 4, 10, isPromo: false));
        Assert.False(GameRules.EarnsGoldFlair("Diamond", 4, 99, isPromo: false));
    }

    [Fact]
    public void GoldFlair_IsDiamondsOnly()
    {
        // A tenth copy of a star earns nothing either, so the rule is neither "ten copies" nor
        // "ten copies of a diamond".
        Assert.False(GameRules.EarnsGoldFlair("Star", 1, 10, isPromo: false));
        Assert.False(GameRules.EarnsGoldFlair("Shiny", 1, 99, isPromo: false));
        Assert.False(GameRules.EarnsGoldFlair("Crown", 1, 10, isPromo: false));
    }

    [Fact]
    public void GoldFlair_MatchesTheLaddersOwnRungs()
    {
        // Checked against the real ladder, so a group rename or a new diamond rung upstream
        // surfaces here rather than silently switching flair off for every card.
        var diamonds = Snapshot.Index().Ladder.Rungs.Where(r => r.GlyphClass == "diamond").ToArray();

        Assert.Equal(4, diamonds.Length);
        Assert.All(diamonds, r =>
            Assert.Equal(r.Count <= 3, GameRules.EarnsGoldFlair(r.Group, r.Count, 10, isPromo: false)));
    }

    [Fact]
    public void GoldFlair_NeverAppliesToAPromo()
    {
        // Promos carry ordinary rarity codes — 79 commons and 70 rares in the snapshot — so a
        // rule based on rarity alone gilded 151 cards that can never earn it.
        Assert.False(GameRules.EarnsGoldFlair("Diamond", 1, 10, isPromo: true));
        Assert.False(GameRules.EarnsGoldFlair("Diamond", 3, 99, isPromo: true));
    }

    [Fact]
    public void GoldFlair_ExcludesEveryPromoInTheData()
    {
        // Against the real card list, because the exclusion depends on how promos are recorded
        // rather than on anything the rule can see for itself.
        var index = Snapshot.Index();
        var promos = index.All.Where(c => c.IsPromo).ToArray();

        Assert.NotEmpty(promos);
        Assert.All(promos, card =>
        {
            var rung = index.Ladder.IndexOf(card.Rarity);
            if (rung is null) return;

            var r = index.Ladder.Rungs[rung.Value];
            Assert.False(GameRules.EarnsGoldFlair(r.Group, r.Count, 10, card.IsPromo));
        });
    }

    [Fact]
    public void PackPoints_SnapToTheFivesTheGameDealsIn()
    {
        // Every balance the game can hold is a multiple of five, so anything else was mistyped.
        Assert.Equal(40, GameRules.SnapPackPoints(40));
        Assert.Equal(40, GameRules.SnapPackPoints(43));
        Assert.Equal(40, GameRules.SnapPackPoints(44));
        Assert.Equal(45, GameRules.SnapPackPoints(45));

        // Down, never up: a balance reported higher than it is recommends a card the shop will
        // refuse to sell.
        Assert.Equal(0, GameRules.SnapPackPoints(4));

        // The cap is itself a multiple of five, so clamping cannot land off the grid.
        Assert.Equal(GameRules.PackPointsCap, GameRules.SnapPackPoints(GameRules.PackPointsCap));
        Assert.Equal(GameRules.PackPointsCap, GameRules.SnapPackPoints(99_999));
        Assert.Equal(0, GameRules.PackPointsCap % GameRules.PackPointsPerPack);

        Assert.Equal(0, GameRules.SnapPackPoints(-25));
    }

    [Fact]
    public void TheDailyHandoutIsTwoCurrenciesAndNeverATotal()
    {
        // Pack and Wonder hourglasses do not convert, so the day pays two figures. Both positive,
        // or the button on the Resources page credits nothing.
        Assert.True(GameRules.DailyPackHourglasses > 0);
        Assert.True(GameRules.DailyWonderHourglasses > 0);
    }
}
