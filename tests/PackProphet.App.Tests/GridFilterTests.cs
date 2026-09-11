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

    /// <summary>
    /// How many cards the bar says are listed. The set bar, with the set name and the percentage —
    /// the total moved there off the grid's own bar, which wraps as soon as a filter is on.
    /// </summary>
    private static int Listed(IRenderedComponent<Collection> page)
    {
        var text = page.Find(".set-picker > summary .tally").TextContent;
        var digits = new string(text.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.Parse(digits);
    }

    [Fact]
    public async Task The_missing_filter_is_in_the_disclosure_and_nowhere_else()
    {
        // One control, in the select with the other five ownership filters. It had a button of its
        // own on the always-on bar as well, which is six controls' worth of bar on a phone and the
        // same state said twice: pressed button, and a chip beside it reading "missing only".
        var page = await PageAsync();

        Assert.DoesNotContain(page.FindAll(".grid-toolbar button"),
                              b => b.TextContent.Contains("Missing Only"));
        Assert.Contains(page.FindAll(".toolbar-more-body option"),
                        o => o.TextContent.Contains("Missing Only"));

        // A collection that owns nothing has nothing missing to hide, so the filter would pass this
        // test by doing nothing at all. Own one card first.
        var owned = Session.Index.All.First(c => c.Set == "B4");
        Session.Adjust([owned.OwnershipKey], 1);
        page.Render();

        var all = Listed(page);

        await page.InvokeAsync(() =>
            page.Find(".toolbar-more-body select").Change("missing"));
        page.WaitForAssertion(() => Assert.True(Listed(page) < all,
            $"missing-only listed {Listed(page)} of {all}"));

        // The chip is the way back, as it is for every other filter.
        await ClickAsync(page, ".chip-filter");
        page.WaitForAssertion(() => Assert.Equal(all, Listed(page)));
    }

    [Fact]
    public async Task The_layout_switch_is_on_the_always_on_bar()
    {
        var page = await PageAsync();

        var bar = page.Find(".grid-toolbar");
        Assert.Contains("Grid", bar.TextContent);
        Assert.Contains("List", bar.TextContent);
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

        Assert.Empty(Session.ChaseLists);

        // Narrowed to a couple of cards first: the grid is virtualised, and with no browser to
        // report a viewport height it renders a window of nothing until the list is small.
        page.Find(".grid-search").Input("wurmple");

        var hearts = page.FindAll(".card-tile .want");
        Assert.NotEmpty(hearts);
        Assert.All(hearts, h => Assert.Equal("false", h.GetAttribute("aria-pressed")));

        await ClickAsync(page, ".card-tile .want");

        // A list of its own, named and pointed at, without asking for anything.
        var list = Assert.Single(Session.ChaseLists);
        Assert.Equal(AppSession.WantListName, list.Name);
        Assert.Equal(list.Id, Session.Profile.WantListId);
        Assert.Single(list.Wanted);

        page.WaitForAssertion(() =>
            Assert.Contains(page.FindAll(".card-tile .want"),
                            h => h.GetAttribute("aria-pressed") == "true"));

        // And off again: one tap is the whole control, in both directions.
        await ClickAsync(page, ".card-tile .want");
        page.WaitForAssertion(() => Assert.Empty(Session.ChaseLists[0].Wanted));

        // The list stays. Emptying it is not the same as deleting it, and a heart that destroyed
        // its own list would take the "hearts" mark with it every time you changed your mind.
        Assert.Single(Session.ChaseLists);
    }

    [Fact]
    public async Task The_hearts_never_write_into_a_list_you_curated()
    {
        // The reason the hearts have a list of their own. Filling whichever chase list happened to be
        // first would quietly rewrite the one thing on that page you built deliberately.
        var page = await PageAsync();

        var mine = Session.CreateChaseList("Deck cards");
        page.Render();

        page.Find(".grid-search").Input("wurmple");
        await ClickAsync(page, ".card-tile .want");

        Assert.Empty(Session.ChaseLists.First(w => w.Id == mine).Wanted);

        var hearts = Session.ChaseLists.First(w => w.Id == Session.Profile.WantListId);
        Assert.NotEqual(mine, hearts.Id);
        Assert.NotEmpty(hearts.Wanted);
    }

    [Fact]
    public async Task Deleting_the_want_list_lets_the_next_heart_make_another()
    {
        // A stored id whose list has gone reads as "no list yet" rather than as a broken pointer,
        // which is what makes the list safe to delete from the chase lists page.
        var page = await PageAsync();

        page.Find(".grid-search").Input("wurmple");
        await ClickAsync(page, ".card-tile .want");

        var first = Session.Profile.WantListId;
        Assert.NotNull(first);

        Session.DeleteChaseList(first!);
        Assert.Empty(Session.ChaseLists);

        page.Render();
        page.Find(".grid-search").Input("wurmple");
        await ClickAsync(page, ".card-tile .want");

        var second = Session.Profile.WantListId;
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.NotEmpty(Session.ChaseLists.Single().Wanted);
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
        Assert.All(groups, g => Assert.Contains(": ", g!));
    }

    [Fact]
    public async Task The_strip_carries_the_sets_logo_beside_its_name()
    {
        // The wordmark, not a booster: it is what the log screen and the set tabs both use, and one
        // set with two different pictures in three places is three things to learn instead of one.
        var page = await PageAsync();
        await ChooseAsync(page, "*", "All cards");

        // Two nodes, so the script can write a background-image on one and a text node on the other
        // without ever assembling markup out of data.
        var strip = page.Find(".grid-spy");
        Assert.NotNull(strip.QuerySelector("[data-spy-art]"));
        Assert.NotNull(strip.QuerySelector("[data-spy-text]"));

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-group-art]")));

        var art = page.FindAll("[data-group-art]")
            .Select(e => e.GetAttribute("data-group-art"))
            .Where(a => !string.IsNullOrEmpty(a))
            .ToArray();

        Assert.NotEmpty(art);
        Assert.All(art, a => Assert.Contains("LOGO_expansion_", a!));
    }

    [Fact]
    public async Task One_set_in_scope_marks_neither_the_name_nor_the_logo()
    {
        // The marking is the gate on the whole strip, and both halves are gated together: art on a
        // row the strip will never read from is bytes in the DOM for nothing.
        var page = await PageAsync();
        await ChooseAsync(page, "A", "A1");

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-group]")));
        Assert.Empty(page.FindAll("[data-group-art]"));
    }
}
