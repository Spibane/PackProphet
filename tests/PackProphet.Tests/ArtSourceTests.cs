namespace PackProphet.Tests;

using PackProphet.Domain;

/// <summary>
/// The art chain, and in particular the part of it that decides how many requests a card grid
/// makes.
///
/// The whole reason this is a chain is that card data and card art are published on different
/// cadences, so the newest set — the one people are opening — regularly has data and no art. The
/// chain answers that. What it must not do is answer it by asking three sources about every card:
/// the back catalogue is over 3,700 cards, and a local candidate offered for all of them would be
/// that many 404s against this app's own host on every visit.
///
/// <see cref="ArtSource"/> holds static state, set once during boot. These reset it around every
/// test so an assertion cannot depend on which one ran first.
/// </summary>
public class ArtSourceTests : IDisposable
{
    public ArtSourceTests()
    {
        ArtSource.UseVendored(null, null);
        ArtSource.UseTcgDex(null);
    }

    public void Dispose()
    {
        ArtSource.UseVendored(null, null);
        ArtSource.UseTcgDex(null);
    }

    private static PocketCard Card(string set, int number) =>
        new() { Set = set, Number = number, Name = "x", Image = "x.webp" };

    [Fact]
    public void With_no_manifest_the_chain_is_limitless_then_the_mirror()
    {
        // A development build looks like this: no manifest, so nothing is known about TCGdex.
        var chain = ArtSource.Candidates(Card("A1", 1));

        Assert.Equal(2, chain.Count);
        Assert.StartsWith(ArtSource.Limitless, chain[0], StringComparison.Ordinal);
        Assert.StartsWith(ArtSource.Mirror, chain[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_vendored_set_is_tried_first_and_still_keeps_its_fallbacks()
    {
        ArtSource.UseVendored(["B4a"]);

        var chain = ArtSource.Candidates(Card("B4a", 7));

        Assert.Equal(3, chain.Count);
        Assert.Equal("art/B4a/7.webp", chain[0]);
        Assert.StartsWith(ArtSource.Limitless, chain[1], StringComparison.Ordinal);
        Assert.StartsWith(ArtSource.Mirror, chain[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_vendored_set_gets_a_local_candidate()
    {
        // The guard that matters. Offering the local url for every card would spend one request
        // per card of the back catalogue discovering that this deploy never vendored that set.
        ArtSource.UseVendored(["B4a"]);

        Assert.DoesNotContain(
            ArtSource.Candidates(Card("A1", 1)),
            url => url.StartsWith(ArtSource.OwnOrigin + "/", StringComparison.Ordinal));
    }

    [Fact]
    public void The_local_candidate_is_relative_so_it_survives_project_page_hosting()
    {
        // A rooted "/art/..." assumes the app is at a domain root. This one is, and the deploy
        // asserts it, but a fork served from /<repo>/ would send every request to a path its host
        // does not have. Relative resolves against index.html's <base href>.
        ArtSource.UseVendored(["B4a"]);

        var local = ArtSource.Candidates(Card("B4a", 1))[0];
        Assert.False(local.StartsWith('/'), $"{local} must not be rooted");
        Assert.DoesNotContain("://", local);
    }

    [Theory]
    // Lower case, and zero-padded to three — verified against the mirror, which 404s otherwise.
    [InlineData("A1", 1, "a1/001.webp")]
    [InlineData("B4", 233, "b4/233.webp")]
    [InlineData("A4b", 79, "a4b/079.webp")]
    // The promos are two letters there and a word here.
    [InlineData("PROMO-A", 1, "pa/001.webp")]
    [InlineData("PROMO-B", 86, "pb/086.webp")]
    public void The_mirror_spells_a_card_its_own_way(string set, int number, string tail)
    {
        var chain = ArtSource.Candidates(Card(set, number));
        Assert.Equal($"{ArtSource.Mirror}/{tail}", chain[^1]);
    }

    [Theory]
    // Upper case, zero-padded to three, the set code twice -- and the promos are P-A and P-B.
    [InlineData("A1", 1, "A1/A1_001_EN.webp")]
    [InlineData("B4b", 233, "B4b/B4b_233_EN.webp")]
    [InlineData("PROMO-A", 7, "P-A/P-A_007_EN.webp")]
    [InlineData("PROMO-B", 86, "P-B/P-B_086_EN.webp")]
    public void Limitless_spells_a_card_its_own_way(string set, int number, string tail)
    {
        Assert.Equal($"{ArtSource.Limitless}/{tail}", ArtSource.Candidates(Card(set, number))[0]);
    }

    [Fact]
    public void TCGdex_is_tried_first_only_for_the_sets_the_deploy_found_it_has()
    {
        // It trails the game by months. Offered for a set it has not reached, it would be a 404
        // ahead of every card of the newest set -- the one people are opening.
        ArtSource.UseTcgDex(["A1", "PROMO-A"]);

        Assert.Equal($"{ArtSource.TcgDex}/A1/001/high.webp", ArtSource.Candidates(Card("A1", 1))[0]);
        Assert.Equal($"{ArtSource.TcgDex}/P-A/007/high.webp", ArtSource.Candidates(Card("PROMO-A", 7))[0]);
        Assert.StartsWith(ArtSource.Limitless, ArtSource.Candidates(Card("A1", 1))[1], StringComparison.Ordinal);

        Assert.DoesNotContain(ArtSource.Candidates(Card("B4b", 1)),
                              url => url.StartsWith(ArtSource.TcgDex, StringComparison.Ordinal));
    }

    [Fact]
    public void Booster_art_follows_the_manifest_and_escapes_the_pack_name()
    {
        Assert.StartsWith(ArtSource.DatabasePacks, PocketCard.PackArtUrl("Team Rocket"),
                          StringComparison.Ordinal);

        ArtSource.UseVendored(["B4a"], ["Team Rocket"]);

        // A space in a pack name has to survive into the URL as an escape, both ways round.
        Assert.Equal("art/packs/Team%20Rocket.webp", PocketCard.PackArtUrl("Team Rocket"));
        Assert.StartsWith(ArtSource.DatabasePacks, PocketCard.PackArtUrl("Mewtwo"),
                          StringComparison.Ordinal);
    }

    [Fact]
    public void A_set_logo_follows_the_manifest_too()
    {
        Assert.StartsWith(ArtSource.DatabaseSets, PocketCard.SetLogoUrl("B4a"),
                          StringComparison.Ordinal);

        ArtSource.UseVendored(["B4a"]);

        Assert.Equal("art/sets/LOGO_expansion_B4a_en_US.webp", PocketCard.SetLogoUrl("B4a"));
        Assert.StartsWith(ArtSource.DatabaseSets, PocketCard.SetLogoUrl("A1"),
                          StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_reports_the_head_of_its_chain_and_the_tail_separately()
    {
        // The contract the markup and js/imgloader.js share: data-src is one url, data-src-alt is
        // the rest pipe-separated. A pipe cannot occur in any of them, which is what makes the
        // split safe.
        ArtSource.UseVendored(["B4a"]);
        var card = Card("B4a", 3);
        var chain = ArtSource.Candidates(card);

        Assert.Equal(chain[0], card.ArtUrl);
        Assert.Equal(string.Join('|', chain.Skip(1)), card.ArtFallbackUrls);
        Assert.DoesNotContain('|', card.ArtUrl);
    }

    [Fact]
    public void A_manifest_naming_nothing_is_the_same_as_no_manifest()
    {
        ArtSource.UseVendored(["B4a"]);
        Assert.Equal(3, ArtSource.Candidates(Card("B4a", 1)).Count);

        // Both shapes the deploy can write when it vendored nothing.
        ArtSource.UseVendored([]);
        Assert.Equal(2, ArtSource.Candidates(Card("B4a", 1)).Count);

        ArtSource.UseVendored(["", "  ", null!]);
        Assert.Equal(2, ArtSource.Candidates(Card("B4a", 1)).Count);
    }

    [Fact]
    public void Set_codes_are_matched_without_regard_to_case()
    {
        // The manifest is generated from the card data, so the codes agree today. This is one
        // spelling change away from silently vendoring art the app never asks for.
        ArtSource.UseVendored(["b4a"]);
        Assert.Equal("art/B4a/1.webp", ArtSource.Candidates(Card("B4a", 1))[0]);
    }

    [Fact]
    public void Own_art_is_an_absolute_address_under_the_apps_base()
    {
        // Relative, it was resolved against the stylesheet that reads --art, in css/, and 404'd.
        ArtSource.UseVendored(["B4a"], ["Team Rocket"], new Uri("https://packprophet.spibane.com/"));

        Assert.Equal("https://packprophet.spibane.com/art/B4a/1.webp", ArtSource.Candidates("B4a", 1)[0]);
        Assert.Equal("https://packprophet.spibane.com/art/packs/Team%20Rocket.webp", ArtSource.PackArt("Team Rocket"));

        // A fork served from a subpath keeps it.
        ArtSource.UseVendored(["B4a"], null, new Uri("https://someone.github.io/PackProphet/"));
        Assert.Equal("https://someone.github.io/PackProphet/art/B4a/1.webp", ArtSource.Candidates("B4a", 1)[0]);
    }
}
