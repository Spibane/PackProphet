namespace PackProphet.App.Tests;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PackProphet.Components;

/// <summary>
/// Keyboard operation of the card grid.
///
/// The grid is how a collection is entered, and it was pointer-only: a tile was a div with a click
/// handler, no tabindex, no key handling, no accessible name.
///
/// The cursor itself lives in js/gridkeys.js, so what is checked here is what .NET owns: the grid
/// semantics, the accessible names, and that each key ends up doing what the pointer path does.
/// </summary>
public class GridKeyboardTests : AppHost
{
    private IReadOnlyList<PocketCard> Cards(int n) =>
        Session.Index.All.DistinctBy(c => c.OwnershipKey).Take(n).ToArray();

    private readonly List<(PocketCard Card, int Delta)> _adjusted = [];
    private readonly List<(PocketCard Card, int Count)> _set = [];
    private readonly List<(IReadOnlyList<PocketCard> Cards, int Delta)> _swept = [];

    private IRenderedComponent<CardGrid> Grid(IReadOnlyList<PocketCard> cards, bool allowSet = true)
    {
        var counts = new Dictionary<string, int>();
        return RenderComponent<CardGrid>(p =>
        {
            p.Add(g => g.Cards, cards);
            p.Add(g => g.CountOf, c => counts.GetValueOrDefault(c.OwnershipKey));
            p.Add(g => g.OnAdjust, EventCallback.Factory.Create<(PocketCard, int)>(
                this, a => _adjusted.Add(a)));
            if (allowSet)
                p.Add(g => g.OnSet, EventCallback.Factory.Create<(PocketCard, int)>(
                    this, a => _set.Add(a)));
            p.Add(g => g.OnSweep, EventCallback.Factory.Create<(IReadOnlyList<PocketCard>, int)>(
                this, a => _swept.Add(a)));
            p.Add(g => g.Columns, 4);
        });
    }

    [Fact]
    public async Task The_grid_is_a_grid_and_its_tiles_are_named()
    {
        await ReadyAsync();
        var cards = Cards(8);
        var grid = Grid(cards);

        var wrap = grid.Find(".grid-wrap");
        Assert.Equal("grid", wrap.GetAttribute("role"));
        Assert.False(string.IsNullOrWhiteSpace(wrap.GetAttribute("aria-label")));

        // Rows exist in the markup, so the grid role is honest rather than asserted over a flat
        // pile of cells.
        Assert.NotEmpty(grid.FindAll("[role=row]"));

        var tile = grid.FindAll("[role=gridcell]").First();
        var label = tile.GetAttribute("aria-label")!;

        // Name, provenance, rarity, and how many you hold — the count last, because it is the part
        // that changes.
        Assert.Contains(cards[0].Name, label);
        Assert.Contains(cards[0].Set, label);
        Assert.Contains("none owned", label);
    }

    [Fact]
    public async Task The_name_says_how_many_you_hold()
    {
        await ReadyAsync();
        var cards = Cards(4);
        var counts = new Dictionary<string, int> { [cards[0].OwnershipKey] = 3 };

        var grid = RenderComponent<CardGrid>(p =>
        {
            p.Add(g => g.Cards, cards);
            p.Add(g => g.CountOf, c => counts.GetValueOrDefault(c.OwnershipKey));
            p.Add(g => g.Columns, 4);
        });

        var labels = grid.FindAll("[role=gridcell]").Select(t => t.GetAttribute("aria-label")!).ToArray();

        Assert.Contains(labels, l => l.Contains(cards[0].Name) && l.Contains("3 owned"));
        Assert.Contains(labels, l => l.Contains(cards[1].Name) && l.Contains("none owned"));
    }

    [Fact]
    public async Task Enter_adds_a_copy_and_minus_removes_one()
    {
        await ReadyAsync();
        var cards = Cards(6);
        var grid = Grid(cards);

        await grid.Instance.KeyAdjust(2, +1);
        await grid.Instance.KeyAdjust(2, -1);

        Assert.Equal(2, _adjusted.Count);
        Assert.All(_adjusted, a => Assert.Equal(cards[2].OwnershipKey, a.Card.OwnershipKey));
        Assert.Equal(+1, _adjusted[0].Delta);
        Assert.Equal(-1, _adjusted[1].Delta);
    }

    [Fact]
    public async Task A_keyboard_range_applies_one_delta_to_every_card_in_it()
    {
        // The keyboard equivalent of a drag sweep: gridkeys.js tracks an anchor and a cursor
        // instead of a lo/hi pair, so this is what confirms the two ends still resolve to the
        // same inclusive range a pointer drag would sweep.
        await ReadyAsync();
        var cards = Cards(6);
        var grid = Grid(cards);

        await grid.InvokeAsync(() => grid.Instance.KeyRangeAdjust(1, 4, +1));

        var swept = Assert.Single(_swept);
        Assert.Equal(+1, swept.Delta);
        Assert.Equal(
            cards.Skip(1).Take(4).Select(c => c.OwnershipKey),
            swept.Cards.Select(c => c.OwnershipKey));
        Assert.Empty(_adjusted);   // a range never also fires the single-tile path
    }

