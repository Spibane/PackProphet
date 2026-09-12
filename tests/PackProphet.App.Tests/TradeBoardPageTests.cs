namespace PackProphet.App.Tests;

using PackProphet.Domain;
using PackProphet.Engine;
using PackProphet.Pages;

/// <summary>
/// The wishlist, as the Answer page whose answer is a diff.
///
/// Every other Answer page recommends one thing out of many. This one recommends edits, because
/// the board already exists and is retyped by hand in the game — twenty rows of "here is your
/// board" is a transcription job, and five edits is a minute.
/// </summary>
public class TradeBoardPageTests : AppHost
{
    private async Task<IRenderedComponent<TradeBoard>> PageAsync()
    {
        await ReadyAsync();

        // Something worth wishlisting: a plan that wants the rarities a board can carry.
        var tiers = new[] { "R", "RR" }
            .Select(Session.Index.Ladder.IndexOf)
            .Where(i => i is not null)
            .Select(i => i!.Value)
            .ToHashSet();

        Session.SetPlan(RarityPlan.Uniform(tiers));

        var page = RenderComponent<TradeBoard>();
        page.WaitForState(() => page.FindAll(".box-strip .box").Count > 0, TimeSpan.FromSeconds(10));
        return page;
    }

    private static string Flat(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    [Fact]
    public async Task The_strip_has_a_box_for_every_slot_the_board_holds()
    {
        // The literal set this component was written for: the board HAS twenty slots and they are
        // numbered. Mostly solid with the gaps at the end says "nearly right" without anyone
        // counting to twenty, which no list of names can do — so every slot gets a box, including
        // the empty ones.
        var page = await PageAsync();

        var boxes = page.FindAll(".box-strip .box").ToArray();
        Assert.Equal(GameRules.TradeBoardSlots, boxes.Length);

        // Numbered in order, because the board is entered by walking a numbered list in the game.
        var labels = boxes.Select(b => b.QuerySelector(".lbl")!.TextContent.Trim()).ToArray();
        Assert.Equal(
            Enumerable.Range(1, GameRules.TradeBoardSlots).Select(n => n.ToString()).ToArray(),
            labels);
    }

    [Fact]
    public async Task A_filled_slot_names_its_card_to_a_screen_reader()
    {
        // The box is a picture and nothing else — the art IS the recognition, at 30px, which is
        // the whole reason the strip works as a shape. A picture with no name is a box that says
        // nothing at all to anyone not looking at it.
        var page = await PageAsync();

        var filled = page.FindAll(".box-strip .box.live").ToArray();
        Assert.NotEmpty(filled);

        Assert.All(filled, b =>
        {
            var named = b.QuerySelector(".visually-hidden")?.TextContent.Trim();
            Assert.False(string.IsNullOrWhiteSpace(named), "a filled slot must name its card");

            // And the art itself is decorative, since the name beside it already says which card
            // this is — announcing both is the same card read twice.
            Assert.Equal("true", b.QuerySelector(".slot-art")!.GetAttribute("aria-hidden"));
        });
    }

    [Fact]
    public async Task The_verdict_counts_edits_and_prices_them_in_effort()
    {
        // The cost of this page's recommendation is typing, not packs — it is the one Answer page
        // whose currency is the user's own minutes.
        var page = await PageAsync();

        var then = Flat(page.Find(".verdict-lead .then").TextContent);
        Assert.Contains("retyping in the game", then);
        Assert.Contains("edit", then);

        // "about about 3 minutes" — the helper and the sentence both supplied the hedge.
        Assert.DoesNotContain("about about", then);

        // And the headline says what kind of edit rather than repeating the count.
        Assert.DoesNotContain("in total", Flat(page.Find(".verdict-lead .name").TextContent));
    }

    [Fact]
    public async Task A_long_list_of_names_stops_being_a_list()
    {
        // A first run on an empty collection asks for all twenty slots, and twenty names in a row
        // is not prose — it is the transcription job this page exists to replace, printed above
        // the table that already lists them properly.
        var page = await PageAsync();

        var says = Flat(page.Find(".verdict-lead .says").TextContent);
        Assert.True(says.Count(c => c == ',') <= 4,
            $"the sentence is listing too many names: {says}");
    }

    [Fact]
    public async Task The_controls_are_in_a_rail_and_the_bar_says_where_they_are_set()
    {
        // The tuning decides which cards are even eligible, so a board read without it is a board
        // you cannot account for — and on a phone the controls are behind a button.
        var page = await PageAsync();

        Assert.Single(page.FindAll(".page-rail"));
        Assert.Empty(page.FindAll(".page-tools"));

        // The scope and all three knobs, in the column rather than folded into a disclosure.
        Assert.Empty(page.FindAll(".page-rail details"));
        Assert.NotEmpty(page.FindAll(".page-rail select"));

        // And the bar carries what they are set to, in words.
        Assert.False(string.IsNullOrWhiteSpace(page.Find(".page-head .subtitle").TextContent));
    }

    [Fact]
    public async Task The_plan_can_be_run_again_without_changing_anything()
    {
        // The plan reads the collection at the moment it ran, and what follows it is retyping the
        // board and opening more packs. Until this button existed the only way to re-run was to
        // change a control and change it back, which also changed the plan.
        var page = await PageAsync();

        var rerun = page.FindAll(".page-head .actions button")
            .FirstOrDefault(b => b.GetAttribute("aria-label") == "Work the plan out again");

        Assert.NotNull(rerun);
    }
}
