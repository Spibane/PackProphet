namespace PackProphet.App.Tests;

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PackProphet.Services;

/// <summary>
/// Serves the vendored snapshot from disk and refuses everything else.
///
/// Refusing the CDN is deliberate rather than convenient: the loader tries the network first and
/// falls back to the snapshot, so every render test also exercises the fallback path — the one the
/// app takes on a plane or behind a captive portal, and the one that would otherwise never be run
/// until a user hit it.
/// </summary>
internal sealed class SnapshotHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();

        // Only the local root is answered. Anything on a remote host is a miss, so the loader
        // downgrades to the snapshot exactly as it would offline.
        var marker = "data/snapshot/";
        var at = url.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0 || request.RequestUri.Host != "test.local")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        var file = url[(at + marker.Length)..];
        var path = Path.Combine(AppContext.BaseDirectory, "snapshot", file);

        if (!File.Exists(path))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(File.ReadAllText(path), System.Text.Encoding.UTF8,
                                        "application/json")
        };
        return Task.FromResult(response);
    }
}

/// <summary>
/// A rendering host wired the same way Program.cs wires the real app, so a page under test gets
/// the services it actually asks for rather than a bespoke set that cannot drift into agreement.
///
/// Two decisions worth stating:
///
///   - JS interop runs in LOOSE mode. Every call becomes a no-op returning a default. That is
///     right for these tests: the pages call into the image loader, the sweep handler, the tooltip
///     positioner and localStorage, none of which have anything to assert without a browser, and
///     strict mode would turn every one of them into a failure that says nothing about the page.
///   - State goes through InMemoryStateStore. localStorage is a browser fact; what these tests are
///     for is whether a page RENDERS against a given state.
/// </summary>
public abstract class AppHost : TestContext
{
    /// <summary>
    /// Resolved on first use, not in the constructor. A test that wants a different store sets
    /// that up in its own body, which runs after construction — so building the session eagerly
    /// meant Store() was always asked before the test had said what it wanted.
    /// </summary>
    protected AppSession Session => _session ??= Services.GetRequiredService<AppSession>();

    private AppSession? _session;

    protected AppHost()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(new HttpClient(new SnapshotHandler())
        {
            BaseAddress = new Uri("https://test.local/")
        });

        Services.AddSingleton<CardDataLoader>();
        Services.AddSingleton<LocalStorageStateStore>();
        // A factory, not an instance: passing Store() here would call it during
        // construction, which is the very thing the laziness below exists to avoid.
        Services.AddSingleton<IStateStore>(_ => Store());
        Services.AddSingleton<AppSession>();
        Services.AddSingleton<UiBusy>();
        Services.AddSingleton<NavHistory>();
        Services.AddSingleton<PaletteSwitch>();
        Services.AddSingleton<GridFocus>();
        Services.AddBlazorBootstrap();
    }

    /// <summary>
    /// The store the session persists through. Overridden by the tests that need a store which
    /// FAILS, since a refused write is the case worth covering.
    /// </summary>
    protected virtual IStateStore Store() => new InMemoryStateStore(Start());

    /// <summary>
    /// The state a test starts from. Overridden to set up a collection, extra profiles, a log —
    /// whatever the case needs. Default is a brand-new profile, which is the state every real user
    /// begins in and the one that broke most often.
    /// </summary>
    protected virtual AppState Start() => AppState.Fresh();

    /// <summary>
    /// Load card data before rendering. Pages call this themselves in OnInitializedAsync, but
    /// awaiting it here means the assertions run against a loaded page rather than its "Loading
    /// card data…" placeholder.
    /// </summary>
    protected async Task ReadyAsync()
    {
        await Session.InitAsync();
        Assert.True(Session.Ready, "card data did not load from the snapshot");
    }
}
