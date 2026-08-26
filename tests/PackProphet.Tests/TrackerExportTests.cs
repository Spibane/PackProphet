namespace PackProphet.Tests;

using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Import;

/// <summary>
/// Writing a collection out as CSV.
///
/// The property that matters is that this and <see cref="TrackerImport"/> are inverses. A round
/// trip that loses a card is the whole feature failing quietly, in the direction where nobody
/// finds out until they are looking at the other tracker.
///
/// The rest is about the file being readable by something this project has never seen, which is
/// the point of not writing it for any particular site.
/// </summary>
public class TrackerExportTests
{
    private const string BulbasaurKey = "cPK_10_000010_00_FUSHIGIDANE_C.webp";

    private static Collection Sample() => new(new Dictionary<string, int>
    {
        [BulbasaurKey] = 3,
        ["cPK_10_000020_00_FUSHIGISOU_U.webp"] = 1,
        ["cPK_10_000030_00_FUSHIGIBANA_R.webp"] = 2,
    });

    private static string[] Lines(string csv) =>
        csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void A_collection_survives_a_round_trip()
    {
        var index = Snapshot.Index();
        var original = Sample();

        var back = TrackerImport.FromCsv(TrackerExport.ToCsv(original, index), index);

        Assert.Empty(back.Problems);
        Assert.Equal(
            original.Raw.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToArray(),
            back.Counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The same for a whole collection rather than three cards. A round trip that works on a
    /// handful and drops something among the 215 re-listed entries would pass the case above.
    /// </summary>
    [Fact]
    public void A_complete_collection_survives_a_round_trip()
    {
        var index = Snapshot.Index();
        var everything = new Collection(index.ByOwnershipKey.Keys.ToDictionary(k => k, _ => 1));

        var back = TrackerImport.FromCsv(TrackerExport.ToCsv(everything, index), index);

        Assert.Empty(back.Problems);
        Assert.Equal(index.DistinctOwnableCards, back.DistinctOwned);
        Assert.Equal(3546, back.DistinctOwned);

        // Every re-listing goes out as its own row and folds back in on the way home.
        Assert.Equal(215, back.RowsCollapsed);
    }

    /// <summary>
    /// The identifier is written in both shapes, so a reader looking for either finds it. This is
    /// what "universal" amounts to in practice, and it is one column of redundancy.
    /// </summary>
    [Fact]
    public void A_card_is_named_both_as_one_id_and_as_a_set_with_a_number()
    {
        var csv = TrackerExport.ToCsv(Sample(), Snapshot.Index());

        Assert.Equal("set,number,card_id,name,rarity,quantity", Lines(csv)[0]);
        Assert.Equal("A1,1,A1-1,Bulbasaur,C,3", Lines(csv)[1]);
    }

    /// <summary>
    /// Either identifier alone is enough to read the file back, which is the thing the redundancy
    /// is for. Proved by deleting the other one and importing what is left.
    /// </summary>
    [Theory]
    [InlineData("set,number,name,rarity,quantity", 0, 1, 3, 4, 5)]
    [InlineData("card_id,name,rarity,quantity", 2, 3, 4, 5)]
    public void Either_identifier_on_its_own_reads_back(string header, params int[] keep)
    {
        var index = Snapshot.Index();
        var full = Lines(TrackerExport.ToCsv(Sample(), index));

        var trimmed = string.Join('\n',
            [header, .. full.Skip(1).Select(line =>
                string.Join(',', keep.Select(c => line.Split(',')[c])))]);

        var back = TrackerImport.FromCsv(trimmed, index);

        Assert.Empty(back.Problems);
        Assert.Equal(3, back.DistinctOwned);
        Assert.Equal(6, back.TotalCopies);
    }

    /// <summary>
    /// One card, several rows. A card re-listed in a later set is owned in that set too, so the
    /// count goes against every entry — anything else reports the Deluxe set as missing 214 cards
    /// the user has.
    /// </summary>
    [Fact]
    public void A_re_listed_card_is_written_once_per_set_that_lists_it()
    {
        var csv = TrackerExport.ToCsv(
            new Collection(new Dictionary<string, int> { [BulbasaurKey] = 3 }), Snapshot.Index());

        var owned = Lines(csv).Where(l => l.EndsWith(",3", StringComparison.Ordinal)).ToArray();

        Assert.Equal(["A1,1,A1-1,Bulbasaur,C,3", "A4b,1,A4b-1,Bulbasaur,C,3"], owned);
    }

    /// <summary>
    /// Unowned cards are written too. A reader cannot tell a row that says nothing from a row that
    /// is not there, and at least one tracker's import deletes what a file reports as zero.
    /// </summary>
    [Fact]
    public void Every_entry_is_written_including_the_ones_owned_none_of()
    {
        var lines = Lines(TrackerExport.ToCsv(Sample(), Snapshot.Index()));

        Assert.Equal(3761, lines.Length - 1);
        Assert.Contains("A1,5,A1-5,Caterpie,C,0", lines);
    }

    /// <summary>
    /// Promos keep this project's spelling rather than borrowing another site's. Both are in use
    /// and neither is standard, so the file uses the one that says what it is — and the importer
    /// takes either, which is what makes the choice safe rather than merely opinionated.
    /// </summary>
    [Fact]
    public void Promos_are_written_as_the_card_data_spells_them()
    {
        var csv = TrackerExport.ToCsv(Sample(), Snapshot.Index());

        Assert.Contains(Lines(csv), l => l.StartsWith("PROMO-A,1,PROMO-A-1,", StringComparison.Ordinal));
        Assert.DoesNotContain(Lines(csv), l => l.StartsWith("P-A,", StringComparison.Ordinal));
    }

    /// <summary>
    /// No byte order mark. Excel would prefer one for the names carrying ♀ and ♂, but a reader
    /// that does not expect it takes it as part of the first column name and then recognises no
    /// column at all — a whole-file failure traded against a rendering glitch in one program.
    /// </summary>
    [Fact]
    public void The_file_does_not_open_with_a_byte_order_mark()
    {
        var csv = TrackerExport.ToCsv(Sample(), Snapshot.Index());

        Assert.StartsWith("set,", csv, StringComparison.Ordinal);
        Assert.DoesNotContain('﻿', csv);
    }

    /// <summary>
    /// Names carrying a comma or a quote would shift every column after them, and the row would
    /// still parse — as a different card, with a different count. No card is named that way today,
    /// which is why this uses one that is not real.
    /// </summary>
    [Fact]
    public void A_name_that_would_break_a_row_is_quoted()
    {
        var index = new CardIndex(
            [new PocketCard { Set = "A1", Number = 1, Rarity = "C", Image = "x.webp",
                              Name = "Bulbasaur, the \"first\"", Packs = ["Mewtwo"] }],
            Snapshot.Rarities());

        var csv = TrackerExport.ToCsv(
            new Collection(new Dictionary<string, int> { ["x.webp"] = 2 }), index);

        Assert.Equal("A1,1,A1-1,\"Bulbasaur, the \"\"first\"\"\",C,2", Lines(csv)[1]);

        // And it comes back as one field rather than as two columns of nonsense.
        Assert.Equal(2, TrackerImport.FromCsv(csv, index).Counts["x.webp"]);
    }

    [Fact]
    public void An_empty_collection_writes_a_full_sheet_of_zeroes_rather_than_nothing()
    {
        var index = Snapshot.Index();
        var csv = TrackerExport.ToCsv(new Collection(), index);
        var back = TrackerImport.FromCsv(csv, index);

        Assert.Equal(3761, Lines(csv).Length - 1);
        Assert.Equal(3761, back.RowsMatched);
        Assert.Equal(0, back.DistinctOwned);
    }

    [Fact]
    public void The_file_name_says_what_it_is_and_when_it_was_taken() =>
        Assert.Equal("packprophet-collection-2026-08-26.csv",
            TrackerExport.FileName(new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.Zero)));
}
