namespace PackProphet.Tests;

using PackProphet.Deck;
using PackProphet.State;
using PackProphet.Sync;

/// <summary>
/// The merge rules, stated as the outcomes a user would notice. Everything here is two devices
/// starting from the same state, editing independently, and meeting again.
/// </summary>
public class StateMergeTests
{
    private static AppState With(Action<Dictionary<string, int>> edit)
    {
        var state = AppState.Fresh();
        edit(state.Active.Collection);
        return state;
    }

    private static AppState Clone(AppState state) =>
        StateSerializer.Deserialize(StateSerializer.Serialize(state))!;

    private static Dictionary<string, int> Owned(AppState state) => state.Active.Collection;

    // ---- Collection counts ---------------------------------------------------------------

    [Fact]
    public void An_edit_on_one_device_only_is_taken_whichever_device_made_it()
    {
        var ancestor = With(c => c["a.webp"] = 1);

        var local = Clone(ancestor);
        Owned(local)["b.webp"] = 2;

        var remote = Clone(ancestor);
        Owned(remote)["c.webp"] = 3;

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.Equal(1, Owned(merged)["a.webp"]);
        Assert.Equal(2, Owned(merged)["b.webp"]);
        Assert.Equal(3, Owned(merged)["c.webp"]);
    }

    [Fact]
    public void The_afternoons_work_on_the_other_device_is_not_erased()
    {
        // The failure that makes last-writer-wins unusable: cards ticked here, cards ticked there,
        // and nothing lost either way.
        var ancestor = AppState.Fresh();

        var phone = Clone(ancestor);
        for (var i = 0; i < 30; i++) Owned(phone)[$"phone-{i}.webp"] = 1;

        var laptop = Clone(ancestor);
        for (var i = 0; i < 30; i++) Owned(laptop)[$"laptop-{i}.webp"] = 1;

        var merged = StateMerge.Merge(phone, laptop, ancestor).State;

        Assert.Equal(60, Owned(merged).Count);
        Assert.True(merged.Active.Collection.Values.All(v => v == 1));
    }

    [Fact]
    public void The_same_card_changed_on_both_devices_keeps_the_higher_count_and_is_reported()
    {
        var ancestor = With(c => c["a.webp"] = 1);

        var local = Clone(ancestor);
        Owned(local)["a.webp"] = 2;

        var remote = Clone(ancestor);
        Owned(remote)["a.webp"] = 5;

        var result = StateMerge.Merge(local, remote, ancestor);

        Assert.Equal(5, Owned(result.State)["a.webp"]);
        Assert.Equal(1, result.Report.Conflicts);
        Assert.False(result.Report.Clean);
    }

    [Fact]
    public void A_card_deleted_on_the_other_device_stays_deleted()
    {
        var ancestor = With(c => { c["a.webp"] = 1; c["b.webp"] = 1; });

        var local = Clone(ancestor);
        var remote = Clone(ancestor);
        Owned(remote).Remove("b.webp");

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.True(Owned(merged).ContainsKey("a.webp"));
        Assert.False(Owned(merged).ContainsKey("b.webp"));
    }

    [Fact]
    public void A_card_deleted_there_but_re_counted_here_survives()
    {
        // A deletion is honoured because it is the most recent word on a card nobody else touched.
        // Once this device has touched it, it is not.
        var ancestor = With(c => c["a.webp"] = 1);

        var local = Clone(ancestor);
        Owned(local)["a.webp"] = 4;

        var remote = Clone(ancestor);
        Owned(remote).Remove("a.webp");

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.Equal(4, Owned(merged)["a.webp"]);
    }

    [Fact]
    public void Merging_a_state_with_itself_changes_nothing_and_reports_nothing()
    {
        var state = With(c => { c["a.webp"] = 2; c["b.webp"] = 1; });
        var result = StateMerge.Merge(state, Clone(state), Clone(state));

        Assert.True(result.Report.Clean);
        Assert.Equal(Owned(state), Owned(result.State));
    }

