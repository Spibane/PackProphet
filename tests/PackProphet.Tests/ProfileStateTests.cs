namespace PackProphet.Tests;

using PackProphet.State;

public class ProfileStateTests
{
    [Fact]
    public void Several_profiles_and_the_active_one_survive_a_round_trip()
    {
        // Data loss if this ever breaks, and the silent kind: an alt whose cards do not come back
        // after a reload looks like the collection was wiped rather than like a serializer bug.
        var main = Profile.NewDefault("main", "Main");
        var alt = Profile.NewDefault("alt", "Alt") with
        {
            Collection = new Dictionary<string, int> { ["a.webp"] = 3 },
            Resources = Resources.Empty with { Shinedust = 12_345 }
        };

        var state = new AppState(AppState.CurrentSchemaVersion, [main, alt], alt.Id, new Prefs());

        var back = StateSerializer.Deserialize(StateSerializer.Serialize(state))!;

        Assert.Equal(2, back.Profiles.Count);
        Assert.Equal("alt", back.ActiveProfileId);
        Assert.Equal("Alt", back.Active.Name);
        Assert.Equal(3, back.Active.Collection["a.webp"]);
        Assert.Equal(12_345, back.Active.Resources.Shinedust);

        // The other profile is untouched, which is what makes the two independent.
        Assert.Empty(back.Profiles.First(p => p.Id == "main").Collection);
    }

    [Fact]
    public void An_active_id_naming_nothing_falls_back_rather_than_throwing()
    {
        // Reachable from a hand-edited or partially-restored backup, and the app has to boot.
        var only = Profile.NewDefault("only", "Only");
        var state = new AppState(AppState.CurrentSchemaVersion, [only], "gone", new Prefs());

        Assert.Equal("only", state.Active.Id);
    }

    [Fact]
    public void A_dismissed_evolution_bar_stays_dismissed()
    {
        // The same trap as the board settings: the default is TRUE, so a reader that treats a
        // missing value as the default would turn a deliberate dismissal back on at every reload -
        // and a notice that comes back after you closed it is worse than one you cannot close.
        var fresh = AppState.Fresh();
        Assert.True(fresh.Prefs.ShowEvolutionGaps);

        var state = fresh with { Prefs = fresh.Prefs with { ShowEvolutionGaps = false } };

        var back = StateSerializer.Deserialize(StateSerializer.Serialize(state))!;

        Assert.False(back.Prefs.ShowEvolutionGaps);
    }
}
