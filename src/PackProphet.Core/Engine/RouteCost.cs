namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;

public enum AcquisitionRoute { Pull, PackPoints, Trade, Share, WonderPick }

/// <summary>Why a route is unavailable for a card, so the UI can explain rather than omit.</summary>
public enum RouteBlock { None, NotSoldInPacks, NoPullRates, NotTradeable, NotShareable, NotInWonderPick, NoDustPrice }

/// <param name="PacksEquivalent">
/// Cost in packs. Only Pull and PackPoints are expressed this way — both genuinely ARE
/// packs. Trade and Wonder Pick are priced in their own currencies and must never be
/// converted, since the three resource systems do not exchange.
/// </param>
public sealed record RouteOption(
    AcquisitionRoute Route,
    bool Available,
    RouteBlock Block = RouteBlock.None,
    double? PacksEquivalent = null,
    int? Dust = null,
    int? Stamina = null,
    int? Points = null,
    string? Note = null);

/// <param name="Cheapest">
/// The best route measured in packs-equivalent, or null when no packs-priced route exists.
/// Trade and Wonder Pick are deliberately excluded from this comparison.
/// </param>
public sealed record CardRoutes(
    PocketCard Card,
    IReadOnlyList<RouteOption> Options,
    RouteOption? Cheapest);

/// <summary>
/// Prices every way to obtain a card, and names the ones that do not exist.
///
/// Which routes are even possible varies by rarity in a non-obvious way — 3-star and Crown
/// are neither tradeable nor available from Wonder Pick, so for them packs and pack points
/// are the ONLY options. Offering a trade for a Crown is worse than offering nothing, so the
/// routing rules are enforced here rather than left to each caller.
/// </summary>
public sealed class RouteCost
{
    private readonly CardIndex _index;
    private readonly PackOdds _odds;
    private readonly IReadOnlyDictionary<string, Rarity> _rarities;

    /// <summary>
    /// Sets with a point economy, materialised once. OpenableSets walks every pack key and
    /// splits each one, and For() runs per card per row of several tables — recomputing it
    /// there would put a full scan of the pack list inside the render loop.
    /// </summary>
    private readonly HashSet<string> _setsWithPoints;

