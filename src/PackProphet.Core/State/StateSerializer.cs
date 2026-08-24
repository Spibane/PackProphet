namespace PackProphet.State;

using System.Text.Json;
using System.Text.Json.Serialization;

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
        Name = string.IsNullOrWhiteSpace(p.Name) ? "My collection" : p.Name,

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

        Decks = (p.Decks ?? []).Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Id))
            .Select(d => d with { DeckBuilderNrs = d.DeckBuilderNrs ?? [], Energies = d.Energies ?? [] })
            .ToList(),

        Wishlists = (p.Wishlists ?? []).Where(w => w is not null && !string.IsNullOrWhiteSpace(w.Id))
            .Select(w => w with { Wanted = w.Wanted ?? new Dictionary<string, int>() })
            .ToList(),

        PackLog = (p.PackLog ?? []).Where(e => e is not null)
            .Select(e => e with { OwnershipKeys = e.OwnershipKeys ?? [] }).ToList(),

        WonderLog = (p.WonderLog ?? []).Where(e => e is not null)
            .Select(e => e with { OwnershipKeys = e.OwnershipKeys ?? [] }).ToList(),

        TradeBoard = (p.TradeBoard ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).ToList(),

        Resources = Normalise(p.Resources)
    };

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
                AssumedRateDonors = prefs.AssumedRateDonors ?? [],
                AvailableLimitedPacks = prefs.AvailableLimitedPacks ?? []
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
