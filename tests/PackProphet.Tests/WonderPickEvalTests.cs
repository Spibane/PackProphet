using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Engine;
using PackProphet.State;

namespace PackProphet.Tests;

public class WonderPickEvalTests
{
    private static WonderPickEval Eval => new(
        Snapshot.Index(),
        new RouteCost(Snapshot.Index(), Snapshot.Odds(), Snapshot.Rarities()));

    /// <summary>A1 cards of a given rarity code, which the Wonder Pick rules are defined over.</summary>
    private static PocketCard[] OfRarity(string code, int take) =>
        Snapshot.Index().BySet["A1"]
            .Where(c => c.Rarity == code && c.IsPackObtainable)
            .DistinctBy(c => c.OwnershipKey)
            .Take(take)
            .ToArray();

    /// <summary>Everything in A1 up to 2-star, so most offer cards are wanted.</summary>
    private static ICompletionTarget WantsA1 =>
        RarityLadderTarget.UpTo("A1", Snapshot.Index().Ladder.IndexOf("SR")!.Value, Snapshot.Index());

    // ---- cost ------------------------------------------------------------------------

    [Fact]
    public void Cost_IsSetByTheHighestRarityPresent()
    {
        var commons = OfRarity("C", 5);
        Assert.Equal(1, Eval.StaminaCost(commons));

        // One card of a higher rarity re-prices the whole offer, whatever the other four are.
        Assert.Equal(2, Eval.StaminaCost([.. commons.Take(4), .. OfRarity("RR", 1)]));
        Assert.Equal(3, Eval.StaminaCost([.. commons.Take(4), .. OfRarity("AR", 1)]));
        Assert.Equal(4, Eval.StaminaCost([.. commons.Take(4), .. OfRarity("SR", 1)]));
    }

    [Fact]
    public void Cost_OfAnOfferWithNoEligibleCards_DoesNotThrow()
    {
        // Cannot happen in the game — 3-star, Crown and Shiny never appear — but a mis-tap on
        // the picker must not take the screen down.
        var crowns = OfRarity("UR", 2);
        Assert.NotEmpty(crowns);
        Assert.Equal(1, Eval.StaminaCost(crowns));
    }

    // ---- expected value --------------------------------------------------------------

    [Fact]
    public void ExpectedValue_IsTheMeanOverFive_NotTheBestCard()
    {
        // You receive ONE card at random. Hoping for the good one is how players talk about
        // Wonder Picks and it is not what the odds say.
        var offer = new List<PocketCard>();
        offer.AddRange(OfRarity("C", 4));
        offer.AddRange(OfRarity("SR", 1));

        var appraisal = Eval.Appraise(offer, WantsA1, new Collection());
        var best = appraisal.Cards.Max(c => c.Value);

        Assert.True(appraisal.ExpectedValue > 0);
        Assert.True(appraisal.ExpectedValue < best,
            $"EV {appraisal.ExpectedValue} should be below the best card's {best}");
        Assert.Equal(appraisal.Cards.Sum(c => c.Value) / 5, appraisal.ExpectedValue, 9);
    }

    [Fact]
    public void CardsYouAlreadyHaveEnoughOf_AreWorthNothing()
    {
        var offer = OfRarity("C", 5);
        var owned = new Collection(offer.ToDictionary(c => c.OwnershipKey, _ => 1));

        var appraisal = Eval.Appraise(offer, WantsA1, owned);

        Assert.Equal(0, appraisal.ExpectedValue);
        Assert.Equal(OfferVerdict.NothingWanted, appraisal.Verdict);
        Assert.All(appraisal.Cards, c => Assert.Equal(0, c.Value));
    }

    [Fact]
    public void RarerCardsAreWorthMore_BecauseTheyCostMorePacksToGetOtherwise()
    {
        var common = Eval.Appraise(OfRarity("C", 5), WantsA1, new Collection());
        var stars = Eval.Appraise(
            [.. OfRarity("C", 4), .. OfRarity("SR", 1)], WantsA1, new Collection());

        Assert.True(stars.ExpectedValue > common.ExpectedValue);
    }

    // ---- the overpriced flag ---------------------------------------------------------

    [Fact]
    public void AnExpensiveOffer_PricedByACardYouAlreadyOwn_IsFlaggedOverpriced()
    {
        // The single most useful thing this screen says, and it falls straight out of the
        // pricing rule: cost tracks the highest rarity, which has nothing to do with what the
        // offer is worth to you.
        var star = OfRarity("SR", 1);
        var commons = OfRarity("C", 4);

        var owned = new Collection(star.ToDictionary(c => c.OwnershipKey, _ => 1));
        var appraisal = Eval.Appraise([.. commons, .. star], WantsA1, owned);

        Assert.Equal(4, appraisal.StaminaCost);
        Assert.Equal(OfferVerdict.Overpriced, appraisal.Verdict);
        Assert.Equal(star[0].Name, appraisal.CostDriver);
        Assert.False(appraisal.CostDriverWanted);
        // Still has value — the four commons — which is exactly why the flag has to say that
        // the value is not what you are paying for.
        Assert.True(appraisal.ExpectedValue > 0);
    }

    [Fact]
    public void AnExpensiveOffer_PricedByACardYouWANT_IsNotOverpriced()
    {
        var appraisal = Eval.Appraise(
            [.. OfRarity("C", 4), .. OfRarity("SR", 1)], WantsA1, new Collection());

        Assert.Equal(4, appraisal.StaminaCost);
        Assert.True(appraisal.CostDriverWanted);
        Assert.Equal(OfferVerdict.Take, appraisal.Verdict);
    }

