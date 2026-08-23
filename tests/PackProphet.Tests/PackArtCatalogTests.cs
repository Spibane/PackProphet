using PackProphet.Data;

namespace PackProphet.Tests;

public class PackArtCatalogTests
{
    private static PackArtCatalog Real() =>
        new(Snapshot.Expansions(), Snapshot.Index().AllPackKeys);

    [Fact]
    public void MatchesEveryOpenablePack_DespiteTheTwoProjectsNamingThemDifferently()
    {
        // The awkward case: for single-pack sets one source says "Paradox Drive" and the other
        // just "Booster", so nothing matches by name. Every openable pack must still resolve,
        // or the log page silently falls back to low-resolution art.
        var catalog = Real();
        var unmatched = Snapshot.Index().OpenablePackKeys
            .Where(k => catalog.UrlFor(k) is null)
            .ToArray();

        Assert.Empty(unmatched);
    }

    [Fact]
    public void MatchesMultiPackSetsByName()
    {
        var catalog = Real();
        Assert.Contains("a1-mewtwo", catalog.UrlFor("A1:Mewtwo"));
        Assert.Contains("a1-charizard", catalog.UrlFor("A1:Charizard"));
        Assert.Contains("a1-pikachu", catalog.UrlFor("A1:Pikachu"));

        // Punctuation and case must not defeat the match: "Ho-Oh" against "hooh".
        Assert.Contains("a4-hooh", catalog.UrlFor("A4:Ho-Oh"));
        Assert.Contains("b1-megaaltaria", catalog.UrlFor("B1:Mega Altaria"));
    }

    [Fact]
    public void MatchesSinglePackSetsByPosition_WhereNamesDisagree()
    {
        var catalog = Real();

        // Ours are "Paradox Drive" and "Deluxe"; theirs are both called "Booster".
        Assert.Contains("b3a-booster", catalog.UrlFor("B3a:Paradox Drive"));
        Assert.Contains("a4b-booster", catalog.UrlFor("A4b:Deluxe"));
    }

    [Fact]
    public void NeverGuesses_WhenASetHasSeveralPacksAndNoNameMatches()
    {
        // Position matching is only safe at one-to-one. With several packs a wrong pairing
        // would show the wrong booster, which is worse than showing a low-resolution one.
        var expansions = new List<ExpansionInfo>
        {
            new() { Id = "zz", Packs = [new() { Id = "zz-a", Name = "Alpha" }, new() { Id = "zz-b", Name = "Beta" }] }
        };
        var catalog = new PackArtCatalog(expansions, ["ZZ:Unrelated One", "ZZ:Unrelated Two"]);

        Assert.Null(catalog.UrlFor("ZZ:Unrelated One"));
        Assert.Null(catalog.UrlFor("ZZ:Unrelated Two"));
    }

    [Fact]
    public void UnknownPacksAndEmptyInputYieldNull_NotAnException()
    {
        Assert.Null(Real().UrlFor("NOPE:Nothing"));
        Assert.Null(PackArtCatalog.Empty.UrlFor("A1:Mewtwo"));
        Assert.Equal(0, PackArtCatalog.Empty.Count);
    }

    [Fact]
    public void PromoPacksDoNotResolve_AndThatIsTheRightAnswer()
    {
        // Our promo groupings are "Vol. 1".."Vol. 13"; theirs are "Promo V1", plus "Campaign",
        // "Shop", "Wonder Pick" and others. Names do not match, and with 13 against 15 entries
        // position matching cannot apply either — so nothing resolves.
        //
        // That is correct rather than a gap: a guess here would show the wrong booster, and
        // promo groupings are never offered as openable packs anyway.
        var catalog = Real();
        var promoKeys = Snapshot.Index().AllPackKeys.Where(k => k.StartsWith("PROMO-")).ToArray();

        Assert.NotEmpty(promoKeys);
        Assert.All(promoKeys, k => Assert.Null(catalog.UrlFor(k)));

        // And the set-code translation itself works, which is what would matter if the names
        // ever aligned.
        Assert.Equal("PROMO-A", SetCatalog.SeriesFromCode("PROMO-A") is "A" ? "PROMO-A" : "?");
    }
}
