namespace PackProphet.Services;

/// <summary>
/// Lets anything on the page hand keyboard focus to the card grid.
///
/// Tabbing to the grid is impractical: on the Collection page it means passing a series picker,
/// fourteen set tabs, four filters and seven layout and bulk-action controls — twenty-six stops,
/// after the dozen in the nav — and nothing on the way says the grid is operable.
///
/// A registry rather than a parameter chain: the grid is nested several components deep inside
/// whichever page rendered it, and the command palette that offers the jump is mounted in the
/// layout, so there is no relationship to pass a callback along.
/// </summary>
public sealed class GridFocus
{
    private object? _owner;
    private Func<Task>? _focus;

    /// <summary>True when a grid is on screen and can take focus.</summary>
    public bool Available => _focus is not null;

    /// <summary>
    /// Claimed by a grid when it mounts. Last one wins, which is what the one page rendering two
    /// wants: the deck builder's own picker.
    /// </summary>
    /// <param name="owner">
    /// The grid itself, used to decide whose release counts. Identity is carried separately from
    /// the callback because a delegate built from a method group is a new object every time it is
    /// written, so comparing callbacks by reference never matched and the registration was never
    /// released, leaving the palette offering a jump into a page that had gone.
    /// </param>
    public void Register(object owner, Func<Task> focus)
    {
        _owner = owner;
        _focus = focus;
    }

    /// <summary>
    /// Released on teardown. A stale release is ignored: a page swap mounts the new grid before it
    /// disposes the old one, so the old one's teardown must not clear the new one's claim.
    /// </summary>
    public void Release(object owner)
    {
        if (!ReferenceEquals(_owner, owner)) return;

        _owner = null;
        _focus = null;
    }

    public Task FocusAsync() => _focus?.Invoke() ?? Task.CompletedTask;

    private string? _returnTo;

    /// <summary>
    /// Note the card the keyboard just opened, so returning from its detail page lands back on it.
    ///
    /// Without this, coming back leaves focus outside the grid, the list scrolled to the top, and
    /// the card somewhere below.
    /// </summary>
    public void ReturnTo(string cardKey) => _returnTo = cardKey;

    /// <summary>
    /// Consumed once, by the next grid to mount. Once, so that arriving at the collection any other
    /// way — a nav link, the palette, a bookmark — does not have focus pulled into the grid.
    /// </summary>
    public string? TakeReturn()
    {
        var key = _returnTo;
        _returnTo = null;
        return key;
    }
}
