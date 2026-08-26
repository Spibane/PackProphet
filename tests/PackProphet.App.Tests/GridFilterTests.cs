namespace PackProphet.App.Tests;

using PackProphet.Pages;
using PackProphet.Services;

/// <summary>
/// The grid's own bar: the filters that were promoted out of the disclosure, the chips that
/// replaced two single-select dropdowns, and the strip that says which set you are scrolling
/// through.
///
/// These assert against the rendered markup rather than against page fields, because the point of
/// every one of them is that a control is reachable — a filter that works and cannot be found is
/// the bug they were written for.
///
/// Every click is re-found inside InvokeAsync and every state change is waited for. Choosing a set
/// runs through UiBusy, which defers the work off the click, so an element handle taken before that
/// render has no handler attached by the time it is clicked.
/// </summary>
public class GridFilterTests : AppHost
{
    private async Task<IRenderedComponent<Collection>> PageAsync()
    {
        await ReadyAsync();
        return RenderComponent<Collection>();
    }

    /// <summary>Switch the picker to a series and then to one of its sets.</summary>
    private static async Task ChooseAsync(IRenderedComponent<Collection> page, string series, string set)
    {
        page.Find("select[aria-label='Series']").Change(series);
        page.WaitForAssertion(() =>
            Assert.Contains(page.FindAll(".set-tab"), t => t.TextContent.Contains(set)));

        await page.InvokeAsync(() =>
            page.FindAll(".set-tab").First(t => t.TextContent.Contains(set)).Click());
        page.WaitForAssertion(() =>
            Assert.Contains(page.FindAll(".set-tab"),
                            t => t.ClassList.Contains("active") && t.TextContent.Contains(set)));
    }

    /// <summary>Click the nth match of a selector, re-found so the handler is the live one.</summary>
    private static Task ClickAsync(IRenderedComponent<Collection> page, string selector, int index = 0) =>
        page.InvokeAsync(() => page.FindAll(selector).ToArray()[index].Click());

    /// <summary>Click one chip of one chip row.</summary>
    private static Task ChipAsync(IRenderedComponent<Collection> page, int row, int index) =>
        page.InvokeAsync(() => page.FindAll(".chip-toolbar").ToArray()[row]
                                   .QuerySelectorAll(".chip-tog").ToArray()[index].Click());

    /// <summary>How many cards the bar says are listed.</summary>
    private static int Listed(IRenderedComponent<Collection> page)
    {
        var text = page.Find(".grid-count").TextContent;
        var digits = new string(text.TakeWhile(c => char.IsDigit(c) || c == ' ').ToArray()).Trim();
        return int.Parse(digits);
    }

    [Fact]
    public async Task The_missing_filter_is_on_the_always_on_bar()
    {
        // On the bar as a button, not only as an option in the select inside the disclosure — which
        // still offers all six filters and still calls this one the same thing.
        var page = await PageAsync();

        Assert.Contains(page.FindAll(".grid-toolbar button"),
                        b => b.TextContent.Contains("missing only"));

        // A collection that owns nothing has nothing missing to hide, so the filter would pass this
        // test by doing nothing at all. Own one card first.
        var owned = Session.Index.All.First(c => c.Set == "B4");
        Session.Adjust([owned.OwnershipKey], 1);
        page.Render();

        var all = Listed(page);

        await ClickAsync(page, ".grid-toolbar button[aria-pressed]");
        page.WaitForAssertion(() => Assert.True(Listed(page) < all,
            $"missing-only listed {Listed(page)} of {all}"));

        // Two-state: pressing again is the way back, not a third hidden state.
        await ClickAsync(page, ".grid-toolbar button[aria-pressed]");
        page.WaitForAssertion(() => Assert.Equal(all, Listed(page)));
    }

    [Fact]
    public async Task The_layout_switch_is_on_the_always_on_bar()
    {
        var page = await PageAsync();

        var bar = page.Find(".grid-toolbar");
        Assert.Contains("grid", bar.TextContent);
        Assert.Contains("list", bar.TextContent);
        Assert.NotNull(page.Find(".grid-toolbar .layout-switch"));
    }

    [Fact]
    public async Task Rarity_chips_select_more_than_one_rung()
    {
        // The dropdown this replaced could hold exactly one rarity, which is the wrong shape for
        // "the stars and the crown, never mind the diamonds".
        var page = await PageAsync();

        var everything = Listed(page);
        var chips = page.FindAll(".chip-toolbar").ToArray()[0].QuerySelectorAll(".chip-tog");
        Assert.True(chips.Length >= 4, $"only {chips.Length} rarity chips");

        await ChipAsync(page, 0, 0);
        var one = Listed(page);

        await ChipAsync(page, 0, 1);
        var two = Listed(page);

        Assert.True(one < everything, "one rarity narrowed nothing");
        Assert.True(two > one, $"a second rarity did not widen the list: {two} vs {one}");

        // And the bar reports the filter, since the count means something different with it on.
        Assert.Contains("2 rarities", page.Find(".grid-toolbar").TextContent);
    }

    [Fact]
    public async Task Clearing_the_rarity_chip_restores_everything()
    {
        var page = await PageAsync();
        var everything = Listed(page);

        await ChipAsync(page, 0, 0);
        Assert.True(Listed(page) < everything);

        await ClickAsync(page, ".grid-toolbar .chip-filter");
        page.WaitForAssertion(() => Assert.Equal(everything, Listed(page)));
    }

