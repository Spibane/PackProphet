namespace PackProphet.App.Tests;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Web;
using PackProphet.Pages;

/// <summary>
/// Getting out of a card's detail page with the keyboard.
///
/// The back button is early in the tab order but nothing announces it, so Escape is the route
/// covered here.
/// </summary>
public class CardDetailTests : AppHost
{
    private IRenderedComponent<CardDetail> Open(string cardKey) =>
        RenderComponent<CardDetail>(p => p.Add(c => c.Key, cardKey));

    [Fact]
    public async Task Every_route_gets_a_box_including_the_ones_that_do_not_exist()
    {
        // The absence of a route is the answer as often as a price is: "you cannot trade for this"
        // is what you came to find out when the card is a crown. So the closed ones are shown, and
        // shown as dashed boxes with the reason under them -- a table cell for an impossible route
        // is blank, and blank reads as a figure that failed to load.
        await ReadyAsync();
        var card = Session.Index.All.First(c => !c.IsPromo);

        var page = Open(card.Key);
        var boxes = page.FindAll(".box-strip .box").ToArray();

        // Every route the engine prices, none dropped.
        var routes = Session.Routes.For(card, Session.Owned).Options;
        Assert.Equal(routes.Count, boxes.Length);

        // And not one of them is blank: a live box has a figure, a dead one has the dash.
        Assert.All(boxes, b =>
        {
            var fig = b.QuerySelector(".fig")!.TextContent.Trim();
            Assert.False(string.IsNullOrEmpty(fig), "a box with no figure reads as a failed load");
            if (b.ClassList.Contains("dead")) Assert.Equal("\u2014", fig);
        });
    }

    [Fact]
    public async Task A_closed_route_says_why_rather_than_just_going_blank()
    {
        await ReadyAsync();

        // A card with at least one route shut. Every card has one in practice -- a set without
        // published pull rates, or a rarity above the trade limit -- but the fixture picks rather
        // than assumes.
        var card = Session.Index.All
            .FirstOrDefault(c => !c.IsPromo && Session.Routes.For(c, Session.Owned).Options.Any(o => !o.Available));
        Assert.NotNull(card);

        var page = Open(card!.Key);
        var dead = page.FindAll(".box-strip .box.dead").ToArray();
        Assert.NotEmpty(dead);

        Assert.All(dead, b =>
            Assert.False(string.IsNullOrWhiteSpace(b.QuerySelector(".note")?.TextContent),
                "a dashed box has to say what closed the route"));
    }

    [Fact]
    public async Task The_prose_explaining_each_price_is_kept()
    {
        // It was the second column of the table this replaced, and it is what makes a figure mean
        // something: "points accrue while you open, so this is what you give up from a shared
        // per-set budget, not extra packs". Out of the boxes, because five sentences inside five
        // boxes is a wall in the one place on the page meant to be read at a glance -- but not
        // gone.
        await ReadyAsync();
        var card = Session.Index.All.First(c => !c.IsPromo);

        var notes = Session.Routes.For(card, Session.Owned).Options
            .Where(o => o.Available && o.Note is { Length: > 0 })
            .ToArray();
        Assert.NotEmpty(notes);

        var page = Open(card.Key);
        var rendered = page.Find(".route-notes").TextContent;

        Assert.All(notes, n => Assert.Contains(n.Note!, rendered));
    }

    [Fact]
    public async Task Escape_goes_back_to_the_list()
    {
        await ReadyAsync();
        var card = Session.Index.All.First(c => !c.IsPromo);

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("collection");

        var page = Open(card.Key);
        page.Find(".detail-keys").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.EndsWith("collection", nav.Uri);
    }

    [Fact]
    public async Task Other_keys_are_left_alone()
    {
        // The page is only listening for one key. Swallowing others would break typing in the
        // count field that sits on this page too.
        await ReadyAsync();
        var card = Session.Index.All.First(c => !c.IsPromo);

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"card/{card.Key}");
        var before = nav.Uri;

        var page = Open(card.Key);
        foreach (var key in new[] { "a", "Enter", "ArrowDown", "Backspace" })
            page.Find(".detail-keys").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal(before, nav.Uri);
    }

    [Fact]
    public async Task The_back_button_says_that_Escape_works()
    {
        // The shortcut is announced on the page, so it can be found.
        await ReadyAsync();
        var card = Session.Index.All.First(c => !c.IsPromo);

        var page = Open(card.Key);
        var back = page.Find("button.back");

        Assert.Contains("Escape", back.GetAttribute("title")!);
    }
}
