using Microsoft.JSInterop;
using PackProphet.State;

namespace PackProphet.Services;

/// <summary>
/// Persists state to the browser's localStorage. Writes are best-effort: a failure is surfaced to
/// the user (see js/store.js) rather than swallowed.
/// </summary>
public sealed class LocalStorageStateStore : IStateStore, IAsyncDisposable
{
    private const string Key = "packprophet.state.v1";

    /// <summary>
    /// Where an unreadable payload is set aside before anything overwrites it.
    ///
    /// Booting fresh from corrupt data keeps the app openable, but on its own it destroys the
    /// evidence: the app comes up with an empty collection, the debounce saves that empty state
    /// over the damaged one, and a partly recoverable collection is gone. Truncated storage is a
    /// real outcome under quota pressure, so the raw text is copied aside first and left alone.
    /// </summary>
    private const string SalvageKey = "packprophet.state.unreadable";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public LocalStorageStateStore(IJSRuntime js) => _js = js;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/store.js");

    public async Task<AppState> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var module = await ModuleAsync();
            var json = await module.InvokeAsync<string?>("load", ct, Key);

            var state = StateSerializer.Deserialize(json);
            if (state is not null) return state;

            // Non-empty but unreadable: something was there and we cannot use it. Copy it aside
            // before the first save overwrites it. An empty slot is an ordinary first run and
            // needs none of this.
            if (!string.IsNullOrWhiteSpace(json))
            {
                LoadFailed = true;
                try { await module.InvokeVoidAsync("save", ct, SalvageKey, json); }
                catch (JSException) { /* nothing better to try */ }
            }

            return AppState.Fresh();
        }
        catch (JSException)
        {
            // Storage is unavailable entirely - private mode, or site data blocked. The app still
            // works for this session, but nothing will survive a reload, and the user has to be
            // told rather than left to discover it.
            StorageUnavailable = true;
            return AppState.Fresh();
        }
    }

    /// <summary>
    /// True once a payload was found that could not be read. Its raw text is kept under a separate
    /// key so it can still be exported by hand.
    /// </summary>
    public bool LoadFailed { get; private set; }

    /// <summary>True when the browser gives no storage at all, so nothing will survive a reload.</summary>
    public bool StorageUnavailable { get; private set; }

    public async Task<bool> SaveAsync(AppState state, CancellationToken ct = default)
    {
        try
        {
            var module = await ModuleAsync();
            // The return value was being discarded. localStorage refuses a write once the quota is
            // reached, so every edit after that point was lost while the app carried on as though
            // it had saved.
            return await module.InvokeAsync<bool>("save", ct, Key, StateSerializer.Serialize(state));
        }
        catch (JSException)
        {
            return false;
        }
    }

    public async Task ExportAsync(AppState state, string filename)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("download", filename, StateSerializer.Export(state));
    }

    /// <summary>
    /// Hand the user any text file. The collection exports go through here rather than through
    /// <see cref="ExportAsync"/>, which serialises the whole app state and is a backup — a
    /// different thing with a different audience.
    /// </summary>
    public async Task DownloadAsync(string filename, string text, string mime = "text/csv")
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("download", filename, text, mime);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try { await _module.DisposeAsync(); }
        catch (JSDisconnectedException) { /* page going away */ }
    }
}
