using PackProphet.Data;
using PackProphet.Vision;

namespace PackProphet.Tests;

/// <summary>
/// Covers the recognition rules without a browser or an image, which is the point of keeping them
/// in Core: a <see cref="ShotScan"/> is a handful of numbers, so every rule that decides what ends
/// up in someone's collection can be stated as a case here.
/// </summary>
public class ScreenshotReadingTests
{
    private static CardIndex Ix => Snapshot.Index();

    /// <summary>
    /// Well-separated fingerprints, deterministically. Two arbitrary 128-bit values disagree on
    /// about 64 bits, an order of magnitude past the match threshold, so a table built this way
    /// stands in for art that looks nothing alike.
    /// </summary>
    private static ArtHash Distinct(int seed)
    {
        var rng = new Random(seed * 7919 + 13);
        var lo = (ulong)rng.NextInt64();
        var hi = (ulong)rng.NextInt64();
        return new ArtHash(lo ^ 0x5555_5555_5555_5555, hi ^ 0x0F0F_0F0F_0F0F_0F0F);
    }

    private static ArtHash Nudge(ArtHash h, int bits)
    {
        var rows = h.Rows;
        for (var i = 0; i < bits; i++) rows ^= 1UL << (i * 5 % 64);
        return h with { Rows = rows };
    }

    /// <summary>Fingerprints for a run of one set's cards, numbered from 1.</summary>
    private static ArtHashTable TableFor(string set, int count) =>
        new(Enumerable.Range(1, count).Select(n => new ArtHashEntry(set, n, Distinct(n))), "2026-08-27");

    private static ShotCell Cell(int row, int col, ArtHash? hash, double sat = 0.4, double detail = 0.5) =>
        new()
        {
            Row = row, Col = col,
            Hash = hash?.ToString() ?? "",
            Luma = 0.5, Saturation = sat,
            Detail = hash is null ? 0.01 : detail,
        };

    private static ShotScan Scan(int rows, int cols, IEnumerable<ShotCell> cells, double relCellWidth = 0.2) =>
        new()
        {
            Ok = true, Width = 1170, Height = 2532,
            Lattice = new ShotLattice
            {
                Rows = rows, Cols = cols, CellWidth = 200, CellHeight = 280,
                Confidence = 0.9, RelativeCellWidth = relCellWidth,
            },
            Cells = cells.ToList(),
        };

    [Fact]
    public void RecognisedArt_NamesTheCard()
    {
        var reader = new ScreenshotReader(Ix, TableFor("A1", 20));
        var scan = Scan(1, 3, [Cell(0, 0, Distinct(5))], relCellWidth: 0.3);

        var reading = reader.Read(scan, CardScreen.PackReveal);

        var match = Assert.Single(reading.Matches);
        Assert.Equal("A1-5", match.Card.Key);
        Assert.Equal(MatchSource.Art, match.Source);
        Assert.Equal(0, match.Distance);
        Assert.Equal(1, match.Confidence);
    }

    [Fact]
    public void ArtWithinToleranceStillMatches_ButLosesConfidence()
    {
        var reader = new ScreenshotReader(Ix, TableFor("A1", 20));
        var scan = Scan(1, 3, [Cell(0, 0, Nudge(Distinct(5), 6))], relCellWidth: 0.3);

        var match = Assert.Single(reader.Read(scan, CardScreen.PackReveal).Matches);
        Assert.Equal("A1-5", match.Card.Key);
        Assert.InRange(match.Confidence, 0.5, 0.99);
    }

    [Fact]
    public void TwoRivalsTooCloseTogether_ReadAsNothingRatherThanAGuess()
    {
        // The failure this guards is specific to card art: a full art and its plain printing are
        // near neighbours, so the closest entry can be closest by a bit or two. Saying nothing is
        // the required outcome — a wrong card added silently is worse than an unread slot.
        var target = Distinct(5);
        var table = new ArtHashTable(
            [new ArtHashEntry("A1", 5, target), new ArtHashEntry("A1", 6, Nudge(target, 3))], "x");

        var reading = new ScreenshotReader(Ix, table)
            .Read(Scan(1, 3, [Cell(0, 0, target)], relCellWidth: 0.3), CardScreen.PackReveal);

        Assert.Empty(reading.Matches);
        Assert.Equal(1, reading.UnreadCells);
    }

