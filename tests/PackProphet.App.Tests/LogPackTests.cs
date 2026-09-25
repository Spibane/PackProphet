namespace PackProphet.App.Tests;

using PackProphet.Data;
using PackProphet.Pages;

/// <summary>
/// The Log a pack picker, and the pin that lifts one pack out of the series ordering.
///
/// A pin is only worth having if it survives the thing it exists to avoid — the trip through the
/// series dropdown — so that is what these assert, rather than the mere fact of a preference being
/// written.
/// </summary>
public class LogPackTests : AppHost
{
    private async Task<IRenderedComponent<LogPack>> PageAsync()
    {
        await ReadyAsync();
        return RenderComponent<LogPack>();
    }

    /// <summary>Pack names in the order the picker draws them, read off the pin buttons.</summary>
    private static string[] Shown(IRenderedComponent<LogPack> page) =>
        page.FindAll(".pack-cell .pin-pack")
            .Select(b => b.GetAttribute("aria-label")!)
            .Select(l => l.StartsWith("Unpin ", StringComparison.Ordinal) ? l["Unpin ".Length..] : l["Pin ".Length..])
            .ToArray();

    [Fact]
    public async Task A_pinned_pack_comes_first()
    {
        var page = await PageAsync();

        var before = Shown(page);
        Assert.True(before.Length > 1, "the picker needs more than one pack for order to mean anything");

        // Not the first one, or the assertion would pass without the pin doing anything.
        var wanted = before[^1];
        page.FindAll(".pin-pack").ElementAt(before.Length - 1).Click();

        Assert.Equal(wanted, Shown(page)[0]);
    }

    [Fact]
    public async Task A_pin_survives_a_change_of_series()
    {
        var page = await PageAsync();

        var pinned = Shown(page)[^1];
        page.FindAll(".pin-pack").ElementAt(Shown(page).Length - 1).Click();

        // The series the picker did NOT open on. A pack pinned in one series has to stay at the
        // front in the other, since reaching it without changing series is the whole point.
        var other = Session.Sets.Series.First(s => s != Session.Sets.Series.Last());
        page.Find("select[aria-label='Series']").Change(other);

        Assert.Equal(pinned, Shown(page)[0]);
    }

    [Fact]
    public async Task Unpinning_puts_a_pack_back_with_its_series()
    {
        var page = await PageAsync();

        var before = Shown(page);
        page.FindAll(".pin-pack").ElementAt(before.Length - 1).Click();
        Assert.NotEqual(before, Shown(page));

        page.FindAll(".pin-pack").First().Click();     // the pinned one is now the first
        Assert.Equal(before, Shown(page));
    }

    [Fact]
    public async Task The_grid_bar_reports_what_is_picked_rather_than_how_big_the_pack_is()
    {
        // The count the grid shows by default is a fact about the pack, fixed for the whole task.
        // What moves — and what says when the pack is fully logged — is how many you have picked.
        var page = await PageAsync();

        page.FindAll(".pack-choice").First().Click();

        var bar = page.WaitForElement(".grid-count").TextContent;
        Assert.Contains("picked 0", bar);
        Assert.DoesNotContain(" cards", bar);
    }

