namespace PackProphet.App.Tests;

using PackProphet.Components;
using PackProphet.Pages;

/// <summary>
/// One target, read by all four Answer pages.
///
/// The pack ranking, the trade queue, the wishlist and the Wonder Pick bar ask the same question —
/// given what I am collecting, what should I do next — and each kept its own copy of what that
/// was. Four buttons labelled Target, four sheets behind them, and setting one changed nothing
/// anywhere else: a reader who narrowed the ranking to one set found Wonder Pick still appraising
/// offers against the whole game.
/// </summary>
public class SharedTargetTests : AppHost
{
    private static string Flat(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>A real set from the shipped data, so the label under test is one the app can name.</summary>
    private string ASet() => Session.Index.OpenableSets.OrderBy(s => s, StringComparer.Ordinal).First();

    [Fact]
    public async Task A_target_set_on_one_page_is_the_target_on_all_of_them()
    {
        await ReadyAsync();

        var set = ASet();

        // Through the picker's own callback rather than by writing state, so the test takes the
        // path a change actually takes.
        var packs = RenderComponent<Packs>();
        await packs.InvokeAsync(() =>
            packs.FindComponent<ScopePicker>().Instance.OnScope.InvokeAsync(
                new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = set }));

        Assert.Equal(set, Session.Target);

        // Every page's own control, since that is what a reader would check it against. The bar
        // says it too on three of the four; the wishlist keeps its scope in the rail.
        Assert.Equal(set, RenderComponent<Trades>().FindComponent<ScopePicker>().Instance.Scope);
        Assert.Equal(set, RenderComponent<TradeBoard>().FindComponent<ScopePicker>().Instance.Scope);
        Assert.Equal(set, RenderComponent<WonderPick>().FindComponent<ScopePicker>().Instance.Scope);
    }

    [Fact]
    public async Task Every_picker_has_the_shared_target_actually_selected()
    {
        // Not the parameter — the option the control is SHOWING. The two branches of ScopePicker
        // spelled one set "A1" and "set:A1", which cost nothing while each page kept its own
        // scope and broke the moment they shared one: the trade pages' dropdown found no matching
        // option and sat on "Everything" while the bar above it named the set.
        await ReadyAsync();

        var set = ASet();
        Session.SetTarget(set);

        Assert.Equal(set, Selected(RenderComponent<Packs>()));
        Assert.Equal(set, Selected(RenderComponent<Trades>()));
        Assert.Equal(set, Selected(RenderComponent<TradeBoard>()));
        Assert.Equal(set, Selected(RenderComponent<WonderPick>()));
    }

    private static string? Selected(IRenderedFragment page) =>
        page.Find(".scope-pick").QuerySelector("option[selected]")?.GetAttribute("value");

    [Fact]
    public async Task The_bar_says_what_the_page_is_working_toward()
    {
        // The reading half: a page ranking against one set while its bar still says "everything"
        // is the failure that made four separate scopes hard to notice in the first place.
        await ReadyAsync();

        var set = ASet();
        Session.SetTarget(set);
        var said = Session.ScopeLabel(set);

        Assert.Contains(said, Flat(RenderComponent<Packs>().Find(".page-head").TextContent));
        Assert.Contains(said, Flat(RenderComponent<Trades>().Find(".page-head").TextContent));
        Assert.Contains(said, Flat(RenderComponent<WonderPick>().Find(".page-head").TextContent));
    }

    [Fact]
    public async Task A_target_naming_a_deleted_chase_list_falls_back_rather_than_sticking()
    {
        // It is stored app-wide while a chase list belongs to one collection, so the stored string
        // can outlive what it names. The picker and the figures under it have to agree, and
        // "everything" is the only answer both can give.
        await ReadyAsync();

        var id = Session.CreateChaseList();
        Session.SetTarget($"chase:{id}");
        Assert.Equal($"chase:{id}", Session.Target);

        Session.DeleteChaseList(id);

        Assert.Equal("everything", Session.Target);
    }
}
