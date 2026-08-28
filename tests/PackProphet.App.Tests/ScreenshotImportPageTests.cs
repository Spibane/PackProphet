namespace PackProphet.App.Tests;

using Microsoft.AspNetCore.Components.Forms;
using PackProphet.Pages;
using PackProphet.Vision;

/// <summary>
/// The page that reads cards off a screenshot.
///
/// The recognition rules are covered exhaustively in the engine suite, and the pixel work needs a
/// browser — so what is tested here is the page's own promises: that it is honest about coverage
/// rather than alarming on every visit, that picking nothing changes nothing, that the destructive
/// half of an import is not armed by default, and that applying one says what it did.
///
/// The scan is stubbed at the module boundary rather than faked further in, so the reading the page
/// works from is produced by the same <see cref="ScreenshotReader"/> and the same committed
/// fingerprint table that production uses. The measurements handed to it are real output from
/// cardshot.js — see ScreenshotEndToEndTests, which records where they came from.
/// </summary>
public class ScreenshotImportPageTests : AppHost
{
    private async Task<IRenderedComponent<ScreenshotImport>> PageAsync()
    {
        await ReadyAsync();
        return RenderComponent<ScreenshotImport>();
    }

    [Fact]
    public async Task Offers_the_two_card_lists_and_nothing_else()
    {
        // This page is the collection's import, so it offers the two My Cards layouts and lets the
        // app tell them apart. A pack's reveal belongs on the log screen and a Wonder Pick line-up
        // on the Wonder Pick screen, because on those pages the same five cards mean something
        // different — and a choice offered in the wrong place is a wrong answer waiting to happen.
        var page = await PageAsync();

        Assert.Equal(3, page.FindAll("input[type=radio][name=cards]").Count);
        Assert.Contains("Five across, with blanks", page.Markup);
        Assert.Contains("Three across, with copy counts", page.Markup);
        Assert.DoesNotContain("Wonder Pick", page.Markup);
        Assert.DoesNotContain("from a pack", page.Markup);
    }

    [Fact]
    public async Task Says_nothing_about_coverage_when_the_table_covers_the_data_loaded()
    {
        // The warning names sets that cannot be recognised yet, and it has to stay quiet in the
        // normal case. Shown on every visit it would read as "this feature is broken" — and the
        // tests render against the same snapshot and the same committed table that ship together,
        // which is exactly the case where there is nothing to warn about.
        var page = await PageAsync();

        Assert.DoesNotContain("cannot be recognised yet", page.Markup);
    }

    [Fact]
    public async Task Picking_nothing_offers_nothing_to_apply()
    {
        var page = await PageAsync();

        Assert.Empty(page.FindAll("button:contains('apply')"));
        Assert.Empty(page.FindAll("input[type=checkbox]"));
        Assert.DoesNotContain("What this picture says", page.Markup);
    }

    [Fact]
    public async Task Explains_where_the_picture_goes_before_asking_for_one()
    {
        // The same promise every other import in the app leads with, and the first thing someone
        // handing over a picture of their screen wants to know.
        var page = await PageAsync();

        Assert.Contains("Nothing is uploaded", page.Markup);
        Assert.Contains("in this browser", page.Markup);
    }

