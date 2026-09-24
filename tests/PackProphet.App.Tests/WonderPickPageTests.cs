namespace PackProphet.App.Tests;

using PackProphet.Domain;
using PackProphet.Pages;

/// <summary>
/// Wonder Pick, the one page where the strip comes before the verdict.
///
/// Everywhere else the boxes explain an answer the page already has. Here you fill them, and
/// filling them is what produces the answer — they are an input first and an explanation second.
/// </summary>
public class WonderPickPageTests : AppHost
{
    private static string Flat(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>
    /// Five cards in the offer, which is what makes the page show anything at all.
    ///
    /// From ONE pack, because that is the only offer the game can produce: a Wonder Pick is a pack
    /// somebody else opened. Five cards off the front of the index were five cards from wherever
    /// the index happened to start, and the page now refuses to build an offer that cannot exist.
    /// </summary>
    private IReadOnlyList<PocketCard> AnOffer() => AnOfferFrom().Cards;

    /// <summary>The first pack in the snapshot holding enough cards to fill an offer.</summary>
    private (string Pack, IReadOnlyList<PocketCard> Cards) AnOfferFrom() =>
        Session.Index.All
            .Where(c => !c.IsPromo && c.Packs is { Length: > 0 })
            .SelectMany(c => c.Packs!.Select(p => (Pack: p, Card: c)))
            .GroupBy(x => x.Pack, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Card).DistinctBy(c => c.OwnershipKey).Count()
                        >= GameRules.WonderPickCardsShown)
            .Select(g => (g.Key, (IReadOnlyList<PocketCard>)g.Select(x => x.Card)
                .DistinctBy(c => c.OwnershipKey)
                .Take(GameRules.WonderPickCardsShown)
                .ToList()))
            .First();

    private IRenderedComponent<WonderPick> WithOffer()
    {
        var page = RenderComponent<WonderPick>();

        // Through the picker's own callback rather than by reaching into the page's state, so the
        // test exercises the path a tap takes. OnPick rather than OnAdd: this page picks a
        // PRINTING, since which set a card came from changes what it is worth.
        foreach (var card in AnOffer())
        {
            var picker = page.FindComponent<PackProphet.Components.CardPicker>();
            page.InvokeAsync(() => picker.Instance.OnPick.InvokeAsync(card)).GetAwaiter().GetResult();
        }

        page.WaitForState(() => page.FindAll(".box-strip .box").Count > 0, TimeSpan.FromSeconds(10));
        return page;
    }

    [Fact]
    public async Task The_screenshot_reader_is_opened_by_a_button_and_starts_folded()
    {
        // The same control as the log screen's, in the same state, because it is the same offer
        // made in the same place in the same job -- and it was a <summary> in secondary text here
        // too, which reads as a footnote about the page rather than the fast way to fill it.
        await ReadyAsync();

        var page = RenderComponent<WonderPick>();

        Assert.Empty(page.FindAll("details"));

        var open = page.Find("button.shot-open");
        Assert.Equal("false", open.GetAttribute("aria-expanded"));
        Assert.Equal("wonder-shots", open.GetAttribute("aria-controls"));
        Assert.NotNull(page.Find("#wonder-shots").GetAttribute("hidden"));

        open.Click();
        Assert.Equal("true", page.Find("button.shot-open").GetAttribute("aria-expanded"));
        Assert.Null(page.Find("#wonder-shots").GetAttribute("hidden"));

        page.Find("button.shot-open").Click();
        Assert.NotNull(page.Find("#wonder-shots").GetAttribute("hidden"));
    }

    [Fact]
    public async Task The_reader_is_not_offered_once_the_offer_has_cards_in_it()
    {
        // Unchanged by turning the disclosure into a button: the reader replaces typing the five
        // cards, so it has nothing to offer someone who has already typed some.
        await ReadyAsync();

        var page = WithOffer();

        Assert.Empty(page.FindAll("button.shot-open"));
        Assert.Empty(page.FindAll("#wonder-shots"));
    }

    [Fact]
    public async Task While_the_offer_is_half_typed_the_picker_is_under_the_strip()
    {
        // The complaint this is for: naming the first card pushed the second search about a screen
        // and a half down, because the verdict, the commit row and the per-card table all sat
        // between the five boxes and the control those boxes tell you to use. "Tap a card below"
        // was true and useless.
        await ReadyAsync();

        var page = RenderComponent<WonderPick>();
        var one = AnOffer()[0];

        var picker = page.FindComponent<PackProphet.Components.CardPicker>();
        await page.InvokeAsync(() => picker.Instance.OnPick.InvokeAsync(one));
        page.WaitForState(() => page.FindAll(".box-strip .box.live").Count == 1, TimeSpan.FromSeconds(10));

        var markup = page.Markup;
        var strip = markup.IndexOf("box-strip", StringComparison.Ordinal);
        var search = markup.IndexOf("Add Card 2 of", StringComparison.Ordinal);
        var verdict = markup.IndexOf("verdict-lead", StringComparison.Ordinal);

        Assert.True(strip >= 0 && search > strip, "the picker must follow the strip");
        Assert.True(verdict < 0 || verdict > search,
            "a verdict over a half-typed offer must not stand between the boxes and the picker");
    }

