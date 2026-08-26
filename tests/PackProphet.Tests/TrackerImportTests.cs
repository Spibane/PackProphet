namespace PackProphet.Tests;

using System.Text;
using PackProphet.Domain;
using PackProphet.Import;

/// <summary>
/// Importing a collection exported by another tracker.
///
/// The format under test is the CSV written by tcgpocketcollectiontracker.com, which is the only
/// one of the surveyed trackers whose export format could be read rather than guessed at — it is
/// GPL-3.0 and its writer emits, in this order:
///
///     Id, CardName, InternalId, NumberOwned, Expansion, Pack, Rarity, Collected
///
/// Its card_id is "A1-1": the same spelling as this project's PocketCard.Key, with promos the one
/// exception ("P-A-1" against "PROMO-A-1"). Aliasing those two set codes makes all 3,761 of its
/// entries resolve against the vendored snapshot, checked entry by entry against its live
/// assets/cards.json — the two datasets agree exactly, with no card in one and not the other.
/// </summary>
public class TrackerImportTests
{
    /// <summary>Their header, verbatim from ExportWriter.tsx.</summary>
    private const string Header = "Id,CardName,InternalId,NumberOwned,Expansion,Pack,Rarity,Collected";

    private static string Row(string id, string name, int owned) =>
        $"{id},{name},65600,{owned},A1,Mewtwo,◊,{(owned > 0).ToString().ToLowerInvariant()}";

    private static string Csv(params string[] rows) =>
        Header + "\r\n" + string.Join("\r\n", rows) + "\r\n";

    /// <summary>
    /// A whole export, synthesised at one copy per entry. The figures are the point: 3,761 entries
    /// in, every one matched, and 3,546 ownable cards out. Nothing in an export is skipped and
    /// nothing is counted twice.
    /// </summary>
    [Fact]
    public void A_complete_export_resolves_every_entry_and_collapses_reprints()
    {
        var index = Snapshot.Index();

        var csv = new StringBuilder(Header).Append("\r\n");
        foreach (var card in Snapshot.Cards())
            csv.Append(Row(TheirId(card), card.Name, 1)).Append("\r\n");

        var preview = TrackerImport.FromCsv(csv.ToString(), index);

        Assert.Empty(preview.Problems);
        Assert.Equal(3761, preview.RowsRead);
        Assert.Equal(3761, preview.RowsMatched);
        Assert.Equal(0, preview.RowsUnmatched);

        // The 215 rows that collapse are re-listings — the same artwork printed in a later set.
        Assert.Equal(215, preview.RowsCollapsed);
        Assert.Equal(3546, preview.DistinctOwned);
        Assert.Equal(index.DistinctOwnableCards, preview.DistinctOwned);
    }

    /// <summary>Their spelling of a card id: ours, with the promo sets renamed.</summary>
    private static string TheirId(PocketCard card) =>
        card.Set switch
        {
            "PROMO-A" => $"P-A-{card.Number}",
            "PROMO-B" => $"P-B-{card.Number}",
            _ => $"{card.Set}-{card.Number}",
        };

    [Fact]
    public void Promo_set_codes_are_aliased_in_both_spellings()
    {
        var index = Snapshot.Index();

        Assert.Equal("PROMO-A-1", TrackerImport.Resolve("P-A-1", index)?.Key);
        Assert.Equal("PROMO-B-1", TrackerImport.Resolve("P-B-1", index)?.Key);
        Assert.Equal("PROMO-A-1", TrackerImport.Resolve("PROMO-A-1", index)?.Key);
    }

    /// <summary>
    /// Zero padding and case are not what this tracker writes, but they are what a spreadsheet
    /// round-trip and two of the other trackers produce, and getting them wrong fails silently on
    /// every row rather than loudly on one.
    /// </summary>
    [Theory]
    [InlineData("A1-1")]
    [InlineData("a1-1")]
    [InlineData("A1-001")]
    [InlineData("  A1-1  ")]
    public void An_identifier_reads_the_same_however_it_is_padded_or_cased(string id) =>
        Assert.Equal("A1-1", TrackerImport.Resolve(id, Snapshot.Index())?.Key);

