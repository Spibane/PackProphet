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

    // Undo history. Bounded: drag-select and fast tapping would otherwise grow it without limit.
    public const int UndoDepth = 50;

    private readonly UndoHistory _history = new(UndoDepth);

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

    /// <summary>Splits a pack budget across pack keys. Null until data has loaded.</summary>
    public PackAllocator? Allocator { get; private set; }

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

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

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
        // All hold a reference to the odds engine, so none can outlive one.
        _packMix = null;
        _trades = null;
        _board = null;
        _diff = null;
        _evolution = null;
        _gapsRevision = -1;
        _fillKeysRevision = -1;

        if (Data is null) return;

        var rates = EffectiveRates();
        Odds = new PackOdds(Data.Index, rates);
        Ranker = new PackRanker(Data.Index, Odds);
        Allocator = new PackAllocator(Data.Index, Odds);
        Routes = new RouteCost(Data.Index, Odds, Data.Rarities);
        Points = new PointsLedger(Data.Index, Data.Rarities);
        Wonder = new WonderPickEval(Data.Index, Routes);
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
            // Same reason: the gap finder reads evolves-from out of the facts table, so one built
            // over the empty table would report a collection with no chains in it at all.
            _evolution = null;
            _gapsRevision = -1;
            _fillKeysRevision = -1;
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
        if (undoable) _history.Record(State);

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
        // The one undoable step that legitimately changes preferences as well.
        _history.Record(State, includesPrefs: true);

        State = imported;
        CollectionRevision++;
        RebuildCollection();
        QueueSave();
        Changed?.Invoke();
    }

    public void Undo() => Apply(_history.Undo(State));
    public void Redo() => Apply(_history.Redo(State));

    private void Apply(AppState? restored)
    {
        if (restored is null) return;

        State = restored;
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

    /// <summary>
    /// Whether this card has earned its gold flair: ten copies of a one- to three-diamond card,
    /// granted by the game automatically. Four-diamond cards and promos do not get it.
    ///
    /// Per PRINTING, not per identity, because the game grants it per card — owning five of the
    /// plain print and five of the alternate art earns neither of them anything.
    /// </summary>
    public bool HasGoldFlair(PocketCard card)
    {
        if (Data is null) return false;

        var rung = Index.Ladder.IndexOf(card.Rarity);
        if (rung is null) return false;

        var r = Index.Ladder.Rungs[rung.Value];
        return GameRules.EarnsGoldFlair(r.Group, r.Count, Owned.Of(card), card.IsPromo);
    }

    // ---- decks ------------------------------------------------------------------------

    /// <summary>
    /// Saved decks, ordered closest-to-buildable first.
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

    // ---- chase lists --------------------------------------------------------------------

    /// <summary>
    /// Saved chase lists, priced by the same odds engine that prices a rarity target.
    /// </summary>
    public IReadOnlyList<ChaseList> ChaseLists => Profile.ChaseLists;

    public ChaseList? ChaseListById(string id) => Profile.ChaseLists.FirstOrDefault(w => w.Id == id);

    /// <summary>Creates an empty list and returns its id, so the caller can navigate to it.</summary>
    public string CreateChaseList(string? name = null)
    {
        var id = Guid.NewGuid().ToString("n")[..8];
        var chosen = string.IsNullOrWhiteSpace(name)
            ? ChaseListEdit.FreshName(Profile.ChaseLists)
            : name.Trim();

        Mutate(p => p with { ChaseLists = [.. p.ChaseLists, new ChaseList(id, chosen, new())] });
        return id;
    }

    /// <summary>
    /// The name a fresh hearts list gets. Not "Chase list 2": the point of the separate list is that
    /// it is recognisable as the one the hearts fill, and it reads as a sentence on the chase lists
    /// page beside lists you named yourself.
    /// </summary>
    public const string WantListName = "Want it";

    /// <summary>
    /// The chase list the grid's hearts write to, or null if there is not one yet.
    ///
    /// Resolved through the stored id every time rather than cached: the list can be renamed,
    /// deleted from the chase lists page, or arrive from an import, and a cached reference would
    /// outlive all three. A stored id whose list has gone reads as "no list", which is what makes
    /// deleting it safe.
    /// </summary>
    public ChaseList? WantList =>
        Profile.WantListId is { Length: > 0 } id
            ? Profile.ChaseLists.FirstOrDefault(w => w.Id == id)
            : null;

    /// <summary>
    /// The hearts list, made if this is the first heart.
    ///
    /// The create and the card that follows it are two mutations, which the undo stack coalesces:
    /// checked in the running app, one undo after a first heart leaves no list behind rather than
    /// an empty one.
    /// </summary>
    public string EnsureWantList()
    {
        if (WantList is { } existing) return existing.Id;

        var id = Guid.NewGuid().ToString("n")[..8];
        Mutate(p => p with
        {
            ChaseLists = [.. p.ChaseLists, new ChaseList(id, WantListName, new())],
            WantListId = id,
        });
        return id;
    }

    /// <summary>Copies of a card wanted on the hearts list. Zero when there is no such list yet.</summary>
    public int WantedOnHeartList(string ownershipKey) =>
        WantList?.Wanted.GetValueOrDefault(ownershipKey) ?? 0;

    /// <summary>
    /// Toggle a card on the hearts list: one copy, on or off. Wanting four of something is a
    /// chase list-page question, and a heart that cycled through counts would give no way to see
    /// what it landed on.
    /// </summary>
    public void ToggleWanted(string ownershipKey)
    {
        var id = EnsureWantList();
        SetWanted(id, ownershipKey, WantedOnHeartList(ownershipKey) > 0 ? 0 : 1);
    }

    public void RenameChaseList(string id, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return;

        UpdateChaseList(id, w => w with { Name = trimmed });
    }

    public void DeleteChaseList(string id) =>
        Mutate(p => p with { ChaseLists = p.ChaseLists.Where(w => w.Id != id).ToList() });

    /// <summary>Add or subtract wanted copies. Zero removes the card from the list.</summary>
    public void BumpWanted(string id, string ownershipKey, int delta) =>
        UpdateChaseList(id, w => ChaseListEdit.Bump(w, ownershipKey, delta));

    public void SetWanted(string id, string ownershipKey, int copies) =>
        UpdateChaseList(id, w => ChaseListEdit.SetWanted(w, ownershipKey, copies));

    /// <summary>
    /// Add many cards to a chase list in ONE change: one undo step, one save, one re-render.
    /// Adding "all diamonds in A1" a card at a time would be 226 mutations, 226 undo entries and
    /// 226 debounced saves — and undoing it would take 226 presses.
    /// </summary>
    /// <param name="copies">
    /// Applied as a floor, never a clobber: a card already wanted three times stays at three, so
    /// a bulk sweep cannot quietly undo a deliberate choice.
    /// </param>
    public void AddToChaseList(string id, IEnumerable<string> ownershipKeys, int copies = 1)
    {
        var keys = ownershipKeys.Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length == 0) return;

        UpdateChaseList(id, list =>
        {
            var wanted = new Dictionary<string, int>(list.Wanted);
            foreach (var key in keys)
                wanted[key] = Math.Clamp(
                    Math.Max(wanted.GetValueOrDefault(key), copies), 1, ChaseListEdit.MaxCopies);

            return list with { Wanted = wanted };
        });
    }

    private void UpdateChaseList(string id, Func<ChaseList, ChaseList> change) =>
        Mutate(p => p with
        {
            ChaseLists = p.ChaseLists.Select(w => w.Id == id ? change(w) : w).ToList()
        });

    /// <summary>
    /// Copies still needed across a whole chase list. Counted per printing, matching how the
    /// list itself is keyed.
    /// </summary>
    public int MissingInChaseList(ChaseList list) =>
        list.Wanted.Sum(kv => Math.Max(0, kv.Value - Owned[kv.Key]));

    public int WantedTotal(ChaseList list) => list.Wanted.Values.Sum();

    public ICompletionTarget TargetForChaseList(ChaseList list) =>
        new ChaseListTarget(list.Name, list.Wanted);

    /// <summary>Cards on a chase list, rarest first — how a chase list is usually read.</summary>
    public IReadOnlyList<(PocketCard Card, int Wanted, int Owned)> ChaseListCards(ChaseList list)
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

    // ---- wonder pick ------------------------------------------------------------------

    /// <summary>Take-or-skip appraisal for a Wonder Pick offer. Null until data has loaded.</summary>
    public WonderPickEval? Wonder { get; private set; }

    /// <summary>Wonder Stamina the user last told us they had. Capped, never negative.</summary>
    public int WonderStamina => Math.Clamp(Profile.Resources.Wonder.Balance, 0, GameRules.StaminaCap);

    public void SetWonderStamina(int balance) =>
        Mutate(p => p with
        {
            Resources = p.Resources with
            {
                // Restamped, because the pool regenerates on a clock: a balance without the moment
                // it was true would have regeneration added to it that has not happened yet.
                Wonder = (p.Resources.Wonder with
                {
                    Balance = Math.Clamp(balance, 0, GameRules.StaminaCap)
                }).AsOfNow(DateTimeOffset.Now)
            }
        }, undoable: false);

    // ---- The three resource systems ---------------------------------------------------------
    // Pack, Wonder and Trade each have their own pool and their own hourglass, and nothing
    // exchanges between them. Three projections, never a total.

    public PoolOutlook WonderOutlook => ResourcePlan.Project(
        Profile.Resources.Wonder, DateTimeOffset.Now,
        GameRules.StaminaCap, GameRules.WonderHourglassesPerStamina);

    public PoolOutlook TradeOutlook => ResourcePlan.Project(
        Profile.Resources.Trade, DateTimeOffset.Now,
        GameRules.StaminaCap, GameRules.TradeHourglassesPerStamina);

    public PackOutlook PackOutlook => ResourcePlan.Packs(Profile.Resources, DateTimeOffset.Now);

    /// <summary>
    /// Record a stamina pool as it stands now. Balance and hourglasses are written together
    /// because they are read off the same screen at the same moment, and the timestamp they share
    /// is what makes the projection meaningful.
    /// </summary>
    public void SetPool(bool trade, int balance, int hourglasses)
    {
        var pool = new ResourcePool(
            Math.Clamp(balance, 0, GameRules.StaminaCap),
            Math.Max(0, hourglasses)).AsOfNow(DateTimeOffset.Now);

        Mutate(p => p with
        {
            Resources = trade
                ? p.Resources with { Trade = pool }
                : p.Resources with { Wonder = pool }
        }, undoable: false);
    }

    /// <summary>
    /// The trade queue, bound to the current card data. Memoised like the other engines: it walks
    /// every ownable card to find the surplus pool.
    /// </summary>
    public TradeQueue? Trades =>
        Data is null || Odds is null ? null : _trades ??= new TradeQueue(Index, Odds, Data.Rarities);

    private TradeQueue? _trades;

    /// <summary>
    /// Trades ranked for a target, best first. The plan resolver is per set, since targets are.
    /// </summary>
    public IReadOnlyList<TradeCandidate> RankTrades(ICompletionTarget target, int max = 20) =>
        Trades?.Rank(target.Outstanding(Index, Owned), Owned, Profile.Resources,
                     set => Profile.Targets.PlanFor(set), max)
        ?? [];

    /// <summary>
    /// The in-game wishlist advisor, bound to current card data. Memoised: it prices every
    /// outstanding card by every route.
    /// </summary>
    public TradeBoardAdvisor? Board =>
        Data is null || Odds is null || Routes is null || Trades is null
            ? null
            : _board ??= new TradeBoardAdvisor(Index, Routes, Trades, Odds, Sets);

    private TradeBoardAdvisor? _board;

    /// <summary>
    /// Board settings, persisted. Everything that changes WHAT is recommended has to be, or
    /// reopening the page would report swaps caused by a forgotten setting rather than by the
    /// collection changing.
    /// </summary>
    public int BoardLiquidSlots => Math.Clamp(State.Prefs.BoardLiquidSlots, 0, GameRules.TradeBoardSlots);

    public double BoardMinimumCost => Math.Max(0, State.Prefs.BoardMinimumCost);

    public string BoardFoils =>
        State.Prefs.BoardFoils is "without" or "only" ? State.Prefs.BoardFoils : "with";

    public void SetBoardSettings(int? liquidSlots = null, double? minimumCost = null, string? foils = null)
    {
        State = State with
        {
            Prefs = State.Prefs with
            {
                BoardLiquidSlots = liquidSlots ?? State.Prefs.BoardLiquidSlots,
                BoardMinimumCost = minimumCost ?? State.Prefs.BoardMinimumCost,
                BoardFoils = foils ?? State.Prefs.BoardFoils
            }
        };
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>Ownership keys currently on the in-game board.</summary>
    public IReadOnlyList<string> TradeBoard => Profile.TradeBoard;

    /// <summary>
    /// Record what is on the board now, after the change has been made IN THE GAME. Capped at the
    /// game's own twenty, since a longer list could not be entered.
    /// </summary>
    public void SetTradeBoard(IEnumerable<string> ownershipKeys) =>
        Mutate(p => p with
        {
            TradeBoard = ownershipKeys.Distinct(StringComparer.OrdinalIgnoreCase)
                                      .Take(GameRules.TradeBoardSlots)
                                      .ToList()
        }, undoable: false);

    /// <summary>
    /// What the board should hold for a target. Today's date is passed in rather than read inside
    /// the engine so the recommendation stays reproducible.
    /// </summary>
    /// <param name="foils">
    /// with / without / only. Filtered here rather than in the advisor, because it is a question
    /// about the game's interface — whether a foil printing can be wishlisted in the game at all — rather than
    /// about what a slot is worth. Filtering the demands before ranking also keeps the twenty slots
    /// full: excluding foils afterwards would leave gaps.
    /// </param>
    public BoardPlan? RecommendBoard(
        ICompletionTarget target, int liquidSlots, double minimumCost, string foils = "with")
    {
        if (Board is null) return null;

        var demands = target.Outstanding(Index, Owned);

        if (foils is "without" or "only" && Odds is { } odds)
        {
            var keys = odds.FoilOwnershipKeys;
            var wantFoil = foils == "only";
            demands = demands
                .Where(d => keys.Contains(d.SuppliedBy[0].OwnershipKey) == wantFoil)
                .ToArray();
        }

        return Board.Recommend(
            demands, Owned,
            set => Profile.Targets.PlanFor(set),
            TradeBoard,
            DateOnly.FromDateTime(DateTime.Now),
            liquidSlots, minimumCost);
    }

    /// <summary>Pack-point balances per set, as tracked and as told to us.</summary>
    public IReadOnlyDictionary<string, int> PointsBySet => Profile.Resources.PackPointsBySet;

    /// <summary>
    /// Correct one set's pack-point balance.
    ///
    /// Needed because points accrue only from packs logged HERE, while the real balance has been
    /// running since the account started. Per set and never in aggregate: points are earned by
    /// opening one set's packs and can be spent nowhere else, so a single total would describe a
    /// currency the game does not have.
    /// </summary>
    public void SetPackPoints(string set, int points) =>
        Mutate(p =>
        {
            var next = new Dictionary<string, int>(p.Resources.PackPointsBySet);
            var value = Math.Clamp(points, 0, GameRules.PackPointsCap);

            if (value == 0) next.Remove(set); else next[set] = value;

            return p with { Resources = p.Resources with { PackPointsBySet = next } };
        }, undoable: false);

    /// <summary>
    /// The pack side and the dust balance. Premium is here rather than in Prefs because it is a
    /// fact about an account, and profiles are accounts - an alt without premium must not inherit
    /// the main's pack rate.
    /// </summary>
    public void SetPackResources(int packHourglasses, int shinedust, bool premium) =>
        Mutate(p => p with
        {
            Resources = p.Resources with
            {
                PackHourglasses = Math.Max(0, packHourglasses),
                Shinedust = Math.Max(0, shinedust),
                Premium = premium
            }
        }, undoable: false);

    /// <summary>
    /// Record an offer that was evaluated, taken or not.
    ///
    /// Offers SEEN are logged, not just offers taken: the reservation threshold is a percentile
    /// of the distribution of what turns up, and a log of accepted offers only would be biased
    /// upward by the very policy it sets.
    /// </summary>
    public void LogWonderOffer(IReadOnlyList<PocketCard> offer, int staminaCost, bool taken, PocketCard? received)
    {
        var keys = offer.Select(c => c.OwnershipKey).ToList();

        Mutate(p =>
        {
            var log = new List<WonderOfferEvent>(p.WonderLog)
            {
                new(DateTimeOffset.Now, keys, staminaCost, taken, received?.OwnershipKey)
            };

            var next = p with { WonderLog = log };

            // Taking one spends the stamina and adds the card, so the collection and the pool
            // both follow from the same action rather than needing three separate edits.
            if (taken)
            {
                next = next with
                {
                    Resources = next.Resources with
                    {
                        // Spending restamps the pool too: the balance is true as of now, and
                        // leaving the old timestamp would credit the regeneration twice.
                        Wonder = (next.Resources.Wonder with
                        {
                            Balance = Math.Max(0, next.Resources.Wonder.Balance - staminaCost)
                        }).AsOfNow(DateTimeOffset.Now)
                    }
                };

                if (received is not null)
                {
                    var collection = new Dictionary<string, int>(next.Collection);
                    collection[received.OwnershipKey] =
                        collection.GetValueOrDefault(received.OwnershipKey) + 1;
                    next = next with { Collection = collection };
                }
            }

            return next;
        });
    }

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

    // Column counts are persisted preferences rather than per-visit view state: someone who wants
    // 8 columns wants them on every visit. Zero in the save means "never chosen", so the defaults
    // live here rather than in the schema, which keeps adding the field free of a migration.

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

    /// <summary>The game's lifetime counters as entered, or null if never entered.</summary>
    public LifetimeTotals? Lifetime => Profile.Lifetime;

    /// <summary>
    /// Record what the game's profile screen says. Read AS OF NOW: anything logged here after
    /// this moment adds to it, anything before is already inside the number the game gave.
    /// Passing zero for both clears the baseline.
    /// </summary>
    public void SetLifetime(int packs, int wonderPicks)
    {
        var cleared = packs <= 0 && wonderPicks <= 0;
        Mutate(p => p with
        {
            Lifetime = cleared
                ? null
                : new LifetimeTotals(Math.Max(0, packs), Math.Max(0, wonderPicks),
                                     DateTimeOffset.Now)
        });
    }

    /// <summary>
    /// Packs opened in total: the game's figure plus everything logged since it was read. Falls
    /// back to the logged count alone when no baseline exists.
    ///
    /// Events are counted by timestamp rather than all of them, because the baseline is a
    /// snapshot: a player who logs for a week and only then reads the game's counter would
    /// otherwise have that week counted twice.
    /// </summary>
    public int LifetimePacks => Lifetime is { } b
        ? b.PacksWith(Profile.PackLog)
        : Profile.PackLog.Count;

    /// <summary>
    /// Wonder Picks taken in total. Only TAKEN offers count: the log records every offer seen,
    /// which is what makes the reservation threshold learnable, but the game counts the ones
    /// you actually spent stamina on.
    /// </summary>
    public int LifetimeWonderPicks => Lifetime is { } b
        ? b.WonderPicksWith(Profile.WonderLog)
        : Profile.WonderLog.Count(e => e.Taken);

    /// <summary>
    /// Splits a lifetime pack count across sets from what the collection holds. Null until card
    /// data and odds are loaded; memoised, since it indexes every priceable pack.
    /// </summary>
    public PackMixEstimator? PackMix =>
        Data is null || Odds is null ? null : _packMix ??= new PackMixEstimator(Index, Odds, Sets);

    private PackMixEstimator? _packMix;

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

    public bool ChaseGrid => State.Prefs.ChaseGrid;

    public void SetChaseGrid(bool on)
    {
        if (on == State.Prefs.ChaseGrid) return;
        State = State with { Prefs = State.Prefs with { ChaseGrid = on } };
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>
    /// Packs pinned to the front of the log picker, in the order they were pinned.
    /// </summary>
    public IReadOnlyList<string> PinnedPacks => State.Prefs.PinnedPacks;

    public bool IsPackPinned(string packKey) => State.Prefs.PinnedPacks.Contains(packKey);

    /// <summary>
    /// Pin or unpin a pack. Pinning appends rather than inserts, so the pins keep the order they
    /// were made in and an existing pin does not move when a second one is added.
    /// </summary>
    public void SetPackPinned(string packKey, bool pinned)
    {
        if (string.IsNullOrWhiteSpace(packKey) || pinned == IsPackPinned(packKey)) return;

        var next = State.Prefs.PinnedPacks.Where(p => p != packKey).ToList();
        if (pinned) next.Add(packKey);

        State = State with { Prefs = State.Prefs with { PinnedPacks = next } };
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

    /// <summary>
    /// Foil printings — currently the Deluxe set's second prints of its 1-3 diamond cards — that
    /// a target should ignore. Null when the user collects them, which is the default.
    /// </summary>
    /// <summary>
    /// The parallel-foil policy for a target. Public so a page building its own targets — the
    /// rarity advisor does — applies the same choice: two places deciding what a target contains
    /// is how two figures on one screen come to disagree.
    /// </summary>
    public FoilPolicy? FoilRule =>
        Odds is null ? null : new FoilPolicy(Odds.FoilOwnershipKeys, FoilCopies);

    /// <summary>Copies wanted of each parallel foil. Zero means they are not collected.</summary>
    public int FoilCopies => Math.Clamp(State.Prefs.FoilCopies, 0, MaxFoilCopies);

    /// <summary>
    /// Two, matching the rarity chips: beyond two copies of a printing is spare stock rather
    /// than a want.
    /// </summary>
    public const int MaxFoilCopies = 2;

    /// <summary>
    /// Where the next tap lands: none → 1 → 2 → none, exactly as a rarity chip cycles.
    ///
    /// Exposed rather than kept inside <see cref="CycleFoilCopies"/> because a caller that names
    /// the change before making it — the pack ranker says "Counting 2 of each parallel foil…"
    /// while it re-ranks — needs the answer up front, and computing it there is how this rule came
    /// to be written out in three places that could disagree.
    /// </summary>
    public int NextFoilCopies => FoilCopies >= MaxFoilCopies ? 0 : FoilCopies + 1;

    /// <summary>none → 1 → 2 → none, exactly as a rarity chip cycles.</summary>
    public void CycleFoilCopies() => SetFoilCopies(NextFoilCopies);

    /// <summary>Sets that have foils at all, so the choice is only offered where it applies.</summary>
    public IReadOnlyList<string> FoilSets =>
        Odds is null ? [] : Odds.SetsWithFoils.OrderBy(s => s, StringComparer.Ordinal).ToArray();

    public void SetFoilCopies(int copies)
    {
        var next = Math.Clamp(copies, 0, MaxFoilCopies);
        if (next == FoilCopies) return;

        State = State with { Prefs = State.Prefs with { FoilCopies = next } };
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>The active completion plan for one set, from the user's settings.</summary>
    public ICompletionTarget TargetForSet(string set) =>
        new RarityLadderTarget(set, Profile.Targets.PlanFor(set), FoilRule);

    /// <summary>
    /// Cards wanted across these sets and how many are satisfied, using each set's OWN plan and
    /// the parallel-foil setting.
    ///
    /// Asked of the target rather than recomputed, because the page that recomputed it got two
    /// things wrong at once: it counted every parallel foil toward the total even when the foil
    /// setting wanted none of them, and when several sets were in view it compared them all
    /// against the DEFAULT plan while totalling them against their own.
    /// </summary>
    public (int Wanted, int Satisfied) TargetProgress(IEnumerable<string> sets)
    {
        var wanted = 0;
        var satisfied = 0;

        foreach (var set in sets)
        {
            var (w, s) = new RarityLadderTarget(set, Plan(set), FoilRule).Progress(Index, Owned);
            wanted += w;
            satisfied += s;
        }

        return (wanted, satisfied);
    }

    /// <summary>
    /// Copies of one card the plan for its own set asks for, foil setting included. Zero means it
    /// is not collected, which is what the "short of target" filter has to respect.
    /// </summary>
    public int RequiredCopies(PocketCard card) =>
        new RarityLadderTarget(card.Set, Plan(card.Set), FoilRule).Required(Index, card);

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

    /// <summary>
    /// A scope string resolved to the target it names.
    ///
    /// Four pages let you pick what to rank against, and four pages had written their own version
    /// of this. They had drifted: two spelled a set "set:A1" and two spelled it "A1", only one
    /// knew about "chases" and "series:", and a scope one page could produce was a scope another
    /// read as "everything". The grammars do not collide, so one reader accepts all of them.
    ///
    /// Anything unresolvable — a chase list since deleted, a series with no openable sets left —
    /// falls back to everything rather than to an empty target, which would read as "nothing left
    /// to collect". Callers that also need the dropdown to stop showing the dead option run
    /// <see cref="NormalizeScope"/> over the string first.
    /// </summary>
    public ICompletionTarget TargetForScope(string scope)
    {
        if (scope.StartsWith("chase:", StringComparison.Ordinal))
            return ChaseListById(scope[5..]) is { } list
                ? TargetForChaseList(list)
                : TargetForEverything();

        // Merged, not summed: CompositeTarget takes the LARGEST requirement per card, so a card
        // on two lists is still one card.
        if (scope == "chases")
            return ChaseLists.Count > 0
                ? new CompositeTarget(ChaseLists.Select(TargetForChaseList).ToArray(), "all chase lists")
                : TargetForEverything();

        if (scope.StartsWith("series:", StringComparison.Ordinal))
        {
            var sets = SetsInSeries(scope[7..]);
            return sets.Length > 0
                ? new CompositeTarget(sets.Select(TargetForSet).ToArray(), $"series {scope[7..]}")
                : TargetForEverything();
        }

        var set = scope.StartsWith("set:", StringComparison.Ordinal) ? scope[4..] : scope;
        return scope == "everything" ? TargetForEverything() : TargetForSet(set);
    }

    /// <summary>
    /// The same scope, or "everything" when what it named is gone. A picker showing a deleted
    /// chase list keeps offering it; resolving to a fallback alone would leave the control and the
    /// results disagreeing about what is being ranked.
    /// </summary>
    public string NormalizeScope(string scope) =>
        scope.StartsWith("chase:", StringComparison.Ordinal) && ChaseListById(scope[5..]) is null ? "everything"
        : scope == "chases" && ChaseLists.Count == 0 ? "everything"
        : scope.StartsWith("series:", StringComparison.Ordinal) && SetsInSeries(scope[7..]).Length == 0 ? "everything"
        : scope;

    /// <summary>The openable sets of one series, in the order the pickers list them.</summary>
    public string[] SetsInSeries(string series) =>
        Sets.SetsIn(series).Where(Index.OpenableSets.Contains).ToArray();

    // ---- Storage health -------------------------------------------------------------
    // Each of these is a state the app announces rather than absorbs: a tracker whose writes are
    // failing looks identical to one that is working, until the reload.

    /// <summary>True once a write has been refused, which means edits are not being kept.</summary>
    public bool SaveFailed { get; private set; }

    /// <summary>True when the browser provides no storage, so nothing survives a reload.</summary>
    public bool StorageUnavailable => Diagnostics?.StorageUnavailable == true;

    /// <summary>True when a saved payload was found that could not be read.</summary>
    public bool LoadFailed => Diagnostics?.LoadFailed == true;

    /// <summary>
    /// The concrete browser store, when that is what is in use. Cast rather than added to
    /// IStateStore: these are facts about localStorage, and a future sync backend would report
    /// entirely different ones.
    /// </summary>
    private LocalStorageStateStore? Diagnostics => _store as LocalStorageStateStore;

    private void RecordSaveResult(bool ok)
    {
        if (ok == !SaveFailed) return;

        SaveFailed = !ok;
        Changed?.Invoke();
    }

    /// <summary>Whether the Collection page shows the evolution-gap strip.</summary>
    public bool ShowGapStrip => State.Prefs.ShowEvolutionGaps;

    public void SetShowGapStrip(bool show)
    {
        if (show == ShowGapStrip) return;

        State = State with { Prefs = State.Prefs with { ShowEvolutionGaps = show } };
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>Whether Wonder Pick still shows the paragraph explaining what it decides.</summary>
    public bool ShowWonderIntro => State.Prefs.ShowWonderIntro;

    public void SetShowWonderIntro(bool show)
    {
        if (show == ShowWonderIntro) return;

        State = State with { Prefs = State.Prefs with { ShowWonderIntro = show } };
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>
    /// Evolution chains you own part of. Memoised over the card data; the REPORT is cached
    /// separately, since it depends on the collection.
    /// </summary>
    public EvolutionGaps? Evolution =>
        Data is null || Odds is null ? null : _evolution ??= new EvolutionGaps(Index, Facts, Odds);

    private EvolutionGaps? _evolution;

    private EvolutionReport? _gaps;
    private int _gapsRevision = -1;

    /// <summary>
    /// The gap report for the collection as it stands, recomputed only when the collection
    /// actually changes. It walks every ownable card, so running it per render - which a grid page
    /// does dozens of times a second while sweeping - would be felt.
    /// </summary>
    public EvolutionReport? Gaps
    {
        get
        {
            if (Evolution is null) return null;
            if (_gapsRevision == CollectionRevision) return _gaps;

            _gaps = Evolution.Find(Owned);
            _gapsRevision = CollectionRevision;
            return _gaps;
        }
    }

    /// <summary>
    /// Ownership keys of every printing that would close a chain, for the collection filter. Every
    /// printing, not just the easiest: any of them works, and someone opening a particular pack
    /// wants to know which of its cards would do.
    /// </summary>
    public IReadOnlySet<string> GapFillKeys
    {
        get
        {
            // Cached with the report, not rebuilt per call. The collection filter asks this once
            // per card, so building the set inside the property put a walk of every gap's every
            // printing inside a 3,546-iteration loop.
            if (_fillKeysRevision == CollectionRevision && _fillKeys is not null) return _fillKeys;

            _fillKeys = Gaps is null
                ? []
                : Gaps.Gaps.SelectMany(g => g.Candidates).Select(c => c.OwnershipKey).ToHashSet();
            _fillKeysRevision = CollectionRevision;
            return _fillKeys;
        }
    }

    private HashSet<string>? _fillKeys;
    private int _fillKeysRevision = -1;

    // ---- Profiles -------------------------------------------------------------------
    // Alt accounts are common in PTCGP and you can trade with yourself, so a second collection is
    // ordinary. Everything below edits AppState directly rather than going through Mutate, which
    // only ever touches the active profile.

    public IReadOnlyList<Profile> Profiles => State.Profiles;

    public string ActiveProfileId => State.ActiveProfileId;

    public bool MultipleProfiles => State.Profiles.Count > 1;

    /// <summary>
    /// Switch collections. Clears undo: the history holds whole app states, so an undo taken
    /// afterwards would restore the other profile, silently switching back and discarding whatever
    /// was just done here.
    /// </summary>
    public void SwitchProfile(string id)
    {
        if (id == State.ActiveProfileId || State.Profiles.All(p => p.Id != id)) return;

        _history.Clear();

        State = State with { ActiveProfileId = id };
        CollectionRevision++;
        RebuildCollection();
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>
    /// A new, empty collection. It inherits the current profile's completion plan rather than the
    /// stock one, since someone adding an alt has already said what they collect.
    /// </summary>
    public string CreateProfile(string name, bool copyPlan = true)
    {
        var id = Guid.NewGuid().ToString("n")[..8];
        var profile = Profile.NewDefault(id, Clean(name, "New collection"));

        if (copyPlan)
        {
            profile = profile with
            {
                Targets = new TargetSettings(
                    new Dictionary<int, int>(Profile.Targets.DefaultPlan),
                    Profile.Targets.PlanBySet.ToDictionary(
                        kv => kv.Key, kv => new Dictionary<int, int>(kv.Value)))
            };
        }

        AddProfile(profile);
        return id;
    }

    /// <summary>
    /// A new collection holding counts read from another tracker's export, switched to at once.
    ///
    /// The default destination for an import, because it is the only one that cannot lose
    /// anything: whatever was here before is still here, under its own name, and a bad import is
    /// undone by deleting the collection it made rather than by trusting an undo stack.
    ///
    /// Switching clears the undo history — see <see cref="SwitchProfile"/> — so this deliberately
    /// offers no undo, and the caller should not imply one. There is nothing to undo.
    /// </summary>
    public string ImportAsNewProfile(string name, IReadOnlyDictionary<string, int> counts)
    {
        var id = CreateProfile(name);

        State = State with
        {
            Profiles = State.Profiles
                .Select(p => p.Id == id
                    ? p with { Collection = counts.ToDictionary(kv => kv.Key, kv => kv.Value) }
                    : p)
                .ToList()
        };

        // Not a plain assignment to ActiveProfileId: switching is what clears undo and rebuilds
        // the derived collection, and doing half of it here is how the two drift apart.
        SwitchProfile(id);
        return id;
    }

    /// <summary>
    /// Copy a whole collection, cards and all — for trying a different plan or a different set of
    /// decks without touching the real numbers.
    /// </summary>
    public string DuplicateProfile(string id, string? name = null)
    {
        var source = State.Profiles.FirstOrDefault(p => p.Id == id);
        if (source is null) return State.ActiveProfileId;

        var newId = Guid.NewGuid().ToString("n")[..8];

        // Every collection here is a fresh dictionary or list, not a shared reference: the two
        // profiles would otherwise edit the same collection and appear to change together.
        var copy = source with
        {
            Id = newId,
            Name = Clean(name, $"{source.Name} copy"),
            Collection = new Dictionary<string, int>(source.Collection),
            Decks = [.. source.Decks],
            ChaseLists = [.. source.ChaseLists],
            PackLog = [.. source.PackLog],
            WonderLog = [.. source.WonderLog],
            TradeBoard = [.. source.TradeBoard],
            Targets = new TargetSettings(
                new Dictionary<int, int>(source.Targets.DefaultPlan),
                source.Targets.PlanBySet.ToDictionary(
                    kv => kv.Key, kv => new Dictionary<int, int>(kv.Value)))
        };

        AddProfile(copy);
        return newId;
    }

    public void RenameProfile(string id, string name)
    {
        var cleaned = Clean(name, null);
        if (cleaned is null) return;

        State = State with
        {
            Profiles = State.Profiles
                .Select(p => p.Id == id ? p with { Name = cleaned } : p).ToList()
        };
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>
    /// The player's name in the game for this collection. Empty clears it, because "I typed it
    /// and I want it gone" is a real intent and there is no other control that could express it —
    /// unlike the profile name, which must always be something.
    /// </summary>
    public void SetInGameName(string id, string? name)
    {
        var cleaned = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        State = State with
        {
            Profiles = State.Profiles
                .Select(p => p.Id == id ? p with { InGameName = cleaned } : p).ToList()
        };
        QueueSave();
        Changed?.Invoke();
    }

    /// <summary>
    /// Delete a collection. Refuses the last one: an app with no profile has nowhere to put a
    /// tap, and the state model has no way to express it.
    /// </summary>
    public bool DeleteProfile(string id)
    {
        if (State.Profiles.Count <= 1 || State.Profiles.All(p => p.Id != id)) return false;

        var remaining = State.Profiles.Where(p => p.Id != id).ToList();

        // Deleting cannot be undone through the undo stack, because the stack is cleared on the
        // switch that follows. The UI says so rather than half-supporting it here.
        _history.Clear();

        var wasActive = State.ActiveProfileId == id;
        State = State with
        {
            Profiles = remaining,
            ActiveProfileId = wasActive ? remaining[0].Id : State.ActiveProfileId
        };

        if (wasActive)
        {
            CollectionRevision++;
            RebuildCollection();
        }

        QueueSave();
        Changed?.Invoke();
        return true;
    }

    private void AddProfile(Profile profile)
    {
        State = State with { Profiles = [.. State.Profiles, profile] };
        QueueSave();
        Changed?.Invoke();
    }

    private static string Clean(string? name, string? fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback! : name.Trim();

    /// <summary>
    /// The self-trade finder. Memoised like the other engines - it walks every ownable card twice,
    /// once per side.
    /// </summary>
    public ProfileDiff? Diff =>
        Data is null || Odds is null || Trades is null
            ? null
            : _diff ??= new ProfileDiff(Index, Odds, Data.Rarities, Trades);

    private ProfileDiff? _diff;

    /// <summary>
    /// Swaps between the active profile and another, from the active profile's point of view.
    /// </summary>
    public ProfileSwaps? CompareWith(string otherId)
    {
        if (Diff is null) return null;

        var other = State.Profiles.FirstOrDefault(p => p.Id == otherId);
        if (other is null || other.Id == Profile.Id) return null;

        return Diff.Compare(SideFor(Profile), SideFor(other));
    }

    /// <summary>
    /// One profile as the diff needs it, using its own plan and collection throughout. Reading the
    /// active profile's plan for both sides is wrong: an alt farmed for diamonds only would have
    /// its stars counted as still wanted, so the main would never be offered them.
    /// </summary>
    private DiffSide SideFor(Profile profile)
    {
        var owned = new Collection(profile.Collection);

        // Promo sets are excluded along with the rest of the app's "everything", and it costs
        // nothing here: promos are trade-ineligible, so no swap could involve one either way.
        var parts = Index.OpenableSets
            .Select(set => (ICompletionTarget)new RarityLadderTarget(
                set, profile.Targets.PlanFor(set), FoilRule))
            .ToArray();

        var outstanding = new CompositeTarget(parts).Outstanding(Index, owned);

        return new DiffSide(profile.Id, profile.Name, owned,
                            set => profile.Targets.PlanFor(set), outstanding, profile.Resources);
    }

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
                bool ok;
                try { ok = await _store.SaveAsync(State, ct); }
                finally { _saveGate.Release(); }

                // Outside the gate. RecordSaveResult raises Changed, which runs page code, and
                // notifying subscribers while holding a lock they could re-enter risks a deadlock.
                RecordSaveResult(ok);
            }
            catch (OperationCanceledException) { /* superseded by a later edit */ }
        }, ct);
    }

    /// <summary>Force an immediate write, e.g. before export.</summary>
    public async Task FlushAsync()
    {
        _pendingSave?.Cancel();

        bool ok;
        await _saveGate.WaitAsync();
        try { ok = await _store.SaveAsync(State); }
        finally { _saveGate.Release(); }

        RecordSaveResult(ok);
    }

    public async ValueTask DisposeAsync()
    {
        _pendingSave?.Cancel();
        try { await FlushAsync(); } catch { /* best effort on teardown */ }
        _saveGate.Dispose();
    }
}
