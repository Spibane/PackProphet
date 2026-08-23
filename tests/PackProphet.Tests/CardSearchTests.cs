using PackProphet.Data;
using PackProphet.Domain;

namespace PackProphet.Tests;

public class CardSearchTests
{
    private static PocketCard Card(string name) =>
        new() { Set = "A1", Number = 1, Name = name, Rarity = "C", Image = "cPK_10_000010_00_X_C.webp" };

    private static CardFact Pokemon(string name, string energy, string stage,
                                    string? attackEffect = null, string? abilityEffect = null) =>
        new()
        {
            Name = name,
            Type = "Pokémon",
            Subtype = energy,
            Stage = stage,
            Attacks = new()
            {
                ["1"] = new CardAttack { Name = "Thunder Jolt", Damage = 30, Effect = attackEffect }
            },
            Ability = abilityEffect is null
                ? null
                : new CardAbility { Exists = true, Name = "Static Body", Effect = abilityEffect }
        };

    private static CardFact Trainer(string name, string kind, string? cardText = null) =>
        new() { Name = name, Type = "Trainer", Subtype = kind, CardText = cardText };

    [Fact]
    public void FindsCardsByWhatTheyDo_NotJustTheirName()
    {
        // The whole reason this exists: "what puts something to Sleep" is unanswerable by name.
        var snorlax = Pokemon("Snorlax", "Colorless", "Basic", attackEffect: "The Defending Pokémon is now Asleep.");
        var pikachu = Pokemon("Pikachu", "Lightning", "Basic", attackEffect: "Flip a coin.");

        var query = new CardQuery(Text: "asleep");

        Assert.True(CardSearch.Matches(Card("Snorlax"), snorlax, query));
        Assert.False(CardSearch.Matches(Card("Pikachu"), pikachu, query));
    }

    [Fact]
    public void SearchesAbilityTextToo()
    {
        var fact = Pokemon("Weezing", "Darkness", "Stage 1", abilityEffect: "Your opponent's Pokémon is Poisoned.");

        Assert.True(CardSearch.Matches(Card("Weezing"), fact, new CardQuery(Text: "poisoned")));
    }

    [Fact]
    public void NameHitsOutrankRulesTextHits()
    {
        // Someone typing "Sleep" may mean the card named Sleep or the effect; ranking names
        // first serves both without having to ask which.
        var named = Pokemon("Sleepy Snorlax", "Colorless", "Basic");
        var effect = Pokemon("Butterfree", "Grass", "Stage 2", attackEffect: "The Defending Pokémon is now Asleep.");

        var query = new CardQuery(Text: "sleep");

        Assert.Equal(CardSearch.Hit.NamePrefix, CardSearch.Rank(Card("Sleepy Snorlax"), named, query));
        Assert.Equal(CardSearch.Hit.RulesText, CardSearch.Rank(Card("Butterfree"), effect, query));
        Assert.True(CardSearch.Hit.NamePrefix > CardSearch.Hit.RulesText);
    }

    [Fact]
    public void ExactAndPrefixNameMatchesAreDistinguished()
    {
        var fact = Pokemon("Pikachu", "Lightning", "Basic");

        Assert.Equal(CardSearch.Hit.NameExact, CardSearch.Rank(Card("Pikachu"), fact, new CardQuery(Text: "pikachu")));
        Assert.Equal(CardSearch.Hit.NamePrefix, CardSearch.Rank(Card("Pikachu"), fact, new CardQuery(Text: "pika")));
        Assert.Equal(CardSearch.Hit.NameContains, CardSearch.Rank(Card("Pikachu"), fact, new CardQuery(Text: "kach")));
    }

    [Fact]
    public void ShortTermsDoNotSearchRulesText()
    {
        // Two-letter fragments match inside nearly every card's text, which is noise exactly
        // when the user has typed too little to disambiguate.
        var fact = Pokemon("Snorlax", "Colorless", "Basic", attackEffect: "is now Asleep");

        Assert.False(CardSearch.Matches(Card("Snorlax"), fact, new CardQuery(Text: "is")));
        Assert.True(CardSearch.Matches(Card("Snorlax"), fact, new CardQuery(Text: "now")));
    }

