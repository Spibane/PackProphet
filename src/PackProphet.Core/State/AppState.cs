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
public sealed record ChaseList(string Id, string Name, Dictionary<string, int> Wanted);

/// <summary>One logged pack opening. The basis of the points ledger and the history view.</summary>
public sealed record PackOpenEvent(
    DateTimeOffset At,
    string Set,
    string Pack,
    string Variant,
    List<string> OwnershipKeys)
{
    /// <summary>
    /// Stable identity, assigned when the pack is logged and kept through every edit.
    ///
    /// The merge used to identify a row by its contents, which is fine until a row changes:
    /// redating a pack rewrote At, the other device still held the original, and the two were no
    /// longer the same row. One pack became two, counted twice in every history figure. An id
    /// that survives the edit is what makes "this row moved" expressible at all.
    ///
    /// Nullable because a save written before v5 has none. <see cref="StateSerializer"/> fills
    /// them in on read -- deterministically, from the contents, so two devices migrating the same
    /// row independently arrive at the same id rather than forking it.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Pack hourglasses this pack cost, if it was one the day's free packs did not cover and the
    /// setting was on to spend them. Zero for a free pack, and for every pack logged before the
    /// setting existed.
    ///
    /// Recorded rather than recomputed, because deleting the row is where it is needed and by
    /// then the day's arithmetic has moved on: whether THIS pack was the third one that Tuesday
    /// cannot be worked out from a log with a row taken out of it. Optional, so older saves load
    /// unchanged.
    /// </summary>
    public int Hourglasses { get; init; }
}

