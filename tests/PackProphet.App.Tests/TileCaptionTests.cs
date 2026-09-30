namespace PackProphet.App.Tests;

using PackProphet.Components;
using PackProphet.Domain;
using PackProphet.Pages;
using PackProphet.Services;
using PackProphet.Text;

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
    public async Task The_art_is_a_box_of_its_own_and_holds_the_image_and_one_badge()
    {
        // The aspect ratio, the clip and the gold flair live on this element. Left on the tile they
        // would have squashed the picture by the height of the caption.
        var tile = Tile(await AnyCardAsync(), count: 3);

        var art = tile.Find(".card-tile .art");
        Assert.NotNull(art.QuerySelector("img[data-src]"));

        // The count, and nothing else. Three badges covered three corners of the picture you are
        // looking at in order to recognise a card, which is why they left; one is a different
        // proposition, and a quantity is the thing a text caption cannot make read across six
        // columns. So the count comes back and the other two stay in the caption.
        Assert.NotNull(art.QuerySelector(".cnt"));
        Assert.Empty(art.QuerySelectorAll(".info, .want, .no, .rr"));
    }

    [Fact]
    public async Task The_caption_carries_the_number_the_name_and_the_heart()
    {
        var card = await AnyCardAsync();
        var tile = Tile(card, count: 3, want: true);

        var cap = tile.Find(".card-tile .cap");

        // The printed number leads it. The grid is in number order by default, so "I am looking
        // for 143" is answerable by eye only if the numbers are on screen; it used to be in the
        // tooltip and the accessible label and nowhere a sighted user could read without hovering.
        Assert.Equal(card.Number.ToString(), cap.QuerySelector(".no")!.TextContent.Trim());

        // The link is the card's NAME, not the word "info". Below the art those six characters
        // labelled a thing that already has a label, and the printed name on a 200px tile is small,
        // stylised and sometimes behind an ex badge.
        var link = cap.QuerySelector("a.info")!;
        Assert.Equal(card.Name, link.TextContent.Trim());
        Assert.Contains(card.Name, link.GetAttribute("aria-label")!);

        // The heart is last, after the facts, because it is the one control among them.
        Assert.NotNull(cap.QuerySelector("button.want"));

        // And the count is NOT here any more: it is a badge on the art.
        Assert.Null(cap.QuerySelector(".cnt"));
    }

    [Fact]
    public async Task The_count_badge_is_absent_where_there_is_nothing_to_count()
    {
        // It kept an empty slot while it was in the caption, because a virtualised grid needs every
        // row the same height and a strip that collapsed on the cards you do not own -- which is
        // most of them -- would make the scrollbar lie. On the art it is out of the flow, so there
        // is no row height to keep and an unowned card simply has no badge.
        var tile = Tile(await AnyCardAsync(), count: 0, want: true);

        var cnt = tile.Find(".card-tile .cnt");
        Assert.Contains("none", cnt.ClassList);
        Assert.Equal("", cnt.TextContent.Trim());

        // The caption is still there and still the same shape, which is what the row height
        // actually depends on now.
        Assert.NotNull(tile.Find(".card-tile .cap .no"));
    }

    [Fact]
    public async Task The_rarity_is_in_the_caption_where_the_grid_supplies_a_ladder()
    {
        var card = await AnyCardAsync();
        var rung = Session.Index.Rung(card);
        Assert.NotNull(rung);

        var withLadder = RenderComponent<CardTile>(p =>
        {
            p.Add(t => t.Card, card);
            p.Add(t => t.Rung, rung);
        });

        // The glyphs the game prints, from the same rungs the list view and the filter chips draw,
        // so a chip and a card agree on what a rarity looks like.
        var rr = withLadder.Find(".card-tile .cap .rr");
        Assert.Equal(rung!.Glyphs, rr.TextContent.Trim());
        Assert.Contains(rung.GlyphClass, rr.ClassList);

        // And omitted rather than left as an empty slot on a grid given no ladder -- the picker
        // screens pass no RungOf at all.
        var withoutLadder = Tile(card);
        Assert.Empty(withoutLadder.FindAll(".rr"));
        Assert.NotNull(withoutLadder.Find(".card-tile .cap"));
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
    public async Task A_picked_row_says_so_across_its_whole_width()
    {
        // The "+N" badge sits in one column of a row that can be 1600px wide, so on a wide screen
        // working out which card it belonged to meant tracking back along the row. The class is
        // what the row tint hangs off; the badge stays for the exact figure.
        await ReadyAsync();
        var cards = Session.Index.All.Take(4).ToArray();

        var grid = RenderComponent<CardGrid>(p =>
        {
            p.Add(g => g.Cards, cards);
            p.Add(g => g.CountOf, _ => 0);
            p.Add(g => g.ListView, true);
            p.Add(g => g.PickedOf, c => c.Key == cards[1].Key ? 2 : 0);
        });

        var rows = grid.FindAll(".card-line[role=row]:not(.head)").ToArray();
        Assert.DoesNotContain("picked", rows[0].GetAttribute("class")!);
        Assert.Contains("picked", rows[1].GetAttribute("class")!);
        Assert.Contains("+2", rows[1].QuerySelector(".c-pick")!.TextContent);
    }

    [Fact]
    public async Task Both_layouts_hand_the_loader_the_whole_art_chain()
    {
        // js/imgloader.js walks data-src-alt when data-src fails, which is what stops the newest
        // set -- data published, art not yet -- from drawing as a grid of placeholders. A layout
        // that emitted only data-src would look completely normal until a set gapped.
        await ReadyAsync();
        var card = Session.Index.All.First();

        var tile = Tile(card);
        Assert.Equal(card.ArtFallbackUrls,
                     tile.Find("img[data-src]").GetAttribute("data-src-alt"));

        var grid = RenderComponent<CardGrid>(p =>
        {
            p.Add(g => g.Cards, new[] { card });
            p.Add(g => g.CountOf, _ => 0);
            p.Add(g => g.ListView, true);
        });

        Assert.Equal(card.ArtFallbackUrls,
                     grid.Find(".card-line .thumb").GetAttribute("data-src-alt"));

        // Not empty, or the assertions above would pass on a card with no fallbacks at all.
        Assert.Contains("cdn.jsdelivr.net", card.ArtFallbackUrls);
    }

    [Fact]
    public async Task A_dual_typed_card_stacks_its_pips_without_widening_the_column()
    {
        // Dual-typed Pokémon arrive in October. Side by side they do not fit: the column is 5.4rem
        // for one pip and one word, and two of each in a row needs about eleven -- which would come
        // out of the card name, the thing a list is scanned for. Stacked, they cost no width at all.
        await ReadyAsync();
        var cards = Session.Index.All.Take(2).ToArray();

        var grid = RenderComponent<CardGrid>(p =>
        {
            p.Add(g => g.Cards, cards);
            p.Add(g => g.CountOf, _ => 0);
            p.Add(g => g.ListView, true);
            p.Add(g => g.TypeOf, c => c.Key == cards[0].Key
                ? new[] { "Grass", "Water" }
                : new[] { "Fire" });
        });

        var rows = grid.FindAll(".card-line[role=row]:not(.head)").ToArray();

        var dual = rows[0].QuerySelector(".ty-stack")!;
        Assert.Contains("dual", dual.GetAttribute("class")!);
        Assert.Equal(2, dual.QuerySelectorAll(".ty-one").Length);
        Assert.Equal(2, dual.QuerySelectorAll(".e").Length);
        Assert.Equal(["Grass", "Water"],
                     dual.QuerySelectorAll(".ty-name").Select(n => n.TextContent.Trim()));

        // The single-typed row keeps exactly what it had: one pip, one word, and no dual class --
        // which is what every card printed so far renders as.
        var single = rows[1].QuerySelector(".ty-stack")!;
        Assert.DoesNotContain("dual", single.GetAttribute("class")!);
        Assert.Single(single.QuerySelectorAll(".ty-one"));
        Assert.Equal("Fire", single.QuerySelector(".ty-name")!.TextContent.Trim());
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
    public async Task The_card_page_leads_with_how_to_get_it_not_with_what_is_printed_on_it()
    {
        // It was three columns -- art, printed statistics, routes -- so the answer someone opened
        // this page for was the third of three, and the first two thirds were a picture of the
        // card and a table of the facts printed on that picture.
        //
        // Two columns now, and the order in the second one is the order of the question: what it
        // costs to get, where that came from, then the card itself.
        await ReadyAsync();
        var card = Session.Index.All.First(c => !c.IsPromo);

        var page = RenderComponent<CardDetail>(p => p.Add(c => c.Key, card.Key));

        // The answer is a display line, the same shape every other Answer page leads with, rather
        // than a heading over a strip.
        var lead = page.Find(".card-detail .verdict-lead");
        Assert.Contains("How to Get It", lead.TextContent);
        Assert.False(string.IsNullOrWhiteSpace(lead.QuerySelector(".name")?.TextContent));

        // And it comes first. Compared by position in the markup, because what is being asserted
        // is reading order -- on a phone the columns stack and this is all there is.
        var markup = page.Markup;
        Assert.True(markup.IndexOf("verdict-lead", StringComparison.Ordinal)
                    < markup.IndexOf("fact-row", StringComparison.Ordinal),
            "the printed facts must follow the answer, not precede it");
    }

    [Fact]
    public void A_card_from_a_set_with_no_art_draws_the_placeholder_without_asking()
    {
        // Rendered in the state the loader leaves a tile that has failed everywhere, so the
        // placeholder is there on the first frame and nothing is queued to find it out.
        var card = new PocketCard { Set = "Z9z", Number = 7, Name = "Bulbasaur", Rarity = "C",
                                    Image = "cPK_10_999070_00_FUSHIGIDANE_C.webp" };
        try
        {
            ArtSource.UseMissing(["Z9z"]);
            var img = Tile(card).Find(".art img");

            Assert.Equal("error", img.GetAttribute("data-state"));
            Assert.Contains("img-failed", img.ClassList);
            Assert.Equal("", img.GetAttribute("data-src"));
        }
        finally
        {
            ArtSource.UseMissing(null);
        }

        // And an ordinary card is left for the loader, with no state of its own.
        Assert.Null(Tile(card).Find(".art img").GetAttribute("data-state"));
    }

}
