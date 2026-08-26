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

    private static CardDataLoader Loader(Func<CancellationToken, Task<HttpResponseMessage>>? onRemote) =>
        new(new HttpClient(new SnapshotHandler(onRemote))
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
