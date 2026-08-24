namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;

/// <summary>One pack's standing against a target.</summary>
/// <param name="PackKey">"A1:Mewtwo".</param>
/// <param name="ChanceUseful">Probability a single pack contains something still needed.</param>
/// <param name="PacksToNextUseful">1 / ChanceUseful. Infinite when the pack cannot help.</param>
/// <param name="PacksToFinishItsShare">
/// Expected packs to finish only the demands this pack can supply. The comparable figure
/// between packs, since no single pack can complete a multi-set target.
/// </param>
/// <param name="DemandsServed">How many outstanding demands this pack can help with.</param>
/// <param name="MissingByTier">Outstanding demands this pack serves, grouped by rarity rung.</param>
public sealed record PackStanding(
    string PackKey,
    double ChanceUseful,
    double PacksToNextUseful,
    double PacksToFinishItsShare,
    int DemandsServed,
    IReadOnlyDictionary<int, int> MissingByTier)
{
    public string Set => PackKey.Split(':')[0];
    public string PackName => PackKey.Split(':')[1];
}

/// <summary>Ranks packs against a target, and prices the target overall.</summary>
public sealed class PackRanker
{
    private readonly CardIndex _index;
    private readonly PackOdds _odds;

    public PackRanker(CardIndex index, PackOdds odds)
    {
        _index = index;
        _odds = odds;
    }

    /// <summary>
    /// Packs ordered best-first: most likely to yield something needed, then by how quickly
    /// they would finish their share of the target. Packs that cannot help are omitted.
    /// </summary>
    /// <param name="unavailablePacks">
    /// Packs not purchasable right now — limited-time packs out of rotation. Dropped from the
    /// ranking entirely.
    /// </param>
    public IReadOnlyList<PackStanding> Rank(
        ICompletionTarget target, Collection owned, IReadOnlySet<string>? unavailablePacks = null)
    {
        var outstanding = target.Outstanding(_index, owned);
        if (outstanding.Count == 0) return [];

        var standings = new List<PackStanding>();
        foreach (var pack in _odds.PriceablePacks)
        {
            if (unavailablePacks is not null && unavailablePacks.Contains(pack)) continue;

            var rates = _odds.ExpectedCopies(pack);

            // Demands this pack can actually contribute to.
            var served = outstanding
                .Where(d => d.SuppliedBy.Any(c => rates.ContainsKey(c.Key)))
                .ToArray();
            if (served.Length == 0) continue;

            var chance = _odds.ChanceOfUseful(pack, served);
            if (chance <= 0) continue;

            // Rate for a demand from THIS pack: copies pool across every supplying printing
            // that this pack can yield.
            var needs = served
                .Select(d => new CompletionEstimator.Need(
                    d.SuppliedBy.Sum(c => rates.GetValueOrDefault(c.Key)), d.Remaining))
                .ToArray();

            var byTier = served
                .SelectMany(d => d.SuppliedBy.Take(1))
                .GroupBy(c => _index.Ladder.IndexOf(c.Rarity) ?? -1)
                .ToDictionary(g => g.Key, g => g.Count());

            standings.Add(new PackStanding(
                PackKey: pack,
                ChanceUseful: chance,
                PacksToNextUseful: 1.0 / chance,
                PacksToFinishItsShare: CompletionEstimator.ExpectedPacks(needs),
                DemandsServed: served.Length,
                MissingByTier: byTier));
        }

        return standings
            .OrderByDescending(s => s.ChanceUseful)
            .ThenBy(s => s.PacksToFinishItsShare)
            .ToArray();
    }

    /// <param name="ExpectedNewCards">
    /// Cards you do not yet have enough of that a batch of this size is expected to complete.
    /// Not <c>n x</c> the single-pack figure: a pack holds several cards, a duplicate inside the
    /// batch counts once, and demand shrinks as it is met.
    /// </param>
    /// <param name="ChanceOfNothing">
    /// Chance the whole batch advances nothing at all, which a per-pack probability does not
    /// show.
    /// </param>
    public sealed record BatchOutcome(
        string PackKey,
        int Packs,
        double ExpectedNewCards,
        double ChanceOfNothing);

