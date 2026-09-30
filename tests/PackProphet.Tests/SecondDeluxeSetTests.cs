using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Engine;

namespace PackProphet.Tests;

/// <summary>
/// A second Deluxe set, B4b "Deluxe Pack: Mega", which is what pins down that nothing about being a
/// Deluxe set was tied to the code "A4b".
///
/// Written before B4b's data existed, against a copy of A4b under a new set code and a pack also
/// called "Deluxe". 2.11.0 published the real thing, and the rates tests now run against it: 429
/// cards, 166 foil pairs where A4b has 139, and no published rates of its own yet, so it borrows.
///
/// The copy got one thing wrong. B4b's pack is "Deluxe Pack Mega", not "Deluxe", so the two Deluxe
/// sets do not share a pack name after all. The booster art tests below were about exactly that
/// sharing, and they stay, about a hypothetical third Deluxe set that does share it -- see
/// <see cref="Shared"/>.
/// </summary>
public class SecondDeluxeSetTests
{
    private static SetCatalog Sets() => new(Snapshot.PublishedSets(), Snapshot.Index().BySet.Keys);

    // ---- borrowing rates -------------------------------------------------------------

    [Fact]
    public void ADeluxeSetBorrowsFromADeluxeSet()
    {
        // The newest measured set is an ordinary one, and its five-card pack would cost B4b its
        // guarantee, its shape and its foils.
        Assert.Equal("A4b", Snapshot.Rates().DonorFor("B4b", deluxe: true, Sets()));
    }

    [Fact]
    public void AnOrdinarySetStillBorrowsTheStandardRates()
    {
        var rates = Snapshot.Rates();
        Assert.Equal(rates.StandardDonor(Sets()), rates.DonorFor("B4", deluxe: false, Sets()));
        Assert.NotEqual("A4b", rates.DonorFor("B4", deluxe: false, Sets()));
    }

    [Fact]
    public void ADeluxeSetIsOfferedNoDonor_RatherThanAnOrdinaryOne()
    {
        var withoutDeluxe = Snapshot.RatesWithout("A4b");

        Assert.Null(withoutDeluxe.DonorFor("B4b", deluxe: true, Sets()));
    }

    [Fact]
    public void ADeluxeSetDoesNotLendToItself()
    {
        // Once B4b has published rates it is the newest Deluxe donor, for the NEXT Deluxe set, but
        // a set asking for a donor must never be handed its own name.
        var published = Snapshot.Rates().ModelledSets
            .ToDictionary(s => s, s => Snapshot.Rates().Published(s).ToDictionary());
        published["B4b"] = published["A4b"];
        var rates = new PullRates(published);

        Assert.Equal("B4b", rates.DonorFor("C1b", deluxe: true, Sets()));
        Assert.Equal("A4b", rates.DonorFor("B4b", deluxe: true, Sets()));
    }

    [Fact]
    public void BorrowedDeluxeRates_KeepFourCards_TheGuarantee_AndTheFoils()
    {
        var index = Snapshot.Index();
        var rates = Snapshot.Rates().Assuming(["B4b"], "A4b");
        var odds = new PackOdds(index, rates);

        Assert.Equal(4, rates.LikelyCardCount("B4b"));
        Assert.Contains(rates.Variants("B4b"),
            v => v.Variant.Slots.Values.Any(s => s.TryGetValue("RR", out var p) && p >= 100));

        Assert.Equal(new[] { "A4b", "B4b" }, odds.SetsWithFoils.OrderBy(s => s).ToArray());
        Assert.Equal(new[] { "A4b:Deluxe", "B4b:Deluxe Pack Mega" }, odds.LimitedTimePacks.OrderBy(p => p));

        // A4b's 139 foils and B4b's own 166 -- 71 commons, 65 uncommons, 30 rares -- each the
        // second printing of a plain card in the same set, and none of them shared with A4b.
        //
        // B4b has more second printings than that: variants _01 to _03 at AR, SR, SSR, UR and IM.
        // Those are alternate arts, not foils, and the borrowed rates treat them so, because A4b's
        // rates name foil codes for the three diamond rungs only. Were they counted, "exclude
        // foils" would drop real cards from a target.
        var b4bFoils = index.BySet["B4b"].Where(c => odds.FoilOwnershipKeys.Contains(c.OwnershipKey)).ToArray();

        Assert.Equal(139 + 166, odds.FoilOwnershipKeys.Count);
        Assert.Equal(166, b4bFoils.Length);
        Assert.All(b4bFoils, c => Assert.Contains(c.Rarity, new[] { "C", "U", "R" }));
        Assert.DoesNotContain(index.BySet["B4b"],
            c => c.VariantIndex > 0 && c.Rarity is not ("C" or "U" or "R")
                 && odds.FoilOwnershipKeys.Contains(c.OwnershipKey));

        // And every card in the pack can be pulled from it, foil and alternate art alike: a plain
        // code yields only the plain printing on a foil rung, a foil code only the foil, and every
        // rung above them yields all its variants.
        var expected = odds.ExpectedCopies("B4b:Deluxe Pack Mega");
        Assert.Empty(index.BySet["B4b"].Where(c => expected.GetValueOrDefault(c.Key) <= 0)
                                       .Select(c => $"{c.Key} {c.Rarity}"));
    }

