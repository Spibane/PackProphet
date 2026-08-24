namespace PackProphet.Tests;

using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Engine;

/// <summary>
/// Progress and demand must be two views of ONE rule. They were computed in two places, and the
/// copy on the Collection page ignored the parallel-foil setting — so with foils switched off the
/// page reported 139 cards still outstanding that the engine considered complete.
/// </summary>
public class TargetProgressTests
{
    private static readonly CardIndex Ix = Snapshot.Index();

    private static RarityPlan Diamonds => RarityPlan.Uniform(Snapshot.Tiers("C", "U", "R", "RR"));

    /// <summary>A4b is the set with parallel foils, so it is the only one where this can be seen.</summary>
    private const string FoilSet = "A4b";

    private static FoilPolicy Foils(int copies) =>
        new(Snapshot.Odds().FoilOwnershipKeys, copies);

    [Fact]
    public void Progress_and_outstanding_agree_on_what_is_wanted()
    {
        var target = new RarityLadderTarget(FoilSet, Diamonds, Foils(1));
        var owned = new Collection();

        var (wanted, satisfied) = target.Progress(Ix, owned);
        var outstanding = target.Outstanding(Ix, owned);

        Assert.Equal(0, satisfied);
        // Nothing owned, so every wanted card is outstanding. If these ever disagree, one of the
        // two is applying a rule the other does not.
        Assert.Equal(wanted, outstanding.Count);
    }

    [Fact]
    public void Turning_parallel_foils_off_removes_them_from_the_total()
    {
        var owned = new Collection();

        var withFoils = new RarityLadderTarget(FoilSet, Diamonds, Foils(1)).Progress(Ix, owned);
        var without = new RarityLadderTarget(FoilSet, Diamonds, Foils(0)).Progress(Ix, owned);

        // The page used to show the with-foils total whatever the setting said, which is how it
        // came to disagree with the ranking on the same set.
        Assert.True(withFoils.Wanted > without.Wanted,
            $"expected foils to add to the total: {withFoils.Wanted} vs {without.Wanted}");

        var foilKeys = Snapshot.Odds().FoilOwnershipKeys;
        var foilsInSet = Ix.BySet[FoilSet].DistinctBy(c => c.OwnershipKey)
            .Count(c => foilKeys.Contains(c.OwnershipKey));

        Assert.Equal(foilsInSet, withFoils.Wanted - without.Wanted);
    }

    [Fact]
    public void A_foil_wanted_at_zero_is_never_short_of_target()
    {
        var foilKeys = Snapshot.Odds().FoilOwnershipKeys;
        var foil = Ix.BySet[FoilSet].First(c => foilKeys.Contains(c.OwnershipKey));

        Assert.Equal(0, new RarityLadderTarget(FoilSet, Diamonds, Foils(0)).Required(Ix, foil));
        Assert.Equal(1, new RarityLadderTarget(FoilSet, Diamonds, Foils(1)).Required(Ix, foil));

        // And it takes its own count rather than its rung's, so two of every diamond does not
        // silently mean two of every foil.
        var twoOfEach = RarityPlan.Uniform(Snapshot.Tiers("C", "U", "R", "RR"), 2);
        Assert.Equal(1, new RarityLadderTarget(FoilSet, twoOfEach, Foils(1)).Required(Ix, foil));
    }

    [Fact]
    public void Satisfied_counts_copies_not_mere_ownership()
    {
        var plan = RarityPlan.Uniform(Snapshot.Tiers("C"), 2);
        var target = new RarityLadderTarget("A1", plan);

        var common = Ix.BySet["A1"].First(c => c.Rarity == "C");

        var one = new Collection(new Dictionary<string, int> { [common.OwnershipKey] = 1 });
        var two = new Collection(new Dictionary<string, int> { [common.OwnershipKey] = 2 });

        Assert.Equal(0, target.Progress(Ix, one).Satisfied);
        Assert.Equal(1, target.Progress(Ix, two).Satisfied);
    }
}
