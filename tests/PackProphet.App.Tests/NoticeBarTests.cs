namespace PackProphet.App.Tests;

using Microsoft.Extensions.DependencyInjection;
using PackProphet.Data;
using PackProphet.Layout;
using PackProphet.Services;

/// <summary>
/// The bar above every page: when it appears, when it does not, and what dismissing it costs.
///
/// Every test here runs against the vendored snapshot with the CDN refused, which is what the
/// whole suite does — so <see cref="DataSource.VendoredSnapshot"/> is the derived notice on offer,
/// and it is a real one rather than a fixture. The wording of the notices is asserted in
/// PackProphet.Tests; what is asserted here is the part that needs a rendered app: the gates, the
/// pick between two live notices, and where a dismissal is written.
/// </summary>
public class NoticeBarTests : AppHost
{
    /// <summary>Whether this test's app starts with cards in it. Derived notices wait for that.</summary>
    private bool _collected;

    private string? _feed;

    protected override NoticeOptions NoticeSettings() => new()
    {
        GistUrl = _feed is null
            ? ""
            : "https://gist.githubusercontent.com/someone/abc123/raw/notice.json",
    };

    protected override string? NoticeFeedJson() => _feed;

    protected override AppState Start()
    {
        var state = AppState.Fresh();
        if (!_collected) return state;

        return state with
        {
            Profiles = [state.Active with { Collection = new() { ["a1-001.webp"] = 1 } }],
        };
    }

    private async Task<IRenderedComponent<MainLayout>> LayoutAsync()
    {
        await ReadyAsync();
        return RenderComponent<MainLayout>();
    }

    private static string? Text(IRenderedComponent<MainLayout> layout) =>
        layout.FindAll(".site-notice .msg").SingleOrDefault()?.TextContent.Trim();

    // ---- the gates --------------------------------------------------------------------

    [Fact]
    public async Task A_brand_new_app_is_told_nothing_it_derived()
    {
        // A first visit is already a wall of onboarding, and "this set has no pull rates yet" is an
        // answer to a question nobody with an empty collection has asked.
        var layout = await LayoutAsync();

        Assert.Empty(layout.FindAll(".site-notice"));
    }

    [Fact]
    public async Task Once_there_are_cards_a_stale_database_is_announced()
    {
        // The snapshot fallback is worth saying because of what it silently does: a set released
        // since the deploy is simply absent, so the app looks complete and is a set short.
        _collected = true;

        var layout = await LayoutAsync();

        Assert.Equal(DataSource.VendoredSnapshot, Session.Data!.Source);
        Assert.Contains("card database is unreachable", Text(layout));
    }

    [Fact]
    public async Task An_authored_notice_does_not_wait_for_a_collection()
    {
        // An outage is as true for a new visitor as for anyone else, and is the more confusing of
        // the two to meet without explanation.
        _feed = """[{"id":"down","level":"problem","text":"Screenshot import is broken."}]""";

        var layout = await LayoutAsync();

        layout.WaitForAssertion(() =>
            Assert.Contains("Screenshot import is broken.", Text(layout)));
    }

    [Fact]
    public async Task An_unconfigured_feed_is_never_asked_for()
    {
        _collected = true;
        await LayoutAsync();

        var feed = Services.GetRequiredService<NoticeFeed>();
        await feed.LoadAsync();

        Assert.True(feed.Read);
        Assert.Empty(feed.Notices);
    }

    // ---- one bar, and which one -------------------------------------------------------

    [Fact]
    public async Task Two_live_notices_still_render_one_bar()
    {
        // A second row here is a second row on every page in the app, and a user told two things
        // at once acts on neither.
        _collected = true;
        _feed = """[{"id":"down","level":"problem","text":"Screenshot import is broken."}]""";

        var layout = await LayoutAsync();

        layout.WaitForAssertion(() => Assert.Single(layout.FindAll(".site-notice")));
        Assert.Contains("Screenshot import is broken.", Text(layout));
    }

    [Fact]
    public async Task The_more_severe_notice_wins_however_it_arrived()
    {
        // The derived outage notice is a Warning and this authored one is Info, so severity
        // decides rather than which channel produced it.
        _collected = true;
        _feed = """[{"id":"heads-up","level":"info","text":"B5 launches on Friday."}]""";

        var layout = await LayoutAsync();

        layout.WaitForAssertion(() => Assert.NotEmpty(layout.FindAll(".site-notice")));
        Assert.Contains("card database is unreachable", Text(layout));
    }

    // ---- how loud it is --------------------------------------------------------------

    // One app per test, because the feed is fetched once per app lifetime and remembered --
    // rendering a second layout after changing _feed would render the first fetch again.

    [Fact]
    public async Task A_warning_does_not_interrupt_a_screen_reader()
    {
        // Notice.razor makes a Warning assertive, which is right beside the form it is about and
        // wrong for a bar read on arrival at every page: being cut off mid-sentence twelve times
        // is worse than hearing it a moment late.
        _collected = true;

        var layout = await LayoutAsync();

        Assert.Equal("status", layout.Find(".site-notice").GetAttribute("role"));
        Assert.Contains("warning", layout.Find(".site-notice").ClassList);
    }