    [Fact]
    public void ReprintsSharingArtworkAreNotRivals()
    {
        // 215 entries are re-listings of a card printed elsewhere and share the artwork exactly.
        // A1-2 and A4b-3 are one such pair: the same artwork filename, so the same ownable card.
        // Counting them as competition would make every reprinted card permanently unreadable.
        Assert.Equal(Ix.ByKey["A1-2"].OwnershipKey, Ix.ByKey["A4b-3"].OwnershipKey);

        var shared = Distinct(5);
        var table = new ArtHashTable(
            [new ArtHashEntry("A1", 2, shared), new ArtHashEntry("A4b", 3, shared)], "x");

        var match = Assert.Single(new ScreenshotReader(Ix, table)
            .Read(Scan(1, 3, [Cell(0, 0, shared)], relCellWidth: 0.3), CardScreen.PackReveal).Matches);

        Assert.Contains(match.Card.Key, new[] { "A1-2", "A4b-3" });
    }

    [Fact]
    public void TwoDifferentCardsSharingAFingerprintAreATieRatherThanAWinner()
    {
        // A foil printing and its plain twin. They are different ownable cards, and after the window
        // stopped covering the card's frame they can land within a bit or two of each other — so the
        // ambiguity rule has to be about which CARD an entry belongs to, not about whether two
        // fingerprints happen to be equal. Keyed on the fingerprint, this returned whichever entry
        // came first and recorded the wrong printing silently.
        Assert.NotEqual(Ix.ByKey["A1-1"].OwnershipKey, Ix.ByKey["A4b-2"].OwnershipKey);

        var shared = Distinct(5);
        var table = new ArtHashTable(
            [new ArtHashEntry("A1", 1, shared), new ArtHashEntry("A4b", 2, shared)], "x");

        var reading = new ScreenshotReader(Ix, table)
            .Read(Scan(1, 3, [Cell(0, 0, shared)], relCellWidth: 0.3), CardScreen.PackReveal);

        Assert.Empty(reading.Matches);
        Assert.Equal(1, reading.UnreadCells);
    }

    [Fact]
    public void AFlatRegionIsNeverMatched()
    {
        var table = new ArtHashTable([new ArtHashEntry("A1", 5, new ArtHash(0, 0))], "x");
        var flat = Cell(0, 0, new ArtHash(0, 0));

        Assert.Empty(new ScreenshotReader(Ix, table)
            .Read(Scan(1, 3, [flat], relCellWidth: 0.3), CardScreen.PackReveal).Matches);
    }

    [Fact]
    public void BlankSlotsBetweenRecognisedCards_AreNamedByPositionAndMarkedMissing()
    {
        // The whole reason a collection grid is worth reading: the cards you do not own are the
        // ones with no art to recognise, and the list's numbering names them anyway.
        var reader = new ScreenshotReader(Ix, TableFor("A1", 40));
        var cells = new[]
        {
            Cell(0, 0, Distinct(1)),
            Cell(0, 1, null),            // placeholder: not owned
            Cell(0, 2, Distinct(3)),
            Cell(0, 3, null),
            Cell(1, 0, Distinct(5)),
        };

        var reading = reader.Read(Scan(2, 4, cells), CardScreen.OwnershipGrid);
        var byKey = reading.Matches.ToDictionary(m => m.Card.Key);

        Assert.Equal(5, reading.Matches.Count);
        Assert.True(byKey["A1-1"].Owned);
        Assert.True(byKey["A1-3"].Owned);
        Assert.True(byKey["A1-5"].Owned);

        Assert.False(byKey["A1-2"].Owned);
        Assert.Equal(MatchSource.GridPosition, byKey["A1-2"].Source);
        Assert.False(byKey["A1-4"].Owned);
        Assert.Empty(reading.Notes);
    }

