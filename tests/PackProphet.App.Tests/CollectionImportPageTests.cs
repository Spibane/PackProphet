namespace PackProphet.App.Tests;

using Microsoft.AspNetCore.Components.Forms;
using PackProphet.Import;
using PackProphet.Pages;

/// <summary>
/// The page that reads another tracker's export.
///
/// The property worth defending here is not that a good file imports — the engine suite covers
/// that — but that a bad one cannot take a collection with it. Picking a file changes nothing;
/// only the button does, and its default destination is one that cannot lose anything.
///
/// The card ids below are real: Bulbasaur is A1-1, and A4b-1 is the Deluxe re-listing of the same
/// artwork.
/// </summary>
public class CollectionImportPageTests : AppHost
{
    private const string Header = "Id,CardName,NumberOwned";

    private const string BulbasaurKey = "cPK_10_000010_00_FUSHIGIDANE_C.webp";
    private const string IvysaurKey = "cPK_10_000020_00_FUSHIGISOU_U.webp";

    /// <summary>A collection already holding two Bulbasaur, to be defended or overwritten.</summary>
    protected override AppState Start()
    {
        var state = AppState.Fresh();
        var profile = state.Active with
        {
            Name = "Mine",
            Collection = new Dictionary<string, int> { [BulbasaurKey] = 2, [IvysaurKey] = 1 },
        };

        return state with { Profiles = [profile] };
    }

    private async Task<IRenderedComponent<CollectionImport>> PageAsync()
    {
        await ReadyAsync();
        return RenderComponent<CollectionImport>();
    }

