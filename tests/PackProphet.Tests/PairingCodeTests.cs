namespace PackProphet.Tests;

using PackProphet.Sync;

public class PairingCodeTests
{
    [Fact]
    public void A_new_code_parses_back_to_itself()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = PairingCode.New();
            Assert.Equal(PairingCode.TotalChars, code.Length);
            Assert.Equal(code, PairingCode.TryParse(code));
        }
    }

    [Fact]
    public void Codes_are_not_repeated()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 500; i++) Assert.True(seen.Add(PairingCode.New()));
    }

    [Fact]
    public void The_grouping_the_ui_shows_is_accepted_back()
    {
        var code = PairingCode.New();
        var shown = PairingCode.Format(code);

        Assert.Equal(14, shown.Length);                 // 12 characters, two dashes
        Assert.Equal(code, PairingCode.TryParse(shown));
    }

    [Fact]
    public void Case_spaces_and_dashes_are_all_ignored()
    {
        // A code copied out of a chat window, read off a screen, or typed with the grouping
        // reaches the app in every one of these shapes and has to mean the same thing.
        var code = PairingCode.New();
        var grouped = PairingCode.Format(code);

        Assert.Equal(code, PairingCode.TryParse(grouped));
        Assert.Equal(code, PairingCode.TryParse(grouped.ToLowerInvariant()));
        Assert.Equal(code, PairingCode.TryParse($"  {grouped}  "));
        Assert.Equal(code, PairingCode.TryParse(grouped.Replace("-", " ")));
        Assert.Equal(code, PairingCode.TryParse(grouped.Replace("-", "")));

        // An em dash, which is what a phone autocorrects a typed hyphen into.
        Assert.Equal(code, PairingCode.TryParse(grouped.Replace("-", "\u2014")));
    }

    [Fact]
    public void Letters_that_look_like_digits_are_read_as_the_digits()
    {
        var code = PairingCode.New();
        var typed = code.Replace('0', 'O').Replace('1', 'L');

        Assert.Equal(code, PairingCode.TryParse(typed));
    }

    [Fact]
    public void A_single_wrong_character_is_refused_rather_than_sent()
    {
        var code = PairingCode.New();

        // Every position, every substitution: the check character exists so a typo fails here and
        // not as an empty result from the server, which the user would read as "nothing to sync".
        var missed = 0;
        for (var i = 0; i < PairingCode.DataChars; i++)
        {
            foreach (var c in "0123456789ABCDEFGHJKMNPQRSTVWXYZ")
            {
                if (c == code[i]) continue;
                var typo = code.ToCharArray();
                typo[i] = c;
                if (PairingCode.TryParse(new string(typo)) is not null) missed++;
            }
        }

        Assert.Equal(0, missed);
    }

    [Fact]
    public void Swapping_two_neighbours_is_refused_unless_they_are_sixteen_apart()
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var missed = 0;

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var code = PairingCode.New();
            for (var i = 0; i < PairingCode.DataChars - 1; i++)
            {
                if (code[i] == code[i + 1]) continue;    // swapping equal characters changes nothing

                var swapped = code.ToCharArray();
                (swapped[i], swapped[i + 1]) = (swapped[i + 1], swapped[i]);
                var caught = PairingCode.TryParse(new string(swapped)) is null;

                // The documented exception, and the only one: a linear check over 32 symbols
                // cannot see a swap of two characters exactly half the alphabet apart. Asserting
                // the shape of the gap rather than a pass rate, so the day it widens, this fails.
                var apart = Math.Abs(alphabet.IndexOf(code[i]) - alphabet.IndexOf(code[i + 1]));
                if (apart == 16) Assert.False(caught);
                else if (!caught) missed++;
            }
        }

        Assert.Equal(0, missed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PACK-7K3M")]                 // too short
    [InlineData("PACK-7K3M-92QX-EXTRA")]      // too long
    [InlineData("PACK-7K3M-92Q!")]            // not in the alphabet
    [InlineData("PUCK-7K3M-92QX")]            // U has no sensible reading
    public void Anything_that_cannot_be_a_code_is_null(string? typed) =>
        Assert.Null(PairingCode.TryParse(typed));
}
