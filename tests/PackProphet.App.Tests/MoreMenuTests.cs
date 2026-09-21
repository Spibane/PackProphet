namespace PackProphet.App.Tests;

using PackProphet.Components;
using PackProphet.Domain;

/// <summary>
/// The sheet behind More, and the one thing on it that is not a place to go.
///
/// Crediting the day's hourglasses is a daily chore that used to live only on Resources. It is on
/// the menu because the menu is already open wherever you are; it is outside the &lt;nav&gt;
/// because a landmark listing destinations should not have a button in it; and it is guarded,
/// because two buttons now press the same act and a day credited twice is invisible afterwards.
/// </summary>
public class MoreMenuTests : AppHost
{
    private async Task<IRenderedComponent<MoreMenu>> MenuAsync()
    {
        await ReadyAsync();
        return RenderComponent<MoreMenu>(p => p.Add(m => m.Id, "more-test"));
    }

    [Fact]
    public async Task The_dailies_button_is_on_the_sheet_and_not_among_the_destinations()
    {
        var menu = await MenuAsync();

        var button = menu.Find("button.nav-more-daily");
        Assert.Contains("Add Today's Dailies", button.TextContent);

        // Outside the landmark. A screen reader running the links must not find a button among
        // them, and the grid above is what "everything else" names.
        Assert.Empty(menu.FindAll("nav button.nav-more-daily"));
    }

    [Fact]
    public async Task Pressing_it_credits_the_day_and_then_says_the_day_is_credited()
    {
        var menu = await MenuAsync();

        var before = Session.Profile.Resources;
        Assert.False(Session.DailyHourglassesAdded);

        menu.Find("button.nav-more-daily").Click();

        Assert.Equal(before.PackHourglasses + GameRules.DailyPackHourglasses,
                     Session.Profile.Resources.PackHourglasses);
        Assert.Equal(before.Wonder.Hourglasses + GameRules.DailyWonderHourglasses,
                     Session.Profile.Resources.Wonder.Hourglasses);

        // Disabled rather than gone, and it says which: a button that vanishes leaves someone
        // wondering whether they pressed it.
        var spent = menu.Find("button.nav-more-daily");
        Assert.NotNull(spent.GetAttribute("disabled"));
        Assert.Contains("Today's Dailies Added", spent.TextContent);
    }

    [Fact]
    public async Task The_day_is_spent_whichever_of_the_two_buttons_was_used()
    {
        // Resources keeps its own copy of this button. Both press the same guard, so the second
        // one has to know the day is already gone -- otherwise the pair credits twice.
        await ReadyAsync();
        Session.AddDailyHourglasses();

        var menu = RenderComponent<MoreMenu>(p => p.Add(m => m.Id, "more-test"));
        var button = menu.Find("button.nav-more-daily");

        Assert.NotNull(button.GetAttribute("disabled"));
        Assert.Contains("Today's Dailies Added", button.TextContent);
    }
}
