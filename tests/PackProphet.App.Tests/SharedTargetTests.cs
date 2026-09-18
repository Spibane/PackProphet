namespace PackProphet.App.Tests;

using PackProphet.Components;
using PackProphet.Pages;

/// <summary>
/// A target lasts as long as the visit that set it, and no longer.
///
/// The four Answer pages ask one question -- given what I am collecting, what should I do next --
/// so for a while they shared one saved answer. The answer does not keep. Saved, it outlived the
/// visit: a page opened days later ranked against a set chosen once on another screen, and a link
/// could write it, so "Which Pack" on a Progress panel decided what Wonder Pick thought an offer
/// was worth. Nothing is stored now, nothing is shared, and a redirect is the one thing that can
/// say what a page opens on.
/// </summary>
public class SharedTargetTests : AppHost
{
    private static string Flat(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>A real set from the shipped data, so the label under test is one the app can name.</summary>
    private string ASet() => Session.Index.OpenableSets.OrderBy(s => s, StringComparer.Ordinal).First();

    private static string Scope(IRenderedFragment page) =>
        page.FindComponent<ScopePicker>().Instance.Scope;

    private static async Task ChooseAsync(IRenderedFragment page, string scope) =>
        await page.InvokeAsync(() =>
            page.FindComponent<ScopePicker>().Instance.OnScope.InvokeAsync(
                new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = scope }));

    [Fact]
    public async Task Every_page_opens_on_everything()
    {
        await ReadyAsync();

        Assert.Equal("everything", Scope(RenderComponent<Packs>()));
        Assert.Equal("everything", Scope(RenderComponent<Trades>()));
        Assert.Equal("everything", Scope(RenderComponent<TradeBoard>()));
        Assert.Equal("everything", Scope(RenderComponent<WonderPick>()));
    }

    [Fact]
    public async Task A_scope_chosen_on_one_page_stays_on_that_page()
    {
        await ReadyAsync();
        var set = ASet();

        var trades = RenderComponent<Trades>();
        await ChooseAsync(trades, set);

        // It holds where it was chosen,
        Assert.Equal(set, Scope(trades));

        // and nowhere else. This is the whole change: a narrowed trade queue is not an
        // instruction about what Wonder Pick should think is worth a stamina.
        Assert.Equal("everything", Scope(RenderComponent<Packs>()));
        Assert.Equal("everything", Scope(RenderComponent<WonderPick>()));
        Assert.Equal("everything", Scope(RenderComponent<TradeBoard>()));
    }

    [Fact]
    public async Task A_scope_is_forgotten_when_the_page_is_left()
    {
        // Nothing is persisted, so coming back is a fresh visit. A stored scope is the thing that
        // used to have a reader ranking against a set they picked on a Tuesday.
        await ReadyAsync();
        var set = ASet();

        await ChooseAsync(RenderComponent<Trades>(), set);

        Assert.Equal("everything", Scope(RenderComponent<Trades>()));
        Assert.Null(Session.State.Prefs.Target);
    }

    [Fact]
    public async Task A_picker_has_its_own_scope_actually_selected()
    {
        // Not the parameter -- the option the control is SHOWING. The two branches of ScopePicker
        // spelled one set "A1" and "set:A1", and a picker that finds no matching option sits on
        // "Everything" while the bar above it names the set.
        await ReadyAsync();
        var set = ASet();

        var packs = RenderComponent<Packs>();
        await ChooseAsync(packs, set);

        Assert.Equal(set, packs.Find(".scope-pick").QuerySelector("option[selected]")?.GetAttribute("value"));
    }

    [Fact]
    public async Task The_bar_says_what_the_page_is_working_toward()
    {
        // The reading half: a page ranking against one set while its bar still says "everything"
        // is the failure that made four separate scopes hard to notice in the first place.
        await ReadyAsync();
        var set = ASet();
        var said = Session.ScopeLabel(set);

        var packs = RenderComponent<Packs>();
        await ChooseAsync(packs, set);
        Assert.Contains(said, Flat(packs.Find(".page-head").TextContent));

        var trades = RenderComponent<Trades>();
        await ChooseAsync(trades, set);
        Assert.Contains(said, Flat(trades.Find(".page-head").TextContent));

        var wonder = RenderComponent<WonderPick>();
        await ChooseAsync(wonder, set);
        Assert.Contains(said, Flat(wonder.Find(".page-head").TextContent));
    }

    [Fact]
    public async Task A_scope_naming_a_deleted_chase_list_falls_back_rather_than_sticking()
    {
        // A list can go while the page holding it is open -- from another tab, or from a sync.
        // The picker and the figures under it have to agree, and "everything" is the only answer
        // both can give.
        await ReadyAsync();

        var id = Session.CreateChaseList();
        var page = RenderComponent<Trades>();
        await ChooseAsync(page, $"chase:{id}");
        Assert.Equal($"chase:{id}", Scope(page));

        Session.DeleteChaseList(id);
        page.Render();

        Assert.Equal("everything", Scope(page));
    }
}
