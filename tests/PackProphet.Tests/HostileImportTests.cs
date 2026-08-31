namespace PackProphet.Tests;

using PackProphet.Domain;
using PackProphet.State;

/// <summary>
/// The import path is the one place the app adopts data it did not write. Every case here was
/// ACCEPTED with nulls intact before normalising on read, which bricked the app: the state is
/// adopted, queued to localStorage, and the first render throws — after which every reload reads
/// the same payload back and throws again, with no route out but clearing site data by hand.
///
/// No attacker is needed. A hand-edited backup, a truncated download, or a file from a future
/// version that renamed a field all land here.
/// </summary>
public class HostileImportTests
{
    private static AppState Load(string json)
    {
        var state = StateSerializer.Deserialize(json);
        Assert.NotNull(state);
        return state!;
    }

    [Fact]
    public void Every_null_collection_comes_back_empty_rather_than_null()
    {
        var state = Load("""
        {"schemaVersion":3,"activeProfileId":"a","prefs":null,
         "profiles":[{"id":"a","name":"A","collection":null,"targets":null,"decks":null,
         "wishlists":null,"packLog":null,"wonderLog":null,"resources":null,"tradeBoard":null}]}
        """);

        var p = state.Active;
        Assert.Empty(p.Collection);
        Assert.Empty(p.Decks);
        Assert.Empty(p.ChaseLists);
        Assert.Empty(p.PackLog);
        Assert.Empty(p.WonderLog);
        Assert.Empty(p.TradeBoard);
        Assert.NotNull(p.Resources);
        Assert.NotNull(state.Prefs);

        // The plan matters most: a v3 payload with "targets": null never reached the migration,
        // so every screen that asks what counts as complete threw.
        Assert.NotNull(p.Targets);
        Assert.NotEmpty(p.Targets.PlanFor().CopiesByTier);
    }

    [Fact]
    public void Resources_are_rebuilt_pool_by_pool()
    {
        var state = Load("""
        {"schemaVersion":3,"activeProfileId":"a","profiles":[{"id":"a","name":"A",
         "collection":{},"targets":null,"decks":[],"wishlists":[],"packLog":[],"wonderLog":[],
         "resources":{"wonder":null,"trade":null,"packHourglasses":-9,"packPointsBySet":null,
         "shinedust":-1,"premium":false,"nextFreePackAt":null}}]}
        """);

        var r = state.Active.Resources;
        Assert.Equal(ResourcePool.Empty, r.Wonder);
        Assert.Equal(ResourcePool.Empty, r.Trade);
        Assert.Empty(r.PackPointsBySet);
        Assert.Equal(0, r.PackHourglasses);
        Assert.Equal(0, r.Shinedust);
    }

    [Fact]
    public void Impossible_owned_counts_are_dropped_not_stored()
    {
        // Collection filters these at runtime, so keeping them in the saved state made the file
        // and the live app disagree about how many cards you own.
        var state = Load("""
        {"schemaVersion":3,"activeProfileId":"a","profiles":[{"id":"a","name":"A",
         "collection":{"real.webp":2,"zero.webp":0,"negative.webp":-5,"":7},
         "targets":null,"decks":[],"wishlists":[],"packLog":[],"wonderLog":[],"resources":null}]}
        """);

        Assert.Equal(new Dictionary<string, int> { ["real.webp"] = 2 }, state.Active.Collection);
    }