    /// <summary>
    /// The committed fingerprint table, read as the app reads it. The scan below is built from its
    /// own entries, so this exercises the page against a perfect reading and stays correct when the
    /// table is regenerated. How well a real screenshot fingerprints is measured on a real
    /// screenshot in ScreenshotEndToEndTests.
    /// </summary>
    private static ArtHashTable Table { get; } = ArtHashTable.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "card-hashes.txt")));

    /// <summary>
    /// A page of the five-across list: sixteen slots of A1 in order, with slots 5, 9 and 12 left
    /// blank as the game draws a card you do not own.
    /// </summary>
    private static ShotScan MeasuredScan()
    {
        var blank = new[] { 5, 9, 12 };

        return new ShotScan
        {
            Ok = true, Width = 848, Height = 1402,
            Lattice = new ShotLattice
            {
                Rows = 4, Cols = 4, CellWidth = 200, CellHeight = 273,
                Confidence = 0.49, RelativeCellWidth = 200.0 / 848,
            },
            Cells = Enumerable.Range(1, 16).Select(n => new ShotCell
            {
                Row = (n - 1) / 4,
                Col = (n - 1) % 4,
                Hash = blank.Contains(n)
                    ? ""
                    : Table.Entries.First(e => e.Key == $"A1-{n}").Hash.ToString(),
                Detail = blank.Contains(n) ? 0.001 : 0.08,
                Saturation = 0.45,
                Luma = 0.5,
            }).ToList(),
        };
    }

    /// <summary>Renders the page with the pixel side stubbed, and hands it a picture.</summary>
    private async Task<IRenderedComponent<ScreenshotImport>> ReadingAsync()
    {
        JSInterop.SetupModule("./js/cardshot.js")
                 .Setup<ShotScan>("scan", _ => true)
                 .SetResult(MeasuredScan());

        var page = await PageAsync();
        page.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "cards.png"));

        return page;
    }

    [Fact]
    public async Task Reads_the_cards_and_names_the_blanks()
    {
        var page = await ReadingAsync();

        Assert.Contains("Bulbasaur", page.Markup);
        Assert.Contains("Caterpie", page.Markup);                       // slot 5, drawn blank
        Assert.Contains("blank slot, named by the cards around it", page.Markup);

        // Nothing hedged: every recognised card landed inside the comfortable distance.
        Assert.DoesNotContain("less certainly", page.Markup);
    }

    [Fact]
    public async Task Arms_the_additions_and_not_the_removals()
    {
        var page = await ReadingAsync();

        Assert.True(page.Find("#apply-found").HasAttribute("checked"));
        Assert.False(page.Find("#apply-missing").HasAttribute("checked"));
    }

    [Fact]
    public async Task Applying_records_the_cards_and_says_so()
    {
        // Regression: applying set the confirmation and then cleared the reading with the same call
        // the "pick a different picture" button uses — which also clears the confirmation. The
        // collection changed and the page said nothing, which reads as a button that does not work.
        var page = await ReadingAsync();
        page.Find("button.btn-primary").Click();

        Assert.Contains("Recorded 13 cards as owned", page.Markup);
        Assert.Equal(13, Session.Owned.DistinctOwned);
        Assert.True(Session.CanUndo);
    }

    [Fact]
    public async Task The_confirmation_offers_a_button_to_undo_it_rather_than_a_keystroke()
    {
        // This is the page you use on a phone, beside the game. It used to say "undoable with
        // Ctrl+Z", which on the device it is designed for is not an instruction at all.
        var page = await ReadingAsync();
        page.Find("button.btn-primary").Click();

        Assert.DoesNotContain("Ctrl", page.Markup);

        page.Find("button:contains('undo that')").Click();

        Assert.Equal(0, Session.Owned.DistinctOwned);
        Assert.DoesNotContain("Recorded 13 cards", page.Markup);
    }

    [Fact]
    public async Task Applying_does_not_mark_anything_missing_unless_asked()
    {
        var page = await ReadingAsync();

        // Say the three blanks are owned, so a removal would be visible as a loss.
        foreach (var key in new[] { "A1-5", "A1-9", "A1-12" })
            Session.SetCount(Session.Index.ByKey[key], 1);

        page.Find("button.btn-primary").Click();

        Assert.Equal(3, new[] { "A1-5", "A1-9", "A1-12" }
            .Count(k => Session.CountOf(Session.Index.ByKey[k]) > 0));
    }

    [Fact]
    public async Task Marking_missing_removes_only_the_blanks_when_asked()
    {
        var page = await ReadingAsync();

        foreach (var key in new[] { "A1-5", "A1-9", "A1-12" })
            Session.SetCount(Session.Index.ByKey[key], 1);

        page.Find("#apply-missing").Change(true);
        page.Find("button.btn-primary").Click();

        Assert.All(new[] { "A1-5", "A1-9", "A1-12" },
                   k => Assert.Equal(0, Session.CountOf(Session.Index.ByKey[k])));
        Assert.Contains("Marked 3 cards as not owned", page.Markup);
    }

    [Fact]
    public async Task An_existing_count_is_never_lowered_by_a_card_list()
    {
        // A card list says a card is held, not how many are. Someone with four Bulbasaur must not
        // come out of an import with one.
        // After the page is ready — Session.Index is not available until the data has loaded.
        var page = await ReadingAsync();
        var bulbasaur = Session.Index.ByKey["A1-1"];
        Session.SetCount(bulbasaur, 4);

        page.Find("button.btn-primary").Click();

        Assert.Equal(4, Session.CountOf(bulbasaur));
    }

    [Fact]
    public async Task A_picture_chosen_before_the_fingerprints_land_still_reads()
    {
        // Regression: the file input is on screen before the fingerprint table has been fetched, so
        // a picture chosen straight away was read against an empty table and came back as "the card
        // fingerprints could not be loaded" — which looks like a broken build rather than a race.
        var page = await ReadingAsync();

        Assert.DoesNotContain("fingerprints could not be loaded", page.Markup);
        Assert.Contains("Bulbasaur", page.Markup);
    }

    [Fact]
    public async Task Leaves_the_collection_alone()
    {
        // Rendering the page, and choosing a screen on it, must not be a collection edit.
        var before = Session.Owned.DistinctOwned;
        var page = await PageAsync();

        // By id rather than by index: the pinned AngleSharp does not expose the indexer bUnit's
        // element collection reaches for, and the id says which choice this is anyway.
        page.Find("#cards-OwnershipGrid").Change(true);

        Assert.Equal(before, Session.Owned.DistinctOwned);
        Assert.False(Session.CanUndo);
    }
}
