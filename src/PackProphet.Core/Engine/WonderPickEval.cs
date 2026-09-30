namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.State;

/// <summary>Why an offer is worth taking, or not, in the words the UI shows.</summary>
public enum OfferVerdict
{
    /// <summary>Nothing in it you want. Skipping costs you nothing at all.</summary>
    NothingWanted,

    /// <summary>
    /// Priced by a rarity you already own, so you are paying the offer's maximum for cards that
    /// are not what makes it expensive.
    /// </summary>
    Overpriced,

    /// <summary>Below your own reservation threshold: better offers come along often enough.</summary>
    BelowThreshold,

    /// <summary>Worth the stamina.</summary>
    Take
}

/// <param name="Value">
/// Packs-equivalent this card is worth to you: zero if you already have enough of it, else the
/// cheapest packs-priced route to it. Wonder Pick's own cost is not part of this — stamina and
/// packs do not exchange.
/// </param>
public sealed record OfferCard(PocketCard Card, double Value, int Owned, int Wanted);

/// <param name="ExpectedValue">Packs-equivalent, averaged over the 1-in-5 chance of each card.</param>
/// <param name="StaminaCost">Set by the offer's highest rarity, not by what it is worth to you.</param>
/// <param name="ValuePerStamina">
/// Used only to compare offers with each other. It is not a price: Wonder Stamina and Pack
/// Hourglasses are separate currencies with no exchange rate.
/// </param>
/// <param name="Threshold">The value-per-stamina this offer had to beat, and why.</param>
public sealed record OfferAppraisal(
    IReadOnlyList<OfferCard> Cards,
    double ExpectedValue,
    int StaminaCost,
    double ValuePerStamina,
    OfferVerdict Verdict,
    double Threshold,
    string CostDriver,
    bool CostDriverWanted)
{
    /// <summary>
    /// A Deluxe offer: four cards and <see cref="GameRules.DeluxeWonderPickHourglasses"/> Pack
    /// Hourglasses in the fifth slot.
    /// </summary>
    public bool HourglassSlot { get; init; }

    /// <summary>
    /// The hourglass slot's share of <see cref="ExpectedValue"/>, in packs. Zero for an ordinary
    /// offer.
    /// </summary>
    public double HourglassValue { get; init; }
}

/// <summary>
/// Take it or skip it?
///
/// A reservation-price problem rather than a valuation one. You see all five cards and then
/// receive one at random, so the only decision is whether to spend on this offer at all — and
/// because stamina caps at 5 and regenerates one per 12 hours, spending it on a mediocre offer
/// costs the good offer you will have to skip later.
///
/// Two rules this class enforces:
///
///   - Value is reported in packs-equivalent and cost in stamina, and the two are never divided
///     into a single "price". They are separate currencies with no exchange rate.
///   - Cost is set by the offer's highest rarity, which is independent of what the offer is worth
///     to you: an offer priced at 4 stamina because of a 2-star you already own is expensive for
///     reasons that do not benefit you.
/// </summary>
public sealed class WonderPickEval
{
    private readonly CardIndex _index;
    private readonly RouteCost _routes;

    public WonderPickEval(CardIndex index, RouteCost routes)
    {
        _index = index;
        _routes = routes;
    }

    /// <summary>
    /// Stamina this offer costs: the highest rarity present decides, and every rarity that can
    /// appear has a cost, so an offer of five commons costs 1 and one containing a single 2-star
    /// costs 4 no matter what the other four are.
    /// </summary>
    public int StaminaCost(IEnumerable<PocketCard> offer)
    {
        var costs = offer
            .Where(c => GameRules.CanAppearInWonderPick(c.Rarity))
            .Select(c => GameRules.WonderPickCost(c.Rarity))
            .ToArray();

        // An offer of nothing but ineligible cards cannot happen in the game. Charging the
        // minimum rather than throwing keeps a mis-tap from taking the screen down.
        return costs.Length == 0 ? 1 : costs.Max();
    }

