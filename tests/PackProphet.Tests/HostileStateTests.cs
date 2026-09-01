namespace PackProphet.Tests;

using PackProphet.Deck;
using PackProphet.State;

/// <summary>
/// State that arrives from somewhere other than this browser.
///
/// Before cloud sync, a payload came from this app's own localStorage or from a file the user
/// picked, and the parse guarded mostly against truncation and older schemas. Sync makes a remote
/// document an ordinary input on every launch, so anything the parse lets through is something the
/// rest of the app has to survive — and several places refuse malformed data by throwing, which on
/// a render path means a page that cannot be opened.
/// </summary>
public class HostileStateTests
{
    private static string Payload(string decks) => $$$"""
        {"schemaVersion":3,"activeProfileId":"default","prefs":{},
         "profiles":[{"id":"default","name":"P","collection":{},
           "targets":{"defaultPlan":{"0":1},"planBySet":{}},
           "decks":[{{{decks}}}],
           "chaseLists":[],"packLog":[],"wonderLog":[],
           "resources":{"wonder":{"balance":0,"hourglasses":0},
             "trade":{"balance":0,"hourglasses":0},
             "packHourglasses":0,"packPointsBySet":{},"shinedust":0,"premium":false}}]}
        """;

    [Fact]
    public void A_deck_carrying_an_impossible_card_identity_cannot_reach_the_share_code()
    {
        // DeckCodec.Create throws on a non-positive identity, and it runs during render to build a
        // deck's QR code. A payload holding one used to survive the parse intact, so opening that
        // deck threw instead of rendering.
        var state = StateSerializer.Deserialize(
            Payload("""{"id":"d","name":"X","deckBuilderNrs":[0,-5,12,34],"energies":[]}"""));

        Assert.NotNull(state);
        var deck = state!.Active.Decks[0];

        Assert.Equal([12, 34], deck.DeckBuilderNrs);
        Assert.Null(Record.Exception(() => DeckCodec.Create(deck.DeckBuilderNrs, deck.Energies)));
    }

    [Fact]
    public void A_deck_carrying_more_energies_than_can_be_encoded_is_trimmed()
    {
        // The same shape of refusal: Create throws above three energy types.
        var state = StateSerializer.Deserialize(
            Payload("""{"id":"d","name":"X","deckBuilderNrs":[12],"energies":[1,2,3,4,5,6]}"""));

        Assert.NotNull(state);
        var deck = state!.Active.Decks[0];

        Assert.True(deck.Energies.Count <= DeckCodec.MaxEnergyTypes);
        Assert.Null(Record.Exception(() => DeckCodec.Create(deck.DeckBuilderNrs, deck.Energies)));
    }

    [Fact]
    public void Repeated_energies_do_not_eat_the_allowance()
    {
        var state = StateSerializer.Deserialize(
            Payload("""{"id":"d","name":"X","deckBuilderNrs":[12],"energies":[1,1,1,1,2]}"""));

        Assert.Equal([EnergyType.Fire, EnergyType.Water], state!.Active.Decks[0].Energies);
    }

    [Fact]
    public void A_deck_left_with_no_cards_at_all_still_parses_rather_than_dropping_the_save()
    {
        // Refusing the whole payload would be worse than an empty deck: the rest of the collection
        // is fine, and a state that will not load is a collection the user cannot reach.
        var state = StateSerializer.Deserialize(
            Payload("""{"id":"d","name":"X","deckBuilderNrs":[0,0],"energies":[]}"""));

        Assert.NotNull(state);
        Assert.Empty(state!.Active.Decks[0].DeckBuilderNrs);
    }

    [Fact]
    public void Hostile_text_in_a_synced_name_is_carried_as_text_and_nothing_else()
    {
        // Blazor encodes interpolated content, so this is a regression guard on the parse rather
        // than on rendering: the point is that the value arrives as characters, with no decoding
        // step of our own that could turn it into anything else.
        const string nasty = "<script>alert(1)</script>";
        var state = StateSerializer.Deserialize(
            Payload($$"""{"id":"d","name":"{{nasty}}","deckBuilderNrs":[12],"energies":[]}"""));

        Assert.Equal(nasty, state!.Active.Decks[0].Name);
    }
}
