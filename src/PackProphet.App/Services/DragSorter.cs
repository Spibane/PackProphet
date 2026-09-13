namespace PackProphet.Services;

using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;

/// <summary>
/// Drag-to-reorder for a shelf, shared by the two pages that have one.
///
/// The pages are otherwise unalike -- a deck shelf and a chase shelf, each in a table and a grid --
/// and what they have in common is exactly this: a container, items carrying an id, a handle to
/// press, and one call when something lands somewhere new. Duplicated in both, the two copies would
/// be identical right up until one of them was fixed.
///
/// The DOM work is in js/dragsort.js and the reason it is pointer events rather than HTML5 drag is
/// written there.
/// </summary>
public sealed class DragSorter : IAsyncDisposable
{
    private readonly Func<string, int, Task> _onDrop;
    private DotNetObjectReference<DragSorter>? _self;
    private IJSObjectReference? _module;

    private DragSorter(Func<string, int, Task> onDrop) => _onDrop = onDrop;

    /// <summary>
    /// Wires the module to <paramref name="host"/>. Call once the element exists — after the first
    /// render — and dispose with the component.
    /// </summary>
    public static async Task<DragSorter?> AttachAsync(
        IJSRuntime js, ElementReference host, Func<string, int, Task> onDrop)
    {
        var sorter = new DragSorter(onDrop);
        try
        {
            sorter._module = await js.InvokeAsync<IJSObjectReference>("import", "./js/dragsort.js");
            sorter._self = DotNetObjectReference.Create(sorter);
            await sorter._module.InvokeVoidAsync("attach", host, sorter._self);
            return sorter;
        }
        catch
        {
            // Reordering is one of three ways to arrange a shelf and the only one that needs
            // JavaScript. If the module will not load, the selects and the move buttons still
            // work, so this is a lost convenience rather than a broken page.
            await sorter.DisposeAsync();
            return null;
        }
    }

    /// <summary>Called once by dragsort.js when an item is released somewhere new.</summary>
    [JSInvokable]
    public Task Dropped(string id, int index) => _onDrop(id, index);

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("dispose"); }
            catch (JSDisconnectedException) { /* the circuit is gone; so is the listener */ }
            catch (ObjectDisposedException) { }
            await _module.DisposeAsync();
            _module = null;
        }

        _self?.Dispose();
        _self = null;
    }
}
