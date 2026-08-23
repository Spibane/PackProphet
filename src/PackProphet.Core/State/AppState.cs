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
