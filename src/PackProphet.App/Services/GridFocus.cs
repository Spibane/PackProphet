namespace PackProphet.Services;

/// <summary>
/// Lets anything on the page hand keyboard focus to the card grid.
///
/// It exists because "just Tab into the grid" turned out to be a fiction. Measured on the
/// Collection page, reaching it means passing a series picker, fourteen set tabs, four filters and
/// seven layout and bulk-action controls — twenty-six stops, after the dozen in the nav. A keyboard
/// user would give up long before arriving, and nothing on the way there says the grid is operable
/// at all.
///
/// A tiny registry rather than a parameter chain: the grid is nested several components deep inside
/// whichever page rendered it, and the command palette that offers the jump is mounted in the
/// layout, so they have no relationship to pass a callback along.
/// </summary>
public sealed class GridFocus
{
    private object? _owner;
    private Func<Task>? _focus;

    /// <summary>True when a grid is on screen and can take focus.</summary>
    public bool Available => _focus is not null;

    /// <summary>
    /// Claimed by a grid when it mounts. Last one wins, which is the right rule for the one page
    /// that renders two: the deck builder's own picker is the one you want.
    /// </summary>
    /// <param name="owner">
    /// The grid itself, used to decide whose release counts. Identity has to be carried separately
    /// from the callback, because a delegate built from a method group is a NEW object every time
    /// it is written - so comparing the callbacks by reference never matched and the registration
    /// was never released, leaving the palette offering a jump into a page that had gone.
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
    /// Without this, coming back means the cursor has been forgotten: focus is outside the grid, the
    /// list is scrolled to the top, and the card you were reading about is somewhere below. Every
    /// press of `i` cost you your place.
    /// </summary>
    public void ReturnTo(string cardKey) => _returnTo = cardKey;

    /// <summary>
    /// Consumed once, by the next grid to mount. Once, deliberately: arriving at the collection any
    /// other way - a nav link, the palette, a bookmark - must not have focus yanked into the grid.
    /// </summary>
    public string? TakeReturn()
    {
        var key = _returnTo;
        _returnTo = null;
        return key;
    }
}