    [Fact]
    public async Task A_keyboard_range_does_not_care_which_end_is_the_anchor()
    {
        // Shift+ArrowLeft from a cursor ahead of the anchor sends the pair the other way round;
        // the applied range must come out identical either direction.
        await ReadyAsync();
        var cards = Cards(6);
        var grid = Grid(cards);

        await grid.InvokeAsync(() => grid.Instance.KeyRangeAdjust(4, 1, -1));

        var swept = Assert.Single(_swept);
        Assert.Equal(-1, swept.Delta);
        Assert.Equal(
            cards.Skip(1).Take(4).Select(c => c.OwnershipKey),
            swept.Cards.Select(c => c.OwnershipKey));
    }

    [Fact]
    public async Task A_keyboard_range_reaching_outside_the_list_is_clamped_not_refused()
    {
        // The index comes from a JS-tracked anchor, so it can only ever be stale in the same
        // ordinary way a single cursor index can — clamp to what still exists rather than drop
        // the whole action a scroll or filter change happened to make partly out of range.
        await ReadyAsync();
        var cards = Cards(4);
        var grid = Grid(cards);

        await grid.InvokeAsync(() => grid.Instance.KeyRangeAdjust(-2, 2, +1));

        var swept = Assert.Single(_swept);
        Assert.Equal(cards.Take(3).Select(c => c.OwnershipKey), swept.Cards.Select(c => c.OwnershipKey));
    }

    [Fact]
    public async Task A_keyboard_range_entirely_outside_the_list_does_nothing()
    {
        await ReadyAsync();
        var grid = Grid(Cards(3));

        await grid.InvokeAsync(() => grid.Instance.KeyRangeAdjust(9, 12, +1));

        Assert.Empty(_swept);
    }

    [Fact]
    public async Task A_digit_sets_the_count_outright()
    {
        // The key that makes the keyboard path practical: entering an existing collection otherwise
        // means nine presses of + per card.
        await ReadyAsync();
        var cards = Cards(6);
        var grid = Grid(cards);

        await grid.Instance.KeySet(1, 4);

        var one = Assert.Single(_set);
        Assert.Equal(cards[1].OwnershipKey, one.Card.OwnershipKey);
        Assert.Equal(4, one.Count);
        Assert.Empty(_adjusted);
    }

    [Fact]
    public async Task A_digit_falls_back_to_relative_change_where_the_page_only_takes_that()
    {
        // Log-a-pack accepts "one more came out of this pack", not "I own four" — so a digit there
        // has to become a delta rather than being silently ignored.
        await ReadyAsync();
        var cards = Cards(6);
        var grid = Grid(cards, allowSet: false);

        await grid.Instance.KeySet(0, 3);

        var one = Assert.Single(_adjusted);
        Assert.Equal(3, one.Delta);
        Assert.Empty(_set);
    }

    [Fact]
    public async Task A_digit_beyond_the_copy_limit_is_clamped_not_refused()
    {
        await ReadyAsync();
        var grid = Grid(Cards(3));

        await grid.Instance.KeySet(0, 99);

        Assert.Equal(CardGrid.MaxCopies, Assert.Single(_set).Count);
    }

    [Fact]
    public async Task Keys_aimed_outside_the_list_do_nothing()
    {
        // The index comes from the DOM, and Virtualize destroys tiles as it scrolls, so a stale
        // index reaching .NET is an ordinary race rather than a bug to assume away.
        await ReadyAsync();
        var grid = Grid(Cards(3));

        await grid.Instance.KeyAdjust(-1, 1);
        await grid.Instance.KeyAdjust(99, 1);
        await grid.Instance.KeySet(99, 2);
        await grid.Instance.KeyOpen(99);

        Assert.Empty(_adjusted);
        Assert.Empty(_set);
    }