    public RouteCost(CardIndex index, PackOdds odds, IReadOnlyDictionary<string, Rarity> rarities)
    {
        _index = index;
        _odds = odds;
        _rarities = rarities;
        _setsWithPoints = index.OpenableSets.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public CardRoutes For(PocketCard card, Collection owned)
    {
        var options = new List<RouteOption>
        {
            Pull(card),
            Points(card),
            Trade(card, owned),
            Share(card),
            Wonder(card)
        };

        // Only packs-priced routes are comparable. Trade and Wonder Pick are separate
        // currencies with no exchange rate, so a "cheapest" across all four would be fiction.
        var cheapest = options
            .Where(o => o is { Available: true, PacksEquivalent: not null })
            .OrderBy(o => o.PacksEquivalent)
            .FirstOrDefault();

        return new CardRoutes(card, options, cheapest);
    }

    private RouteOption Pull(PocketCard card)
    {
        if (!card.IsPackObtainable)
            return new(AcquisitionRoute.Pull, false, RouteBlock.NotSoldInPacks,
                Note: "Not sold in packs — promos come from events.");

        var rate = _odds.BestRatesByCard().GetValueOrDefault(card.Key);
        if (rate <= 0)
            return new(AcquisitionRoute.Pull, false, RouteBlock.NoPullRates,
                Note: "No published pull rates for this set yet, so packs cannot be priced.");

        return new(AcquisitionRoute.Pull, true, PacksEquivalent: 1.0 / rate,
            Note: "Expected packs of the best pack for this card.");
    }

    private RouteOption Points(PocketCard card)
    {
        if (!_rarities.TryGetValue(card.Rarity, out var rarity) || rarity.Points <= 0)
            return new(AcquisitionRoute.PackPoints, false, RouteBlock.None,
                Note: "No pack-point price.");

        // Points are earned by opening THIS SET's packs and can be spent nowhere else, so a set
        // with no openable packs has no point economy at all. The card carries a point price —
        // every rarity does — but quoting it for a promo offers a route that cannot exist: there
        // is no pack to earn the points in and no shop to spend them at.
        if (!_setsWithPoints.Contains(card.Set))
            return new(AcquisitionRoute.PackPoints, false, RouteBlock.NotSoldInPacks,
                Note: $"Points are earned and spent within one set, and {card.Set} has no " +
                      "packs to earn them in.");

        // Points accrue per set and are spendable only within that set, so the packs you
        // must open are packs OF THIS CARD'S SET — they cannot be earned elsewhere.
        var packs = (double)rarity.Points / GameRules.PackPointsPerPack;
        return new(AcquisitionRoute.PackPoints, true,
            PacksEquivalent: packs,
            Points: rarity.Points,
            Note: $"{rarity.Points} points \u2014 {packs:N0} packs of {card.Set} at " +
                  $"{GameRules.PackPointsPerPack}/pack. Points accrue while you open, so this " +
                  "is what you give up from a shared per-set budget, not extra packs.");
    }

    private RouteOption Trade(PocketCard card, Collection owned)
    {
        if (!GameRules.CanBeTraded(card))
            return new(AcquisitionRoute.Trade, false, RouteBlock.NotTradeable,
                Note: card.IsPromo
                    ? "Promos cannot be traded."
                    : $"{card.Rarity} cannot be traded at all.");

        if (!_rarities.TryGetValue(card.Rarity, out var rarity) || rarity.TradePrice is null)
            return new(AcquisitionRoute.Trade, false, RouteBlock.NoDustPrice);

        // Trading needs a same-rarity card to offer as well as the dust, so report the gate
        // that is actually blocking rather than collapsing it to one number.
        var spares = _index.All
            .Where(c => c.Rarity == card.Rarity && c.OwnershipKey != card.OwnershipKey)
            .DistinctBy(c => c.OwnershipKey)
            .Sum(c => Math.Max(0, owned.Of(c) - 1));

        var note = spares > 0
            ? $"{spares} spare {card.Rarity} to offer."
            : $"No spare {card.Rarity} to offer — trades must be same-rarity.";

        return new(AcquisitionRoute.Trade, spares > 0,
            Dust: rarity.TradePrice,
            Stamina: GameRules.TradeStaminaPerTrade,
            Note: note);
    }

    /// <summary>
    /// A friend sends the card and gets nothing back. Free, and gated only by a daily allowance
    /// on your side - so where it applies it beats trading outright, and the app should stop
    /// quoting dust for a card someone could simply hand over.
    ///
    /// Not packs-priced, and deliberately so: like a trade it needs another person, and unlike a
    /// pull there is no rate that says how likely that is. What it costs is a day of goodwill,
    /// which is not a currency this app can total up.
    /// </summary>
    private static RouteOption Share(PocketCard card)
    {
        if (!GameRules.CanBeShared(card))
            return new(AcquisitionRoute.Share, false, RouteBlock.NotShareable,
                Note: card.IsPromo
                    ? "Promos cannot be shared."
                    : $"{card.Rarity} cannot be shared \u2014 Shares carry 1 to 4 diamonds only.");

        return new(AcquisitionRoute.Share, true,
            Note: "A friend can simply send it \u2014 no card back, no dust, no stamina. You can " +
                  $"receive {GameRules.SharesReceivedPerDay} a day, so the cost is the day.");
    }

    private RouteOption Wonder(PocketCard card)
    {
        if (!GameRules.CanAppearInWonderPick(card.Rarity))
            return new(AcquisitionRoute.WonderPick, false, RouteBlock.NotInWonderPick,
                Note: $"{card.Rarity} never appears in Wonder Pick.");

        return new(AcquisitionRoute.WonderPick, true,
            Stamina: GameRules.WonderPickCost(card.Rarity),
            Note: "Only if it turns up in someone's offer, and then it is 1-in-5.");
    }

    /// <param name="Value">
    /// Packs saved per pack's worth of points spent. Above 1 the shop beats chasing; below 1
    /// you are better off opening. Use this to rank, never the raw point price.
    /// </param>
    public sealed record PointsValue(PocketCard Card, double PullPacks, double PointsPacks, double Value);

    /// <summary>
    /// Outstanding cards ranked by how well the point shop serves them.
    ///
    /// Pack points are a BYPRODUCT of opening, not an alternative to it: you accrue five per
    /// pack whatever you do. So the real question is never "buy or pull?" but "which cards
    /// deserve my limited per-set point budget?" — which makes the ratio of pull cost to
    /// point cost the only meaningful ranking.
    ///
    /// The answer is not uniform, and not intuitive. Measured across A1 and A3, Crown rares
    /// return ~2.5 packs saved per pack of points, while Double Rares return ~0.6 and
    /// Immersives just ~0.3 — an Immersive pulls in about 90 packs because a set holds only
    /// one or two of them, yet costs 300 packs' worth of points. Ranking by point price alone
    /// would recommend precisely the worst purchases.
    /// </summary>
    public IReadOnlyList<PointsValue> PointsShopRanking(
        IEnumerable<Demand> outstanding, Collection owned, bool onlyWorthwhile = true)
    {
        var result = new List<PointsValue>();
        foreach (var demand in outstanding)
        {
            var card = demand.SuppliedBy[0];
            var routes = For(card, owned);

            var pull = routes.Options.First(o => o.Route == AcquisitionRoute.Pull);
            var points = routes.Options.First(o => o.Route == AcquisitionRoute.PackPoints);

            if (pull is not { Available: true, PacksEquivalent: { } p } ||
                points is not { Available: true, PacksEquivalent: { } q } || q <= 0) continue;

            var value = p / q;
            if (onlyWorthwhile && value <= 1.0) continue;
            result.Add(new PointsValue(card, p, q, value));
        }
        return result.OrderByDescending(r => r.Value).ToArray();
    }
}
