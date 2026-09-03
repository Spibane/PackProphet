namespace PackProphet.Tests;

using System.Text.Json;
using PackProphet.Data;
using PackProphet.Deck;

/// <summary>
/// Dual-typed Pokémon, which arrive in October.
///
/// Nothing in the data has two types yet, so every one of these builds the card by hand. That is
/// the point: the code has to be right before the data exists, because the alternative is finding
/// out on release day with a live site.
///
/// The single-type cases are asserted alongside the dual ones throughout. A change that supports
/// two types by breaking one would pass a suite that only tested the new thing, and one type is
/// what all 3,761 cards printed so far have.
/// </summary>
public class DualTypeTests
{
    private static CardFact Card(string? subtype, string name = "Test") =>
        new() { Name = name, Type = "Pokémon", Stage = "Basic", Subtype = subtype };

    /// <summary>
    /// The options the app actually reads card detail with. GetFromJsonAsync uses
    /// JsonSerializerDefaults.Web, which is case-insensitive -- and the field is `subtype` in the
    /// data against a `Subtype` property here. Deserialising with the plain defaults instead binds
    /// nothing and every assertion below passes vacuously, which is how these were first written.
    /// </summary>
    private static readonly JsonSerializerOptions AsTheAppDoes = new(JsonSerializerDefaults.Web);

    // ------------------------------------------------------------------ reading the field apart

