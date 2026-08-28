using System.Diagnostics;
using PackProphet.Data;

namespace PackProphet.Tests;

/// <summary>
/// Guards the cost of the collection page's ordering. Desktop .NET is roughly an order of magnitude
/// faster than the WebAssembly interpreter, so the budget here is tight: what reads as "fine" in a
/// test is a visible delay on a phone.
/// </summary>
public class PerfProbeOrder
{
    [Fact]
    public void SortingEveryCardBySetIsCheapEnoughToDoPerRender()
    {
        var sets = new SetCatalog(Snapshot.PublishedSets(), Snapshot.Index().BySet.Keys);
        var all = Snapshot.Index().All.DistinctBy(c => c.OwnershipKey).ToArray();

        // Warm the per-set key cache, as a running app's would be.
        _ = all.OrderBy(sets.SortKey, StringComparer.Ordinal).ToArray();

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++)
            _ = all.OrderBy(sets.SortKey, StringComparer.Ordinal).ToArray();
        sw.Stop();

        var perSort = sw.Elapsed.TotalMilliseconds / 5;
        Assert.True(perSort < 25, $"sorting {all.Length} cards took {perSort:N1}ms");
    }

    [Fact]
    public void TheSetKeyIsComputedOncePerSetNotOncePerCard()
    {
        // The regression this replaces: SortKey ran a regex per call, so ordering the "All cards"
        // view meant 3,546 regex matches on every render — and a render happens on every tap.
        var sets = new SetCatalog(Snapshot.PublishedSets(), Snapshot.Index().BySet.Keys);

        // Warmed first, like the sibling test above. Without this the loop is timed while the JIT
        // is still at tier 0, so the figure is a measure of how contended the machine was rather
        // than of the cache — which is how it came to report 107ms on a CI runner against the 1ms
        // it costs warm, and fail a budget it has fifty times the headroom for.
        for (var i = 0; i < 100_000; i++) _ = sets.SortKey("A1");

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 100_000; i++) _ = sets.SortKey("A1");
        sw.Stop();

        Assert.True(sw.Elapsed.TotalMilliseconds < 50,
            $"100k cached lookups took {sw.Elapsed.TotalMilliseconds:N0}ms");
    }
}
