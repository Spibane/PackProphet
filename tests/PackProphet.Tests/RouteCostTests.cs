using PackProphet.Engine;

namespace PackProphet.Tests;

public class RouteCostTests
{
    private static RouteCost Routes =>
        new(Snapshot.Index(), Snapshot.Odds(), Snapshot.Rarities());

    private static PocketCard FirstOfRarity(string rarity, string set = "A1") =>
        Snapshot.Index().BySet[set].First(c => c.Rarity == rarity && c.IsPackObtainable);

    [Fact]
    public void CrownAndImmersive_HaveOnlyPacksAndPoints()
    {
        // The routing rule that matters most: 3-star and Crown are neither tradeable nor
        // available from Wonder Pick, so a Crown target is a pure pack-and-points grind.
        foreach (var rarity in new[] { "UR", "IM" })
        {
            var r = Routes.For(FirstOfRarity(rarity), new Collection());

            Assert.False(r.Options.Single(o => o.Route == AcquisitionRoute.Trade).Available);
            Assert.Equal(RouteBlock.NotTradeable,
                r.Options.Single(o => o.Route == AcquisitionRoute.Trade).Block);

            Assert.False(r.Options.Single(o => o.Route == AcquisitionRoute.WonderPick).Available);
            Assert.Equal(RouteBlock.NotInWonderPick,
                r.Options.Single(o => o.Route == AcquisitionRoute.WonderPick).Block);

            Assert.True(r.Options.Single(o => o.Route == AcquisitionRoute.Pull).Available);
            Assert.True(r.Options.Single(o => o.Route == AcquisitionRoute.PackPoints).Available);
        }
    }

    [Fact]
    public void ShinyIsTradeableButNotWonderPickable()
    {
        var shiny = Snapshot.Index().All.First(c => c.Rarity == "S" && c.IsPackObtainable);
        var r = Routes.For(shiny, new Collection());

        Assert.Equal(RouteBlock.NotInWonderPick,
            r.Options.Single(o => o.Route == AcquisitionRoute.WonderPick).Block);

        var trade = r.Options.Single(o => o.Route == AcquisitionRoute.Trade);
        Assert.Equal(10_000, trade.Dust);   // and it undercuts 2-star at 25,000
        Assert.Equal(RouteBlock.None, trade.Block);
    }

    [Fact]
    public void WonderPickCost_RisesWithRarity()
    {
        int Cost(string rarity) => Routes.For(FirstOfRarity(rarity), new Collection())
            .Options.Single(o => o.Route == AcquisitionRoute.WonderPick).Stamina!.Value;

        Assert.Equal(1, Cost("C"));
        Assert.Equal(2, Cost("RR"));
        Assert.Equal(3, Cost("AR"));
        Assert.Equal(4, Cost("SR"));
    }

    [Fact]
    public void PromoCards_CannotBePulled_AndSayWhy()
    {
        var promo = Snapshot.Index().BySet["PROMO-A"].First(c => !c.IsPackObtainable);
        var pull = Routes.For(promo, new Collection())
            .Options.Single(o => o.Route == AcquisitionRoute.Pull);

        Assert.False(pull.Available);
        Assert.Equal(RouteBlock.NotSoldInPacks, pull.Block);
        Assert.NotNull(pull.Note);
    }

    [Fact]
    public void CardsFromASetWithNoPullRates_ReportThatSpecifically()
    {
        // B4 has real packs but no published rates, which is a different problem from being
        // unavailable — and the user deserves to be told which.
        var b4 = Snapshot.Index().BySet["B4"].First();
        var pull = Routes.For(b4, new Collection()).Options.Single(o => o.Route == AcquisitionRoute.Pull);

        Assert.False(pull.Available);
        Assert.Equal(RouteBlock.NoPullRates, pull.Block);
    }