/// <summary>
/// One Wonder Pick offer that was evaluated. Offers seen are logged rather than only offers taken,
/// so the reservation threshold can be learned from the user's own distribution.
/// </summary>
public sealed record WonderOfferEvent(
    DateTimeOffset At,
    List<string> OwnershipKeys,
    int StaminaCost,
    bool Taken,
    string? Received)
{
    /// <summary>Stable identity. See <see cref="PackOpenEvent.Id"/>, which it works exactly like.</summary>
    public string? Id { get; init; }

    /// <summary>
    /// A Deluxe offer, with Pack Hourglasses in its fifth slot. Null for offers logged before this
    /// was recorded. Kept out of <see cref="LogId.Content"/>, so those offers keep their identity.
    /// </summary>
    public bool? Deluxe { get; init; }

    /// <summary>Taken, and the hourglasses were what came out.</summary>
    public bool ReceivedHourglasses { get; init; }
}

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
    /// <summary>
    /// When the day's hourglasses were last credited from the Resources page, so the button that
    /// does it can refuse a second go on the same day. A double tap is otherwise invisible: two
    /// balances four and two too high, and every timeline on the page quietly optimistic.
    ///
    /// An optional property rather than a constructor parameter, so saves written before it load
    /// unchanged. Null means never.
    /// </summary>
    public DateTimeOffset? DailyHourglassesAt { get; init; }

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
    /// Which palette the app draws itself in: "paper", warm neutral surfaces with a single accent
    /// and the default, or "slate", the greys and blues the app shipped with.
    ///
    /// A separate axis from <see cref="Theme"/> rather than two more values on it. Each skin has a
    /// light and a dark form, so folding them together would mean four settings that mostly repeat
    /// each other, and picking a look would silently pick a brightness with it.
    ///
    /// Nullable, and null means never chosen: an unset skin is not written at all, so a save made
    /// before this existed adopts whatever the current default is rather than being pinned to the
    /// look it happened to be saved under. As with <see cref="PackColumns"/>, that is what let this
    /// field be added without a schema bump.
    /// </summary>
    public string? Skin { get; init; }

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
    /// Show chase lists as showcase cards instead of a table. Kept separate from
    /// <see cref="DeckGrid"/>: someone with three chase lists and forty decks wants a different
    /// answer for each.
    /// </summary>
    public bool ChaseGrid { get; init; }

    /// <summary>
    /// How the deck shelf is ordered: "buildable", "name", "size", or "custom".
    ///
    /// Persisted, because the shelf is a place you return to and an order that reset on every
    /// visit is one nobody would bother setting. Null means never chosen, and the reader supplies
    /// the default -- so this could be added without a schema bump, and an order written by a
    /// later version that this one does not know falls back rather than throwing.
    ///
    /// A string rather than the page's enum, for the same reason <see cref="BoardFoils"/> is one:
    /// the enum is a detail of one component and the stored value outlives it.
    /// </summary>
    public string? DeckOrder { get; init; }

    /// <summary>
    /// How the chase shelf is ordered: "closest", "name", "size", or "custom". Separate from
    /// <see cref="DeckOrder"/>, on the same argument as the two grid flags.
    /// </summary>
    public string? ChaseOrder { get; init; }

    /// <summary>
    /// What the four Answer pages worked toward, and no longer: read and cleared, like
    /// <see cref="WishGrid"/> below.
    ///
    /// It was one saved scope for the pack ranking, the trade queue, the wishlist and the Wonder
    /// Pick bar, on the argument that they ask one question. They do -- but the answer does not
    /// keep. Saved, it outlived the visit that set it, so a page opened days later ranked against
    /// a set chosen once somewhere else, with a picker nobody remembered touching as the only
    /// clue. A link could write it too, which is how "Which Pack" on one Progress panel came to
    /// decide what Wonder Pick thought was worth a stamina.
    ///
    /// Each page keeps its own scope for the length of the visit now, and a redirect can seed it.
    /// Kept here so a save that carries one still loads; StateSerializer drops the value.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>The v3 spelling of <see cref="ChaseGrid"/>. Read and cleared, as on the profile.</summary>
    public bool? WishGrid { get; init; }

    /// <summary>
    /// Spend pack hourglasses automatically on packs logged past the day's free ones.
    ///
    /// Off by default: it draws down a stored balance without being asked, and a player who
    /// hoards hourglasses for a new set's release would have the app spending them on every pack
    /// they log. On, the allowance is the account's own -- two a day, three with premium.
    ///
    /// A preference rather than a fact about the account, unlike <c>Resources.Premium</c>: it is
    /// how the user wants their logging booked, not something the game decides.
    /// </summary>
    public bool AutoPackHourglasses { get; init; }

    /// <summary>
    /// Copies wanted of each parallel foil — the Deluxe sets' second printings of their 1-3
    /// diamond cards. Zero ignores them; one is the default. One count for every Deluxe set.
    ///
    /// A count rather than a switch, and separate from the rarity plan, because a foil is not the
    /// rarity it shares: it is 139 extra cards in A4b alone, sold only in a limited-time pack. Someone wanting
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

    /// <summary>
    /// How the Progress panels are ordered: "release" or "closest".
    ///
    /// Deliberately not "packs". Expected packs is the one figure on that page that cannot order
    /// it: a set's packs are its own, so putting a 60-pack set above a 400-pack one would rank two
    /// quantities that are not the same quantity. Percent complete is comparable across sets, and
    /// release order is the order the collection itself is in, so those are the two on offer.
    ///
    /// Null means never chosen and the reader supplies the default, as with the shelf orders — so
    /// this needed no schema bump, and a value written by a later version falls back rather than
    /// throwing.
    /// </summary>
    public string? ProgressOrder { get; init; }

    /// <summary>
    /// Leave finished sets off the Progress page. True by default: the page answers "what is
    /// left", and a set with nothing left is not an answer to it.
    /// </summary>
    public bool ProgressHideComplete { get; init; } = true;

    /// <summary>
    /// Include sets that have not been released on the Progress page. False by default, because
    /// every card in them is outstanding and nothing can be done about any of it — an unreleased
    /// set would otherwise lead the page on the strength of being entirely missing.
    /// </summary>
    public bool ProgressIncludeUnreleased { get; init; }

    /// <summary>
    /// Notices the user has hidden, by key. See <c>PackProphet.Data.SiteNotice</c>.
    ///
    /// A LIST OF KEYS, NOT A FLAG
    /// ------------------------------------------------------------------------------
    /// The two dismissible strips above this — <see cref="ShowEvolutionGaps"/> and
    /// <see cref="ShowWonderIntro"/> — are each one boolean, because each announces one standing
    /// fact that never becomes a different fact. The app-wide bar is the opposite: it announces
    /// whichever thing is currently behind, and there is always a next thing. A flag there would
    /// mean hiding "B4a has no rates yet" also hides "B5 has no rates yet" two months later, which
    /// is not dismissing a notice, it is turning off the feature.
    ///
    /// So a dismissal names its subject. <c>AppSession.DismissNotice</c> owns the pruning that
    /// keeps it from growing without bound.
    ///
    /// Empty by default, and empty is also what a save written before this existed deserialises
    /// to — so no schema bump, as with PackColumns and Skin.
    /// </summary>
    public List<string> DismissedNotices { get; init; } = [];
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
    List<ChaseList> ChaseLists,
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
    /// Log rows this collection has deleted, by id -- for the pack log and the Wonder log alike,
    /// since the ids are unique across both.
    ///
    /// Both logs are append-only to the merge, which is what stops a row one device has not seen
    /// from reading as a deletion. The cost is that a row the user really did delete came back on
    /// the next sync from whichever device still had it: clearing the history, or resetting a
    /// collection, undid itself. A deletion has to be a thing the state says, not the absence of
    /// one, or it cannot survive a union.
    ///
    /// Kept rather than pruned, because "absent here" and "never arrived here" are the same
    /// silence. It grows only when something is deleted, and holds an id per row rather than a
    /// row, so a cleared history of several thousand packs costs tens of kilobytes.
    /// </summary>
    public List<string> RemovedLog { get; init; } = [];

    /// <summary>
    /// The game's own lifetime counters, or null if never entered. An optional property rather
    /// than a constructor parameter, so existing saves deserialize unchanged and no schema bump
    /// is needed.
    /// </summary>
    public LifetimeTotals? Lifetime { get; init; }

    /// <summary>
    /// The player's name in the game, so a shared chase list can be acted on. A want-list is
    /// useless to the person reading it unless they can find you to send the card, and nothing
    /// else in the app carries that: the profile name is whatever you called this collection.
    ///
    /// Per profile rather than per app. A second profile is a second account, with its own
    /// friend list and its own name.
    ///
    /// Optional, like Lifetime, so existing saves deserialize unchanged.
    /// </summary>
    public string? InGameName { get; init; }

    /// <summary>
    /// The chase list the grid's hearts write to, or null until the first heart makes one.
    ///
    /// Its own list rather than "whichever chase list happens to be first". A heart is a one-tap
    /// note that you want a card; a chase list is something you curate and price packs against, and
    /// dropping every passing heart into the first curated list quietly rewrites the thing you
    /// built on purpose. Named separately, so the two can be told apart on the chase lists page.
    ///
    /// An id rather than a name, so renaming the list keeps the hearts pointed at it, and nullable
    /// so existing saves deserialize unchanged. A stale id -- the list was deleted -- reads as
    /// "no list yet", and the next heart makes a new one.
    /// </summary>
    public string? WantListId { get; init; }

    /// <summary>
    /// The v3 spelling of <see cref="ChaseLists"/>, read so that saves written before the rename
    /// still open. <see cref="StateSerializer"/> moves it across and clears it, and the global
    /// serializer options drop nulls, so it is read once and never written again.
    /// </summary>
    public List<ChaseList>? Wishlists { get; init; }

    /// <summary>
    /// What a collection is called before anyone renames it. Named rather than inlined because the
    /// merge has to recognise it: a device still carrying the default name has not chosen anything,
    /// so it yields to a device that has, and that check cannot be allowed to drift from this.
    /// </summary>
    public const string DefaultName = "My Collection";

    public static Profile NewDefault(string id = "default", string name = DefaultName) =>
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
    public const int CurrentSchemaVersion = 5;

    public static AppState Fresh()
    {
        var p = Profile.NewDefault();
        return new AppState(CurrentSchemaVersion, [p], p.Id, new Prefs());
    }

    public Profile Active =>
        Profiles.FirstOrDefault(p => p.Id == ActiveProfileId) ?? Profiles[0];
}
