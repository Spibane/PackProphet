namespace PackProphet.App.Tests;

using PackProphet.Engine;
using PackProphet.Pages;

/// <summary>
/// The Which pack page, as an Answer page: it leads with a verdict rather than with controls, so
/// the controls are behind one button and what they are set to is said in the bar instead.
/// </summary>
public class PacksPageTests : AppHost
{
    /// <summary>
    /// The page with something to rank for.
    ///
    /// A brand-new profile collects nothing, so the target is already satisfied and the page
    /// correctly says so instead of ranking anything -- which is the right behaviour and the wrong
    /// fixture for every assertion here. Wanting one of each diamond gives it a question to answer.
    /// </summary>
    private async Task<IRenderedComponent<Packs>> PageAsync()
    {
        await ReadyAsync();

        var diamonds = new[] { "C", "U", "R", "RR" }
            .Select(Session.Index.Ladder.IndexOf)
            .Where(i => i is not null)
            .Select(i => i!.Value)
            .ToHashSet();

        Session.SetPlan(RarityPlan.Uniform(diamonds));

        var page = RenderComponent<Packs>();

        // The ranking is async and runs behind the busy indicator, so the first render is the page
        // without it -- every assertion here is about what the ranking produced.
        page.WaitForState(() => page.FindAll("tr.pack-row-click").Count > 0, TimeSpan.FromSeconds(10));

        return page;
    }

    private static string Flat(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    [Fact]
    public async Task The_page_stacks_no_bar_of_its_own_beyond_the_page_bar()
    {
        // It had two: a header, and a tools bar carrying the scope picker with the rarity plan and
        // the availability chips folded into a disclosure inside it. An Answer page gets no rail —
        // it leads with a verdict, not with content to browse — so the one group of controls it has
        // goes behind a button rather than standing up a strip for itself.
        var page = await PageAsync();

        Assert.Single(page.FindAll(".page-head"));
        Assert.Empty(page.FindAll(".page-tools"));
        Assert.Empty(page.FindAll(".grid-toolbar"));
    }

    [Fact]
    public async Task Everything_that_sets_what_is_ranked_is_in_one_sheet()
    {
        // Scope, rarity plan and availability are one kind of control: each changes what is being
        // ranked, so each changes every figure on the page. They belong together, and they were a
        // bar plus a fold inside it.
        var page = await PageAsync();

        var sheet = page.Find("#target-sheet");
        Assert.True(sheet.HasAttribute("popover"));

        // The scope picker, and the rarity chips that edit the plan.
        Assert.NotNull(sheet.QuerySelector("select"));
        Assert.NotEmpty(sheet.QuerySelectorAll(".rarity-chip, .chip-tog"));

        // Opened from the bar, and from nowhere else.
        var openers = page.FindAll("[popovertarget='target-sheet']").ToArray();
        Assert.NotEmpty(openers);
        Assert.NotNull(openers[0].Closest(".page-head"));

        // And no fold within the fold: the sheet is the disclosure now.
        Assert.Empty(sheet.QuerySelectorAll("details"));
    }

    [Fact]
    public async Task The_page_leads_with_a_verdict_about_the_best_pack()
    {
        // The ranking's first row IS the answer, and it was arriving as a tinted row in a table of
        // a dozen with the figure that matters under a column heading.
        var page = await PageAsync();

        var lead = page.Find(".verdict-lead");
        Assert.Contains("Open this next", lead.TextContent);

        // The same pack the table calls best, said at the top rather than found by reading down.
        var best = page.Find("tr.row-best .pack-name strong").TextContent.Trim();
        Assert.Equal(best, lead.QuerySelector(".name")!.TextContent.Trim());
    }

    [Fact]
    public async Task The_verdict_spends_the_arithmetic_in_words()
    {
        // The copy rule for an Answer page: a percentage is a number the reader has to convert
        // before acting on it, so the page converts it. And a rate becomes calendar time, which is
        // the figure that actually changes a decision -- nobody plans in packs.
        var page = await PageAsync();

        // Whitespace-collapsed: the sentence is wrapped across several lines of markup, so its
        // text content carries the indentation between them.
        var says = Flat(page.Find(".verdict-lead .says").TextContent);
        Assert.Contains("gives you a card you still need", says);

        // Agreement, both ways. "roughly 1 packs" and "About every pack" were both real.
        Assert.DoesNotContain("1 packs", says);
        Assert.DoesNotContain("About every pack", says);
        Assert.DoesNotContain("packs more", says);

        Assert.Contains("free packs a day", Flat(page.Find(".verdict-lead .then").TextContent));
    }

    [Fact]
    public async Task The_strip_has_one_box_per_card_in_the_pack()
    {
        // The strip's precondition is that the set of boxes is literal. A pack has five cards, so
        // there are five boxes -- the engine groups adjacent positions that share a distribution,
        // and the strip expands them back out, because "1st-3rd card" is an implementation detail
        // of how the rates are published rather than something a player sees.
        var page = await PageAsync();

        var slots = page.FindAll(".verdict-lead .slot").ToArray();
        Assert.Equal(5, slots.Length);

        // Each one says which position it is, in order.
        var ordinals = slots.Select(s => s.QuerySelector(".ord")!.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "1st", "2nd", "3rd", "4th", "5th" }, ordinals);

        // And every box is either live with a figure or dead with a dash — never blank, which
        // would read as data that failed to arrive rather than as a position that cannot help.
        Assert.All(slots, s =>
        {
            var fig = s.QuerySelector(".fig")!.TextContent.Trim();
            Assert.False(string.IsNullOrEmpty(fig));
            if (s.ClassList.Contains("dead")) Assert.Equal("\u2014", fig);
        });
    }

    [Fact]
    public async Task Tapping_a_ranked_row_promotes_that_pack_into_the_verdict()
    {
        // Comparing two packs should be reading one sentence twice, not reading across five
        // columns of a table.
        var page = await PageAsync();

        var rows = page.FindAll("tr.pack-row-click").ToArray();
        Assert.True(rows.Length > 1, "need a second pack to promote");

        var second = rows[1].QuerySelector(".pack-name strong")!.TextContent.Trim();
        Assert.NotEqual(second, page.Find(".verdict-lead .name").TextContent.Trim());

        await page.InvokeAsync(() => page.FindAll("tr.pack-row-click").ToArray()[1].Click());

        page.WaitForAssertion(() =>
            Assert.Equal(second, page.Find(".verdict-lead .name").TextContent.Trim()));

        // And it is no longer introduced as the one to open, because it is not the best one.
        Assert.DoesNotContain("Open this next", page.Find(".verdict-lead").TextContent);
    }

    [Fact]
    public async Task The_bar_says_what_the_ranking_is_of()
    {
        // Every figure on this page means something different once the scope or the plan changes,
        // and both now live behind a button. The subtitle is what keeps the ranking honest about
        // its own premise — it used to name the scope and leave the plan folded away under the
        // control that sets it.
        var page = await PageAsync();

        var subtitle = page.Find(".page-head .subtitle");
        Assert.False(string.IsNullOrWhiteSpace(subtitle.TextContent),
            "the bar must say what the ranking is of");

        // The plan is said here, not only inside the sheet: PlanSummary renders the copies wanted
        // per rarity, and it is the half of the premise that used to be invisible from the page.
        Assert.NotNull(subtitle.QuerySelector(".plan-summary, .glyphs"));
    }
}
