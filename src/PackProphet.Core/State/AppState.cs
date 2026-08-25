namespace PackProphet.State;

using PackProphet.Deck;
using PackProphet.Engine;

/// <summary>A saved deck. Cards are identities, one entry per copy, as a deck code lists them.</summary>
/// <param name="FaceNr">
/// Card identity the user picked to represent the deck. Null means unset, and the deck falls
/// back to its rarest Pokémon — NOT its rarest card, because a Crown-rarity Tool goes into many
/// unrelated decks while a deck is almost always built around one Pokémon.
/// </param>
public sealed record SavedDeck(
    string Id,
    string Name,
    List<int> DeckBuilderNrs,
    List<EnergyType> Energies,
    string Source = "manual",
    int Priority = 0,
    int? FaceNr = null);

/// <summary>An arbitrary set of wanted cards, keyed by ownership key.</summary>
public sealed record Wishlist(string Id, string Name, Dictionary<string, int> Wanted);

/// <summary>One logged pack opening. The basis of the points ledger and the history view.</summary>
public sealed record PackOpenEvent(
    DateTimeOffset At,
    string Set,
    string Pack,
    string Variant,
    List<string> OwnershipKeys);

/// <summary>
/// One Wonder Pick offer that was evaluated. Offers seen are logged rather than only offers taken,
/// so the reservation threshold can be learned from the user's own distribution.
/// </summary>
public sealed record WonderOfferEvent(
    DateTimeOffset At,
    List<string> OwnershipKeys,
    int StaminaCost,
    bool Taken,
    string? Received);

/// <summary>
/// A regenerating resource pool. Pack, Wonder and Trade are three instances of this one shape, and
/// nothing converts between them, so they are never summed or compared. Every hourglass is worth
/// one hour, which is why that ratio lives in GameRules rather than here.
/// </summary>
public sealed record ResourcePool(int Balance, int Hourglasses)
{
    public static readonly ResourcePool Empty = new(0, 0);

    /// <summary>
    /// When this balance was true. The pool regenerates on a clock, so "3 of 5" means one thing
    /// said now and another said two days ago, and projecting it needs the age.
    ///
    /// Null means the figure has no known age and is taken at face value. An optional property
    /// rather than a constructor parameter, so existing saves load unchanged.
    /// </summary>
    public DateTimeOffset? AsOf { get; init; }

    /// <summary>Restamped whenever the balance is written, or the projection would double count.</summary>
    public ResourcePool AsOfNow(DateTimeOffset now) => this with { AsOf = now };
}

public sealed record Resources(
    ResourcePool Wonder,
    ResourcePool Trade,
    int PackHourglasses,
    Dictionary<string, int> PackPointsBySet,
    int Shinedust,
    bool Premium,
    DateTimeOffset? NextFreePackAt)
{
    public static Resources Empty => new(
        ResourcePool.Empty, ResourcePool.Empty, 0, new(), 0, false, null);
}

/// <param name="DefaultPlan">
/// Copies wanted per rarity rung, by default. One map expresses both which rarities are collected
/// and how many of each — "two of every diamond, one of the stars" needs both.
/// </param>
public sealed record TargetSettings(
    Dictionary<int, int> DefaultPlan,
    Dictionary<string, Dictionary<int, int>> PlanBySet)
{
    /// <summary>All diamonds, one copy each.</summary>
    public static TargetSettings Default => new(
        new Dictionary<int, int> { [0] = 1, [1] = 1, [2] = 1, [3] = 1 }, new());

    public RarityPlan PlanFor(string? set = null) =>
        new(set is not null && PlanBySet.TryGetValue(set, out var p) ? p : DefaultPlan);

    public bool HasOverride(string set) => PlanBySet.ContainsKey(set);

    // ---- Older-schema remnants, read only so existing saves can be migrated -----------
    // v1 stored a single threshold; v2 stored a rarity set plus one copy count. These carry
    // the old values through deserialization so StateSerializer can fold them into a plan.
    // Nothing else should ever read them.

    public int? DefaultTierIndex { get; init; }
    public Dictionary<string, int>? TierBySet { get; init; }
    public List<int>? DefaultTiers { get; init; }
    public Dictionary<string, List<int>>? TiersBySet { get; init; }
    public int? DefaultCopies { get; init; }
    public Dictionary<int, int>? CopiesByTier { get; init; }
}

