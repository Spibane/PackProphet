using PackProphet.Engine;

namespace PackProphet.Tests;

public class PointsLedgerTests
{
    private static PointsLedger Ledger => new(Snapshot.Index(), Snapshot.Rarities());

    private static PackProphet.Data.CardIndex Ix => Snapshot.Index();

    /// <summary>Collect everything, one copy each - the widest plan, so nothing is filtered out.</summary>
    private static RarityPlan All => RarityPlan.Uniform(Ix.Ladder.Everything);

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

        var state = Ledger.Describe("A1", earned["A1"], new Collection(), All);
        Assert.True(state.AtCap);
        Assert.Equal(0, state.PacksUntilCap);
    }

    [Fact]
    public void PacksUntilCap_CountsDownCorrectly()
    {
        var state = Ledger.Describe("A1", GameRules.PackPointsCap - 20, new Collection(), All);
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
        var state = Ledger.Describe("A1", 200, new Collection(), All);

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
        var before = Ledger.Describe("A1", 2500, new Collection(), All).AffordableNow;
        Assert.NotEmpty(before);

        var owned = before.Aggregate(new Collection(), (c, card) => c.With(card.OwnershipKey, 1));
        Assert.Empty(Ledger.Describe("A1", 2500, owned, All).AffordableNow);
    }

    [Fact]
    public void Describe_NamesTheRarestCardStillWantedAndThePacksToAffordIt()
    {
        var empty = Ledger.Describe("A1", 0, new Collection(), All);

        Assert.NotNull(empty.RarestWanted);
        Assert.True(empty.RarestPoints > 0);

        // From nothing, the packs figure is simply the price at five points a pack.
        var expected = (int)Math.Ceiling(empty.RarestPoints / (double)GameRules.PackPointsPerPack);
        Assert.Equal(expected, empty.PacksToAfford);

        // The rarest card wanted must sit at the top of the ladder among what is missing.
        var rung = Ix.Ladder.IndexOf(empty.RarestWanted!.Rarity);
        var highest = Ix.BySet["A1"]
            .Select(c => Ix.Ladder.IndexOf(c.Rarity) ?? -1)
            .Max();
        Assert.Equal(highest, rung);
    }

    [Fact]
    public void Describe_CountsNoPacksForACardTheBalanceAlreadyCovers()
    {
        var capped = Ledger.Describe("A1", GameRules.PackPointsCap, new Collection(), All);

        // Nothing in the game costs more than the cap, so a capped balance affords anything —
        // which is exactly why sitting there is waste rather than saving.
        Assert.Equal(0, capped.PacksToAfford);
        Assert.True(capped.RarestPoints <= GameRules.PackPointsCap);
    }

    [Fact]
    public void Describe_MovesToTheNextRungOnceTheRarestIsOwned()
    {
        var first = Ledger.Describe("A1", 0, new Collection(), All);
        var topRung = Ix.Ladder.IndexOf(first.RarestWanted!.Rarity);

        // Own every card on that rung, and the target must drop to a lower one rather than
        // repeating a card that is no longer wanted.
        var owned = Ix.BySet["A1"]
            .Where(c => Ix.Ladder.IndexOf(c.Rarity) == topRung)
            .Aggregate(new Collection(), (c, card) => c.With(card.OwnershipKey, 1));

        var next = Ledger.Describe("A1", 0, owned, All);

        Assert.NotNull(next.RarestWanted);
        Assert.True(Ix.Ladder.IndexOf(next.RarestWanted!.Rarity) < topRung);
    }

    [Fact]
    public void Describe_IgnoresRaritiesTheUserDoesNotCollect()
    {
        // The bug this pins: with only diamonds collected, the shop advice named a Crown as the
        // card to save for - the dearest thing in the set, and one the user had said they do not
        // chase. A rung wanted zero times is not "still wanted" at any price.
        var diamonds = RarityPlan.Uniform(Ix.Ladder.ByGroup("Diamond"));

        var row = Ledger.Describe("A1", GameRules.PackPointsCap, new Collection(), diamonds);

        Assert.NotNull(row.RarestWanted);
        var group = Ix.Ladder.Rungs.First(r => r.Codes.Contains(row.RarestWanted!.Rarity)).Group;
        Assert.Equal("Diamond", group);

        // And the affordable list is filtered by the same rule, or the two columns would be
        // answering different questions on the same row.
        Assert.All(row.AffordableNow, card =>
            Assert.Equal("Diamond",
                Ix.Ladder.Rungs.First(r => r.Codes.Contains(card.Rarity)).Group));
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

        var warnings = Ledger.Warnings(balances, new Collection(), _ => All);

        Assert.Equal("A1", warnings[0].Set);
        Assert.True(warnings[0].AtCap);
        Assert.DoesNotContain(warnings, w => w.Set == "A3");
    }

    [Fact]
    public void A_finished_plan_with_points_left_names_what_they_can_buy_instead()
    {
        // Diamonds only, and every diamond in the set owned: the plan is done, but the balance is
        // stranded -- points can only be spent in the set that earned them. So the columns answer
        // what the points CAN buy rather than "Set Complete" over a balance of 800.
        var diamonds = RarityPlan.Uniform(Ix.Ladder.ByGroup("Diamond"));
        var owned = Ix.BySet["A1"]
            .Where(c => Ix.Ladder.Rungs.Any(r => r.Group == "Diamond" && r.Codes.Contains(c.Rarity)))
            .Aggregate(new Collection(), (c, card) => c.With(card.OwnershipKey, 1));

        var row = Ledger.Describe("A1", 800, owned, diamonds);

        Assert.True(row.BeyondTarget);
        Assert.NotNull(row.RarestWanted);
        Assert.NotEqual("Diamond",
            Ix.Ladder.Rungs.First(r => r.Codes.Contains(row.RarestWanted!.Rarity)).Group);
        Assert.NotEmpty(row.AffordableNow);
        Assert.All(row.AffordableNow, c => Assert.Equal(0, owned.Of(c)));
    }

    [Fact]
    public void A_finished_plan_with_no_points_is_just_finished()
    {
        // Nothing to spend, so there is nothing to point at: the fallback would be advice about a
        // balance the user does not have.
        var diamonds = RarityPlan.Uniform(Ix.Ladder.ByGroup("Diamond"));
        var owned = Ix.BySet["A1"]
            .Where(c => Ix.Ladder.Rungs.Any(r => r.Group == "Diamond" && r.Codes.Contains(c.Rarity)))
            .Aggregate(new Collection(), (c, card) => c.With(card.OwnershipKey, 1));

        var row = Ledger.Describe("A1", 0, owned, diamonds);

        Assert.False(row.BeyondTarget);
        Assert.Null(row.RarestWanted);
        Assert.Empty(row.AffordableNow);
    }

    [Fact]
    public void An_unfinished_plan_never_looks_outside_it()
    {
        // The fallback is for a finished plan only. While anything inside it is outstanding, a
        // card from a rung the user ignores must not appear as the thing to save for.
        var diamonds = RarityPlan.Uniform(Ix.Ladder.ByGroup("Diamond"));
        var row = Ledger.Describe("A1", 2000, new Collection(), diamonds);

        Assert.False(row.BeyondTarget);
        Assert.Equal("Diamond",
            Ix.Ladder.Rungs.First(r => r.Codes.Contains(row.RarestWanted!.Rarity)).Group);
    }
}
