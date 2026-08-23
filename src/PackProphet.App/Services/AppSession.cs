using Microsoft.JSInterop;
using PackProphet.Data;
using PackProphet.Deck;
using PackProphet.Domain;
using PackProphet.Engine;
using PackProphet.State;

namespace PackProphet.Services;

/// <summary>
/// The app's single source of truth: loaded card data, the active profile, and the engine
/// built over both. Pages read from here and call Mutate to change anything.
///
/// Saves are debounced because tap-to-increment fires rapidly; writing on every tap would
/// serialise the whole state dozens of times a second.
/// </summary>
public sealed class AppSession : IAsyncDisposable
{
    private readonly CardDataLoader _loader;
    private readonly IStateStore _store;
    private readonly IJSRuntime _js;

    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _pendingSave;

    // Undo history. Bounded: with drag-select and fast tapping, unbounded history would grow
    // without limit, and nobody needs to undo a thousand steps.
    private const int MaxUndo = 50;
    private readonly List<AppState> _undo = [];
    private readonly List<AppState> _redo = [];

    public AppSession(CardDataLoader loader, IStateStore store, IJSRuntime js)
    {
        _loader = loader;
        _store = store;
        _js = js;
    }

    public CardData? Data { get; private set; }
    public AppState State { get; private set; } = AppState.Fresh();
    public PackOdds? Odds { get; private set; }
    public PackRanker? Ranker { get; private set; }

    /// <summary>Cheapest-route pricing. Null until data has loaded.</summary>
    public RouteCost? Routes { get; private set; }

    /// <summary>Per-set pack-point balances and cap warnings. Null until data has loaded.</summary>
    public PointsLedger? Points { get; private set; }

    public bool Ready => Data is not null;
    public CardIndex Index => Data?.Index ?? throw new InvalidOperationException("Card data not loaded.");
    public CardFacts Facts => Data?.Facts ?? CardFacts.Empty;
    public SetCatalog Sets => Data?.Sets ?? new SetCatalog(null, []);
    public PackArtCatalog PackArt => Data?.PackArt ?? PackArtCatalog.Empty;

    /// <summary>
    /// Best available booster art for a pack: the higher-resolution image where it is
    /// published, otherwise the lower-resolution one. Null only if neither exists, in which
    /// case the tile shows its drawn placeholder.
    /// </summary>
    public string PackArtUrl(string packKey)
    {
        var parts = packKey.Split(':', 2);
        var better = PackArt.UrlFor(packKey);
        return better ?? (parts.Length == 2 ? PocketCard.PackArtUrl(parts[1]) : "");
    }

    /// <summary>
    /// The card's type as one column: its energy for a Pokémon, its trainer kind otherwise.
    /// Empty when the facts table has not caught up with the card list, which it often has
    /// not for the newest sets.
    /// </summary>
    public string TypeLabel(PocketCard card) => FactFor(card)?.Subtype ?? "";

    /// <summary>Printed detail for a card, by identity. Null only if upstream lacks it.</summary>
    public CardFact? FactFor(PocketCard card)
    {
        var nr = Index.DeckNrOf(card);
        return nr is null ? null : Facts.For(nr.Value);
    }

    /// <summary>Evolution stage for a Pokémon: "basic", "1" or "2". Empty for trainers.</summary>
    public string StageLabel(PocketCard card) => FactFor(card)?.Stage ?? "";
    public Profile Profile => State.Active;
    public Collection Owned { get; private set; } = new();

