namespace PackProphet.App.Tests;

using System.Reflection;
using Microsoft.AspNetCore.Components;

/// <summary>
/// Renders every routable page, on a brand-new profile and again on a populated one.
///
/// Discovered by reflection rather than listed, so a page added later is covered without anyone
/// remembering to add it here. The compare page shipped with a crash on the empty case, which a
/// hand-maintained list would have omitted.
///
/// The empty profile is the important half: it is the state every user starts in, the least
/// exercised while building, and where a page indexes a list that has nothing in it — which
/// compiles cleanly and passes every engine test.
/// </summary>
public class EveryPageRendersTests : AppHost
{
    /// <summary>
    /// Pages whose route takes a parameter are excluded: without an id they are a different test
    /// (see the not-found behaviour tests), not a smoke test of this page.
    /// </summary>
    public static TheoryData<string> ParameterlessPages
    {
        get
        {
            // Distinct by type, not by route: Collection answers both "/" and "/collection",
            // and xUnit drops theory cases whose arguments repeat — so without this the page
            // is silently dropped from every page-driven theory rather than tested once.
            var data = new TheoryData<string>();
            foreach (var name in Routable().Where(r => !r.Route.Contains('{'))
                                           .Select(r => r.Type.FullName!)
                                           .Distinct())
                data.Add(name);
            return data;
        }
    }

    private static IEnumerable<(Type Type, string Route)> Routable() =>
        typeof(PackProphet.Services.AppSession).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false } && typeof(IComponent).IsAssignableFrom(t))
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>()
                              .Select(a => (Type: t, Route: a.Template)))
            .OrderBy(r => r.Route, StringComparer.Ordinal);

    [Fact]
    public void The_reflection_sweep_actually_finds_the_pages()
    {
        // A guard on the guard: if this ever finds nothing, every test below would pass while
        // testing nothing at all.
        var routes = Routable().ToArray();

        Assert.True(routes.Length >= 15, $"only found {routes.Length} routable pages");
        Assert.Contains(routes, r => r.Route == "/collection");
        Assert.Contains(routes, r => r.Route == "/compare");
    }

    [Theory]
    [MemberData(nameof(ParameterlessPages))]
    public async Task Renders_on_a_brand_new_profile(string typeName)
    {
        await ReadyAsync();

        var rendered = Render(typeName);

        // Markup, not a crash. An exception during render is what this whole file is for; an empty
        // body would mean the page silently gave up.
        Assert.False(string.IsNullOrWhiteSpace(rendered), $"{typeName} rendered nothing");
    }

    [Theory]
    [MemberData(nameof(ParameterlessPages))]
    public async Task Renders_with_a_collection_a_log_and_a_second_profile(string typeName)
    {
        // The other end: enough state that every branch has something to show. Between the two,
        // most pages are rendered along both their "nothing yet" and "plenty" paths.
        Populate();
        await ReadyAsync();

        var rendered = Render(typeName);

        Assert.False(string.IsNullOrWhiteSpace(rendered), $"{typeName} rendered nothing");
    }

    /// <summary>
    /// Renders a page by name. DynamicComponent rather than the generic RenderComponent, so the
    /// list can be driven from reflection.
    /// </summary>
    private string Render(string typeName)
    {
        var type = typeof(PackProphet.Services.AppSession).Assembly.GetType(typeName)!;
        return RenderComponent<DynamicComponent>(p => p.Add(c => c.Type, type)).Markup;
    }

    /// <summary>
    /// A plausible mid-game state: some cards, a couple of logged packs, resources entered, a
    /// second collection to compare against.
    /// </summary>
    private void Populate()
    {
        Session.InitAsync().GetAwaiter().GetResult();

        var owned = Session.Index.All.DistinctBy(c => c.OwnershipKey).Take(400).ToArray();
        foreach (var card in owned.Take(200)) Session.SetCount(card, 1);
        foreach (var card in owned.Skip(200).Take(50)) Session.SetCount(card, 3);

        // Logged through Mutate, the same way the log screen does it — there is no session method
        // for this, and inventing one just for a test would put a second path into the app.
        var parts = Session.Index.OpenablePackKeys.First().Split(':', 2);
        var keys = owned.Take(5).Select(c => c.OwnershipKey).ToList();
        Session.Mutate(p => p with
        {
            PackLog = [.. p.PackLog,
                       new PackOpenEvent(DateTimeOffset.Now.AddHours(-2), parts[0], parts[1],
                                         "Regular Pack", keys)]
        });

        Session.SetPackResources(packHourglasses: 240, shinedust: 50_000, premium: false);
        Session.SetPool(trade: false, balance: 3, hourglasses: 24);
        Session.SetPool(trade: true, balance: 2, hourglasses: 12);
        Session.CreateProfile("Alt");
    }
}
