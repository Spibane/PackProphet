using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Engine;

namespace PackProphet.Tests;

/// <summary>
/// The game's ten-at-once option costs exactly ten packs and guarantees nothing. These tests pin
/// what follows from that: a batch cannot beat opening the same number of packs one at a time, and
/// the gap between them is the cost of not choosing again.
/// </summary>
public class BatchTests
{
    private static PackRanker Ranker => new(Snapshot.Index(), Snapshot.Odds());

    private static ICompletionTarget AllOfA1 =>
        RarityLadderTarget.UpTo("A1", Snapshot.Index().Ladder.Rungs[^1].Index, Snapshot.Index());

    [Fact]
    public void ABatchYieldsMoreThanASinglePack_ButFarLessThanTenTimes()
    {
        var one = Ranker.Batch(AllOfA1, new Collection(), "A1:Mewtwo", packs: 1);
        var ten = Ranker.Batch(AllOfA1, new Collection(), "A1:Mewtwo", packs: 10);

        Assert.True(ten.ExpectedNewCards > one.ExpectedNewCards);
        // Not 10x: a pack holds five cards, duplicates within the batch count once, and demand
        // shrinks as it is met.
        Assert.True(ten.ExpectedNewCards < one.ExpectedNewCards * 10);
    }

    [Fact]
    public void FromAnEmptyCollectionNoBatchCanBeWasted()
    {
        // A pack always contains cards and every one of them is wanted, so the chance of getting
        // nothing is exactly zero at any batch size. Worth pinning: it is the boundary where a
        // strict "ten beats one" assertion is false for a good reason rather than a bad one.
        var one = Ranker.Batch(AllOfA1, new Collection(), "A1:Mewtwo", packs: 1);
        var ten = Ranker.Batch(AllOfA1, new Collection(), "A1:Mewtwo", packs: 10);

        Assert.Equal(0, one.ChanceOfNothing, 9);
        Assert.Equal(0, ten.ChanceOfNothing, 9);
    }

    [Fact]
    public void OnANearlyFinishedSet_TheChanceOfAWastedBatchFallsSharplyWithSize()
    {
        // Where the risk actually lives. With one card left, a single pack is very likely to be
        // wasted and ten packs much less so, which is what a batch trades away: not odds, but the
        var index = Snapshot.Index();
        var inA1 = index.BySet["A1"].DistinctBy(c => c.OwnershipKey).ToArray();
        var owned = new Collection(inA1.Skip(1).ToDictionary(c => c.OwnershipKey, _ => 1));

        var one = Ranker.Batch(AllOfA1, owned, "A1:Mewtwo", packs: 1);
        var ten = Ranker.Batch(AllOfA1, owned, "A1:Mewtwo", packs: 10);

        Assert.True(one.ChanceOfNothing > 0.5, $"one pack wasted with p={one.ChanceOfNothing}");
        Assert.True(ten.ChanceOfNothing < one.ChanceOfNothing);
        Assert.True(ten.ExpectedNewCards > one.ExpectedNewCards);
    }

    [Fact]
    public void BatchesRankTheSetsAgainstEachOther()
    {
        // The decision the batch option actually forces: ten of WHICH set. An A1 target should
        // put A1's own packs at the top and never rank a pack that cannot help.
        var batches = Ranker.Batches(AllOfA1, new Collection());

        Assert.NotEmpty(batches);
        Assert.StartsWith("A1", batches[0].PackKey);
        Assert.All(batches, b => Assert.True(b.ExpectedNewCards > 0));
        // Ordered best first, so the page can present it as a ranking.
        Assert.Equal(
            batches.OrderByDescending(b => b.ExpectedNewCards).Select(b => b.PackKey),
            batches.Select(b => b.PackKey));
    }

    [Fact]
    public void ABatchOfTheBestPackBeatsABatchOfAWeakerOne()
    {
        var batches = Ranker.Batches(AllOfA1, new Collection());

        Assert.True(batches[0].ExpectedNewCards > batches[^1].ExpectedNewCards);
    }

    [Fact]
    public void UnavailablePacksAreLeftOutOfTheComparison()
    {
        // Recommending ten of a pack that is off sale is worse than useless.
        var away = new HashSet<string> { "A4b:Deluxe" };
        var everything = new CompositeTarget(
            Snapshot.Index().OpenableSets.Select(s =>
                (ICompletionTarget)RarityLadderTarget.UpTo(s, 3, Snapshot.Index())).ToArray());

        Assert.Contains(Ranker.Batches(everything, new Collection()), b => b.PackKey == "A4b:Deluxe");
        Assert.DoesNotContain(
            Ranker.Batches(everything, new Collection(), unavailablePacks: away),
            b => b.PackKey == "A4b:Deluxe");
    }

    [Fact]
    public void ABatchOfAPackThatCannotHelpYieldsNothing()
    {
        // A target from one set against a pack from another: no overlap, so the batch is dead
        // weight and must say so rather than returning a small non-zero figure.
        var batch = Ranker.Batch(AllOfA1, new Collection(), "A2:Dialga");

        Assert.Equal(0, batch.ExpectedNewCards, 9);
        Assert.Equal(1, batch.ChanceOfNothing, 9);
    }

    [Fact]
    public void AFinishedTargetYieldsNothingFromAnyBatch()
    {
        var index = Snapshot.Index();
        var owned = new Collection(
            index.BySet["A1"].DistinctBy(c => c.OwnershipKey).ToDictionary(c => c.OwnershipKey, _ => 9));

        var batch = Ranker.Batch(AllOfA1, owned, "A1:Mewtwo");

        Assert.Equal(0, batch.ExpectedNewCards, 9);
        Assert.Equal(1, batch.ChanceOfNothing, 9);
    }

    [Fact]
    public void TheDefaultBatchIsTheGamesTen()
    {
        Assert.Equal(10, GameRules.PacksPerBatch);
        Assert.Equal(10, Ranker.Batch(AllOfA1, new Collection(), "A1:Mewtwo").Packs);
    }
}