    [Fact]
    public async Task A_problem_does_interrupt_one()
    {
        _feed = """[{"id":"down","level":"problem","text":"Screenshot import is broken."}]""";

        var layout = await LayoutAsync();

        layout.WaitForAssertion(() =>
            Assert.Equal("alert", layout.Find(".site-notice").GetAttribute("role")));
        Assert.Contains("problem", layout.Find(".site-notice").ClassList);
    }

    [Fact]
    public async Task The_dismiss_control_is_labelled_rather_than_only_a_glyph()
    {
        _collected = true;
        var layout = await LayoutAsync();

        Assert.Equal("Hide this notice", layout.Find(".site-notice .x").GetAttribute("aria-label"));
    }

    // ---- dismissing ------------------------------------------------------------------

    [Fact]
    public async Task Dismissing_hides_the_bar()
    {
        _collected = true;
        var layout = await LayoutAsync();

        layout.Find(".site-notice .x").Click();

        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll(".site-notice")));
    }

    [Fact]
    public async Task Dismissing_an_outage_is_not_remembered_across_reloads()
    {
        // It is re-tested on every boot, so a remembered dismissal would silence a genuine outage
        // months later on the strength of one bad afternoon.
        _collected = true;
        var layout = await LayoutAsync();

        layout.Find(".site-notice .x").Click();

        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll(".site-notice")));
        Assert.Empty(Session.State.Prefs.DismissedNotices);
    }

    [Fact]
    public async Task Dismissing_an_authored_notice_is_remembered()
    {
        _feed = """[{"id":"down","level":"problem","text":"Screenshot import is broken."}]""";
        var layout = await LayoutAsync();

        layout.WaitForAssertion(() => Assert.NotEmpty(layout.FindAll(".site-notice")));
        layout.Find(".site-notice .x").Click();

        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll(".site-notice")));
        Assert.Contains("feed:down", Session.State.Prefs.DismissedNotices);
    }

    [Fact]
    public async Task Dismissing_one_notice_leaves_the_next_one_free_to_appear()
    {
        // The whole reason a dismissal is keyed rather than a boolean. The two dismissible strips
        // that came before this were each one flag, so hiding the message was turning off the
        // feature — which cannot work for a bar that announces whichever thing is behind today.
        await ReadyAsync();

        Session.DismissNotice("feed:down");

        Assert.True(Session.NoticeDismissed("feed:down"));
        Assert.False(Session.NoticeDismissed("feed:other"));
    }

    [Fact]
    public async Task A_new_release_supersedes_the_dismissal_of_the_last_one()
    {
        // The derived notice is keyed on the whole list of sets the site is behind on, so adding
        // one makes every earlier key unmatchable forever. Keeping them would accumulate a key per
        // release.
        await ReadyAsync();

        Session.DismissNotice("waiting:B4a");
        Session.DismissNotice("waiting:B4a+B5");

        Assert.False(Session.NoticeDismissed("waiting:B4a"));
        Assert.True(Session.NoticeDismissed("waiting:B4a+B5"));
    }

    [Fact]
    public async Task A_feed_that_invents_an_id_an_hour_cannot_grow_the_save_without_bound()
    {
        await ReadyAsync();

        for (var i = 0; i < AppSession.RememberedDismissals * 3; i++)
            Session.DismissNotice($"feed:n{i}");

        Assert.Equal(AppSession.RememberedDismissals, Session.State.Prefs.DismissedNotices.Count);

        // Oldest first out, so the most recently dismissed notices are the ones that stay quiet.
        Assert.True(Session.NoticeDismissed($"feed:n{AppSession.RememberedDismissals * 3 - 1}"));
        Assert.False(Session.NoticeDismissed("feed:n0"));
    }

    [Fact]
    public async Task Hidden_notices_can_be_switched_back_on_and_the_bar_returns()
    {
        // Both dismissible strips before this were reversible from Settings — the gap bar's own
        // tooltip says so — and a permanent dismissal with no way back turns a misclick into a
        // loss. This also pins the memo down: Current is cached on a signature, and a counter this
        // class bumped in Dismiss would not see the settings page clearing the list.
        _feed = """[{"id":"down","level":"problem","text":"Screenshot import is broken."}]""";
        var layout = await LayoutAsync();

        layout.WaitForAssertion(() => Assert.NotEmpty(layout.FindAll(".site-notice")));
        layout.Find(".site-notice .x").Click();
        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll(".site-notice")));

        Assert.Equal(1, Session.DismissedNoticeCount);
        Session.ClearDismissedNotices();

        layout.WaitForAssertion(() => Assert.NotEmpty(layout.FindAll(".site-notice")));
        Assert.Equal(0, Session.DismissedNoticeCount);
    }

    [Fact]
    public async Task A_dismissal_survives_a_reload()
    {
        // Through the store, not just the in-memory record: the point of persisting it is the
        // next visit.
        await ReadyAsync();
        Session.DismissNotice("feed:down");
        await Session.FlushAsync();

        Assert.Contains("feed:down", Session.State.Prefs.DismissedNotices);

        var reread = StateSerializer.Deserialize(StateSerializer.Serialize(Session.State));
        Assert.Contains("feed:down", reread!.Prefs.DismissedNotices);
    }
}