    [Fact]
    public void Merging_is_stable_when_run_twice()
    {
        // Sync runs on every launch. A merge that keeps producing a new answer would push a new
        // document every time, forever.
        var ancestor = With(c => c["a.webp"] = 1);
        var local = Clone(ancestor); Owned(local)["a.webp"] = 2;
        var remote = Clone(ancestor); Owned(remote)["a.webp"] = 5;

        var once = StateMerge.Merge(local, remote, ancestor).State;
        var twice = StateMerge.Merge(once, Clone(once), Clone(once));

        Assert.True(twice.Report.Clean);
        Assert.Equal(Owned(once), Owned(twice.State));
    }

    // ---- Logs -------------------------------------------------------------

    [Fact]
    public void Packs_logged_on_both_devices_all_survive()
    {
        var ancestor = AppState.Fresh();
        var at = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        var local = Clone(ancestor);
        local.Active.PackLog.Add(new PackOpenEvent(at, "A1", "A1:pikachu", "std", ["x.webp"]));

        var remote = Clone(ancestor);
        remote.Active.PackLog.Add(new PackOpenEvent(at.AddHours(1), "A1", "A1:mewtwo", "std", ["y.webp"]));

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.Equal(2, merged.Active.PackLog.Count);
    }

    [Fact]
    public void A_pack_both_devices_already_know_about_is_not_duplicated()
    {
        var at = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var ancestor = AppState.Fresh();
        ancestor.Active.PackLog.Add(new PackOpenEvent(at, "A1", "A1:pikachu", "std", ["x.webp"]));

        var merged = StateMerge.Merge(Clone(ancestor), Clone(ancestor), ancestor).State;

        Assert.Single(merged.Active.PackLog);
    }

    [Fact]
    public void Two_packs_logged_in_the_same_second_are_both_kept()
    {
        // A ten-pack burst shares a timestamp; the cards pulled are what tell the rows apart.
        var at = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var ancestor = AppState.Fresh();

        var local = Clone(ancestor);
        local.Active.PackLog.Add(new PackOpenEvent(at, "A1", "A1:pikachu", "std", ["x.webp"]));

        var remote = Clone(ancestor);
        remote.Active.PackLog.Add(new PackOpenEvent(at, "A1", "A1:pikachu", "std", ["y.webp"]));

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.Equal(2, merged.Active.PackLog.Count);
    }

    [Fact]
    public void A_pack_the_other_device_has_not_seen_yet_is_not_a_deletion()
    {
        // The property the union was protecting, and the one that has to survive being able to
        // delete: a device that has been closed for a week is absent from most of the week.
        var at = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var ancestor = Clone(AppState.Fresh());

        var local = Clone(ancestor);
        local.Active.PackLog.Add(Pack(at, "x.webp"));

        var merged = StateMerge.Merge(local, Clone(ancestor), ancestor).State;

        Assert.Single(merged.Active.PackLog);
    }

    [Fact]
    public void A_deleted_pack_stays_deleted_rather_than_being_handed_back()
    {
        // The old behaviour: the row came back from whichever device still had it, while the
        // cards and pack points that deleting it reversed stayed reversed. A history of packs
        // whose cards are not in the collection, and every figure on the page counting them.
        var at = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var ancestor = Clone(Logged(at, "x.webp"));
        var remote = Clone(ancestor);

        var local = Deleted(ancestor);

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.Empty(merged.Active.PackLog);
        Assert.Single(merged.Active.RemovedLog);
    }

    [Fact]
    public void A_deletion_carries_across_a_first_pair_where_nothing_can_be_inferred()
    {
        // No ancestor, so absence proves nothing and the union is the forgiving reading. The
        // tombstone is the difference between "I have not seen that row" and "I deleted it",
        // and it is the only thing that can say so here.
        var at = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var logged = Clone(Logged(at, "x.webp"));

        var merged = StateMerge.FirstPair(Deleted(logged), logged).State;

        Assert.Empty(merged.Active.PackLog);
    }

