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

    /// <summary>
    /// Packs in the game's batch-open option. It costs exactly ten packs and adds no guarantee,
    /// so it can never beat ten singles on odds — what it costs you is the chance to choose
    /// again after each pull, which is why the app prices the batch rather than recommending it.
    /// </summary>
    public const int PacksPerBatch = 10;

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

    // ---- Flair --------------------------------------------------------------------

    /// <summary>
    /// Copies of a diamond-rarity card that earn its gold flair, granted automatically.
    ///
    /// Worth modelling even though it is purely cosmetic: it is the only reason to keep pulling
    /// a card you already own, so it is a real collecting goal — and unlike most flair it costs
    /// nothing and needs no tracking, because the copy count already says whether it is earned.
    /// </summary>
    public const int GoldFlairCopies = 10;

    /// <summary>
    /// The highest diamond rung that earns gold flair. Four-diamond cards do NOT, despite being
    /// diamonds — so the rule needs the number of symbols, not just the family.
    /// </summary>
    public const int GoldFlairMaxDiamonds = 3;

    /// <summary>
    /// Gold flair is earned by ten copies of a ONE- TO THREE-diamond card that is not a promo.
    ///
    /// Every part of that is load-bearing, and each was wrong in an earlier version: it is not
    /// "ten copies" (a tenth star earns nothing), not "ten copies of a diamond" (four-diamond
    /// cards are excluded), and not decided by rarity alone — promos carry ordinary rarity codes,
    /// 79 commons and 70 rares among them, so a rarity-only rule gilded 151 cards that can never
    /// earn it.
    /// </summary>
    /// <param name="rarityGroup">The rung's family: Diamond, Star, Shiny, Crown.</param>
    /// <param name="rarityCount">Symbols on the rung, e.g. 3 for a three-diamond card.</param>
    /// <param name="isPromo">Promo cards are excluded whatever their rarity says.</param>
    public static bool EarnsGoldFlair(string rarityGroup, int rarityCount, int copies, bool isPromo) =>
        copies >= GoldFlairCopies
        && !isPromo
        && rarityCount <= GoldFlairMaxDiamonds
        && rarityGroup.Equals("Diamond", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Cards the in-game wishlist holds. It is a public board other players browse when looking
    /// for a trade, so the twenty slots are advertising space, not a tracking list - which is why
    /// a hard cap on it is a real constraint worth planning around.
    /// </summary>
    public const int TradeBoardSlots = 20;

    /// <summary>Trades must be same-rarity, and these rarities cannot be traded at all.</summary>
    public static bool IsTradeable(string rarityCode) =>
        rarityCode is not ("IM" or "UR");

    /// <summary>
    /// Whether this CARD may be traded: its rarity, plus the promo rule that rarity cannot express.
    ///
    /// A card-level overload because the rarity-only one is not the whole rule, and taking it for
    /// the whole rule was a live bug: RouteCost priced a trade for promo cards - quoting a
    /// shinedust cost for something the game refuses outright - while the trade queue and the board
    /// advisor, which both remembered the promo check, refused them. Offering a route that does not
    /// exist is worse than offering none.
    /// </summary>
    public static bool CanBeTraded(PocketCard card) =>
        IsTradeable(card.Rarity) && (PromosTradeable || !card.IsPromo);

    /// <summary>Whether this card may be shared: 1-4 diamonds, and not a promo for now.</summary>
    public static bool CanBeShared(PocketCard card) =>
        IsShareable(card.Rarity) && (PromosShareable || !card.IsPromo);

    // ---- Shares ----
    //
    // A Share is a one-way gift: a friend sends you a card and receives NOTHING back. That single
    // fact makes it unlike every other route in the game, and it is why it cannot be folded into
    // the trade logic:
    //
    //   - No same-rarity payment. The whole reason a trade can be impossible - having nothing at
    //     that rung to offer - simply does not apply.
    //   - No shinedust and no Trade Stamina. It is free on both sides.
    //   - It is capped by a DAILY allowance on the receiving end, not by a regenerating pool, so
    //     the constraint is calendar days rather than a balance to spend down.
    //
    // Between two accounts of the same player it is therefore strictly better than a trade
    // wherever it applies: same cards delivered, no dust, no stamina, and no card given up. The
    // only price is the day.

    /// <summary>
    /// Cards a Share can carry: 1 to 4 diamonds and nothing above. Stars, shinies, Immersives and
    /// Crowns cannot be shared at all, so they remain a trade-or-pull problem.
    /// </summary>
    public static bool IsShareable(string rarityCode) =>
        rarityCode is "C" or "U" or "R" or "RR";

    /// <summary>
    /// Shares one account may RECEIVE per day. Sending is unlimited as far as we know, which is
    /// what makes the receiving cap the binding constraint - and it binds on each account
    /// separately, so two of your own accounts can each receive one on the same day.
    /// </summary>
    public const int SharesReceivedPerDay = 1;

    /// <summary>
    /// Whether a promo can be shared. FALSE, matching the trade rule, and for the same reason it
    /// is a switch rather than a scattered IsPromo check: it cannot be inferred from rarity, since
    /// promos carry ordinary C, U and R codes, and it is the kind of rule the developers change.
    ///
    /// Kept SEPARATE from <see cref="PromosTradeable"/> deliberately. They are two different rules
    /// about two different features, and tying them together would mean the day one changes, the
    /// app silently claims the other did too.
    /// </summary>
    public const bool PromosShareable = false;

    /// <summary>
    /// Whether promo cards can be traded. FALSE today, and expected to change: the developers
    /// have said promo trading is coming.
    ///
    /// A single switch rather than an <c>IsPromo</c> check spread through the engine and the UI,
    /// so the day it flips is one line. The change will not be cosmetic - a promo has no pack
    /// route at all, so the moment they become tradeable they are the most valuable thing a trade
    /// can get you, and every ranking that prices "what would this save me" will say so.
    ///
    /// Note it cannot be inferred from rarity: promos carry ordinary C, U and R codes.
    /// </summary>
    public const bool PromosTradeable = false;

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
