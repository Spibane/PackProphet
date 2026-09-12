namespace PackProphet.App.Tests;

using PackProphet.Pages;

/// <summary>
/// The Which pack page, as an Answer page: it leads with a verdict rather than with controls, so
/// the controls are behind one button and what they are set to is said in the bar instead.
/// </summary>
public class PacksPageTests : AppHost
{
    private async Task<IRenderedComponent<Packs>> PageAsync()
    {
        await ReadyAsync();
        return RenderComponent<Packs>();
    }

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