    /// <summary>
    /// Appraise one offer against a target and a collection.
    /// </summary>
    /// <param name="threshold">
    /// Value-per-stamina this offer must beat. Zero accepts anything with value in it, which is
    /// the right default before there is any history to learn from.
    /// </param>
    /// <param name="hourglassSlot">
    /// Whether the offer is a Deluxe one. Worked out from the cards' printings when not given;
    /// a logged offer says so itself, because it stores ownership keys and a reprint's key
    /// resolves to its original printing.
    /// </param>
    public OfferAppraisal Appraise(
        IReadOnlyList<PocketCard> offer,
        ICompletionTarget target,
        Collection owned,
        double threshold = 0,
        bool? hourglassSlot = null)
    {
        // Demand keyed by ownership key, so "do I want this card" is answered by the same target
        // that drives the pack ranking rather than by a second, subtly different rule.
        var wanted = target.Outstanding(_index, owned)
            .SelectMany(d => d.SuppliedBy.Select(c => (c.OwnershipKey, d.Remaining)))
            .GroupBy(x => x.OwnershipKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Remaining), StringComparer.Ordinal);

        var cards = offer.Select(card =>
        {
            var remaining = wanted.GetValueOrDefault(card.OwnershipKey);
            var value = remaining <= 0 ? 0 : PacksSaved(card, owned);
            return new OfferCard(card, value, owned.Of(card), remaining);
        }).ToArray();

        var cost = StaminaCost(offer);

        // The average over a uniform 1-in-5, not the best card in the offer. A Deluxe offer's
        // fifth slot is Pack Hourglasses, and those are packs: twelve bring one forward.
        var deluxe = hourglassSlot ?? HasHourglassSlot(offer);
        var cardsEv = cards.Sum(c => c.Value) * GameRules.WonderPickCardChance;
        var hourglassEv = deluxe
            ? GameRules.WonderPickCardChance * GameRules.DeluxeWonderPickHourglasses / GameRules.PackHourglassesPerPack
            : 0;
        var ev = cardsEv + hourglassEv;
        var perStamina = cost > 0 ? ev / cost : ev;

        // The card that set the price, and whether it is one you actually want.
        var driver = offer
            .Where(c => GameRules.CanAppearInWonderPick(c.Rarity))
            .OrderByDescending(c => GameRules.WonderPickCost(c.Rarity))
            .FirstOrDefault();

        var driverWanted = driver is not null && wanted.GetValueOrDefault(driver.OwnershipKey) > 0;

        // Nothing wanted is judged on the cards alone. The hourglasses are in every Deluxe offer,
        // and a fifth of two hourglasses does not make stamina worth spending on four cards you
        // already have.
        var verdict =
            cardsEv <= 0 ? OfferVerdict.NothingWanted
            // Only at a premium price: at 1 stamina there is no premium to be paying.
            : cost >= OverpricedFrom && !driverWanted ? OfferVerdict.Overpriced
            : perStamina < threshold ? OfferVerdict.BelowThreshold
            : OfferVerdict.Take;

