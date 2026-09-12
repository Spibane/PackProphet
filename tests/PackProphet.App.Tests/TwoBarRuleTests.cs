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
}
