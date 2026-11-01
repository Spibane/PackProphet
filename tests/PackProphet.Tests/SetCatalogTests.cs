using PackProphet.Data;

namespace PackProphet.Tests;

public class SetCatalogTests
{
    private static SetCatalog Real() =>
        new(Snapshot.PublishedSets(), Snapshot.Index().BySet.Keys);

    [Fact]
    public void GroupsTheRealDataIntoItsSeries_WithSpecialLast()
    {
        var catalog = Real();
        Assert.Equal(new[] { "A", "B", "C", "Special" }, catalog.Series);

        // Promo sets belong to their series, not a series of their own.
        Assert.Contains("PROMO-A", catalog.SetsIn("A"));
        Assert.Contains("PROMO-B", catalog.SetsIn("B"));
        Assert.Contains("PROMO-C", catalog.SetsIn("C"));
        Assert.Equal("A", catalog.SeriesOf("A4b"));
        Assert.Equal("B", catalog.SeriesOf("PROMO-B"));
        Assert.Equal(SetCatalog.SpecialSeries, catalog.SeriesOf("SP1"));
    }

    [Fact]
    public void TheNewestSeriesIsTheNewestLetteredOne_NotSpecial()
    {
        // Special sorts last and is never where a page should open: its packs are opened on
        // their own weekly hourglass.
        var catalog = Real();
        Assert.Equal("Special", catalog.Series[^1]);
        Assert.Equal("C", catalog.NewestSeries);
    }

    [Fact]
    public void SpecialPackSetsSortAfterEveryLetteredSet()
    {
        // "Special" alphabetically would land between R and T. A future series T is unlikely,
        // but the order should not depend on that.
        var catalog = new SetCatalog(null, ["SP1", "C1", "T1", "PROMO-T"]);

        Assert.Equal(new[] { "C", "T", "Special" }, catalog.Series);
        Assert.True(string.CompareOrdinal(catalog.SortKey("SP1"), catalog.SortKey("PROMO-T")) > 0);
    }

    [Fact]
    public void ANewSpecialPackSetIsFiledUnderSpecial_BeforeTheSetListKnowsIt()
    {
        var catalog = new SetCatalog(Snapshot.PublishedSets(), ["SP1", "SP2"]);

        Assert.Equal(new[] { "SP1", "SP2" }, catalog.SetsIn(SetCatalog.SpecialSeries));
        Assert.DoesNotContain("SP", catalog.Series);
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
        var catalog = new SetCatalog(Snapshot.PublishedSets(), ["A1", "B1", "D1", "D2a"]);

        Assert.Contains("D", catalog.Series);
        Assert.Equal(new[] { "D1", "D2a" }, catalog.SetsIn("D"));
        Assert.Equal("D", catalog.SeriesOf("D1"));
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

    [Fact]
    public void SortKey_PutsSetsInReleaseOrderWithinTheirSeries()
    {
        var sets = Real();
        var ordered = new[] { "A1", "A1a", "A2", "A2a", "A2b", "A3", "A3a", "A3b", "A4", "A4a", "A4b" };

        Assert.Equal(ordered, ordered.OrderBy(sets.SortKey, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void SortKey_PutsAPromoSetLastInItsSeries()
    {
        // Not by date: PROMO-A shares A1's release date because that is when promos began, and a
        // promo set keeps growing long after the numbered sets beside it.
        var sets = Real();
        var seriesA = new[] { "PROMO-A", "A2", "A1", "A4b" };

        Assert.Equal(
            new[] { "A1", "A2", "A4b", "PROMO-A" },
            seriesA.OrderBy(sets.SortKey, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void SortKey_KeepsSeriesTogether()
    {
        var sets = Real();
        var mixed = new[] { "B1", "PROMO-A", "A1", "PROMO-B", "A4b", "B4" };

        Assert.Equal(
            new[] { "A1", "A4b", "PROMO-A", "B1", "B4", "PROMO-B" },
            mixed.OrderBy(sets.SortKey, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void SortKey_PadsTheNumberSoATenthSetDoesNotSortBeforeTheSecond()
    {
        // Ordinally "A10" precedes "A2". Harmless today and silently wrong the moment a series
        // reaches ten sets, which is the sort of thing nobody notices for a release or two.
        var sets = Real();

        Assert.Equal(
            new[] { "A2", "A9", "A10" },
            new[] { "A10", "A2", "A9" }.OrderBy(sets.SortKey, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void SortKey_OrdersCardsBySetThenNumber()
    {
        var sets = Real();
        var index = Snapshot.Index();

        var a1 = index.BySet["A1"];
        var a2 = index.BySet["A2"];

        var first = a1.OrderBy(c => c.Number).First();
        var last = a1.OrderBy(c => c.Number).Last();
        var nextSet = a2.OrderBy(c => c.Number).First();

        Assert.True(string.CompareOrdinal(sets.SortKey(first), sets.SortKey(last)) < 0);
        Assert.True(string.CompareOrdinal(sets.SortKey(last), sets.SortKey(nextSet)) < 0);
    }

    [Fact]
    public void A_sets_packs_are_the_card_datas_where_the_two_disagree()
    {
        // Until 2.12.0, sets.json named B4a's pack after B4's while every B4a card said
        // otherwise. Upstream has fixed it, so the mistake is put back here.
        var published = Snapshot.PublishedSets();
        published["B"].Single(s => s.Code == "B4a").Packs = ["Ruler of the Skies"];

        var ix = Snapshot.Index();
        var catalog = new SetCatalog(published, ix.BySet.Keys, ix.AllPackKeys);

        Assert.Equal(ix.AllPackKeys.Where(k => k.StartsWith("B4a:", StringComparison.Ordinal))
                                   .Select(k => k[4..]),
                     catalog.Info("B4a")!.Packs);
        Assert.Equal("Team Rocket\u2019s Ambition", catalog.DisplayName("B4a"));
    }

    [Fact]
    public void A_set_the_card_data_does_not_have_keeps_the_set_lists_packs()
    {
        var published = Snapshot.PublishedSets();
        published["B"].Add(new SetInfo { Code = "B9z", Packs = ["Future"] });

        var catalog = new SetCatalog(published, Snapshot.Index().BySet.Keys, Snapshot.Index().AllPackKeys);

        Assert.Equal(["Future"], catalog.Info("B9z")!.Packs);
    }
}