    [Fact]
    public void Redating_a_pack_moves_it_rather_than_duplicating_it()
    {
        // Identity used to be the row's contents, which included the timestamp -- so moving a
        // pack to another day made it a different row, and the other device's copy of the
        // original survived alongside it.
        var at = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var ancestor = Clone(Logged(at, "x.webp"));
        var remote = Clone(ancestor);

        var local = Clone(ancestor);
        var moved = local.Active.PackLog[0] with { At = at.AddDays(-2) };
        local.Active.PackLog[0] = moved;

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        var row = Assert.Single(merged.Active.PackLog);
        Assert.Equal(at.AddDays(-2), row.At);
    }

    [Fact]
    public void Resetting_a_collection_does_not_get_its_history_back()
    {
        // Cards and logs went in opposite directions: the cards were deleted everywhere and the
        // history came back, so the reset left a state neither device had asked for.
        var at = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var ancestor = Clone(Logged(at, "x.webp"));
        ancestor.Active.Collection["x.webp"] = 1;

        var remote = Clone(ancestor);
        var reset = Clone(ancestor);
        var wiped = reset.Active with
        {
            Collection = new(),
            PackLog = [],
            RemovedLog = [.. reset.Active.PackLog.Select(LogId.Of)],
        };
        var local = reset with { Profiles = [wiped] };

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.Empty(merged.Active.PackLog);
        Assert.Empty(merged.Active.Collection);
    }

    [Fact]
    public void A_log_merged_on_two_devices_comes_out_in_the_same_order_on_both()
    {
        // Otherwise the two hold lists that compare unequal forever, and each sync pushes a
        // result the next one undoes.
        var at = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var ancestor = Clone(AppState.Fresh());

        var phone = Clone(ancestor);
        phone.Active.PackLog.Add(Pack(at.AddHours(2), "later.webp"));

        var laptop = Clone(ancestor);
        laptop.Active.PackLog.Add(Pack(at, "earlier.webp"));

        var onPhone = StateMerge.Merge(phone, laptop, ancestor).State;
        var onLaptop = StateMerge.Merge(laptop, phone, ancestor).State;

        Assert.Equal(
            onPhone.Active.PackLog.Select(e => e.Id),
            onLaptop.Active.PackLog.Select(e => e.Id));
        Assert.True(StateMerge.SameState(onPhone, onLaptop));
    }

    private static PackOpenEvent Pack(DateTimeOffset at, string card) =>
        new(at, "A1", "A1:pikachu", "std", [card]) { Id = LogId.New() };

    /// <summary>A state holding one logged pack.</summary>
    private static AppState Logged(DateTimeOffset at, string card)
    {
        var state = AppState.Fresh();
        state.Active.PackLog.Add(Pack(at, card));
        return state;
    }

    /// <summary>That state with the pack deleted the way the history page deletes it.</summary>
    private static AppState Deleted(AppState state)
    {
        var copy = Clone(state);
        var without = copy.Active with
        {
            PackLog = [],
            RemovedLog = [.. copy.Active.PackLog.Select(LogId.Of)],
        };
        return copy with { Profiles = [without] };
    }

    // ---- Decks and chase lists -----------------------------------------------------------

    [Fact]
    public void A_deck_built_on_each_device_gives_two_decks()
    {
        var ancestor = AppState.Fresh();

        var local = Clone(ancestor);
        local.Active.Decks.Add(new SavedDeck("d1", "Mine", [1, 2], [EnergyType.Fire]));

        var remote = Clone(ancestor);
        remote.Active.Decks.Add(new SavedDeck("d2", "Theirs", [3, 4], [EnergyType.Water]));

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.Equal(2, merged.Active.Decks.Count);
    }

