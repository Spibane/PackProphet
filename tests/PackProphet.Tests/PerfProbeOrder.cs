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
/// comment below records a run reporting 107ms against a budget of 50, on code costing about 1ms
/// warm. The remedy then was to warm the JIT, which was right and not enough — a wall clock measures
/// how busy the machine is, and this suite runs beside a dev server.
///
/// So each probe times a CONTROL: the cheapest thing doing the same shape of work, with none of the
/// work being guarded. The assertion is a ratio, and a machine twice as slow slows both halves.
///
/// INTERLEAVED, and compared on the FASTEST round rather than the total. Neither is decoration.
/// Timing all of the control and then all of the subject leaves the ratio at the mercy of when load
/// arrives, so the halves alternate round by round and a spike lands on both. And a sort of 3,546
/// cards takes well under a millisecond, which is smaller than one GC pause — so a sum is mostly a
/// measure of what interrupted it. Noise can only ever ADD time, so the cheapest round of each half
/// is the closest either gets to the cost being asked about.
///
/// The sort probe failed both ways on the way here: five iterations summed, passing five times
/// alone and failing inside the full suite; then interleaved but summed, still one run in three.
///
/// The budgets are calibrated against both outcomes rather than guessed, by measuring each probe
/// with the memo in place and with it taken out:
///
///                        cached        uncached      budget
///   sorting 3.5k cards   x2.4          x9.5          x6
///   100k key lookups     x1.6          x37.6         x8
///
/// No absolute floor beside the ratio. There was one — Math.Max(50ms, ratio) — and it silently
/// outranked the ratio it was meant to protect: 50ms per 100k lookups is dearer than an uncached
/// SortKey, so the probe passed with the memo ripped out entirely. A guard that cannot fail is
/// worse than no guard, because it is counted.
/// </summary>
public class PerfProbeOrder
{
    /// <summary>
    /// What <paramref name="subject"/> costs as a multiple of <paramref name="control"/>.
    ///
    /// Both are warmed first, so the figure is the code's cost rather than the JIT's. Then they are
    /// timed in alternating rounds — so whatever else the machine is doing is charged to both — and
    /// compared on their fastest round, which is the one least interrupted.
    /// </summary>
    private static double Ratio(int rounds, int perRound, Action control, Action subject)
    {
        for (var i = 0; i < perRound; i++) { control(); subject(); }

        var controlBest = long.MaxValue;
        var subjectBest = long.MaxValue;

        for (var round = 0; round < rounds; round++)
        {
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < perRound; i++) control();
            controlBest = Math.Min(controlBest, sw.ElapsedTicks);

            sw = Stopwatch.StartNew();
            for (var i = 0; i < perRound; i++) subject();
            subjectBest = Math.Min(subjectBest, sw.ElapsedTicks);
        }

        // A control round that measured zero ticks would divide by nothing. Only reachable if the
        // clock is coarser than the work, which for these two is not the case — but a probe that
        // throws DivideByZero instead of reporting is a probe nobody trusts again.
        return controlBest <= 0 ? 0 : (double)subjectBest / controlBest;
    }

    [Fact]
    public void SortingEveryCardBySetIsCheapEnoughToDoPerRender()
    {
        var sets = new SetCatalog(Snapshot.PublishedSets(), Snapshot.Index().BySet.Keys);
        var all = Snapshot.Index().All.DistinctBy(c => c.OwnershipKey).ToArray();

        // The control is the same sort over the same cards, keyed on a field each one already
        // carries. What is left over is what deriving a set key costs.
        var ratio = Ratio(25, 1,
            () => _ = all.OrderBy(c => c.Set, StringComparer.Ordinal).ToArray(),
            () => _ = all.OrderBy(sets.SortKey, StringComparer.Ordinal).ToArray());

        Assert.True(ratio < 6,
            $"sorting {all.Length} cards cost x{ratio:N1} a plain sort; memoised it is about x2.4 "
            + "and with the memo gone about x9.5");
    }

    [Fact]
    public void TheSetKeyIsComputedOncePerSetNotOncePerCard()
    {
        // The regression this replaces: SortKey ran a regex per call, so ordering the "All cards"
        // view meant 3,546 regex matches on every render — and a render happens on every tap.
        var sets = new SetCatalog(Snapshot.PublishedSets(), Snapshot.Index().BySet.Keys);

        // The control is a dictionary hit on the same key, which is what SortKey claims to be plus
        // a little. The parse it memoises is more than twenty times dearer than that, so the two
        // outcomes are not close enough for a busy machine to confuse.
        var table = new Dictionary<string, string>(StringComparer.Ordinal) { ["A1"] = "A1" };

        var ratio = Ratio(15, 10_000, () => _ = table["A1"], () => _ = sets.SortKey("A1"));

        Assert.True(ratio < 8,
            $"a cached lookup cost x{ratio:N1} a dictionary hit; memoised it is about x1.6 "
            + "and with the memo gone about x37");
    }
}
