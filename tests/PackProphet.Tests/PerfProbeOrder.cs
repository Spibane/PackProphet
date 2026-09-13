using System.Diagnostics;
using PackProphet.Data;

namespace PackProphet.Tests;

/// <summary>
/// Guards the cost of the collection page's ordering. Desktop .NET is roughly an order of magnitude
/// faster than the WebAssembly interpreter, so what reads as "fine" in a test is a visible delay on
/// a phone — which is why these exist at all.
///
/// MEASURED AGAINST THIS MACHINE, NOT AGAINST A NUMBER
/// ==================================================================================
/// Both probes used a fixed millisecond budget, and both had already been caught out by one: the
/// comment below records a run reporting 107ms against a budget of 50, on code that costs about 1ms
/// warm. The remedy then was to warm the JIT, which was right and not enough — a wall clock measures
/// how busy the machine is, and this suite runs beside a dev server. One of them failed that way
/// again while this was being written.
///
/// So each probe times a CONTROL in the same run, on the same machine, under the same contention:
/// the cheapest thing doing the same shape of work, with none of the work being guarded. The
/// assertion is a ratio. A machine twice as slow slows both halves and the ratio holds.
///
/// The budgets are calibrated against both outcomes rather than guessed, by measuring each probe
/// with the cache in place and with it taken out:
///
///                        cached        uncached      budget
///   sorting 3.5k cards   x2.4          x9.5          x5
///   100k key lookups     x1.6          x37.6         x8
///
/// No absolute floor beside the ratio. There was one — Math.Max(50ms, ratio) — and it silently
/// outranked the ratio it was meant to protect: 50ms per 100k lookups is dearer than an uncached
/// SortKey, so the probe passed with the cache ripped out entirely. A guard that cannot fail is
/// worse than no guard, because it is counted.
/// </summary>
public class PerfProbeOrder
{
    /// <summary>
    /// What one iteration of <paramref name="work"/> costs, warmed first so the figure is the
    /// code's cost rather than the JIT's.
    /// </summary>
    private static double PerCallMs(int iterations, Action work)
    {
        for (var i = 0; i < iterations; i++) work();

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++) work();
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds / iterations;
    }

    [Fact]
    public void SortingEveryCardBySetIsCheapEnoughToDoPerRender()
    {
        var sets = new SetCatalog(Snapshot.PublishedSets(), Snapshot.Index().BySet.Keys);
        var all = Snapshot.Index().All.DistinctBy(c => c.OwnershipKey).ToArray();

        // The same sort over the same cards, keyed on a field each one already carries. That is the
        // floor: what is left over is what deriving a set key costs.
        var control = PerCallMs(5, () => _ = all.OrderBy(c => c.Set, StringComparer.Ordinal).ToArray());
        var actual = PerCallMs(5, () => _ = all.OrderBy(sets.SortKey, StringComparer.Ordinal).ToArray());

        Assert.True(actual < control * 5,
            $"sorting {all.Length} cards cost {actual:N2}ms against a plain sort's {control:N2}ms "
            + $"— x{actual / control:N1}, and cached it is about x2.4");
    }

    [Fact]
    public void TheSetKeyIsComputedOncePerSetNotOncePerCard()
    {
        // The regression this replaces: SortKey ran a regex per call, so ordering the "All cards"
        // view meant 3,546 regex matches on every render — and a render happens on every tap.
        var sets = new SetCatalog(Snapshot.PublishedSets(), Snapshot.Index().BySet.Keys);

        // A dictionary hit on the same key, which is what SortKey claims to be plus a little. The
        // parse it memoises is about twenty times dearer than that, so the two outcomes are not
        // close enough to be confused by a busy machine.
        var table = new Dictionary<string, string>(StringComparer.Ordinal) { ["A1"] = "A1" };

        var control = PerCallMs(100_000, () => _ = table["A1"]);
        var actual = PerCallMs(100_000, () => _ = sets.SortKey("A1"));

        Assert.True(actual < control * 8,
            $"100k lookups cost {actual * 100_000:N1}ms against a dictionary's {control * 100_000:N1}ms "
            + $"— x{actual / control:N1}, and cached it is about x1.6");
    }
}
