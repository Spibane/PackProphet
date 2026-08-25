namespace PackProphet.Tests;

using PackProphet.State;

public class WishlistCodecTests
{
    [Fact]
    public void RoundTrips_owner_name_list_name_and_wanted_copies()
    {
        var list = new Wishlist("id", "Chase cards", new Dictionary<string, int>
        {
            ["a.webp"] = 2,
            ["b.webp"] = 1,
        });

        var code = WishlistCodec.Encode("Alex", list);
        var shared = WishlistCodec.TryDecode(code);

        Assert.NotNull(shared);
        Assert.Equal("Alex", shared!.OwnerName);
        Assert.Equal(list.Name, shared.ListName);
        Assert.Equal(list.Wanted, shared.Wanted);
    }

    [Fact]
    public void An_empty_wishlist_still_round_trips()
    {
        var list = new Wishlist("id", "Empty", new Dictionary<string, int>());
        var shared = WishlistCodec.TryDecode(WishlistCodec.Encode("Alex", list));

        Assert.NotNull(shared);
        Assert.Empty(shared!.Wanted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 at all!!")]
    [InlineData("dGhpcyBpcyBub3QgY29tcHJlc3NlZCBqc29u")] // valid base64, garbage once inflated
    public void Garbage_input_decodes_to_null_rather_than_throwing(string? code)
    {
        Assert.Null(WishlistCodec.TryDecode(code));
    }

    [Fact]
    public void The_code_is_URL_safe()
    {
        // Pasted into a query string or a fragment with no escaping, so it must contain none of
        // the characters that would need it.
        var list = new Wishlist("id", "A/B+C", Enumerable.Range(0, 40)
            .ToDictionary(i => $"card-{i}.webp", i => 1 + i % 2));

        var code = WishlistCodec.Encode("Owner/Name+Weird", list);

        Assert.DoesNotContain('+', code);
        Assert.DoesNotContain('/', code);
        Assert.DoesNotContain('=', code);
    }

    [Fact]
    public void The_in_game_name_survives_the_round_trip()
    {
        // The field a shared list is useless without: it is the only thing in the payload that
        // tells the reader who to send the card to.
        var list = new Wishlist("id", "Trades", new Dictionary<string, int> { ["A1:001.webp"] = 2 });

        var back = WishlistCodec.TryDecode(WishlistCodec.Encode("My alt", list, "Spibane"));

        Assert.NotNull(back);
        Assert.Equal("Spibane", back!.InGameName);
        Assert.Equal("My alt", back.OwnerName);
    }

    [Fact]
    public void A_link_made_before_the_in_game_name_existed_still_decodes()
    {
        // The field was added to a record that is already out in shared links. Those links carry
        // no such property, and a decode that threw would turn every one of them into "this link
        // is broken" rather than "this link predates a field".
        var list = new Wishlist("id", "Trades", new Dictionary<string, int> { ["A1:001.webp"] = 1 });

        var back = WishlistCodec.TryDecode(WishlistCodec.Encode("Alex", list));

        Assert.NotNull(back);
        Assert.Null(back!.InGameName);
        Assert.Equal("Alex", back.OwnerName);
    }

    [Fact]
    public void A_realistic_wishlist_makes_a_short_link()
    {
        // The whole point of sharing a wishlist rather than a collection: this should stay small
        // enough to paste anywhere without a 414, no fragment trick required.
        var list = new Wishlist("id", "Binder page", Enumerable.Range(0, 30)
            .ToDictionary(i => $"A1:card-{i:00}.webp", i => 1));

        var code = WishlistCodec.Encode("Alex", list);

        Assert.True(code.Length < 500, $"expected a short link, got {code.Length} chars");
    }
}
