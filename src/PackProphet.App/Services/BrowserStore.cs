namespace PackProphet.Services;

using Microsoft.JSInterop;

/// <summary>
/// Read and write single localStorage keys, for the few things that are not part of the collection:
/// the pairing code, the last state both devices agreed on, and this install's id.
///
/// Separate from <see cref="LocalStorageStateStore"/> on purpose. That class is the persistence of
/// the collection and owns exactly one key; giving it a general key-value API would invite the next
/// feature to scatter state through it.
/// </summary>
public sealed class BrowserStore : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public BrowserStore(IJSRuntime js) => _js = js;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/store.js");

    public async Task<string?> GetAsync(string key)
    {
        try { return await (await ModuleAsync()).InvokeAsync<string?>("load", key); }
        catch (JSException) { return null; }        // storage blocked; caller treats it as unset
    }

    /// <summary>False when the write did not happen -- quota, or storage refused entirely.</summary>
    public async Task<bool> SetAsync(string key, string value)
    {
        try { return await (await ModuleAsync()).InvokeAsync<bool>("save", key, value); }
        catch (JSException) { return false; }
    }

    public async Task RemoveAsync(string key)
    {
        try { await (await ModuleAsync()).InvokeVoidAsync("remove", key); }
        catch (JSException) { /* nothing to remove from */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try { await _module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }
}
