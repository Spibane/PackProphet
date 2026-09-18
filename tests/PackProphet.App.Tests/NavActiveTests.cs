namespace PackProphet.App.Tests;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Bunit;
using PackProphet.Components;
using PackProphet.Layout;
using PackProphet.Services;

/// <summary>
/// Where you are is lit up in the nav, at every URL the page answers to.
///
/// The collection is served at two URLs — the app's root and <c>/collection</c> — because the root
/// has to show something. A NavLink matches one href, so the entry pointing at <c>collection</c>
/// was not active at the root: the deployed site's own address rendered the collection with nothing
/// lit up in either nav, which reads as a nav that has no home. It was the one URL every first
/// visit arrives at, and nothing threw.
///
/// Swept over both navs together, because they are the same nav at two widths and the failure was
/// in both. Routes.IsCollection is checked directly as well: it is the rule, and a rule is cheaper
/// to read back than to infer from two rendered components.
/// </summary>
public class NavActiveTests : AppHost
{
    private void GoTo(string relative)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(relative);
    }

    /// <summary>Every URL the collection answers to, and a few that it does not.</summary>
    [Theory]
    [InlineData("", true)]
    [InlineData("/", true)]
    [InlineData("collection", true)]
    [InlineData("/collection", true)]
    [InlineData("collection?set=A2", true)]           // a set filter is not the nav's business
    [InlineData("collection#top", true)]
    [InlineData("collection/screenshot", false)]      // its own page, with its own job
    [InlineData("collection/import", false)]
    [InlineData("progress", false)]
    [InlineData("packs", false)]
    public void The_rule_names_every_url_the_collection_answers_to(string path, bool expected) =>
        Assert.Equal(expected, Routes.IsCollection(path));

    [Fact]
    public async Task The_desktop_nav_lights_the_collection_up_at_the_root()
    {
        await ReadyAsync();
        GoTo("");

        var nav = RenderComponent<NavMenu>();

        var active = nav.FindAll(".app-nav .nav-item.active").Select(a => a.TextContent.Trim()).ToArray();
        Assert.Single(active);
        Assert.Contains("Collection", active[0]);
    }

    [Fact]
    public async Task The_tab_bar_lights_the_collection_up_at_the_root()
    {
        await ReadyAsync();
        GoTo("");

        var bar = RenderComponent<TabBar>();

        var active = bar.FindAll(".tab-bar .tab.active").ToArray();
        Assert.Single(active);
        Assert.Contains("Collection", active[0].TextContent);
    }

    /// <summary>
    /// And still at the other URL, which is the half NavLink used to get right — the fix has to
    /// keep it rather than move the gap.
    /// </summary>
    [Fact]
    public async Task Both_navs_still_light_it_up_at_the_collection_url()
    {
        await ReadyAsync();
        GoTo("collection");

        var nav = RenderComponent<NavMenu>();
        var bar = RenderComponent<TabBar>();

        Assert.Single(nav.FindAll(".app-nav .nav-item.active"));
        Assert.Contains("Collection", nav.Find(".app-nav .nav-item.active").TextContent);

        Assert.Single(bar.FindAll(".tab-bar .tab.active"));
        Assert.Contains("Collection", bar.Find(".tab-bar .tab.active").TextContent);
    }

    /// <summary>
    /// Exactly one destination is lit at a time. The collection's entry is the one computed by
    /// hand, so it is the one that could light up somewhere it does not belong — a prefix match on
    /// an empty href would have made it active on every page in the app.
    /// </summary>
    [Theory]
    [InlineData("progress")]
    [InlineData("packs")]
    [InlineData("log")]
    [InlineData("collection/screenshot")]
    public async Task The_collection_is_not_lit_up_on_another_page(string path)
    {
        await ReadyAsync();
        GoTo(path);

        var nav = RenderComponent<NavMenu>();
        var bar = RenderComponent<TabBar>();

        foreach (var item in nav.FindAll(".app-nav .nav-item.active"))
            Assert.DoesNotContain("Collection", item.TextContent);

        foreach (var tab in bar.FindAll(".tab-bar .tab.active"))
            Assert.DoesNotContain("Collection", tab.TextContent);
    }

    /// <summary>
    /// The computed entry follows a navigation that happens while the nav is on screen.
    ///
    /// A NavLink keeps its own state in step with the URL; an anchor whose class is computed during
    /// render does not, and would be right on first paint and stale ever after. This is the
    /// assertion that the subscription is there.
    /// </summary>
    [Fact]
    public async Task The_highlight_follows_a_navigation_it_did_not_render_for()
    {
        await ReadyAsync();
        GoTo("progress");

        var nav = RenderComponent<NavMenu>();
        Assert.DoesNotContain(nav.FindAll(".app-nav .nav-item.active"),
                              a => a.TextContent.Contains("Collection"));

        GoTo("");

        nav.WaitForAssertion(() =>
            Assert.Contains("Collection", nav.Find(".app-nav .nav-item.active").TextContent),
            TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// The wordmark goes home rather than nowhere, which is the thing every visitor tries first.
    /// It points at the path the nav agrees on, so arriving by it lights the collection up.
    /// </summary>
    [Fact]
    public async Task The_wordmark_is_a_link_home()
    {
        await ReadyAsync();
        GoTo("progress");

        var brand = RenderComponent<NavMenu>().Find(".app-nav .brand");

        Assert.Equal("a", brand.TagName, ignoreCase: true);
        Assert.True(Routes.IsCollection(brand.GetAttribute("href")),
            "the wordmark must lead somewhere the collection renders");
    }
}