    [Fact]
    public void A_deck_edited_on_both_devices_keeps_this_ones_and_says_so()
    {
        var ancestor = AppState.Fresh();
        ancestor.Active.Decks.Add(new SavedDeck("d1", "Original", [1], [EnergyType.Fire]));

        var local = Clone(ancestor);
        local.Active.Decks[0] = local.Active.Decks[0] with { Name = "Renamed here" };

        var remote = Clone(ancestor);
        remote.Active.Decks[0] = remote.Active.Decks[0] with { Name = "Renamed there" };

        var result = StateMerge.Merge(local, remote, ancestor);

        Assert.Equal("Renamed here", result.State.Active.Decks[0].Name);
        Assert.Equal(1, result.Report.Conflicts);
        Assert.Contains(result.Report.Notes, n => n.Contains("deck"));
    }

    [Fact]
    public void A_deck_renamed_only_on_the_other_device_takes_that_name()
    {
        var ancestor = AppState.Fresh();
        ancestor.Active.Decks.Add(new SavedDeck("d1", "Original", [1], [EnergyType.Fire]));

        var local = Clone(ancestor);
        var remote = Clone(ancestor);
        remote.Active.Decks[0] = remote.Active.Decks[0] with { Name = "Better name" };

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.Equal("Better name", merged.Active.Decks[0].Name);
    }

    [Fact]
    public void A_deck_deleted_on_the_other_device_stays_deleted()
    {
        var ancestor = AppState.Fresh();
        ancestor.Active.Decks.Add(new SavedDeck("d1", "Doomed", [1], [EnergyType.Fire]));

        var local = Clone(ancestor);
        var remote = Clone(ancestor);
        remote.Active.Decks.Clear();

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.Empty(merged.Active.Decks);
    }

    [Fact]
    public void A_chase_list_added_on_the_other_device_arrives()
    {
        var ancestor = AppState.Fresh();

        var remote = Clone(ancestor);
        remote.Active.ChaseLists.Add(new ChaseList("w1", "Chase", new() { ["a.webp"] = 1 }));

        var merged = StateMerge.Merge(Clone(ancestor), remote, ancestor).State;

        Assert.Single(merged.Active.ChaseLists);
        Assert.Equal("Chase", merged.Active.ChaseLists[0].Name);
    }

    // ---- Profiles ------------------------------------------------------------------------

    [Fact]
    public void A_profile_added_on_the_other_device_arrives()
    {
        var ancestor = AppState.Fresh();

        var remote = Clone(ancestor);
        remote.Profiles.Add(Profile.NewDefault("alt", "Alt account"));

        var merged = StateMerge.Merge(Clone(ancestor), remote, ancestor).State;

        Assert.Equal(2, merged.Profiles.Count);
        Assert.Contains(merged.Profiles, p => p.Id == "alt");
    }

    [Fact]
    public void A_profile_deleted_on_the_other_device_stays_deleted()
    {
        var ancestor = AppState.Fresh();
        ancestor.Profiles.Add(Profile.NewDefault("alt", "Alt account"));

        var local = Clone(ancestor);
        var remote = Clone(ancestor);
        remote.Profiles.RemoveAll(p => p.Id == "alt");

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.Single(merged.Profiles);
    }

    [Fact]
    public void A_profile_deleted_there_but_edited_here_survives()
    {
        var ancestor = AppState.Fresh();
        ancestor.Profiles.Add(Profile.NewDefault("alt", "Alt account"));

        var local = Clone(ancestor);
        local.Profiles[1].Collection["a.webp"] = 3;

        var remote = Clone(ancestor);
        remote.Profiles.RemoveAll(p => p.Id == "alt");

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.Equal(2, merged.Profiles.Count);
        Assert.Equal(3, merged.Profiles.First(p => p.Id == "alt").Collection["a.webp"]);
    }