    /// <summary>
    /// What committing a fixed number of packs to one pack key is expected to yield.
    ///
    /// The game's ten-at-once option costs exactly ten packs and adds no guarantee, so its odds
    /// match ten singles of the same pack — the same ten draws. The comparison it does inform is
    /// across sets: ten of this pack against ten of that one, which is what
    /// <see cref="Batches"/> is for.
    ///
    /// The value of re-choosing mid-batch is not modelled. Pricing it means solving how to split
    /// n packs across packs optimally, which is Phase 3's allocator.
    /// </summary>
    public BatchOutcome Batch(
        ICompletionTarget target, Collection owned, string packKey,
        int packs = GameRules.PacksPerBatch) =>
        Batch(target.Outstanding(_index, owned), packKey, packs);

    private BatchOutcome Batch(IReadOnlyList<Demand> outstanding, string packKey, int packs)
    {
        if (outstanding.Count == 0 || packs <= 0)
            return new BatchOutcome(packKey, packs, 0, 1);

        var rates = _odds.ExpectedCopies(packKey);

        // Per-demand arrival rate from THIS pack, pooled across every printing it can yield.
        var expected = 0.0;
        foreach (var demand in outstanding)
        {
            var lambda = demand.SuppliedBy.Sum(c => rates.GetValueOrDefault(c.Key));
            if (lambda <= 0) continue;

            // P(enough copies arrive within the batch). Summing these gives the expected COUNT
            // of demands met, which is what "new cards" means to a collector.
            expected += CompletionEstimator.PoissonTail(lambda * packs, demand.Remaining);
        }

        // Packs are independent draws, so a batch misses entirely only if every pack in it does.
        var perPack = _odds.ChanceOfUseful(packKey, outstanding);
        var nothing = Math.Pow(1 - perPack, packs);

        return new BatchOutcome(packKey, packs, expected, nothing);
    }

    /// <summary>
    /// Every purchasable pack's batch outcome, best first. One pass over the target, since these
    /// figures are read side by side: the batch option is a choice of which set to commit ten
    /// packs to.
    /// </summary>
    public IReadOnlyList<BatchOutcome> Batches(
        ICompletionTarget target, Collection owned,
        int packs = GameRules.PacksPerBatch,
        IReadOnlySet<string>? unavailablePacks = null)
    {
        var outstanding = target.Outstanding(_index, owned);
        if (outstanding.Count == 0) return [];

        var results = new List<BatchOutcome>();
        foreach (var pack in _odds.PriceablePacks)
        {
            if (unavailablePacks is not null && unavailablePacks.Contains(pack)) continue;

            var outcome = Batch(outstanding, pack, packs);
            if (outcome.ExpectedNewCards > 0) results.Add(outcome);
        }

        return results.OrderByDescending(b => b.ExpectedNewCards).ToArray();
    }

    /// <summary>
    /// Cheapest expected packs to finish the priceable part of a target, assuming you always
    /// open the best available pack. A lower bound rather than a plan: it credits each demand
    /// with its best single source.
    ///
    /// Demands no pack can supply are excluded rather than treated as infinite, so one
    /// unpriceable card — from a set awaiting pull rates — does not turn the whole estimate into
    /// "never". They are reported separately by <see cref="Unreachable"/>, and callers show both.
    /// </summary>
    public double BestCasePacksToFinish(
        ICompletionTarget target, Collection owned, IReadOnlySet<string>? unavailablePacks = null)
    {
        var outstanding = target.Outstanding(_index, owned);
        if (outstanding.Count == 0) return 0.0;

        var best = _odds.BestRatesByCard(unavailablePacks);
        var needs = outstanding
            .Select(d => new CompletionEstimator.Need(
                d.SuppliedBy.Max(c => best.GetValueOrDefault(c.Key)), d.Remaining))
            .Where(n => n.Rate > 0)
            .ToArray();

        // Three distinct answers:
        //   nothing outstanding            -> 0        (done)
        //   outstanding, none priceable    -> infinity (no pack will ever help)
        //   outstanding, some priceable    -> estimate over those, rest disclosed separately
        if (needs.Length == 0) return double.PositiveInfinity;

        return CompletionEstimator.ExpectedPacks(needs);
    }

    /// <summary>
    /// Demands no priceable pack can supply — cards from a set with no pull-rate data, or not
    /// sold in packs at all. Disclosed rather than dropped, since dropping them turns an
    /// impossible target into a merely expensive-looking one.
    /// </summary>
    public IReadOnlyList<Demand> Unreachable(
        ICompletionTarget target, Collection owned, IReadOnlySet<string>? unavailablePacks = null)
    {
        var best = _odds.BestRatesByCard(unavailablePacks);
        return target.Outstanding(_index, owned)
            .Where(d => d.SuppliedBy.All(c => best.GetValueOrDefault(c.Key) <= 0))
            .ToArray();
    }
}