    [Fact]
    public void PositionalInferenceSurvivesAScrolledShot()
    {
        // The top-left slot is card 17, not card 1. Nothing anchors that but the recognised cards.
        var reader = new ScreenshotReader(Ix, TableFor("A1", 60));
        var cells = new[] { Cell(0, 0, Distinct(17)), Cell(0, 1, null), Cell(0, 2, Distinct(19)) };

        var reading = reader.Read(Scan(1, 3, cells), CardScreen.OwnershipGrid);

        var gap = Assert.Single(reading.Matches, m => m.Source == MatchSource.GridPosition);
        Assert.Equal("A1-18", gap.Card.Key);
    }

    [Fact]
    public void CardsOutOfNumberOrder_LeaveTheBlanksAlone()
    {
        // A list sorted by rarity, a filter, or a shot spanning a page boundary. The positional
        // reasoning is simply invalid there, and inventing numbers would mark owned cards missing.
        var reader = new ScreenshotReader(Ix, TableFor("A1", 60));
        var cells = new[] { Cell(0, 0, Distinct(40)), Cell(0, 1, null), Cell(0, 2, Distinct(7)) };

        var reading = reader.Read(Scan(1, 3, cells), CardScreen.OwnershipGrid);

        Assert.DoesNotContain(reading.Matches, m => m.Source == MatchSource.GridPosition);
        Assert.Contains(reading.Notes, n => n.Contains("not in set-number order"));
    }

    [Fact]
    public void ASingleRecognisedCardCannotAnchorAWholeScreen()
    {
        var reader = new ScreenshotReader(Ix, TableFor("A1", 60));
        var cells = new[] { Cell(0, 0, Distinct(9)), Cell(0, 1, null), Cell(0, 2, null) };

        var reading = reader.Read(Scan(1, 3, cells), CardScreen.OwnershipGrid);

        Assert.Single(reading.Matches);
        Assert.DoesNotContain(reading.Matches, m => m.Source == MatchSource.GridPosition);
    }

    [Fact]
    public void TwoAnchoredRowsCarryTheNumberingIntoARowThatAnchoredNothing()
    {
        // Rows 0 and 1 agree that a row is four cards wide, which is a statement about the grid
        // rather than about either row — so row 2, which recognised only one card, inherits it.
        var reader = new ScreenshotReader(Ix, TableFor("A1", 40));
        var cells = new[]
        {
            Cell(0, 0, Distinct(1)), Cell(0, 1, Distinct(2)),
            Cell(1, 0, Distinct(5)), Cell(1, 1, Distinct(6)),
            Cell(2, 0, Distinct(9)), Cell(2, 1, null), Cell(2, 2, null),
        };

        var reading = reader.Read(Scan(3, 4, cells), CardScreen.OwnershipGrid);
        var inferred = reading.Matches.Where(m => m.Source == MatchSource.GridPosition)
                                      .Select(m => m.Card.Key).ToArray();

        Assert.Equal(["A1-10", "A1-11"], inferred);
    }

    [Fact]
    public void ARowOfChromeAnchorsNothingAndCostsNothing()
    {
        // The lattice tiles over whatever is on screen, so a full-screen shot can hand the reader a
        // row that is really the navigation bar. It recognises nothing, so it anchors nothing, and
        // the rows that are cards are unaffected — which is the reason anchoring is per row.
        var reader = new ScreenshotReader(Ix, TableFor("A1", 40));
        var cells = new[]
        {
            Cell(0, 0, null), Cell(0, 1, null), Cell(0, 2, null),
            Cell(1, 0, Distinct(1)), Cell(1, 1, null), Cell(1, 2, Distinct(3)),
        };

        var reading = reader.Read(Scan(2, 3, cells), CardScreen.OwnershipGrid);

        var gap = Assert.Single(reading.Matches, m => m.Source == MatchSource.GridPosition);
        Assert.Equal("A1-2", gap.Card.Key);
    }