    [Fact]
    public void A_merge_never_leaves_zero_profiles()
    {
        var ancestor = AppState.Fresh();
        var local = Clone(ancestor);
        var remote = Clone(ancestor);
        remote.Profiles.Clear();

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.NotEmpty(merged.Profiles);
        Assert.Contains(merged.Profiles, p => p.Id == merged.ActiveProfileId);
    }

    // ---- What deliberately does not sync -------------------------------------------------

    [Fact]
    public void Appearance_and_layout_stay_on_the_device_that_set_them()
    {
        // A phone and a desktop want different column counts, and a theme belongs to the screen
        // you are looking at.
        var ancestor = AppState.Fresh();

        var local = Clone(ancestor) with { Prefs = new Prefs("dark", GridColumns: 3) };
        var remote = Clone(ancestor) with { Prefs = new Prefs("light", GridColumns: 9) };

        var result = StateMerge.Merge(local, remote, ancestor);

        Assert.Equal("dark", result.State.Prefs.Theme);
        Assert.Equal(3, result.State.Prefs.GridColumns);
        Assert.True(result.Report.Clean);       // a difference here is not a conflict
    }

    [Fact]
    public void The_profile_you_are_looking_at_does_not_change_under_you()
    {
        var ancestor = AppState.Fresh();
        ancestor.Profiles.Add(Profile.NewDefault("alt", "Alt account"));

        var local = Clone(ancestor) with { ActiveProfileId = "default" };
        var remote = Clone(ancestor) with { ActiveProfileId = "alt" };

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.Equal("default", merged.ActiveProfileId);
    }

    // ---- Resources -----------------------------------------------------------------------

    [Fact]
    public void The_more_recently_read_stamina_balance_wins()
    {
        var ancestor = AppState.Fresh();
        var noon = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        var local = Clone(ancestor);
        local.Profiles[0] = local.Profiles[0] with
        {
            Resources = local.Active.Resources with { Wonder = new ResourcePool(2, 0) { AsOf = noon } },
        };

        var remote = Clone(ancestor);
        remote.Profiles[0] = remote.Profiles[0] with
        {
            Resources = remote.Active.Resources with
            {
                Wonder = new ResourcePool(5, 0) { AsOf = noon.AddHours(3) },
            },
        };

        var merged = StateMerge.Merge(local, remote, ancestor).State;

        Assert.Equal(5, merged.Active.Resources.Wonder.Balance);
    }

    [Fact]
    public void Points_recorded_for_different_sets_are_not_a_conflict()
    {
        var ancestor = AppState.Fresh();

        var local = Clone(ancestor);
        local.Active.Resources.PackPointsBySet["A1"] = 100;

        var remote = Clone(ancestor);
        remote.Active.Resources.PackPointsBySet["A2"] = 40;

        var result = StateMerge.Merge(local, remote, ancestor);

        Assert.Equal(100, result.State.Active.Resources.PackPointsBySet["A1"]);
        Assert.Equal(40, result.State.Active.Resources.PackPointsBySet["A2"]);
        Assert.True(result.Report.Clean);
    }

    // ---- First pair ----------------------------------------------------------------------

    [Fact]
    public void Pairing_two_collections_built_separately_joins_them()
    {
        var phone = AppState.Fresh();
        Owned(phone)["a.webp"] = 1;
        Owned(phone)["shared.webp"] = 2;

        var laptop = AppState.Fresh();
        Owned(laptop)["b.webp"] = 1;
        Owned(laptop)["shared.webp"] = 5;

        var merged = StateMerge.FirstPair(phone, laptop).State;

        Assert.Equal(1, Owned(merged)["a.webp"]);
        Assert.Equal(1, Owned(merged)["b.webp"]);
        Assert.Equal(5, Owned(merged)["shared.webp"]);   // the higher count, as ever
    }