    [Fact]
    public void BothDeluxePacksAreRecognisedByName()
    {
        // The on-sale toggle and the guaranteed-4-diamond badge both key off this, not off A4b.
        var packs = Snapshot.Index().OpenablePackKeys.Where(k => k.StartsWith("B4b:")).ToArray();

        Assert.NotEmpty(packs);
        Assert.All(packs, k => Assert.True(GameRules.IsDeluxePack(k.Split(':')[1])));
    }

    // ---- booster art -----------------------------------------------------------------

    /// <summary>
    /// A Deluxe set that sells a pack called "Deluxe", as A4b does. Invented, and not B4b.
    ///
    /// These tests were written for B4b on the assumption that it would share A4b's pack name.
    /// It does not -- upstream calls it "Deluxe Pack Mega" -- so B4b's tile is not at risk of
    /// showing A4b's booster. The fallback they guard is still in PackArtCatalog, and a future
    /// set sharing a name is as likely as it ever was, so they now describe that set. A code
    /// upstream cannot publish keeps them from colliding with a real one, which the first
    /// version of this class did with B4b the day B4b arrived.
    /// </summary>
    private const string Shared = "Z9z:Deluxe";

    private static IEnumerable<string> PackKeysWithShared() => Snapshot.Index().AllPackKeys.Append(Shared);

    [Fact]
    public void ASharedPackName_NeverBorrowsTheOtherSetsBooster()
    {
        // "Deluxe.webp" is one file named by pack alone. Before the expansions index lists the
        // new set, its tile must show the placeholder, not A4b's booster.
        var catalog = new PackArtCatalog(Snapshot.Expansions(), PackKeysWithShared());

        Assert.Equal("", catalog.Url(Shared));
        Assert.Contains("a4b-booster", catalog.Url("A4b:Deluxe"));

        // The real B4b is not that case: its pack name is its own, so the fallback is safe.
        Assert.NotEqual("", catalog.Url("B4b:Deluxe Pack Mega"));
    }

    [Fact]
    public void ASharedPackName_GetsItsOwnArtOnceTheIndexHasIt()
    {
        var expansions = Snapshot.Expansions().Append(new ExpansionInfo
        {
            Id = "z9z",
            Packs = [new() { Id = "z9z-booster", Name = "Booster" }],
        });
        var catalog = new PackArtCatalog(expansions, PackKeysWithShared());

        Assert.Contains("z9z-booster", catalog.Url(Shared));
        Assert.Contains("a4b-booster", catalog.Url("A4b:Deluxe"));
    }

    [Fact]
    public void AUniquePackName_StillFallsBackToTheLowResolutionArt()
    {
        var catalog = new PackArtCatalog([], ["ZZ:Only Here", Shared, "A4b:Deluxe"]);
        Assert.StartsWith(ArtSource.ExchangePacks, catalog.Url("ZZ:Only Here"), StringComparison.Ordinal);
    }
}
