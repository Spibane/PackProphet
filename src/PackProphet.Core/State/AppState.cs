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
/// One Wonder Pick offer that was evaluated. Logging offers SEEN (not just taken) is what
/// lets the reservation threshold be learned from the user's own distribution of offers.
/// </summary>
public sealed record WonderOfferEvent(
    DateTimeOffset At,
    List<string> OwnershipKeys,
    int StaminaCost,
    bool Taken,
    string? Received);

/// <summary>
/// A regenerating resource pool. Pack, Wonder and Trade are three instances of this one
/// shape — and NOTHING converts between them, so they are never summed or compared.
/// Every hourglass is worth one hour, which is why that ratio lives in GameRules rather
/// than here.
/// </summary>
public sealed record ResourcePool(int Balance, int Hourglasses)
{
    public static readonly ResourcePool Empty = new(0, 0);

    /// <summary>
    /// When this balance was true. Without it a stamina figure is unusable the moment it is
    /// typed: the pool regenerates on a clock, so "3 of 5" means one thing said now and another
    /// said two days ago, and the difference is the whole point of projecting it.
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
/// Copies wanted per rarity rung, by default. One map expresses both which rarities are
/// collected and how many of each — "two of every diamond, one of the stars" is ordinary,
/// and a separate selection plus a single copy count could not say it.
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
    /// Columns in the pack picker, kept separate from <see cref="GridColumns"/>: booster
    /// tiles are a different shape from card tiles, so the count that fits comfortably is
    /// not the same. Zero means unset — readers substitute their own default, which is why
    /// adding this field needs no schema bump.
    /// </summary>
    public int PackColumns { get; init; }

    /// <summary>
    /// Roomier list rows, which have space to show attack and ability text inline rather than
    /// only on hover. False is the compact original, and stays the default: it fits far more
    /// cards on screen, which is what scanning a set wants.
    /// </summary>
    public bool ListRoomy { get; init; }

    /// <summary>
    /// Rarity rungs the user counts as a "hit" in History. Null means never chosen, and readers
    /// substitute everything above the diamonds.
    ///
    /// A SET of rungs, not a threshold, for the same reason the completion plan is: someone might
    /// count 2-star and Crown but be indifferent to shinies, and "N and above" cannot say that.
    /// It is a personal measure — a 1-star is a good day to one player and noise to someone
    /// opening thirty packs a week — so the app must not decide it.
    /// </summary>
    public List<int>? HitTiers { get; init; }

    /// <summary>
    /// Show saved decks as showcase cards instead of a table. Both views answer different
    /// questions — the table compares decks, the grid recognises them — so it is a preference
    /// rather than a mode, and it sticks.
    /// </summary>
    public bool DeckGrid { get; init; }

    /// <summary>
    /// Show wishlists as showcase cards instead of a table. Kept separate from
    /// <see cref="DeckGrid"/> rather than shared: someone with three wishlists and forty decks
    /// wants different answers for each, and one flag would make the two pages fight.
    /// </summary>
    public bool WishGrid { get; init; }

    /// <summary>
    /// Copies wanted of each PARALLEL FOIL — the Deluxe set's second printings of its 1-3 diamond
    /// cards. Zero ignores them; one is the default.
    ///
    /// A count rather than a switch, and separate from the rarity plan, because a foil is not the
    /// rarity it shares: it is 139 extra cards sold only in a limited-time pack. Someone wanting
    /// two of every diamond may well want one parallel foil, or none, and a shared number cannot
    /// say either.
    /// </summary>
    public int FoilCopies { get; init; } = 1;

    /// <summary>
    /// Sets priced with BORROWED rates, mapped to the set the rates came from.
    ///
    /// Opt-in per set, because it trades accuracy for coverage and only the user can say which
    /// they want. A set released weeks ago whose rates nobody has published yet is otherwise
    /// reported as unpullable — technically honest, and useless: the cards drop, and an estimate
    /// from the standard five-card distribution is far closer to the truth than leaving the set
    /// out of every figure on the page.
    ///
    /// The donor is stored rather than derived so the numbers do not silently change when a newer
    /// set gains rates and becomes the better default.
    /// </summary>
    public Dictionary<string, string> AssumedRateDonors { get; init; } = [];

