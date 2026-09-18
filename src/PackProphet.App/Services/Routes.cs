namespace PackProphet.Services;

/// <summary>
/// Which page a URL is, where a <c>NavLink</c> cannot answer it.
///
/// THE COLLECTION HAS TWO URLS
/// ==================================================================================
/// It is served at the app's root and at <c>/collection</c>, because the root has to show
/// something and the collection is what it shows. NavLink matches one href: with
/// <c>NavLinkMatch.All</c> the nav entry pointing at <c>collection</c> is not active at the root,
/// and with <c>NavLinkMatch.Prefix</c> an empty href is a prefix of every path and it would be
/// active everywhere. Neither says what is true.
///
/// So the root rendered the collection with nothing lit up in either nav — on the one URL every
/// first visit arrives at, and the one the deployed site is bookmarked as. The page was right and
/// the nav looked like it had no home.
///
/// This is the only route in the app with two URLs, which is why it is the only entry whose active
/// state is computed rather than left to NavLink. Shared between the two navs rather than written
/// in each, on the same argument as NavIcon and PackMark: the desktop row and the tab bar are the
/// same nav at two widths, and two copies of "is this the collection" is two answers waiting to
/// disagree.
/// </summary>
public static class Routes
{
    /// <summary>
    /// True when this base-relative path is one the collection renders at.
    ///
    /// Takes the path as a string rather than a NavigationManager so the rule can be read back in
    /// a test without a rendered component. Callers pass
    /// <c>Nav.ToBaseRelativePath(Nav.Uri)</c>, which carries any query and fragment with it —
    /// <c>/collection?set=A2</c> is still the collection, and the set filter is not the nav's
    /// business.
    /// </summary>
    public static bool IsCollection(string? relativePath)
    {
        var path = relativePath ?? "";

        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0) path = path[..cut];

        path = path.Trim('/');

        return path.Length == 0
            || path.Equals("collection", StringComparison.OrdinalIgnoreCase);
    }
}
