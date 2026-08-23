using Microsoft.AspNetCore.Components;

namespace PackProphet.Services;

/// <summary>
/// Remembers where the user was before they opened a detail page, so "back" returns there.
///
/// The browser's own history.back() is the obvious answer and the wrong one: card detail is
/// reached from four different surfaces, and on a deep link — a shared URL, a reload, a
/// bookmark — there is no in-app history to go back to, so it would walk the user out of the
/// app entirely. Tracking it here means back always lands somewhere inside PackProphet.
///
/// Detail pages are deliberately not recorded, so opening one card from another still returns
/// to the list rather than bouncing between cards.
/// </summary>
public sealed class NavHistory : IDisposable
{
    private readonly NavigationManager _nav;

    public NavHistory(NavigationManager nav)
    {
        _nav = nav;
        _nav.LocationChanged += OnLocationChanged;
        Remember(_nav.Uri);
    }

    /// <summary>The last non-detail page visited, relative to the app base. Never null.</summary>
    public string LastListPage { get; private set; } = "collection";

    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e) =>
        Remember(e.Location);

    private void Remember(string uri)
    {
        var relative = _nav.ToBaseRelativePath(uri);
        if (IsDetail(relative)) return;

        // An empty path is the app root, which redirects; sending "back" there would be a
        // bounce rather than a return.
        LastListPage = relative.Length == 0 ? "collection" : relative;
    }

    private static bool IsDetail(string relative) =>
        relative.StartsWith("card/", StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _nav.LocationChanged -= OnLocationChanged;
}