    [Fact]
    public void ACheapOfferIsNeverCalledOverpriced()
    {
        // At 1 stamina there is no premium to be paying, so the flag would be noise. The
        // commons here are owned, so the price driver is unwanted — the only thing keeping this
        // out of Overpriced is the cost floor.
        var commons = OfRarity("C", 5);
        var wanted = OfRarity("U", 1);
        var owned = new Collection(commons.ToDictionary(c => c.OwnershipKey, _ => 1));

        var appraisal = Eval.Appraise([.. commons.Take(4), .. wanted], WantsA1, owned);

        Assert.True(appraisal.StaminaCost < WonderPickEval.OverpricedFrom);
        Assert.NotEqual(OfferVerdict.Overpriced, appraisal.Verdict);
    }

    // ---- the reservation threshold ---------------------------------------------------

    [Fact]
    public void Threshold_IsZeroUntilThereIsAHistoryWorthTrusting()
    {
        // Take anything useful while learning: a percentile of four samples is not a
        // distribution, and refusing offers on the strength of one would be worse than having
        // no policy at all.
        Assert.Equal(0, WonderPickEval.ReservationThreshold([1, 2, 3, 4], staminaNow: 3));
    }

    [Fact]
    public void Threshold_HoldsOutForTheBetterOffers()
    {
        // 20 offers, values 1..20. Spending only what regenerates means accepting roughly one
        // in four, so the threshold should sit high in that range rather than in the middle.
        var seen = Enumerable.Range(1, 20).Select(i => (double)i);

        var threshold = WonderPickEval.ReservationThreshold(seen, staminaNow: 2);

        Assert.True(threshold >= 12, $"threshold {threshold} should be in the upper range");
        Assert.True(threshold <= 20);
    }

    [Fact]
    public void AtTheCap_TheThresholdDrops()
    {
        // Stamina at the cap is not regenerating, so waiting has a strictly negative expected
        // cost. The correct response is to become LESS fussy — the exact inverse of the
        // pack-points cap warning.
        var seen = Enumerable.Range(1, 20).Select(i => (double)i).ToArray();

        var normal = WonderPickEval.ReservationThreshold(seen, staminaNow: 2);
        var capped = WonderPickEval.ReservationThreshold(seen, staminaNow: GameRules.StaminaCap);

        Assert.True(capped < normal, $"capped {capped} should be below normal {normal}");
    }

    [Fact]
    public void AnOfferBelowTheThresholdIsSkipped_EvenThoughItHasValue()
    {
        var offer = OfRarity("C", 5);
        var appraisal = Eval.Appraise(offer, WantsA1, new Collection(), threshold: 1e9);

        Assert.True(appraisal.ExpectedValue > 0);
        Assert.Equal(OfferVerdict.BelowThreshold, appraisal.Verdict);
    }

    [Fact]
    public void History_IsReadFromOffersSeen_IncludingOnesSkipped()
    {
        // A distribution built only from accepted offers would be biased upward by the very
        // policy it is meant to set.
        var offer = OfRarity("C", 5);
        var log = new List<WonderOfferEvent>
        {
            new(DateTimeOffset.Now, offer.Select(c => c.OwnershipKey).ToList(), 1, Taken: false, null),
            new(DateTimeOffset.Now, offer.Select(c => c.OwnershipKey).ToList(), 1, Taken: true, offer[0].OwnershipKey),
        };

        var values = Eval.HistoricalValuePerStamina(log, WantsA1, new Collection()).ToArray();

        Assert.Equal(2, values.Length);
        Assert.All(values, v => Assert.True(v > 0));
    }

    // ---- currency discipline ---------------------------------------------------------

    [Fact]
    public void ValueIsInPacks_AndCostStaysInStamina()
    {
        // The two are never folded into one number. ValuePerStamina exists only to compare
        // offers with each other; Wonder Stamina and Pack Hourglasses have no exchange rate.
        var appraisal = Eval.Appraise(
            [.. OfRarity("C", 4), .. OfRarity("AR", 1)], WantsA1, new Collection());

        Assert.Equal(3, appraisal.StaminaCost);
        Assert.Equal(appraisal.ExpectedValue / 3, appraisal.ValuePerStamina, 9);
    }

    [Fact]
    public void AnUnpriceableCard_IsWorthNothingRatherThanEverything()
    {
        // A promo has no packs-priced route. Treated as infinite it would make every offer
        // containing it look infinitely good, which is worse than undervaluing it.
        // Checked across every entry sharing the ownership key, not just this one: Deluxe packs
        // reprint promos, so a promo with a reprint IS priceable. That is the engine correctly
        // pooling sources across printings, and it caught the first version of this test.
        var index = Snapshot.Index();
        var promo = index.All.First(c =>
            GameRules.CanAppearInWonderPick(c.Rarity)
            && !index.PacksYielding(c.OwnershipKey).Any());

        var offer = new List<PocketCard> { promo };
        offer.AddRange(OfRarity("C", 4));

        var appraisal = Eval.Appraise(offer, new WishlistTarget(
            "promo", new Dictionary<string, int> { [promo.OwnershipKey] = 1 }), new Collection());

        Assert.Equal(0, appraisal.ExpectedValue);
        Assert.All(appraisal.Cards, c => Assert.True(double.IsFinite(c.Value)));
    }
}
