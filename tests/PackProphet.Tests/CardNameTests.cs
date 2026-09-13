namespace PackProphet.Tests;

using PackProphet.Text;

/// <summary>
/// Two datasets, one printed name.
///
/// The card index and the card-detail dataset are compiled by different people and they punctuate
/// differently. Every name-based question the app asks crosses that boundary: "do I own anything
/// called this", "is this deck's chain complete", "which evolutions am I missing" — so an ordinal
/// comparison is not a stricter version of the right answer, it is the wrong one.
/// </summary>
public class CardNameTests
{
    [Fact]
    public void The_two_datasets_spell_the_same_card_differently()
    {
        // Verbatim from the two files, and the reason this class exists: the index carries the
        // typographic apostrophe and the facts carry the ASCII one. Eleven of B4a's
        // pre-evolutions are named this way, and every one of them was reported as a card the
        // player did not own.
        const string index = "Team Rocket’s Houndour";
        const string facts = "Team Rocket's Houndour";

        Assert.NotEqual(index, facts);                       // the bug, in one line
        Assert.True(CardName.Same(index, facts));
    }

    [Theory]
    [InlineData("Farfetch’d", "Farfetch'd")]
    [InlineData("Clemont’s Backpack", "Clemont's Backpack")]
    [InlineData("Professor´s Research", "Professor's Research")]
    [InlineData("Kidʼs Room", "Kid's Room")]
    public void Every_mark_that_means_an_apostrophe_reads_as_one(string a, string b) =>
        Assert.True(CardName.Same(a, b));

    [Fact]
    public void Case_and_stray_spacing_do_not_make_two_cards()
    {
        Assert.True(CardName.Same("bulbasaur", "Bulbasaur"));
        Assert.True(CardName.Same("  Mega  Gyarados ex ", "Mega Gyarados ex"));
        Assert.True(CardName.Same("Team Rocket s Meowth", "Team Rocket s Meowth"));
    }

    [Fact]
    public void Different_cards_stay_different()
    {
        // The point of folding only punctuation. These are four separate cards in the game and
        // anything that merged them would be worse than the bug it fixed.
        Assert.False(CardName.Same("Meowth", "Alolan Meowth"));
        Assert.False(CardName.Same("Gardevoir", "Gardevoir ex"));
        Assert.False(CardName.Same("Nidoran♀", "Nidoran♂"));
        Assert.False(CardName.Same("Charmander", ""));
    }

    [Fact]
    public void It_works_as_a_dictionary_key()
    {
        // How every caller actually uses it: a table built from one dataset and looked up with
        // names from the other. Equal names must also hash equally or the lookup misses.
        var owned = new HashSet<string>(CardName.Comparer) { "Team Rocket’s Voltorb" };

        Assert.Contains("Team Rocket's Voltorb", owned);
        Assert.DoesNotContain("Team Rocket's Electrode", owned);
    }

    [Fact]
    public void Nothing_is_not_something()
    {
        Assert.True(CardName.Same(null, ""));
        Assert.True(CardName.Same("   ", null));
        Assert.False(CardName.Same("Pikachu", null));
    }
}
