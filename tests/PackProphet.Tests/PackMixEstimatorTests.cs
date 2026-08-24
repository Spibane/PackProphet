namespace PackProphet.Tests;

using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Engine;

public class PackMixEstimatorTests
{
    private static CardIndex Ix => Snapshot.Index();

    private static PackMixEstimator Estimator => new(
        Ix, Snapshot.Odds(), new SetCatalog(Snapshot.PublishedSets(), Ix.BySet.Keys));

    /// <summary>Own <paramref name="copies"/> of every card whose ORIGIN set is the given one.</summary>
    private static Collection OwningFrom(string set, int copies)
    {
        var counts = new Dictionary<string, int>();
        foreach (var card in Ix.BySet[set].DistinctBy(c => c.OwnershipKey))
        {
            // Only cards this set introduced. A reprint is credited to its original set, so
            // owning one says nothing about packs of the set that reprinted it.
            var origin = Ix.ByOwnershipKey[card.OwnershipKey]
                .Select(p => p.Set).Distinct().OrderBy(s => s, StringComparer.Ordinal).First();
            if (!origin.Equals(set, StringComparison.OrdinalIgnoreCase)) continue;

            counts[card.OwnershipKey] = copies;
        }
        return new Collection(counts);
    }

    [Fact]
    public void Says_nothing_from_an_empty_collection()
    {
        // No evidence means no estimate. A split of zero across every set would be read as a
        // finding, and the caller must be able to tell the two apart.
        Assert.Empty(Estimator.Estimate(new Collection(), 1000));
    }

    [Fact]
    public void Splits_the_total_it_was_given_and_nothing_else()
    {
        var owned = new Collection(
            Ix.All.DistinctBy(c => c.OwnershipKey).ToDictionary(c => c.OwnershipKey, _ => 2));

        var mix = Estimator.Estimate(owned, 1000);

        Assert.NotEmpty(mix);
        Assert.Equal(1000, mix.Sum(s => s.Packs), 3);
        Assert.Equal(1.0, mix.Sum(s => s.Share), 6);
    }

    [Fact]
    public void Credits_a_set_only_from_cards_it_introduced()
    {
        // Owning A1 cards must not imply A4b packs, even though Deluxe packs reprint A1: the
        // copies are indistinguishable, so they belong to the set that printed them first.
        var mix = Estimator.Estimate(OwningFrom("A1", 1), 500);

        var only = Assert.Single(mix);
        Assert.Equal("A1", only.Set);
        Assert.Equal(500, only.Packs, 3);
    }

    [Fact]
    public void Estimates_deluxe_packs_from_the_cards_only_they_can_give()
    {
        // The case that would otherwise need a rule of its own. Most of a Deluxe pack is
        // reprints; what is left is the parallel foils and its own new cards, and those are
        // the only evidence pointing at Deluxe packs specifically.
        var mix = Estimator.Estimate(OwningFrom("A4b", 1), 500);

        var only = Assert.Single(mix);
        Assert.Equal("A4b", only.Set);
        Assert.Equal(500, only.Packs, 3);
    }

    [Fact]
    public void Does_not_assume_a_deluxe_pack_yields_one_of_its_own_cards()
    {
        // A Deluxe pack does not guarantee a foil - the foil-bearing slot carries most, not all,
        // of its probability there. So the divisor for A4b must be well below its pack size, and
        // dividing by one would understate Deluxe packs opened.
        var attributable = Estimator.AttributablePerPack("A4b");
        var wholePack = Snapshot.Odds().ExpectedCopies("A4b:Deluxe").Values.Sum();

        Assert.True(attributable > 0, "A4b must be estimable from its own cards.");
        Assert.True(attributable < wholePack,
            $"A4b's own cards ({attributable:0.###}) cannot exceed a whole pack ({wholePack:0.###}).");
        Assert.True(attributable < 1.5,
            $"Expected well under two attributable cards per Deluxe pack, got {attributable:0.###}.");
    }

    [Fact]
    public void An_ordinary_set_attributes_almost_its_whole_pack()
    {
        // The contrast that makes the Deluxe number believable: a normal set introduces its own
        // cards, so nearly everything in its pack counts towards it.
        var attributable = Estimator.AttributablePerPack("A1");
        var wholePack = Snapshot.Odds().ExpectedCopies("A1:Mewtwo").Values.Sum();

        Assert.True(attributable > wholePack * 0.95,
            $"A1 should attribute nearly its whole pack: {attributable:0.###} of {wholePack:0.###}.");
    }

    [Fact]
    public void Leaves_promos_out_of_the_split()
    {
        var promoCards = Ix.All.Where(c => CardIndex.IsPromoSet(c.Set)).DistinctBy(c => c.OwnershipKey);
        var owned = new Collection(promoCards.ToDictionary(c => c.OwnershipKey, _ => 3));

        // Promos come from events, never packs, so a shelf full of them implies no packs at all.
        Assert.Empty(Estimator.Estimate(owned, 400));
    }
}