    [Fact]
    public async Task Every_tile_carries_an_id_so_the_cursor_can_point_at_it()
    {
        // aria-activedescendant names an id, so a tile without one cannot be announced — and two
        // grids on a page must not share ids or the cursor points into the wrong one.
        await ReadyAsync();
        var grid = Grid(Cards(5));

        var ids = grid.FindAll("[role=gridcell]").Select(t => t.Id).ToArray();

        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public async Task The_info_link_is_out_of_the_tab_order()
    {
        // One focus stop per tile would put thousands of them between the grid and whatever comes
        // after it. The keyboard route to detail is `i` on the cursor instead.
        await ReadyAsync();
        var grid = Grid(Cards(4));

        Assert.All(grid.FindAll(".card-tile a.info"),
                   a => Assert.Equal("-1", a.GetAttribute("tabindex")));
    }

    [Fact]
    public async Task There_is_a_live_region_for_the_cursor_to_speak_through()
    {
        await ReadyAsync();
        var grid = Grid(Cards(4));

        var live = grid.Find("[data-grid-live]");
        Assert.Equal("polite", live.GetAttribute("aria-live"));
    }

    [Fact]
    public async Task The_list_view_is_a_grid_of_rows_and_cells_too()
    {
        // Same keyboard contract in both layouts, and the list is genuinely tabular, so its header
        // gets column headers rather than being a row of unlabelled buttons.
        await ReadyAsync();
        var cards = Cards(5);

        var grid = RenderComponent<CardGrid>(p =>
        {
            p.Add(g => g.Cards, cards);
            p.Add(g => g.CountOf, _ => 0);
            p.Add(g => g.ListView, true);
        });

        Assert.NotEmpty(grid.FindAll("[role=columnheader]"));

        var rows = grid.FindAll(".card-line[role=row]:not(.head)");
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.GetAttribute("aria-label"))));
    }

    [Fact]
    public async Task There_is_a_VISIBLE_way_to_start_keyboard_entry()
    {
        // The question this answers: how do you begin keyboard interaction without clicking? The
        // first attempt was a skip button hidden until focused, which fails twice over — someone
        // who cannot know it exists will not find it, and it still sat behind the page's own
        // toolbar. So the switch is a plain, visible control in the grid's toolbar.
        await ReadyAsync();
        var grid = Grid(Cards(6));

        var button = grid.FindAll("button").Single(b => b.TextContent.Trim() == "Keyboard");

        // The keys are on it, because a control that moves the cursor without saying what to press
        // next has moved the problem rather than solved it.
        var title = button.GetAttribute("title")!;
        Assert.Contains("Arrow keys", title);
        Assert.Contains("0-9", title);
        Assert.Contains("card detail", title);
    }

    [Fact]
    public async Task The_command_palette_offers_the_jump_only_while_a_grid_is_mounted()
    {
        // The genuinely zero-click route: Ctrl+K is advertised in the nav, so this is discoverable
        // in a way "press Tab twenty-six times" is not. Offering it with no grid on screen would
        // be an action that silently does nothing.
        await ReadyAsync();
        var focus = Services.GetRequiredService<PackProphet.Services.GridFocus>();

        Assert.False(focus.Available);

        var grid = Grid(Cards(4));
        Assert.True(focus.Available);

        // Registration is released on teardown, or the palette keeps offering a jump into a page
        // that has gone.
        await grid.Instance.DisposeAsync();
        Assert.False(focus.Available);
    }

    [Fact]
    public async Task Opening_a_card_notes_where_to_come_back_to()
    {
        // Pressing `i` used to cost you your place: coming back left focus outside the grid, the
        // list scrolled to the top, and the card you had just read about somewhere below.
        await ReadyAsync();
        var cards = Cards(8);
        var grid = Grid(cards);
        var focus = Services.GetRequiredService<PackProphet.Services.GridFocus>();

        await grid.Instance.KeyOpen(5);

        Assert.Equal(cards[5].Key, focus.TakeReturn());
    }

    [Fact]
    public async Task The_note_is_consumed_once_so_focus_is_not_stolen_later()
    {
        // Arriving at the collection any other way — a nav link, a bookmark, the palette — must not
        // have focus yanked into the grid because of a card opened ten minutes ago.
        await ReadyAsync();
        var focus = Services.GetRequiredService<PackProphet.Services.GridFocus>();

        focus.ReturnTo("A1-4");

        Assert.Equal("A1-4", focus.TakeReturn());
        Assert.Null(focus.TakeReturn());
    }

    [Fact]
    public async Task A_card_that_the_filters_have_since_dropped_does_not_move_the_cursor()
    {
        // Ordinary rather than exceptional: setting a count while reading about a card can remove
        // it from a "missing only" list, so the card to come back to is simply not there.
        await ReadyAsync();
        var focus = Services.GetRequiredService<PackProphet.Services.GridFocus>();

        focus.ReturnTo("no-such-card");
        var grid = Grid(Cards(4));   // mounts, consumes the memo, finds nothing, carries on

        Assert.NotNull(grid.Instance);
        Assert.Null(focus.TakeReturn());
    }

    // ---- the pointer's inverting gestures ----------------------------------------------

    [Fact]
    public async Task Off_means_off_for_a_right_click_and_a_shift_click_too()
    {
        // Right-click went on removing a copy in Off, on the grounds that nobody does it by
        // accident. People did, and then had to switch to Add to put it back.
        await ReadyAsync();
        var grid = Grid(Cards(4));

        grid.FindAll(".card-tile").ElementAt(0).ContextMenu();
        grid.FindAll(".card-tile").ElementAt(1).Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true });

        Assert.Empty(_adjusted);
    }

    [Fact]
    public async Task In_a_counting_mode_a_right_click_still_does_the_opposite()
    {
        await ReadyAsync();
        var cards = Cards(4);
        var grid = Grid(cards);

        grid.FindAll("button").First(b => b.TextContent.Trim() == "Add").Click();
        grid.FindAll(".card-tile").ElementAt(0).ContextMenu();

        Assert.Equal((cards[0], -1), Assert.Single(_adjusted));
    }
}
