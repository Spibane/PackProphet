namespace PackProphet.Tests;

using PackProphet.State;

public class UndoHistoryTests
{
    private static AppState With(int cards, string theme = "auto")
    {
        var state = AppState.Fresh();
        var profile = state.Active with
        {
            Collection = Enumerable.Range(0, cards).ToDictionary(i => $"c{i}.webp", _ => 1)
        };
        return state with { Profiles = [profile], Prefs = state.Prefs with { Theme = theme } };
    }

    [Fact]
    public void An_ordinary_undo_leaves_preferences_alone()
    {
        // The bug this exists for. Preferences never record a step of their own, but a step
        // recorded by a card edit carries a snapshot of them - so putting that snapshot back
        // reverted every preference changed since. Tap a card, switch to dark mode, press Ctrl+Z,
        // and the theme flipped back with nothing to explain why.
        var history = new UndoHistory(10);

        var before = With(cards: 1, theme: "auto");
        history.Record(before);

        var afterEditAndThemeChange = With(cards: 2, theme: "dark");

        var undone = history.Undo(afterEditAndThemeChange)!;

        Assert.Single(undone.Active.Collection);      // the card edit is rolled back
        Assert.Equal("dark", undone.Prefs.Theme);     // the theme is not
    }

    [Fact]
    public void Undoing_an_import_does_restore_preferences()
    {
        // An import is the one step where preferences are part of what changed, so undoing it has
        // to put the whole file back - otherwise half the import survives.
        var history = new UndoHistory(10);

        var mine = With(cards: 1, theme: "light");
        history.Record(mine, includesPrefs: true);

        var imported = With(cards: 50, theme: "dark");
        var undone = history.Undo(imported)!;

        Assert.Single(undone.Active.Collection);
        Assert.Equal("light", undone.Prefs.Theme);
    }

    [Fact]
    public void Redo_puts_back_exactly_what_undo_took_away()
    {
        var history = new UndoHistory(10);

        var before = With(cards: 1, theme: "auto");
        history.Record(before);

        var after = With(cards: 2, theme: "dark");

        var undone = history.Undo(after)!;
        var redone = history.Redo(undone)!;

        Assert.Equal(2, redone.Active.Collection.Count);
        // Still not touching preferences, in either direction.
        Assert.Equal("dark", redone.Prefs.Theme);
    }

    [Fact]
    public void A_new_edit_discards_the_redo_branch()
    {
        // What redo would replay no longer follows from the current state, so offering it would
        // apply a change out of order.
        var history = new UndoHistory(10);

        history.Record(With(1));
        history.Undo(With(2));
        Assert.True(history.CanRedo);

        history.Record(With(5));
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void The_history_is_bounded()
    {
        // Drag-select records a step per gesture and fast tapping one per tap, so an unbounded
        // stack would hold every state the app has ever been in.
        var history = new UndoHistory(3);

        for (var i = 0; i < 10; i++) history.Record(With(i));

        var current = With(99);
        for (var i = 0; i < 3; i++)
        {
            current = history.Undo(current)!;
            Assert.NotNull(current);
        }

        Assert.False(history.CanUndo);
        Assert.Null(history.Undo(current));
    }

    [Fact]
    public void Undo_on_an_empty_history_changes_nothing()
    {
        var history = new UndoHistory(5);

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(history.Undo(With(1)));
        Assert.Null(history.Redo(With(1)));
    }

    [Fact]
    public void Clearing_drops_both_directions()
    {
        // Called when switching profiles: the stack holds whole app states, so an undo afterwards
        // would silently switch back to the other collection.
        var history = new UndoHistory(5);

        history.Record(With(1));
        history.Undo(With(2));

        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }
}
