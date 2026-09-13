namespace PackProphet.App.Tests;

using PackProphet.Pages;

/// <summary>
/// Trades: a ranking of several hundred rows, and four controls deciding which several hundred.
///
/// The page that moved from a bar sheet to a rail, because its controls are not set once on
/// arrival — a reader works down the table, decides it is the wrong cut of the data, and reaches
/// for one. That is the case a column beside the content is for.
/// </summary>
public class TradesPageTests : AppHost
{
    private static string Flat(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    private async Task<IRenderedComponent<Trades>> PageAsync()
    {
        await ReadyAsync();
        return RenderComponent<Trades>();
    }

    [Fact]
    public async Task Every_control_sits_under_a_heading_saying_what_it_sets()
    {
        // The sheet opened onto "Everything, Ranked", "Hardest to Pull" and "Any Rarity" in a row,
        // with nothing saying which was the grouping and which the sort. Two selects side by side
        // cannot label each other.
        var page = await PageAsync();

        var headings = page.FindAll(".page-rail .rail-group .ttl")
            .Select(t => Flat(t.TextContent))
            .ToArray();

        Assert.Equal(4, headings.Length);
        Assert.Equal("Trading for", headings[0]);
        Assert.Equal("Grouped", headings[1]);
        Assert.Equal("Ranked by", headings[2]);
        Assert.StartsWith("Showing", headings[3]);
    }

    [Fact]
    public async Task The_bar_says_what_the_target_is_rather_than_leaving_it_to_the_rail()
    {
        // Every figure on the page is against this, and below 600px the rail is a sheet: the bar
        // is then the only thing on screen naming what the ranking is of.
        var page = await PageAsync();

        var subtitle = Flat(page.Find(".page-head .subtitle").TextContent);

        Assert.StartsWith("Target:", subtitle);
        Assert.Contains("stamina", subtitle);
        Assert.Contains("dust", subtitle);
    }

    [Fact]
    public async Task A_narrowed_table_says_so_in_the_bar_and_not_only_on_a_badge()
    {
        // The rail's badge counts controls that are doing something without saying what they are,
        // and on a phone the badge is all there is. A table filtered to what you can do today is a
        // different answer to the one an unlabelled ranking appears to be.
        var page = await PageAsync();

        Assert.Empty(page.FindAll(".page-head .subtitle .filters"));

        await page.InvokeAsync(() => page.Find(".page-rail button[aria-pressed]").Click());

        page.WaitForAssertion(() =>
        {
            var words = Flat(page.Find(".page-head .subtitle .filters").TextContent);
            Assert.Contains("doable only", words);
        });
    }

    [Fact]
    public async Task The_filters_can_be_cleared_from_where_they_are_reported()
    {
        // Each chip clears its own control. They were a loose row above the table and outside the
        // page shell, which is a second bar; in the rail they sit above the controls that set them.
        var page = await PageAsync();

        await page.InvokeAsync(() => page.Find(".page-rail button[aria-pressed]").Click());

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".page-rail .chip-filter")));
        await page.InvokeAsync(() => page.Find(".page-rail .chip-filter").Click());

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".page-rail .chip-filter"));
            Assert.Empty(page.FindAll(".page-head .subtitle .filters"));
        });
    }
}