    /// <summary>
    /// Increments only when the owned-cards map actually changes. Lets a page tell a
    /// collection edit apart from an unrelated setting change, so it can skip an expensive
    /// re-rank that would produce an identical result.
    /// </summary>
    public int CollectionRevision { get; private set; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Raised after any change, so pages can re-render.</summary>
    public event Action? Changed;

    private Task? _init;

    /// <summary>
    /// Load data and state. Safe to call from every page: several pages initialise at once
    /// during navigation, and without memoising the task they would each fetch the database
    /// and each overwrite State.
    /// </summary>
    public Task InitAsync(CancellationToken ct = default) => _init ??= InitCoreAsync(ct);

    private async Task InitCoreAsync(CancellationToken ct)
    {
        Data = await _loader.LoadAsync(ct);
        State = await _store.LoadAsync(ct);
        // After the state load, because the engine is built over the user's assumed-rate
        // choices as well as over the published data.
        RebuildEngine();
        RebuildCollection();
        // theme.js already applied the saved theme from localStorage before Blazor booted, so
        // this is only a re-assert — it matters when the state came from somewhere that script
        // could not read, e.g. a JSON import.
        await ApplyThemeAsync();
        Changed?.Invoke();

        // Card detail arrives afterwards: it is the biggest payload by an order of magnitude
        // and blocking startup on it made the app look broken. Columns that depend on it stay
        // blank until it lands, then fill in.
        _ = LoadFactsInBackgroundAsync();
    }

    private void RebuildCollection() => Owned = new Collection(Profile.Collection);

    /// <summary>
    /// Builds the odds engine and everything derived from it. Re-run whenever the RATE TABLE
    /// changes — which the user can do, by asking an unpriced set to borrow another set's
    /// distributions. Cheap: every one of these caches lazily, so nothing is computed here.
    /// </summary>
    private void RebuildEngine()
    {
        if (Data is null) return;

        var rates = EffectiveRates();
        Odds = new PackOdds(Data.Index, rates);
        Ranker = new PackRanker(Data.Index, Odds);
        Routes = new RouteCost(Data.Index, Odds, Data.Rarities);
        Points = new PointsLedger(Data.Index, Data.Rarities);
    }

    /// <summary>
    /// Published rates, plus any the user has asked to borrow. Applied one donor at a time
    /// because each set stores its own: a user who assumed A4a's rates for B4 months ago keeps
    /// them even after a newer set arrives.
    /// </summary>
    private PullRates EffectiveRates()
    {
        var rates = Data!.Rates;
        var donors = State.Prefs.AssumedRateDonors;
        if (donors.Count == 0) return rates;

        foreach (var group in donors.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase))
            rates = rates.Assuming(group.Select(kv => kv.Key), group.Key);

        return rates;
    }

    /// <summary>Sets currently priced with borrowed rates, mapped to where the rates came from.</summary>
    public IReadOnlyDictionary<string, string> AssumedRates =>
        Odds?.Rates.AssumedFrom ?? new Dictionary<string, string>();

    /// <summary>
    /// The set a borrowed distribution would come from by default: the newest measured set that
    /// sells ordinary packs.
    /// </summary>
    public string? StandardRateDonor => Data is null ? null : EffectiveRates().StandardDonor(Sets);

