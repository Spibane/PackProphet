using PackProphet.Engine;

namespace PackProphet.Tests;

/// <summary>
/// Where in a pack the chance is, position by position.
///
/// The headline "chance of a hit" says whether a pack helps. It cannot say which card in it does
/// the helping, and in this game that is a different story: the first three cards of a five-card
/// pack are one-diamond commons, so a collection that has finished the commons has all of its
/// chance in the last two.
/// </summary>
public class SlotOddsTests
{
    private static PackOdds Odds => Snapshot.Odds();

    private static IReadOnlyList<Demand> Everything(string set, int topRung = 9) =>
        RarityLadderTarget.UpTo(set, topRung, Snapshot.Index(), 1)
            .Outstanding(Snapshot.Index(), new Collection())
            .ToArray();

    [Fact]
    public void AFiveCardPack_CoversEveryPositionExactlyOnce()
    {
        // Asked of a collection that HAS commons, so there is more than one group to be
        // contiguous about.
        var groups = Odds.ChanceOfUsefulBySlot("A1:Mewtwo", WithCommonsOwned("A1"));

        Assert.NotEmpty(groups);

        // Contiguous, in pack order, starting at the first card and covering every position with
        // no gaps and no overlaps — a group list that skipped a position would be reporting a
        // pack with fewer cards than it has.
        Assert.Equal(1, groups[0].First);
        for (var i = 1; i < groups.Count; i++)
        {
            Assert.Equal(groups[i - 1].Last + 1, groups[i].First);
            Assert.True(groups[i].Last >= groups[i].First);
        }

        Assert.Equal(5, groups[^1].Last);
    }

    [Fact]
    public void WantingEverything_MergesTheWholePackIntoOneGroup()
    {
        // From an empty collection every card is wanted, so every position is certain to help and
        // there is nothing to distinguish them. One group saying so is the honest report; five
        // rows of "100%" would be five ways of saying the same thing.
        var groups = Odds.ChanceOfUsefulBySlot("A1:Mewtwo", Everything("A1"));

        var only = Assert.Single(groups);
        Assert.Equal(1, only.First);
        Assert.Equal(5, only.Last);
        Assert.Equal("1st-5th card", only.Label);
        Assert.Equal(1.0, only.Chance, 12);
    }

    [Fact]
    public void TheGroupBoundaryIsReadFromTheRates_NotHardcoded()
    {
        // The split people recognise — three commons, then the two that matter — only exists once
        // the commons are owned, and it comes out of the numbers rather than out of a constant.
        var groups = Odds.ChanceOfUsefulBySlot("A1:Mewtwo", WithCommonsOwned("A1"));

        Assert.True(groups.Count >= 2, "the commons and the rare slots should not read alike");
        Assert.Equal(1, groups[0].First);
        Assert.Equal(3, groups[0].Last);
        Assert.Equal("1st-3rd card", groups[0].Label);
    }

    [Fact]
    public void FinishingTheCommons_MovesNearlyAllOfTheChanceIntoTheLastTwoCards()
    {
        // The finding the whole breakdown exists for: the headline percentage cannot say that the
        // first three cards of every pack have stopped being able to help you.
        //
        // "Nearly" and not "entirely", which is the modelling being right rather than a rounding
        // slip. One variant of each pack — the themed rare pack, weight 0.05% — fills every slot
        // from the rare pool, so the first card retains exactly that sliver of a chance. A test
        // asserting a flat zero here would be asserting that variant away.
        var groups = Odds.ChanceOfUsefulBySlot("A1:Mewtwo", WithCommonsOwned("A1"));

        var early = groups.Where(g => g.Last <= 3).Sum(g => g.Chance);
        var late = groups.Where(g => g.First >= 4).Sum(g => g.Chance);

        Assert.True(early < 0.01, $"the commons should be all but spent, not {early:P2}");
        Assert.True(late > early * 100, $"late {late:P2} should dwarf early {early:P2}");
    }

    /// <summary>
    /// Everything still outstanding for a set once every one-diamond card is owned — the state
    /// most collections reach within a week and the one the per-slot reading is for.
    /// </summary>
    private static IReadOnlyList<Demand> WithCommonsOwned(string set)
    {
        var index = Snapshot.Index();
        var target = RarityLadderTarget.UpTo(set, 9, index, 1);

        var owned = new Collection();
        foreach (var demand in target.Outstanding(index, owned))
        {
            var rung = demand.SuppliedBy
                .Select(c => index.Ladder.IndexOf(c.Rarity))
                .FirstOrDefault(r => r is not null);

            if (rung == 0) owned = owned.With(demand.Key, demand.Remaining);
        }

        return target.Outstanding(index, owned).ToArray();
    }

    [Fact]
    public void TheGroupsAgreeWithTheHeadlineChance()
    {
        // The same arithmetic the ranking does, read off the groups: 1 − Π(1 − p) over positions,
        // weighting each group by how many positions it covers. They are computed by different
        // loops, so agreement is the check that neither drifted.
        //
        // Exact only for a pack whose variants all hold the same number of cards, which is why
        // this asks A1 rather than the Deluxe set.
        var outstanding = Everything("A1");

        var groups = Odds.ChanceOfUsefulBySlot("A1:Charizard", outstanding);
        var miss = groups.Aggregate(1.0, (acc, g) => acc * Math.Pow(1 - g.Chance, g.Last - g.First + 1));

        Assert.Equal(Odds.ChanceOfUseful("A1:Charizard", outstanding), 1 - miss, 6);
    }

    [Fact]
    public void NothingOutstanding_ReportsNothingRatherThanZeroes()
    {
        // A row of "0.00%" would read as a finding. There is no question here to answer.
        Assert.Empty(Odds.ChanceOfUsefulBySlot("A1:Mewtwo", []));
    }

    [Fact]
    public void EveryPriceablePack_ProducesContiguousGroups()
    {
        // Across all of them, including the Deluxe pack, whose variants hold different numbers of
        // cards, and the themed rare packs, whose slots are numbered from zero.
        foreach (var pack in Odds.PriceablePacks)
        {
            var groups = Odds.ChanceOfUsefulBySlot(pack, Everything(pack.Split(':')[0]));
            if (groups.Count == 0) continue;

            Assert.Equal(1, groups[0].First);
            for (var i = 1; i < groups.Count; i++)
                Assert.Equal(groups[i - 1].Last + 1, groups[i].First);

            Assert.All(groups, g => Assert.InRange(g.Chance, 0.0, 1.0));
        }
    }
}
