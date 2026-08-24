namespace PackProphet.App.Tests;

using PackProphet.Pages;

/// <summary>
/// The page that shipped a crash. Its guard tested "no trades and something unpayable", and the
/// following else was left as a catch-all, so a comparison with shares but no trades fell through
/// it and indexed an empty list. Every diamond is routed to a share, so that is the ordinary shape
/// of a comparison between two of your own accounts.
/// </summary>
public class ProfileCompareTests : AppHost
{
    [Fact]
    public async Task Says_there_is_nothing_to_compare_with_one_collection()
    {
        await ReadyAsync();

        var cut = RenderComponent<ProfileCompare>();

        Assert.Contains("only one collection", cut.Markup);
    }

    [Fact]
    public async Task Renders_a_comparison_that_is_all_shares_and_no_trades()
    {
        // The regression. The alt holds spare DIAMONDS the main is missing, which a Share carries
        // for free — so there is plenty to report and not one trade among it.
        await ReadyAsync();

        var diamonds = Session.Index.All
            .Where(c => c.Rarity is "C" or "U" or "R" && !c.IsPromo && c.Openable)
            .DistinctBy(c => c.OwnershipKey)
            .Take(6)
            .ToArray();

        Assert.Equal(6, diamonds.Length);

        var alt = Session.CreateProfile("Alt");
        Session.SwitchProfile(alt);
        foreach (var card in diamonds) Session.SetCount(card, 2);   // one spare each

        Session.SwitchProfile(Session.Profiles.First(p => p.Id != alt).Id);

        var cut = RenderComponent<ProfileCompare>();

        Assert.Contains("can just be sent", cut.Markup);
        Assert.Contains("Free to send", cut.Markup);
    }

    [Fact]
    public async Task Reports_a_want_it_cannot_pay_for_without_pretending_a_trade_exists()
    {
        // Above the diamonds a Share cannot help, and with no spare at that rarity there is no
        // trade either. Both facts have to reach the page.
        await ReadyAsync();

        // The default plan is diamonds only, so a 2-star is not wanted by default and would not
        // appear at all, which is why the plan has to say it is collected first.
        var stars2 = Session.Index.Ladder.IndexOf("SR")!.Value;
        Session.SetPlan(PackProphet.Engine.RarityPlan.Uniform(
            [.. Session.Plan().WantedTiers, stars2]));

        var stars = Session.Index.All
            .Where(c => c.Rarity == "SR" && !c.IsPromo)
            .DistinctBy(c => c.OwnershipKey)
            .Take(2)
            .ToArray();

        var alt = Session.CreateProfile("Alt");
        Session.SwitchProfile(alt);
        foreach (var card in stars) Session.SetCount(card, 2);

        Session.SwitchProfile(Session.Profiles.First(p => p.Id != alt).Id);

        var cut = RenderComponent<ProfileCompare>();

        Assert.Contains("nothing to pay with", cut.Markup);
    }

    [Fact]
    public async Task Switching_the_active_collection_reverses_the_comparison()
    {
        // The direction of every row depends on which profile is active, and the switcher lives in
        // the top bar — so the page has to rebuild, not merely redraw, when it changes.
        await ReadyAsync();

        var diamond = Session.Index.All.First(c => c.Rarity == "C" && !c.IsPromo && c.Openable);

        var alt = Session.CreateProfile("Alt");
        var main = Session.Profiles.First(p => p.Id != alt).Id;

        Session.SwitchProfile(alt);
        Session.SetCount(diamond, 2);
        Session.SwitchProfile(main);

        var cut = RenderComponent<ProfileCompare>();
        Assert.Contains("can just be sent", cut.Markup);

        // Now from the alt's side: it is the one with the spare, so nothing comes IN to it.
        Session.SwitchProfile(alt);
        cut.Render();

        Assert.Contains("Nothing this way", cut.Markup);
    }
}
