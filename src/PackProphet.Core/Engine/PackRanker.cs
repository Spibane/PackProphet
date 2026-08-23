namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;

/// <summary>One pack's standing against a target.</summary>
/// <param name="PackKey">"A1:Mewtwo".</param>
/// <param name="ChanceUseful">Probability a single pack contains something still needed.</param>
/// <param name="PacksToNextUseful">1 / ChanceUseful. Infinite when the pack cannot help.</param>
/// <param name="PacksToFinishItsShare">
/// Expected packs to finish only the demands this pack can actually supply. The fair
/// comparison between packs, since no single pack can complete a multi-set target.
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
    /// Packs not purchasable right now — limited-time packs out of rotation. Recommending one
    /// of those is worse than useless, so they are dropped from the ranking entirely.
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

    /// <summary>
    /// Cheapest expected packs to finish the PRICEABLE part of a target, assuming you always
    /// open the best available pack. A lower bound rather than a plan: it credits each demand
    /// with its best single source, which is how a rational opener would actually behave.
    ///
    /// Demands no pack can supply are excluded rather than treated as infinite. A single
    /// unpriceable card — one from a set awaiting pull rates — would otherwise turn the whole
    /// estimate into "never" and hide the real cost of everything else. They are reported
    /// separately by <see cref="Unreachable"/>, and callers must show both.
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

        // Three distinct answers, and conflating any two of them misleads:
        //   nothing outstanding            -> 0        (done)
        //   outstanding, none priceable    -> infinity (no pack will ever help)
        //   outstanding, some priceable    -> estimate over those, rest disclosed separately
        if (needs.Length == 0) return double.PositiveInfinity;

        return CompletionEstimator.ExpectedPacks(needs);
    }

    /// <summary>
    /// Demands no priceable pack can supply — cards from a set with no pull-rate data, or
    /// not sold in packs at all. Must be DISCLOSED: silently dropping them turns an
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
