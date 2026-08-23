namespace PackProphet.Domain;

/// <summary>
/// Every Pokémon TCG Pocket game constant, in one place.
///
/// These are balance decisions the developers can change at any time. A wrong value
/// here does not throw — it silently corrupts every recommendation the app makes. So
/// they live together, sourced and commented, and a patch is a one-file update rather
/// than a hunt through the engine.
///
/// All values below were confirmed against the live game.
/// </summary>
public static class GameRules
{
    // ---- Packs -------------------------------------------------------------------

    /// <summary>Pack Points earned per pack opened. Promo packs award none.</summary>
    public const int PackPointsPerPack = 5;

    /// <summary>
    /// Pack Points are capped, and packs opened while capped earn NOTHING. That makes
    /// "capped" an actionable warning state, not just a number: spend before opening.
    /// </summary>
    public const int PackPointsCap = 2500;

    /// <summary>Free packs per day on the base account, on a 12-hour timer.</summary>
    public const int FreePacksPerDay = 2;

    /// <summary>
    /// Premium membership adds one pack every 24h: 2/day becomes 3/day, a +50% rate.
    /// Every timeline estimate must respect the user's premium flag — hardcoding the
    /// free-tier rate makes every projection wrong for paying players.
    /// </summary>
    public const int PremiumExtraPacksPerDay = 1;

    public static int PacksPerDay(bool premium) =>
        FreePacksPerDay + (premium ? PremiumExtraPacksPerDay : 0);

    // ---- The three resource pools -------------------------------------------------
    //
    // Pack, Wonder, and Trade each have their own hourglass and their own pool, and
    // NOTHING exchanges between them. The cardinal rule downstream: never express one
    // system's cost in another system's units.
    //
    // Every hourglass is worth exactly one hour, without exception — so that ratio is
    // a single shared constant rather than a per-pool parameter.

    /// <summary>One hourglass of any kind = one hour of waiting removed.</summary>
    public const int MinutesPerHourglass = 60;

    /// <summary>12 Pack Hourglasses (12 hours) bring one pack forward.</summary>
    public const int PackHourglassesPerPack = 12;

    /// <summary>12 Wonder Hourglasses restore one Wonder Stamina.</summary>
    public const int WonderHourglassesPerStamina = 12;

    /// <summary>12 Trade Hourglasses restore one Trade Stamina.</summary>
    public const int TradeHourglassesPerStamina = 12;

    /// <summary>
    /// Wonder and Trade pools are structurally identical: cap 5, one restored every
    /// 12h. Only what they cost to spend differs.
    /// </summary>
    public const int StaminaCap = 5;

    public static readonly TimeSpan StaminaRegen = TimeSpan.FromHours(12);

    /// <summary>
    /// Filling an empty pool takes 5 × 12h = 60h. Worth stating because it makes the
    /// at-cap warning humane: there is ~2.5 days of slack before a full pool starts
    /// wasting regen, so the app can say "no rush until Thursday" instead of nagging.
    /// </summary>
    public static readonly TimeSpan StaminaFullRefill = StaminaRegen * StaminaCap;

    // ---- Wonder Pick --------------------------------------------------------------

    /// <summary>You see all 5 cards, then receive exactly one, uniformly at random.</summary>
    public const int WonderPickCardsShown = 5;

    public const double WonderPickCardChance = 1.0 / WonderPickCardsShown;

    /// <summary>
    /// Stamina cost is set by the HIGHEST rarity in the offer — not by what the offer is
    /// worth to you. That asymmetry is the whole basis of the overpriced-offer flag: an
    /// offer costs 4 because it contains a 2★, even if you already own that 2★.
    /// </summary>
    public static int WonderPickCost(string rarityCode) => rarityCode switch
    {
        "C" or "U" or "R" => 1,   // 1-3 diamond
        "RR" => 2,                // 4 diamond
        "AR" => 3,                // 1 star
        "SR" or "SAR" => 4,       // 2 star — the ceiling
        _ => throw new ArgumentOutOfRangeException(nameof(rarityCode),
                $"'{rarityCode}' cannot appear in a Wonder Pick; check CanAppearInWonderPick first.")
    };

    /// <summary>
    /// 3★, Crown and Shiny never appear in Wonder Pick. This is why the cost ceiling is
    /// 4, and it means Wonder Pick cannot help with the top tiers at all.
    /// </summary>
    public static bool CanAppearInWonderPick(string rarityCode) =>
        rarityCode is "C" or "U" or "R" or "RR" or "AR" or "SR" or "SAR";

    /// <summary>Offers rotate roughly every 3 hours, which makes accepting a reservation-price problem.</summary>
    public static readonly TimeSpan WonderOfferLifetime = TimeSpan.FromHours(3);

    // ---- Trading ------------------------------------------------------------------

    /// <summary>
    /// Flat 1 per trade regardless of rarity, so the stamina gate is simply "have ≥1?".
    /// All rarity-dependence lives in the shinedust cost, which is read from
    /// rarities.json (tradePrice) rather than hardcoded here.
    ///
    /// At 1 per 12h this caps trading at ~2/day — the same order as free packs, and far
    /// scarcer than dust, which merely accumulates. Stamina is the bottleneck, not dust.
    /// </summary>
    public const int TradeStaminaPerTrade = 1;

    /// <summary>Trades must be same-rarity, and these rarities cannot be traded at all.</summary>
    public static bool IsTradeable(string rarityCode) =>
        rarityCode is not ("IM" or "UR");

    // ---- Pack availability ----

    /// <summary>
    /// A pack sold only for a limited window, returning occasionally rather than staying on
    /// the shelf. Deluxe packs work this way, which changes the advice fundamentally: it is
    /// pointless to recommend a pack that cannot be bought today, and Deluxe is otherwise the
    /// standout pick for a 4-diamond target because its last slot is guaranteed RR.
    ///
    /// Detected by name because no dataset records availability — it is a live, time-varying
    /// fact. So this only marks a pack as NEEDING confirmation; whether it is actually on sale
    /// right now is the user's to tell us, and their answer is authoritative.
    /// </summary>
    public static bool IsLimitedTimePack(string packName) =>
        packName.Contains("Deluxe", StringComparison.OrdinalIgnoreCase);

    // ---- Deck legality ------------------------------------------------------------

    public const int DeckSize = 20;

    /// <summary>At most 2 copies per card NAME — not per printing.</summary>
    public const int MaxCopiesPerName = 2;

    /// <summary>A deck with no Basic Pokémon cannot be played at all.</summary>
    public const int MinBasicPokemon = 1;

    /// <summary>Up to 3 energy types. Energy cards are not part of the deck in Pocket.</summary>
    public const int MaxEnergyTypes = 3;
}
