namespace PackProphet.App.Tests;

using PackProphet.Components;
using PackProphet.Domain;
using PackProphet.Pages;
using PackProphet.Services;

/// <summary>
/// The tile: art in a box of its own, and the three controls in a caption strip under it.
///
/// They were badges over the artwork — count, info, heart — and three of them covered three
/// corners of the only thing the grid layout exists to show. The structure is what these assert,
/// because it is the structure that the stylesheet's two layouts both hang off: on a pointer the
/// caption is a row below the art, on a touch screen it is absolutely positioned back over it with
/// only the count showing.
/// </summary>
public class TileCaptionTests : AppHost
{
    private IRenderedComponent<CardTile> Tile(PocketCard card, int count = 0, bool want = false) =>
        RenderComponent<CardTile>(p =>
        {
            p.Add(t => t.Card, card);
            p.Add(t => t.Count, count);
            if (want) p.Add(t => t.OnWant, _ => { });
        });

    private async Task<PocketCard> AnyCardAsync()
    {
        await ReadyAsync();
        return Session.Index.All.First(c => !c.IsPromo);
    }

    [Fact]
    public async Task The_art_is_a_box_of_its_own_and_holds_only_the_image()
    {
        // The aspect ratio, the clip and the gold flair live on this element. Left on the tile they
        // would have squashed the picture by the height of the caption.
        var tile = Tile(await AnyCardAsync());

        var art = tile.Find(".card-tile .art");
        Assert.NotNull(art.QuerySelector("img[data-src]"));

        // Nothing else inside it: the spinner is not furniture, it stands in for the image.
        Assert.Empty(art.QuerySelectorAll(".cnt, .info, .want"));
    }

    [Fact]
    public async Task The_caption_carries_the_heart_the_name_and_the_count()
    {
        var card = await AnyCardAsync();
        var tile = Tile(card, count: 3, want: true);

        var cap = tile.Find(".card-tile .cap");
        Assert.NotNull(cap.QuerySelector("button.want"));
        Assert.Equal("3", cap.QuerySelector(".cnt")!.TextContent.Trim());

        // The link is the card's NAME, not the word "info". Below the art those six characters
        // labelled a thing that already has a label, and the printed name on a 200px tile is small,
        // stylised and sometimes behind an ex badge.
        var link = cap.QuerySelector("a.info")!;
        Assert.Equal(card.Name, link.TextContent.Trim());
        Assert.Contains(card.Name, link.GetAttribute("aria-label")!);
    }

    [Fact]
    public async Task The_count_keeps_its_slot_when_there_is_nothing_to_count()
    {
        // A virtualised grid needs every row to be the same height. A caption that collapsed on the
        // cards you do not own — which is most of them — would make the scrollbar lie about how long
        // the list is, so the slot stays and the stylesheet hides its contents.
        var tile = Tile(await AnyCardAsync(), count: 0, want: true);

        var cnt = tile.Find(".card-tile .cap .cnt");
        Assert.Contains("none", cnt.ClassList);
        Assert.Equal("", cnt.TextContent.Trim());
    }

    [Fact]
    public async Task The_heart_is_absent_where_the_page_has_no_want_handler()
    {
        // The log screen, and any picker: a tap there means something other than owning, so there is
        // no list for a heart to write to.
        var tile = Tile(await AnyCardAsync(), want: false);

        Assert.Empty(tile.FindAll(".want"));
        Assert.NotNull(tile.Find(".cap"));
    }

    [Fact]
    public async Task Every_image_carries_a_stand_in_for_while_it_loads()
    {
        // The loader holds src back until a slot is free, and an img with an alt and no src is drawn
        // by the browser as its broken-image marker — so a fast scroll showed a fault on every tile
        // it had not reached yet. The stylesheet swaps in the spinner off the image's own
        // data-state; all this asserts is that there is something for it to swap to.
        var tile = Tile(await AnyCardAsync());

        var art = tile.Find(".card-tile .art");
        Assert.NotNull(art.QuerySelector(".art-wait"));

        // No src of its own: the loader owns that attribute, and a src here would defeat the cap on
        // in-flight requests that the loader exists to enforce.
        Assert.Null(art.QuerySelector("img")!.GetAttribute("src"));
    }

