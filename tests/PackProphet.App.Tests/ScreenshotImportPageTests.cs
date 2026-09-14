namespace PackProphet.App.Tests;

using Microsoft.AspNetCore.Components.Forms;
using Bunit.JSInterop.InvocationHandlers;
using PackProphet.Components;
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
        Assert.DoesNotContain("What This Picture Says", page.Markup);
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
    private static ShotScan MeasuredScan(params int[] blanks)
    {
        var blank = blanks.Length > 0 ? blanks : [5, 9, 12];

        // 1060 wide, not 848. The width only matters through RelativeCellWidth, and at 848 a slot
        // came to 0.236 of the screen -- a number no five-across list produces. The real thing
        // measures 0.168 and the three-across list 0.29, and the fixture was sitting between them,
        // on the ownership side only because the threshold used to be 0.28. It has to be a size a
        // phone really produces, or it is testing the rule against a picture that cannot exist.
        return new ShotScan
        {
            Ok = true, Width = 1060, Height = 1402,
            Lattice = new ShotLattice
            {
                Rows = 4, Cols = 4, CellWidth = 200, CellHeight = 273,
                Confidence = 0.49, RelativeCellWidth = 200.0 / 1060,
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

    /// <summary>
    /// Two screenfuls of the same list, stubbed to their own scans. The scan is chosen by matching
    /// the data URL the component builds, so the file bytes differ &mdash; the only handle a test
    /// has on "this call is for that file".
    /// </summary>
    private async Task<IRenderedComponent<ScreenshotImport>> ReadingTwoAsync(
        ShotScan first, ShotScan second)
    {
        var module = JSInterop.SetupModule("./js/cardshot.js");
        module.Setup<ShotScan>("scan", i => Sent(i, [1, 2, 3])).SetResult(first);
        module.Setup<ShotScan>("scan", i => Sent(i, [4, 5, 6])).SetResult(second);

        var page = await PageAsync();
        page.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([1, 2, 3], "before-scrolling.png"),
            InputFileContent.CreateFromBinary([4, 5, 6], "after-scrolling.png"));

        return page;
    }

    private static bool Sent(JSRuntimeInvocation invocation, byte[] bytes) =>
        invocation.Arguments[0] is string url && url.EndsWith(Convert.ToBase64String(bytes));

    [Fact]
    public async Task A_picture_that_names_nothing_offers_nothing_to_apply()
    {
        // Regression: the actions moved from per-picture to once-for-all-pictures when the import
        // learned to take several, and the new block rendered for any picture that merely read
        // cleanly. A shot of a set too new for this build reads cleanly and names nothing, so the
        // page showed a live apply button that did nothing when pressed.
        var scan = MeasuredScan();
        foreach (var cell in scan.Cells) cell.Hash = new string('0', 32);

        JSInterop.SetupModule("./js/cardshot.js").Setup<ShotScan>("scan", _ => true).SetResult(scan);
        var page = await PageAsync();
        page.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "nothing.png"));

        Assert.Empty(page.FindAll("button.btn-primary"));

        // Nothing to apply is not nothing to do. Thirteen of the sixteen slots hold something and
        // none of them could be named, so every one of them is offered with a box to name it —
        // which is the whole of what this page can still usefully do with a picture like this.
        Assert.Contains("13 slots could not be named", page.Markup);
        Assert.Equal(13, page.FindComponents<SlotFix>().Count);
    }

    [Fact]
    public async Task The_slots_picture_enlarges_on_a_press_as_well_as_on_a_hover()
    {
        // The enlargement used to be a hover and a focus, and the button existed only to be
        // focusable. A pointer got the picture; a keyboard got a control that announced it would
        // show the card larger and then did nothing when pressed, because the focus that would
        // have enlarged it had already happened on the way in.
        //
        // So the press toggles it outright. What is worth holding here is the pair that says so to
        // somebody not looking at the screen: aria-pressed, and a label that names the next press
        // rather than the last one.
        var scan = MeasuredScan();
        scan.Cells[2].Hash = new string('0', 31) + "1";
        scan.Cells[2].Thumb = "data:image/png;base64,iVBORw0KGgo=";

        JSInterop.SetupModule("./js/cardshot.js").Setup<ShotScan>("scan", _ => true).SetResult(scan);
        var page = await PageAsync();
        page.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "cards.png"));

        var shot = page.Find("button.slot-shot");
        Assert.Equal("false", shot.GetAttribute("aria-pressed"));
        Assert.Contains("larger", shot.GetAttribute("aria-label"));

        shot.Click();

        shot = page.Find("button.slot-shot");
        Assert.Equal("true", shot.GetAttribute("aria-pressed"));
        Assert.Contains("zoom", shot.ClassList);
        Assert.Contains("Hide", shot.GetAttribute("aria-label"));

        shot.Click();
        Assert.DoesNotContain("zoom", page.Find("button.slot-shot").ClassList);
    }

    [Fact]
    public async Task A_slot_named_by_hand_is_applied_like_any_other_card()
    {
        // The end of the manual fix, and the only part of it worth a page test: what the user picks
        // has to reach the collection by the same route a recognised card does. The pieces above it
        // -- the slot coming back with its picture, the pick folding into the reading -- are covered
        // in the engine suite, where they can be stated without a browser.
        //
        // Slot 3 is made unrecognisable rather than blank: a hash in the table for nothing. That is
        // a slot with a card in it the reader cannot name, which is the case the search box exists
        // for -- a blank slot is a card the user does not own and is named by its neighbours.
        var scan = MeasuredScan();
        scan.Cells[2].Hash = new string('0', 31) + "1";

        JSInterop.SetupModule("./js/cardshot.js").Setup<ShotScan>("scan", _ => true).SetResult(scan);
        var page = await PageAsync();
        page.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "cards.png"));

        var fix = Assert.Single(page.FindComponents<SlotFix>());
        Assert.Equal("Row 1, Card 3", fix.Find("label").TextContent);

        // Typed and then waited on, because the search is debounced -- a scan of every card in the
        // game on each keystroke is what that delay is there to avoid.
        //
        // Waited FOR rather than slept through. This was Task.Delay(600), which is a guess about
        // how long someone else's machine takes: it held on an idle box and lost on a loaded one,
        // where the result had not rendered yet and First() threw on an empty sequence. Polling
        // passes the moment the row appears and only spends the budget when it does not.
        fix.Find("input[type=search]").Input("Charmander");

        fix.WaitForAssertion(() =>
            Assert.Contains(fix.FindAll("button"), b => b.TextContent.Contains("A1-33")));

        // Found and clicked inside one InvokeAsync, the way every other click in this suite is:
        // the wait above renders, and an element handle taken before a render has no handler
        // attached by the time it is clicked.
        await fix.InvokeAsync(() =>
            fix.FindAll("button").First(b => b.TextContent.Contains("A1-33")).Click());

        // In the table as a card the user named, in its own slot, and counted apart from what the
        // artwork managed. Waited for as well: the pick folds into the reading through the page
        // above it, so the table it lands in is a render away rather than a return away. The sleep
        // this replaces was covering that too, by accident.
        page.WaitForAssertion(() =>
        {
            Assert.Contains("you named it", page.Markup);
            Assert.Contains("Named by Hand", page.Markup);
        });
        Assert.DoesNotContain("could not be named", page.Markup);

        await page.InvokeAsync(() => page.Find("button.btn-primary").Click());
        page.WaitForAssertion(() =>
            Assert.Equal(1, Session.CountOf(Session.Index.ByKey["A1-33"])));
    }

    [Fact]
    public async Task Apply_is_dead_when_the_ticked_half_has_nothing_behind_it()
    {
        // A tick outlives the reading that justified it. Mark-missing is armed against the
        // five-across list, where blanks are cards you do not own; saying the same picture is
        // actually the three-across list removes every removal, because an unowned card is absent
        // from that screen rather than blank on it. The tick stays set, and the button used to go
        // by the ticks alone — so it stayed live over a pair of halves that between them had
        // nothing to do.
        var page = await ReadingAsync();
        page.Find("#apply-missing").Change(true);

        page.Find("#cards-CopiesGrid").Change(true);
        Assert.Empty(page.FindAll("#apply-missing"));      // nothing is missing on this screen

        page.Find("#apply-found").Change(false);
        Assert.True(page.Find("button.btn-primary").HasAttribute("disabled"));

        page.Find("#apply-found").Change(true);
        Assert.False(page.Find("button.btn-primary").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Several_screenfuls_are_applied_as_one_import()
    {
        // The My Cards list runs past one screenful, so several shots of it are the normal case.
        // They describe one collection between them, so they get one apply and one undo step —
        // not one of each per picture.
        var page = await ReadingTwoAsync(MeasuredScan(5, 9, 12), MeasuredScan(9, 12));

        Assert.Equal(2, page.FindAll("h2").Count);          // one table per picture
        Assert.Single(page.FindAll("button.btn-primary"));  // one apply for the batch

        page.Find("button.btn-primary").Click();

        // Fourteen distinct cards, not twenty-seven: the two pictures overlap almost entirely, and
        // a card read twice is one card.
        Assert.Contains("Recorded 14 cards as owned", page.Markup);
        Assert.Equal(14, Session.Owned.DistinctOwned);
        Assert.True(Session.CanUndo);
    }

    [Fact]
    public async Task A_card_found_in_one_picture_is_not_deleted_by_another()
    {
        // The rule that makes the merge worth doing at all. Card 5 is a blank slot in the screenful
        // taken before you scrolled and a recognised card in the one taken after. Read on its own
        // the first picture says "delete it"; read beside the second it says nothing of the kind.
        // This is the destructive half of the import, so the doubt goes to keeping the card.
        var page = await ReadingTwoAsync(MeasuredScan(5, 9, 12), MeasuredScan(9, 12));

        foreach (var key in new[] { "A1-5", "A1-9", "A1-12" })
            Session.SetCount(Session.Index.ByKey[key], 1);

        page.Find("#apply-missing").Change(true);
        page.Find("button.btn-primary").Click();

        Assert.Equal(1, Session.CountOf(Session.Index.ByKey["A1-5"]));
        Assert.Equal(0, Session.CountOf(Session.Index.ByKey["A1-9"]));
        Assert.Equal(0, Session.CountOf(Session.Index.ByKey["A1-12"]));
        Assert.Contains("Marked 2 cards as not owned", page.Markup);
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

        page.Find("button:contains('Undo That')").Click();

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