    [Fact]
    public void Duplicate_profile_ids_are_separated()
    {
        // Mutate rewrites every profile whose id matches, so two profiles sharing an id would
        // silently edit together — one collection appearing to mirror another.
        var state = Load("""
        {"schemaVersion":3,"activeProfileId":"dup","profiles":[
         {"id":"dup","name":"One","collection":{},"targets":null,"decks":[],"wishlists":[],
          "packLog":[],"wonderLog":[],"resources":null},
         {"id":"dup","name":"Two","collection":{},"targets":null,"decks":[],"wishlists":[],
          "packLog":[],"wonderLog":[],"resources":null}]}
        """);

        Assert.Equal(2, state.Profiles.Count);
        Assert.Equal(2, state.Profiles.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void A_nameless_profile_and_a_dangling_active_id_both_resolve()
    {
        var state = Load("""
        {"schemaVersion":3,"activeProfileId":"nothing-with-this-id","profiles":[{"id":null,
         "name":null,"collection":{},"targets":null,"decks":[],"wishlists":[],"packLog":[],
         "wonderLog":[],"resources":null}]}
        """);

        var p = Assert.Single(state.Profiles);
        Assert.False(string.IsNullOrWhiteSpace(p.Id));
        Assert.False(string.IsNullOrWhiteSpace(p.Name));
        Assert.Equal(p.Id, state.ActiveProfileId);
    }

    [Fact]
    public void Decks_and_chase_lists_missing_their_contents_are_repaired()
    {
        var state = Load("""
        {"schemaVersion":3,"activeProfileId":"a","profiles":[{"id":"a","name":"A","collection":{},
         "targets":null,"resources":null,"packLog":[{"at":"2026-01-01T00:00:00Z","set":"A1",
         "pack":"Mewtwo","variant":"Regular Pack","ownershipKeys":null}],"wonderLog":[],
         "decks":[{"id":"d","name":"D","deckBuilderNrs":null,"energies":null},
                  {"id":"","name":"no id","deckBuilderNrs":[],"energies":[]}],
         "wishlists":[{"id":"w","name":"W","wanted":null}]}]}
        """);

        var p = state.Active;

        // The deck with no id is dropped: nothing could ever address it, and it would show as a
        // phantom row that cannot be opened or deleted.
        var deck = Assert.Single(p.Decks);
        Assert.Empty(deck.DeckBuilderNrs);
        Assert.Empty(deck.Energies);

        Assert.Empty(Assert.Single(p.ChaseLists).Wanted);
        Assert.Empty(Assert.Single(p.PackLog).OwnershipKeys);
    }

    [Fact]
    public void A_payload_with_no_usable_profile_is_refused_outright()
    {
        // Refused, not repaired into an empty collection: silently replacing someone's data with a
        // blank profile is the one outcome worse than saying the file is unreadable.
        Assert.Null(StateSerializer.Deserialize("""
        {"schemaVersion":3,"activeProfileId":"a","profiles":[null]}
        """));

        Assert.Null(StateSerializer.Deserialize("""{"schemaVersion":3,"profiles":[]}"""));
        Assert.Null(StateSerializer.Deserialize("not json at all"));
        Assert.Null(StateSerializer.Deserialize(""));
    }

    [Fact]
    public void Prefs_dictionaries_survive_being_null()
    {
        // AssumedRateDonors is read on every engine rebuild, so a null there threw before the
        // first pack was ever ranked.
        var state = Load("""
        {"schemaVersion":3,"activeProfileId":"a","prefs":{"theme":"dark",
         "assumedRateDonors":null,"availableLimitedPacks":null,"pinnedPacks":null},
         "profiles":[{"id":"a","name":"A","collection":{},"targets":null,"decks":[],
         "wishlists":[],"packLog":[],"wonderLog":[],"resources":null}]}
        """);

        Assert.Empty(state.Prefs.AssumedRateDonors);
        Assert.Empty(state.Prefs.AvailableLimitedPacks);
        Assert.Empty(state.Prefs.PinnedPacks);
        Assert.Equal("dark", state.Prefs.Theme);
    }

    [Fact]
    public void A_blank_pinned_pack_is_dropped()
    {
        // A pin is a pack key the log picker matches against its own list. A blank one matches
        // nothing and can never be unpinned from the UI, so it would sit in the save forever.
        var state = Load("""
        {"schemaVersion":3,"activeProfileId":"a",
         "prefs":{"pinnedPacks":["A1:Charizard","","   ",null]},
         "profiles":[{"id":"a","name":"A","collection":{},"targets":null,"decks":[],
         "wishlists":[],"packLog":[],"wonderLog":[],"resources":null}]}
        """);

        Assert.Equal(["A1:Charizard"], state.Prefs.PinnedPacks);
    }

    [Fact]
    public void A_normal_backup_is_unchanged_by_any_of_this()
    {
        // The repair must be invisible to real data, or it is a new source of bugs rather than a
        // fix for one.
        var original = AppState.Fresh();
        var profile = original.Active with
        {
            Collection = new Dictionary<string, int> { ["a.webp"] = 3 },
            TradeBoard = ["b.webp"],
            Resources = Resources.Empty with { Shinedust = 5000, PackHourglasses = 1751 }
        };
        original = original with { Profiles = [profile] };

        var back = StateSerializer.Deserialize(StateSerializer.Export(original))!;

        Assert.Equal(3, back.Active.Collection["a.webp"]);
        Assert.Equal(["b.webp"], back.Active.TradeBoard);
        Assert.Equal(5000, back.Active.Resources.Shinedust);
        Assert.Equal(1751, back.Active.Resources.PackHourglasses);
        Assert.Equal(profile.Id, back.ActiveProfileId);
    }
}

/// <summary>
/// Numbers as a person actually supplies them. The app runs with InvariantGlobalization, so a
/// plain int.TryParse refuses a thousands separator — and refusing meant the field silently became
/// ZERO and was then saved over a real balance. The game prints its own figures with separators,
/// so pasting one in is the ordinary way these fields get filled.
/// </summary>
public class TypedNumberTests
{
    [Theory]
    [InlineData("1751", 1751)]
    [InlineData("1,751", 1751)]
    [InlineData(" 1751 ", 1751)]
    [InlineData("12,345", 12345)]
    [InlineData("1'751", 1751)]          // Swiss grouping
    [InlineData("1 751", 1751)]     // non-breaking space, which copy-paste brings along
    [InlineData("0", 0)]
    public void Reads_a_count_the_way_it_was_pasted(string text, int expected)
    {
        Assert.True(PackProphet.Text.Num.TryCount(text, out var value), text);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12.5")]      // fractional hourglasses mean the input was misread
    [InlineData("1e6")]
    [InlineData(null)]
    public void Declines_what_is_not_a_whole_count(string? text)
    {
        // Declining matters as much as accepting: a caller that gets `false` can leave the stored
        // value alone, where a silent zero overwrites it.
        Assert.False(PackProphet.Text.Num.TryCount(text, out _));
    }

    [Fact]
    public void A_negative_count_is_read_and_left_for_the_caller_to_clamp()
    {
        // Parsed rather than rejected, because the clamp belongs where the field's range is known.
        Assert.True(PackProphet.Text.Num.TryCount("-5", out var value));
        Assert.Equal(-5, value);
    }
}
