using PackProphet.Engine;

namespace PackProphet.Tests;

public class PointsLedgerTests
{
    private static PointsLedger Ledger => new(Snapshot.Index(), Snapshot.Rarities());

    [Fact]
    public void PointsAccrueAtFivePerPack()
    {
        var earned = Ledger.EarnedFrom([("A1", 10)]);
        Assert.Equal(50, earned["A1"]);
    }

    [Fact]
    public void PromoPacksEarnNothing()
    {
        var earned = Ledger.EarnedFrom([("PROMO-A", 100), ("A1", 2)]);
        Assert.False(earned.ContainsKey("PROMO-A"));
        Assert.Equal(10, earned["A1"]);
    }

    [Fact]
    public void PointsAreCapped_AndPacksOpenedWhileCappedEarnNothing()
    {
        var earned = Ledger.EarnedFrom([("A1", 10_000)]);
        Assert.Equal(GameRules.PackPointsCap, earned["A1"]);

        var state = Ledger.Describe("A1", earned["A1"], new Collection());
        Assert.True(state.AtCap);
        Assert.Equal(0, state.PacksUntilCap);
    }

    [Fact]
    public void PacksUntilCap_CountsDownCorrectly()
    {
        var state = Ledger.Describe("A1", GameRules.PackPointsCap - 20, new Collection());
        Assert.False(state.AtCap);
        Assert.Equal(4, state.PacksUntilCap);   // 20 points remaining at 5 per pack
    }

    [Fact]
    public void PointsAreScopedToTheirOwnSet()
    {
        // Points earned from A1 packs cannot buy an A2 card, so each set is tracked alone.
        var earned = Ledger.EarnedFrom([("A1", 100), ("A2", 2)]);
        Assert.Equal(500, earned["A1"]);
        Assert.Equal(10, earned["A2"]);
    }

    [Fact]
    public void AffordableNow_ListsOnlyMissingCardsWithinBudget()
    {
        var state = Ledger.Describe("A1", 200, new Collection());

        Assert.NotEmpty(state.AffordableNow);
        Assert.All(state.AffordableNow, c => Assert.Equal("A1", c.Set));
        Assert.All(state.AffordableNow, c =>
            Assert.True(Snapshot.Rarities()[c.Rarity].Points <= 200));
        // 200 points buys commons/uncommons/rares but not a 500-point double rare.
        Assert.DoesNotContain(state.AffordableNow, c => c.Rarity == "RR");
    }

    [Fact]
    public void AlreadyOwnedCards_AreNotListedAsAffordable()
    {
        var before = Ledger.Describe("A1", 2500, new Collection()).AffordableNow;
        Assert.NotEmpty(before);

        var owned = before.Aggregate(new Collection(), (c, card) => c.With(card.OwnershipKey, 1));
        Assert.Empty(Ledger.Describe("A1", 2500, owned).AffordableNow);
    }

    [Fact]
    public void Warnings_LeadWithTheSetActivelyWastingPoints()
    {
        var balances = new Dictionary<string, int>
        {
            ["A1"] = GameRules.PackPointsCap,        // wasting every pack
            ["A2"] = GameRules.PackPointsCap - 50,   // 10 packs away
            ["A3"] = 0                               // nowhere near
        };

        var warnings = Ledger.Warnings(balances, new Collection());

        Assert.Equal("A1", warnings[0].Set);
        Assert.True(warnings[0].AtCap);
        Assert.DoesNotContain(warnings, w => w.Set == "A3");
    }
}
