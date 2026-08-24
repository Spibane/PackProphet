using System.Diagnostics;
using PackProphet.Data;

namespace PackProphet.Tests;

/// <summary>
/// Guards the cost of the collection page's ordering. Desktop .NET is roughly an order of
/// magnitude faster than the WebAssembly interpreter, so the budget here is deliberately tight:
/// what reads as "fine" in a test is a visible delay on a phone.
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

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 100_000; i++) _ = sets.SortKey("A1");
        sw.Stop();

        Assert.True(sw.Elapsed.TotalMilliseconds < 50,
            $"100k cached lookups took {sw.Elapsed.TotalMilliseconds:N0}ms");
    }
}