    [Fact]
    public async Task A_finished_offer_puts_the_verdict_straight_under_the_strip()
    {
        // And the other half of the rule: once the work is done there is no work to show, so the
        // picker stops rendering and the page is the ordinary Answer shape -- reached by finishing
        // rather than by a second layout.
        await ReadyAsync();
        var page = WithOffer();

        Assert.DoesNotContain("Add Card", page.Markup);
        Assert.Empty(page.FindComponents<PackProphet.Components.CardPicker>());

        var markup = page.Markup;
        Assert.True(markup.IndexOf("verdict-lead", StringComparison.Ordinal)
                    > markup.IndexOf("box-strip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_strip_comes_before_the_verdict()
    {
        // The reversal, and the reason for it: these five boxes are what you are filling in, and
        // the verdict is what filling them produces. Putting the answer above its own input would
        // ask the page to state a conclusion before it has been given the premise.
        await ReadyAsync();
        var page = WithOffer();

        var markup = page.Markup;
        var strip = markup.IndexOf("box-strip", StringComparison.Ordinal);
        var verdict = markup.IndexOf("verdict-lead", StringComparison.Ordinal);

        Assert.True(strip >= 0 && verdict >= 0, "both the strip and the verdict should render");
        Assert.True(strip < verdict, "the strip is the input, so it comes first on this page");
    }

    [Fact]
    public async Task There_is_a_box_for_every_card_the_game_shows()
    {
        await ReadyAsync();
        var page = RenderComponent<WonderPick>();

        var picker = page.FindComponent<PackProphet.Components.CardPicker>();
        await page.InvokeAsync(() => picker.Instance.OnPick.InvokeAsync(AnOffer()[0]));
        page.WaitForState(() => page.FindAll(".box-strip .box").Count > 0, TimeSpan.FromSeconds(10));

        // Five boxes with one card in the offer: the empty ones are the point, since the strip is
        // an input and an input has to show what is still missing.
        var boxes = page.FindAll(".box-strip .box").ToArray();
        Assert.Equal(GameRules.WonderPickCardsShown, boxes.Length);
        Assert.Equal(GameRules.WonderPickCardsShown - 1,
                     page.FindAll(".box-strip .box.dead").Count);
    }

    [Fact]
    public async Task The_verdict_sources_its_own_bar()
    {
        // The threshold is the page's whole contribution and it is computed from the user's own
        // logged offers, so a verdict stating it without sourcing it is asking to be trusted about
        // the one number nobody else could check.
        await ReadyAsync();
        var page = WithOffer();

        var then = Flat(page.Find(".verdict-lead .then").TextContent);
        Assert.Contains("logged offer", then);
    }

    [Fact]
    public async Task The_chart_waits_until_there_is_a_distribution_to_draw()
    {
        // The one chart in the app, and it earns its place by showing a percentile — which cannot
        // be said in a sentence. Ten buckets over a handful of offers is not a shape though, it is
        // a few bars and some noise, so below the sample floor the verdict says so in words
        // instead and nothing is drawn.
        await ReadyAsync();
        var page = WithOffer();

        Assert.Empty(page.FindAll(".offer-hist"));
        Assert.Contains("logged offers", Flat(page.Find(".verdict-lead .then").TextContent));
    }

    [Fact]
    public async Task Taking_and_skipping_are_offered_as_equals()
    {
        // The bar is built from BOTH, and an offer skipped and not logged is a sample the
        // threshold never sees. The old bar made skipping a small outline button at the end of a
        // row, which quietly says one of them is the real answer.
        await ReadyAsync();
        var page = WithOffer();

        var commit = page.Find(".offer-commit");
        Assert.Contains("Took It", commit.TextContent);
        Assert.Contains("Skipped It", commit.TextContent);

        // Both are top-level buttons of the commit row rather than one being tucked behind the
        // other's control.
        var buttons = commit.QuerySelectorAll("button")
            .Select(b => Flat(b.TextContent))
            .ToArray();

        Assert.Contains(buttons, b => b.StartsWith("Took It", StringComparison.Ordinal));
        Assert.Contains(buttons, b => b.StartsWith("Skipped It", StringComparison.Ordinal));
    }

    /// <summary>
    /// A Wonder Pick is one pack somebody else opened, so the five cards are five cards from that
    /// pack. Before this the picker offered the whole game after the first card, and an appraisal
    /// of an offer that cannot exist is a number with nothing behind it.
    /// </summary>
    [Fact]
    public async Task After_the_first_card_only_its_own_pack_can_be_added()
    {
        await ReadyAsync();
        var page = RenderComponent<WonderPick>();

        var (pack, cards) = AnOfferFrom();
        var first = cards[0];

        var stranger = Session.Index.All.First(c =>
            c.Packs is { Length: > 0 } packs
            && !packs.Contains(pack, StringComparer.OrdinalIgnoreCase));

        var picker = page.FindComponent<PackProphet.Components.CardPicker>();
        await page.InvokeAsync(() => picker.Instance.OnPick.InvokeAsync(first));
        page.WaitForState(() => page.FindAll(".box-strip .box").Count > 0, TimeSpan.FromSeconds(10));

        // A card from another pack is refused...
        picker = page.FindComponent<PackProphet.Components.CardPicker>();
        await page.InvokeAsync(() => picker.Instance.OnPick.InvokeAsync(stranger));

        Assert.DoesNotContain(page.FindAll(".box-strip .box .visually-hidden"),
            b => b.TextContent.Trim() == stranger.Name);

        // ...and one from the same pack still lands, so the rule narrows rather than locks.
        picker = page.FindComponent<PackProphet.Components.CardPicker>();
        await page.InvokeAsync(() => picker.Instance.OnPick.InvokeAsync(cards[1]));

        page.WaitForAssertion(() =>
            Assert.Equal(2, page.FindAll(".box-strip .box .visually-hidden").Count),
            TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// The rule has to be visible before it bites. A search coming back empty because the card is
    /// in another pack looks like a broken search unless the page said which pack first.
    /// </summary>
    [Fact]
    public async Task The_picker_names_the_pack_it_is_restricted_to()
    {
        await ReadyAsync();
        var page = RenderComponent<WonderPick>();

        var heading = () => Flat(page.Find(".sub-head").TextContent);

        // Nothing named yet, so nothing to restrict to and nothing to say.
        Assert.Equal("Add Card 1 of 5", heading());

        var (_, cards) = AnOfferFrom();
        var picker = page.FindComponent<PackProphet.Components.CardPicker>();
        await page.InvokeAsync(() => picker.Instance.OnPick.InvokeAsync(cards[0]));

        page.WaitForAssertion(() =>
        {
            Assert.StartsWith("Add Card 2 of 5", heading());
            Assert.Contains("\u00b7", heading());
        }, TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Emptying the offer has to empty the restriction with it, or the page stays narrowed to a
    /// pack whose card is no longer on screen.
    /// </summary>
    [Fact]
    public async Task Clearing_the_offer_opens_the_picker_back_up()
    {
        var page = WithOffer();

        await page.InvokeAsync(() => page.Find(".offer-clear").Click());

        page.WaitForAssertion(() =>
            Assert.Equal("Add Card 1 of 5", Flat(page.Find(".sub-head").TextContent)),
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task The_page_stacks_no_bar_of_its_own()
    {
        await ReadyAsync();
        var page = RenderComponent<WonderPick>();

        Assert.Single(page.FindAll(".page-head"));
        Assert.Empty(page.FindAll(".page-tools"));

        // The scope picker went into a sheet; the stamina selector did not, because it is the
        // page's only live input and the verdict is wrong the moment it is stale.
        Assert.NotNull(page.Find("#wonder-target"));
        Assert.NotNull(page.Find(".page-head #stamina"));
    }

    /// <summary>A Deluxe pack holds four cards, so its offer's fifth slot is Pack Hourglasses.</summary>
    private IRenderedComponent<WonderPick> WithDeluxeOffer()
    {
        var page = RenderComponent<WonderPick>();
        foreach (var card in Session.Index.BySet["A4b"].Where(c => c.Rarity == "C").Take(GameRules.DeluxePackCards))
        {
            var picker = page.FindComponent<PackProphet.Components.CardPicker>();
            page.InvokeAsync(() => picker.Instance.OnPick.InvokeAsync(card)).GetAwaiter().GetResult();
        }

        page.WaitForState(() => page.FindAll(".offer-commit").Count > 0, TimeSpan.FromSeconds(10));
        return page;
    }

    [Fact]
    public async Task A_Deluxe_offer_is_four_cards_and_the_hourglasses()
    {
        await ReadyAsync();
        var page = WithDeluxeOffer();

        var boxes = page.FindAll(".box-strip .box").ToArray();
        Assert.Equal(GameRules.WonderPickCardsShown, boxes.Length);
        Assert.Empty(page.FindAll(".box-strip .box.dead"));
        Assert.Contains("pack hourglasses", Flat(boxes[^1].TextContent));

        // Four cards complete the offer, so the picker is gone.
        Assert.Empty(page.FindComponents<PackProphet.Components.CardPicker>());
    }

    [Fact]
    public async Task Taking_the_hourglasses_credits_them()
    {
        await ReadyAsync();
        var page = WithDeluxeOffer();
        var before = Session.Profile.Resources.PackHourglasses;

        page.Find(".offer-commit select").Change("hourglasses");
        page.FindAll(".offer-commit button").First(b => b.TextContent.Contains("Took It")).Click();

        Assert.Equal(before + GameRules.DeluxeWonderPickHourglasses, Session.Profile.Resources.PackHourglasses);
        var logged = Session.Profile.WonderLog[^1];
        Assert.True(logged.Deluxe);
        Assert.True(logged.ReceivedHourglasses);
        Assert.Null(logged.Received);
    }
}
