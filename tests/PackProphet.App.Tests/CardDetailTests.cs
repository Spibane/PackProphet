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
    public async Task A_card_opens_at_the_top_of_itself()
    {
        // Blazor routes in place and nothing resets the scroll, so this page inherited whatever
        // offset the page before it was at. Coming from a scrolled card grid -- far taller than
        // one card -- the browser clamped that offset to this page's own maximum and the card
        // opened at its bottom. Measured at 375px: the grid at 1400 of 6315, this page 1475
        // tall, and it opened at 663, which is 1475 less the 812 viewport.
        await ReadyAsync();

        var key = Session.Index.All[0].Key;
        var page = Open(key);

        page.WaitForAssertion(() => Assert.Single(
            JSInterop.Invocations.Where(i => i.Identifier == "ppScroll.toPageTop")));
    }

    [Fact]
    public async Task Moving_from_one_card_to_another_goes_to_the_top_again()
    {
        // This page navigates to itself -- the reprints and the evolution line are links to other
        // cards -- and those arrive as a parameter change rather than a fresh component, so a
        // guard keyed on first render would fire once and never again.
        await ReadyAsync();

        var page = Open(Session.Index.All[0].Key);
        page.WaitForAssertion(() => Assert.NotEmpty(
            JSInterop.Invocations.Where(i => i.Identifier == "ppScroll.toPageTop")));

        page.SetParametersAndRender(p => p.Add(c => c.Key, Session.Index.All[1].Key));

        page.WaitForAssertion(() => Assert.Equal(
            2, JSInterop.Invocations.Count(i => i.Identifier == "ppScroll.toPageTop")));
    }

    [Fact]
    public async Task Re_rendering_the_same_card_does_not_move_the_window()
    {
        // Ticking the count re-renders this page, and a reset on every render would drag someone
        // reading the attack text back to the top each time they pressed plus.
        await ReadyAsync();

        var page = Open(Session.Index.All[0].Key);
        page.WaitForAssertion(() => Assert.Single(
            JSInterop.Invocations.Where(i => i.Identifier == "ppScroll.toPageTop")));

        page.Render();
        page.Render();

        Assert.Single(JSInterop.Invocations.Where(i => i.Identifier == "ppScroll.toPageTop"));
    }

    [Fact]
    public async Task The_art_opens_itself_larger()
    {
        // The art is the one thing on this page you might want to look at rather than read, and at
        // 15rem the printed text on it is not legible. Pressing it opens the same art over the
        // page; a button rather than a click handler on an image, so the keyboard can reach it.
        await ReadyAsync();
        var card = Session.Index.All.First(c => !c.IsPromo);

        var page = Open(card.Key);

        var art = page.Find(".card-detail .big-art");
        Assert.Equal("BUTTON", art.TagName);
        Assert.Equal("card-zoom", art.GetAttribute("popovertarget"));
        Assert.False(string.IsNullOrWhiteSpace(art.GetAttribute("aria-label")));

        // The card is named once, by the page's own h1. role="img" with the name on it as well
        // made a screen reader read it twice.
        Assert.Null(art.GetAttribute("role"));

        var zoom = page.Find("#card-zoom");
        Assert.NotNull(zoom.GetAttribute("popover"));

        // Outside the div that answers Escape by navigating back. Inside it, one Escape would
        // close this AND leave the page.
        Assert.Null(zoom.Closest(".detail-keys"));

        // The same art, and a way out that is the whole picture rather than a glyph in a corner.
        var shown = zoom.QuerySelector(".zoom-art")!;
        Assert.Equal(art.GetAttribute("style"), shown.GetAttribute("style"));
        Assert.Equal("card-zoom", shown.GetAttribute("popovertarget"));
        Assert.Equal("hide", shown.GetAttribute("popovertargetaction"));
        Assert.False(string.IsNullOrWhiteSpace(shown.GetAttribute("aria-label")));
    }

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

        var routes = Session.Routes.For(card, Session.Owned);
        var notes = routes.Options
            .Where(o => o.Available && o.Note is { Length: > 0 })
            .ToArray();
        Assert.NotEmpty(notes);

        var page = Open(card.Key);

        // Every one of them is on the page. The cheapest route's is the sentence under the display
        // figure, because that is the price it explains; the rest are the list below the strip.
        // Printing the cheapest one in both places reads as two different facts about one price.
        Assert.All(notes, n => Assert.Contains(n.Note!, page.Markup));

        if (routes.Cheapest?.Note is { Length: > 0 } lead)
        {
            Assert.Contains(lead, page.Find(".verdict-lead .says").TextContent);
            Assert.DoesNotContain(lead, page.FindAll(".route-notes").Count == 0
                ? "" : page.Find(".route-notes").TextContent);
        }
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
