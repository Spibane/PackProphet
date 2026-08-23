namespace PackProphet.State;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Reads and writes the persisted state, and owns migration. Also the export/import format,
/// so a user can always get their data out — a local-only app with no export is a trap.
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
    /// recoverable, so the caller can start fresh rather than crash on boot — losing a
    /// session beats an app that will not open.
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
    /// Bring an older payload up to the current schema. Unknown FUTURE versions are refused
    /// rather than guessed at: opening newer data with an older build and silently dropping
    /// fields would destroy the user's collection on the next save.
    /// </summary>
    private static AppState? Migrate(AppState state)
    {
        if (state.SchemaVersion > AppState.CurrentSchemaVersion) return null;

        if (state.SchemaVersion < 3) state = ToV3(state);

        return state with { SchemaVersion = AppState.CurrentSchemaVersion };
    }

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
