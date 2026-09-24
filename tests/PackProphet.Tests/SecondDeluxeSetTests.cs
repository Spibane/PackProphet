using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Engine;

namespace PackProphet.Tests;

/// <summary>
/// A second Deluxe set, B4b "Deluxe Pack: Mega", due 2026-09-29 with no data published yet.
///
/// Stood in for by a copy of A4b under a new set code and new artwork ids: the same four-card
/// pack, the same foil pairs, none of its cards owned already. What these pin down is that
/// nothing about being a Deluxe set was tied to the code "A4b".
/// </summary>
public class SecondDeluxeSetTests
{
    private static readonly List<PocketCard> B4b = Snapshot.Index().BySet["A4b"]
        .Select(c => new PocketCard
        {
            Set = "B4b",
            Number = c.Number,
            Rarity = c.Rarity,
            Name = c.Name,
            // cPK_10_000010_01_... -> cPK_10_900010_01_...: a new card, same variant index.
            Image = NewArtwork(c.Image),
            Packs = ["Deluxe"],
        })
        .ToList();

    private static string NewArtwork(string image)
    {
        var parts = image.Split('_');
        parts[2] = "9" + parts[2][1..];
        return string.Join('_', parts);
    }

    private static CardIndex Index() => new(Snapshot.Cards().Concat(B4b), Snapshot.Rarities());

    private static SetCatalog Sets()
    {
        var published = Snapshot.PublishedSets();
        published["B"].Add(new SetInfo
        {
            Code = "B4b",
            ReleaseDate = "2026-09-29",
            Count = B4b.Count,
            Name = new() { ["en"] = "Deluxe Pack: Mega" },
            Packs = ["Deluxe"],
        });
        return new SetCatalog(published, Index().BySet.Keys);
    }

    // ---- borrowing rates -------------------------------------------------------------

    [Fact]
    public void ADeluxeSetBorrowsFromADeluxeSet()
    {
        // B3b is the newest measured set, and its five-card pack would cost B4b its guarantee,
        // its shape and its foils.
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
        var withoutDeluxe = new PullRates(Snapshot.Rates().ModelledSets
            .Where(s => s != "A4b")
            .ToDictionary(s => s, s => Snapshot.Rates().Published(s).ToDictionary()));

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
        var index = Index();
        var rates = Snapshot.Rates().Assuming(["B4b"], "A4b");
        var odds = new PackOdds(index, rates);

        Assert.Equal(4, rates.LikelyCardCount("B4b"));
        Assert.Contains(rates.Variants("B4b"),
            v => v.Variant.Slots.Values.Any(s => s.TryGetValue("RR", out var p) && p >= 100));

        Assert.Equal(new[] { "A4b", "B4b" }, odds.SetsWithFoils.OrderBy(s => s).ToArray());
        Assert.Equal(139 * 2, odds.FoilOwnershipKeys.Count);
        Assert.Equal(new[] { "A4b:Deluxe", "B4b:Deluxe" }, odds.LimitedTimePacks.OrderBy(p => p));
    }

    [Fact]
    public void BothDeluxePacksAreRecognisedByName()
    {
        // The on-sale toggle and the guaranteed-4-diamond badge both key off this, not off A4b.
        Assert.All(Index().OpenablePackKeys.Where(k => k.StartsWith("B4b:")),
                   k => Assert.True(GameRules.IsDeluxePack(k.Split(':')[1])));
    }

    // ---- booster art -----------------------------------------------------------------

    [Fact]
    public void ASharedPackName_NeverBorrowsTheOtherSetsBooster()
    {
        // "Deluxe.webp" is one file named by pack alone. Before the expansions index lists b4b,
        // B4b's tile must show the placeholder, not A4b's booster.
        var catalog = new PackArtCatalog(Snapshot.Expansions(), Index().AllPackKeys);

        Assert.Equal("", catalog.Url("B4b:Deluxe"));
        Assert.Contains("a4b-booster", catalog.Url("A4b:Deluxe"));
    }

    [Fact]
    public void ASharedPackName_GetsItsOwnArtOnceTheIndexHasIt()
    {
        var expansions = Snapshot.Expansions().Append(new ExpansionInfo
        {
            Id = "b4b",
            Packs = [new() { Id = "b4b-booster", Name = "Booster" }],
        });
        var catalog = new PackArtCatalog(expansions, Index().AllPackKeys);

        Assert.Contains("b4b-booster", catalog.Url("B4b:Deluxe"));
        Assert.Contains("a4b-booster", catalog.Url("A4b:Deluxe"));
    }

    [Fact]
    public void AUniquePackName_StillFallsBackToTheLowResolutionArt()
    {
        var catalog = new PackArtCatalog([], ["ZZ:Only Here", "B4b:Deluxe", "A4b:Deluxe"]);
        Assert.StartsWith(ArtSource.ExchangePacks, catalog.Url("ZZ:Only Here"), StringComparison.Ordinal);
    }
}
