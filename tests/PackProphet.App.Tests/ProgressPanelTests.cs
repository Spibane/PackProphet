namespace PackProphet.App.Tests;

using System.Text.RegularExpressions;
using Bunit;
using PackProphet.Pages;

/// <summary>
/// The Progress panel invariant: every panel is the same bands, in the same order, at the same
/// heights.
///
/// This is not a matter of taste. A panel carries a figure, a sentence, a rarity line, three
/// routes, its caveats and its buttons, and the page shows up to twenty-one of them at once — so
/// a component that sits at a different height in each panel makes the reader re-find it in every
/// one instead of reading across a row. Four panels that each look slightly different are four
/// objects; four that agree are one object shown four times.
///
/// Both halves broke in testing, and neither threw:
///
///   The STRUCTURE broke when the caveat list was rendered only where there was something to put
///   in it. A panel with no caveats lost the row, and its buttons rose to where its neighbour's
///   routes were.
///
///   The HEIGHTS broke when a panel was expanded. Grid items stretch by default, so one open
///   panel made its row as tall as the expansion and the panels beside it were stretched to
///   match — and because the template's outer rows are `auto`, they soaked up the difference and
///   spread their own contents out over several hundred pixels.
///
/// bUnit renders the DOM and has no layout, so the structural half is asserted on the markup and
/// the height half on the stylesheet. Between them they cover the two ways this has actually gone
/// wrong.
/// </summary>
public class ProgressPanelTests : AppHost
{
    private static readonly string Css =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "app.css"));

    /// <summary>The body of the first rule whose selector list contains exactly this selector.</summary>
    private static string RuleBody(string selector)
    {
        foreach (Match rule in Regex.Matches(Css, @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}"))
            foreach (var sel in rule.Groups["sel"].Value.Split(','))
                if (sel.Trim() == selector)
                    return rule.Groups["body"].Value;

        return "";
    }

    private async Task<IRenderedComponent<Progress>> PageAsync()
    {
        await ReadyAsync();

        var page = RenderComponent<Progress>();
        page.WaitForState(() => page.FindAll(".set-panel").Count > 0, TimeSpan.FromSeconds(20));
        return page;
    }

    /// <summary>Set codes in the order the page draws their panels.</summary>
    private static string[] PanelOrder(IRenderedComponent<Progress> page) =>
        page.FindAll(".set-panel .p-name .code").Select(e => e.TextContent.Trim()).ToArray();

    [Fact]
    public async Task Release_order_puts_the_newest_set_first()
    {
        // The reverse of the Collection page, and right here for the reason that page's order is
        // right there. The Collection is a catalogue and reads forwards; this is a list of work
        // outstanding, and the work is nearly always in the sets that just came out -- an old set
        // is either finished or has been unfinished for a year. Oldest-first put the two sets you
        // are actually opening at the bottom of twenty panels.
        var page = await PageAsync();

        var shown = PanelOrder(page);
        Assert.True(shown.Length > 1, "the ordering needs more than one panel to mean anything");

        var newestFirst = shown
            .OrderByDescending(Session.Sets.SortKey, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(newestFirst, shown);

        // Named rather than only implied: the rail's own summary has to agree with the order.
        Assert.Contains("newest first", page.Markup);
    }

    [Fact]
    public async Task Closest_first_is_still_closest_first()
    {
        // The other order is unchanged, and the two have to stay distinguishable -- a reversal
        // applied to both would make the control a no-op that still looks like a choice.
        var page = await PageAsync();

        page.FindAll(".rail-seg button").First(b => b.TextContent.Trim() == "Closest").Click();
        page.WaitForAssertion(() => Assert.Contains("closest first", page.Markup));

        var byClosest = PanelOrder(page);
        Assert.True(byClosest.Length > 1);
        Assert.NotEqual(
            byClosest.OrderByDescending(Session.Sets.SortKey, StringComparer.Ordinal).ToArray(),
            byClosest);

        // And back, because a control that only works one way still looks like a choice.
        page.FindAll(".rail-seg button").First(b => b.TextContent.Trim() == "Release").Click();
        page.WaitForAssertion(() => Assert.Contains("newest first", page.Markup));

        var byRelease = PanelOrder(page);
        Assert.Equal(
            byRelease.OrderByDescending(Session.Sets.SortKey, StringComparer.Ordinal).ToArray(),
            byRelease);
    }

    /// <summary>
    /// Every panel emits every band, in one order, whether or not it has anything to put in it.
    ///
    /// The caveat list is the one that matters and the one that broke: it is empty in most panels,
    /// and rendering it conditionally is the obvious-looking tidy-up that silently lifts the
    /// action row by a whole band.
    /// </summary>
    [Fact]
    public async Task Every_panel_emits_every_band_in_the_same_order()
    {
        var page = await PageAsync();

        // The bands that carry content, in template order. Head, lead and bar are structural and
        // always present; these are the four that vary plus the row that follows them, which is
        // the one that moves when a band above it goes missing.
        string[] bands = [".p-say", ".p-rar", ".routes", ".caveats", ".p-acts"];

        var panels = page.FindAll(".set-panel").ToArray();
        Assert.NotEmpty(panels);

        foreach (var panel in panels)
        {
            var set = panel.QuerySelector(".code")?.TextContent ?? "?";

            foreach (var band in bands)
                Assert.True(panel.QuerySelectorAll(band).Length == 1,
                    $"Progress panel {set} has {panel.QuerySelectorAll(band).Length} `{band}` " +
                    "elements where it must have exactly one: every panel is the same bands in " +
                    "the same order, and a missing one lifts everything under it.");

            // Order, not just presence. A band moved past another would keep the count right and
            // put the reader's eye in the wrong place.
            var found = panel.QuerySelectorAll(string.Join(',', bands))
                             .Select(e => bands.First(b => e.Matches(b)))
                             .ToArray();

            Assert.Equal(bands, found);
        }
    }

    /// <summary>
    /// The caveat band is reserved even when empty, which is what lines the action rows up, and
    /// the list is capped in code rather than by the band's overflow.
    ///
    /// Capped in code because four caveats can be true at once and a band that clips the last two
    /// says nothing about having done so. The cap is ordered by consequence, so what survives is
    /// what matters.
    /// </summary>
    [Fact]
    public async Task No_panel_prints_more_caveats_than_the_band_holds()
    {
        var page = await PageAsync();

        foreach (var panel in page.FindAll(".set-panel").ToArray())
        {
            var set = panel.QuerySelector(".code")?.TextContent ?? "?";
            var items = panel.QuerySelectorAll(".caveats li").Length;

            Assert.True(items <= 2,
                $"Progress panel {set} prints {items} caveats. The band holds two, and a third " +
                "is clipped by overflow without saying so -- cap the list where it is built, in " +
                "order of consequence.");
        }
    }

    /// <summary>
    /// A panel is never stretched to its row's height, and never shares spare height out among
    /// its own rows.
    ///
    /// This is the expansion bug, and it is a stylesheet fact rather than a markup one: with the
    /// default `stretch`, opening one panel re-laid out every panel beside it. Either declaration
    /// alone fixes the symptom; both are here because they fail differently — the first keeps a
    /// shut panel the same size as every other shut panel on the page, and the second keeps the
    /// bands put if anything ever does stretch one.
    /// </summary>
    [Fact]
    public void An_open_panel_does_not_re_lay_out_the_panels_beside_it()
    {
        Assert.Contains("align-items: start", RuleBody(".set-panels"));
        Assert.Contains("align-content: start", RuleBody(".set-panel"));
    }

    /// <summary>
    /// The four varying bands are declared as fixed heights on the panel.
    ///
    /// Named here so that turning one back into a `minmax()` or dropping it fails a test rather
    /// than quietly letting one panel disagree with the next.
    /// </summary>
    [Fact]
    public void The_varying_bands_are_fixed_heights()
    {
        var body = RuleBody(".set-panel");

        foreach (var band in new[] { "--band-say", "--band-rar", "--band-route", "--band-cav" })
            Assert.Contains(band, body);

        // The template reads them rather than repeating their values, which is what lets the
        // phone block re-pin two of them without restating the other six rows.
        Assert.DoesNotContain("minmax(var(--band", body);
    }
}
