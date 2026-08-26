namespace PackProphet.Tests;

using PackProphet.Import;

/// <summary>
/// The second export format, and the one that showed the first design was too narrow.
///
/// PTCGP Tracker (ptcgp-tracker.com) differs from tcgpocketcollectiontracker.com in two ways that
/// both had to be absorbed rather than special-cased:
///
///   - It writes an .xlsx workbook, not a CSV.
///   - It splits the card identifier across two columns — set_id "A1" beside card_id 1 — and calls
///     the number column <c>card_id</c>, which is the same name the other tracker gives a column
///     holding the whole "A1-1". A header name alone therefore cannot say what the column means.
///
/// The fixture is a real export, kept exactly as that site wrote it.
/// </summary>
public class PtcgpTrackerImportTests
{
    private static byte[] Fixture(string extension = "xlsx") =>
        File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "fixtures", $"ptcgp-tracker-genetic-apex.{extension}"));

    /// <summary>
    /// The real file, read end to end. The figures were measured from the workbook independently
    /// of this code, so they check the reader rather than restate it: 286 data rows — the whole of
    /// Genetic Apex — of which 253 are owned, totalling 1,529 copies.
    /// </summary>
    [Fact]
    public void A_real_export_reads_with_every_row_matched()
    {
        var preview = TrackerImport.From(Fixture(), Snapshot.Index());

        Assert.Empty(preview.Problems);
        Assert.Equal(286, preview.RowsRead);
        Assert.Equal(286, preview.RowsMatched);
        Assert.Equal(253, preview.DistinctOwned);
        Assert.Equal(1529, preview.TotalCopies);

        // A1 lists no card twice, so nothing should collapse here. The collapse path is exercised
        // by the A4b re-listings in TrackerImportTests.
        Assert.Equal(0, preview.RowsCollapsed);
    }

    /// <summary>
    /// One export is one set. A whole collection is several files, so the merge has to be additive
    /// across imports rather than each file replacing the last.
    /// </summary>
    [Fact]
    public void An_export_covers_one_set_only()
    {
        var preview = TrackerImport.From(Fixture(), Snapshot.Index());
        var index = Snapshot.Index();

        var sets = preview.Counts.Keys
            .Select(key => index.ByOwnershipKey[key][0].Set)
            .Distinct()
            .ToArray();

        Assert.Equal(["A1"], sets);
    }

    /// <summary>
    /// The format is chosen from the bytes. A workbook whose extension says otherwise still reads,
    /// which matters because the extension is the part of a file a user can change by accident.
    /// </summary>
    [Fact]
    public void The_kind_of_file_is_decided_by_its_contents_not_its_name()
    {
        var index = Snapshot.Index();

        var asWorkbook = TrackerImport.FromXlsx(Fixture(), index);
        var sniffed = TrackerImport.From(Fixture(), index);

        Assert.Equal(asWorkbook.TotalCopies, sniffed.TotalCopies);
        Assert.Equal(253, sniffed.DistinctOwned);

        // And a CSV handed to the same entry point is still read as a CSV.
        var csv = TrackerImport.From(
            System.Text.Encoding.UTF8.GetBytes("Id,NumberOwned\r\nA1-1,2\r\n"), index);
        Assert.Equal(1, csv.DistinctOwned);
    }

    /// <summary>
    /// The split identifier, in CSV form so the case is readable. set_id carries the set and
    /// card_id carries only the number.
    /// </summary>
    [Fact]
    public void A_set_column_beside_a_bare_number_names_a_card()
    {
        var preview = TrackerImport.FromCsv(
            "set_id,card_id,card_name,quantity\nA1,1,Bulbasaur,4\nA1a,1,Exeggcute,2\n",
            Snapshot.Index());

        Assert.Empty(preview.Problems);
        Assert.Equal(2, preview.DistinctOwned);
        Assert.Equal(6, preview.TotalCopies);
    }

    /// <summary>
    /// The collision that makes header names insufficient on their own.
    ///
    /// tcgpocketcollectiontracker's CSV carries a complete "A1-1" alongside an Expansion column of
    /// "A1". Composing those, as the split-identifier path does, would ask for "A1-A1-1" on every
    /// row — so the whole identifier is tried first and the set column is never reached.
    /// </summary>
    [Fact]
    public void A_file_carrying_both_a_whole_id_and_a_set_column_is_not_read_as_both()
    {
        var preview = TrackerImport.FromCsv(
            "Id,CardName,NumberOwned,Expansion\nA1-1,Bulbasaur,2,A1\nA1a-1,Exeggcute,1,A1a\n",
            Snapshot.Index());

        Assert.Empty(preview.Problems);
        Assert.Equal(2, preview.RowsMatched);
        Assert.Equal(3, preview.TotalCopies);
    }

    /// <summary>An unresolved row quotes the identifier as the file spelled it, across both columns.</summary>
    [Fact]
    public void An_unknown_card_from_a_split_identifier_is_reported_whole()
    {
        var preview = TrackerImport.FromCsv(
            "set_id,card_id,quantity\nZ9,7,1\n", Snapshot.Index());

        var problem = Assert.Single(preview.Problems);
        Assert.Equal("Z9-7", problem.Value);
    }

    [Fact]
    public void Something_that_is_not_a_workbook_at_all_imports_nothing_rather_than_throwing()
    {
        var index = Snapshot.Index();

        // A zip signature with nothing behind it: the sniff says workbook, the reader disagrees.
        var fakeZip = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0 };

        Assert.Equal(0, TrackerImport.From(fakeZip, index).RowsRead);
        Assert.Equal(0, TrackerImport.From([], index).RowsRead);
        Assert.Equal(0, TrackerImport.From(null, index).RowsRead);
        Assert.Equal(0, TrackerImport.FromXlsx([1, 2, 3], index).RowsRead);
    }

    /// <summary>
    /// The same tracker also offers a CSV, and the same collection exported both ways has to
    /// arrive as the same collection. Asserted card by card rather than on the totals, since two
    /// different sets of counts can share a total.
    ///
    /// Its CSV quotes every field, numbers included, so the count column arrives as "4" rather
    /// than 4 — which is only harmless because quotes are stripped before the count is parsed.
    /// </summary>
    [Fact]
    public void The_csv_and_the_xlsx_of_one_collection_import_identically()
    {
        var index = Snapshot.Index();

        var fromCsv = TrackerImport.From(Fixture("csv"), index);
        var fromXlsx = TrackerImport.From(Fixture(), index);

        Assert.Empty(fromCsv.Problems);
        Assert.Equal(fromXlsx.RowsRead, fromCsv.RowsRead);
        Assert.Equal(fromXlsx.RowsMatched, fromCsv.RowsMatched);
        Assert.Equal(
            fromXlsx.Counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToArray(),
            fromCsv.Counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// A count wrapped in quotes is still a count. Worth its own case because a file quoting only
    /// its text columns and one quoting everything are both common, and the difference is
    /// invisible in a spreadsheet.
    /// </summary>
    [Fact]
    public void A_quoted_count_is_read_as_a_number()
    {
        var preview = TrackerImport.FromCsv(
            "\"set_id\",\"card_id\",\"quantity\"\n\"A1\",\"1\",\"4\"\n", Snapshot.Index());

        Assert.Empty(preview.Problems);
        Assert.Equal(4, preview.TotalCopies);
    }
}
