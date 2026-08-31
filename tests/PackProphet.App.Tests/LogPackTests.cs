namespace PackProphet.App.Tests;

using PackProphet.Pages;

/// <summary>
/// The Log a pack picker, and the pin that lifts one pack out of the series ordering.
///
/// A pin is only worth having if it survives the thing it exists to avoid — the trip through the
/// series dropdown — so that is what these assert, rather than the mere fact of a preference being
/// written.
/// </summary>
public class LogPackTests : AppHost
{
    private async Task<IRenderedComponent<LogPack>> PageAsync()
    {
        await ReadyAsync();
        return RenderComponent<LogPack>();
    }

    /// <summary>Pack names in the order the picker draws them, read off the pin buttons.</summary>
    private static string[] Shown(IRenderedComponent<LogPack> page) =>
        page.FindAll(".pack-cell .pin-pack")
            .Select(b => b.GetAttribute("aria-label")!)
            .Select(l => l.StartsWith("Unpin ", StringComparison.Ordinal) ? l["Unpin ".Length..] : l["Pin ".Length..])
            .ToArray();

    [Fact]
    public async Task A_pinned_pack_comes_first()
    {
        var page = await PageAsync();

        var before = Shown(page);
        Assert.True(before.Length > 1, "the picker needs more than one pack for order to mean anything");

        // Not the first one, or the assertion would pass without the pin doing anything.
        var wanted = before[^1];
        page.FindAll(".pin-pack").ElementAt(before.Length - 1).Click();

        Assert.Equal(wanted, Shown(page)[0]);
    }

    [Fact]
    public async Task A_pin_survives_a_change_of_series()
    {
        var page = await PageAsync();

        var pinned = Shown(page)[^1];
        page.FindAll(".pin-pack").ElementAt(Shown(page).Length - 1).Click();

        // The series the picker did NOT open on. A pack pinned in one series has to stay at the
        // front in the other, since reaching it without changing series is the whole point.
        var other = Session.Sets.Series.First(s => s != Session.Sets.Series.Last());
        page.Find("select[aria-label='Series']").Change(other);

        Assert.Equal(pinned, Shown(page)[0]);
    }

    [Fact]
    public async Task Unpinning_puts_a_pack_back_with_its_series()
    {
        var page = await PageAsync();

        var before = Shown(page);
        page.FindAll(".pin-pack").ElementAt(before.Length - 1).Click();
        Assert.NotEqual(before, Shown(page));

        page.FindAll(".pin-pack").First().Click();     // the pinned one is now the first
        Assert.Equal(before, Shown(page));
    }

    [Fact]
    public async Task The_grid_bar_reports_what_is_picked_rather_than_how_big_the_pack_is()
    {
        // The count the grid shows by default is a fact about the pack, fixed for the whole task.
        // What moves — and what says when the pack is fully logged — is how many you have picked.
        var page = await PageAsync();

        page.FindAll(".pack-choice").First().Click();

        var bar = page.WaitForElement(".grid-count").TextContent;
        Assert.Contains("picked 0", bar);
        Assert.DoesNotContain(" cards", bar);
    }

    [Fact]
    public async Task The_header_that_carries_the_save_button_is_pinned_to_the_top()
    {
        // Tapping the last card of a pack must not mean scrolling back past every card already
        // tapped to reach "add to collection".
        var page = await PageAsync();

        page.FindAll(".pack-choice").First().Click();

        var head = page.WaitForElement(".page-head.sticky-head");
        Assert.Contains("sticky-head", head.GetAttribute("class"));
        Assert.Contains("Add 0 to collection", head.TextContent);
    }
}
