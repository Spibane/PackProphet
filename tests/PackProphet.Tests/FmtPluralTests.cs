namespace PackProphet.Tests;

using PackProphet.Text;

/// <summary>
/// The plural helper, which exists because "1 packs" was on four screens.
///
/// Worth its own tests for the double overload: the figure beside the noun has already been
/// rounded for display, and agreeing with the stored value instead of the shown one is how you get
/// "1 packs" back from a helper written to prevent it.
/// </summary>
public class FmtPluralTests
{
    [Theory]
    [InlineData(0, "packs")]
    [InlineData(1, "pack")]
    [InlineData(2, "packs")]
    public void A_count_of_one_is_the_only_singular(int n, string expected) =>
        Assert.Equal(expected, Fmt.S(n, "pack"));

    [Fact]
    public void An_irregular_plural_is_given_rather_than_guessed() =>
        Assert.Equal("do", Fmt.S(3, "does", "do"));

    [Theory]
    [InlineData(1.0, "pack")]
    // Printed with "0.#", so this reads "1 pack" on the page and has to say so.
    [InlineData(1.04, "pack")]
    [InlineData(1.5, "packs")]
    [InlineData(0.5, "packs")]
    public void A_decimal_figure_agrees_with_what_is_printed(double n, string expected) =>
        Assert.Equal(expected, Fmt.S(n, "pack"));

    [Theory]
    [InlineData("hourglass", "hourglasses")]
    [InlineData("box", "boxes")]
    [InlineData("match", "matches")]
    [InlineData("wish", "wishes")]
    // And the ordinary case is untouched, which is every other word the app counts.
    [InlineData("card", "cards")]
    [InlineData("pack", "packs")]
    [InlineData("variant", "variants")]
    public void A_sibilant_takes_es_rather_than_a_third_s(string singular, string expected)
    {
        // "12 pack hourglasss" is what the naive rule put on the log screen.
        Assert.Equal(expected, Fmt.S(2, singular));
        Assert.Equal(singular, Fmt.S(1, singular));
    }
}
