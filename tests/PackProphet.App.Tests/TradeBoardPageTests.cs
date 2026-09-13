namespace PackProphet.App.Tests;

using PackProphet.Domain;
using PackProphet.Engine;
using PackProphet.Pages;

/// <summary>
/// The wishlist, as the Answer page whose answer is a diff.
///
/// Every other Answer page recommends one thing out of many. This one recommends edits, because
/// the board already exists and is retyped by hand in the game — twenty rows of "here is your
/// board" is a transcription job, and five edits is a minute.
/// </summary>
public class TradeBoardPageTests : AppHost
{
    private async Task<IRenderedComponent<TradeBoard>> PageAsync()
    {
        await ReadyAsync();

        // Something worth wishlisting: a plan that wants the rarities a board can carry.
        var tiers = new[] { "R", "RR" }
            .Select(Session.Index.Ladder.IndexOf)
            .Where(i => i is not null)
            .Select(i => i!.Value)
            .ToHashSet();

        Session.SetPlan(RarityPlan.Uniform(tiers));

        var page = RenderComponent<TradeBoard>();
        page.WaitForState(() => page.FindAll(".box-strip .box").Count > 0, TimeSpan.FromSeconds(10));
        return page;
    }

    private static string Flat(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    [Fact]
    public async Task The_strip_has_a_box_for_every_slot_and_for_every_edit()
    {
        // The literal set this component was written for: the board HAS twenty slots. Mostly solid
        // with the gaps at the end says "nearly right" without anyone counting to twenty, which no
        // list of names can do — so every slot gets a box, including the empty ones.
        //
        // Plus a box per card coming off, which is what takes it past twenty.
        var page = await PageAsync();

        var boxes = page.FindAll(".box-strip .box").ToArray();
        var off = page.FindAll(".box-strip .slot-art.off").Count;

        Assert.Equal(GameRules.TradeBoardSlots + off, boxes.Length);
        Assert.True(boxes.Length <= GameRules.TradeBoardSlots * 2,
            "a board cannot drop more cards than it holds");
    }

    [Fact]
    public async Task No_slot_is_numbered_because_the_game_does_not_number_them()
    {
        // The boxes carried 1 to 20. The game's wishlist has no slot order — it lists what you
        // want from a set, in collector number order — so a slot number named a position that
        // exists nowhere but on this page, and it was being read as one.
        var page = await PageAsync();

        var words = page.FindAll(".box-strip .box .act")
                        .Select(e => e.TextContent.Trim())
                        .ToArray();

        Assert.All(words, w => Assert.Contains(w, new[] { "On", "Off" }));

        // How full the board is has to survive the numbers going: the empty slots are still drawn,
        // and the clause over the strip still says how many there are.
        Assert.Equal(
            GameRules.TradeBoardSlots
                - page.FindAll(".box-strip .box.live").Count
                + page.FindAll(".box-strip .slot-art.off").Count,
            page.FindAll(".box-strip .box.dead").Count);
    }

    [Fact]
    public async Task A_slot_carries_what_a_collection_tile_carries()
    {
        // Art alone identifies a card you already recognise and nothing else, and this is the
        // board you are about to check against the game's own list. The same three facts a
        // collection tile shows, in the same order: number, name, rarity.
        var page = await PageAsync();

        var caps = page.FindAll(".box-strip .box.live .slot-cap").ToArray();
        Assert.NotEmpty(caps);

        Assert.All(caps, c =>
        {
            var printed = c.QuerySelector(".no")!.TextContent.Trim();
            Assert.Matches(@"^\S+ \d+$", printed);

            var name = c.QuerySelector("a.nm")!;
            Assert.False(string.IsNullOrWhiteSpace(name.TextContent));
            Assert.StartsWith("card/", name.GetAttribute("href"));

            Assert.NotNull(c.QuerySelector(".rr"));
        });
    }

    [Fact]
    public async Task The_board_reads_newest_set_first_then_by_collector_number()
    {
        // The game's wishlist has no order of its own to match, so this one is picked for reading:
        // the set you are opening now is the one you are checking, and within a set the game lists
        // what you want in number order. Selection order is by value and is no use for reading
        // back — the strip and the table would otherwise list the same twenty cards two ways.
        var page = await PageAsync();

        var sets = Session.Sets;

        static (string Set, int Number) Printed(string text)
        {
            var parts = text.Trim().Split(' ');
            return (parts[0], int.Parse(parts[^1]));
        }

        var listed = page.FindAll("table tbody tr")
                         .Select(r => Printed(r.QuerySelectorAll("td")[1].TextContent))
                         .ToArray();

        Assert.NotEmpty(listed);
        AssertReadingOrder(listed, sets);

        // And the strip's groups, so one screen does not carry two orders. The groups themselves
        // are the diff — what stays, what comes off, what goes on — so each is checked on its own.
        foreach (var group in new[] { "", "on", "off" })
        {
            var boxes = page.FindAll(".box-strip .box.live")
                .Where(b => b.QuerySelector(".slot-art")!.ClassList.Contains("on") == (group == "on")
                         && b.QuerySelector(".slot-art")!.ClassList.Contains("off") == (group == "off"))
                .Select(b => Printed(b.QuerySelector(".slot-cap .no")!.TextContent))
                .ToArray();

            AssertReadingOrder(boxes, sets);
        }
    }

    private static void AssertReadingOrder(
        IReadOnlyList<(string Set, int Number)> cards, PackProphet.Data.SetCatalog sets)
    {
        for (var i = 1; i < cards.Count; i++)
        {
            var previous = sets.SortKey(cards[i - 1].Set);
            var current = sets.SortKey(cards[i].Set);
            var order = string.CompareOrdinal(previous, current);

            Assert.True(order >= 0,
                $"{cards[i - 1].Set} came before the newer {cards[i].Set}");

            if (order == 0)
                Assert.True(cards[i - 1].Number <= cards[i].Number,
                    $"{cards[i - 1].Set} {cards[i - 1].Number} came before "
                    + $"{cards[i].Set} {cards[i].Number}");
        }
    }

    [Fact]
    public async Task A_slot_is_coloured_by_which_way_it_is_moving()
    {
        // The additions used to be ringed in the accent — which is red — so the only marked boxes
        // on a picture of a wishlist wore the colour of a warning and read as "take these down"
        // when they meant the opposite. Green on, red off, and nothing marked stays.
        var page = await PageAsync();

        Assert.Empty(page.FindAll(".slot-art.new"));

        // Whatever the fixture's plan happens to be, no box can be both directions at once, and a
        // marked box always says in words which way it is going.
        foreach (var art in page.FindAll(".slot-art").ToArray())
        {
            Assert.False(art.ClassList.Contains("on") && art.ClassList.Contains("off"));

            var word = art.Closest(".box")!.QuerySelector(".act")?.TextContent.Trim();
            if (art.ClassList.Contains("on")) Assert.Equal("On", word);
            if (art.ClassList.Contains("off")) Assert.Equal("Off", word);

            // A card that merely stays says nothing at all: sixteen boxes reading "Stays" is a
            // word per card for the case that needs none.
            if (!art.ClassList.Contains("on") && !art.ClassList.Contains("off"))
                Assert.Null(word);
        }
    }

    [Fact]
    public async Task A_filled_slot_names_its_card()
    {
        // It used to name the card to a screen reader only, in a visually-hidden span, because the
        // box was a picture and nothing else. The name is on the box now, so the hidden copy would
        // be the same card read twice — and the art is decorative for the same reason.
        var page = await PageAsync();

        var filled = page.FindAll(".box-strip .box.live").ToArray();
        Assert.NotEmpty(filled);

        Assert.All(filled, b =>
        {
            var named = b.QuerySelector(".slot-cap .nm")?.TextContent.Trim();
            Assert.False(string.IsNullOrWhiteSpace(named), "a filled slot must name its card");

            Assert.Equal("true", b.QuerySelector(".slot-art")!.GetAttribute("aria-hidden"));
            Assert.Empty(b.QuerySelectorAll(".visually-hidden"));
        });
    }

    [Fact]
    public async Task The_verdict_counts_edits_and_prices_them_in_effort()
    {
        // The cost of this page's recommendation is typing, not packs — it is the one Answer page
        // whose currency is the user's own minutes.
        var page = await PageAsync();

        var then = Flat(page.Find(".verdict-lead .then").TextContent);
        Assert.Contains("in the game", then);
        Assert.Contains("edit", then);

        // "about about 3 minutes" — the helper and the sentence both supplied the hedge.
        Assert.DoesNotContain("about about", then);

        // And the headline says what kind of edit rather than repeating the count.
        Assert.DoesNotContain("in total", Flat(page.Find(".verdict-lead .name").TextContent));
    }

    [Fact]
    public async Task A_long_list_of_names_stops_being_a_list()
    {
        // A first run on an empty collection asks for all twenty slots, and twenty names in a row
        // is not prose — it is the transcription job this page exists to replace, printed above
        // the table that already lists them properly.
        var page = await PageAsync();

        var says = Flat(page.Find(".verdict-lead .says").TextContent);
        Assert.True(says.Count(c => c == ',') <= 4,
            $"the sentence is listing too many names: {says}");
    }

    [Fact]
    public async Task The_controls_are_in_a_rail_and_the_bar_says_where_they_are_set()
    {
        // The tuning decides which cards are even eligible, so a board read without it is a board
        // you cannot account for — and on a phone the controls are behind a button.
        var page = await PageAsync();

        Assert.Single(page.FindAll(".page-rail"));
        Assert.Empty(page.FindAll(".page-tools"));

        // The scope and all three knobs, in the column rather than folded into a disclosure.
        Assert.Empty(page.FindAll(".page-rail details"));
        Assert.NotEmpty(page.FindAll(".page-rail select"));

        // And the bar carries what they are set to, in words.
        Assert.False(string.IsNullOrWhiteSpace(page.Find(".page-head .subtitle").TextContent));
    }

    [Fact]
    public async Task The_board_is_still_shown_when_there_is_nothing_to_change()
    {
        // A board that needs no edits is still a board. It used to vanish the moment it came
        // right — the one state where you might simply want to look at what you have got was the
        // one state that showed you nothing but a sentence saying so.
        var page = await PageAsync();

        var accept = page.FindAll(".lead-actions button")
                         .First(b => b.TextContent.Contains("made these changes"));
        await page.InvokeAsync(() => accept.Click());
        page.WaitForState(() => page.FindAll(".verdict-lead .name.take").Count == 1,
                          TimeSpan.FromSeconds(10));

        Assert.Contains("already right", page.Find(".verdict-lead .name").TextContent);
        Assert.NotEmpty(page.FindAll(".box-strip .box.live .slot-cap"));

        // No edits, so nothing is marked and there is nothing to confirm having done.
        Assert.Empty(page.FindAll(".box-strip .act"));
        Assert.Empty(page.FindAll(".lead-actions"));
    }

    [Fact]
    public async Task What_cannot_be_listed_is_a_note_in_the_rail()
    {
        // A footnote about cards that are NOT on the board, which under the board read as part of
        // it. The question it answers — "why is my Crown not here" — is asked of the tuning.
        await ReadyAsync();

        // Crowns and Immersives cannot be traded at all, so wanting them is what fills this list.
        var tiers = new[] { "UR", "IM" }
            .Select(Session.Index.Ladder.IndexOf)
            .Where(i => i is not null)
            .Select(i => i!.Value)
            .ToHashSet();

        Assert.NotEmpty(tiers);
        Session.SetPlan(RarityPlan.Uniform(tiers));

        var page = RenderComponent<TradeBoard>();
        page.WaitForState(() => page.FindAll(".page-rail .rail-note").Count > 0,
                          TimeSpan.FromSeconds(10));

        Assert.Empty(page.FindAll(".rail-main .rail-note"));

        var note = page.Find(".page-rail .rail-note");
        Assert.Contains("cannot be traded", note.QuerySelector(".lbl")!.TextContent);

        // Every name rather than the first few: only these rarities are excluded, so the group is
        // small and a truncated list would not say which cards are affected.
        Assert.False(string.IsNullOrWhiteSpace(note.QuerySelector(".who")!.TextContent));
    }

    [Fact]
    public async Task The_plan_can_be_run_again_without_changing_anything()
    {
        // The plan reads the collection at the moment it ran, and what follows it is retyping the
        // board and opening more packs. Until this button existed the only way to re-run was to
        // change a control and change it back, which also changed the plan.
        var page = await PageAsync();

        var rerun = page.FindAll(".page-head .actions button")
            .FirstOrDefault(b => b.GetAttribute("aria-label") == "Work the plan out again");

        Assert.NotNull(rerun);
    }
    [Fact]
    public async Task The_tuning_badge_counts_what_you_changed_not_what_it_shipped_with()
    {
        // The badge says how many of the rail's controls are doing something, which is only worth
        // reading if the answer is zero on arrival. It counted "slots kept for likely offers" as
        // set whenever it was above none — and four of the twenty are kept by default — so a board
        // nobody had touched said Tuning 1, every visit, forever.
        await ReadyAsync();

        var page = RenderComponent<TradeBoard>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".rail-fab.filters")));
        Assert.Empty(page.FindAll(".rail-fab.filters .n"));

        // And it does still count: moving the reserve off its default is a change worth reporting.
        Session.SetBoardSettings(liquidSlots: 0);

        var tuned = RenderComponent<TradeBoard>();
        tuned.WaitForAssertion(() =>
            Assert.Equal("1", tuned.Find(".rail-fab.filters .n").TextContent.Trim()));
    }

}