    [Fact]
    public async Task A_tile_whose_art_never_arrives_shows_the_card_number_instead()
    {
        // A set is playable in-game days before the community CDN has scanned its cards, so on a
        // brand-new set every tile fails at once. The set and number stand in — the same pair the
        // game prints in the card's own corner — and the stylesheet reveals them off the image's
        // data-state. This asserts the text is there to reveal, and that it names THIS card.
        var card = await AnyCardAsync();
        var tile = Tile(card);

        var placeholder = tile.Find(".card-tile .art .art-none");

        // Attributes rather than child elements: the stylesheet draws both lines from these, so
        // one element carries what two spans used to on every tile of a couple-hundred-tile grid.
        Assert.Equal(card.Set, placeholder.GetAttribute("data-set"));
        Assert.Equal(card.Number.ToString(), placeholder.GetAttribute("data-nr"));

        // The tile's own label already names the card, so a screen reader reading the id after it
        // would be repeating an identifier nobody asked for.
        Assert.Equal("true", placeholder.GetAttribute("aria-hidden"));
    }

    [Fact]
    public async Task The_list_row_gets_no_placeholder_because_it_already_prints_the_id()
    {
        // Deliberately absent rather than overlooked: the row carries the same set and number in
        // its own cell a few pixels to the right, and the thumbnail is 32px.
        await ReadyAsync();
        var grid = RenderComponent<CardGrid>(p =>
        {
            p.Add(g => g.Cards, Session.Index.All.Take(4).ToArray());
            p.Add(g => g.CountOf, _ => 0);
            p.Add(g => g.ListView, true);
        });

        Assert.Empty(grid.FindAll(".card-line .art-none"));
        Assert.NotEmpty(grid.FindAll(".card-line .c-id"));
    }

    [Fact]
    public async Task The_list_row_thumbnail_has_the_same_stand_in()
    {
        await ReadyAsync();
        var grid = RenderComponent<CardGrid>(p =>
        {
            p.Add(g => g.Cards, Session.Index.All.Take(4).ToArray());
            p.Add(g => g.CountOf, _ => 0);
            p.Add(g => g.ListView, true);
        });

        Assert.NotEmpty(grid.FindAll(".card-line .c-thumb .art-wait"));
    }

    [Fact]
    public async Task The_card_page_can_start_the_want_list_when_there_is_not_one()
    {
        // On a phone the tile hearts are gone, so this is the only route to wanting a card. "No
        // chase lists yet" must not be the reason there is no way to say so.
        await ReadyAsync();
        var card = Session.Index.All.First(c => !c.IsPromo);

        Assert.Empty(Session.ChaseLists);

        var page = RenderComponent<CardDetail>(p => p.Add(c => c.Key, card.Key));
        var heart = page.Find(".want-heart");
        Assert.Equal("false", heart.GetAttribute("aria-pressed"));

        page.Find(".want-heart").Click();

        var list = Assert.Single(Session.ChaseLists);
        Assert.Equal(AppSession.WantListName, list.Name);
        Assert.Equal(list.Id, Session.Profile.WantListId);
        page.WaitForAssertion(() => Assert.Equal("true", page.Find(".want-heart").GetAttribute("aria-pressed")));
    }

