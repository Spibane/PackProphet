using PackProphet.Data;
using PackProphet.Deck;
using PackProphet.Text;

namespace PackProphet.Tests;

/// <summary>
/// Cross-checks two independently compiled datasets against each other. Agreement is evidence both
/// are right; disagreement is a bug in one of them.
/// </summary>
public class CrossValidationTests
{
    private static CardIndex Ix => Snapshot.Index();
    private static CardFacts Facts => Snapshot.Facts();

    [Fact]
    public void TheirCardIdentity_AgreesWithOursDerivedFromArtworkFilenames()
    {
        // This is the strongest validation available for the reverse-engineered deck format:
        // every deck feature depends on this derivation, and here an unrelated project
        // publishes the same numbers. Zero mismatches is the assertion, not "mostly agrees".
        var mismatches = new List<string>();
        var compared = 0;

        foreach (var card in Ix.All)
        {
            var ours = Ix.DeckNrOf(card);
            if (ours is null) continue;

            var theirs = Facts.For(ours.Value)?.DeckBuilderNr;
            if (theirs is null) continue;   // joined BY identity, so this only skips gaps

            compared++;
            if (theirs != ours) mismatches.Add($"{card.Key} {card.Name}: ours={ours} theirs={theirs}");
        }

        Assert.True(compared > 3000, $"expected a broad comparison, only made {compared}");
        Assert.Empty(mismatches);
    }

    [Fact]
    public void PackPointCosts_AgreeWithTheRarityTableWeUseForPricing()
    {
        // The points advisor is built on rarities.json. If the two sources disagree, the
        // cheapest-route figures are wrong for that rarity.
        var disagreements = new List<string>();

        foreach (var card in Ix.All)
        {
            // BY PRINTING, not by identity: an art rare and its base card share attacks but
            // not price, so an identity lookup would compare the wrong card's points.
            var theirs = Facts.ForPrinting(card)?.PackPoints;
            if (theirs is null or 0) continue;
            if (!Snapshot.Rarities().TryGetValue(card.Rarity, out var rarity)) continue;
            if (rarity.Points <= 0) continue;

            if (rarity.Points != theirs)
                disagreements.Add($"{card.Key} {card.Rarity}: table={rarity.Points} theirs={theirs}");
        }

        // Report a sample rather than thousands of lines if this ever breaks.
        Assert.Empty(disagreements.Take(20));
    }

    [Fact]
    public void PerPrintingLookupDistinguishesAnArtRareFromItsBaseCard()
    {
        // The distinction the previous test flushed out. A1-1 is the common Bulbasaur; A1-227
        // is its art rare. Same card identity, same attacks, very different price.
        var common = Facts.ForPrinting("A1-1");
        var artRare = Facts.ForPrinting("A1-227");

        Assert.NotNull(common);
        Assert.NotNull(artRare);
        Assert.Equal(common.Name, artRare.Name);
        Assert.Equal(35, common.PackPoints);
        Assert.Equal(400, artRare.PackPoints);

        // ...and identity-level facts are genuinely identical across the two arts.
        Assert.Equal(common.Stage, artRare.Stage);
        Assert.Equal(common.Health, artRare.Health);
    }

    [Fact]
    public void EveryPrintingJoins_IncludingPromosWhoseSetCodesDiffer()
    {
        // Their promo sets are "pa"/"pb"; ours are "PROMO-A"/"PROMO-B". A silent mismatch
        // there would drop 203 cards from every detail lookup.
        //
        // Less the printings upstream has not described yet, which have nothing to join TO: the
        // newest set while it has no detail at all, and a promo set's newest run. That allowance
        // is shaped so a broken join cannot hide in it -- see Snapshot.AwaitingDetail.
        var unjoined = Ix.All.Where(c => Facts.ForPrinting(c) is null)
                             .Where(c => !Snapshot.AwaitingDetail().Contains(c.Key))
                             .ToList();

        Assert.Empty(unjoined.Take(20).Select(c => $"{c.Key} {c.Name}"));
        Assert.NotNull(Facts.ForPrinting("PROMO-A-1"));
        Assert.NotNull(Facts.ForPrinting("PROMO-B-1"));
    }

