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
    /// How many cards the controls say are listed.
    ///
    /// In the rail's "Showing" heading, which is where the count went when the set bar it used to
    /// sit on was folded into a sheet: it belongs to the group whose filters decide it, and the
    /// whole argument for the rail is that a control can carry its own label.
    /// </summary>
    private static int Listed(IRenderedComponent<Collection> page)
    {
        var text = page.Find(".page-rail .rail-group > .ttl > .cnt").TextContent;
        var digits = new string(text.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.Parse(digits);
    }

    [Fact]
    public async Task The_missing_filter_is_one_control_and_nowhere_else()
    {
        // One control, in the select with the other five ownership filters. It had a button of its
        // own on the always-on bar as well, which is six controls' worth of bar on a phone and the
        // same state said twice: pressed button, and a chip beside it reading "missing only".
        //
        // The shell moved -- the controls are in a rail now rather than on a bar and a disclosure
        // -- and the invariant did not: still one control, wherever the controls live.
        var page = await PageAsync();

        Assert.DoesNotContain(page.FindAll(".page-rail button"),
                              b => b.TextContent.Contains("Missing Only"));
        Assert.Contains(page.FindAll(".page-rail option"),
                        o => o.TextContent.Contains("Missing Only"));

        // A collection that owns nothing has nothing missing to hide, so the filter would pass
        // this test by doing nothing at all. Own one card first.
        //
        // The set is pinned rather than left to the page's own default, which is the newest one
        // and therefore moves under this test every time the snapshot is refreshed. It owned a B4
        // card while the grid had moved on to B4a, so the filter had nothing owned on screen to
        // hide and reported 110 of 110 either way. A1 is the oldest set and will not move.
        await ChooseAsync(page, "A", "A1");

        var owned = Session.Index.All.First(c => c.Set == "A1");
        Session.Adjust([owned.OwnershipKey], 1);
        page.Render();

        var all = Listed(page);

        await page.InvokeAsync(() =>
            page.Find(".page-rail select").Change("missing"));
        page.WaitForAssertion(() => Assert.True(Listed(page) < all,
            $"missing-only listed {Listed(page)} of {all}"));

        // The chip is the way back, as it is for every other filter.
        await ClickAsync(page, ".chip-filter");
        page.WaitForAssertion(() => Assert.Equal(all, Listed(page)));
    }

    [Fact]
    public async Task The_page_stacks_no_more_than_one_bar_of_its_own()
    {
        // The rule the whole shell exists for. This page used to stack a header, the set row, the
        // evolution-gap strip and the grid's toolbar under the global nav -- about 300px before
        // the first card on a phone. One is what is left: the global nav is bar one and this is
        // bar two, and everything else that used to be a strip is in the rail, in a sheet, or in
        // this bar's own sentence.
        var page = await PageAsync();

        Assert.Single(page.FindAll(".page-head"));
        Assert.Empty(page.FindAll(".grid-toolbar"));
        Assert.Empty(page.FindAll(".page-tools"));

        // The set row is a sheet now, opened from the bar rather than sitting under it.
        Assert.Empty(page.FindAll("details.set-picker"));
        var opener = page.Find(".page-head .actions [popovertarget='set-sheet']");
        Assert.Equal("set-sheet", page.Find(".set-sheet").Id);
        Assert.True(page.Find(".set-sheet").HasAttribute("popover"));
        Assert.Contains("Change Set", opener.TextContent);

        // And the gap strip is in the rail rather than being a strip.
        var gaps = page.FindAll(".gap-strip").ToArray();
        if (gaps.Length > 0) Assert.NotNull(gaps[0].Closest(".page-rail"));
    }

    [Fact]
    public async Task The_controls_are_one_set_in_the_page_not_two_copies_by_width()
    {
        // The rail is a column beside the grid above the fold width and a sheet over it below,
        // and it is ONE element carrying popover with the stylesheet deciding which. Rendering it
        // twice and hiding one by width would mean two of every select, two of every generated id,
        // and two controls on one handler -- a pair that agrees until it does not.
        var page = await PageAsync();

        var rails = page.FindAll(".page-rail").ToArray();
        Assert.Single(rails);
        Assert.True(rails[0].HasAttribute("popover"),
            "the rail must carry popover; without it the phone has no sheet to open");

        // And the button that opens it points at that one element.
        var opener = page.Find(".rail-fab.filters");
        Assert.Equal(rails[0].Id, opener.GetAttribute("popovertarget"));
    }

    [Fact]
    public async Task A_tap_changes_nothing_until_you_say_it_should()
    {
        // The grid's whole surface is a control: every tile is a button that silently changes a
        // number read as fact later, with no confirmation and no feedback beyond a badge on a card
        // you have probably scrolled past. Undo does not help, because you have to know it
        // happened. So every visit starts in the state where nothing happens.
        var page = await PageAsync();

        var before = Session.CountOf(Session.Index.All.First(c => c.Set == "A1"));

        var tile = page.Find(".card-tile");
        var key = tile.GetAttribute("id");
        await page.InvokeAsync(() => page.Find($"#{key}").Click());

        Assert.Equal(before, Session.CountOf(Session.Index.All.First(c => c.Set == "A1")));

        // And the grid says so rather than looking armed.
        Assert.Contains("tap-off", page.Find(".grid-wrap").ClassList);
    }

    [Fact]
    public async Task Arming_the_mode_makes_a_tap_count_again()
    {
        // The off state is a default, not a cage: one press and the grid works as it did.
        var page = await PageAsync();

        var adds = page.FindAll(".page-rail .rail-group.mode .btn").ToArray()[1];
        Assert.Equal("Add", adds.TextContent.Trim());

        await page.InvokeAsync(() => adds.Click());

        page.WaitForAssertion(() =>
            Assert.DoesNotContain("tap-off", page.Find(".grid-wrap").ClassList));
    }

    [Fact]
    public async Task A_set_sold_in_one_pack_is_offered_no_pack_filter()
    {
        // A filter that cannot narrow anything is worse than no filter: it looks like a control,
        // it takes a press, and the list it produces is the list that was already there. Most sets
        // have exactly one pack, so this was the ordinary case rather than an edge of it.
        var page = await PageAsync();

        // Whichever set the picker is on, the rule is the same: a pack row exists only where there
        // is a choice in it.
        var picks = page.FindAll(".pack-picks .pack-pick").Count;
        Assert.True(picks != 1, "a pack filter offering one pack narrows nothing");

        // Non-vacuous: drive the picker to the set with the most packs in the fixture and check
        // the row appears there.
        var best = Session.Index.OpenableSets
            .Select(set => (Set: set, Packs: Session.Index.All
                .Where(c => c.Set == set && c.IsPackObtainable)
                .SelectMany(c => c.Packs!).Distinct().Count()))
            .OrderByDescending(x => x.Packs)
            .First();

        if (best.Packs < 2) return;

        await ChooseAsync(page, Session.Sets.SeriesOf(best.Set), best.Set);
        page.WaitForAssertion(() =>
            Assert.Equal(best.Packs, page.FindAll(".pack-picks .pack-pick").Count));
    }

    [Fact]
    public async Task The_add_remove_mode_is_never_inside_the_sheet()
    {
        // The only control in the app that can take a card away. A destructive mode you have to
        // open a sheet to check is a mode you will be wrong about, so on a phone it is a floating
        // button that is on screen the whole time -- outside the rail, not within it.
        var page = await PageAsync();

        var fab = page.Find(".rail-fab.mode");
        Assert.Null(fab.Closest(".page-rail"));

        // The rail's own copy is marked so the sheet can leave it out at that width.
        Assert.NotNull(page.Find(".page-rail .rail-group.mode"));
    }

    [Fact]
    public async Task The_page_bar_says_what_the_filters_are_set_to()
    {
        // The half of the phone treatment that keeps the rest honest. The controls fold into a
        // sheet, and a count whose filters are out of sight is a lie: "110 cards" means something
        // else with "missing only" on. A badge reading "2" says there are filters without saying
        // what they are, which is not the same thing.
        var page = await PageAsync();

        // Nothing applied, nothing claimed.
        Assert.Empty(page.FindAll(".page-head .subtitle .filters"));
        Assert.DoesNotContain("Filters", page.Find(".rail-fab.filters").QuerySelectorAll(".n")
            .Select(n => n.TextContent));

        await page.InvokeAsync(() => page.Find(".page-rail select").Change("missing"));

        page.WaitForAssertion(() =>
        {
            var said = page.Find(".page-head .subtitle .filters").TextContent;
            Assert.Contains("missing only", said);
        });

        // And the count on the button agrees with the words, being built from the same state.
        Assert.Equal("1", page.Find(".rail-fab.filters .n").TextContent.Trim());
    }

    [Fact]
    public async Task The_layout_switch_is_not_behind_a_disclosure()
    {
        // List mode is not a preference among equals: it is the only view carrying a count per
        // row, the set and number, type, rarity and the printed text. Behind a summary reading
        // "filters and layout" it was findable only by someone who already suspected it existed,
        // so it may never go back behind one -- on a bar or in a rail.
        var page = await PageAsync();

        var rail = page.Find(".page-rail");
        Assert.Contains("Grid", rail.TextContent);
        Assert.Contains("List", rail.TextContent);

        Assert.Empty(page.FindAll(".page-rail details"));
        Assert.Null(page.Find(".page-rail").QuerySelector("details"));
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

        // And the controls report the filter, since the count means something different with it
        // on. In the rail the two sit in one group, which is the point of the group.
        Assert.Contains("2 rarities", page.Find(".page-rail").TextContent);
    }

    [Fact]
    public async Task Clearing_the_rarity_chip_restores_everything()
    {
        var page = await PageAsync();
        var everything = Listed(page);

        await ChipAsync(page, 0, 0);
        Assert.True(Listed(page) < everything);

        await ClickAsync(page, ".page-rail .chip-filter");
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
        // Across every set, not whichever one the page opened on: the default is the newest set
        // and Wurmple is not in it, so the search found nothing and the heart there was none to
        // click. Left implicit, these broke on a card snapshot refresh.
        await ChooseAsync(page, "*", "All cards");
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

        // Across every set, not whichever one the page opened on: the default is the newest set
        // and Wurmple is not in it, so the search found nothing and the heart there was none to
        // click. Left implicit, these broke on a card snapshot refresh.
        await ChooseAsync(page, "*", "All cards");
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

        // Across every set, not whichever one the page opened on: the default is the newest set
        // and Wurmple is not in it, so the search found nothing and the heart there was none to
        // click. Left implicit, these broke on a card snapshot refresh.
        await ChooseAsync(page, "*", "All cards");
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
