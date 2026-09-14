using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Text;

namespace PackProphet.Tests;

/// <summary>
/// What a keyboard produces has to reach what a publisher printed.
///
/// "poke ball" found nothing, because the card is Poké Ball. Nobody reaches for Option-E to search
/// a card list, so a search box that requires the accent is a search box that says a card the user
/// is holding does not exist. The apostrophe is the same bug in a different glyph: the index spells
/// the Team Rocket line with U+2019 and a keyboard makes the ASCII one, and most people type
/// neither.
///
/// Both directions are tested, not just the one that was reported. A user who does paste the
/// accented spelling must still find the card, which is what folding BOTH sides buys and what
/// folding only the query would have broken.
/// </summary>
public class SearchFoldTests
{
    private static PocketCard Card(string name) =>
        new() { Set = "A1", Number = 1, Name = name, Rarity = "C", Image = "cPK_10_000010_00_X_C.webp" };

    private static CardFact Trainer(string name, string? cardText = null) =>
        new() { Name = name, Type = "Trainer", Subtype = "Item", CardText = cardText };

    [Theory]
    [InlineData("poke ball")]
    [InlineData("Poke Ball")]
    [InlineData("poké ball")]
    [InlineData("POKE BALL")]
    [InlineData("poke")]
    [InlineData("ball")]
    public void An_accented_name_is_reachable_from_a_keyboard(string typed)
    {
        var card = Card("Poké Ball");

        Assert.True(CardSearch.Matches(card, Trainer("Poké Ball"), new CardQuery(Text: typed)));
    }

    [Theory]
    [InlineData("team rocket’s meowth")]   // pasted from the page
    [InlineData("team rocket's meowth")]   // typed with an apostrophe
    [InlineData("team rockets meowth")]    // typed without one, which is what most people do
    [InlineData("rockets meowth")]
    public void A_typographic_apostrophe_is_reachable_however_it_is_typed(string typed)
    {
        var card = Card("Team Rocket’s Meowth");

        Assert.True(CardSearch.Matches(card, null, new CardQuery(Text: typed)));
    }

    [Fact]
    public void Farfetchd_is_reachable_without_its_apostrophe()
    {
        Assert.True(CardSearch.Matches(Card("Farfetch’d"), null, new CardQuery(Text: "farfetchd")));
    }

    [Fact]
    public void Two_accents_in_one_name_both_fold()
    {
        Assert.True(CardSearch.Matches(Card("Flabébé"), null, new CardQuery(Text: "flabebe")));
    }

    /// <summary>
    /// The rank a folded name gets is the rank the unfolded one would have got. Folding at the
    /// comparison rather than in the ranking would have quietly demoted every accented card to a
    /// contains-hit, so Poké Ball would have sorted below anything whose rules text says "ball".
    /// </summary>
    [Fact]
    public void Folding_does_not_cost_a_name_its_rank()
    {
        var card = Card("Poké Ball");
        var fact = Trainer("Poké Ball");

        Assert.Equal(CardSearch.Hit.NameExact, CardSearch.Rank(card, fact, new CardQuery(Text: "poke ball")));
        Assert.Equal(CardSearch.Hit.NamePrefix, CardSearch.Rank(card, fact, new CardQuery(Text: "poke b")));
        Assert.Equal(CardSearch.Hit.NameContains, CardSearch.Rank(card, fact, new CardQuery(Text: "ke bal")));
    }

    /// <summary>
    /// Rules text names cards, so it needs the same fold: "put a Poké Ball into your hand" is
    /// text someone searches for with the word they can type.
    /// </summary>
    [Fact]
    public void Rules_text_is_folded_as_well_as_names()
    {
        var fact = Trainer("Professor’s Research", "Put 1 random Poké Ball into your hand.");

        Assert.Equal(CardSearch.Hit.RulesText,
                     CardSearch.Rank(Card("Professor’s Research"), fact, new CardQuery(Text: "poke ball")));
    }

    /// <summary>
    /// The precomputed blob and the direct walk have to agree, because which one runs is a
    /// performance decision made by the caller and not a difference in what matches. The blob is
    /// folded once when it is built and the walk folds as it goes; two folds in two places is
    /// exactly the pair that can disagree.
    /// </summary>
    [Theory]
    [InlineData("poke ball")]
    [InlineData("poké ball")]
    [InlineData("professors research")]
    [InlineData("nothing here")]
    public void The_blob_and_the_direct_walk_reach_the_same_answer(string typed)
    {
        var card = Card("Professor’s Research");
        var fact = Trainer("Professor’s Research", "Put 1 random Poké Ball into your hand.");
        var query = new CardQuery(Text: typed);

        Assert.Equal(CardSearch.Rank(card, fact, query),
                     CardSearch.Rank(card, fact, query, CardSearch.RulesBlob(fact)));
    }

    /// <summary>
    /// The deliberate limit, so nobody widens the table without reading this. ♀ and ♂ are the
    /// whole of the difference between two Nidoran, and a fold that dropped them would leave the
    /// two cards indistinguishable by name.
    /// </summary>
    [Fact]
    public void The_gender_symbols_are_not_folded_away()
    {
        Assert.True(CardSearch.Matches(Card("Nidoran♀"), null, new CardQuery(Text: "Nidoran♀")));
        Assert.False(CardSearch.Matches(Card("Nidoran♂"), null, new CardQuery(Text: "Nidoran♀")));
    }

    /// <summary>
    /// A name with nothing to fold comes back as the same instance. This runs over every card name
    /// on every keystroke, and the claim in <see cref="SearchKey"/> that it does not allocate for
    /// the 3,829 plain-ASCII names of 3,879 is worth holding to.
    /// </summary>
    [Fact]
    public void An_ascii_name_is_not_copied()
    {
        var name = "Pikachu ex";

        Assert.Same(name, SearchKey.Fold(name));
    }

    [Fact]
    public void Nothing_folds_to_nothing()
    {
        Assert.Equal("", SearchKey.Fold(null));
        Assert.Equal("", SearchKey.Fold(""));
    }

    /// <summary>
    /// A name is still a name after folding: the accent goes, the letters and the spacing do not.
    /// A fold that also stripped spaces or case would make "poke ball" match "Pokeball" and
    /// "POKE" rank as an exact hit on a card called "Poke", neither of which is wanted here.
    /// </summary>
    [Theory]
    [InlineData("Poké Ball", "Poke Ball")]
    [InlineData("Team Rocket’s Meowth", "Team Rockets Meowth")]
    [InlineData("Flabébé", "Flabebe")]
    [InlineData("Nidoran♀", "Nidoran♀")]
    public void Folding_changes_the_accents_and_nothing_else(string printed, string expected) =>
        Assert.Equal(expected, SearchKey.Fold(printed));
}
