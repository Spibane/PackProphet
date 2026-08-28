using PackProphet.Vision;

namespace PackProphet.Tests;

/// <summary>
/// Reading the copy count off a card's badge.
///
/// The digits recognised here are the game's own, in a fixed-pitch font, so this is a much easier
/// problem than card art — and a much less forgiving one, because a misread digit is silent. A card
/// recorded as 1 copy when the badge said 14 is indistinguishable afterwards from one the user
/// entered by hand.
/// </summary>
public class CountReaderTests
{
    private static DigitGlyph Glyph(string grey, double aspect) => new() { Grey = grey, Aspect = aspect };

    [Fact]
    public void AllTenDigitsHaveAnExemplar()
    {
        // Missing one would not fail loudly: every count containing it would simply come back
        // unreadable, which looks like a feature that sometimes does not work.
        Assert.Equal(10, CountReader.Covered.Count);
        foreach (var digit in "0123456789") Assert.Contains(digit, CountReader.Covered);
    }

    [Fact]
    public void EveryExemplarIsTheRightShapeForTheGrid()
    {
        Assert.All(CountReader.Table,
                   e => Assert.Equal(CountReader.GlyphWidth * CountReader.GlyphHeight, e.Grey.Length));
    }

    [Fact]
    public void EveryExemplarWithAPeerClassifiesWithoutItself()
    {
        // The only generalisation evidence there is. These exemplars are the digits three
        // screenshots happened to contain, so there is no held-out set — but classifying each one
        // against the table with itself removed does test whether the digit is recognised from a
        // DIFFERENT sample of it, which is the question that matters. Digits with a single exemplar
        // are skipped, because removing their only sample can only fail.
        var table = CountReader.Table;
        var checked_ = 0;

        for (var i = 0; i < table.Count; i++)
        {
            var mine = table[i];
            var hasPeer = table.Where((e, j) => j != i && e.Digit == mine.Digit).Any();
            if (!hasPeer) continue;

            checked_++;
            var got = CountReader.Classify(Glyph(mine.Grey, mine.Aspect), skip: i);
            Assert.Equal(mine.Digit, got);
        }

        // Six of the ten digits have a second sample; if that ever drops, this test is testing less
        // than it appears to.
        Assert.True(checked_ >= 14, $"only {checked_} exemplars had a peer to be tested against");
    }

    [Fact]
    public void AnExemplarIsRecognisedAsItself()
    {
        Assert.All(CountReader.Table,
                   e => Assert.Equal(e.Digit, CountReader.Classify(Glyph(e.Grey, e.Aspect))));
    }

    [Fact]
    public void TheDigitsOfARealBadgeBecomeTheRightNumber()
    {
        // The two-digit cases from the reference screenshot: "11", "14" and "20". Verifies the
        // ordering and the carry rather than the classifier — leading digit first, so 14 is not 41.
        var one = Table('1');
        var four = Table('4');
        var two = Table('2');
        var zero = Table('0');

        Assert.Equal(11, CountReader.Read([one, one]));
        Assert.Equal(14, CountReader.Read([one, four]));
        Assert.Equal(20, CountReader.Read([two, zero]));
        Assert.Equal(1, CountReader.Read([one]));
    }

    [Fact]
    public void AnUnreadableDigitLosesTheWholeCount()
    {
        // All or nothing, because a partly-read count is not a smaller answer — it is a different
        // number. Dropping an unreadable leading digit turns 14 into 4.
        var nonsense = Glyph(new string('0', CountReader.GlyphWidth * CountReader.GlyphHeight), 0.69);

        Assert.Null(CountReader.Read([nonsense]));
        Assert.Null(CountReader.Read([Table('1'), nonsense]));
        Assert.Null(CountReader.Read([nonsense, Table('4')]));
    }

    [Fact]
    public void NothingToReadIsNotZero()
    {
        // Null means "this screen did not say". Zero would mean "you own none", which would erase a
        // card — and the game never prints a zero anyway, since a card you own none of is not on the
        // list at all.
        Assert.Null(CountReader.Read(null));
        Assert.Null(CountReader.Read([]));
        Assert.Null(CountReader.Read([Table('0')]));
    }

    [Fact]
    public void MoreDigitsThanACountCanHaveIsRefused()
    {
        var one = Table('1');
        Assert.Null(CountReader.Read([one, one, one, one]));
    }

    [Fact]
    public void AGlyphOfTheWrongSizeIsRefusedRatherThanPadded()
    {
        Assert.Null(CountReader.Classify(Glyph("abc", 0.69)));
    }

    [Fact]
    public void ANarrowGlyphIsNeverReadAsAWideDigit()
    {
        // The aspect guard. A 1 measures about 0.38 wide for its height and every other digit
        // measures 0.63 or more, so the shapes never even compete — which removes the confusions
        // that a bitmap comparison on its own was making.
        var one = Table('1');
        var eight = Table('8');

        Assert.Equal('1', CountReader.Classify(one));
        Assert.Equal('8', CountReader.Classify(eight));

        // The 8's own pixels, claimed to be as narrow as a 1, match nothing.
        Assert.Null(CountReader.Classify(Glyph(eight.Grey, one.Aspect)));
    }

    private static DigitGlyph Table(char digit)
    {
        var e = CountReader.Table.First(x => x.Digit == digit);
        return Glyph(e.Grey, e.Aspect);
    }
}