public sealed record Prefs(string Theme = "auto", int GridColumns = 0, bool ShowOwnedDimmed = true, bool ListView = false)
{
    /// <summary>
    /// Columns in the pack picker, kept separate from <see cref="GridColumns"/>: booster tiles are
    /// a different shape from card tiles, so the count that fits is not the same. Zero means unset
    /// and readers substitute their own default, so adding this field needed no schema bump.
    /// </summary>
    public int PackColumns { get; init; }

    /// <summary>
    /// Roomier list rows, which have space to show attack and ability text inline rather than only
    /// on hover. False is the compact original and the default, since it fits more cards on screen.
    /// </summary>
    public bool ListRoomy { get; init; }

    /// <summary>
    /// Rarity rungs the user counts as a "hit" in History. Null means never chosen, and readers
    /// substitute everything above the diamonds.
    ///
    /// A set of rungs rather than a threshold, as with the completion plan: someone might count
    /// 2-star and Crown but be indifferent to shinies, which "N and above" cannot express. It is a
    /// personal measure, so the app does not pick it.
    /// </summary>
    public List<int>? HitTiers { get; init; }

    /// <summary>
    /// Show saved decks as showcase cards instead of a table. The two views answer different
    /// questions — the table compares decks, the grid recognises them — so it is a persisted
    /// preference rather than a mode.
    /// </summary>
    public bool DeckGrid { get; init; }

    /// <summary>
    /// Show wishlists as showcase cards instead of a table. Kept separate from
    /// <see cref="DeckGrid"/>: someone with three wishlists and forty decks wants a different
    /// answer for each.
    /// </summary>
    public bool WishGrid { get; init; }

    /// <summary>
    /// Copies wanted of each parallel foil — the Deluxe set's second printings of its 1-3 diamond
    /// cards. Zero ignores them; one is the default.
    ///
    /// A count rather than a switch, and separate from the rarity plan, because a foil is not the
    /// rarity it shares: it is 139 extra cards sold only in a limited-time pack. Someone wanting
    /// two of every diamond may want one parallel foil, or none.
    /// </summary>
    public int FoilCopies { get; init; } = 1;

    /// <summary>
    /// Sets priced with borrowed rates, mapped to the set the rates came from.
    ///
    /// Opt-in per set, since it trades accuracy for coverage. A set released weeks ago whose rates
    /// nobody has published yet is otherwise reported as unpullable, even though its cards drop and
    /// an estimate from the standard five-card distribution is closer to the truth than leaving the
    /// set out of every figure on the page.
    ///
    /// The donor is stored rather than derived, so the numbers do not change when a newer set gains
    /// rates and becomes the better default.
    /// </summary>
    public Dictionary<string, string> AssumedRateDonors { get; init; } = [];

    /// <summary>
    /// Slots on the in-game wishlist reserved for widely-held cards rather than the dearest ones.
    ///
    /// Persisted, because the board advisor reports swaps against what is already on the board: a
    /// setting that reset on navigation would have the page demand changes caused by its own
    /// forgetfulness.
    /// </summary>
    public int BoardLiquidSlots { get; init; } = PackProphet.Engine.TradeBoardAdvisor.DefaultLiquidSlots;

    /// <summary>
    /// Packs-equivalent floor below which a card is not worth a board slot. Zero is no floor, and
    /// is the default — see TradeBoardAdvisor.Recommend on how an absolute threshold can empty the
    /// board.
    /// </summary>
    public double BoardMinimumCost { get; init; }

    /// <summary>"with", "without" or "only" - how the board treats parallel foils.</summary>
    public string BoardFoils { get; init; } = "with";

    /// <summary>
    /// Show the evolution-gap strip on the Collection page. True by default, since it is the one
    /// place the information appears unprompted.
    ///
    /// Dismissible and persisted, because for a small collection the gaps are a standing fact
    /// rather than a problem: nearly every evolution is missing a stage early on, so the strip
    /// would not go away on its own.
    /// </summary>
    public bool ShowEvolutionGaps { get; init; } = true;

    /// <summary>
    /// Whether Wonder Pick still explains what it is for. The paragraph exists because the page is
    /// easy to mistake for something the game already does, and that is a thing you need told
    /// once — after which it is a wall of text above the control you came to use.
    ///
    /// Dismissible and persisted, for the same reason as the gap strip: it is a standing note
    /// rather than a passing state, so it never goes away on its own.
    /// </summary>
    public bool ShowWonderIntro { get; init; } = true;

    /// <summary>
    /// Limited-time packs the user has confirmed are currently on sale, by pack key.
    ///
    /// Empty by default, i.e. assumed not available: Deluxe packs are absent more often than
    /// present, so defaulting to available would recommend a pack nobody can buy. The Packs page
    /// surfaces the toggle so the assumption is visible.
    /// </summary>
    public List<string> AvailableLimitedPacks { get; init; } = [];