    [Fact]
    public void TheStatsFlibustierGetsWrong_AreRightInTheSourceWeNowUse()
    {
        // The specific bug that drove the switch: cards.extra.json reported Venusaur ex as
        // 50 HP and retreat 1. The real card is 190 and 3.
        var venusaur = Ix.ByKey["A1-4"];
        var fact = Facts.For(Ix.DeckNrOf(venusaur)!.Value);

        Assert.NotNull(fact);
        Assert.Equal("Venusaur ex", fact.Name);
        Assert.Equal(190, fact.Health);
        Assert.Equal(3, fact.Retreat);
        Assert.Equal("Stage 2", fact.Stage);
        Assert.Equal("Grass", fact.Subtype);
    }

    [Fact]
    public void EveryPokemonHasAStageAndEveryTrainerHasNone()
    {
        var wrong = new List<string>();

        foreach (var nr in Ix.ByDeckBuilderNr.Keys)
        {
            var fact = Facts.For(nr);
            if (fact is null) continue;

            if (fact.IsPokemon && fact.Stage is null) wrong.Add($"{fact.Id} {fact.Name}: Pokémon with no stage");

            // A Trainer may carry "Basic" and nothing else, because a handful of them are played
            // AS Basic Pokémon: the Fossils -- Helix, Dome, Old Amber, Skull, Armor -- are Item
            // cards that go to the bench and take damage. Upstream started stating that in 2.10.0
            // and it is right to, so the rule is now "a Trainer is never a Stage 1 or a Stage 2"
            // rather than "a Trainer has no stage".
            //
            // Not an allowlist of names, which would need editing every time a Fossil is
            // reprinted -- fourteen printings already. The shape of the claim is what matters: a
            // Trainer that evolves from something is nonsense whatever it is called.
            if (!fact.IsPokemon && fact.Stage is { } stage && stage != "Basic")
                wrong.Add($"{fact.Id} {fact.Name}: Trainer with stage {stage}");
        }

        Assert.Empty(wrong.Take(20));
    }

    [Fact]
    public void TrainerIdentities_AreTheOnesWeClassifyAsTrainers()
    {
        // Our Trainer test is a numeric offset recovered from a filename; theirs is a typed
        // field. They must agree, or trainer cards land in the wrong deck-code segment.
        var disagreements = new List<string>();

        foreach (var nr in Ix.ByDeckBuilderNr.Keys)
        {
            var fact = Facts.For(nr);
            if (fact is null) continue;

            var oursSaysTrainer = DeckBuilderNr.IsTrainer(nr);
            if (oursSaysTrainer == fact.IsPokemon)
                disagreements.Add($"{fact.Id} {fact.Name}: offset says trainer={oursSaysTrainer}, type={fact.Type}");
        }

        Assert.Empty(disagreements.Take(20));
    }

    [Fact]
    public void AttackAndAbilityTextIsBroadlyPopulated()
    {
        // The reason for adopting this source. Not every card attacks (Trainers do not), so
        // this asserts breadth rather than totality.
        var pokemon = Ix.ByDeckBuilderNr.Keys
            .Select(Facts.For).Where(f => f is { IsPokemon: true }).ToList();

        Assert.True(pokemon.Count > 1500, $"only {pokemon.Count} Pokémon identities");
        Assert.True(pokemon.Count(f => f!.AttackList.Count > 0) > pokemon.Count * 0.9);
        Assert.Contains(pokemon, f => f!.HasAbility);

        // Costs must expand to real energy names, not raw letters.
        var withCost = pokemon.SelectMany(f => f!.AttackList)
            .First(a => !string.IsNullOrEmpty(a.Cost) && a.Cost != "0");
        Assert.All(withCost.CostSymbols, sym => Assert.True(sym.Length > 1, $"unexpanded cost: {sym}"));
    }

    [Fact]
    public void EvolutionChainsResolveToRealCardNames()
    {
        // The deck linter warns when an evolution's pre-evolution is absent, which is only
        // meaningful if evolves-from actually names a card we know.
        //
        // Through CardName, because that is what the app compares with -- the two datasets write
        // an apostrophe differently and an ordinal comparison here would be testing something the
        // app does not do. See PackProphet.Text.CardName.
        var names = Ix.All.Select(c => c.Name).ToHashSet(CardName.Comparer);

        var dangling = Ix.ByDeckBuilderNr.Keys
            .Select(Facts.For)
            .Where(f => f?.EvolvesFrom is { Length: > 0 })
            .Where(f => !names.Contains(f!.EvolvesFrom!))
            .Select(f => $"{f!.Id} {f.Name} evolves from unknown '{f.EvolvesFrom}'")
            .ToList();

        Assert.Empty(dangling.Take(20));
    }
}
