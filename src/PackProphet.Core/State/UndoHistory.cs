namespace PackProphet.State;

/// <summary>
/// The undo and redo stacks, and the rule for what a step is allowed to put back.
///
/// Preferences are not part of the history. Theme, grid columns, the parallel-foil count, board
/// settings and the rest are written straight to state and never record a step — but a step
/// recorded by a card edit still carries a snapshot of whatever the preferences were at that
/// moment. Putting that snapshot back wholesale reverted every preference changed since: tap a
/// card, switch to dark mode, press Ctrl+Z, and the theme flips back. An imported backup is the one
/// step where preferences are part of what changed, so that step says so and undoing it restores
/// them.
///
/// Bounded, since drag-select and fast tapping would otherwise grow the stack without limit.
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
    /// undo taken afterwards would restore the other collection and discard what had just been
    /// done.
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
