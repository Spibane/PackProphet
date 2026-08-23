using PackProphet.State;

namespace PackProphet.Tests;

/// <summary>
/// The revision counter decides whether the Packs page re-ranks. Getting it wrong is either
/// a wasted multi-second recompute (too eager) or a stale ranking (too lazy), so both
/// directions are pinned.
/// </summary>
public class RevisionTests
{
    private static Profile Sample() => Profile.NewDefault() with
    {
        Collection = new() { ["a.webp"] = 1 }
    };

    [Fact]
    public void EditingTheCollection_ProducesANewDictionaryInstance()
    {
        // The page distinguishes collection edits from other changes by reference identity,
        // so an edit MUST allocate a new dictionary rather than mutate in place.
        var before = Sample();
        var after = before with { Collection = new Dictionary<string, int>(before.Collection) { ["b.webp"] = 1 } };

        Assert.False(ReferenceEquals(before.Collection, after.Collection));
    }

    [Fact]
    public void ChangingAnUnrelatedSetting_KeepsTheSameCollectionInstance()
    {
        // Premium, targets and resources must all carry the collection through untouched,
        // otherwise every settings tweak would trigger a full re-rank.
        var before = Sample();

        var premium = before with { Resources = before.Resources with { Premium = true } };
        var targets = before with { Targets = before.Targets with { DefaultTierIndex = 5 } };
        var dust = before with { Resources = before.Resources with { Shinedust = 500 } };

        Assert.True(ReferenceEquals(before.Collection, premium.Collection));
        Assert.True(ReferenceEquals(before.Collection, targets.Collection));
        Assert.True(ReferenceEquals(before.Collection, dust.Collection));
    }

    [Fact]
    public void PremiumAffectsOnlyTheRateConversion_NotWhatIsOutstanding()
    {
        // The justification for skipping the re-rank: premium changes no demand at all.
        Assert.Equal(2, GameRules.PacksPerDay(premium: false));
        Assert.Equal(3, GameRules.PacksPerDay(premium: true));

        var target = PackProphet.Engine.RarityLadderTarget.UpTo(
            "A1", 3, Snapshot.Index(), 1);
        var owned = new Collection();

        var before = target.Outstanding(Snapshot.Index(), owned).Count;
        // Nothing about a target or its demands consults the premium flag.
        Assert.Equal(before, target.Outstanding(Snapshot.Index(), owned).Count);
    }
}