    [Fact]
    public void Pairing_a_fresh_device_adopts_the_existing_collection_whole()
    {
        var existing = AppState.Fresh();
        for (var i = 0; i < 40; i++) Owned(existing)[$"card-{i}.webp"] = 1;
        existing.Active.Decks.Add(new SavedDeck("d1", "Deck", [1], [EnergyType.Fire]));

        var merged = StateMerge.FirstPair(AppState.Fresh(), existing).State;

        Assert.Equal(40, Owned(merged).Count);
        Assert.Single(merged.Active.Decks);
    }

    [Fact]
    public void Joining_a_blank_device_to_an_established_one_keeps_the_established_settings()
    {
        // The bug behind an empty conflict box on a first pair. With no ancestor, every field that
        // moves as one value fell to "keep local" -- and on the device doing the joining, local is
        // the empty one. A fresh private window adopting a real collection was therefore blanking
        // its trade board, its shinedust, its hourglasses and its rarity plan, and counting each as
        // a conflict it could not describe.
        var established = AppState.Fresh();
        established.Profiles[0] = established.Profiles[0] with
        {
            Name = "Main account",
            InGameName = "Spibane",
            Targets = new TargetSettings(new Dictionary<int, int> { [0] = 2, [1] = 2 }, new()),
            Resources = Resources.Empty with { PackHourglasses = 34, Shinedust = 1200 },
            TradeBoard = ["a.webp", "b.webp", "c.webp"],
        };
        established.Active.Resources.PackPointsBySet["A1"] = 240;

        var blank = AppState.Fresh();

        var result = StateMerge.FirstPair(blank, established);
        var joined = result.State.Active;

        Assert.Equal("Main account", joined.Name);
        Assert.Equal("Spibane", joined.InGameName);
        Assert.Equal(34, joined.Resources.PackHourglasses);
        Assert.Equal(1200, joined.Resources.Shinedust);
        Assert.Equal(240, joined.Resources.PackPointsBySet["A1"]);
        Assert.Equal(3, joined.TradeBoard.Count);
        Assert.Equal(2, joined.Targets.DefaultPlan[0]);

        // And none of it was a conflict: one side had simply never set any of these.
        Assert.True(result.Report.Clean, string.Join(" | ", result.Report.Notes));
    }

    [Fact]
    public void Joining_in_the_other_direction_keeps_the_established_settings_too()
    {
        // Whichever window the user happens to press Join in.
        var established = AppState.Fresh();
        established.Profiles[0] = established.Profiles[0] with
        {
            Resources = Resources.Empty with { Shinedust = 900 },
            TradeBoard = ["a.webp"],
        };

        var result = StateMerge.FirstPair(established, AppState.Fresh());

        Assert.Equal(900, result.State.Active.Resources.Shinedust);
        Assert.Single(result.State.Active.TradeBoard);
        Assert.True(result.Report.Clean, string.Join(" | ", result.Report.Notes));
    }

    [Fact]
    public void Two_devices_that_both_set_a_figure_differently_is_still_a_conflict()
    {
        // The yielding rule must not swallow a real disagreement.
        var a = AppState.Fresh();
        a.Profiles[0] = a.Profiles[0] with
        {
            Resources = Resources.Empty with { Shinedust = 100 },
            TradeBoard = ["a.webp"],
        };

        var b = AppState.Fresh();
        b.Profiles[0] = b.Profiles[0] with
        {
            Resources = Resources.Empty with { Shinedust = 700 },
            TradeBoard = ["z.webp"],
        };

        var result = StateMerge.FirstPair(a, b);

        Assert.Equal(100, result.State.Active.Resources.Shinedust);   // this device's
        Assert.Equal(2, result.Report.Conflicts);
        Assert.Contains(result.Report.Notes, n => n.Contains("shinedust"));
        Assert.Contains(result.Report.Notes, n => n.Contains("trade board"));
    }

    // ---- The report itself ---------------------------------------------------------------