    /// <summary>
    /// Packs pinned to the front of the Log a pack picker, by pack key, most recently pinned last.
    ///
    /// The picker is scoped to one series, and a pin deliberately escapes that scope: someone
    /// working through a series they are not currently opening still opens the same one or two
    /// packs every day, and having to change the series dropdown first is the whole cost the pin
    /// removes. Order is preserved rather than sorted, so a pin lands where you put it.
    /// </summary>
    public List<string> PinnedPacks { get; init; } = [];
}

/// <summary>
/// The game's own lifetime counters, copied in by the player.
///
/// A baseline with a timestamp rather than a plain total: someone who played for months before
/// finding this app has thousands of packs behind them and nothing logged. Recording what the game
/// says, and when it was read, lets a total be stated as the baseline plus everything logged since,
/// without double-counting packs already inside the game's figure.
///
/// It cannot attribute anything. The game reports how many packs, not which packs, so a baseline
/// never feeds a per-set figure, the points ledger, or an odds-versus-reality comparison. Those
/// stay logged-only, and the UI says which is which.
/// </summary>
/// <param name="At">When the counters were read, so later logging adds rather than overlaps.</param>
public sealed record LifetimeTotals(int PacksOpened, int WonderPicks, DateTimeOffset At)
{
    /// <summary>
    /// Total packs opened: this baseline plus the ones logged after it was read.
    ///
    /// The timestamp filter keeps a player who logs for a week and only then reads the game's
    /// counter from having that week counted twice.
    /// </summary>
    public int PacksWith(IEnumerable<PackOpenEvent> log) =>
        PacksOpened + log.Count(e => e.At > At);

    /// <summary>
    /// Total Wonder Picks taken. Only taken ones count: the log holds every offer seen, which is
    /// what makes the reservation threshold learnable, but the game counts stamina spent.
    /// </summary>
    public int WonderPicksWith(IEnumerable<WonderOfferEvent> log) =>
        WonderPicks + log.Count(e => e.Taken && e.At > At);
}

/// <summary>
/// One collection. Multiple profiles exist because alt accounts are commonplace, and you
/// can trade with yourself.
/// </summary>
public sealed record Profile(
    string Id,
    string Name,
    Dictionary<string, int> Collection,
    TargetSettings Targets,
    List<SavedDeck> Decks,
    List<Wishlist> Wishlists,
    List<PackOpenEvent> PackLog,
    List<WonderOfferEvent> WonderLog,
    Resources Resources)
{
    /// <summary>
    /// Cards currently on the game's own 20-slot wishlist, by ownership key.
    ///
    /// Stored because there is no import path into the game: the board is retyped by hand, so the
    /// app has to know what is already on it to recommend swaps rather than a fresh list of twenty.
    /// </summary>
    public List<string> TradeBoard { get; init; } = [];

    /// <summary>
    /// The game's own lifetime counters, or null if never entered. An optional property rather
    /// than a constructor parameter, so existing saves deserialize unchanged and no schema bump
    /// is needed.
    /// </summary>
    public LifetimeTotals? Lifetime { get; init; }

    /// <summary>
    /// The player's name in the game, so a shared wishlist can be acted on. A want-list is
    /// useless to the person reading it unless they can find you to send the card, and nothing
    /// else in the app carries that: the profile name is whatever you called this collection.
    ///
    /// Per profile rather than per app. A second profile is a second account, with its own
    /// friend list and its own name.
    ///
    /// Optional, like Lifetime, so existing saves deserialize unchanged.
    /// </summary>
    public string? InGameName { get; init; }

    public static Profile NewDefault(string id = "default", string name = "My collection") =>
        new(id, name, new(), TargetSettings.Default, [], [], [], [], Resources.Empty);
}

/// <summary>
/// Everything persisted. <see cref="SchemaVersion"/> ships from day one because this shape will
/// change, and a silent misread of old data is worse than a migration.
/// </summary>
public sealed record AppState(
    int SchemaVersion,
    List<Profile> Profiles,
    string ActiveProfileId,
    Prefs Prefs)
{
    public const int CurrentSchemaVersion = 3;

    public static AppState Fresh()
    {
        var p = Profile.NewDefault();
        return new AppState(CurrentSchemaVersion, [p], p.Id, new Prefs());
    }

    public Profile Active =>
        Profiles.FirstOrDefault(p => p.Id == ActiveProfileId) ?? Profiles[0];
}
