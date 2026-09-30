using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Engine;

namespace PackProphet.Tests;

public class RouteCostTests
{
    private static RouteCost Routes =>
        new(Snapshot.Index(), Snapshot.Odds(), Snapshot.Rarities());

    private static CardIndex Ix => Snapshot.Index();

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
        //
        // With B4's rates withheld, which is how it stood until 2.11.0 published them: named
        // directly, this test stopped having an unpriced set to ask about. See
        // Snapshot.RatesWithout.
        var routes = new RouteCost(Ix, new PackOdds(Ix, Snapshot.RatesWithout("B4")), Snapshot.Rarities());
        var b4 = Ix.BySet["B4"].First();
        var pull = routes.For(b4, new Collection()).Options.Single(o => o.Route == AcquisitionRoute.Pull);

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

    [Fact]
    public void PromoCards_HaveNoPointsRoute()
    {
        // Points are earned by opening a set's packs and spent only within that set, so a set
        // with no packs has no point economy. Every rarity carries a point price, which made it
        // easy to quote 35 points for a promo — a route with no pack to earn it in and no shop
        // to spend it at. Found by a Wonder Pick test valuing an unobtainable promo at 7 packs.
        var index = Snapshot.Index();
        var promo = index.All.First(c => CardIndex.IsPromoSet(c.Set));

        var routes = Routes.For(promo, new Collection());
        var points = routes.Options.Single(o => o.Route == AcquisitionRoute.PackPoints);

        Assert.False(points.Available);
        Assert.Equal(RouteBlock.NotSoldInPacks, points.Block);
        // And with no pull route either, there is no packs-priced route at all — which is the
        // honest answer for a card that only ever came from an event.
        Assert.Null(routes.Cheapest);
    }

    [Fact]
    public void CardsInOrdinarySets_StillHaveAPointsRoute()
    {
        var card = Snapshot.Index().BySet["A1"].First(c => c.IsPackObtainable);
        var points = Routes.For(card, new Collection())
            .Options.Single(o => o.Route == AcquisitionRoute.PackPoints);

        Assert.True(points.Available);
        Assert.True(points.Points > 0);
    }

    [Fact]
    public void Shares_carry_the_diamonds_and_stop_there()
    {
        // A Share needs no card back, no dust and no stamina, so unlike every other route its
        // availability is a pure rarity question - which is exactly why it must not be folded into
        // the trade option, whose whole difficulty is having something to offer.
        foreach (var code in new[] { "C", "U", "R", "RR" })
        {
            var card = Ix.All.First(c => c.Rarity == code && !c.IsPromo);
            var share = Routes.For(card, new Collection())
                .Options.Single(o => o.Route == AcquisitionRoute.Share);

            Assert.True(share.Available, code);
            Assert.Null(share.Dust);
            Assert.Null(share.Stamina);
        }

        foreach (var code in new[] { "AR", "SR", "S", "IM", "UR" })
        {
            var card = Ix.All.First(c => c.Rarity == code);
            var share = Routes.For(card, new Collection())
                .Options.Single(o => o.Route == AcquisitionRoute.Share);

            Assert.False(share.Available, code);
            Assert.Equal(RouteBlock.NotShareable, share.Block);
        }
    }

    [Fact]
    public void A_share_is_never_the_cheapest_route_because_it_has_no_price_in_packs()
    {
        // It costs a friend's card and a day of your allowance, neither of which converts to
        // packs. Quoting it as the cheapest route would make every diamond look free.
        var card = Ix.All.First(c => c.Rarity == "RR" && !c.IsPromo && c.IsPackObtainable);

        var routes = Routes.For(card, new Collection());

        Assert.NotNull(routes.Cheapest);
        Assert.NotEqual(AcquisitionRoute.Share, routes.Cheapest!.Route);
        Assert.Null(routes.Options.Single(o => o.Route == AcquisitionRoute.Share).PacksEquivalent);
    }

    [Fact]
    public void A_promo_is_offered_neither_a_trade_nor_a_share()
    {
        // The bug this pins: RouteCost checked only the RARITY, so a promo carrying an ordinary
        // C/U/R code was quoted a shinedust price for a trade the game refuses outright — while
        // the trade queue and the board advisor, which both remembered the promo rule, refused it.
        // Two parts of the app disagreeing about the same card, and the wrong one was the part
        // shown on card detail.
        var promo = Ix.All.First(c => c.IsPromo && GameRules.IsTradeable(c.Rarity));
        var routes = Routes.For(promo, new Collection());

        var trade = routes.Options.Single(o => o.Route == AcquisitionRoute.Trade);
        Assert.Equal(GameRules.PromosTradeable, trade.Available);
        if (!GameRules.PromosTradeable) Assert.Equal(RouteBlock.NotTradeable, trade.Block);

        // A diamond promo would otherwise look shareable on rarity alone, for the same reason.
        var diamondPromo = Ix.All.FirstOrDefault(c => c.IsPromo && GameRules.IsShareable(c.Rarity));
        if (diamondPromo is not null)
        {
            var share = Routes.For(diamondPromo, new Collection())
                .Options.Single(o => o.Route == AcquisitionRoute.Share);

            Assert.Equal(GameRules.PromosShareable, share.Available);
        }
    }

    [Fact]
    public void The_card_level_rule_is_the_one_every_caller_uses()
    {
        // Rarity alone is not the rule, and the two overloads must not be confused: this is the
        // difference that produced the bug above.
        var promo = Ix.All.First(c => c.IsPromo && GameRules.IsTradeable(c.Rarity));

        Assert.True(GameRules.IsTradeable(promo.Rarity));
        Assert.Equal(GameRules.PromosTradeable, GameRules.CanBeTraded(promo));

        var ordinary = Ix.All.First(c => !c.IsPromo && c.Rarity == "R");
        Assert.True(GameRules.CanBeTraded(ordinary));
        Assert.True(GameRules.CanBeShared(ordinary));

        var crown = Ix.All.First(c => c.Rarity == "UR");
        Assert.False(GameRules.CanBeTraded(crown));
        Assert.False(GameRules.CanBeShared(crown));
    }
}