    /// <summary>
    /// Slots on the in-game wishlist reserved for widely-held cards rather than the dearest ones.
    ///
    /// Persisted, and that is a correctness matter rather than a convenience: the board advisor
    /// reports SWAPS against what is already on the board, so a setting that reset on navigation
    /// would have the page demand changes caused by nothing but its own forgetfulness.
    /// </summary>
    public int BoardLiquidSlots { get; init; } = PackProphet.Engine.TradeBoardAdvisor.DefaultLiquidSlots;

    /// <summary>
    /// Packs-equivalent floor below which a card is not worth a board slot. Zero is no floor,
    /// which is the default - see the note on TradeBoardAdvisor.Recommend for why an absolute
    /// threshold can empty the board entirely.
    /// </summary>
    public double BoardMinimumCost { get; init; }

    /// <summary>"with", "without" or "only" - how the board treats parallel foils.</summary>
    public string BoardFoils { get; init; } = "with";

    /// <summary>
    /// Show the evolution-gap strip on the Collection page. True by default - it is the one place
    /// the information appears unprompted, and it is genuinely useful the first time.
    ///
    /// Dismissible and PERSISTED because for a small collection the gaps are a standing fact
    /// rather than a problem: nearly every evolution is missing a stage early on, so the strip
    /// would never go away on its own. A notice that cannot be closed is a notice that gets
    /// ignored, which costs more than hiding it.
    /// </summary>
    public bool ShowEvolutionGaps { get; init; } = true;

    /// <summary>
    /// Limited-time packs the user has confirmed are currently on sale, by pack key.
    ///
    /// Empty by default, i.e. assumed NOT available: Deluxe packs are absent far more often
    /// than they are present, so defaulting to available would routinely recommend a pack
    /// nobody can buy. The Packs page surfaces the toggle prominently so the assumption is
    /// visible rather than silent.
    /// </summary>
    public List<string> AvailableLimitedPacks { get; init; } = [];
}

/// <summary>
/// The game's own lifetime counters, copied in by the player.
///
/// Why a BASELINE with a timestamp rather than a plain total: someone who has played for months
/// before finding this app has thousands of packs behind them and nothing logged, so every count
/// here reads as a fraction of the truth. Recording what the game says, and the moment it was
/// read, lets a total be stated honestly - the baseline plus everything logged since - without
/// double-counting packs that were already inside the game's figure when it was read.
///
/// What it deliberately CANNOT do is attribute anything. The game reports how many packs, not
/// which packs, so a baseline can never feed a per-set figure, the points ledger, or any
/// odds-versus-reality comparison. Those stay strictly logged-only, and the UI says which is
/// which.
/// </summary>
/// <param name="At">When the counters were read, so later logging adds rather than overlaps.</param>
public sealed record LifetimeTotals(int PacksOpened, int WonderPicks, DateTimeOffset At)
{
    /// <summary>
    /// Total packs opened: this baseline plus the ones logged AFTER it was read.
    ///
    /// The timestamp filter is the whole point. A player who logs for a week and only then reads
    /// the game's counter would otherwise have that week counted twice, once inside the game's
    /// figure and once from the log.
    /// </summary>
    public int PacksWith(IEnumerable<PackOpenEvent> log) =>
        PacksOpened + log.Count(e => e.At > At);

    /// <summary>
    /// Total Wonder Picks taken. Only taken ones count: the log holds every offer SEEN, which is
    /// what makes the reservation threshold learnable, but the game counts the stamina you spent.
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
    /// app has to know what is already on it to recommend SWAPS rather than a fresh list of twenty.
    /// Retyping the whole board because one card's rank moved is what would get the feature
    /// abandoned.
    /// </summary>
    public List<string> TradeBoard { get; init; } = [];

    /// <summary>
    /// The game's own lifetime counters, or null if never entered. An optional property rather
    /// than a constructor parameter, so existing saves deserialize unchanged and no schema bump
    /// is needed.
    /// </summary>
    public LifetimeTotals? Lifetime { get; init; }

    public static Profile NewDefault(string id = "default", string name = "My collection") =>
        new(id, name, new(), TargetSettings.Default, [], [], [], [], Resources.Empty);
}

/// <summary>
/// Everything persisted. <see cref="SchemaVersion"/> ships from day one because this shape
/// WILL change, and a silent misread of old data is worse than a migration.
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
