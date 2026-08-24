namespace PackProphet.Domain;

/// <summary>
/// Every Pokémon TCG Pocket game constant, in one place.
///
/// These are balance values the developers can change at any time, and a wrong value here
/// does not throw — it changes every recommendation the app makes. Keeping them together
/// makes a patch a one-file update.
///
/// All values below were confirmed against the live game.
/// </summary>
public static class GameRules
{
    // ---- Packs -------------------------------------------------------------------

    /// <summary>Pack Points earned per pack opened. Promo packs award none.</summary>
    public const int PackPointsPerPack = 5;

    /// <summary>
    /// Pack Points are capped, and packs opened while capped earn nothing. The UI treats
    /// "capped" as a warning state: spend before opening.
    /// </summary>
    public const int PackPointsCap = 2500;

    /// <summary>Free packs per day on the base account, on a 12-hour timer.</summary>
    public const int FreePacksPerDay = 2;

    /// <summary>
    /// Premium membership adds one pack every 24h: 2/day becomes 3/day. Timeline estimates
    /// read the user's premium flag rather than assuming the free-tier rate.
    /// </summary>
    public const int PremiumExtraPacksPerDay = 1;

    public static int PacksPerDay(bool premium) =>
        FreePacksPerDay + (premium ? PremiumExtraPacksPerDay : 0);

    /// <summary>
    /// Packs in the game's batch-open option. It costs exactly ten packs and adds no guarantee,
    /// so its odds equal ten singles; what it gives up is the chance to choose again after each
    /// pull. The app prices the batch rather than recommending it.
    /// </summary>
    public const int PacksPerBatch = 10;

    // ---- The three resource pools -------------------------------------------------
    //
    // Pack, Wonder, and Trade each have their own hourglass and their own pool, and nothing
    // exchanges between them. Downstream code never expresses one system's cost in another
    // system's units.
    //
    // Every hourglass is worth exactly one hour, so that ratio is a single shared constant
    // rather than a per-pool parameter.

    /// <summary>One hourglass of any kind = one hour of waiting removed.</summary>
    public const int MinutesPerHourglass = 60;

    /// <summary>12 Pack Hourglasses (12 hours) bring one pack forward.</summary>
    public const int PackHourglassesPerPack = 12;

    /// <summary>12 Wonder Hourglasses restore one Wonder Stamina.</summary>
    public const int WonderHourglassesPerStamina = 12;

    /// <summary>12 Trade Hourglasses restore one Trade Stamina.</summary>
    public const int TradeHourglassesPerStamina = 12;

    /// <summary>
    /// Wonder and Trade pools are structurally identical: cap 5, one restored every 12h. Only
    /// what they cost to spend differs.
    /// </summary>
    public const int StaminaCap = 5;

    public static readonly TimeSpan StaminaRegen = TimeSpan.FromHours(12);

    /// <summary>
    /// Filling an empty pool takes 5 × 12h = 60h. Used to report how much slack a partly full
    /// pool has before it reaches the cap and starts wasting regeneration.
    /// </summary>
    public static readonly TimeSpan StaminaFullRefill = StaminaRegen * StaminaCap;

    // ---- Flair --------------------------------------------------------------------

    /// <summary>
    /// Copies of a diamond-rarity card that earn its gold flair, granted automatically.
    ///
    /// Modelled because it is the only reason to keep pulling a card you already own. It needs
    /// no separate tracking: the copy count already says whether it is earned.
    /// </summary>
    public const int GoldFlairCopies = 10;

    /// <summary>
    /// The highest diamond rung that earns gold flair. Four-diamond cards do not, so the rule
    /// needs the number of symbols rather than just the family.
    /// </summary>
    public const int GoldFlairMaxDiamonds = 3;

    /// <summary>
    /// Gold flair is earned by ten copies of a one- to three-diamond card that is not a promo.
    ///
    /// Each condition matters: a tenth star earns nothing, four-diamond cards are excluded, and
    /// rarity alone does not decide it — promos carry ordinary rarity codes, 79 commons and 70
    /// rares among them, none of which can earn flair.
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
    /// Stamina cost is set by the highest rarity in the offer, not by what the offer is worth
    /// to you: an offer costs 4 because it contains a 2★, even if you already own that 2★.
    /// The overpriced-offer flag is derived from this.
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
    /// 3★, Crown and Shiny never appear in Wonder Pick, which is why the cost ceiling is 4.
    /// Wonder Pick is not a route to the top tiers.
    /// </summary>
    public static bool CanAppearInWonderPick(string rarityCode) =>
        rarityCode is "C" or "U" or "R" or "RR" or "AR" or "SR" or "SAR";