        return new OfferAppraisal(
            cards, ev, cost, perStamina, verdict, threshold,
            driver?.Name ?? "", driverWanted)
        {
            HourglassSlot = deluxe,
            HourglassValue = hourglassEv,
        };
    }

    /// <summary>
    /// Whether an offer came from a Deluxe pack, by the printings named: every card is a Deluxe
    /// printing. A reprint named by its original printing reads as ordinary, which is right,
    /// because that is the pack it was named from.
    /// </summary>
    public static bool IsDeluxeOffer(IReadOnlyCollection<PocketCard> offer) =>
        offer.Count > 0 && offer.All(IsDeluxePrinting);

    /// <summary>
    /// Whether an offer's missing card is Pack Hourglasses: a Deluxe offer short of five cards.
    /// Five cards from a Deluxe pack is the event offer, which has no hourglasses.
    /// </summary>
    public static bool HasHourglassSlot(IReadOnlyCollection<PocketCard> offer) =>
        offer.Count < GameRules.WonderPickCardsShown && IsDeluxeOffer(offer);

    public static bool IsDeluxePrinting(PocketCard card) =>
        card.Packs is { Length: > 0 } packs && packs.Any(GameRules.IsDeluxePack);

    /// <summary>
    /// Whether a logged offer was a Deluxe one. Offers logged before the flag existed are read
    /// by their shape: four cards, every one of them sold in a Deluxe pack.
    /// </summary>
    public bool WasDeluxe(WonderOfferEvent offer) =>
        offer.Deluxe ?? (offer.OwnershipKeys.Count == GameRules.DeluxePackCards
                         && offer.OwnershipKeys.All(k => _index.ByOwnershipKey.TryGetValue(k, out var e)
                                                         && e.Any(IsDeluxePrinting)));

    /// <summary>
    /// The stamina cost at which "you are paying for a rarity that does you no good" starts to
    /// be worth saying. Three of five, so it covers 1-star and 2-star offers — the ones where a
    /// single card can triple or quadruple the price of the other four.
    /// </summary>
    public const int OverpricedFrom = 3;

    /// <summary>
    /// What obtaining this card would otherwise cost, in packs. Uses the same cheapest-route
    /// pricing as everywhere else, so a card that is cheap to pull is correctly worth little
    /// here even if it is rare.
    /// </summary>
    private double PacksSaved(PocketCard card, Collection owned)
    {
        var routes = _routes.For(card, owned);
        var packs = routes.Cheapest?.PacksEquivalent;

        // No packs-priced route at all — a promo, or a set with no rates. Worth nothing here
        // rather than infinity: this is a comparison between offers, and one unpriceable card
        // would make every offer containing it look infinitely good.
        return packs is { } p && double.IsFinite(p) ? p : 0;
    }

    /// <summary>
    /// The value-per-stamina to hold out for, learned from the offers this user has actually
    /// seen.
    ///
    /// The policy: accept often enough to spend what regenerates, and no more. At one stamina
    /// per 12 hours and offers rotating every ~3 hours, roughly one offer in eight is
    /// affordable in the long run — so the threshold is the percentile of past offers that
    /// leaves that acceptance rate. Below <paramref name="minimumSamples"/> offers there is no
    /// distribution worth trusting, and the threshold is zero: take anything useful while
    /// learning.
    /// </summary>
    public static double ReservationThreshold(
        IEnumerable<double> pastValuePerStamina,
        int staminaNow,
        int minimumSamples = 12)
    {
        var seen = pastValuePerStamina.Where(double.IsFinite).OrderBy(v => v).ToArray();
        if (seen.Length < minimumSamples) return 0;

        // Offers per stamina earned: 12h of regen divided by a ~3h rotation.
        var offersPerStamina = GameRules.StaminaRegen / GameRules.WonderOfferLifetime;
        var acceptRate = Math.Clamp(1.0 / offersPerStamina, 0.01, 1.0);

        // At the cap, stamina has stopped regenerating, so holding out has a negative expected
        // cost and the threshold drops rather than rises.
        if (staminaNow >= GameRules.StaminaCap) acceptRate = Math.Min(1.0, acceptRate * 2);

        var index = (int)Math.Floor((1 - acceptRate) * (seen.Length - 1));
        return seen[Math.Clamp(index, 0, seen.Length - 1)];
    }

    /// <summary>
    /// Value-per-stamina of every offer in the log, for the threshold above. Read from what the
    /// user saw rather than what they took: a distribution of accepted offers only would be
    /// biased upward by the policy it sets.
    /// </summary>
    public IEnumerable<double> HistoricalValuePerStamina(
        IEnumerable<WonderOfferEvent> log, ICompletionTarget target, Collection owned)
    {
        foreach (var offer in log)
        {
            var cards = offer.OwnershipKeys
                .Select(k => _index.ByOwnershipKey.TryGetValue(k, out var e) ? e[0] : null)
                .Where(c => c is not null)
                .Select(c => c!)
                .ToArray();

            if (cards.Length == 0) continue;

            yield return Appraise(cards, target, owned, hourglassSlot: WasDeluxe(offer)).ValuePerStamina;
        }
    }
}
