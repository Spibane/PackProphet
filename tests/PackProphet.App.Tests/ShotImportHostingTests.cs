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

        Assert.Equal(2, page.FindAll("h2").Count);
        Assert.Contains("one.png", page.Markup);
        Assert.Contains("two.png", page.Markup);
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
        // element collection reaches for.
        var adopt = page.FindAll("button.btn-primary");
        Assert.Equal(2, adopt.Count);
        adopt.ElementAt(1).Click();

        Assert.Contains("picked 5 of 5", page.Markup);
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

        Assert.Contains("more than one offer", page.Markup);
    }
}