    /// <summary>
    /// Eight of the twenty-two sets carry a lowercase suffix — A1a, A2b, B3a. Upper-casing a set
    /// code, which is the obvious way to make matching case-insensitive, turns every one of them
    /// into a code that exists nowhere, and an entire set imports as unknown rows while every
    /// other set succeeds.
    /// </summary>
    [Theory]
    [InlineData("A1a-1", "A1a-1")]
    [InlineData("a1a-1", "A1a-1")]
    [InlineData("A1A-1", "A1a-1")]
    [InlineData("A4b-1", "A4b-1")]
    [InlineData("b3a-1", "B3a-1")]
    public void A_set_code_with_a_lowercase_suffix_resolves_however_it_is_cased(string id, string key) =>
        Assert.Equal(key, TrackerImport.Resolve(id, Snapshot.Index())?.Key);

    /// <summary>Every set in the database resolves from the spelling an export would carry.</summary>
    [Fact]
    public void Every_set_in_the_database_is_reachable_by_its_own_code()
    {
        var index = Snapshot.Index();

        foreach (var set in index.BySet.Keys)
        {
            var first = index.BySet[set][0];
            Assert.Equal(first.Key, TrackerImport.Resolve(first.Key, index)?.Key);
        }
    }

    /// <summary>
    /// Two rows, one card. A4b re-lists 214 earlier cards, so this is the ordinary case rather
    /// than a corrupt file — and the counts are two opinions about one pile, so the larger wins.
    /// Summing would double a Deluxe-set owner's entire collection.
    /// </summary>
    [Fact]
    public void A_reprint_keeps_the_larger_count_rather_than_the_sum()
    {
        var preview = TrackerImport.FromCsv(
            Csv(Row("A1-1", "Bulbasaur", 2), Row("A4b-1", "Bulbasaur", 3)), Snapshot.Index());

        Assert.Equal(1, preview.DistinctOwned);
        Assert.Equal(3, preview.TotalCopies);
        Assert.Equal(1, preview.RowsCollapsed);
        Assert.Empty(preview.Problems);
    }

    [Fact]
    public void Cards_owned_zero_times_take_no_space_but_still_count_as_matched()
    {
        var preview = TrackerImport.FromCsv(
            Csv(Row("A1-1", "Bulbasaur", 0), Row("A1-2", "Ivysaur", 1)), Snapshot.Index());

        Assert.Equal(2, preview.RowsMatched);
        Assert.Equal(1, preview.DistinctOwned);
        Assert.Empty(preview.Problems);
    }

    /// <summary>
    /// An unknown row is reported with its line, not dropped. A file from a tracker that has
    /// added a set before this project's snapshot has is the expected cause, and the user needs
    /// to be told which rows did not arrive.
    /// </summary>
    [Fact]
    public void An_unknown_card_is_reported_against_its_line_and_the_rest_still_import()
    {
        var preview = TrackerImport.FromCsv(
            Csv(Row("A1-1", "Bulbasaur", 1), Row("Z9-999", "Nothing", 1), Row("A1-2", "Ivysaur", 1)),
            Snapshot.Index());

        Assert.Equal(3, preview.RowsRead);
        Assert.Equal(2, preview.RowsMatched);
        Assert.Equal(2, preview.DistinctOwned);

        var problem = Assert.Single(preview.Problems);
        Assert.Equal(ImportProblemKind.UnknownCard, problem.Kind);
        Assert.Equal("Z9-999", problem.Value);

        // Line 1 is the header, so the offending row is line 3 of the file the user is looking at.
        Assert.Equal(3, problem.Line);
    }

    [Fact]
    public void A_count_that_is_not_a_number_is_reported_rather_than_guessed_at()
    {
        var preview = TrackerImport.FromCsv(
            Csv(Row("A1-1", "Bulbasaur", 1).Replace(",1,A1", ",lots,A1")), Snapshot.Index());

        var problem = Assert.Single(preview.Problems);
        Assert.Equal(ImportProblemKind.BadCount, problem.Kind);
        Assert.Empty(preview.Counts);
    }

