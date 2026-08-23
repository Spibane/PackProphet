using PackProphet.Data;

namespace PackProphet.Tests;

public class SetCatalogTests
{
    private static SetCatalog Real() =>
        new(Snapshot.PublishedSets(), Snapshot.Index().BySet.Keys);

    [Fact]
    public void GroupsTheRealDataIntoSeriesAAndB()
    {
        var catalog = Real();
        Assert.Equal(new[] { "A", "B" }, catalog.Series);

        // Promo sets belong to their series, not a series of their own.
        Assert.Contains("PROMO-A", catalog.SetsIn("A"));
        Assert.Contains("PROMO-B", catalog.SetsIn("B"));
        Assert.Equal("A", catalog.SeriesOf("A4b"));
        Assert.Equal("B", catalog.SeriesOf("PROMO-B"));
    }

    [Fact]
    public void SetsAreInReleaseOrderWithPromosLast()
    {
        var a = Real().SetsIn("A");
        Assert.Equal("A1", a[0]);
        Assert.Equal("A1a", a[1]);
        Assert.Equal("A2", a[2]);
        Assert.Equal("PROMO-A", a[^1]);
    }

    [Fact]
    public void ANewSetMissingFromTheCatalogue_IsStillPlaced()
    {
        // sets.json lags cards.json for a brand-new set. It must appear anyway, or it would
        // vanish from the picker entirely and its cards become unreachable.
        var catalog = new SetCatalog(Snapshot.PublishedSets(), ["A1", "B1", "C1", "C2a"]);

        Assert.Contains("C", catalog.Series);
        Assert.Equal(new[] { "C1", "C2a" }, catalog.SetsIn("C"));
        Assert.Equal("C", catalog.SeriesOf("C1"));
    }

    [Fact]
    public void AWholeNewSeriesWorksWithNoPublishedGroupingAtAll()
    {
        var catalog = new SetCatalog(null, ["C1", "C1a", "PROMO-C", "D1"]);

        Assert.Equal(new[] { "C", "D" }, catalog.Series);
        Assert.Equal(new[] { "C1", "C1a", "PROMO-C" }, catalog.SetsIn("C"));
    }

    [Theory]
    [InlineData("A1", "A")]
    [InlineData("A4b", "A")]
    [InlineData("B12", "B")]
    [InlineData("PROMO-A", "A")]
    [InlineData("PROMO-C", "C")]
    public void SeriesIsDerivableFromACodeAlone(string code, string series) =>
        Assert.Equal(series, SetCatalog.SeriesFromCode(code));

    [Fact]
    public void OrderingSurvivesASeriesReachingTenSets()
    {
        // Ordinal sorting would put "A1a" after "A10"; numeric ordering must win.
        var catalog = new SetCatalog(null, ["A10", "A1a", "A2", "A1"]);
        Assert.Equal(new[] { "A1", "A1a", "A2", "A10" }, catalog.SetsIn("A"));
    }

    [Fact]
    public void ExposesTheHumanNameForASet()
    {
        Assert.Equal("Genetic Apex", Real().DisplayName("A1"));
        Assert.Equal("Deluxe Pack: ex", Real().DisplayName("A4b"));
        // Unknown codes fall back to the code rather than throwing.
        Assert.Equal("Z9", Real().DisplayName("Z9"));
    }

    [Fact]
    public void ReleaseDate_IsParsedFromTheSnapshot()
    {
        var sets = Real();

        // A1 shipped on a known date, so this pins the format rather than just the plumbing.
        Assert.Equal(new DateOnly(2024, 10, 30), sets.ReleaseDateOf("A1"));
        Assert.True(sets.IsReleased("A1", new DateOnly(2026, 1, 1)));
        Assert.False(sets.IsReleased("A1", new DateOnly(2024, 10, 29)));
    }

    [Fact]
    public void EverySetHasAParseableReleaseDate()
    {
        // The Packs page tells "out, but nobody has published its rates" apart from "not out
        // yet" using these dates. An unparseable date reads as released, so a format change
        // upstream would silently turn every future set into a released one.
        var sets = Real();
        var missing = sets.Series
            .SelectMany(sets.SetsIn)
            .Where(code => sets.ReleaseDateOf(code) is null)
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void UnknownSet_CountsAsReleased()
    {
        // cards.json runs ahead of sets.json for a brand-new set, so a set we have cards for but
        // no metadata about must not be reported as unreleased — its cards are droppable.
        Assert.True(Real().IsReleased("ZZ9", new DateOnly(2026, 1, 1)));
        Assert.Null(Real().ReleaseDateOf("ZZ9"));
    }

    [Fact]
    public void UnparseableReleaseDate_CountsAsReleased()
    {
        var info = new SetInfo { Code = "X1", ReleaseDate = "soon" };

        Assert.Null(info.ReleasedOn);
        Assert.True(info.IsReleased(new DateOnly(2026, 1, 1)));
    }
}
