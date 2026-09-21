using PackProphet.Data;

namespace PackProphet.Tests;

/// <summary>
/// Which promos count as recent, and the one property the whole idea rests on: that a promo's
/// number says when it arrived.
///
/// That is asserted against the shipped snapshot rather than a fixture, because it is a claim
/// about the community dataset and not about this code. The volumes are the evidence — if a
/// refresh ever lands a volume numbered below the one before it, the ordering is no longer a
/// clock and the page is showing the wrong cards with no other symptom.
/// </summary>
public class RecentPromosTests
{
    private static CardIndex Ix => Snapshot.Index();

    private static SetCatalog Sets => new(Snapshot.PublishedSets(), Ix.BySet.Keys);

    /// <summary>
    /// Volume names differ between the two promo sets — "Vol. 7" in PROMO-A, "B Series Vol. 7" in
    /// PROMO-B — so the number is read off the end rather than matched against a prefix.
    /// </summary>
    private static int? VolumeOf(PocketCard card)
    {
        if (card.Packs is not [var pack] || !pack.Contains("Vol.", StringComparison.Ordinal))
            return null;

        var digits = pack[(pack.IndexOf("Vol.", StringComparison.Ordinal) + 4)..].Trim();
        return int.TryParse(digits, out var n) ? n : null;
    }

    [Theory]
    [InlineData("PROMO-A")]
    [InlineData("PROMO-B")]
    public void PromoNumbersRunInReleaseOrder(string set)
    {
        // The volumes are the one promo grouping with a published sequence, so they are the only
        // thing that can check the numbering against it. Both sets, because the one being filled
        // is the one the page reads and the one that has finished is the longer proof.
        var volumes = Ix.BySet[set]
            .Select(c => (Card: c, Volume: VolumeOf(c)))
            .Where(x => x.Volume is not null)
            .GroupBy(x => x.Volume!.Value)
            .OrderBy(g => g.Key)
            .Select(g => (Volume: g.Key,
                          First: g.Min(x => x.Card.Number),
                          Last: g.Max(x => x.Card.Number)))
            .ToArray();

        Assert.True(volumes.Length >= 12, $"{set} should hold every published volume");

        for (var i = 1; i < volumes.Length; i++)
        {
            Assert.True(
                volumes[i].First > volumes[i - 1].Last,
                $"{set} Vol. {volumes[i].Volume} starts at {volumes[i].First}, which is not past "
                + $"Vol. {volumes[i - 1].Volume} ending at {volumes[i - 1].Last} — promo numbers "
                + "are no longer issued in release order, and RecentPromos is built on that");
        }
    }

    [Fact]
    public void AThirdOfThePromosAreInNoVolumeAtAll()
    {
        // Why the list is a flat run of the newest rather than the volumes the data offers: the
        // promos with no volume are the Wonder Pick and mission cards, which is precisely what an
        // event hands out. Grouping by volume would file two thirds of the set and strand the
        // part this page exists for.
        var loose = Ix.BySet["PROMO-B"].Count(c => VolumeOf(c) is null);

        Assert.True(loose > Ix.BySet["PROMO-B"].Count / 4,
                    $"only {loose} of PROMO-B's {Ix.BySet["PROMO-B"].Count} are outside a volume");
    }

    [Fact]
    public void TheCurrentPromoSetIsTheNewestSeriesOne()
    {
        // PROMO-B while series B is the newest. Asserted through the catalogue's own ordering
        // rather than by naming B, so a series C does not make this a lie.
        var current = RecentPromos.CurrentSet(Ix, Sets);

        Assert.NotNull(current);
        Assert.True(CardIndex.IsPromoSet(current));

        var newestSeries = Sets.Series.Last();
        Assert.Equal(newestSeries, SetCatalog.SeriesFromCode(current));
    }

    [Fact]
    public void TheNewestAreTheHighestNumbered_AndTheWindowIsRespected()
    {
        var cards = RecentPromos.Newest(Ix, "PROMO-A", 5);

        Assert.Equal(5, cards.Count);
        Assert.Equal(
            Ix.BySet["PROMO-A"].Select(c => c.Number).OrderByDescending(n => n).Take(5).ToArray(),
            cards.Select(c => c.Number).ToArray());
    }

    [Fact]
    public void OwnedPromosAreNotFilteredOut()
    {
        // A promo can be earned more than once, so a second copy is an ordinary thing to record.
        // This helper never sees the collection at all, which is how that is guaranteed rather
        // than merely intended -- the signature is the assertion.
        var all = RecentPromos.Newest(Ix, "PROMO-A", RecentPromos.Window);

        Assert.Equal(RecentPromos.Window, all.Count);
        Assert.All(all, c => Assert.Equal("PROMO-A", c.Set));
    }

    [Fact]
    public void ASetShorterThanTheWindowGivesWhatItHas()
    {
        var cards = RecentPromos.Newest(Ix, "PROMO-A", 10_000);

        Assert.Equal(Ix.BySet["PROMO-A"].Count, cards.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonWindowIsEmptyRatherThanAThrow(int count) =>
        Assert.Empty(RecentPromos.Newest(Ix, "PROMO-A", count));

    [Fact]
    public void ASetThatIsNotInTheDataIsEmptyRatherThanAThrow() =>
        Assert.Empty(RecentPromos.Newest(Ix, "PROMO-Z"));
}
