namespace PackProphet.Tests;

using PackProphet.Deck;
using PackProphet.Domain;
using PackProphet.Engine;

public class EvolutionGapsTests
{
    private static EvolutionGaps Gaps =>
        new(Snapshot.Index(), Snapshot.Facts(), Snapshot.Odds());

    private static Collection Owning(params string[] names)
    {
        var ix = Snapshot.Index();
        var counts = new Dictionary<string, int>();

        foreach (var name in names)
        {
            var card = ix.All.First(c => c.Name == name);
            counts[card.OwnershipKey] = 1;
        }

        return new Collection(counts);
    }

    [Fact]
    public void Reports_every_stage_of_a_chain_you_are_missing()
    {
        // A Stage 2 with neither lower stage is TWO cards from playable, and the deeper gap is
        // invisible from the owned cards alone - you do not own the middle card, so nothing in the
        // collection can point at what it needs.
        var report = Gaps.Find(Owning("Charizard"));

        Assert.Equal(2, report.Gaps.Count);

        var names = report.Gaps.Select(g => g.MissingName).ToArray();
        Assert.Contains("Charmeleon", names);
        Assert.Contains("Charmander", names);

        var mid = report.Gaps.First(g => g.MissingName == "Charmeleon");
        Assert.Equal("Charmander", mid.Requires);

        // The basic needs nothing further, and saying so is what makes the chain readable.
        Assert.Null(report.Gaps.First(g => g.MissingName == "Charmander").Requires);
    }

    [Fact]
    public void Owning_the_lower_stage_closes_the_deeper_gap()
    {
        var report = Gaps.Find(Owning("Charizard", "Charmander"));

        var gap = Assert.Single(report.Gaps);
        Assert.Equal("Charmeleon", gap.MissingName);
        Assert.Null(gap.Requires);
    }

    [Fact]
    public void A_complete_collection_has_no_gaps_at_all()
    {
        // The strongest invariant here, and it doubles as a data check: it can only hold if every
        // evolves-from name in the facts table resolves to a real card name in the index.
        var ix = Snapshot.Index();
        var everything = new Collection(
            ix.All.DistinctBy(c => c.OwnershipKey).ToDictionary(c => c.OwnershipKey, _ => 1));

        var report = Gaps.Find(everything);

        Assert.True(report.Complete);

        // Unverified counts the Pokémon nobody has published a stage for, which is a property of
        // upstream rather than of this code: zero for as long as the detail table covered every
        // card, and one in 2.11.0, where Mega Garchomp ex arrived in PROMO-B's undescribed run.
        // So the count is held to exactly those cards, the ones Snapshot.AwaitingDetail excuses
        // and whose identity the table does not know under any other printing. One more is a
        // described card the lookup lost; one fewer is a card with no stage passed as checked.
        var undescribed = ix.All
            .DistinctBy(c => c.OwnershipKey)
            .Where(c => Snapshot.AwaitingDetail().Contains(c.Key))
            .Count(c => ix.DeckNrOf(c) is int nr
                        && nr < DeckBuilderNr.TrainerOffset
                        && !Snapshot.Facts().Knows(nr));

        Assert.Equal(undescribed, report.Unverified);
    }

    [Fact]
    public void A_fossil_is_a_trainer_and_still_closes_a_gap()
    {
        // The bug this test exists for: excluding trainers from the name tables reported eleven
        // missing fossils to a player who owned every card in the game. Omanyte evolves from Helix
        // Fossil, which is an Item card.
        var gap = Assert.Single(Gaps.Find(Owning("Omanyte")).Gaps);

        Assert.Equal("Helix Fossil", gap.MissingName);
        Assert.NotEmpty(gap.Candidates);

        Assert.True(Gaps.Find(Owning("Omanyte", "Helix Fossil")).Complete);
    }

    [Fact]
    public void Recommends_a_printing_that_can_actually_be_opened()
    {
        // Obtainability comes before rarity, which is not the obvious order. The promo Charmeleon
        // is a 1-diamond while every openable one is a 2-diamond, so ranking by rarity first
        // recommended the single card you cannot get.
        var gap = Gaps.Find(Owning("Charizard")).Gaps.First(g => g.MissingName == "Charmeleon");

        Assert.NotNull(gap.Easiest);
        Assert.True(gap.Easiest!.Openable);
        Assert.False(gap.Easiest.IsPromo);
        Assert.False(gap.NoPackRoute);
    }

    [Fact]
    public void One_gap_per_missing_card_however_many_arts_are_blocked()
    {
        // Holding three arts of one Charizard is still one card that cannot evolve, and a list that
        // said "Charmeleon" three times would read as three separate problems.
        var ix = Snapshot.Index();
        var arts = ix.All.Where(c => c.Name == "Charizard").DistinctBy(c => c.OwnershipKey)
                     .Take(3).ToArray();

        Assert.True(arts.Length > 1, "needs more than one Charizard printing to be meaningful");

        var owned = new Collection(arts.ToDictionary(c => c.OwnershipKey, _ => 1));
        var report = Gaps.Find(owned);

        var gap = report.Gaps.First(g => g.MissingName == "Charmeleon");
        Assert.Equal("Charizard", Assert.Single(gap.Blocks).Name);
        Assert.Equal(1, report.BlockedCards);
    }

    [Fact]
    public void A_basic_on_its_own_is_never_a_gap()
    {
        Assert.True(Gaps.Find(Owning("Charmander")).Complete);
    }

    [Fact]
    public void Trainers_are_not_counted_as_unverifiable()
    {
        // A trainer has no pre-evolution, so it can never open a gap. Counting them as "stage
        // unknown" would put a caveat on hundreds of cards it could not apply to.
        var ix = Snapshot.Index();
        var trainers = ix.All
            .Where(c => ix.DeckNrOf(c) is int nr && nr >= PackProphet.Deck.DeckBuilderNr.TrainerOffset)
            .DistinctBy(c => c.OwnershipKey)
            .Take(20)
            .ToArray();

        var report = Gaps.Find(new Collection(trainers.ToDictionary(c => c.OwnershipKey, _ => 1)));

        Assert.Equal(0, report.Unverified);
        Assert.True(report.Complete);
    }
}
