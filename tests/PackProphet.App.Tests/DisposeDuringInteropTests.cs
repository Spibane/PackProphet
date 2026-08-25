namespace PackProphet.App.Tests;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using PackProphet.Components;
using PackProphet.Services;

/// <summary>
/// Navigating away while a component's first-render interop is still in flight.
///
/// The grid imports three JS modules on first render, and each import is an await the renderer does
/// not wait for. Leaving /collection inside that window disposes the component, and the
/// continuation then went on using a <see cref="DotNetObjectReference{T}"/> that DisposeAsync had
/// already thrown away — Blazor reads it while serialising the call, so an ObjectDisposedException
/// came out of the render loop and was logged as "Unhandled exception rendering component". It also
/// registered the disposed grid with <see cref="GridFocus"/>, leaving the command palette offering
/// a jump into a page that had gone.
///
/// It never fired in ordinary use; only on a fast navigation, which is why it lived for a while.
/// </summary>
public class DisposeDuringInteropTests : AppHost
{
    /// <summary>
    /// A JS runtime that holds one call open, so the fast-navigation window can be opened on
    /// purpose and closed when the test says so.
    ///
    /// bUnit's own interop cannot stand in here. It refuses to hand out a pending
    /// <see cref="IJSObjectReference"/> at all, and — the part that matters — it records call
    /// arguments without serialising them, so a disposed object reference passes through it
    /// unnoticed and the bug under test would never show. This one reads the reference the way
    /// Blazor's serialiser does, which is the exact line the ObjectDisposedException came from.
    /// </summary>
    private sealed class ParkingJSRuntime : IJSRuntime
    {
        /// <summary>The call to hold open, as it appears in <see cref="Calls"/>.</summary>
        public string ParkOn { get; init; } = "";

        public List<string> Calls { get; } = [];

        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Let the parked call return, as the module finishing its download would.</summary>
        public void Release() => _gate.TrySetResult();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => Dispatch<TValue>(identifier, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args)
            => Dispatch<TValue>(identifier, args);

        private async ValueTask<TValue> Dispatch<TValue>(string identifier, object?[]? args)
        {
            var call = Describe(identifier, args);
            Calls.Add(call);

            // Serialisation happens as the call is made, before any awaiting — so this is where a
            // reference disposed a moment ago blows up.
            foreach (var arg in args ?? [])
                if (arg is DotNetObjectReference<CardGrid> reference)
                    _ = reference.Value;

            if (call == ParkOn) await _gate.Task;

            // An import resolves to a module, and calls on that module come back through here.
            if (typeof(TValue) == typeof(IJSObjectReference))
                return (TValue)(object)new Module(this);

            return default!;
        }

        /// <summary>The identifier, plus the module path for imports, which is what tells the
        /// three imports apart.</summary>
        private static string Describe(string identifier, object?[]? args) =>
            identifier == "import" && args is [string path, ..] ? $"import {path}" : identifier;

        private sealed class Module(ParkingJSRuntime runtime) : IJSObjectReference
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
                => runtime.Dispatch<TValue>(identifier, args);

            public ValueTask<TValue> InvokeAsync<TValue>(
                string identifier, CancellationToken cancellationToken, object?[]? args)
                => runtime.Dispatch<TValue>(identifier, args);
        }
    }

    private void Grid(IReadOnlyList<PocketCard> cards) =>
        RenderComponent<CardGrid>(p =>
        {
            p.Add(g => g.Cards, cards);
            p.Add(g => g.CountOf, _ => 0);
            p.Add(g => g.OnAdjust, EventCallback.Factory.Create<(PocketCard, int)>(this, _ => { }));
            p.Add(g => g.Columns, 4);
        });

    /// <summary>
    /// Render the grid, dispose it while <paramref name="parkOn"/> is still outstanding, then let
    /// that call land — and check that nothing came out of the resumed continuation.
    /// </summary>
    private async Task NothingEscapesWhenDisposedDuring(string parkOn)
    {
        var js = new ParkingJSRuntime { ParkOn = parkOn };
        Services.AddSingleton<IJSRuntime>(js);

        await ReadyAsync();
        var cards = Session.Index.All.DistinctBy(c => c.OwnershipKey).Take(8).ToArray();

        Grid(cards);
        Assert.Contains(parkOn, js.Calls);

        // The navigation.
        DisposeComponents();

        // And now the module lands, into a component that is no longer there.
        js.Release();

        // Give the parked continuation its turn before deciding nothing came out of it.
        await Task.Delay(100);

        if (Renderer.UnhandledException.IsCompleted)
            Assert.Fail($"the render loop saw: {Renderer.UnhandledException.Result}");

        // The other half of continuing past disposal: a dead grid claiming the focus slot, which
        // the palette would then offer as a place to jump to.
        Assert.False(Services.GetRequiredService<GridFocus>().Available,
                     "a disposed grid registered itself for focus");
    }

    [Fact]
    public Task Disposing_the_grid_while_the_first_module_loads_stays_quiet() =>
        NothingEscapesWhenDisposedDuring("import ./js/gridsweep.js");

    /// <summary>
    /// One await further in: the sweep module is attached and the pointer probe is what is
    /// outstanding, so the keyboard module's attach — the next call to be handed the object
    /// reference — is what the resumed continuation would reach. The guard has to hold after every
    /// await, not just the first.
    /// </summary>
    [Fact]
    public Task Disposing_the_grid_between_two_modules_stays_quiet() =>
        NothingEscapesWhenDisposedDuring("hasCoarsePointer");

    [Fact]
    public Task Disposing_the_grid_while_the_image_loader_loads_stays_quiet() =>
        NothingEscapesWhenDisposedDuring("import ./js/imgloader.js");
}