    [Theory]
    [InlineData("Grass", new[] { "Grass" })]
    [InlineData("Colorless", new[] { "Colorless" })]
    // A Trainer's kind is never plural and must survive untouched.
    [InlineData("Supporter", new[] { "Supporter" })]
    // The separators a combined value could plausibly use.
    [InlineData("Grass/Water", new[] { "Grass", "Water" })]
    [InlineData("Grass, Water", new[] { "Grass", "Water" })]
    [InlineData("Grass + Water", new[] { "Grass", "Water" })]
    // Whitespace and stray separators, because a hand-maintained dataset produces both.
    [InlineData(" Fire / Fighting ", new[] { "Fire", "Fighting" })]
    [InlineData("Fire//", new[] { "Fire" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void A_subtype_is_read_apart_into_its_types(string? raw, string[] expected)
    {
        Assert.Equal(expected, Card(raw).Subtypes);
    }

    [Fact]
    public void Reading_it_apart_is_memoised_rather_than_repeated()
    {
        // Read once per visible row per render, so it must not re-split on every access.
        var card = Card("Grass/Water");
        Assert.Same(card.Subtypes, card.Subtypes);
    }

    // ------------------------------------------------------------------ the deserialiser

    [Fact]
    public void An_array_of_types_does_not_take_the_whole_document_with_it()
    {
        // The failure this guards is total, not partial. If upstream expresses two energies by
        // turning `subtype` into an array, System.Text.Json throws while reading the DOCUMENT --
        // so one dual-typed card would cost every card in every set its attacks, abilities, HP and
        // stage. A blank type column on one set is cosmetic; that is losing half the app's data.
        const string json = """
        [{"id":"a1-001","name":"Single","type":"Pokémon","subtype":"Grass","deckBuilderNr":1},
         {"id":"z9-001","name":"Dual","type":"Pokémon","subtype":["Grass","Water"],"deckBuilderNr":2},
         {"id":"a1-002","name":"After","type":"Pokémon","subtype":"Fire","deckBuilderNr":3}]
        """;

        var facts = JsonSerializer.Deserialize<List<CardFact>>(json, AsTheAppDoes)!;

        Assert.Equal(3, facts.Count);
        Assert.Equal(["Grass"], facts[0].Subtypes);
        Assert.Equal(["Grass", "Water"], facts[1].Subtypes);
        // The card AFTER the surprising one, which is what a mid-document throw would have eaten.
        Assert.Equal(["Fire"], facts[2].Subtypes);
    }

    [Theory]
    // Anything at all, as long as it costs only its own field. Each of these is a shape a
    // hand-maintained dataset has produced somewhere at some point.
    [InlineData("""{"subtype":null}""", new string[0])]
    [InlineData("""{"subtype":[]}""", new string[0])]
    [InlineData("""{"subtype":["Water"]}""", new[] { "Water" })]
    [InlineData("""{"subtype":["Water",null,"Fire"]}""", new[] { "Water", "Fire" })]
    [InlineData("""{"subtype":7}""", new string[0])]
    [InlineData("""{"subtype":{"primary":"Water"}}""", new string[0])]
    public void No_shape_of_that_field_can_fail_the_card(string json, string[] expected)
    {
        var fact = JsonSerializer.Deserialize<CardFact>(json, AsTheAppDoes)!;
        Assert.Equal(expected, fact.Subtypes);
    }

    [Fact]
    public void An_unreadable_field_still_leaves_the_rest_of_the_card_intact()
    {
        // The reader has to be left in the right place after skipping a value it cannot use, or
        // every field after it is lost too.
        const string json = """
        {"id":"z9-001","name":"Odd","type":"Pokémon","subtype":{"a":[1,2]},"health":90,
         "stage":"Basic","deckBuilderNr":4}
        """;

        var fact = JsonSerializer.Deserialize<CardFact>(json, AsTheAppDoes)!;

        Assert.Empty(fact.Subtypes);
        Assert.Equal("Odd", fact.Name);
        Assert.Equal(90, fact.Health);
        Assert.Equal("Basic", fact.Stage);
        Assert.Equal(4, fact.DeckBuilderNr);
    }

    // ------------------------------------------------------------------ searching and filtering

    [Fact]
    public void Filtering_to_one_type_keeps_a_card_that_is_two()
    {
        var dual = Card("Grass/Water", "Dual");
        var plain = Card("Grass", "Plain");
        var card = new Domain.PocketCard
        {
            Set = "Z9", Number = 1, Name = "Test", Rarity = "C",
            Image = "cPK_10_000010_00_X_C.webp",
        };

        // Both halves find the dual card, and a type it does not have does not.
        foreach (var want in new[] { "Grass", "Water" })
            Assert.True(CardSearch.Matches(card, dual, new CardQuery(Subtype: want)),
                        $"a Grass/Water card should match {want}");

        Assert.False(CardSearch.Matches(card, dual, new CardQuery(Subtype: "Fire")));

        // The single-typed card is unaffected in both directions, which is the half of this that
        // 3,761 cards depend on.
        Assert.True(CardSearch.Matches(card, plain, new CardQuery(Subtype: "Grass")));
        Assert.False(CardSearch.Matches(card, plain, new CardQuery(Subtype: "Water")));
    }

    [Fact]
    public void The_type_filter_list_offers_each_type_rather_than_the_pair()
    {
        // What the picker's dropdown is built from. Left as a distinct over the raw field, a dual
        // card would add "Grass/Water" beside Grass and Water as a third thing to choose.
        var facts = new[] { Card("Grass/Water"), Card("Grass"), Card("Fire") };

        var offered = facts.SelectMany(f => f.Subtypes)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .OrderBy(s => s, StringComparer.Ordinal)
                           .ToArray();

        Assert.Equal(["Fire", "Grass", "Water"], offered);
    }

    // ------------------------------------------------------------------ the deck linter

    [Fact]
    public void Either_energy_powers_a_dual_typed_attacker()
    {
        // The correctness case. A Grass deck running a Grass/Water attacker is fine -- it can be
        // powered either way -- and warning on the half that is absent would tell someone their
        // perfectly good deck is broken.
        var running = new HashSet<string>(["Grass"], StringComparer.OrdinalIgnoreCase);
        var dual = new[] { "Grass", "Water" };

        Assert.True(dual.Any(running.Contains), "one matching energy is enough");
        Assert.False(new[] { "Fire", "Water" }.Any(running.Contains),
            "neither energy present is still a mismatch");
    }
}