    [Fact]
    public void CheapestRoute_NeverIncludesTradeOrWonderPick()
    {
        // Those are separate currencies with no exchange rate, so including them in a
        // "cheapest" comparison would be inventing a conversion.
        foreach (var rarity in new[] { "C", "R", "RR", "AR", "SR" })
        {
            var cheapest = Routes.For(FirstOfRarity(rarity), new Collection()).Cheapest;
            Assert.NotNull(cheapest);
            Assert.Contains(cheapest.Route, new[] { AcquisitionRoute.Pull, AcquisitionRoute.PackPoints });
        }
    }

    [Fact]
    public void PointsBeatPullingForMostSingleCards_WhichIsWhyRankingByValueMatters()
    {
        // Counter-intuitive but true: buying one specific common costs 7 packs of points
        // while pulling that exact common averages ~16. Points look cheaper for almost
        // everything, because they are a BYPRODUCT of opening rather than a rival to it.
        // That is exactly why "cheapest route" alone is a poor guide and the shop must be
        // ranked by value ratio instead.
        Assert.Equal(AcquisitionRoute.PackPoints,
            Routes.For(FirstOfRarity("C"), new Collection()).Cheapest!.Route);
        Assert.Equal(AcquisitionRoute.PackPoints,
            Routes.For(FirstOfRarity("UR"), new Collection()).Cheapest!.Route);
    }

    [Fact]
    public void PointsPriceIsPointsOverFive()
    {
        var crown = Routes.For(FirstOfRarity("UR"), new Collection())
            .Options.Single(o => o.Route == AcquisitionRoute.PackPoints);

        Assert.Equal(2500, crown.Points);
        Assert.Equal(2500.0 / GameRules.PackPointsPerPack, crown.PacksEquivalent!.Value, 6);
    }

    [Fact]
    public void TradeIsBlockedWithoutASpareOfTheSameRarity()
    {
        var card = FirstOfRarity("R");
        var blocked = Routes.For(card, new Collection())
            .Options.Single(o => o.Route == AcquisitionRoute.Trade);
        Assert.False(blocked.Available);
        Assert.Contains("same-rarity", blocked.Note);

        // Two copies of a DIFFERENT rare gives one spare to offer.
        var other = Snapshot.Index().All.First(c =>
            c.Rarity == "R" && c.OwnershipKey != card.OwnershipKey);
        var withSpare = Routes.For(card, new Collection().With(other.OwnershipKey, 2))
            .Options.Single(o => o.Route == AcquisitionRoute.Trade);

        Assert.True(withSpare.Available);
        Assert.Equal(1200, withSpare.Dust);
        Assert.Equal(1, withSpare.Stamina);
    }

    [Fact]
    public void PointsShopRanking_PutsCrownsFirstAndRejectsBadBuys()
    {
        var target = RarityLadderTarget.UpTo("A1", 9, Snapshot.Index(), 1);
        var outstanding = target.Outstanding(Snapshot.Index(), new Collection());

        var ranked = Routes.PointsShopRanking(outstanding, new Collection());

        Assert.NotEmpty(ranked);
        Assert.All(ranked, r => Assert.True(r.Value > 1.0));            // only worthwhile buys
        Assert.Equal(ranked.OrderByDescending(r => r.Value), ranked);   // best value first
        Assert.Equal("UR", ranked[0].Card.Rarity);                      // Crowns are the standout

        // Double Rare and Immersive are BAD buys — an Immersive pulls in ~90 packs but costs
        // 300 packs of points — so they must not be recommended at all.
        var all = Routes.PointsShopRanking(outstanding, new Collection(), onlyWorthwhile: false);
        Assert.All(all.Where(r => r.Card.Rarity == "RR"), r => Assert.True(r.Value < 1.0));
        Assert.All(all.Where(r => r.Card.Rarity == "IM"), r => Assert.True(r.Value < 1.0));
        Assert.DoesNotContain(ranked, r => r.Card.Rarity is "RR" or "IM");
    }
}