    [Fact]
    public void A_counted_conflict_can_never_be_a_conflict_with_nothing_to_say()
    {
        // The invariant the settings page depends on. It rendered "Both devices had changed the
        // same things:" above an empty list, because nine resolution sites bumped a counter and
        // none of them wrote a note. Swept here across every field that can differ, in both merge
        // modes, so the two cannot come apart again.
        var noon = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        var changes = new (string What, Action<AppState> A, Action<AppState> B)[]
        {
            ("counts",
                s => s.Active.Collection["a.webp"] = 1,
                s => s.Active.Collection["a.webp"] = 2),
            ("name",
                s => s.Profiles[0] = s.Profiles[0] with { Name = "Here" },
                s => s.Profiles[0] = s.Profiles[0] with { Name = "There" }),
            ("in-game name",
                s => s.Profiles[0] = s.Profiles[0] with { InGameName = "Here" },
                s => s.Profiles[0] = s.Profiles[0] with { InGameName = "There" }),
            ("trade board",
                s => s.Profiles[0] = s.Profiles[0] with { TradeBoard = ["a.webp"] },
                s => s.Profiles[0] = s.Profiles[0] with { TradeBoard = ["b.webp"] }),
            ("default plan",
                s => s.Profiles[0] = s.Profiles[0] with
                     { Targets = new TargetSettings(new() { [0] = 2 }, new()) },
                s => s.Profiles[0] = s.Profiles[0] with
                     { Targets = new TargetSettings(new() { [0] = 3 }, new()) }),
            ("per-set plan",
                s => s.Profiles[0] = s.Profiles[0] with
                     { Targets = new TargetSettings(new() { [0] = 1 },
                         new() { ["A1"] = new() { [0] = 2 } }) },
                s => s.Profiles[0] = s.Profiles[0] with
                     { Targets = new TargetSettings(new() { [0] = 1 },
                         new() { ["A1"] = new() { [0] = 3 } }) }),
            ("hourglasses",
                s => s.Profiles[0] = s.Profiles[0] with
                     { Resources = Resources.Empty with { PackHourglasses = 3 } },
                s => s.Profiles[0] = s.Profiles[0] with
                     { Resources = Resources.Empty with { PackHourglasses = 9 } }),
            ("shinedust",
                s => s.Profiles[0] = s.Profiles[0] with
                     { Resources = Resources.Empty with { Shinedust = 3 } },
                s => s.Profiles[0] = s.Profiles[0] with
                     { Resources = Resources.Empty with { Shinedust = 9 } }),
            ("pack points",
                s => s.Active.Resources.PackPointsBySet["A1"] = 30,
                s => s.Active.Resources.PackPointsBySet["A1"] = 90),
            ("decks",
                s => s.Active.Decks.Add(new SavedDeck("d1", "Here", [1], [EnergyType.Fire])),
                s => s.Active.Decks.Add(new SavedDeck("d1", "There", [2], [EnergyType.Water]))),
            ("chase lists",
                s => s.Active.ChaseLists.Add(new ChaseList("w1", "Here", new() { ["a.webp"] = 1 })),
                s => s.Active.ChaseLists.Add(new ChaseList("w1", "There", new() { ["a.webp"] = 2 }))),
            ("stamina",
                s => s.Profiles[0] = s.Profiles[0] with
                     { Resources = Resources.Empty with
                         { Wonder = new ResourcePool(1, 0) { AsOf = noon } } },
                s => s.Profiles[0] = s.Profiles[0] with
                     { Resources = Resources.Empty with
                         { Wonder = new ResourcePool(4, 0) { AsOf = noon.AddHours(1) } } }),
        };

        foreach (var (what, editA, editB) in changes)
        {
            var ancestor = AppState.Fresh();

            var local = Clone(ancestor);
            editA(local);
            var remote = Clone(ancestor);
            editB(remote);

            foreach (var (mode, report) in new (string, MergeReport)[]
            {
                ("merge", StateMerge.Merge(local, remote, ancestor).Report),
                ("first pair", StateMerge.FirstPair(local, remote).Report),
            })
            {
                Assert.False(report.Conflicts > 0 && report.Notes.Count == 0,
                    $"{what}, on {mode}: reported {report.Conflicts} conflicts and said nothing "
                    + "about any of them, which renders as an empty message box.");

                Assert.DoesNotContain("", report.Notes);
            }
        }
    }