    /// <summary>
    /// A tracker that records only whether a card is held, with no quantity, imports as one copy
    /// each. One copy is exactly what "collected" asserts, and refusing the file would be refusing
    /// the whole collection over a column it never had.
    /// </summary>
    [Fact]
    public void A_file_with_no_count_column_falls_back_to_the_collected_flag()
    {
        var preview = TrackerImport.FromCsv(
            "card_id,collected\r\nA1-1,true\r\nA1-2,false\r\nA1-3,yes\r\n", Snapshot.Index());

        Assert.Equal(3, preview.RowsMatched);
        Assert.Equal(2, preview.DistinctOwned);
        Assert.Equal(2, preview.TotalCopies);
    }

    [Fact]
    public void A_file_that_is_not_a_collection_export_imports_nothing_rather_than_throwing()
    {
        var index = Snapshot.Index();

        Assert.Equal(0, TrackerImport.FromCsv(null, index).RowsRead);
        Assert.Equal(0, TrackerImport.FromCsv("", index).RowsRead);
        Assert.Equal(0, TrackerImport.FromCsv("just some text", index).RowsRead);
        Assert.Equal(0, TrackerImport.FromCsv("name,hp\r\nPikachu,60\r\n", index).RowsRead);
    }

    /// <summary>
    /// Excel writes a BOM, and a file that has been through a spreadsheet comes back with one. It
    /// lands on the first header, which is the id column, so leaving it in place would fail
    /// detection on exactly the files most likely to have been edited by hand.
    /// </summary>
    [Fact]
    public void A_byte_order_mark_does_not_hide_the_first_column()
    {
        var preview = TrackerImport.FromCsv("﻿" + Csv(Row("A1-1", "Bulbasaur", 1)), Snapshot.Index());

        Assert.Equal(1, preview.DistinctOwned);
        Assert.Empty(preview.Problems);
    }

    [Fact]
    public void Quoted_fields_carry_commas_newlines_and_quotes_without_shifting_the_columns()
    {
        var preview = TrackerImport.FromCsv(
            "Id,CardName,NumberOwned\r\n" +
            "A1-1,\"Bulbasaur, the \"\"first\"\"\",2\r\n" +
            "A1-2,\"a name\nwith a newline\",1\r\n",
            Snapshot.Index());

        Assert.Equal(2, preview.RowsMatched);
        Assert.Equal(3, preview.TotalCopies);
        Assert.Empty(preview.Problems);
    }

    [Fact]
    public void Line_endings_and_a_missing_final_newline_are_both_tolerated()
    {
        var index = Snapshot.Index();

        Assert.Equal(2, TrackerImport.FromCsv(
            "Id,NumberOwned\nA1-1,1\nA1-2,1", index).RowsMatched);

        Assert.Equal(2, TrackerImport.FromCsv(
            "Id,NumberOwned\r\nA1-1,1\r\n\r\nA1-2,1\r\n", index).RowsMatched);
    }

    /// <summary>
    /// The import replaces rather than merges by default: an export states everything its owner
    /// has, so a card missing from it is a card they do not have.
    /// </summary>
    [Fact]
    public void Importing_as_a_collection_states_the_whole_of_it()
    {
        var preview = TrackerImport.FromCsv(Csv(Row("A1-1", "Bulbasaur", 2)), Snapshot.Index());
        var collection = preview.AsCollection();

        Assert.Equal(1, collection.DistinctOwned);
        Assert.Equal(2, collection["cPK_10_000010_00_FUSHIGIDANE_C.webp"]);
    }

    [Fact]
    public void Merging_keeps_whichever_side_claims_more_copies()
    {
        var existing = new Collection(new Dictionary<string, int>
        {
            ["cPK_10_000010_00_FUSHIGIDANE_C.webp"] = 5,
            ["cPK_10_000020_00_FUSHIGISOU_U.webp"] = 1,
        });

        var preview = TrackerImport.FromCsv(
            Csv(Row("A1-1", "Bulbasaur", 2), Row("A1-3", "Venusaur", 4)), Snapshot.Index());

        var merged = preview.MergedInto(existing);

        Assert.Equal(5, merged["cPK_10_000010_00_FUSHIGIDANE_C.webp"]);  // the file claimed fewer
        Assert.Equal(1, merged["cPK_10_000020_00_FUSHIGISOU_U.webp"]);   // absent from the file
        Assert.Equal(4, merged["cPK_10_000030_00_FUSHIGIBANA_R.webp"]);  // new
        Assert.Equal(3, merged.DistinctOwned);
    }
}