    /// <summary>Sets that could be priced by borrowing: released, no published rates, has packs.</summary>
    public IReadOnlyList<string> SetsAwaitingRates
    {
        get
        {
            if (Data is null) return [];

            var today = DateOnly.FromDateTime(DateTime.Now);
            return Index.OpenableSets
                .Where(set => !Data.Rates.Covers(set))
                .Where(set => Sets.IsReleased(set, today))
                .OrderBy(set => set, StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>
    /// Start or stop pricing a set with another set's distributions. Rebuilds the engine, so
    /// every figure on every page follows immediately.
    /// </summary>
    public void SetAssumedRates(string set, string? donor)
    {
        var next = new Dictionary<string, string>(State.Prefs.AssumedRateDonors, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(donor)) next.Remove(set);
        else next[set] = donor;

        State = State with { Prefs = State.Prefs with { AssumedRateDonors = next } };
        RebuildEngine();
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>True once card detail has loaded, so the UI can say "still loading" honestly.</summary>
    public bool FactsReady { get; private set; }

    private async Task LoadFactsInBackgroundAsync()
    {
        try
        {
            var facts = await _loader.LoadFactsAsync();
            if (Data is null) return;

            Data = Data with { Facts = facts };
            FactsReady = facts.Count > 0;
            // Dropped so it is rebuilt against the facts that just arrived: a linter holding
            // the empty table would go on reporting every stage as unverifiable.
            _linter = null;
            Changed?.Invoke();
        }
        catch
        {
            // Enrichment only: the app stays fully usable without it.
        }
    }

    /// <summary>Apply a change to the active profile, recording it for undo.</summary>
    public void Mutate(Func<Profile, Profile> change, bool undoable = true)
    {
        if (undoable)
        {
            _undo.Add(State);
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            _redo.Clear();
        }

        var previousCollection = Profile.Collection;
        var updated = change(Profile);

        // Reference comparison is exact here: every collection edit builds a NEW dictionary,
        // while `p with { ... }` for anything else carries the same instance through.
        if (!ReferenceEquals(previousCollection, updated.Collection)) CollectionRevision++;

        State = State with
        {
            Profiles = State.Profiles.Select(p => p.Id == updated.Id ? updated : p).ToList()
        };
        RebuildCollection();
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>Replace all state, e.g. from an imported backup. Undoable.</summary>
    public void ReplaceState(AppState imported)
    {
        _undo.Add(State);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();

        State = imported;
        CollectionRevision++;
        RebuildCollection();
        QueueSave();
        Changed?.Invoke();
    }

    public void Undo() => Step(_undo, _redo);
    public void Redo() => Step(_redo, _undo);

    private void Step(List<AppState> from, List<AppState> to)
    {
        if (from.Count == 0) return;
        to.Add(State);
        State = from[^1];
        from.RemoveAt(from.Count - 1);
        CollectionRevision++;
        RebuildCollection();
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>Set the owned count for one card. The core collection edit.</summary>
    public void SetCount(PocketCard card, int count) => SetCount(card.OwnershipKey, count);

    public void SetCount(string ownershipKey, int count)
    {
        Mutate(p =>
        {
            var next = new Dictionary<string, int>(p.Collection);
            if (count > 0) next[ownershipKey] = count; else next.Remove(ownershipKey);
            return p with { Collection = next };
        });
    }

    /// <summary>Adjust several cards at once — one undo step for a whole drag-sweep.</summary>
    public void Adjust(IEnumerable<string> ownershipKeys, int delta, int max = 99)
    {
        var keys = ownershipKeys.Distinct().ToArray();
        if (keys.Length == 0) return;

        Mutate(p =>
        {
            var next = new Dictionary<string, int>(p.Collection);
            foreach (var key in keys)
            {
                var value = Math.Clamp((next.TryGetValue(key, out var c) ? c : 0) + delta, 0, max);
                if (value > 0) next[key] = value; else next.Remove(key);
            }
            return p with { Collection = next };
        });
    }

    public int CountOf(PocketCard card) => Owned.Of(card);

    // ---- decks ------------------------------------------------------------------------

    /// <summary>
    /// Saved decks, ordered closest-to-buildable first. Being told you are one card away is
    /// the most motivating thing this app can say, so it leads rather than hides behind a sort
    /// control.
    /// </summary>
    public IReadOnlyList<SavedDeck> DecksByBuildability =>
        Profile.Decks
            .OrderBy(MissingCount)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public SavedDeck? DeckById(string id) => Profile.Decks.FirstOrDefault(d => d.Id == id);

    /// <summary>
    /// Copies the deck still needs. Counted per IDENTITY with multiplicity, because a deck
    /// slot is satisfied by ANY printing of the card — and by two copies of the same printing
    /// just as well as by two different ones.
    /// </summary>
    public int MissingCount(SavedDeck deck)
    {
        if (Data is null) return 0;

        var missing = 0;
        foreach (var group in deck.DeckBuilderNrs.GroupBy(nr => nr))
            missing += Math.Max(0, group.Count() - OwnedCopiesOfIdentity(group.Key));

        return missing;
    }

    /// <summary>
    /// Copies owned of a card identity, pooling every printing of it. Owning one alternate art
    /// and one plain print is two copies for deck purposes, which is how the game sees it.
    /// </summary>
    public int OwnedCopiesOfIdentity(int deckBuilderNr)
    {
        if (Data is null) return 0;
        if (!Index.ByDeckBuilderNr.TryGetValue(deckBuilderNr, out var printings)) return 0;

        return Owned.OfIdentity(printings);
    }

    /// <summary>Any printing of an identity, preferring one already owned so the art shown is the user's.</summary>
    public PocketCard? PrintingOf(int deckBuilderNr)
    {
        if (Data is null || !Index.ByDeckBuilderNr.TryGetValue(deckBuilderNr, out var printings))
            return null;

        return printings.FirstOrDefault(c => Owned.Of(c) > 0) ?? printings[0];
    }

    /// <summary>
    /// The card that represents a deck: the user's pick if they made one and it is still in the
    /// deck, otherwise the rarest POKÉMON in it.
    ///
    /// Pokémon rather than rarest card, because rarity does not identify a deck — a Crown Tool
    /// would end up fronting every deck that runs it, while a deck is nearly always built around
    /// one Pokémon. Trainers are only used if the deck has no Pokémon at all.
    /// </summary>
    public PocketCard? FaceCardOf(SavedDeck deck)
    {
        if (Data is null) return null;

        if (deck.FaceNr is int chosen && deck.DeckBuilderNrs.Contains(chosen))
            return PrintingOf(chosen);

        var cards = deck.DeckBuilderNrs.Distinct()
            .Select(PrintingOf)
            .Where(c => c is not null)
            .Select(c => c!)
            .ToArray();

        var pokemon = cards.Where(c => FactFor(c)?.IsPokemon == true).ToArray();
        var pool = pokemon.Length > 0 ? pokemon : cards;

        return pool
            .OrderByDescending(c => Index.Ladder.IndexOf(c.Rarity) ?? -1)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
    }

    public void SaveDeck(SavedDeck deck)
    {
        Mutate(p =>
        {
            var next = new List<SavedDeck>(p.Decks);
            var at = next.FindIndex(d => d.Id == deck.Id);
            if (at >= 0) next[at] = deck; else next.Add(deck);
            return p with { Decks = next };
        });
    }

    public void DeleteDeck(string id) =>
        Mutate(p => p with { Decks = p.Decks.Where(d => d.Id != id).ToList() });

    // ---- wishlists --------------------------------------------------------------------

    /// <summary>
    /// Saved wishlists. The main lever for versatility: the same odds engine that prices a
    /// rarity target prices "the cards I think are cool" with no new machinery.
    /// </summary>
    public IReadOnlyList<Wishlist> Wishlists => Profile.Wishlists;

    public Wishlist? WishlistById(string id) => Profile.Wishlists.FirstOrDefault(w => w.Id == id);

    /// <summary>Creates an empty list and returns its id, so the caller can navigate to it.</summary>
    public string CreateWishlist(string? name = null)
    {
        var id = Guid.NewGuid().ToString("n")[..8];
        var chosen = string.IsNullOrWhiteSpace(name)
            ? WishlistEdit.FreshName(Profile.Wishlists)
            : name.Trim();

        Mutate(p => p with { Wishlists = [.. p.Wishlists, new Wishlist(id, chosen, new())] });
        return id;
    }

    public void RenameWishlist(string id, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return;

        UpdateWishlist(id, w => w with { Name = trimmed });
    }

    public void DeleteWishlist(string id) =>
        Mutate(p => p with { Wishlists = p.Wishlists.Where(w => w.Id != id).ToList() });

    /// <summary>Add or subtract wanted copies. Zero removes the card from the list.</summary>
    public void BumpWanted(string id, string ownershipKey, int delta) =>
        UpdateWishlist(id, w => WishlistEdit.Bump(w, ownershipKey, delta));

    public void SetWanted(string id, string ownershipKey, int copies) =>
        UpdateWishlist(id, w => WishlistEdit.SetWanted(w, ownershipKey, copies));

    private void UpdateWishlist(string id, Func<Wishlist, Wishlist> change) =>
        Mutate(p => p with
        {
            Wishlists = p.Wishlists.Select(w => w.Id == id ? change(w) : w).ToList()
        });

    /// <summary>
    /// Copies still needed across a whole wishlist. Counted per printing, matching how the
    /// list itself is keyed.
    /// </summary>
    public int MissingInWishlist(Wishlist list) =>
        list.Wanted.Sum(kv => Math.Max(0, kv.Value - Owned[kv.Key]));

    public int WantedTotal(Wishlist list) => list.Wanted.Values.Sum();

    public ICompletionTarget TargetForWishlist(Wishlist list) =>
        new WishlistTarget(list.Name, list.Wanted);

    /// <summary>Cards on a wishlist, rarest first — how a wishlist is usually read.</summary>
    public IReadOnlyList<(PocketCard Card, int Wanted, int Owned)> WishlistCards(Wishlist list)
    {
        if (Data is null) return [];

        return list.Wanted
            .Select(kv => (Card: CardByOwnershipKey(kv.Key), Wanted: kv.Value, Owned: Owned[kv.Key]))
            .Where(x => x.Card is not null)
            .Select(x => (x.Card!, x.Wanted, x.Owned))
            .OrderByDescending(x => Index.Ladder.IndexOf(x.Item1.Rarity) ?? -1)
            .ThenBy(x => x.Item1.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Any entry for a printing. Several set entries can share one ownership key — a Deluxe
    /// reprint is the same card — and for display they are interchangeable.
    /// </summary>
    public PocketCard? CardByOwnershipKey(string ownershipKey) =>
        Data is not null && Index.ByOwnershipKey.TryGetValue(ownershipKey, out var cards)
            ? cards[0]
            : null;

    /// <summary>A linter bound to the current card data, or null before it loads.</summary>
    public DeckLinter? Linter => Data is null ? null : _linter ??= new DeckLinter(Index, Facts);

    private DeckLinter? _linter;


    /// <summary>
    /// Limited-time packs the user has NOT confirmed as currently on sale. Passed to the
    /// ranker so it never recommends a pack that cannot be bought today.
    /// </summary>
    public IReadOnlySet<string> UnavailablePacks =>
        Odds is null
            ? new HashSet<string>()
            : Odds.LimitedTimePacks
                  .Where(p => !State.Prefs.AvailableLimitedPacks.Contains(p))
                  .ToHashSet();

    /// <summary>Limited-time packs at all, so the UI can offer a toggle for each.</summary>
    public IReadOnlyList<string> LimitedPacks =>
        Odds is null ? [] : Odds.LimitedTimePacks.OrderBy(p => p, StringComparer.Ordinal).ToArray();

    // ---- appearance -------------------------------------------------------------------

    /// <summary>"auto" (follow the OS), "light", or "dark".</summary>
    public string Theme => State.Prefs.Theme is "light" or "dark" ? State.Prefs.Theme : "auto";

    public async Task SetThemeAsync(string theme)
    {
        var next = theme is "light" or "dark" ? theme : "auto";
        if (next == Theme) return;

        State = State with { Prefs = State.Prefs with { Theme = next } };
        QueueSave();
        await ApplyThemeAsync();
        Changed?.Invoke();
    }

    /// <summary>
    /// Hands the preference to theme.js, which owns resolving "auto" against the OS and
    /// setting Bootstrap's data-bs-theme. Failures are swallowed: theming is cosmetic, and an
    /// interop error during prerender or teardown must not take a page down with it.
    /// </summary>
    private async Task ApplyThemeAsync()
    {
        try { await _js.InvokeVoidAsync("ppTheme.set", Theme); }
        catch { /* cosmetic only */ }
    }

    public bool ListView => State.Prefs.ListView;

    /// <summary>
    /// Roomier list rows. Worth persisting rather than defaulting per visit: it is a reading
    /// preference, like the column count, not a per-page mode.
    /// </summary>
    public bool ListRoomy => State.Prefs.ListRoomy;

    public void SetListRoomy(bool on)
    {
        if (on == State.Prefs.ListRoomy) return;
        State = State with { Prefs = State.Prefs with { ListRoomy = on } };
        QueueSave();
        Changed?.Invoke();
    }

    public void SetListView(bool on)
    {
        State = State with { Prefs = State.Prefs with { ListView = on } };
        QueueSave();
        Changed?.Invoke();
    }

    // Column counts are persisted preferences, not per-visit view state: a user who wants
    // 8 columns wants them on every visit, and re-picking on each navigation is exactly the
    // kind of friction that makes a tracker tiring to use. Zero in the save means "never
    // chosen", so the defaults live here rather than in the schema — which keeps adding
    // the field free of a migration.

    public const int DefaultGridColumns = 6;
    public const int DefaultPackColumns = 4;

    /// <summary>Columns in a card grid, shared by Collection and Log a Pack.</summary>
    public int GridColumns =>
        State.Prefs.GridColumns > 0 ? State.Prefs.GridColumns : DefaultGridColumns;

    public void SetGridColumns(int n)
    {
        if (n <= 0 || n == GridColumns) return;
        State = State with { Prefs = State.Prefs with { GridColumns = n } };
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>Columns in the pack picker on Log a Pack.</summary>
    public int PackColumns =>
        State.Prefs.PackColumns > 0 ? State.Prefs.PackColumns : DefaultPackColumns;

    public void SetPackColumns(int n)
    {
        if (n <= 0 || n == PackColumns) return;
        State = State with { Prefs = State.Prefs with { PackColumns = n } };
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>
    /// Rungs that count as a hit. Defaults to everything above the diamonds, which is the
    /// common reading, but it is the user's call — see <see cref="Prefs.HitTiers"/>.
    /// </summary>
    public IReadOnlySet<int> HitTiers
    {
        get
        {
            var saved = State.Prefs.HitTiers;
            if (saved is { Count: > 0 }) return saved.ToHashSet();

            return Data is null
                ? new HashSet<int>()
                : Index.Ladder.Rungs.Where(r => r.GlyphClass != "diamond")
                       .Select(r => r.Index).ToHashSet();
        }
    }

    /// <summary>
    /// Add or remove one rung. An empty selection is refused: zero hits forever reads as a
    /// broken counter rather than as a choice.
    /// </summary>
    public void ToggleHitTier(int tier)
    {
        var next = HitTiers.ToHashSet();
        if (!next.Remove(tier)) next.Add(tier);
        if (next.Count == 0) return;

        State = State with { Prefs = State.Prefs with { HitTiers = next.Order().ToList() } };
        QueueSave();
        Changed?.Invoke();
    }

    public bool DeckGrid => State.Prefs.DeckGrid;

    public void SetDeckGrid(bool on)
    {
        if (on == State.Prefs.DeckGrid) return;
        State = State with { Prefs = State.Prefs with { DeckGrid = on } };
        QueueSave();
        Changed?.Invoke();
    }

    public bool WishGrid => State.Prefs.WishGrid;

    public void SetWishGrid(bool on)
    {
        if (on == State.Prefs.WishGrid) return;
        State = State with { Prefs = State.Prefs with { WishGrid = on } };
        QueueSave();
        Changed?.Invoke();
    }

    public bool IsLimitedPackAvailable(string packKey) =>
        State.Prefs.AvailableLimitedPacks.Contains(packKey);

    public void SetLimitedPackAvailable(string packKey, bool available)
    {
        var next = State.Prefs.AvailableLimitedPacks.Where(p => p != packKey).ToList();
        if (available) next.Add(packKey);

        State = State with { Prefs = State.Prefs with { AvailableLimitedPacks = next } };
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>The active completion plan for one set, from the user's settings.</summary>
    public ICompletionTarget TargetForSet(string set) =>
        new RarityLadderTarget(set, Profile.Targets.PlanFor(set));

    /// <summary>Copies wanted per rarity, for the whole collection or one set.</summary>
    public RarityPlan Plan(string? set = null) => Profile.Targets.PlanFor(set);

    /// <summary>
    /// Whether this set has its own plan rather than following the default. Worth surfacing:
    /// an override is invisible from the chips alone, and someone who set one months ago will
    /// otherwise read the default as a bug.
    /// </summary>
    public bool HasPlanOverride(string set) => Profile.Targets.HasOverride(set);

    /// <summary>
    /// Step one rarity through none, one, two, none. Refuses to leave the plan empty: a plan
    /// wanting nothing makes every screen read "complete", which looks like a bug rather than
    /// a choice.
    /// </summary>
    public void CycleTier(int tierIndex, string? set = null)
    {
        var next = Plan(set).Cycle(tierIndex);
        if (next.IsEmpty) return;

        SetPlan(next, set);
    }

    /// <param name="set">
    /// Null sets the default that every set without its own plan follows; a set code sets that
    /// one set's plan. Sets differ in what is worth chasing — a set you only want the stars
    /// from is ordinary — so one global answer cannot serve.
    /// </param>
    public void SetPlan(RarityPlan plan, string? set = null)
    {
        if (plan.IsEmpty) return;

        var map = plan.CopiesByTier.Where(kv => kv.Value > 0)
                      .OrderBy(kv => kv.Key)
                      .ToDictionary(kv => kv.Key, kv => kv.Value);

        Mutate(p => set is null
            ? p with { Targets = p.Targets with { DefaultPlan = map } }
            : p with
            {
                Targets = p.Targets with
                {
                    PlanBySet = new Dictionary<string, Dictionary<int, int>>(p.Targets.PlanBySet)
                    {
                        [set] = map
                    }
                }
            }, undoable: false);
    }

    /// <summary>Hand a set back to the default plan.</summary>
    public void ClearPlanOverride(string set)
    {
        if (!HasPlanOverride(set)) return;

        Mutate(p => p with
        {
            Targets = p.Targets with
            {
                PlanBySet = p.Targets.PlanBySet.Where(kv => kv.Key != set)
                             .ToDictionary(kv => kv.Key, kv => kv.Value)
            }
        }, undoable: false);
    }

    /// <summary>
    /// Every set with openable packs, as one combined target. Promo sets are excluded: their
    /// cards come from events, so no amount of pack opening advances them and including them
    /// would only make every estimate look permanently unfinishable.
    /// </summary>
    public ICompletionTarget TargetForEverything() =>
        new CompositeTarget(Index.OpenableSets.Select(TargetForSet).ToArray(), "everything");

    // Debounce: coalesce a burst of edits into one write.
    private void QueueSave()
    {
        _pendingSave?.Cancel();
        _pendingSave = new CancellationTokenSource();
        var ct = _pendingSave.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, ct);
                await _saveGate.WaitAsync(ct);
                try { await _store.SaveAsync(State, ct); }
                finally { _saveGate.Release(); }
            }
            catch (OperationCanceledException) { /* superseded by a later edit */ }
        }, ct);
    }

    /// <summary>Force an immediate write, e.g. before export.</summary>
    public async Task FlushAsync()
    {
        _pendingSave?.Cancel();
        await _saveGate.WaitAsync();
        try { await _store.SaveAsync(State); }
        finally { _saveGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _pendingSave?.Cancel();
        try { await FlushAsync(); } catch { /* best effort on teardown */ }
        _saveGate.Dispose();
    }
}
