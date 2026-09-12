namespace PackProphet.App.Tests;

using PackProphet.Pages;
using PackProphet.State;

/// <summary>
/// History, the one page in the app with no verdict at all.
///
/// A record answers "what happened". Prefacing it with a recommendation would invent one — the
/// page has nothing to advise, because everything on it has already been done.
/// </summary>
public class HistoryPageTests : AppHost
{
    private static string Flat(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>Some packs in the log, which is what this page is a reading of.</summary>
    private void Log(int packs)
    {
        var set = Session.Index.OpenableSets.First();
        var cards = Session.Index.All
            .Where(c => c.Set == set)
            .Take(5)
            .Select(c => c.OwnershipKey)
            .ToList();

        var pack = Session.Odds.PriceablePacks.First(p => p.StartsWith(set, StringComparison.Ordinal));

        Session.Mutate(p =>
        {
            var log = new List<PackOpenEvent>(p.PackLog);
            for (var i = 0; i < packs; i++)
                log.Add(new PackOpenEvent(
                    DateTimeOffset.Now.AddDays(-(i % 20)), set, pack, "", new List<string>(cards)));

            return p with { PackLog = log };
        });
    }

    private async Task<IRenderedComponent<History>> PageAsync(int packs = 30)
    {
        await ReadyAsync();
        Log(packs);
        return RenderComponent<History>();
    }

    [Fact]
    public async Task The_page_offers_no_verdict()
    {
        // The rule that puts this page in the Ledger job rather than in Answer. Everything here
        // has already happened, so there is nothing to recommend — and a headline figure over a
        // record would be the page picking one of its own numbers to care about.
        var page = await PageAsync();

        Assert.Empty(page.FindAll(".verdict-lead"));
        Assert.Empty(page.FindAll(".verdict"));
        Assert.Empty(page.FindAll(".box-strip"));
    }

    [Fact]
    public async Task Every_figure_stands_beside_the_others_rather_than_above_them()
    {
        // A stat grid rather than a headline, because none of these outranks the rest: packs
        // opened, cards per pack, duplicates and the streak answer different questions and one of
        // them promoted to display size would be the page claiming otherwise.
        var page = await PageAsync();

        var stats = page.FindAll(".summary .stat").ToArray();
        Assert.True(stats.Length >= 6, $"only {stats.Length} figures in the grid");

        Assert.All(stats, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.QuerySelector(".label")?.TextContent));
            Assert.False(string.IsNullOrWhiteSpace(s.QuerySelector(".value")?.TextContent));
        });
    }

    [Fact]
    public async Task The_day_chart_caption_reads_the_bars_rather_than_stating_a_rule()
    {
        // "The free allowance is 2 a day" states a rule and leaves the reader to apply it to
        // thirty bars. Saying what a bar of two MEANS is the same rule already applied, which is
        // the only form of it that helps while looking at the picture.
        var page = await PageAsync();

        var caption = Flat(page.Find(".bars").ParentElement!.TextContent);
        Assert.Contains("is a day you kept up", caption);
    }

    [Fact]
    public async Task The_odds_check_names_the_sample_it_rests_on()
    {
        // "Small samples swing wildly" is true of every sample ever taken and says nothing about
        // this one. The number is what decides whether a row a fifth out is worth a second look.
        var page = await PageAsync(30);

        var markup = Flat(page.Markup);
        Assert.Contains("is a small sample", markup);
        Assert.Contains("ordinary variance rather than evidence the model is wrong", markup);
    }

    [Fact]
    public async Task The_bars_are_readable_without_seeing_them()
    {
        // The chart is a picture, so its data exists as text twice over: a summary on the image
        // itself, and the full figures behind a disclosure. Thirty rows read out before the rest
        // of the page is a worse answer than a control that says what it opens.
        var page = await PageAsync();

        var bars = page.Find(".bars");
        Assert.Equal("img", bars.GetAttribute("role"));
        Assert.False(string.IsNullOrWhiteSpace(bars.GetAttribute("aria-label")));

        Assert.Contains(page.FindAll("summary"),
                        s => s.TextContent.Contains("Show these as numbers"));
    }
}
