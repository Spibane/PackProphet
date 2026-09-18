using PackProphet.Deck;
using PackProphet.State;

namespace PackProphet.Tests;

public class StateTests
{
    [Fact]
    public void RoundTripsAPopulatedProfile()
    {
        var profile = Profile.NewDefault() with
        {
            Collection = new() { ["a.webp"] = 2, ["b.webp"] = 1 },
            Decks = [new SavedDeck("d1", "Charizard", [1, 1, 36], [EnergyType.Fire], FaceNr: 36)],
            ChaseLists = [new ChaseList("w1", "cool", new() { ["c.webp"] = 1 })],
            PackLog = [new PackOpenEvent(DateTimeOffset.Parse("2026-08-22T10:00:00Z"),
                                         "A1", "Mewtwo", "Regular Pack", ["a.webp"])],
            Resources = Resources.Empty with { Premium = true, Shinedust = 4200 }
        };
        var state = new AppState(AppState.CurrentSchemaVersion, [profile], profile.Id, new Prefs());

        var restored = StateSerializer.Deserialize(StateSerializer.Serialize(state));

        Assert.NotNull(restored);
        Assert.Equal(2, restored.Active.Collection["a.webp"]);
        Assert.Equal("Charizard", restored.Active.Decks[0].Name);
        Assert.Equal([1, 1, 36], restored.Active.Decks[0].DeckBuilderNrs);
        Assert.Equal([EnergyType.Fire], restored.Active.Decks[0].Energies);
        Assert.Equal(36, restored.Active.Decks[0].FaceNr);
        Assert.True(restored.Active.Resources.Premium);
        Assert.Equal(4200, restored.Active.Resources.Shinedust);
        Assert.Single(restored.Active.PackLog);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":1,\"profiles\":[]}")]
    public void UnusablePayloads_YieldNull_SoTheAppCanStartFresh(string? json) =>
        // Losing a session beats an app that will not open.
        Assert.Null(StateSerializer.Deserialize(json));

    [Fact]
    public void FutureSchema_IsRefusedRatherThanGuessedAt()
    {
        // Opening newer data with an older build and silently dropping unknown fields would
        // destroy the collection on the next save.
        var future = new AppState(AppState.CurrentSchemaVersion + 5,
            [Profile.NewDefault()], "default", new Prefs());

        Assert.Null(StateSerializer.Deserialize(StateSerializer.Serialize(future)));
    }

    [Fact]
    public void ExportIsIndented_SoABackupIsReadableAndDiffable()
    {
        var export = StateSerializer.Export(AppState.Fresh());
        Assert.Contains('\n', export);
        Assert.NotNull(StateSerializer.Deserialize(export));   // and still importable
    }

    [Fact]
    public async Task InMemoryStore_RoundTrips()
    {
        var store = new InMemoryStateStore();
        var state = AppState.Fresh();
        var mutated = state with { Prefs = new Prefs(Theme: "dark") };

        await store.SaveAsync(mutated);
        Assert.Equal("dark", (await store.LoadAsync()).Prefs.Theme);
    }

    [Fact]
    public void Prefs_DropTheSavedTargetOnRead()
    {
        // It used to be saved and shared by the four Answer pages, and the sharing was fine while
        // the SAVING was not: the scope outlived the visit that set it, so a page opened days
        // later ranked against a set chosen once somewhere else. Each page keeps its own for the
        // length of a visit now, and a save still carrying one loads with it dropped rather than
        // quietly ranking against it.
        var state = AppState.Fresh() with { Prefs = new Prefs() with { Target = "series:A" } };

        var reloaded = StateSerializer.Deserialize(StateSerializer.Serialize(state))!;

        Assert.Null(reloaded.Prefs.Target);
        Assert.Null(StateSerializer.Deserialize(StateSerializer.Serialize(AppState.Fresh()))!.Prefs.Target);
    }

    /// <summary>
    /// Column counts are persisted, so they must survive a save/load — and an older save
    /// that predates the field must not come back as zero columns, which would render an
    /// empty grid. Zero is the "never chosen" sentinel and readers substitute a default.
    /// </summary>
    [Fact]
    public void Prefs_RoundTripColumnCounts_AndOlderSavesReadAsUnset()
    {
        var state = AppState.Fresh() with
        {
            Prefs = new Prefs(Theme: "dark", GridColumns: 10)
            {
                PackColumns = 6, ListRoomy = true, HitTiers = [5, 8], DeckGrid = true
            }
        };

        var reloaded = StateSerializer.Deserialize(StateSerializer.Serialize(state))!;
        Assert.Equal(10, reloaded.Prefs.GridColumns);
        Assert.Equal(6, reloaded.Prefs.PackColumns);
        Assert.Equal("dark", reloaded.Prefs.Theme);
        Assert.True(reloaded.Prefs.ListRoomy);
        Assert.Equal([5, 8], reloaded.Prefs.HitTiers);
        Assert.True(reloaded.Prefs.DeckGrid);

        // A save written before either field existed. Note the profile: an empty profile list
        // is refused outright, so the fixture has to carry one.
        var full = StateSerializer.Serialize(AppState.Fresh() with { Prefs = new Prefs(Theme: "dark") });
        Assert.Contains("gridColumns", full);
        Assert.Contains("packColumns", full);

        var older = System.Text.RegularExpressions.Regex.Replace(
            full, @"\s*""(gridColumns|packColumns)"":\s*\d+,", "");
        Assert.DoesNotContain("olumns", older);

        var loaded = StateSerializer.Deserialize(older)!;
        Assert.Equal("dark", loaded.Prefs.Theme);
        Assert.Equal(0, loaded.Prefs.GridColumns);
        Assert.Equal(0, loaded.Prefs.PackColumns);
        Assert.False(loaded.Prefs.ListRoomy);   // compact is the default
        // Null, not empty: "never chosen" has to be distinguishable from "chose nothing", since
        // the reader substitutes a default for the first and must not for the second.
        Assert.Null(loaded.Prefs.HitTiers);
    }

    [Fact]
    public void TargetSettings_FallBackToTheDefaultPlan()
    {
        var settings = new TargetSettings(
            DefaultPlan: new() { [0] = 1, [1] = 2 },
            PlanBySet: new() { ["A1"] = new() { [4] = 2, [9] = 1 } });

        Assert.Equal(2, settings.PlanFor("A1").Copies(4));
        Assert.Equal(1, settings.PlanFor("A1").Copies(9));
        Assert.False(settings.PlanFor("A1").Wants(0));

        Assert.Equal(2, settings.PlanFor("A2").Copies(1));
        Assert.True(settings.HasOverride("A1"));
        Assert.False(settings.HasOverride("A2"));
    }

    [Fact]
    public void V3Wishlists_BecomeChaseListsWithoutLosingAny()
    {
        // v4 renamed the site's own lists to "chase list", freeing "wishlist" for the game's own
        // 20-slot board. These are lists someone built by hand and the only copy is in their
        // browser, so reading a v3 save must move them across rather than start empty — which
        // would not look like a skipped migration, it would look like the app lost their work.
        var v3 = """
        {
          "schemaVersion": 3,
          "activeProfileId": "default",
          "prefs": { "theme": "auto", "wishGrid": true },
          "profiles": [{
            "id": "default", "name": "Mine", "collection": {},
            "targets": { "defaultPlan": {"0":1}, "planBySet": {} },
            "decks": [],
            "wishlists": [{ "id": "w1", "name": "Chase cards", "wanted": { "a.webp": 2 } },
                          { "id": "w2", "name": "Binder page", "wanted": { "b.webp": 1 } }],
            "wantListId": "w2",
            "packLog": [], "wonderLog": [], "resources": null
          }]
        }
        """;

        var migrated = StateSerializer.Deserialize(v3);

        Assert.NotNull(migrated);
        Assert.Equal(AppState.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal(["Chase cards", "Binder page"], migrated.Active.ChaseLists.Select(w => w.Name));
        Assert.Equal(2, migrated.Active.ChaseLists[0].Wanted["a.webp"]);

        // Untouched by the migration and still pointing at the right list: "want list" was never
        // one of the names that collided, so the hearts keep writing where they were.
        Assert.Equal("w2", migrated.Active.WantListId);
        Assert.True(migrated.Prefs.ChaseGrid);
    }

    [Fact]
    public void AMigratedSaveIsRewrittenWithOnlyTheNewSpelling()
    {
        // The legacy fields are read once and cleared, so a save written after the migration does
        // not carry both spellings — two places to look is how they drift apart.
        var v3 = """
        {"schemaVersion":3,"activeProfileId":"d","prefs":{"wishGrid":true},
         "profiles":[{"id":"d","name":"M","collection":{},"targets":null,"decks":[],
          "wishlists":[{"id":"w","name":"W","wanted":{}}],"wantListId":"w",
          "packLog":[],"wonderLog":[],"resources":null}]}
        """;

        var json = StateSerializer.Serialize(StateSerializer.Deserialize(v3)!);

        Assert.DoesNotContain("wishlists", json);
        Assert.DoesNotContain("wishGrid", json);
        Assert.Contains("chaseLists", json);
        Assert.Contains("wantListId", json);
    }

    [Fact]
    public void V1Threshold_MigratesToTheEquivalentSelection()
    {
        // A v1 save stored "everything up to tier 3". After migration the user must still
        // want exactly the four diamond rungs — no more, no fewer.
        var v1 = $$"""
        {
          "schemaVersion": 1,
          "activeProfileId": "default",
          "prefs": {},
          "profiles": [{
            "id": "default", "name": "Mine",
            "collection": { "a.webp": 2 },
            "targets": { "defaultTierIndex": 3, "tierBySet": { "A1": 5 }, "defaultCopies": 2 },
            "decks": [], "wishlists": [], "packLog": [], "wonderLog": [],
            "resources": { "wonder": {"balance":0,"hourglasses":0}, "trade": {"balance":0,"hourglasses":0},
                           "packHourglasses": 0, "packPointsBySet": {}, "shinedust": 0, "premium": false }
          }]
        }
        """;

        var migrated = StateSerializer.Deserialize(v1);

        Assert.NotNull(migrated);
        Assert.Equal(AppState.CurrentSchemaVersion, migrated.SchemaVersion);
        // The threshold expands to the rungs it covered, and the single copy count is
        // applied to each of them.
        Assert.Equal(new[] { 0, 1, 2, 3 },
            migrated.Active.Targets.PlanFor().WantedTiers.OrderBy(t => t));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 },
            migrated.Active.Targets.PlanFor("A1").WantedTiers.OrderBy(t => t));
        Assert.All(migrated.Active.Targets.PlanFor().WantedTiers,
            t => Assert.Equal(2, migrated.Active.Targets.PlanFor().Copies(t)));

        // And nothing else is lost in the process.
        Assert.Equal(2, migrated.Active.Collection["a.webp"]);
    }

    [Fact]
    public void APlanRoundTripsThroughSerialization()
    {
        // Two of each diamond, one of the stars, crowns ignored — the whole point of the
        // per-rarity model, so it must survive a save and load intact.
        var settings = new TargetSettings(
            DefaultPlan: new() { [0] = 2, [1] = 2, [2] = 2, [3] = 2, [4] = 1, [5] = 1, [6] = 1 },
            PlanBySet: new() { ["A1"] = new() { [9] = 1 } });

        var profile = Profile.NewDefault() with { Targets = settings };
        var state = new AppState(AppState.CurrentSchemaVersion, [profile], profile.Id, new Prefs());

        var restored = StateSerializer.Deserialize(StateSerializer.Serialize(state));

        Assert.NotNull(restored);
        var plan = restored.Active.Targets.PlanFor();
        Assert.Equal(2, plan.Copies(0));
        Assert.Equal(1, plan.Copies(4));
        Assert.False(plan.Wants(9));
        Assert.Equal(1, restored.Active.Targets.PlanFor("A1").Copies(9));
    }

    [Fact]
    public void ActiveProfile_FallsBackWhenTheIdIsStale()
    {
        // A stale active id (e.g. a deleted profile) must not throw on boot.
        var p = Profile.NewDefault("real");
        var state = new AppState(1, [p], "does-not-exist", new Prefs());
        Assert.Equal("real", state.Active.Id);
    }

    [Fact]
    public void RoundTrip_KeepsPerSetPlanOverrides()
    {
        // The Packs page writes these, and a plan silently dropped on reload would look like
        // the app forgetting a deliberate choice — the one thing a tracker cannot do.
        var state = AppState.Fresh();
        state = state with
        {
            Profiles =
            [
                state.Active with
                {
                    Targets = new TargetSettings(
                        DefaultPlan: new() { [0] = 1, [1] = 1 },
                        PlanBySet: new() { ["A1"] = new() { [6] = 2 } })
                }
            ]
        };

        var back = StateSerializer.Deserialize(StateSerializer.Serialize(state));

        Assert.NotNull(back);
        Assert.True(back!.Active.Targets.HasOverride("A1"));
        Assert.Equal(2, back.Active.Targets.PlanFor("A1").Copies(6));
        Assert.False(back.Active.Targets.HasOverride("A2"));
        Assert.Equal(1, back.Active.Targets.PlanFor("A2").Copies(0));
    }

    [Fact]
    public void RoundTrip_KeepsAssumedRateDonors()
    {
        // These change every number the engine produces, so losing them on reload would silently
        // move a user's estimates back to "this set cannot be pulled".
        var state = AppState.Fresh();
        state = state with
        {
            Prefs = state.Prefs with { AssumedRateDonors = new() { ["B4"] = "B3b" } }
        };

        var back = StateSerializer.Deserialize(StateSerializer.Serialize(state));

        Assert.NotNull(back);
        Assert.Equal("B3b", back!.Prefs.AssumedRateDonors["B4"]);
    }

    [Fact]
    public void OlderSave_WithNoAssumedRates_LoadsWithNone()
    {
        // The field is new, so every existing save lacks it. Absent must mean "assume nothing",
        // not null — the session enumerates this dictionary on every engine rebuild.
        var back = StateSerializer.Deserialize(
            """{"schemaVersion":3,"profiles":[{"id":"p","name":"n","collection":{},"targets":{"defaultPlan":{"0":1},"planBySet":{}},"decks":[],"wishlists":[],"packLog":[],"wonderLog":[],"resources":null}],"activeProfileId":"p","prefs":{"theme":"auto"}}""");

        Assert.NotNull(back);
        Assert.Empty(back!.Prefs.AssumedRateDonors);
    }

    /// <summary>A v4 save: two logged packs, no row ids, since ids did not exist yet.</summary>
    private const string V4WithLog =
        """{"schemaVersion":4,"profiles":[{"id":"p","name":"n","collection":{},"targets":{"defaultPlan":{"0":1},"planBySet":{}},"decks":[],"chaseLists":[],"packLog":[{"at":"2026-03-01T12:00:00+00:00","set":"A1","pack":"A1:pikachu","variant":"std","ownershipKeys":["x.webp"]},{"at":"2026-03-01T12:00:00+00:00","set":"A1","pack":"A1:pikachu","variant":"std","ownershipKeys":["x.webp"]}],"wonderLog":[],"resources":null}],"activeProfileId":"p","prefs":{"theme":"auto"}}""";

    [Fact]
    public void Two_devices_migrating_the_same_log_agree_on_every_row_id()
    {
        // The property the whole migration rests on. Each device reads its own copy, offline,
        // with no way to ask the other what it called anything -- so the ids have to fall out of
        // the row's contents. Random ones would turn every existing pack into one pack per
        // device at the next sync, which is the fault this was meant to end.
        var phone = StateSerializer.Deserialize(V4WithLog)!;
        var laptop = StateSerializer.Deserialize(V4WithLog)!;

        Assert.Equal(
            phone.Active.PackLog.Select(e => e.Id),
            laptop.Active.PackLog.Select(e => e.Id));
        Assert.All(phone.Active.PackLog, e => Assert.False(string.IsNullOrWhiteSpace(e.Id)));
    }

    [Fact]
    public void Two_identical_rows_in_one_log_are_still_told_apart()
    {
        // Both rows in that save are the same pack at the same instant with the same cards -- a
        // burst the game gave twice. Deriving an id from the contents alone would give them one
        // id between them, and the merge would collapse two packs into one.
        var back = StateSerializer.Deserialize(V4WithLog)!;

        Assert.Equal(2, back.Active.PackLog.Count);
        Assert.Equal(2, back.Active.PackLog.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void A_row_that_already_has_an_id_keeps_it_through_a_reload()
    {
        var state = AppState.Fresh();
        state.Active.PackLog.Add(
            new PackOpenEvent(DateTimeOffset.UnixEpoch, "A1", "A1:pikachu", "std", ["x.webp"])
            {
                Id = "abc123abc123",
            });

        var back = StateSerializer.Deserialize(StateSerializer.Serialize(state))!;

        Assert.Equal("abc123abc123", back.Active.PackLog[0].Id);
    }

    [Fact]
    public void A_save_from_a_newer_schema_is_refused_rather_than_read_lossily()
    {
        // v5 added row ids and the deleted-row list. A build that does not know those fields
        // would drop them on read and store the loss on the next write -- which for the deleted
        // list means every deletion undoing itself again.
        Assert.Null(StateSerializer.Deserialize(
            V4WithLog.Replace("\"schemaVersion\":4", $"\"schemaVersion\":{AppState.CurrentSchemaVersion + 1}")));
    }
}
