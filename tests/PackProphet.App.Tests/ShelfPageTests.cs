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
        await page.InvokeAsync(() => order.Change("Name"));

        page.WaitForAssertion(() =>
        {
            var names = page.FindAll(".showcase .nm").Select(n => Flat(n.TextContent)).ToArray();
            Assert.True(names.Length >= 2, "need two decks to tell an order from a coincidence");
            Assert.Equal(names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase), names);
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
