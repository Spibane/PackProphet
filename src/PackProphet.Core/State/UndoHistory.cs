namespace PackProphet.State;

/// <summary>
/// The undo and redo stacks, and the one rule that makes them behave sensibly: what a step is
/// allowed to put back.
///
/// It lives here rather than inside the session because the interesting part is not the stack, it
/// is that PREFERENCES are not part of the history. Theme, grid columns, the parallel-foil count,
/// board settings and the rest are written straight to state and never record a step - but a step
/// recorded by a card edit still carries a snapshot of whatever the preferences were at that
/// moment. Putting that snapshot back wholesale silently reverted every preference changed since:
/// tap a card, switch to dark mode, press Ctrl+Z, and the theme flips back with no way to tell
/// why. An imported backup is the one step where preferences ARE part of what changed, so that
/// step says so and undoing it restores them.
///
/// Bounded, because drag-select and fast tapping would otherwise grow it without limit and nobody
/// needs to undo a thousand taps.
/// </summary>
public sealed class UndoHistory
{
    private readonly record struct Step(AppState State, bool RestorePrefs);

    private readonly List<Step> _undo = [];
    private readonly List<Step> _redo = [];
    private readonly int _depth;

    public UndoHistory(int depth) => _depth = Math.Max(1, depth);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <param name="includesPrefs">
    /// True only where the step itself changed preferences, which in practice means importing a
    /// backup. False for every ordinary edit.
    /// </param>
    public void Record(AppState before, bool includesPrefs = false)
    {
        _undo.Add(new Step(before, includesPrefs));
        if (_undo.Count > _depth) _undo.RemoveAt(0);

        // A new edit invalidates the redo branch: what it would have replayed no longer follows
        // from the current state.
        _redo.Clear();
    }

    /// <summary>
    /// Forget everything. Used when switching profiles: the stack holds whole app states, so an
    /// undo taken afterwards would restore the OTHER collection - silently switching back and
    /// discarding whatever had just been done.
    /// </summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public AppState? Undo(AppState current) => Move(_undo, _redo, current);

    public AppState? Redo(AppState current) => Move(_redo, _undo, current);

    private static AppState? Move(List<Step> from, List<Step> to, AppState current)
    {
        if (from.Count == 0) return null;

        var step = from[^1];
        from.RemoveAt(from.Count - 1);

        // The flag travels with the step so redo undoes the undo exactly.
        to.Add(new Step(current, step.RestorePrefs));

        return step.RestorePrefs
            ? step.State
            : step.State with { Prefs = current.Prefs };
    }
}
