namespace PackProphet.State;

using System.Text.Json;
using System.Text.Json.Serialization;

using PackProphet.Deck;

/// <summary>
/// Reads and writes the persisted state, and owns migration. Also the export/import format, so a
/// user can always get their data out of a local-only app.
/// </summary>
public static class StateSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ExportOptions = new(Options)
    {
        WriteIndented = true,
    };

    public static string Serialize(AppState state) => JsonSerializer.Serialize(state, Options);

    /// <summary>Human-readable form for the export file.</summary>
    public static string Export(AppState state) => JsonSerializer.Serialize(state, ExportOptions);

    /// <summary>
    /// Parse persisted state, migrating older schemas. Returns null when the payload is not
    /// recoverable, so the caller can start fresh rather than crash on boot.
    /// </summary>
    public static AppState? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var state = JsonSerializer.Deserialize<AppState>(json, Options);
            if (state is null || state.Profiles is null || state.Profiles.Count == 0) return null;

            return Migrate(state);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Bring an older payload up to the current schema. Unknown future versions are refused rather
    /// than guessed at: opening newer data with an older build and dropping fields would destroy
    /// the user's collection on the next save.
    /// </summary>
    private static AppState? Migrate(AppState state)
    {
        if (state.SchemaVersion > AppState.CurrentSchemaVersion) return null;

        if (state.SchemaVersion < 3) state = ToV3(state);
        if (state.SchemaVersion < 4) state = ToV4(state);

        state = Normalise(state);
        if (state.Profiles.Count == 0) return null;

        return state with { SchemaVersion = AppState.CurrentSchemaVersion };
    }

    /// <summary>
    /// Replace every null collection with an empty one, and clamp what cannot legally be negative.
    ///
    /// Every list and dictionary in the state model is non-nullable in C# and the serializer does
    /// not enforce that, so a payload saying <c>"decks": null</c> deserialises to a Profile whose
    /// Decks really is null. The import path then adopts that state, queues it to localStorage, and
    /// the first render throws — after which every reload reads the same payload back and throws
    /// again, leaving an app that cannot be opened without clearing site data by hand.
    ///
    /// Editing a backup, a partial download, or a file written by a future version that renamed a
    /// field all reach this, so it is fixed on read, where boot, import and any future sync go
    /// through one place.
    /// </summary>
    private static AppState Normalise(AppState state)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var profiles = new List<Profile>();

        foreach (var profile in state.Profiles.Where(p => p is not null))
        {
            var id = profile.Id;

            // A blank or duplicated id is worse than a wrong one: Mutate rewrites every profile
            // whose id matches, so two profiles sharing one id would silently edit together.
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
            {
                id = $"p{profiles.Count + 1}";
                while (!seen.Add(id)) id += "x";
            }

            profiles.Add(Normalise(profile, id));
        }

        var active = profiles.Any(p => p.Id == state.ActiveProfileId)
            ? state.ActiveProfileId
            : profiles.Count > 0 ? profiles[0].Id : "";

        return state with
        {
            Profiles = profiles,
            ActiveProfileId = active,
            Prefs = Normalise(state.Prefs)
        };
    }

    private static Profile Normalise(Profile p, string id) => p with
    {
        Id = id,
        Name = string.IsNullOrWhiteSpace(p.Name) ? "My Collection" : p.Name,

        // Zero and negative counts are dropped rather than kept, matching what Collection does
        // with them at runtime - otherwise the saved state and the live one disagree about how
        // many cards you own.
        Collection = p.Collection is null
            ? new Dictionary<string, int>()
            : p.Collection.Where(kv => kv.Key is { Length: > 0 } && kv.Value > 0)
                          .ToDictionary(kv => kv.Key, kv => kv.Value),

        // ToPlan only runs for pre-v3 payloads, so a v3 file with "targets": null reaches here
        // untouched and every plan lookup on it throws.
        Targets = p.Targets is null || p.Targets.DefaultPlan is null || p.Targets.PlanBySet is null
            ? TargetSettings.Default
            : p.Targets,

        // Card identities and energies are clamped, not merely null-guarded. DeckCodec.Create
        // REFUSES a non-positive identity or more than three energies -- correctly, since neither
        // can be encoded -- and it is called during render to build a deck's share code. So a
        // payload carrying "deckBuilderNrs": [0] throws on the deck page rather than showing a
        // deck with an unresolvable card, which is what the null-guarding here was for.
        //
        // This mattered less when a payload could only come from this browser or a file the user
        // chose. Cloud sync makes remote state an ordinary input, so the parse has to be the place
        // that makes it safe -- and it already is the one funnel that boot, import and sync share.
        Decks = (p.Decks ?? []).Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Id))
            .Select(d => d with
            {
                DeckBuilderNrs = (d.DeckBuilderNrs ?? []).Where(n => n > 0).ToList(),
                Energies = (d.Energies ?? []).Distinct().Take(DeckCodec.MaxEnergyTypes).ToList(),
            })
            .ToList(),

        ChaseLists = (p.ChaseLists ?? []).Where(w => w is not null && !string.IsNullOrWhiteSpace(w.Id))
            .Select(w => w with { Wanted = w.Wanted ?? new Dictionary<string, int>() })
            .ToList(),

        // Ids are filled in here rather than in a migration step, so that a row arriving without
        // one -- a pre-v5 save, a hand-edited backup, a row some future code path forgets to
        // name -- is named once, at the boundary every reader comes through.
        PackLog = WithIds(
            (p.PackLog ?? []).Where(e => e is not null)
                .Select(e => e with { OwnershipKeys = e.OwnershipKeys ?? [] }),
            LogId.Content, (e, id) => e with { Id = id }),

        WonderLog = WithIds(
            (p.WonderLog ?? []).Where(e => e is not null)
                .Select(e => e with { OwnershipKeys = e.OwnershipKeys ?? [] }),
            LogId.Content, (e, id) => e with { Id = id }),

        RemovedLog = (p.RemovedLog ?? []).Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal).ToList(),

        TradeBoard = (p.TradeBoard ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).ToList(),

        Resources = Normalise(p.Resources)
    };

    /// <summary>
    /// Name every row that has no name, from its contents, so that two devices reading their own
    /// copy of the same log independently agree on what to call each row. Rows that already have
    /// an id keep it -- this can never rename a row, only name an unnamed one.
    /// </summary>
    private static List<T> WithIds<T>(
        IEnumerable<T> log, Func<T, string> content, Func<T, string, T> name)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<T>();

        foreach (var row in log)
        {
            var key = content(row);

            // Counted for every row, not only the unnamed ones: the count is a position among
            // identical contents, and skipping the named ones would shift it.
            var occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;

            result.Add(Named(row) ? row : name(row, LogId.Derive(key, occurrence)));
        }

        return result;

        static bool Named(T row) => row switch
        {
            PackOpenEvent e => e.Id is { Length: > 0 },
            WonderOfferEvent e => e.Id is { Length: > 0 },
            _ => false,
        };
    }

    private static Resources Normalise(Resources? r) =>
        r is null
            ? Resources.Empty
            : r with
            {
                Wonder = Normalise(r.Wonder),
                Trade = Normalise(r.Trade),
                PackHourglasses = Math.Max(0, r.PackHourglasses),
                Shinedust = Math.Max(0, r.Shinedust),
                PackPointsBySet = r.PackPointsBySet is null
                    ? new Dictionary<string, int>()
                    : r.PackPointsBySet.Where(kv => kv.Key is { Length: > 0 })
                        .ToDictionary(kv => kv.Key, kv => Math.Max(0, kv.Value))
            };

    private static ResourcePool Normalise(ResourcePool? pool) =>
        pool is null
            ? ResourcePool.Empty
            : pool with
            {
                Balance = Math.Max(0, pool.Balance),
                Hourglasses = Math.Max(0, pool.Hourglasses)
            };

    private static Prefs Normalise(Prefs? prefs) =>
        prefs is null
            ? new Prefs()
            : prefs with
            {
                // Dropped rather than carried: no page reads a saved scope any more, and a value
                // left in the file is one a future reader could pick up as if it still meant
                // something. See Prefs.Target.
                Target = null,
                AssumedRateDonors = prefs.AssumedRateDonors ?? [],
                AvailableLimitedPacks = prefs.AvailableLimitedPacks ?? [],
                PinnedPacks = (prefs.PinnedPacks ?? [])
                    .Where(k => !string.IsNullOrWhiteSpace(k)).ToList()
            };

    /// <summary>
    /// Fold every older completion shape into a single rarity-to-copies plan.
    ///
    ///   v1 stored one threshold ("everything up to tier N") and one copy count.
    ///   v2 stored an arbitrary set of rungs, still with one copy count.
    ///   v3 stores copies PER rung, which subsumes both.
    ///
    /// A v1 "all diamonds" user and a v2 "all diamonds, 2 copies" user both come across with
    /// exactly what they had, now expressed per rung.
    /// </summary>
    private static AppState ToV3(AppState state) => state with
    {
        Profiles = state.Profiles.Select(p => p with { Targets = ToPlan(p.Targets) }).ToList()
    };

    /// <summary>
    /// v4 renamed the site's own lists from "wishlist" to "chase list", freeing the word for the
    /// game's own 20-slot board, which is what the game itself calls a wishlist. The stored shape
    /// changed with the name, so a v3 save carries <c>wishlists</c> where a v4 one carries
    /// <c>chaseLists</c>. Which list the grid's hearts write to is untouched: it was already
    /// <c>wantListId</c>, and "want list" collided with nothing worth renaming it for.
    ///
    /// Moved rather than left to default. These are the lists someone built by hand, and the only
    /// copy is in their browser: reading a v3 save as "no chase lists" would not look like a
    /// migration that was skipped, it would look like the app lost their work.
    ///
    /// The legacy fields are cleared as they are read, and the serializer drops nulls, so a save
    /// written after this carries only the new spelling.
    /// </summary>
    /// <remarks>
    /// Runs before <see cref="Normalise"/>, so it meets the payload exactly as written: a null
    /// profile in the list, or no prefs object at all, are both things a hand-edited backup really
    /// carries. Nulls are passed through rather than dropped here — deciding which profiles survive
    /// is Normalise's job, and doing it in two places would mean two answers.
    /// </remarks>
    private static AppState ToV4(AppState state) => state with
    {
        Profiles = state.Profiles.Select(p => p is null ? null! : p with
        {
            ChaseLists = p.ChaseLists is { Count: > 0 } ? p.ChaseLists : p.Wishlists ?? [],
            Wishlists = null,
        }).ToList(),

        Prefs = (state.Prefs ?? new Prefs()) with
        {
            ChaseGrid = (state.Prefs?.ChaseGrid ?? false) || state.Prefs?.WishGrid == true,
            WishGrid = null,
        },
    };

    private static TargetSettings ToPlan(TargetSettings? old)
    {
        if (old is null) return TargetSettings.Default;
        if (old.DefaultPlan is { Count: > 0 }) return old;   // already v3

        var copies = old.DefaultCopies is > 0 ? old.DefaultCopies.Value : 1;

        int CopiesAt(int tier) =>
            old.CopiesByTier is not null && old.CopiesByTier.TryGetValue(tier, out var n) && n > 0
                ? n : copies;

        Dictionary<int, int> Plan(IEnumerable<int> tiers) =>
            tiers.Distinct().ToDictionary(t => t, CopiesAt);

        var defaultTiers = old.DefaultTiers is { Count: > 0 }
            ? old.DefaultTiers.AsEnumerable()
            : Enumerable.Range(0, (old.DefaultTierIndex ?? 3) + 1);

        var bySet = old.TiersBySet is { Count: > 0 }
            ? old.TiersBySet.ToDictionary(kv => kv.Key, kv => Plan(kv.Value))
            : (old.TierBySet ?? []).ToDictionary(
                kv => kv.Key, kv => Plan(Enumerable.Range(0, kv.Value + 1)));

        return new TargetSettings(Plan(defaultTiers), bySet);
    }
}
