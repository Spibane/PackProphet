namespace PackProphet.App.Tests;

using PackProphet.Deck;
using PackProphet.Pages;
using PackProphet.State;

/// <summary>
/// The two shelves: decks and chase lists.
///
/// Browse pages, but a shelf of six rather than a grid of hundreds — so the rail shrinks to two
/// groups, which is the argument for a rail over a fixed sidebar. The collection's has seven and
/// this has two, and both are the right size for the page they are on.
/// </summary>
public class ShelfPageTests : AppHost
{
    private static string Flat(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    private void SeedDecks()
    {
        // Deck-builder numbers rather than cards: what these assert is the shelf's shape and its
        // ordering, and neither needs the numbers to resolve to anything.
        var nrs = new List<int> { 1, 2, 3, 4 };

        Session.Mutate(p => p with
        {
            Decks =
            [
                new SavedDeck("d1", "Zapdos beatdown", new List<int>(nrs), [EnergyType.Lightning]),
                new SavedDeck("d2", "Articuno stall", new List<int>(nrs.Take(2)), [EnergyType.Water]),
                new SavedDeck("d3", "Empty shell", [], []),
            ],
        });
    }

    [Fact]
    public async Task The_decks_shelf_carries_two_rail_groups_and_no_bar()
    {
        await ReadyAsync();
        SeedDecks();

        var page = RenderComponent<Decks>();

        // The layout switch was on a bar of its own over the content; the order control never
        // existed at all.
        Assert.Empty(page.FindAll(".grid-toolbar"));
        Assert.Single(page.FindAll(".page-head"));

        var groups = page.FindAll(".page-rail .rail-group").ToArray();
        Assert.Equal(2, groups.Length);

        var headings = groups.Select(g => Flat(g.QuerySelector(".ttl")!.TextContent)).ToArray();
        Assert.Equal(new[] { "Layout", "Order" }, headings);
    }

    [Fact]
    public async Task The_decks_bar_counts_what_you_can_build_not_what_you_have_saved()
    {
        // "6 saved" is a fact about your filing. How many you could build today is what you came to
        // find out, and it is the figure that moves when you open a pack.
        await ReadyAsync();
        SeedDecks();

        var page = RenderComponent<Decks>();
        var subtitle = Flat(page.Find(".page-head .subtitle").TextContent);

        Assert.Contains("saved", subtitle);
        Assert.Contains("buildable today", subtitle);
    }

    [Fact]
    public async Task The_decks_shelf_can_be_ordered_by_something_other_than_buildability()
    {
        // Closest-to-buildable answers "what can I play tonight" and cannot answer "where is the
        // one I made last week". A shelf nobody can sort is fine at six decks and useless at
        // thirty, and nobody's stays at six.
        await ReadyAsync();
        SeedDecks();

        // The grid view, because that is the one whose order is readable off the cards. The table
        // is the same decks in the same order.
        Session.SetDeckGrid(true);

        var page = RenderComponent<Decks>();

        var order = page.Find(".page-rail select[aria-label='Order the decks']");
        var options = order.QuerySelectorAll("option").Select(o => Flat(o.TextContent)).ToArray();

        Assert.Contains("Closest to buildable", options);
        Assert.Contains("By name", options);

        // And it actually reorders: by name puts Articuno before Zapdos, buildability does not.
        // The value is the stored vocabulary rather than the page's own enum -- an order outlives
        // the component that reads it, so Prefs keeps a string.
        await page.InvokeAsync(() => order.Change("name"));

        page.WaitForAssertion(() =>
        {
            var names = page.FindAll(".showcase .nm").Select(n => Flat(n.TextContent)).ToArray();
            Assert.True(names.Length >= 2, "need two decks to tell an order from a coincidence");
            Assert.Equal(names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase), names);
        });
    }

    [Fact]
    public async Task The_shelf_remembers_what_it_was_last_ordered_by()
    {
        // A shelf is somewhere you come back to. The order lived in a field on the page, so every
        // arrival reset it to the default and nobody would have bothered setting it twice.
        await ReadyAsync();
        SeedDecks();

        var page = RenderComponent<Decks>();
        var order = page.Find(".page-rail select[aria-label='Order the decks']");
        await page.InvokeAsync(() => order.Change("name"));

        Assert.Equal("name", Session.DeckOrder);

        // A fresh render of the page is the same thing as coming back to it.
        var again = RenderComponent<Decks>();
        var selected = again.FindAll(".page-rail select[aria-label='Order the decks'] option")
                            .Single(o => o.HasAttribute("selected"));

        Assert.Equal("name", selected.GetAttribute("value"));
    }