    [Fact]
    public async Task A_type_filter_exists_and_covers_the_energies_and_trainer_kinds()
    {
        // There was no type filter at all before, and the pips are the same ones a list row draws.
        var page = await PageAsync();

        var types = page.FindAll(".chip-toolbar").ToArray()[1].QuerySelectorAll(".chip-tog");
        Assert.Equal(14, types.Length);
    }

    [Fact]
    public async Task The_pack_filter_is_the_wrappers()
    {
        // Genetic Apex is the set with three packs, so it is the one where the control has to be
        // more than a single button.
        var page = await PageAsync();

        // The picker opens on the newest series, so this has to walk back to A before A1 exists.
        await ChooseAsync(page, "A", "A1");
        page.WaitForAssertion(() => Assert.True(page.FindAll(".pack-pick").Count >= 2,
            $"only {page.FindAll(".pack-pick").Count} pack chips in A1"));

        Assert.All(page.FindAll(".pack-pick"), p => Assert.NotNull(p.QuerySelector(".pack-thumb")));

        var everything = Listed(page);

        await ClickAsync(page, ".pack-pick");
        page.WaitForAssertion(() => Assert.True(Listed(page) < everything,
            $"pack filter listed {Listed(page)} of {everything}"));

        // Tapping the chosen pack again is how you get out of it.
        await ClickAsync(page, ".pack-pick");
        page.WaitForAssertion(() => Assert.Equal(everything, Listed(page)));
    }

    [Fact]
    public async Task A_heart_makes_the_want_list_and_toggles_a_card_on_it()
    {
        // The whole point of the heart: wanting a card was reachable only from that card's own
        // page, so recording it meant leaving the set you were looking at.
        var page = await PageAsync();

        Assert.Empty(Session.Wishlists);

        // Narrowed to a couple of cards first: the grid is virtualised, and with no browser to
        // report a viewport height it renders a window of nothing until the list is small.
        page.Find(".grid-search").Input("wurmple");

        var hearts = page.FindAll(".card-tile .want");
        Assert.NotEmpty(hearts);
        Assert.All(hearts, h => Assert.Equal("false", h.GetAttribute("aria-pressed")));

        await ClickAsync(page, ".card-tile .want");

        // A list of its own, named and pointed at, without asking for anything.
        var list = Assert.Single(Session.Wishlists);
        Assert.Equal(AppSession.WantListName, list.Name);
        Assert.Equal(list.Id, Session.Profile.WantListId);
        Assert.Single(list.Wanted);

        page.WaitForAssertion(() =>
            Assert.Contains(page.FindAll(".card-tile .want"),
                            h => h.GetAttribute("aria-pressed") == "true"));

        // And off again: one tap is the whole control, in both directions.
        await ClickAsync(page, ".card-tile .want");
        page.WaitForAssertion(() => Assert.Empty(Session.Wishlists[0].Wanted));

        // The list stays. Emptying it is not the same as deleting it, and a heart that destroyed
        // its own list would take the "hearts" mark with it every time you changed your mind.
        Assert.Single(Session.Wishlists);
    }

    [Fact]
    public async Task The_hearts_never_write_into_a_list_you_curated()
    {
        // The reason the hearts have a list of their own. Filling whichever wishlist happened to be
        // first would quietly rewrite the one thing on that page you built deliberately.
        var page = await PageAsync();

        var mine = Session.CreateWishlist("Deck cards");
        page.Render();

        page.Find(".grid-search").Input("wurmple");
        await ClickAsync(page, ".card-tile .want");

        Assert.Empty(Session.Wishlists.First(w => w.Id == mine).Wanted);

        var hearts = Session.Wishlists.First(w => w.Id == Session.Profile.WantListId);
        Assert.NotEqual(mine, hearts.Id);
        Assert.NotEmpty(hearts.Wanted);
    }

    [Fact]
    public async Task Deleting_the_want_list_lets_the_next_heart_make_another()
    {
        // A stored id whose list has gone reads as "no list yet" rather than as a broken pointer,
        // which is what makes the list safe to delete from the wishlists page.
        var page = await PageAsync();

        page.Find(".grid-search").Input("wurmple");
        await ClickAsync(page, ".card-tile .want");

        var first = Session.Profile.WantListId;
        Assert.NotNull(first);

        Session.DeleteWishlist(first!);
        Assert.Empty(Session.Wishlists);

        page.Render();
        page.Find(".grid-search").Input("wurmple");
        await ClickAsync(page, ".card-tile .want");

        var second = Session.Profile.WantListId;
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.NotEmpty(Session.Wishlists.Single().Wanted);
    }

    [Fact]
    public async Task Rows_are_marked_with_their_set_only_where_the_list_spans_sets()
    {
        // The marking is the gate on the strip: the script reads it, so an unmarked grid leaves the
        // strip empty and the stylesheet drops it. The strip element itself is always in the DOM,
        // because the same script publishes where the sticky bars end, which every grid needs.
        var page = await PageAsync();

        await ChooseAsync(page, "A", "A1");
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".grid-spy")));

        // One set: the header two bars up already names it, so nothing is marked and the strip has
        // nothing to say.
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-group]")));

        // Every set: now there is something to report, and every row carries the answer.
        await ChooseAsync(page, "*", "All cards");

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-group]")));

        var groups = page.FindAll("[data-group]")
            .Select(e => e.GetAttribute("data-group"))
            .Where(g => !string.IsNullOrEmpty(g))
            .ToArray();

        Assert.NotEmpty(groups);
        Assert.All(groups, g => Assert.Contains("—", g!));
    }
}
