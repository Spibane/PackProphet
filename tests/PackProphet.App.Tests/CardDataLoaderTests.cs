namespace PackProphet.App.Tests;

using System.Diagnostics;
using PackProphet.Services;

/// <summary>
/// The fallback to the vendored snapshot only runs when a CDN attempt returns. These cover the
/// case where it does not: a request that hangs instead of failing.
/// </summary>
public class CardDataLoaderTests
{
    /// <summary>Generous against the loader's own five-second deadline, so a slow CI box does not fail it.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CardDataLoader Loader(Func<CancellationToken, Task<HttpResponseMessage>>? onRemote,
                                        string? artManifest = null) =>
        new(new HttpClient(new SnapshotHandler(onRemote, artManifest))
        {
            BaseAddress = new Uri("https://test.local/")
        });

    /// <summary>A connection held open with no response — a network that drops packets to the CDN.</summary>
    private static async Task<HttpResponseMessage> Hang(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new UnreachableException();
    }

    [Fact]
    public async Task A_hanging_cdn_still_boots_from_the_snapshot()
    {
        var data = await Loader(Hang).LoadAsync().WaitAsync(Patience);

        Assert.Equal(DataSource.VendoredSnapshot, data.Source);
        Assert.True(data.Index.All.Count > 3000, $"only {data.Index.All.Count} cards");
    }

    [Fact]
    public async Task The_art_manifest_decides_where_a_vendored_set_is_fetched_from()
    {
        // The link between the deploy and the app. The workflow writes this file; unless the
        // loader reads it, every card of the set that was vendored still asks the upstream CDN
        // first -- which is the gap the vendoring existed to close, so the whole feature would be
        // six megabytes of art nothing ever requests.
        //
        // Asserted on what this load reported rather than on ArtSource, which is process-wide:
        // every other test class boots a loader of its own and resets it, so a global assertion
        // here passes alone and races in the suite. What the URLs then look like is ArtSourceTests'
        // job. This is only the wiring.
        var data = await Loader(null, """{"sets":["B4a"],"packs":["Team Rocket"]}""").LoadAsync();

        Assert.Contains("B4a", data.VendoredArtSets);
    }

    [Fact]
    public async Task A_missing_art_manifest_leaves_every_card_on_the_remote_chain()
    {
        // The ordinary case: a development build, and any deploy where upstream was already
        // complete. A 404 here is an answer, not a fault.
        var data = await Loader(null).LoadAsync();

        Assert.Empty(data.VendoredArtSets);
    }

    [Fact]
    public async Task A_hanging_art_manifest_does_not_hold_the_boot_open()
    {
        // The manifest says which sets this deployment vendored art for, and it is read before the
        // card data so the first grid renders with the right urls. It is a few dozen bytes from
        // the app's own host -- and it went in with no deadline, which turned a network that drops
        // packets into an app stuck on "Loading card data…" for good.
        //
        // Its absence is a supported answer, so failing to reach it must cost nothing. Asserted
        // through the reported set list as well as the clock: a boot that returned but claimed a
        // vendored set would pass a timing check and then serve local urls for art it does not
        // have.
        var data = await Loader(Hang).LoadAsync().WaitAsync(Patience);

        Assert.Equal(DataSource.VendoredSnapshot, data.Source);
        Assert.Empty(data.VendoredArtSets);
    }

    [Fact]
    public async Task A_hanging_cdn_still_returns_card_facts_from_the_snapshot()
    {
        // The local copy is tried first here, so this proves the deadline did not break the
        // ordinary path rather than that it fired.
        var facts = await Loader(Hang).LoadFactsAsync().WaitAsync(Patience);

        Assert.True(facts.Count > 0, "no card detail loaded");
    }

    [Fact]
    public async Task A_cancelled_caller_is_not_mistaken_for_a_slow_source()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Loader(Hang).LoadFactsAsync(cancelled.Token));
    }
}
