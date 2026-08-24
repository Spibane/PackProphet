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
        var back = page.FindAll("button").First(b => b.TextContent.Contains("back"));

        Assert.Contains("Escape", back.GetAttribute("title")!);
    }
}