    [Fact]
    public async Task A_shelf_can_be_put_in_an_order_of_your_own()
    {
        // The three computed orders rank decks by a property. "These two first because I am playing
        // them this week" is not a property of a deck, so it cannot be a sort — it is an
        // arrangement, and the arrangement is the order the profile stores them in.
        await ReadyAsync();
        SeedDecks();

        var page = RenderComponent<Decks>();
        var order = page.Find(".page-rail select[aria-label='Order the decks']");
        await page.InvokeAsync(() => order.Change("custom"));

        // A grip per deck, and nothing to grip in any other order.
        page.WaitForAssertion(() =>
            Assert.Equal(Session.Profile.Decks.Count, page.FindAll("[data-drag-handle]").Count));

        var stored = Session.Profile.Decks.Select(d => d.Name).ToArray();
        var shown = page.FindAll("tbody [data-drag-id]")
                        .Select(r => Flat(r.QuerySelector("td a strong")!.TextContent))
                        .ToArray();
        Assert.Equal(stored, shown);

        // The last deck moved to the front, which is what a drop at index 0 means.
        var last = Session.Profile.Decks[^1];
        Session.MoveDeck(last.Id, 0);

        Assert.Equal(last.Id, Session.Profile.Decks[0].Id);
        Assert.Equal(stored.Length, Session.Profile.Decks.Count);

        page.WaitForAssertion(() => Assert.Equal(
            last.Name,
            Flat(page.FindAll("tbody [data-drag-id] td a strong").First().TextContent)));

        await page.InvokeAsync(() => order.Change("name"));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-drag-handle]")));
    }

    [Fact]
    public async Task Reordering_never_loses_or_duplicates_an_item()
    {
        // The move is a removal and an insertion, and an index counted against the list BEFORE the
        // removal is off by one for every move down the shelf. Whatever it does to the order, it
        // has to leave the same decks on it.
        await ReadyAsync();
        SeedDecks();

        var ids = Session.Profile.Decks.Select(d => d.Id).ToArray();
        Assert.True(ids.Length >= 3, "need three decks for a move to be able to land wrong");

        foreach (var to in new[] { 0, 1, ids.Length - 1, ids.Length + 5, -3 })
        {
            Session.MoveDeck(ids[1], to);

            var after = Session.Profile.Decks.Select(d => d.Id).ToArray();
            Assert.Equal(ids.Length, after.Length);
            Assert.Equal(ids.OrderBy(x => x, StringComparer.Ordinal),
                         after.OrderBy(x => x, StringComparer.Ordinal));
        }

        // An id the shelf does not hold changes nothing, rather than throwing or inserting a hole.
        var before = Session.Profile.Decks.Select(d => d.Id).ToArray();
        Session.MoveDeck("no-such-deck", 0);
        Assert.Equal(before, Session.Profile.Decks.Select(d => d.Id).ToArray());
    }

    [Fact]
    public async Task Neither_shelf_badges_the_way_it_is_arranged()
    {
        // The badge on the rail's button answers one question: is something being kept from me.
        // That is why it is a count of FILTERS. Neither control on either shelf filters — an order
        // changes what comes first and a layout changes what a row looks like, and both are
        // legible on the page itself — so the button carried a permanent "1" reporting a setting
        // that was not even off its default.
        await ReadyAsync();
        SeedDecks();

        foreach (var order in new[] { "buildable", "name", "size", "custom" })
        {
            Session.SetDeckOrder(order);

            var page = RenderComponent<Decks>();
            Assert.Empty(page.FindAll(".rail-fab.filters .n"));
        }

        Session.CreateChaseList("Crowns I want");
        Assert.Empty(RenderComponent<ChaseLists>().FindAll(".rail-fab.filters .n"));
    }

    [Fact]
    public async Task The_grip_is_on_the_card_rather_than_in_its_body()
    {
        // It sat in the body under the deck's name, where it read as one more fact about the deck
        // and put the thing you pick the card up by in the middle of the card. It grips the whole
        // card, so it belongs against the card's own edge.
        await ReadyAsync();
        SeedDecks();
        Session.SetDeckGrid(true);
        Session.SetDeckOrder("custom");

        var page = RenderComponent<Decks>();
        var handles = page.FindAll(".showcase [data-drag-handle]");
        Assert.Equal(Session.Profile.Decks.Count, handles.Count);

        Assert.All(handles, h =>
        {
            Assert.True(h.ParentElement!.ClassList.Contains("showcase"),
                "the grip is a child of the card, not of something inside it");
            Assert.Null(h.Closest(".showcase .body"));

            // Drawn rather than typed. The braille grip it replaced is round, is laid out 2 by 4,
            // and is not in every system font.
            Assert.NotNull(h.QuerySelector("svg"));
            Assert.DoesNotContain("\u283f", h.TextContent);
        });
    }

    [Fact]
    public async Task The_chase_shelf_is_the_same_shelf()
    {
        // Same two groups in the same order, because it is the same shelf with different things on
        // it. A user who learned the decks page knows this one.
        await ReadyAsync();

        Session.CreateChaseList("Crowns I want");
        Session.CreateChaseList("Ex cards");

        var page = RenderComponent<ChaseLists>();

        Assert.Empty(page.FindAll(".grid-toolbar"));

        var headings = page.FindAll(".page-rail .rail-group .ttl")
            .Select(t => Flat(t.TextContent))
            .ToArray();

        Assert.Equal(new[] { "Layout", "Order" }, headings);
        Assert.Contains("complete", Flat(page.Find(".page-head .subtitle").TextContent));
    }
}