    [Fact]
    public void Pairing_deletes_nothing_on_either_side()
    {
        // With no ancestor nothing can be a deletion, and a first pair that quietly dropped rows
        // would be the worst possible introduction to the feature.
        var phone = AppState.Fresh();
        phone.Active.Decks.Add(new SavedDeck("d1", "Phone deck", [1], [EnergyType.Fire]));

        var laptop = AppState.Fresh();
        laptop.Active.Decks.Add(new SavedDeck("d2", "Laptop deck", [2], [EnergyType.Water]));

        var merged = StateMerge.FirstPair(phone, laptop).State;

        Assert.Equal(2, merged.Active.Decks.Count);
    }

    // ---- The unloaded-state rail ----------------------------------------------------------

    [Fact]
    public void A_state_that_was_never_loaded_is_recognisable_before_it_is_merged()
    {
        // The whole point of the check: a fresh AppState and a genuinely empty one are equal by
        // value, so this is what the caller has instead -- "is there anything here to lose".
        Assert.True(StateMerge.NothingRecorded(AppState.Fresh()));
        Assert.False(StateMerge.NothingRecorded(With(c => c["a.webp"] = 1)));
    }

    [Fact]
    public void A_collection_the_user_reset_is_not_mistaken_for_one_that_never_loaded()
    {
        // The two are identical apart from the tombstones: no cards, no logs, nothing. Getting
        // this wrong would block every sync after a reset, telling the user their collection had
        // failed to load when they had just cleared it themselves.
        var at = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var reset = Deleted(Clone(Logged(at, "x.webp")));

        Assert.Empty(reset.Active.PackLog);
        Assert.False(StateMerge.NothingRecorded(reset));
    }

    [Fact]
    public void A_named_but_empty_collection_still_counts_as_nothing_recorded()
    {
        // Renaming a collection, or making a second one, is not something to lose.
        var state = AppState.Fresh();
        var named = state with
        {
            Profiles = [state.Active with { Name = "Alt account" }, Profile.NewDefault("p2", "Main")],
        };

        Assert.True(StateMerge.NothingRecorded(named));
    }

    [Fact]
    public void A_logged_pack_alone_is_enough_to_count_as_recorded()
    {
        var state = AppState.Fresh();
        var logged = state with
        {
            Profiles =
            [
                state.Active with
                {
                    PackLog = [new PackOpenEvent(DateTimeOffset.UnixEpoch, "A1", "pikachu", "", [])],
                },
            ],
        };

        Assert.False(StateMerge.NothingRecorded(logged));
    }

    [Fact]
    public void Merging_an_unloaded_state_would_delete_everything_which_is_what_the_rail_prevents()
    {
        // Not a fix for the merge -- this documents WHY the caller must not reach it. The rules
        // here are right: local lacks what the ancestor had, so it was deleted here. They are
        // right about a user's deletion and catastrophic about a state that never loaded, and
        // nothing inside a three-way merge can tell those apart. SyncService checks first.
        var ancestor = With(c => { c["a.webp"] = 1; c["b.webp"] = 2; });
        var remote = Clone(ancestor);
        var unloaded = AppState.Fresh();

        var merged = StateMerge.Merge(unloaded, remote, ancestor).State;

        Assert.Empty(Owned(merged));
        Assert.True(StateMerge.NothingRecorded(unloaded));
        Assert.False(StateMerge.NothingRecorded(ancestor));
    }
}
