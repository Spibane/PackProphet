using Microsoft.JSInterop;
using PackProphet.State;

namespace PackProphet.Services;

/// <summary>
/// Persists state to the browser's localStorage. Writes are best-effort: a failure is
/// surfaced to the user (see js/store.js) rather than swallowed, because silently losing
/// edits is the worst outcome for a tracker.
/// </summary>
public sealed class LocalStorageStateStore : IStateStore, IAsyncDisposable
{
    private const string Key = "packprophet.state.v1";

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
            // A corrupt or future-schema payload yields null, and we start fresh rather than
            // refuse to boot.
            return StateSerializer.Deserialize(json) ?? AppState.Fresh();
        }
        catch (JSException)
        {
            return AppState.Fresh();
        }
    }

    public async Task SaveAsync(AppState state, CancellationToken ct = default)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("save", ct, Key, StateSerializer.Serialize(state));
    }

    public async Task ExportAsync(AppState state, string filename)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("download", filename, StateSerializer.Export(state));
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try { await _module.DisposeAsync(); }
        catch (JSDisconnectedException) { /* page going away */ }
    }
}
