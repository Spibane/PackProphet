namespace PackProphet.App.Tests;

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PackProphet.Services;

/// <summary>
/// Serves the vendored snapshot from disk and refuses everything else.
///
/// Refusing the CDN means every render test also exercises the offline fallback path: the loader
/// tries the network first and falls back to the snapshot.
/// </summary>
/// <param name="onRemote">
/// What a request that is not the local snapshot gets. The default is an immediate 404 — a CDN
/// that is simply unreachable. A test that wants a CDN which hangs rather than fails passes
/// something that never completes.
/// </param>
/// <param name="remoteFiles">
/// Remote responses by URL suffix, for the card-detail top-up: it fetches one file per set that
/// the vendored table does not cover, and what matters is WHICH files it asks for. Anything not
/// listed falls through to <paramref name="onRemote"/> and then to a 404, which is what a set
/// upstream has not published yet actually answers.
/// </param>
internal sealed class SnapshotHandler(
    Func<CancellationToken, Task<HttpResponseMessage>>? onRemote = null,
    string? artManifest = null,
    IReadOnlyDictionary<string, string>? remoteFiles = null) : HttpMessageHandler
{
    /// <summary>Every URL asked for, in order. A request not made is as much of an assertion as one made.</summary>
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);

        if (remoteFiles is not null)
            foreach (var (suffix, json) in remoteFiles)
                if (url.EndsWith(suffix, StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, System.Text.Encoding.UTF8,
                                                    "application/json")
                    });

        // The manifest of art this deployment vendored. Absent by default, which is what a
        // development build and most deploys serve, and what every other test wants.
        if (request.RequestUri.Host == "test.local" && url.EndsWith("art/index.json", StringComparison.Ordinal))
        {
            return Task.FromResult(artManifest is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                  {
                      Content = new StringContent(artManifest, System.Text.Encoding.UTF8,
                                                  "application/json")
                  });
        }

        // The card art fingerprints, served as the text file they are. Not JSON and not under the
        // snapshot folder, so it needs its own branch — and it is worth having one: without it the
        // screenshot import page renders against an empty table, which is the one state its own
        // coverage warning is designed to describe and therefore the least useful one to test in.
        if (request.RequestUri.Host == "test.local" && url.EndsWith("data/card-hashes.txt", StringComparison.Ordinal))
        {
            var table = Path.Combine(AppContext.BaseDirectory, "card-hashes.txt");
            return Task.FromResult(File.Exists(table)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                  {
                      Content = new StringContent(File.ReadAllText(table), System.Text.Encoding.UTF8, "text/plain")
                  }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        // Only the local root is answered. Anything on a remote host is a miss, so the loader
        // downgrades to the snapshot exactly as it would offline.
        var marker = "data/snapshot/";
        var at = url.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0 || request.RequestUri.Host != "test.local")
            return onRemote?.Invoke(ct)
                ?? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

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
/// A rendering host wired the same way Program.cs wires the real app, so a page under test gets the
/// services it actually asks for.
///
/// Two things to note:
///
///   - JS interop runs in loose mode, so every call is a no-op returning a default. The pages call
///     into the image loader, the sweep handler, the tooltip positioner and localStorage, none of
///     which have anything to assert without a browser.
///   - State goes through InMemoryStateStore. These tests cover whether a page renders against a
///     given state, not browser storage.
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

        // bUnit waits one second by default, which is a budget rather than a limit: the assertion
        // passes the moment the render lands, so a longer ceiling costs nothing on a machine that
        // is keeping up and only matters on one that is not. The collection page renders a couple
        // of hundred tiles and takes ~130ms here; on a contended CI runner that is close enough to
        // one second to fail on load rather than on behaviour, which is what it did — reporting a
        // check count of zero, meaning the wait expired before the assertion ran even once.
        DefaultWaitTimeout = TimeSpan.FromSeconds(10);

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
        Services.AddSingleton<ArtHashSource>();
        Services.AddSingleton<TypeBadgeSource>();
        Services.AddSingleton<ShotScanner>();

        Services.AddSingleton<BrowserStore>();
        Services.AddSingleton(_ => SyncSettings());
        Services.AddSingleton<SyncCrypto>();
        Services.AddSingleton(sp => new SyncTransport(sp.GetRequiredService<SyncOptions>(), SyncHandler()));
        Services.AddSingleton<SyncService>();
    }

    /// <summary>
    /// Whether this test's app has somewhere to sync to. Unconfigured by default, which is both the
    /// state a fork of the repo builds in and the state that keeps every existing page test from
    /// growing a section it was not written for.
    /// </summary>
    protected virtual SyncOptions SyncSettings() => new();

    /// <summary>What the sync transport talks to. Null means a real client, which no test wants.</summary>
    protected virtual HttpMessageHandler? SyncHandler() => new SnapshotHandler();

    /// <summary>
    /// The store the session persists through. Overridden by the tests that need a store which
    /// fails, since a refused write is a case worth covering.
    /// </summary>
    protected virtual IStateStore Store() => new InMemoryStateStore(Start());

    /// <summary>
    /// The state a test starts from. Overridden to set up a collection, extra profiles, a log —
    /// whatever the case needs. Default is a brand-new profile, the state every real user begins in.
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