    private static string Flat(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    [Fact]
    public async Task The_page_leads_with_what_is_still_missing()
    {
        // The Record job's signature, and the reason this page has no headline figure: nothing on
        // it is true until the commit, so what it can lead with is the gap between what you have
        // typed and what a pack holds.
        var page = await PageAsync();
        page.FindAll(".pack-choice").First().Click();

        var strip = page.WaitForElement(".todo-strip");
        Assert.Contains("Still to Do", strip.TextContent);

        // Nothing picked yet, so it says what to do rather than what is wrong.
        Assert.DoesNotContain("short", strip.GetAttribute("class") ?? "");

        // And it offers the way out of doing it by hand, which is the one moment that offer is
        // worth making — before any of it has been done.
        Assert.NotNull(strip.QuerySelector(".escape"));
    }

    [Fact]
    public async Task The_screenshot_reader_is_opened_by_a_button_and_starts_folded()
    {
        // It was a <summary> in secondary text, which reads as a footnote about the page rather
        // than as the faster of the two ways to log a pack.
        var page = await PageAsync();

        Assert.Empty(page.FindAll("details"));

        var open = page.Find("button.shot-open");
        Assert.Equal("false", open.GetAttribute("aria-expanded"));
        Assert.Equal("log-shots", open.GetAttribute("aria-controls"));

        // Folded, but still in the page: a reading is a minute of work and a mis-tap on the
        // button must not be able to throw twenty of them away.
        Assert.NotNull(page.Find("#log-shots").GetAttribute("hidden"));

        open.Click();
        Assert.Equal("true", page.Find("button.shot-open").GetAttribute("aria-expanded"));
        Assert.Null(page.Find("#log-shots").GetAttribute("hidden"));

        page.Find("button.shot-open").Click();
        Assert.Equal("false", page.Find("button.shot-open").GetAttribute("aria-expanded"));
        Assert.NotNull(page.Find("#log-shots").GetAttribute("hidden"));
    }

    // ------------------------------------------------------------------ logging a promo

    [Fact]
    public async Task A_promo_is_logged_from_a_button_and_never_from_the_pack_picker()
    {
        // A promo is handed over by an event rather than opened, so it must not appear among the
        // boosters as though it were a choice you could make.
        var page = await PageAsync();

        Assert.DoesNotContain(Shown(page), name => name.Contains("Vol.", StringComparison.Ordinal));

        var open = page.Find("button[aria-controls='log-promos']");
        Assert.Equal("false", open.GetAttribute("aria-expanded"));

        open.Click();
        Assert.Equal("true", page.Find("button[aria-controls='log-promos']").GetAttribute("aria-expanded"));
        Assert.Null(page.Find("#log-promos").GetAttribute("hidden"));
    }

    [Fact]
    public async Task The_list_is_the_newest_promos_of_the_set_being_filled()
    {
        var page = await PageAsync();
        page.Find("button[aria-controls='log-promos']").Click();

        var set = RecentPromos.CurrentSet(Session.Index, Session.Sets)!;
        var expected = RecentPromos.Newest(Session.Index, set)
            .Select(c => $"{c.Set} {c.Number}")
            .ToArray();

        var shown = page.FindAll("#log-promos .promo-row .nm .sub")
            .Select(e => e.TextContent.Trim())
            .ToArray();

        Assert.Equal(expected, shown);
        Assert.Equal(RecentPromos.Window, shown.Length);
    }

    [Fact]
    public async Task A_promo_already_held_stays_on_the_list_and_can_be_taken_again()
    {
        // The reason owned cards are not filtered out: a promo can be earned more than once, and
        // a row that vanished on the first tap would vanish exactly when the second is needed.
        var page = await PageAsync();
        page.Find("button[aria-controls='log-promos']").Click();

        var first = RecentPromos.Newest(Session.Index, Session.Sets)[0];
        Assert.Equal(0, Session.CountOf(first));

        page.FindAll("#log-promos .promo-row .take").First().Click();
        Assert.Equal(1, Session.CountOf(first));

        Assert.Equal(
            RecentPromos.Window, page.FindAll("#log-promos .promo-row").Count);

        page.FindAll("#log-promos .promo-row .take").First().Click();
        Assert.Equal(2, Session.CountOf(first));

        page.WaitForAssertion(() =>
            Assert.Equal("2", page.FindAll("#log-promos .promo-row .have").First().TextContent.Trim()));
    }

    [Fact]
    public async Task Logging_a_promo_touches_the_collection_and_nothing_else()
    {
        // Not a pack: no log row, no pack points, no hourglass. A row in the pack log would be a
        // pack that was never bought, and it would reach the odds check on History as a pack the
        // model has no rates for.
        var page = await PageAsync();
        page.Find("button[aria-controls='log-promos']").Click();

        var before = Session.Profile.Resources;
        page.FindAll("#log-promos .promo-row .take").First().Click();

        Assert.Empty(Session.Profile.PackLog);
        Assert.Equal(before.PackHourglasses, Session.Profile.Resources.PackHourglasses);
        Assert.Equal(before.PackPointsBySet, Session.Profile.Resources.PackPointsBySet);
    }

    [Fact]
    public async Task A_mistapped_promo_can_be_taken_back_without_a_keyboard()
    {
        // The page is used on a phone beside the game, where Ctrl+Z is not available.
        var page = await PageAsync();
        page.Find("button[aria-controls='log-promos']").Click();

        var first = RecentPromos.Newest(Session.Index, Session.Sets)[0];

        Assert.Empty(page.FindAll("#log-promos .promo-row .drop"));   // nothing to take back yet

        page.FindAll("#log-promos .promo-row .take").First().Click();
        page.FindAll("#log-promos .promo-row .drop").First().Click();

        Assert.Equal(0, Session.CountOf(first));
        Assert.Empty(page.FindAll("#log-promos .promo-row .drop"));
    }

    [Fact]
    public async Task Only_one_of_the_two_panels_is_open_at_a_time()
    {
        // Both sit above the pack grid and both are tall; two open put the grid off the bottom of
        // a phone. They are alternatives rather than steps.
        var page = await PageAsync();

        page.Find("button.shot-open[aria-controls='log-shots']").Click();
        page.Find("button[aria-controls='log-promos']").Click();

        Assert.Equal("false",
            page.Find("button.shot-open[aria-controls='log-shots']").GetAttribute("aria-expanded"));
        Assert.NotNull(page.Find("#log-shots").GetAttribute("hidden"));
        Assert.Null(page.Find("#log-promos").GetAttribute("hidden"));

        page.Find("button.shot-open[aria-controls='log-shots']").Click();
        Assert.NotNull(page.Find("#log-promos").GetAttribute("hidden"));
    }

    [Fact]
    public async Task The_way_out_of_typing_leads_to_this_page_s_own_reader()
    {
        // It used to point at the collection's screenshot import, which records cards and nothing
        // else -- so a pack read that way never reached the pack log, which is the one thing
        // someone on this page came to write. It has to land on this page's own reader instead.
        var page = await PageAsync();
        page.FindAll(".pack-choice").First().Click();

        var escape = page.WaitForElement(".todo-strip .escape");
        Assert.Null(escape.GetAttribute("href"));

        escape.Click();

        // Back on the picker, with the reader already open rather than folded away: it is what was
        // asked for, so it must not need finding again.
        page.WaitForAssertion(() =>
        {
            var open = page.Find("button.shot-open");
            Assert.Equal("true", open.GetAttribute("aria-expanded"));
            Assert.NotNull(page.Find("#log-shots"));
        });
    }

    [Fact]
    public async Task The_strip_only_warns_when_the_commit_is_actually_short()
    {
        // The existing finding this had to keep: a live warning about the count is wrong for the
        // whole time you are tapping, because one card into a five-card pack it is already "not
        // five". So the alarm belongs to the moment the question is asked, not to the task.
        var page = await PageAsync();
        page.FindAll(".pack-choice").First().Click();
        page.WaitForElement(".todo-strip");

        // One card in: still just progress. The grid is virtualised, so the tiles arrive a render
        // after the pack choice rather than with it.
        page.WaitForState(() => page.FindAll(".card-tile").Count > 0, TimeSpan.FromSeconds(10));
        page.Find(".card-tile").Click();
        page.WaitForAssertion(() =>
            Assert.Contains("named", page.Find(".page-head.sticky-head").TextContent));

        Assert.Empty(page.FindAll(".todo-strip.short"));

        // Pressing commit on a short pack is when it says what that would mean.
        page.Find(".page-head.sticky-head .btn-success").Click();

        page.WaitForAssertion(() =>
        {
            var warned = page.Find(".todo-strip.short");
            Assert.Contains("pack holds", Flat(warned.TextContent));
            Assert.Contains("Anyway", Flat(warned.TextContent));
        });
    }

    [Fact]
    public async Task The_foot_says_what_committing_will_do()
    {
        // A Record page cannot headline a figure, but it can say what the figure will BE — which
        // is a statement about the future rather than a reading of a present that does not exist
        // yet. Points accrue per set and stop dead at the cap, so a pack opened while capped earns
        // nothing, and that is worth knowing before rather than after.
        var page = await PageAsync();
        page.FindAll(".pack-choice").First().Click();

        var foot = page.WaitForElement(".commit-foot");
        var said = Flat(foot.TextContent);

        Assert.True(said.Contains("points to") || said.Contains("cap"),
                    $"the foot should say where the points land: {said}");
        Assert.Contains("One undo step", said);
        Assert.Contains("Log Another", said);
    }

    [Fact]
    public async Task The_header_that_carries_the_save_button_is_pinned_to_the_top()
    {
        // Tapping the last card of a pack must not mean scrolling back past every card already
        // tapped to reach "add to collection".
        var page = await PageAsync();

        page.FindAll(".pack-choice").First().Click();

        var head = page.WaitForElement(".page-head.sticky-head");
        Assert.Contains("sticky-head", head.GetAttribute("class"));
        Assert.Contains("Add to Collection", head.TextContent);

        // The count went with it, as progress against what a pack holds rather than a bare tally:
        // "picked 3" is a fact about your typing and "3 of 5 named" is a fact about the job.
        Assert.Contains("named", head.TextContent);
    }
}
