namespace PackProphet.App.Tests;

using System.Globalization;
using PackProphet.Pages;
using PackProphet.State;

/// <summary>
/// The date axis under "packs per day".
///
/// A label is far wider than the day it marks, so which days get one is a spacing question rather
/// than a calendar one. The bug: the axis marked every Monday and both ends, and 24 August is a
/// Monday one day before the 25th — so the last two labels printed on top of each other.
/// </summary>
public class HistoryAxisTests : AppHost
{
    /// <summary>A log with a pack on each of the last few days, so the chart has something to draw.</summary>
    protected override AppState Start()
    {
        var state = AppState.Fresh();
        var log = Enumerable.Range(0, 5)
            .Select(i => new PackOpenEvent(DateTimeOffset.Now.AddDays(-i), "A1", "Pikachu",
                                           "unknown", ["a.webp"]))
            .ToList();

        return state with { Profiles = [state.Active with { PackLog = log }] };
    }

    /// <summary>The column each drawn label sits over, from its left percentage.</summary>
    private static double[] Positions(IRenderedComponent<History> page) =>
        page.FindAll(".bars-axis span")
            .Select(s => s.GetAttribute("style")!)
            .Select(s => double.Parse(s["left:".Length..].TrimEnd('%', ';'), CultureInfo.InvariantCulture))
            .ToArray();

    [Fact]
    public async Task No_two_dates_are_printed_on_top_of_each_other()
    {
        await ReadyAsync();
        var page = RenderComponent<History>();

        var at = Positions(page);
        Assert.True(at.Length >= 2, "the axis should carry at least both ends");

        // A tenth of the chart is roughly one label's width at this font size. Anything closer
        // than that is two dates in the same place, which is what the reader saw.
        foreach (var (a, b) in at.Zip(at.Skip(1)))
            Assert.True(b - a >= 9.9, $"labels at {a}% and {b}% are too close to read");
    }

    [Fact]
    public async Task Both_ends_of_the_range_are_still_labelled()
    {
        // Dropping a crowded mark must never cost the range itself: the two ends are what say
        // which thirty days the chart covers.
        await ReadyAsync();
        var page = RenderComponent<History>();

        var ends = page.FindAll(".bars-axis span.edge");
        Assert.Equal(2, ends.Count);

        var today = DateTime.Now.Date;
        Assert.Equal(today.AddDays(-29).ToString("d MMM"), ends.First().TextContent.Trim());
        Assert.Equal(today.ToString("d MMM"), ends.Last().TextContent.Trim());
    }
}