    [Fact]
    public void FacetsNarrowRatherThanWiden()
    {
        var supporter = Trainer("Professor's Research", "Supporter", "Draw 2 cards.");
        var item = Trainer("Poké Ball", "Item", "Put 1 random Basic Pokémon from your deck into your hand.");

        var onlySupporters = new CardQuery(Kind: "Trainer", Subtype: "Supporter");

        Assert.True(CardSearch.Matches(Card("Professor's Research"), supporter, onlySupporters));
        Assert.False(CardSearch.Matches(Card("Poké Ball"), item, onlySupporters));
    }

    [Fact]
    public void CombinesTextWithFacets()
    {
        var basicSleeper = Pokemon("Jigglypuff", "Colorless", "Basic", attackEffect: "is now Asleep.");
        var stageSleeper = Pokemon("Wigglytuff", "Colorless", "Stage 1", attackEffect: "is now Asleep.");

        var query = new CardQuery(Text: "asleep", Stage: "Basic");

        Assert.True(CardSearch.Matches(Card("Jigglypuff"), basicSleeper, query));
        Assert.False(CardSearch.Matches(Card("Wigglytuff"), stageSleeper, query));
    }

    [Fact]
    public void MatchesPokemonKindWithOrWithoutTheAccent()
    {
        var plain = new CardFact { Type = "Pokemon", Subtype = "Fire", Stage = "Basic" };
        var accented = new CardFact { Type = "Pokémon", Subtype = "Fire", Stage = "Basic" };

        var query = new CardQuery(Kind: "Pokémon");

        Assert.True(CardSearch.Matches(Card("Charmander"), plain, query));
        Assert.True(CardSearch.Matches(Card("Charmander"), accented, query));
    }

    [Fact]
    public void FacetSearchNeedsCardDetail_AndSaysSoByNotMatching()
    {
        // Card detail loads after startup and some sets have none at all. A facet filter must
        // then match nothing rather than everything: silently ignoring the filter would show
        // Trainers under "Basic Pokémon".
        Assert.False(CardSearch.Matches(Card("Unknown"), null, new CardQuery(Stage: "Basic")));

        // A plain name search still works without it, which is what keeps the builder usable
        // while detail is still downloading.
        Assert.True(CardSearch.Matches(Card("Unknown"), null, new CardQuery(Text: "unknown")));
    }

    /// <summary>
    /// The precomputed blob is an optimisation, so it must not change any answer. If these two
    /// paths ever disagree, the fast path is silently returning different results to the one the
    /// rest of these tests pin.
    /// </summary>
    [Theory]
    [InlineData("asleep")]
    [InlineData("poisoned")]
    [InlineData("coin")]
    [InlineData("snorlax")]
    [InlineData("nothingmatchesthis")]
    public void PrecomputedRulesTextAgreesWithWalkingTheCard(string term)
    {
        var fact = Pokemon("Snorlax", "Colorless", "Basic",
                           attackEffect: "Flip a coin. The Defending Pokémon is now Asleep.",
                           abilityEffect: "Your opponent's Active Pokémon is Poisoned.");
        var card = Card("Snorlax");
        var query = new CardQuery(Text: term);

        var walked = CardSearch.Rank(card, fact, query);
        var precomputed = CardSearch.Rank(card, fact, query, CardSearch.RulesBlob(fact));

        Assert.Equal(walked, precomputed);
    }

    [Fact]
    public void RulesBlobExcludesFlavourText()
    {
        var fact = new CardFact
        {
            Type = "Pokémon", Subtype = "Psychic", Stage = "Basic",
            FlavourText = "It is said to fall asleep for days at a time.",
            Attacks = new() { ["1"] = new CardAttack { Name = "Pound", Damage = 10 } }
        };

        var blob = CardSearch.RulesBlob(fact);

        Assert.Contains("pound", blob);
        Assert.DoesNotContain("asleep", blob);
    }

    [Fact]
    public void RulesBlobOfAMissingFactIsEmptyRatherThanNull()
    {
        // The picker holds one blob per card and some cards have no detail; a null would have to
        // be special-cased at every use.
        Assert.Equal("", CardSearch.RulesBlob(null));
    }

    [Fact]
    public void FlavourTextIsNotSearched()
    {
        // Prose about the creature would match a dozen Pokémon whose rules do nothing of the kind.
        var fact = new CardFact
        {
            Type = "Pokémon", Subtype = "Psychic", Stage = "Basic",
            FlavourText = "It is said to fall asleep for days at a time."
        };

        Assert.False(CardSearch.Matches(Card("Drowzee"), fact, new CardQuery(Text: "asleep")));
    }
}