    private static void Pick(IRenderedComponent<CollectionImport> page, string csv) =>
        page.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(csv, "export.csv"));

    private static void PickFile(IRenderedComponent<CollectionImport> page, byte[] bytes, string name) =>
        page.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromBinary(bytes, name));

    private static byte[] Workbook() =>
        File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "fixtures", "ptcgp-tracker-genetic-apex.xlsx"));

    private static void Choose(IRenderedComponent<CollectionImport> page, string id) =>
        page.Find($"#{id}").Change(true);

    private static string Text(IRenderedComponent<CollectionImport> page) =>
        page.Markup;

    /// <summary>
    /// The whole reason the preview exists. A file that parses is still a file that might be the
    /// wrong one, and until the user says where it goes, nothing has happened.
    /// </summary>
    [Fact]
    public async Task Picking_a_file_shows_the_figures_and_changes_nothing()
    {
        var page = await PageAsync();

        Pick(page, $"{Header}\nA1-3,Venusaur,4\nA1-4,Venusaur ex,1\n");

        Assert.Contains("What This File Says", Text(page));

        // Two cards in the file, and the collection is still the two it started with.
        Assert.Equal(2, Session.Owned.DistinctOwned);
        Assert.Equal(2, Session.Owned[BulbasaurKey]);
        Assert.Single(Session.Profiles);
    }

    /// <summary>
    /// The default destination. A new collection cannot destroy anything, which is why it is what
    /// the page opens on rather than one option among three.
    /// </summary>
    [Fact]
    public async Task The_default_destination_makes_a_new_collection_and_leaves_the_old_one_whole()
    {
        var page = await PageAsync();

        Pick(page, $"{Header}\nA1-3,Venusaur,4\n");
        page.Find("#dest-new");           // present, and selected without being clicked
        page.Find(".btn-primary").Click();

        Assert.Equal(2, Session.Profiles.Count);

        var original = Session.Profiles.First(p => p.Name == "Mine");
        Assert.Equal(2, original.Collection[BulbasaurKey]);
        Assert.Equal(1, original.Collection[IvysaurKey]);

        // The new one is what the page switched to, holding only what the file said.
        Assert.NotEqual("Mine", Session.Profile.Name);
        Assert.Equal(1, Session.Owned.DistinctOwned);
    }

    /// <summary>
    /// Switching collections clears the undo history, so the page must not promise an undo it
    /// cannot deliver. It offers the way back that does work instead.
    /// </summary>
    [Fact]
    public async Task A_new_collection_is_not_offered_an_undo_that_would_not_work()
    {
        var page = await PageAsync();

        Pick(page, $"{Header}\nA1-3,Venusaur,4\n");

        // Asserted while the choice is still on screen: after the import the hint is gone whatever
        // destination was taken, so a check made afterwards could not fail.
        //
        // The promise is worded without a keystroke, because this page is used on a phone as often
        // as at a desk and Ctrl+Z is not a thing you can press there. The control that keeps it is
        // the button in the confirmation.
        Assert.DoesNotContain("Ctrl", Text(page));
        Assert.DoesNotContain("You can undo this", Text(page));
        Choose(page, "dest-replace");
        Assert.Contains("You can undo this", Text(page));
        Choose(page, "dest-new");
        Assert.DoesNotContain("You can undo this", Text(page));

        page.Find(".btn-primary").Click();

        Assert.False(Session.CanUndo);
        Assert.Contains("Delete it in Settings", Text(page));
    }

    [Fact]
    public async Task Replacing_discards_what_was_there_and_can_be_undone()
    {
        var page = await PageAsync();

        Pick(page, $"{Header}\nA1-3,Venusaur,4\n");
        Choose(page, "dest-replace");
        page.Find(".btn-primary").Click();

        Assert.Single(Session.Profiles);
        Assert.Equal(1, Session.Owned.DistinctOwned);
        Assert.Equal(0, Session.Owned[BulbasaurKey]);

        // Unlike the new-collection path, this one really did throw work away, so the undo has to
        // be real rather than advertised.
        Assert.True(Session.CanUndo);
        Session.Undo();
        Assert.Equal(2, Session.Owned[BulbasaurKey]);
    }

    [Fact]
    public async Task Merging_keeps_the_larger_count_and_anything_the_file_never_mentioned()
    {
        var page = await PageAsync();

        // One card the file claims fewer of, one it claims more of, one it does not list.
        Pick(page, $"{Header}\nA1-1,Bulbasaur,1\nA1-3,Venusaur,4\n");
        Choose(page, "dest-merge");
        page.Find(".btn-primary").Click();

        Assert.Equal(2, Session.Owned[BulbasaurKey]);   // the file said 1; the collection said 2
        Assert.Equal(1, Session.Owned[IvysaurKey]);     // absent from the file, still here
        Assert.Equal(3, Session.Owned.DistinctOwned);
        Assert.Single(Session.Profiles);
    }

    /// <summary>
    /// The reprint case, through the page rather than the parser: two rows naming one artwork must
    /// not import as two cards, and must not sum.
    /// </summary>
    [Fact]
    public async Task Two_rows_for_one_artwork_import_as_one_card()
    {
        var page = await PageAsync();

        Pick(page, $"{Header}\nA1-1,Bulbasaur,2\nA4b-1,Bulbasaur,3\n");
        Choose(page, "dest-replace");
        page.Find(".btn-primary").Click();

        Assert.Equal(1, Session.Owned.DistinctOwned);
        Assert.Equal(3, Session.Owned[BulbasaurKey]);
    }

    /// <summary>
    /// A row that did not match is named with its line, because a count on its own is not
    /// something anyone can act on.
    /// </summary>
    [Fact]
    public async Task An_unrecognised_row_is_named_with_its_line_and_the_rest_still_offer_to_import()
    {
        var page = await PageAsync();

        Pick(page, $"{Header}\nA1-3,Venusaur,4\nZ9-999,Nothing,1\n");

        var markup = Text(page);
        Assert.Contains("Rows Not Recognised", markup);
        Assert.Contains("Line 3", markup);
        Assert.Contains("Z9-999", markup);

        // The good row is still importable — one bad line does not refuse the file.
        page.Find(".btn-primary").Click();
        Assert.Equal(1, Session.Owned.DistinctOwned);
    }

    [Fact]
    public async Task A_file_that_is_not_a_collection_export_says_so_and_offers_no_import()
    {
        var page = await PageAsync();

        Pick(page, "name,hp\nPikachu,60\n");

        Assert.Contains("no rows this could read as a collection", Text(page));
        Assert.DoesNotContain("What This File Says", Text(page));
        Assert.Equal(2, Session.Owned.DistinctOwned);
    }

    /// <summary>
    /// A file whose rows all fail is a different failure from one whose header failed: there are
    /// figures to show, and every one of them is zero. It must not offer to import them.
    /// </summary>
    [Fact]
    public async Task A_file_matching_no_cards_at_all_refuses_to_import_rather_than_importing_nothing()
    {
        var page = await PageAsync();

        Pick(page, $"{Header}\nZ9-1,Nothing,1\nZ9-2,Nothing,1\n");

        Assert.Contains("nothing to import", Text(page));
        Assert.Empty(page.FindAll("#dest-replace"));
        Assert.Empty(page.FindAll(".btn-primary"));
    }

    /// <summary>
    /// A real PTCGP Tracker workbook, through the picker. The page reads bytes and lets the format
    /// be decided by the contents, so a spreadsheet needs no separate control.
    /// </summary>
    [Fact]
    public async Task An_xlsx_export_imports_through_the_same_picker()
    {
        var page = await PageAsync();

        PickFile(page, Workbook(), "user_genetic_apex.xlsx");

        Assert.Contains("What This File Says", Text(page));
        Assert.DoesNotContain("Rows Not Recognised", Text(page));

        page.Find(".btn-primary").Click();

        Assert.Equal(2, Session.Profiles.Count);
        Assert.Equal(253, Session.Owned.DistinctOwned);
    }

    /// <summary>
    /// That tracker exports one set per file, so a whole collection arrives as several imports.
    /// The second file has to add to the first rather than replace it, which is what merge is for
    /// and the reason the page says so on the picker.
    /// </summary>
    [Fact]
    public async Task A_second_file_merges_on_top_of_the_first_rather_than_replacing_it()
    {
        var page = await PageAsync();

        PickFile(page, Workbook(), "genetic_apex.xlsx");
        Choose(page, "dest-replace");
        page.Find(".btn-primary").Click();

        var afterFirst = Session.Owned.DistinctOwned;
        Assert.Equal(253, afterFirst);

        // A second set, as a second file.
        Pick(page, "set_id,card_id,quantity\nA1a,1,3\nA1a,2,1\n");
        Choose(page, "dest-merge");
        page.Find(".btn-primary").Click();

        Assert.Equal(afterFirst + 2, Session.Owned.DistinctOwned);
    }

    /// <summary>
    /// The second file of a set-at-a-time export defaults to merging, not to a second collection.
    ///
    /// Left on the safe default, importing a twenty-set collection would produce twenty
    /// collections holding one set each — every one of them safe, and none of them the thing the
    /// user was assembling.
    /// </summary>
    [Fact]
    public async Task After_one_import_the_next_file_defaults_to_merging_into_it()
    {
        var page = await PageAsync();

        Pick(page, "set_id,card_id,quantity\nA1,1,2\n");
        page.Find(".btn-primary").Click();
        Assert.Equal(2, Session.Profiles.Count);

        Pick(page, "set_id,card_id,quantity\nA1a,1,3\n");
        Assert.Equal("Merge In", page.Find(".btn-primary").TextContent.Trim());

        page.Find(".btn-primary").Click();

        // Still two collections, and the second file landed in the one the first made.
        Assert.Equal(2, Session.Profiles.Count);
        Assert.Equal(2, Session.Owned.DistinctOwned);
    }

    /// <summary>
    /// The figure that will make someone recount: the file has more rows than they have cards,
    /// because a card printed in several sets is written once per set. Said on the page rather
    /// than left to be discovered.
    /// </summary>
    [Fact]
    public async Task The_export_says_why_it_has_more_rows_than_the_collection_has_cards()
    {
        var page = await PageAsync();
        var markup = Text(page);

        Assert.Contains("3,879 rows", markup);
        Assert.Contains("3,664 cards", markup);
    }

    /// <summary>
    /// Import and export are inverses, driven through the page rather than the engine: import a
    /// file, export the result, and the two files describe the same collection.
    /// </summary>
    [Fact]
    public async Task A_collection_imported_through_the_page_exports_back_to_the_same_counts()
    {
        var page = await PageAsync();

        PickFile(page, Workbook(), "genetic_apex.xlsx");
        Choose(page, "dest-replace");
        page.Find(".btn-primary").Click();

        var exported = TrackerExport.ToCsv(Session.Owned, Session.Index);
        var back = TrackerImport.FromCsv(exported, Session.Index);

        Assert.Equal(253, back.DistinctOwned);
        Assert.Equal(1529, back.TotalCopies);
    }

    /// <summary>
    /// One button, one file. There is no format to choose because the file is written for no site
    /// in particular.
    /// </summary>
    [Fact]
    public async Task Exporting_offers_one_file_rather_than_a_choice_of_sites()
    {
        var page = await PageAsync();

        Assert.Empty(page.FindAll("#export-format"));

        page.Find("button.btn-outline-primary").Click();

        Assert.Contains("packprophet-collection-", Text(page));
    }
}