    [Fact]
    public void ArtWithTheColourDrainedOutOfIt_ReadsAsNotOwned()
    {
        var reader = new ScreenshotReader(Ix, TableFor("A1", 20));
        var cells = new[] { Cell(0, 0, Distinct(1), sat: 0.4), Cell(0, 1, Distinct(2), sat: 0.01) };

        var reading = reader.Read(Scan(1, 2, cells), CardScreen.OwnershipGrid);
        var byKey = reading.Matches.ToDictionary(m => m.Card.Key);

        Assert.True(byKey["A1-1"].Owned);
        Assert.False(byKey["A1-2"].Owned);
    }

    [Fact]
    public void AFlatSlotBesideEquallyFlatCards_IsNotCalledMissing()
    {
        // A device or a compression setting that renders everything soft. The slot is below the
        // absolute floor, but so are the cards recognised either side of it, so it is not the
        // conspicuously blank thing a placeholder is — and a card the user owns must not be
        // proposed for deletion on that evidence.
        var reader = new ScreenshotReader(Ix, TableFor("A1", 40));
        var cells = new[]
        {
            Cell(0, 0, Distinct(1), detail: 0.03),
            new ShotCell { Row = 0, Col = 1, Hash = "", Saturation = 0.2, Detail = 0.02 },
            Cell(0, 2, Distinct(3), detail: 0.03),
        };

        var reading = reader.Read(Scan(1, 3, cells), CardScreen.OwnershipGrid);

        Assert.DoesNotContain(reading.Matches, m => m.Source == MatchSource.GridPosition);
        Assert.Equal(1, reading.UnreadCells);
    }

    [Fact]
    public void ASlotWithArtThatCouldNotBeRead_IsCountedNotDeleted()
    {
        // A foil, a crop, a card mid-animation. It is owned as far as anyone knows, so the one
        // thing the reader must not do is report it missing.
        var reader = new ScreenshotReader(Ix, TableFor("A1", 40));
        var cells = new[]
        {
            Cell(0, 0, Distinct(1)),
            Cell(0, 1, Distinct(999)),   // real art, absent from the table
            Cell(0, 2, Distinct(3)),
        };

        var reading = reader.Read(Scan(1, 3, cells), CardScreen.OwnershipGrid);

        Assert.Equal(1, reading.UnreadCells);
        Assert.DoesNotContain(reading.Matches, m => m.Card.Key == "A1-2");
    }

    [Fact]
    public void APackReveal_AddsEveryCardItSeesAndDecidesNoOwnership()
    {
        var reader = new ScreenshotReader(Ix, TableFor("A1", 20));
        var cells = Enumerable.Range(1, 5).Select(i => Cell(0, i - 1, Distinct(i))).ToArray();

        var reading = reader.Read(Scan(1, 5, cells, relCellWidth: 0.19), CardScreen.PackReveal);

        Assert.Equal(5, reading.Matches.Count);
        Assert.All(reading.Matches, m => Assert.True(m.Owned));
        Assert.Empty(reading.Notes);
    }

    [Fact]
    public void AShortRowSaysSoRatherThanLookingComplete()
    {
        var reader = new ScreenshotReader(Ix, TableFor("A1", 20));
        var cells = new[] { Cell(0, 0, Distinct(1)), Cell(0, 1, Distinct(2)) };

        var reading = reader.Read(Scan(1, 5, cells, relCellWidth: 0.19), CardScreen.WonderPick);

        Assert.Equal(CardScreen.WonderPick, reading.Screen);
        Assert.Contains(reading.Notes, n => n.Contains("five cards"));
    }

