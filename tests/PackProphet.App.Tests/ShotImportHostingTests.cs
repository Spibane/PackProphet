namespace PackProphet.App.Tests;

using Microsoft.AspNetCore.Components.Forms;
using Bunit.JSInterop.InvocationHandlers;
using PackProphet.Pages;
using PackProphet.Vision;

/// <summary>
/// The two pages that host a screenshot import for their own purpose.
///
/// The same five cards mean different things depending on where you are: on the log screen they are
/// cards just acquired out of a pack that can be worked out from them, and on the Wonder Pick screen
/// they are an offer to weigh up and take nothing from. That is why the import lives on both pages
/// rather than asking, once, in a third place — and it is what these tests check, since the reading
/// itself is covered in the engine suite.
///
/// The fingerprints are real cardshot.js output; see ScreenshotEndToEndTests for where they came
/// from. A1-1 to A1-4 are all exclusive to the Mewtwo pack, so the pack really is determined by the
/// cards.
/// </summary>
public class ShotImportHostingTests : AppHost
{
    /// <summary>
    /// The committed fingerprint table, read as the app reads it. The hands below are built from its
    /// own entries, so these tests exercise the page wiring against a perfect reading and stay
    /// correct when the table is regenerated. How well a real screenshot actually fingerprints is a
    /// different question, measured on a real screenshot in ScreenshotEndToEndTests.
    /// </summary>
    private static ArtHashTable Table { get; } = ArtHashTable.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "card-hashes.txt")));

    private static string HashFor(string key) =>
        Table.Entries.First(e => e.Key == key).Hash.ToString();

    /// <summary>A1-1 to A1-4 and A1-8 are all exclusive to the Mewtwo pack, so the cards name it.</summary>
    private static readonly string[] MewtwoHand = ["A1-1", "A1-2", "A1-3", "A1-4", "A1-8"];

    /// <summary>
    /// A hand laid out the way the game lays one out: three cards, then two. Wide slots, so the
    /// reader does not mistake it for a card list.
    /// </summary>
    private static ShotScan HandScan(IReadOnlyList<string>? keys = null)
    {
        var cards = keys ?? MewtwoHand;
        return new ShotScan
        {
            Ok = true, Width = 920, Height = 1400,
            Lattice = new ShotLattice
            {
                Rows = 2, Cols = 3, CellWidth = 280, CellHeight = 390,
                Confidence = 0.6, RelativeCellWidth = 0.30,
            },
            Cells = cards.Select((key, i) => new ShotCell
            {
                Row = i / 3, Col = i % 3, Hash = HashFor(key),
                Detail = 0.07, Saturation = 0.45, Luma = 0.5,
            }).ToList(),
        };
    }

    private void StubScan(ShotScan scan) =>
        JSInterop.SetupModule("./js/cardshot.js")
                 .Setup<ShotScan>("scan", _ => true)
                 .SetResult(scan);

    /// <summary>Razor indentation becomes real whitespace in the DOM; assertions want the words.</summary>
    private static string Collapse(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void Upload(IRenderedFragment page) =>
        page.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "shot.png"));

    /// <summary>
    /// Two pictures in one go, each stubbed to its own scan. The scan is chosen by matching the data
    /// URL the component builds, so the file bytes differ — which is the only handle the test has on
    /// "this call is for that file".
    /// </summary>
    private void StubTwo(ShotScan first, ShotScan second)
    {
        var module = JSInterop.SetupModule("./js/cardshot.js");
        module.Setup<ShotScan>("scan", i => Sent(i, [1, 2, 3])).SetResult(first);
        module.Setup<ShotScan>("scan", i => Sent(i, [4, 5, 6])).SetResult(second);
    }

    private static bool Sent(JSRuntimeInvocation invocation, byte[] bytes) =>
        invocation.Arguments[0] is string url && url.EndsWith(Convert.ToBase64String(bytes));

    private static void UploadTwo(IRenderedFragment page) =>
        page.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([1, 2, 3], "one.png"),
            InputFileContent.CreateFromBinary([4, 5, 6], "two.png"));

    // ------------------------------------------------------------------ several at once

    [Fact]
    public async Task A_run_of_packs_is_read_as_a_run_of_packs()
    {
        // The reason multiple pictures exist at all on this page: packs get opened in a sitting, and
        // each one is its own event to log. Two shots are two offers to open a pack, not one merged
        // hand of ten cards.
        StubTwo(HandScan(), HandScan(["A2-1", "A2-2", "A2-3", "A2-4", "A2-5"]));
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        UploadTwo(page);

        // The per-picture headings, which are the ones named after a file. Identified by that
        // rather than by their class: the class is the app's smaller heading tier and the page
        // around this component uses it too, so counting the class counts headings that have
        // nothing to do with how many pictures were read.
        var perPicture = page.FindAll("h2")
            .Where(h => h.TextContent.Contains(".png", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, perPicture.Length);
        Assert.Contains(perPicture, h => h.TextContent.Contains("one.png", StringComparison.Ordinal));
        Assert.Contains(perPicture, h => h.TextContent.Contains("two.png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Each_picture_in_a_run_can_be_adopted_on_its_own()
    {
        // Adopting the second must load the second, not the first. The workspace holds one pack at a
        // time, so the pictures stay on screen to be worked through in turn.
        StubTwo(HandScan(["A1-1", "A1-2", "A1-3"]), HandScan());
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        UploadTwo(page);

        // ElementAt rather than the indexer: the pinned AngleSharp does not expose the one bUnit's
        // element collection reaches for. Excluding the batch button, which is one offer about all
        // the pictures rather than one of the per-picture offers being counted here.
        var adopt = page.FindAll("button.btn-primary:not(#log-all)");
        Assert.Equal(2, adopt.Count);

        // Twice, because taking one picture into the workspace discards the other and is asked
        // about first -- see the confirm test below.
        adopt.ElementAt(1).Click();
        page.FindAll("button.btn-primary:not(#log-all)").ElementAt(1).Click();

        Assert.Contains("picked 5 of 5", page.Markup);
    }

    [Fact]
    public async Task A_run_of_packs_is_logged_in_one_press_and_one_undo_step()
    {
        // The gap this closes: the per-picture button opens the picker for one pack, and doing that
        // unmounts the import along with every other reading in it. Ten packs meant ten uploads.
        //
        // Two different packs, so the batch cannot be collapsing them into one: A1-1..A1-4 and A1-8
        // are Mewtwo-exclusive, A1-100..A1-104 are Pikachu's.
        StubTwo(HandScan(), HandScan(["A1-100", "A1-101", "A1-102", "A1-103", "A1-104"]));
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        UploadTwo(page);

        Assert.Contains("Mewtwo, Pikachu", Collapse(page.Markup));

        // Re-found inside InvokeAsync and waited for, because logging runs through UiBusy, which
        // defers the work off the click -- see GridFilterTests.
        await page.InvokeAsync(() => page.Find("#log-all").Click());
        page.WaitForAssertion(() => Assert.Equal(2, Session.Profile.PackLog.Count));

        // Two events, one per picture, each carrying its own pack -- not one event of ten cards.
        var log = Session.Profile.PackLog;
        Assert.Equal(2, log.Count);
        Assert.Equal(["Mewtwo", "Pikachu"], log.Select(e => e.Pack).Order().ToArray());
        Assert.All(log, e => Assert.Equal(5, e.OwnershipKeys.Count));

        Assert.Equal(1, Session.CountOf(Session.Index.ByKey["A1-1"]));
        Assert.Equal(1, Session.CountOf(Session.Index.ByKey["A1-100"]));

        // Points accrue per pack, so two packs of one set is two lots -- and both sets got theirs.
        Assert.Equal(GameRules.PackPointsPerPack, Session.Profile.Resources.PackPointsBySet["A1"] / 2);

        page.WaitForAssertion(() =>
            Assert.Contains("Logged 2 packs and 10 cards", Collapse(page.Markup)));

        // One step, not two. Five packs logged in one press should be five packs unlogged in one
        // press back, rather than a row of undos to walk through.
        Assert.True(Session.CanUndo);
        Session.Undo();
        Assert.Empty(Session.Profile.PackLog);
        Assert.Equal(0, Session.CountOf(Session.Index.ByKey["A1-1"]));

        // And the readings are gone: the batch is spent, and a button still on screen is a button
        // that opens all of them a second time.
        Assert.Empty(page.FindAll("#log-all"));
        Assert.DoesNotContain("one.png", page.Markup);
    }

    [Fact]
    public async Task A_picture_that_does_not_name_a_pack_is_left_out_of_the_batch_and_counted()
    {
        // A batch must not be all-or-nothing over one picture that does not settle. A1-26 to A1-30
        // are in all three A1 packs, so a hand of them narrows to three and finishes at none -- the
        // other picture still logs, and the page says how many were left for the per-picture
        // buttons.
        StubTwo(HandScan(), HandScan(["A1-26", "A1-27", "A1-28", "A1-29", "A1-30"]));
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        UploadTwo(page);

        Assert.Contains("1 of these pictures does not say which pack it is", Collapse(page.Markup));

        await page.InvokeAsync(() => page.Find("#log-all").Click());
        page.WaitForAssertion(() => Assert.Single(Session.Profile.PackLog));

        var logged = Assert.Single(Session.Profile.PackLog);
        Assert.Equal("Mewtwo", logged.Pack);
        page.WaitForAssertion(() =>
            Assert.Contains("Logged 1 pack and 5 cards", Collapse(page.Markup)));
    }

    [Fact]
    public async Task A_batch_holding_a_wrong_sized_pack_asks_once_before_logging()
    {
        // The same question the single-pack commit asks, asked once for the batch rather than once
        // per picture. Four cards is not a size an A1 pack comes in, and a miscount recorded here
        // reads on History as the odds model being wrong.
        StubTwo(HandScan(), HandScan(["A1-1", "A1-2", "A1-3", "A1-4"]));
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        UploadTwo(page);

        await page.InvokeAsync(() => page.Find("#log-all").Click());
        page.WaitForAssertion(() =>
            Assert.Contains("holds a number of cards that pack cannot", Collapse(page.Markup)));

        Assert.Empty(Session.Profile.PackLog);          // asked, not done

        await page.InvokeAsync(() => page.Find("button.btn-warning").Click());
        page.WaitForAssertion(() => Assert.Equal(2, Session.Profile.PackLog.Count));
    }

    [Fact]
    public async Task Adopting_one_of_several_pictures_asks_before_dropping_the_rest()
    {
        // Taking one picture into the pack workspace unmounts the import and every other reading
        // with it. Nothing is written, so there is nothing to undo -- the files simply have to be
        // chosen again. Asked once, the way a delete is.
        StubTwo(HandScan(), HandScan(["A2-1", "A2-2", "A2-3", "A2-4", "A2-5"]));
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        UploadTwo(page);

        var first = page.FindAll("button.btn-primary:not(#log-all)").ElementAt(0);
        Assert.Contains("Open Mewtwo with these", Collapse(first.TextContent));

        first.Click();

        // Still on the import, and the button now says what pressing it again costs.
        Assert.Contains("Really? Other screenshots will be deleted.", Collapse(page.Markup));
        Assert.Contains("two.png", page.Markup);

        page.FindAll("button.btn-primary:not(#log-all)").ElementAt(0).Click();
        Assert.Contains("picked 5 of 5", page.Markup);
    }

    [Fact]
    public async Task Adopting_the_only_picture_asks_nothing()
    {
        // Nothing is lost with one picture, and a confirm on the ordinary path is a tap for its own
        // sake. This is the case the page was built around.
        StubScan(HandScan());
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        Upload(page);

        page.Find("button.btn-primary").Click();

        Assert.DoesNotContain("Really?", page.Markup);
        Assert.Contains("picked 5 of 5", page.Markup);
    }

    [Fact]
    public async Task Pressing_a_different_pack_moves_the_question_rather_than_answering_it()
    {
        // A picture whose cards fit several packs offers a shortlist. Pressing Mewtwo and then
        // Charizard is two different decisions, so the second press must re-ask rather than being
        // taken as the confirm for the first. A1-26 to A1-30 are in all three A1 packs.
        StubTwo(HandScan(["A1-26", "A1-27", "A1-28", "A1-29", "A1-30"]), HandScan());
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        UploadTwo(page);

        var shortlist = page.FindAll("button.btn-outline-primary");
        Assert.True(shortlist.Count >= 2, "the shortlist should offer more than one pack");

        shortlist.ElementAt(0).Click();
        Assert.Contains("Really?", page.Markup);

        // A different pack, so the workspace must not open.
        page.FindAll("button.btn-outline-primary").ElementAt(1).Click();
        Assert.DoesNotContain("picked", page.Markup);
        Assert.Contains("Really?", page.Markup);

        // The same one twice does open it.
        page.FindAll("button.btn-outline-primary").ElementAt(1).Click();
        Assert.Contains("picked 5 of 5", page.Markup);
    }

    [Fact]
    public async Task Appraising_one_of_several_offers_asks_before_dropping_the_rest()
    {
        // The Wonder Pick page has the same shape and the same cost: the import shows only while
        // the bench is empty, so filling it takes the other readings away.
        StubTwo(HandScan(), HandScan(["A2-1", "A2-2", "A2-3", "A2-4", "A2-5"]));
        await ReadyAsync();

        var page = RenderComponent<WonderPick>();
        UploadTwo(page);

        page.FindAll("button.btn-primary").ElementAt(0).Click();
        Assert.Contains("Really? Other screenshots will be deleted.", Collapse(page.Markup));
        Assert.Contains("two.png", page.Markup);

        page.FindAll("button.btn-primary").ElementAt(0).Click();
        Assert.DoesNotContain("two.png", page.Markup);      // the bench is loaded, the import gone
    }

    [Fact]
    public async Task One_unreadable_picture_does_not_lose_the_others()
    {
        StubTwo(ShotScan.Failed("No cards were found in that image."), HandScan());
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        UploadTwo(page);

        // The failure names its own file, because "that file could not be read" beside two pictures
        // does not say which one to retake.
        Assert.Contains("one.png", page.Markup);
        Assert.Equal("Open Mewtwo with these 5 cards",
                     Collapse(page.Find("button.btn-primary").TextContent));
    }

    [Fact]
    public async Task A_mistap_on_the_camera_roll_does_not_lose_the_pictures_already_read()
    {
        // The refusal must not cost the batch behind it. Someone part-way through eight pack shots
        // who fat-fingers the whole roll should get a warning over their results, not an empty page.
        StubScan(HandScan());
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        Upload(page);
        Assert.Contains("Open Mewtwo with these", Collapse(page.Markup));

        page.FindComponent<InputFile>().UploadFiles(
            Enumerable.Range(0, 21)
                      .Select(i => InputFileContent.CreateFromBinary([1, 2, 3], $"{i}.png"))
                      .ToArray());

        Assert.Contains("more than 20 pictures", page.Markup);
        Assert.Contains("Open Mewtwo with these", Collapse(page.Markup));
    }

    [Fact]
    public async Task A_whole_camera_roll_is_refused_rather_than_read()
    {
        StubScan(HandScan());
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        page.FindComponent<InputFile>().UploadFiles(
            Enumerable.Range(0, 21)
                      .Select(i => InputFileContent.CreateFromBinary([1, 2, 3], $"{i}.png"))
                      .ToArray());

        // Said, not silently truncated: reading the first twenty of twenty-one would log nineteen
        // packs and lose one without ever mentioning it.
        Assert.Contains("more than 20 pictures", page.Markup);
        Assert.Empty(page.FindAll("button.btn-primary"));
    }

    // ------------------------------------------------------------------ the log screen

    [Fact]
    public async Task The_log_screen_works_out_which_pack_from_the_cards()
    {
        StubScan(HandScan());
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        Upload(page);

        // Named, not asked. All five are Mewtwo-exclusive, so the picture settles it.
        // Whitespace collapsed: the label spans several lines in the markup.
        var label = Collapse(page.Find("button.btn-primary").TextContent);
        Assert.Equal("Open Mewtwo with these 5 cards", label);
    }

    [Fact]
    public async Task Adopting_a_pack_shot_fills_in_the_pack_and_the_cards()
    {
        StubScan(HandScan());
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        Upload(page);
        page.Find("button.btn-primary").Click();

        // The page is now where it would be after picking Mewtwo and tapping five cards — and no
        // further, because the user still confirms against the grid with the button they would
        // have used anyway.
        Assert.Contains("picked 5 of 5", page.Markup);
        Assert.Equal(0, Session.Owned.DistinctOwned);
    }

    [Fact]
    public async Task A_pack_shot_does_not_touch_the_collection_until_it_is_committed()
    {
        StubScan(HandScan());
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        Upload(page);
        page.Find("button.btn-primary").Click();

        Assert.Equal(0, Session.Owned.DistinctOwned);
        Assert.False(Session.CanUndo);
    }

    [Fact]
    public async Task Cards_no_single_pack_holds_are_reported_rather_than_named()
    {
        // No one pack holds both of these, which means something in the picture was read wrongly.
        // Saying so beats naming a pack: the wrong pack would be logged against the wrong odds and
        // nothing would look amiss afterwards.
        // A1-5 is exclusive to Pikachu and A1-11 to Charizard, so no one pack holds both.
        StubScan(HandScan(["A1-5", "A1-11"]));
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        Upload(page);

        Assert.DoesNotContain("open Mewtwo", page.Markup);
        Assert.Contains("No single pack", page.Markup);
    }

    // ------------------------------------------------------------------ the Wonder Pick screen

    [Fact]
    public async Task The_wonder_pick_screen_appraises_the_offer_from_a_shot()
    {
        StubScan(HandScan());
        await ReadyAsync();

        var page = RenderComponent<WonderPick>();
        Upload(page);
        page.Find("button.btn-primary").Click();

        // The five cards are in the offer and the page has judged it — which is the whole of what
        // this page wanted from a screenshot. The cards were the tedious part; the call is the point.
        Assert.Contains("Bulbasaur", page.Markup);
        Assert.DoesNotContain("Add card 1 of 5", page.Markup);
    }

    [Fact]
    public async Task A_wonder_pick_shot_never_adds_anything_to_the_collection()
    {
        // These are cards on offer, not cards held. Nothing about reading one is a collection edit.
        StubScan(HandScan());
        await ReadyAsync();

        var page = RenderComponent<WonderPick>();
        Upload(page);
        page.Find("button.btn-primary").Click();

        Assert.Equal(0, Session.Owned.DistinctOwned);
        Assert.False(Session.CanUndo);
    }

    /// <summary>
    /// A hand where four cards fingerprint and the fifth does not: the pack is determined, and one
    /// slot still has to be named by hand.
    /// </summary>
    private static ShotScan HandWithOneUnread()
    {
        var scan = HandScan([.. MewtwoHand.Take(4)]);
        scan.Cells.Add(new ShotCell
        {
            // A hash that matches nothing in the table, which is what an artwork the reader cannot
            // place looks like by the time it reaches the page.
            Row = 1, Col = 1, Hash = new string('f', 32),
            Detail = 0.07, Saturation = 0.45, Luma = 0.5,
        });
        return scan;
    }

    /// <summary>A card of the same set that this pack cannot give, chosen from the data rather than
    /// named here: which cards a pack holds changes, and a hard-coded one quietly stops testing
    /// anything the day it becomes a card the pack does hold.</summary>
    private string OutsideTheMewtwoPack()
    {
        var inside = Session.Index.ByPack["A1:Mewtwo"]
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Session.Index.All
            .First(c => c.Set == "A1" && !inside.Contains(c.Name) && c.Name.Length > 4)
            .Name;
    }

    [Fact]
    public async Task A_slot_you_have_to_name_is_searched_within_the_pack_the_picture_is_of()
    {
        // The cards that WERE read say which pack this is, and a pack holds about eighty cards. So
        // the slot that was not read is one of those eighty and cannot be anything else.
        //
        // Searching the whole game for it answers a closed question with an open list — and the
        // answers it puts first are the same Pokémon printed in other sets, which is the exact
        // resemblance that stopped the slot being read in the first place.
        StubScan(HandWithOneUnread());
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        Upload(page);

        var box = page.Find("input[type=search]");

        // It says which pack it is over. A short list is only obviously right if you can see what
        // makes it short.
        Assert.Contains("Mewtwo", box.GetAttribute("placeholder"));

        // A card of the same set that this pack cannot give. Nothing, because this picture cannot
        // be of it.
        box.Input(OutsideTheMewtwoPack());
        page.WaitForAssertion(
            () => Assert.Contains("Search every card", page.Markup),
            TimeSpan.FromSeconds(3));
        Assert.Empty(page.FindAll(".btn-outline-primary"));

        // And a card the pack does hold comes back — with every hit from that pack, which is the
        // assertion that would fail if the restriction were only a sort order.
        var inside = Session.Index.ByPack["A1:Mewtwo"];
        box.Input(inside[0].Name);
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll(".btn-outline-primary")),
            TimeSpan.FromSeconds(3));

        var keys = inside.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        Assert.All(page.FindAll(".btn-outline-primary"), hit =>
            Assert.Contains(hit.QuerySelector("span")!.TextContent.Trim(), keys));
    }

    [Fact]
    public async Task The_short_list_can_be_escaped_when_the_pack_was_guessed_wrong()
    {
        // The pack is worked out from the cards that were read, so it can be wrong — a hand of
        // shared high rarities, a reprint recognised as its original printing. It is rare, and when
        // it happens the card in front of you is not in the list at all, so a restriction with no
        // way past it is a slot that can never be named.
        StubScan(HandWithOneUnread());
        await ReadyAsync();

        var page = RenderComponent<LogPack>();
        Upload(page);

        var box = page.Find("input[type=search]");
        box.Input(OutsideTheMewtwoPack());
        page.WaitForAssertion(
            () => Assert.Contains("Search every card", page.Markup),
            TimeSpan.FromSeconds(3));

        page.FindAll("button").First(b => b.TextContent.Contains("Search every card")).Click();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll(".btn-outline-primary")),
            TimeSpan.FromSeconds(3));

        // And it stops claiming to be over one pack once it is not.
        Assert.DoesNotContain("Mewtwo", page.Find("input[type=search]").GetAttribute("placeholder"));
    }

    [Fact]
    public async Task More_cards_than_an_offer_holds_is_flagged_rather_than_truncated_silently()
    {
        // A screenshot of the Wonder Pick list holds several offers at once. Taking the first five
        // would appraise a mixture of two, and the appraisal would look just as confident.
        // Seven cards: more than one offer caught in the same picture.
        StubScan(HandScan([.. MewtwoHand, "A1-10", "A1-16"]));
        await ReadyAsync();

        var page = RenderComponent<WonderPick>();
        Upload(page);

        // On the fact rather than on the sentence: what has to reach the reader is that the
        // picture holds more than one offer and that only the first five would be used.
        Assert.Contains("two offers", page.Markup);
        Assert.Contains("Crop to one", page.Markup);
    }
}
