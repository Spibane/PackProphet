namespace PackProphet.Tests;

using PackProphet.Deck;

/// <summary>
/// The codec is the only place the app parses bytes it did not write: a share code comes off a
/// screenshot, which may be of anyone's screen, a QR from an unrelated app, or a photo that
/// decoded imperfectly. So the contract is not "handles valid codes" but "never fails in a way the
/// caller cannot catch".
/// </summary>
public class DeckCodecFuzzTests
{
    /// <summary>Deterministic, so a failure can be reproduced from the seed alone.</summary>
    private static IEnumerable<byte[]> Corpus(int seed, int count)
    {
        var rng = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var bytes = new byte[rng.Next(0, 64)];
            rng.NextBytes(bytes);
            yield return bytes;
        }
    }

    [Fact]
    public void Arbitrary_bytes_either_parse_or_raise_FormatException()
    {
        // Anything else - IndexOutOfRange, OverflowException, ArgumentException - would escape the
        // import screen's catch and take the app down on a bad photo.
        foreach (var bytes in Corpus(seed: 20260823, count: 4000))
        {
            var code = Convert.ToBase64String(bytes);
            try
            {
                var parsed = DeckCodec.Parse(code);

                // A successful parse must also be internally consistent, or the caller gets
                // nonsense it cannot tell from a real deck.
                Assert.Equal(parsed.CardCount, parsed.Trainers.Count + parsed.Pokemon.Count);
                Assert.True(parsed.Energies.Count <= DeckCodec.MaxEnergyTypes);
                Assert.All(parsed.AllCards, nr => Assert.True(nr > 0, $"card id {nr} in {code}"));
            }
            catch (FormatException)
            {
                // The documented failure.
            }
        }
    }

    [Fact]
    public void TryParse_never_throws_on_anything()
    {
        foreach (var bytes in Corpus(seed: 7, count: 1500))
            DeckCodec.TryParse(Convert.ToBase64String(bytes));

        // Text that is not base64 at all, which is what a mis-decoded QR usually yields.
        foreach (var junk in new[] { "", "  ", "!!!!", "not base64", "AAAA====", "\0\0", new string('A', 10_000) })
            DeckCodec.TryParse(junk);
    }

    [Fact]
    public void A_zero_card_id_is_refused_rather_than_stored()
    {
        // Create rejects a non-positive identity, so Parse accepting one let a deck be SAVED with a
        // card that can never resolve against the index — it shows as a permanently missing card
        // with no name, and nothing in the app can explain why.
        var withZero = Convert.ToBase64String(new byte[] { 1, 0, 0, 0, 0, 0 });

        Assert.Throws<FormatException>(() => DeckCodec.Parse(withZero));
        Assert.Null(DeckCodec.TryParse(withZero));
    }

    [Fact]
    public void A_truncated_real_code_fails_cleanly_at_every_length()
    {
        // The realistic corruption: a photo cut off, or a QR read that dropped trailing modules.
        var full = DeckCodec.Create([1, 1, 4, 1000008], [EnergyType.Fire]);
        var bytes = Convert.FromBase64String(full);

        for (var length = 0; length < bytes.Length; length++)
        {
            var code = Convert.ToBase64String(bytes[..length]);
            try { DeckCodec.Parse(code); }
            catch (FormatException) { }
        }
    }
}
