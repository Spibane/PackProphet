namespace PackProphet.Tests;

/// <summary>
/// Pack sizes, which the log screen needs in order to notice a miscount. The observed side of the
/// odds check comes straight from what was tapped, so four taps on a five-card pack reads later as
/// the model having been wrong — the one way to quietly corrupt the app's own accuracy figures.
/// </summary>
public class CardCountTests
{
    private static readonly PackProphet.Data.PullRates Rates = Snapshot.Rates();

    [Fact]
    public void A_set_reports_every_size_its_variants_can_hold()
    {
        // A single expected size would flag an ordinary opening as wrong: "Regular Pack +1" is a
        // real six-card pack at about 8%, and A4b has a four-card variant.
        var a1 = Rates.CardCounts("A1");

        Assert.NotEmpty(a1);
        Assert.Contains(5, a1);
        Assert.All(a1, n => Assert.InRange(n, 4, 6));
    }

    [Fact]
    public void The_likely_size_is_the_one_you_almost_always_open()
    {
        // Regular Pack is ~99.95% of openings, so that is the size to quote.
        Assert.Equal(5, Rates.LikelyCardCount("A1"));
    }

    [Fact]
    public void The_deluxe_set_is_the_one_that_breaks_the_five_card_assumption()
    {
        // A4b holds four, which is exactly why the hint asks the data instead of assuming.
        var counts = Rates.CardCounts("A4b");

        Assert.NotEmpty(counts);
        Assert.Contains(4, counts);
        Assert.Equal(4, Rates.LikelyCardCount("A4b"));
    }

    [Fact]
    public void An_unpriced_set_reports_nothing_rather_than_guessing()
    {
        // B4 has no published rates. Saying "5" would invent a check the data cannot support.
        Assert.Empty(Rates.CardCounts("B4"));
        Assert.Equal(0, Rates.LikelyCardCount("B4"));

        Assert.Empty(Rates.CardCounts("not-a-set"));
    }

    [Fact]
    public void Every_priced_set_reports_a_plausible_size()
    {
        foreach (var set in Rates.ModelledSets)
        {
            var counts = Rates.CardCounts(set);
            Assert.NotEmpty(counts);
            Assert.All(counts, n => Assert.InRange(n, 4, 6));
        }
    }
}