    [Fact]
    public void ScreenIsInferredFromGeometry_AndFlaggedAsAGuess()
    {
        var reader = new ScreenshotReader(Ix, TableFor("A1", 40));

        // Five small cards across, several rows: the ownership list, which shows the whole set.
        var ownership = reader.Read(Scan(4, 5,
            [Cell(0, 0, Distinct(1)), Cell(1, 0, Distinct(2))], relCellWidth: 0.19));
        Assert.Equal(CardScreen.OwnershipGrid, ownership.Screen);
        Assert.True(ownership.ScreenWasInferred);

        // Three large cards across: the copies list, which shows only what you own.
        var copies = reader.Read(Scan(3, 3,
            [Cell(0, 0, Distinct(1)), Cell(1, 0, Distinct(2)), Cell(2, 0, Distinct(3))],
            relCellWidth: 0.32));
        Assert.Equal(CardScreen.CopiesGrid, copies.Screen);

        // A hand of five, which the game lays out three then two — so two populated rows of large
        // cards, and never more than two.
        var hand = reader.Read(Scan(2, 3,
            [Cell(0, 0, Distinct(1)), Cell(0, 1, Distinct(2)), Cell(1, 0, Distinct(3))],
            relCellWidth: 0.32));
        Assert.Equal(CardScreen.PackReveal, hand.Screen);

        // A pack's reveal and a Wonder Pick's line-up are the same picture. Nothing in the geometry
        // separates them, so the pack is the default and the page the user is on decides.
        Assert.False(reader.Read(Scan(2, 3, [Cell(0, 0, Distinct(1))], relCellWidth: 0.32),
                                 CardScreen.WonderPick).ScreenWasInferred);
    }

    [Fact]
    public void TheCopiesListNeverProposesDeletingAnything()
    {
        // The safety rule that separates the two card lists. This list leaves unowned cards out
        // rather than drawing them blank, so a gap in it means "not shown" — which may be "not
        // owned", or "on the next page", or "filtered out". Reasoning from it would propose
        // deleting cards the user owns.
        var reader = new ScreenshotReader(Ix, TableFor("A1", 40));
        var cells = new[]
        {
            Cell(0, 0, Distinct(1)),
            Cell(0, 1, null),            // whatever this is, it is not evidence
            Cell(0, 2, Distinct(3)),
        };

        var reading = reader.Read(Scan(1, 3, cells, relCellWidth: 0.32), CardScreen.CopiesGrid);

        Assert.All(reading.Matches, m => Assert.True(m.Owned));
        Assert.All(reading.Matches, m => Assert.Equal(MatchSource.Art, m.Source));
        Assert.DoesNotContain(reading.Matches, m => m.Card.Key == "A1-2");
    }

