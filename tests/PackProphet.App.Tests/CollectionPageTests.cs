namespace PackProphet.App.Tests;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PackProphet.Engine;
using PackProphet.Pages;

/// <summary>
/// The Collection page, which had two bugs of the same kind: it worked the completion rule out for
/// itself instead of asking the target, and drifted from it in two directions.
/// </summary>
public class CollectionPageTests : AppHost
{
    private async Task<IRenderedComponent<Collection>> PageAsync()
    {
        await ReadyAsync();
        return RenderComponent<Collection>();
    }

    [Fact]
    public async Task Every_filter_renders()
    {
        // Each of these is a different predicate over the card list, and one of them — the
        // evolution-gap filter — reads a set that is rebuilt from the collection. A filter that
        // throws takes the whole page with it.
        var page = await PageAsync();

        var options = page.FindAll("select")
            .SelectMany(s => s.QuerySelectorAll("option"))
            .Select(o => o.GetAttribute("value"))
            .Where(v => v is "all" or "missing" or "owned" or "dupes" or "needed" or "gaps")
            .Distinct()
            .ToArray();

        Assert.Contains("needed", options);

        foreach (var value in options)
        {
            var select = page.FindAll("select")
                .First(s => s.QuerySelectorAll("option").Any(o => o.GetAttribute("value") == value));

            select.Change(value);
            Assert.False(string.IsNullOrWhiteSpace(page.Markup), $"filter '{value}' rendered nothing");
        }
    }

    [Fact]
    public async Task The_target_count_follows_the_parallel_foil_setting()
    {
        // The bug: the page counted all 139 parallel foils toward the target however the foil
        // setting was set, so it reported work outstanding that the ranking called complete.
        await ReadyAsync();

        var foilSet = Session.FoilSets.FirstOrDefault();
        Assert.NotNull(foilSet);

        Session.SetFoilCopies(1);
        var withFoils = Session.TargetProgress([foilSet!]).Wanted;

        Session.SetFoilCopies(0);
        var without = Session.TargetProgress([foilSet!]).Wanted;

        Assert.True(withFoils > without,
            $"foils should add to the target: {withFoils} vs {without}");

        // And the page must agree with the engine, which is the whole point of asking it.
        var outstanding = new RarityLadderTarget(foilSet!, Session.Plan(foilSet), Session.FoilRule)
            .Outstanding(Session.Index, Session.Owned);

        Assert.Equal(without, outstanding.Count);
    }

    [Fact]
    public async Task A_per_set_override_is_honoured_in_the_all_sets_view()
    {
        // The second bug in the same place: across several sets the owned count was measured
        // against the DEFAULT plan while the total used each set's own override.
        await ReadyAsync();

        var sets = Session.Index.OpenableSets.Take(3).ToArray();
        var before = Session.TargetProgress(sets).Wanted;

        // Collect only the commons in one of them.
        Session.SetPlan(RarityPlan.Uniform(Snapshot("C")), sets[0]);
        var after = Session.TargetProgress(sets).Wanted;

        Assert.True(after < before, $"narrowing one set's plan should shrink the total: {after} vs {before}");
        Assert.True(Session.HasPlanOverride(sets[0]));
    }

    private IReadOnlySet<int> Snapshot(params string[] codes) =>
        codes.Select(c => Session.Index.Ladder.IndexOf(c)!.Value).ToHashSet();

    [Fact]
    public async Task The_headline_target_figure_matches_what_the_engine_says()
    {
        // The assertion that would have caught the original bug. The page prints "target N / M" in
        // its header and used to compute both numbers itself, so the header disagreed with the
        // ranking on the same set. Comparing the rendered text against the engine is what notices a
        // page doing its own arithmetic.
        await ReadyAsync();

        var foilSet = Session.FoilSets.First();
        Session.SetFoilCopies(0);

        // Through the URL, because the page reads the set from the query string — which is also
        // how the command palette and a bookmark reach it.
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("set", foilSet));

        var page = RenderComponent<Collection>();

        var expected = Session.TargetProgress([foilSet]);

        Assert.Contains($"target {expected.Satisfied} / {expected.Wanted}", page.Markup);

        // And it moves when the setting does, rather than being right once by luck.
        Session.SetFoilCopies(1);
        page.Render();

        var withFoils = Session.TargetProgress([foilSet]);
        Assert.True(withFoils.Wanted > expected.Wanted);
        Assert.Contains($"target {withFoils.Satisfied} / {withFoils.Wanted}", page.Markup);
    }

    [Fact]
    public async Task The_evolution_gap_bar_can_be_dismissed_and_stays_dismissed()
    {
        await ReadyAsync();

        // Own an evolution without its lower stage, which is what opens a gap.
        var zard = Session.Index.All.First(c => c.Name == "Charizard");
        Session.SetCount(zard, 1);

        var page = RenderComponent<Collection>();
        Assert.Contains("cannot evolve", page.Markup);

        page.Find(".gap-strip .x").Click();

        Assert.False(Session.ShowGapStrip);
        Assert.DoesNotContain("cannot evolve", page.Markup);
    }
}
