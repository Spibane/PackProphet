namespace PackProphet.App.Tests;

using System.Reflection;
using Microsoft.AspNetCore.Components;
using Bunit;

/// <summary>
/// The rule the whole shell exists for, checked across every routable page at once.
///
/// No page stacks more than two horizontal bars. On a desktop bar one is the global nav and bar
/// two is the page's own; on a phone bar one is the page bar and bar two is the tab bar. Neither
/// is a page's to spend, so what a page may stack is exactly one strip of its own — and everything
/// that used to be a second or third one is now in a rail, in a sheet, or in that bar's sentence.
///
/// Written as a sweep rather than per page because the failure it guards against is a page added
/// later reaching for a `page-tools` because that is what the page beside it used to do. Discovered
/// by reflection for the same reason: a route added next year is covered without anyone
/// remembering this file exists.
/// </summary>
public class TwoBarRuleTests : AppHost
{
    public static TheoryData<string> ParameterlessPages
    {
        get
        {
            var data = new TheoryData<string>();

            var seen = new HashSet<Type>();
            foreach (var type in typeof(PackProphet.Services.AppSession).Assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(IComponent).IsAssignableFrom(type)) continue;

                var routes = type.GetCustomAttributes<RouteAttribute>().ToArray();
                if (routes.Length == 0) continue;

                // A route with a parameter is a different test without one — see the not-found
                // behaviour tests — so only the pages that render bare are swept here.
                if (routes.All(r => r.Template.Contains('{'))) continue;
                if (!seen.Add(type)) continue;

                data.Add(type.FullName!);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ParameterlessPages))]
    public async Task No_page_stacks_a_second_bar_of_its_own(string typeName)
    {
        await ReadyAsync();

        // Through DynamicComponent, the way the page-render sweep next door does it: bUnit's
        // generic RenderComponent needs the type at compile time and a reflection sweep has it
        // only at run time.
        var type = typeof(PackProphet.Services.AppSession).Assembly.GetType(typeName)!;
        var page = RenderComponent<DynamicComponent>(p => p.Add(c => c.Type, type));

        // page-tools and grid-toolbar are the two strips the redesign retired. A page may still
        // have exactly one page-head; anything beyond that is the third bar the rule forbids.
        Assert.Empty(page.FindAll(".page-tools"));
        Assert.Empty(page.FindAll(".grid-toolbar"));

        // One page bar at a time. Several pages render a different head per branch — loading,
        // error, loaded — and those are alternatives rather than a stack.
        Assert.True(page.FindAll(".page-head").Count <= 1,
            $"{type.Name} renders {page.FindAll(".page-head").Count} page bars at once");
    }

    [Theory]
    [MemberData(nameof(ParameterlessPages))]
    public async Task A_page_that_folds_its_controls_away_says_so_on_its_bar(string typeName)
    {
        await ReadyAsync();

        var type = typeof(PackProphet.Services.AppSession).Assembly.GetType(typeName)!;
        var page = RenderComponent<DynamicComponent>(p => p.Add(c => c.Type, type));

        // A sheet is the app's one way of putting controls out of sight, whether it opens from a
        // floating button or from the page bar. Below 600px the subtitle is hidden as explanatory
        // prose -- which it is not on these pages, where it is the only statement of what the
        // hidden controls are set to. `folded` is the page saying so, and the stylesheet stacks
        // the bar into two lines rather than dropping the sentence.
        if (page.FindAll(".sheet").Count == 0) return;

        var heads = page.FindAll(".page-head").ToArray();
        foreach (var head in heads)
        {
            // A bar with nothing to say is not covered by this: the rule is about a sentence worth
            // keeping, not about every bar on a page that happens to own a sheet.
            if (head.QuerySelector(".subtitle") is null) continue;

            Assert.True(head.ClassList.Contains("folded"),
                $"{type.Name} folds its controls into a sheet but its bar is not marked `folded`, " +
                "so its subtitle -- the only thing saying what those controls are set to -- is " +
                "hidden below 600px.");
        }
    }

    [Theory]
    [MemberData(nameof(ParameterlessPages))]
    public async Task Folded_controls_arrive_under_a_heading_that_says_what_they_set(string typeName)
    {
        await ReadyAsync();

        var type = typeof(PackProphet.Services.AppSession).Assembly.GetType(typeName)!;
        var page = RenderComponent<DynamicComponent>(p => p.Add(c => c.Type, type));

        // The argument for a column over a bar, and the one the four hand-rolled sheets never got:
        // a bar has room for the control and not for its name, so a filter arrives as a bare
        // "Hardest to Pull" with nothing saying it is the sort rather than the grouping. Both
        // shells group, and a group carries its heading.
        foreach (var shell in page.FindAll(".page-rail, .page-sheet").ToArray())
        {
            var groups = shell.QuerySelectorAll(".rail-group").ToArray();
            Assert.True(groups.Length > 0,
                $"{type.Name} folds controls away without grouping them, so nothing on the " +
                "opened shell says what any of them sets.");

            foreach (var group in groups)
            {
                // One control that names itself is exempt, and the exemption is the point rather
                // than a hole in the rule: the collection's search box carries "Search name" as
                // its own placeholder, and a SEARCH heading over it would be the label written
                // twice. What needs the heading is a group -- two selects side by side, where
                // neither can say which of them is the sort.
                var named = group.QuerySelectorAll("input, select, textarea").ToArray();
                if (named.Length == 1
                    && !string.IsNullOrWhiteSpace(
                        named[0].GetAttribute("aria-label") ?? named[0].GetAttribute("placeholder")))
                    continue;

                Assert.False(string.IsNullOrWhiteSpace(group.QuerySelector(".ttl")?.TextContent),
                    $"{type.Name} has a control group with no heading.");
            }
        }

        // `target-sheet` was the class four pages put on a sheet of their own. It never had a
        // stylesheet rule: everything it appeared to do came from `sheet` beside it, so it marked
        // the duplication without carrying any of it. Nothing should reach for it again.
        Assert.Empty(page.FindAll(".target-sheet"));
    }

    [Theory]
    [MemberData(nameof(ParameterlessPages))]
    public async Task A_page_has_one_shape_for_a_verdict(string typeName)
    {
        await ReadyAsync();

        var type = typeof(PackProphet.Services.AppSession).Assembly.GetType(typeName)!;
        var page = RenderComponent<DynamicComponent>(p => p.Add(c => c.Type, type));

        // There were two: `verdict-lead`, which every Answer page leads with, and a tinted box
        // called `verdict`. The wishlist drew the box when its answer was "nothing to change" and
        // the lead when there was something -- two shapes for one slot on one page -- and Compare
        // had three boxes and no lead at all.
        Assert.Empty(page.FindAll(".verdict"));
    }

    [Theory]
    [MemberData(nameof(ParameterlessPages))]
    public async Task No_heading_picks_its_level_and_then_undoes_it(string typeName)
    {
        await ReadyAsync();

        var type = typeof(PackProphet.Services.AppSession).Assembly.GetType(typeName)!;
        var page = RenderComponent<DynamicComponent>(p => p.Add(c => c.Type, type));

        // `<h2 class="h6">` is a level chosen for the document outline and then a Bootstrap class
        // chosen for the SIZE to override it -- which leaves the size decided one heading at a
        // time, in the markup, where nothing can keep twenty-five of them in step. The app had
        // exactly that, in two sizes, with the top margin arriving separately as mt-3, or mt-4, or
        // not at all.
        //
        // Two tiers now, both in the stylesheet: a bare h2 inside `sections` for a break between
        // sections of a page, and `sub-head` for a heading inside a panel or a column.
        var sizes = new[] { "h1", "h2", "h3", "h4", "h5", "h6" };

        foreach (var head in page.FindAll("h1, h2, h3, h4, h5, h6").ToArray())
        {
            var offender = sizes.FirstOrDefault(c => head.ClassList.Contains(c));
            Assert.True(offender is null,
                $"{type.Name} has a <{head.TagName.ToLowerInvariant()}> carrying `{offender}`, " +
                "which sets its size in the markup. Use `sub-head`, or a bare heading inside a " +
                "`sections` shell.");
        }
    }

    [Theory]
    [MemberData(nameof(ParameterlessPages))]
    public async Task The_way_out_of_a_page_is_written_one_way(string typeName)
    {
        await ReadyAsync();

        var type = typeof(PackProphet.Services.AppSession).Assembly.GetType(typeName)!;
        var page = RenderComponent<DynamicComponent>(p => p.Add(c => c.Type, type));

        // Nine hand-written back links produced eight sentences -- "back", "decks", "chase lists",
        // "all chase lists", "settings", "collection", "go to your collection", "Change Pack" --
        // with two of them in one file pointing at the same page. One was a button where the rest
        // were links, so open-in-new-tab worked on eight of nine.
        foreach (var control in page.FindAll("a, button").ToArray())
        {
            if (!control.TextContent.TrimStart().StartsWith('\u2190')) continue;

            Assert.True(control.ClassList.Contains("back"),
                $"{type.Name} writes its own back control (\"{control.TextContent.Trim()}\"). " +
                "Use BackLink, which decides the arrow, the element and how the label is worded.");
        }
    }

    [Theory]
    [MemberData(nameof(ParameterlessPages))]
    public async Task Every_page_has_exactly_one_h1(string typeName)
    {
        await ReadyAsync();

        var type = typeof(PackProphet.Services.AppSession).Assembly.GetType(typeName)!;
        var page = RenderComponent<DynamicComponent>(p => p.Add(c => c.Type, type));

        // Two pages use an editable field as their title, because the title is the thing being
        // renamed. The deck editor kept a visually-hidden h1 beside it so the page still has a
        // heading to land on; the chase list, with the same bar, had none -- so navigating by
        // headings went straight past the page's own name.
        Assert.Single(page.FindAll("h1"));
    }

    [Theory]
    [MemberData(nameof(ParameterlessPages))]
    public async Task Only_the_floating_button_opens_a_rail(string typeName)
    {
        await ReadyAsync();

        var type = typeof(PackProphet.Services.AppSession).Assembly.GetType(typeName)!;
        var page = RenderComponent<DynamicComponent>(p => p.Add(c => c.Type, type));

        var rails = page.FindAll(".page-rail").Select(r => r.Id).Where(id => id is { Length: > 0 }).ToHashSet();
        if (rails.Count == 0) return;

        // The rail is a column above 900px and a sheet below, and the sheet half is keyed to the
        // popover being OPEN rather than to a width -- `.sheet:popover-open`, which at two class
        // selectors outweighs the rule that makes it a column. So a rail that is open above the
        // fold paints as a full-width sheet over the page.
        //
        // Nothing may open one except the floating button, which the stylesheet hides at exactly
        // the width where the column takes over. A second opener anywhere else -- a bar button, a
        // link in an empty state -- is visible at that width and would put the rail into the one
        // state it has no layout for. js/railfold.js closes it if the viewport crosses while it is
        // open; this stops a page offering the trip in the first place.
        foreach (var opener in page.FindAll("[popovertarget]").ToArray())
        {
            var target = opener.GetAttribute("popovertarget");
            if (target is null || !rails.Contains(target)) continue;

            // Openers only. The sheet's own Done button points at the rail too, to close it, and
            // it lives inside the rail rather than in the fabs -- a control that can only ever
            // take the rail OUT of the state this is about.
            if (opener.GetAttribute("popovertargetaction") == "hide") continue;

            Assert.NotNull(opener.Closest(".rail-fabs"));
        }
    }

    [Theory]
    [MemberData(nameof(ParameterlessPages))]
    public async Task No_control_in_a_rail_carries_its_own_width(string typeName)
    {
        await ReadyAsync();

        var type = typeof(PackProphet.Services.AppSession).Assembly.GetType(typeName)!;
        var page = RenderComponent<DynamicComponent>(p => p.Add(c => c.Type, type));

        // Every control in a rail runs the column's full width, which is a stylesheet rule -- and
        // an inline style beats it however the rule is written. The pack log kept two widths from
        // its bar days, 11rem on the series picker and 7rem on the column count, so its three
        // controls came out three different lengths in a column sized to the widest of them: the
        // rail then reads as one control with two short ones under it rather than as a set.
        //
        // Swept rather than fixed twice, because the width belongs to the BAR these controls came
        // from. Any control moved into a rail from a toolbar arrives carrying one.
        foreach (var control in page.FindAll(".page-rail select, .page-rail input, .page-rail .btn").ToArray())
        {
            var style = control.GetAttribute("style");
            if (style is null) continue;

            Assert.DoesNotContain("width", style, StringComparison.OrdinalIgnoreCase);
        }
    }
}