    [Fact]
    public void TheCopiesListSaysNothingAboutWhatItCannotSee()
    {
        // It is an import. It adds the cards it read, and a note explaining that this screen does
        // not show unowned cards answers a question the screen never raised — while sitting on top
        // of the reading, which is the thing there to be read.
        var reader = new ScreenshotReader(Ix, TableFor("A1", 40));

        var reading = reader.Read(
            Scan(1, 3, [Cell(0, 0, Distinct(1))], relCellWidth: 0.32), CardScreen.CopiesGrid);

        Assert.DoesNotContain(reading.Notes, n => n.Contains("missing", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(reading.Notes, n => n.Contains("five-across"));
    }

    [Fact]
    public void CopiesAreNullUntilTheyAreActuallyRead()
    {
        // The copies list prints a count on every card and reading those digits is not built yet.
        // Null has to mean "this screen did not say", never "none" — a zero here would erase.
        var reader = new ScreenshotReader(Ix, TableFor("A1", 40));

        var reading = reader.Read(
            Scan(1, 3, [Cell(0, 0, Distinct(1))], relCellWidth: 0.32), CardScreen.CopiesGrid);

        Assert.All(reading.Matches, m => Assert.Null(m.Copies));
    }

    [Fact]
    public void CardsFromSeveralSetsAreFlaggedForChecking()
    {
        var table = new ArtHashTable(
            [new ArtHashEntry("A1", 5, Distinct(1)), new ArtHashEntry("A2", 5, Distinct(2))], "x");
        var cells = new[] { Cell(0, 0, Distinct(1)), Cell(0, 1, Distinct(2)) };

        var reading = new ScreenshotReader(Ix, table)
            .Read(Scan(1, 5, cells, relCellWidth: 0.19), CardScreen.PackReveal);

        Assert.Contains(reading.Notes, n => n.Contains("2 sets"));
    }

    [Fact]
    public void NoLatticeAndNoTableBothFailWithSomethingSayable()
    {
        var reader = new ScreenshotReader(Ix, TableFor("A1", 20));

        var empty = reader.Read(new ShotScan { Ok = true, Lattice = null }, CardScreen.OwnershipGrid);
        Assert.False(empty.Ok);
        Assert.Contains("No cards were found", empty.Error);

        var noTable = new ScreenshotReader(Ix, ArtHashTable.Empty)
            .Read(Scan(1, 3, [Cell(0, 0, Distinct(1))]), CardScreen.PackReveal);
        Assert.False(noTable.Ok);
        Assert.Contains("fingerprints", noTable.Error);

        var broken = reader.Read(ShotScan.Failed("That file could not be read as an image."));
        Assert.False(broken.Ok);
        Assert.Equal("That file could not be read as an image.", broken.Error);
    }

    [Fact]
    public void TheTableFileRoundTrips()
    {
        var table = TableFor("A1", 30);
        var parsed = ArtHashTable.Parse(table.Serialize("2026-08-27"));

        Assert.Equal(table.Count, parsed.Count);
        Assert.Equal("2026-08-27", parsed.Generated);
        Assert.Equal(30, parsed.Covered("A1"));

        var nearest = parsed.Nearest(Distinct(7));
        Assert.Equal("A1-7", nearest[0].Entry.Key);
        Assert.Equal(0, nearest[0].Distance);

        // Ranked, nearest first, and nothing beyond the threshold.
        Assert.True(nearest.Select(n => n.Distance).SequenceEqual(nearest.Select(n => n.Distance).Order()));
        Assert.All(nearest, n => Assert.True(n.Distance <= ArtHashTable.MaxDistance));
    }

    [Fact]
    public void AMalformedLineCostsOneCardRatherThanTheWholeImport()
    {
        var parsed = ArtHashTable.Parse(
            "# generated 2026-08-27\nA1 1 " + Distinct(1) + "\nA1 nonsense zz\nA1 3 " + Distinct(3) + "\n");

        Assert.Equal(2, parsed.Count);
    }

    [Fact]
    public void CoverageNamesTheSetsThatCannotBeRecognisedYet()
    {
        // The case this exists for: card lists come live from the CDN, fingerprints ship with the
        // build, so a set can be fully browsable and completely unrecognisable at the same time.
        var sets = new SetCatalog(Snapshot.PublishedSets(), Ix.BySet.Keys);
        var coverage = ArtHashCoverage.Of(TableFor("A1", Ix.BySet["A1"].Count), Ix, sets);

        Assert.False(coverage.IsComplete);
        Assert.DoesNotContain("A1", coverage.Missing);
        Assert.Contains("A2", coverage.Missing);

        var warning = coverage.Warning(sets);
        Assert.NotNull(warning);
        Assert.Contains("2026-08-27", warning);
        Assert.Contains("cannot be recognised yet", warning);
    }

    [Fact]
    public void CoverageIsSilentWhenTheTableIsWhole()
    {
        var sets = new SetCatalog(Snapshot.PublishedSets(), Ix.BySet.Keys);
        var whole = new ArtHashTable(
            Ix.All.Select((c, i) => new ArtHashEntry(c.Set, c.Number, Distinct(i))), "2026-08-27");

        var coverage = ArtHashCoverage.Of(whole, Ix, sets);

        Assert.True(coverage.IsComplete);
        Assert.Null(coverage.Warning(sets));
    }
}