    /// <summary>Offers rotate roughly every 3 hours.</summary>
    public static readonly TimeSpan WonderOfferLifetime = TimeSpan.FromHours(3);

    // ---- Trading ------------------------------------------------------------------

    /// <summary>
    /// Flat 1 per trade regardless of rarity, so the stamina gate is "have ≥1?". Rarity
    /// dependence lives in the shinedust cost, read from rarities.json (tradePrice).
    ///
    /// At 1 per 12h this caps trading at ~2/day, far scarcer than dust, which accumulates.
    /// Rankings therefore treat stamina as the bottleneck.
    /// </summary>
    public const int TradeStaminaPerTrade = 1;

    /// <summary>
    /// Cards the in-game wishlist holds. It is a public board other players browse when looking
    /// for a trade, so the twenty slots are advertising space rather than a tracking list.
    /// </summary>
    public const int TradeBoardSlots = 20;

    /// <summary>Trades must be same-rarity, and these rarities cannot be traded at all.</summary>
    public static bool IsTradeable(string rarityCode) =>
        rarityCode is not ("IM" or "UR");

    /// <summary>
    /// Whether this card may be traded: its rarity, plus the promo rule that rarity cannot
    /// express. Every surface that offers a trade route uses this overload rather than
    /// <see cref="IsTradeable"/>, so promos are refused consistently.
    /// </summary>
    public static bool CanBeTraded(PocketCard card) =>
        IsTradeable(card.Rarity) && (PromosTradeable || !card.IsPromo);

    /// <summary>Whether this card may be shared: 1-4 diamonds, and not a promo for now.</summary>
    public static bool CanBeShared(PocketCard card) =>
        IsShareable(card.Rarity) && (PromosShareable || !card.IsPromo);

    // ---- Shares ----
    //
    // A Share is a one-way gift: a friend sends you a card and receives nothing back. It is
    // modelled separately from trading because none of the trade constraints apply:
    //
    //   - No same-rarity payment, so having nothing at that rung to offer is not a blocker.
    //   - No shinedust and no Trade Stamina. It is free on both sides.
    //   - Capped by a daily allowance on the receiving end rather than by a regenerating pool,
    //     so the constraint is calendar days rather than a balance to spend down.
    //
    // Between two accounts of the same player a share delivers the same card as a trade with no
    // dust, no stamina and no card given up, so shareable rarities are routed to a share.

    /// <summary>
    /// Cards a Share can carry: 1 to 4 diamonds. Stars, shinies, Immersives and Crowns cannot
    /// be shared, and remain a trade-or-pull problem.
    /// </summary>
    public static bool IsShareable(string rarityCode) =>
        rarityCode is "C" or "U" or "R" or "RR";

    /// <summary>
    /// Shares one account may receive per day. Sending appears to be unlimited, so the
    /// receiving cap is the binding constraint, and it binds on each account separately: two of
    /// your own accounts can each receive one on the same day.
    /// </summary>
    public const int SharesReceivedPerDay = 1;

    /// <summary>
    /// Whether a promo can be shared. False, matching the trade rule. It is a switch rather
    /// than scattered IsPromo checks because it cannot be inferred from rarity — promos carry
    /// ordinary C, U and R codes.
    ///
    /// Kept separate from <see cref="PromosTradeable"/>: they are two rules about two features
    /// and can change independently.
    /// </summary>
    public const bool PromosShareable = false;

    /// <summary>
    /// Whether promo cards can be traded. False today; the developers have said promo trading
    /// is coming.
    ///
    /// A single switch rather than an <c>IsPromo</c> check spread through the engine and the UI,
    /// so the day it flips is one line. A promo has no pack route, so when they become tradeable
    /// every ranking that prices "what would this save me" changes.
    ///
    /// It cannot be inferred from rarity: promos carry ordinary C, U and R codes.
    /// </summary>
    public const bool PromosTradeable = false;

    // ---- Pack availability ----

    /// <summary>
    /// A pack sold only for a limited window, returning occasionally rather than staying on the
    /// shelf. Deluxe packs work this way, and Deluxe is otherwise the strongest pick for a
    /// 4-diamond target because its last slot is guaranteed RR.
    ///
    /// Detected by name, since no dataset records availability. This only marks a pack as
    /// needing confirmation; whether it is on sale right now is the user's answer to give.
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
