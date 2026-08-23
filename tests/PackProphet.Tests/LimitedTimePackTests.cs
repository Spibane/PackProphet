using PackProphet.Engine;

namespace PackProphet.Tests;

public class LimitedTimePackTests
{
    private static PackRanker Ranker => new(Snapshot.Index(), Snapshot.Odds());
    private static readonly HashSet<string> DeluxeAway = new() { "A4b:Deluxe" };

    [Fact]
    public void DeluxeIsRecognisedAsLimitedTime_AndNothingElseIs()
    {
        Assert.Equal(new[] { "A4b:Deluxe" }, Snapshot.Odds().LimitedTimePacks.OrderBy(p => p));
        Assert.True(GameRules.IsLimitedTimePack("Deluxe"));
        Assert.False(GameRules.IsLimitedTimePack("Mewtwo"));
    }

    [Fact]
    public void WhenDeluxeIsAway_ItIsNotRecommended()
    {
        // Recommending a pack nobody can buy today is worse than useless.
        var target = RarityLadderTarget.UpTo("A4b", 3, Snapshot.Index(), 1);

        Assert.Contains(Ranker.Rank(target, new Collection()), s => s.PackKey == "A4b:Deluxe");
        Assert.DoesNotContain(Ranker.Rank(target, new Collection(), DeluxeAway),
            s => s.PackKey == "A4b:Deluxe");
    }

    [Fact]
    public void WhileDeluxeIsAway_ReprintedCardsFallBackToTheirOriginalPacks()
    {
        // Deluxe reprints earlier sets, so its absence changes the odds for A1 cards too —
        // they revert to A1's own packs rather than losing a route entirely.
        var reprint = Snapshot.Index().BySet["A4b"].First(c =>
            Snapshot.Index().ByOwnershipKey[c.OwnershipKey].Any(e => e.Set == "A1"));

        var withDeluxe = Snapshot.Odds().BestRatesByCard();
        var withoutDeluxe = Snapshot.Odds().BestRatesByCard(DeluxeAway);

        // The A1 printing keeps a positive rate either way.
        var a1Entry = Snapshot.Index().ByOwnershipKey[reprint.OwnershipKey].First(e => e.Set == "A1");
        Assert.True(withDeluxe.GetValueOrDefault(a1Entry.Key) > 0);
        Assert.True(withoutDeluxe.GetValueOrDefault(a1Entry.Key) > 0);

        // The A4b printing loses its only source.
        Assert.True(withDeluxe.GetValueOrDefault(reprint.Key) > 0);
        Assert.Equal(0.0, withoutDeluxe.GetValueOrDefault(reprint.Key));
    }

    [Fact]
    public void CardsOnlyInDeluxe_BecomeUnreachableWhileItIsAway()
    {
        // A4b's foils exist nowhere else, so an A4b target is genuinely blocked meanwhile —
        // and must be reported as such rather than quietly re-priced.
        var target = RarityLadderTarget.UpTo("A4b", 3, Snapshot.Index(), 1);

        Assert.Empty(Ranker.Unreachable(target, new Collection()));
        Assert.NotEmpty(Ranker.Unreachable(target, new Collection(), DeluxeAway));
    }

    [Fact]
    public void WhileDeluxeIsAway_TheEstimateCoversFewerCards_SoItIsNotComparable()
    {
        // A counter-intuitive but correct consequence, and a trap for the UI: with Deluxe away
        // its exclusive cards become unreachable and drop OUT of the estimate, so the headline
        // number FALLS even though the situation is worse. The estimate silently changed what
        // it covers.
        //
        // That is why the number must always be shown with the count of cards it covers, and
        // the unreachable remainder beside it. Two estimates over different scopes cannot be
        // compared, and presenting the drop on its own would read as "this got cheaper".
        var ix = Snapshot.Index();
        var everything = new CompositeTarget(
            ix.OpenableSets.Select(s => (ICompletionTarget)RarityLadderTarget.UpTo(s, 3, Snapshot.Index(), 1))
              .ToArray());

        var withIt = Ranker.BestCasePacksToFinish(everything, new Collection());
        var without = Ranker.BestCasePacksToFinish(everything, new Collection(), DeluxeAway);

        Assert.True(double.IsFinite(withIt));
        Assert.True(double.IsFinite(without));

        // Fewer cards priced, hence a smaller number over a smaller scope.
        var blockedWith = Ranker.Unreachable(everything, new Collection()).Count;
        var blockedWithout = Ranker.Unreachable(everything, new Collection(), DeluxeAway).Count;
        Assert.True(blockedWithout > blockedWith);
        Assert.True(without < withIt);
    }

    [Fact]
    public void AvailabilityIsCachedPerPackSet_NotSharedAcrossThem()
    {
        // The rate cache is keyed by which packs are in play; a stale entry would silently
        // apply Deluxe odds while Deluxe is away.
        var a = Snapshot.Odds().BestRatesByCard();
        var b = Snapshot.Odds().BestRatesByCard(DeluxeAway);
        var aAgain = Snapshot.Odds().BestRatesByCard();

        Assert.NotSame(a, b);
        Assert.Same(a, aAgain);
    }
}
