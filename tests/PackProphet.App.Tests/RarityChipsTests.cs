namespace PackProphet.App.Tests;

using PackProphet.Components;
using PackProphet.Data;
using PackProphet.Engine;

/// <summary>
/// The plan chips, and where Anomaly sits in them: beside the parallel foils, not at the top of
/// the ladder. It is a rung to the engine and a special printing on screen.
/// </summary>
public class RarityChipsTests : AppHost
{
    [Fact]
    public async Task Anomaly_sits_apart_from_the_ladder_after_the_divider()
    {
        await ReadyAsync();
        var plan = RarityPlan.Uniform([0]);

        var chips = RenderComponent<RarityChips>(p => p.Add(c => c.Plan, plan).Add(c => c.ShowFoils, true));

        var children = chips.Nodes.OfType<AngleSharp.Dom.IElement>().ToArray();
        var divider = Array.FindIndex(children, e => e.ClassList.Contains("bar-sep"));
        var anomaly = Array.FindIndex(children, e => e.TextContent.Contains(RarityRung.AnomalyMark));
        var foils = Array.FindIndex(children, e => e.TextContent.Contains(RarityLadder.FoilMark));
        var crown = Array.FindIndex(children, e => e.TextContent.Contains("♛"));

        Assert.True(divider >= 0, "no divider");
        Assert.True(crown < divider, "Crown should stay on the ladder side");
        Assert.True(foils > divider && anomaly > foils, "Anomaly should follow the foils, past the divider");
    }

    [Fact]
    public async Task The_divider_is_there_for_Anomaly_even_where_foils_are_not()
    {
        await ReadyAsync();

        var chips = RenderComponent<RarityChips>(p => p.Add(c => c.Plan, RarityPlan.Uniform([0])));

        var children = chips.Nodes.OfType<AngleSharp.Dom.IElement>().ToArray();
        Assert.DoesNotContain(children, e => e.TextContent.Contains(RarityLadder.FoilMark));
        Assert.True(Array.FindIndex(children, e => e.ClassList.Contains("bar-sep"))
                    < Array.FindIndex(children, e => e.TextContent.Contains(RarityRung.AnomalyMark)));
    }
}
