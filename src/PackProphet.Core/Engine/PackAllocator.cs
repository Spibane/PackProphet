namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;

/// <summary>
/// Splits a fixed pack budget across pack keys to maximise expected progress on a target.
///
/// <see cref="PackRanker.Batch"/> prices committing a batch to one pack key; it cannot say how
/// to split a budget across several, since a demand can be supplied by more than one pack and
/// the packs that best serve the same demand overlap. This solves that by greedy marginal
/// allocation: the packs available for arrival are independent draws, so a demand's arrivals
/// pool by simple addition of per-pack rates, and the expected number of demands met from
/// a fixed allocation is exact via the same Poisson tail <see cref="CompletionEstimator"/> uses.
/// Assigning each pack of budget to whichever key currently yields the largest marginal gain is
/// optimal when the objective is concave in each key's count, which a Poisson tail is once past
/// its inflection; away from that it is a strong, cheap heuristic rather than a proof.
/// </summary>
public sealed class PackAllocator
{
    private readonly CardIndex _index;
    private readonly PackOdds _odds;

    public PackAllocator(CardIndex index, PackOdds odds)
    {
        _index = index;
        _odds = odds;
    }

    /// <param name="PackKey">"A1:Mewtwo".</param>
    /// <param name="Packs">How many of the budget this key was assigned.</param>
    public sealed record AllocationRow(string PackKey, int Packs)
    {
        public string Set => PackKey.Split(':')[0];
        public string PackName => PackKey.Split(':')[1];
    }

    /// <param name="Rows">Every pack key assigned at least one pack, most-assigned first.</param>
    /// <param name="ExpectedNewCards">
    /// Expected count of outstanding demands this plan satisfies. Not additive across
    /// <see cref="Rows"/>: two keys can both supply the same demand, so crediting each in
    /// isolation would double-count it.
    /// </param>
    /// <param name="ChanceOfFinishing">
    /// Chance every outstanding demand is met by this plan, treating arrivals for different
    /// demands as independent draws.
    /// </param>
    public sealed record AllocationPlan(
        IReadOnlyList<AllocationRow> Rows,
        double ExpectedNewCards,
        double ChanceOfFinishing);

    /// <summary>
    /// Best split of <paramref name="totalPacks"/> across purchasable pack keys for
    /// <paramref name="target"/>. Stops assigning early once no key can still add anything, so a
    /// budget larger than the target needs is not wasted on nothing.
    /// </summary>
    public AllocationPlan Allocate(
        ICompletionTarget target, Collection owned, int totalPacks,
        IReadOnlySet<string>? unavailablePacks = null)
    {
        if (totalPacks <= 0) return new AllocationPlan([], 0.0, 0.0);

        var outstanding = target.Outstanding(_index, owned);
        if (outstanding.Count == 0) return new AllocationPlan([], 0.0, 1.0);

        var packKeys = (unavailablePacks is null
                ? _odds.PriceablePacks
                : _odds.PriceablePacks.Where(p => !unavailablePacks.Contains(p)))
            .ToArray();

        // Per-demand arrival rate from each pack key, pooled across every printing that key can
        // yield and that satisfies the demand — the same pooling PackRanker.Rank/Batch use.
        var lambda = new double[outstanding.Count][];
        for (var k = 0; k < packKeys.Length; k++)
        {
            var rates = _odds.ExpectedCopies(packKeys[k]);
            for (var d = 0; d < outstanding.Count; d++)
            {
                lambda[d] ??= new double[packKeys.Length];
                lambda[d][k] = outstanding[d].SuppliedBy.Sum(c => rates.GetValueOrDefault(c.Key));
            }
        }

        var live = Enumerable.Range(0, packKeys.Length)
            .Where(k => lambda.Any(row => row[k] > 0))
            .ToArray();
        if (live.Length == 0) return new AllocationPlan([], 0.0, 0.0);

        var cumulative = new double[outstanding.Count];
        var counts = new int[packKeys.Length];

        for (var step = 0; step < totalPacks; step++)
        {
            var bestKey = -1;
            var bestGain = 0.0;
            foreach (var k in live)
            {
                var gain = 0.0;
                for (var d = 0; d < outstanding.Count; d++)
                {
                    var rate = lambda[d][k];
                    if (rate <= 0) continue;
                    var before = CompletionEstimator.PoissonTail(cumulative[d], outstanding[d].Remaining);
                    var after = CompletionEstimator.PoissonTail(cumulative[d] + rate, outstanding[d].Remaining);
                    gain += after - before;
                }
                if (gain > bestGain)
                {
                    bestGain = gain;
                    bestKey = k;
                }
            }

            // No key still moves the needle: the rest of the budget cannot help this target.
            if (bestKey < 0) break;

            counts[bestKey]++;
            for (var d = 0; d < outstanding.Count; d++)
                cumulative[d] += lambda[d][bestKey];
        }

        var expectedNewCards = 0.0;
        var chanceOfFinishing = 1.0;
        for (var d = 0; d < outstanding.Count; d++)
        {
            var p = CompletionEstimator.PoissonTail(cumulative[d], outstanding[d].Remaining);
            expectedNewCards += p;
            chanceOfFinishing *= p;
        }

        var rows = Enumerable.Range(0, packKeys.Length)
            .Where(k => counts[k] > 0)
            .OrderByDescending(k => counts[k])
            .Select(k => new AllocationRow(packKeys[k], counts[k]))
            .ToArray();

        return new AllocationPlan(rows, expectedNewCards, chanceOfFinishing);
    }
}
