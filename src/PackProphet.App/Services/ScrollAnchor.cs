namespace PackProphet.Services;

using Microsoft.JSInterop;

/// <summary>
/// Holds one element still across a re-render, for the two Record screens that grow a summary
/// above the control you are using. What it does and why the browser will not do it is in
/// js/anchor.js.
///
/// A page loads one of these once and calls <see cref="KeepAsync"/> from its after-render. The
/// selector is the page's, because the element belongs to a component the page does not hold a
/// reference to — the card grid's wrapper, the picker's search row — and naming it is cheaper than
/// threading an ElementReference out through a component that has no other reason to expose one.
/// </summary>
public sealed class ScrollAnchor : IAsyncDisposable
{
    private readonly IJSObjectReference _module;

    private ScrollAnchor(IJSObjectReference module) => _module = module;

    /// <summary>
    /// Null if the module will not load. Nothing on either page depends on this working: without
    /// it the screens behave exactly as they did before, which is to say they jump.
    /// </summary>
    public static async Task<ScrollAnchor?> LoadAsync(IJSRuntime js)
    {
        try
        {
            return new ScrollAnchor(
                await js.InvokeAsync<IJSObjectReference>("import", "./js/anchor.js"));
        }
        catch
        {
            return null;
        }
    }

    /// <param name="token">
    /// Which screen this measurement belongs to. Change it and the next call re-marks rather than
    /// compensating — a page that has swapped its whole contents has not grown, it has changed.
    /// </param>
    public async Task KeepAsync(string selector, string token)
    {
        try { await _module.InvokeVoidAsync("keep", selector, token); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }
}