    [Fact]
    public async Task The_want_list_is_not_offered_twice_on_the_card_page()
    {
        // Two controls for one list, one meaning "on or off" and the other "one more", is how you
        // get a heart that appears to do nothing.
        await ReadyAsync();
        var card = Session.Index.All.First(c => !c.IsPromo);

        Session.ToggleWanted(card.OwnershipKey);
        Session.CreateChaseList("Deck cards");

        var page = RenderComponent<CardDetail>(p => p.Add(c => c.Key, card.Key));
        var buttons = page.FindAll(".want-row .want-pair button").Select(b => b.TextContent.Trim()).ToArray();

        Assert.Contains(buttons, b => b.StartsWith("Deck cards", StringComparison.Ordinal));
        Assert.DoesNotContain(buttons, b => b.StartsWith(AppSession.WantListName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_heart_is_reachable_from_the_keyboard()
    {
        // Everything in a tile is tabindex="-1" -- a focus stop per card would put thousands of them
        // between this grid and the rest of the page -- so the grid's contract is that the cursor
        // plus a letter does whatever a tile's furniture does. Adding a control without adding its
        // key would have made the heart a mouse-only feature.
        await ReadyAsync();
        var cards = Session.Index.All.Take(3).ToArray();

        var grid = RenderComponent<CardGrid>(p =>
        {
            p.Add(g => g.Cards, cards);
            p.Add(g => g.CountOf, _ => 0);
            p.Add(g => g.WantedOf, c => Session.WantedOnHeartList(c.OwnershipKey));
            p.Add(g => g.OnWant, (PocketCard c) => Session.ToggleWanted(c.OwnershipKey));
        });

        // The key legend has to advertise it, or it is a secret.
        Assert.Contains("want it", grid.Find(".kb-hint").TextContent);

        await grid.InvokeAsync(() => grid.Instance.KeyWant(1));

        var list = Assert.Single(Session.ChaseLists);
        Assert.True(list.Wanted.ContainsKey(cards[1].OwnershipKey));

        // And back off again, like the button.
        await grid.InvokeAsync(() => grid.Instance.KeyWant(1));
        Assert.Empty(Session.ChaseLists[0].Wanted);
    }

    [Fact]
    public async Task The_want_key_does_nothing_where_there_is_no_list_to_write_to()
    {
        // The log screen passes no handler. A key that threw there would take the render loop with
        // it, and one that silently created a chase list would be worse.
        await ReadyAsync();

        var grid = RenderComponent<CardGrid>(p =>
        {
            p.Add(g => g.Cards, Session.Index.All.Take(3).ToArray());
            p.Add(g => g.CountOf, _ => 0);
        });

        await grid.InvokeAsync(() => grid.Instance.KeyWant(0));

        Assert.Empty(Session.ChaseLists);
        Assert.DoesNotContain("want it", grid.Find(".kb-hint").TextContent);
    }

    [Fact]
    public async Task The_want_column_header_is_named_for_a_screen_reader()
    {
        // Blank on screen -- a visible "want" over a column of hearts explains an icon that already
        // says it -- but a column header with no accessible name at all is announced as nothing.
        await ReadyAsync();

        var grid = RenderComponent<CardGrid>(p =>
        {
            p.Add(g => g.Cards, Session.Index.All.Take(3).ToArray());
            p.Add(g => g.CountOf, _ => 0);
            p.Add(g => g.ListView, true);
            p.Add(g => g.OnWant, (PocketCard _) => { });
        });

        var header = grid.Find(".card-line.head .c-want");
        Assert.Equal("Want", header.TextContent.Trim());
        Assert.NotNull(header.QuerySelector(".visually-hidden"));
    }

    [Fact]
    public async Task How_to_get_it_is_addressable_so_it_can_span_the_width_on_a_tablet()
    {
        // Two columns at tablet widths puts the third child on a second row — in column one, the
        // 12rem art column, where a four-route table was rendered 192px wide. The stylesheet spans
        // it across both; this is the hook that lets it.
        await ReadyAsync();
        var card = Session.Index.All.First(c => !c.IsPromo);

        var page = RenderComponent<CardDetail>(p => p.Add(c => c.Key, card.Key));

        var routes = page.Find(".card-detail .routes");
        Assert.Contains("How to get it", routes.TextContent);
    }
}
