using PackProphet.Data;
using PackProphet.Deck;

namespace PackProphet.Tests;

public class DeckLinterTests
{
    private static CardIndex Ix => Snapshot.Index();
    private static DeckLinter Linter => new(Ix, Snapshot.Facts());

    /// <summary>A legal 20-card deck built from A1 basics, which do have facts available.</summary>
    private static (List<int> Cards, List<EnergyType> Energy) LegalDeck()
    {
        var basics = Ix.BySet["A1"]
            .Where(c => c.Set == "A1")
            .Select(c => Ix.DeckNrOf(c))
            .Where(nr => nr is not null).Select(nr => nr!.Value)
            .Distinct()
            .Where(nr => Snapshot.Facts().For(nr) is { IsBasic: true, Subtype: "Grass" })
            .Take(10)
            .ToList();

        var cards = new List<int>();
        foreach (var nr in basics) { cards.Add(nr); cards.Add(nr); }
        return (cards, [EnergyType.Grass]);
    }

    [Fact]
    public void ALegalDeckPassesCleanly()
    {
        var (cards, energy) = LegalDeck();
        Assert.Equal(20, cards.Count);

        var report = Linter.Lint(cards, energy);

        Assert.False(report.HasErrors);
        Assert.Empty(report.Of(LintSeverity.Error));
    }

    [Fact]
    public void WrongCardCount_IsAnError()
    {
        var (cards, energy) = LegalDeck();

        var tooFew = Linter.Lint(cards.Take(19).ToList(), energy);
        Assert.Contains(tooFew.Of(LintSeverity.Error), f => f.Rule == "deck-size");
        Assert.Contains("19", tooFew.Of(LintSeverity.Error).First(f => f.Rule == "deck-size").Message);

        var tooMany = Linter.Lint([.. cards, cards[0]], energy);
        Assert.Contains(tooMany.Of(LintSeverity.Error), f => f.Rule == "deck-size");
    }

    [Fact]
    public void ThreeCopiesOfAName_IsAnError()
    {
        var (cards, energy) = LegalDeck();
        var third = new List<int>(cards) { cards[0] };
        third.RemoveAt(third.Count - 2);   // keep the count at 20

        var report = Linter.Lint(third, energy);
        Assert.Contains(report.Of(LintSeverity.Error), f => f.Rule == "copy-limit");
    }

    [Fact]
    public void TheCopyLimitIsPerName_NotPerPrinting()
    {
        // Two different printings of the same card still count together toward the limit.
        var shared = Ix.ByDeckBuilderNr.First(kv =>
            kv.Value.Select(p => p.Name).Distinct().Count() == 1 && kv.Value.Count > 1);

        var report = Linter.Lint([shared.Key, shared.Key, shared.Key], []);
        Assert.Contains(report.Of(LintSeverity.Error), f => f.Rule == "copy-limit");
    }

    [Fact]
    public void MoreThanThreeEnergyTypes_IsAnError()
    {
        var (cards, _) = LegalDeck();
        var report = Linter.Lint(cards,
            [EnergyType.Fire, EnergyType.Water, EnergyType.Grass, EnergyType.Metal]);

        Assert.Contains(report.Of(LintSeverity.Error), f => f.Rule == "energy-types");
    }

    [Fact]
    public void NoEnergySelected_IsAWarning()
    {
        var (cards, _) = LegalDeck();
        Assert.Contains(Linter.Lint(cards, []).Of(LintSeverity.Warning),
            f => f.Rule == "energy-types");
    }

    [Fact]
    public void ADeckOfOnlyEvolutions_HasNoBasicAndIsUnplayable()
    {
        // The rule that matters most: a deck with no Basic cannot even start.
        var stage2 = Ix.ByDeckBuilderNr.Keys
            .Where(nr => Snapshot.Facts().For(nr) is { Stage: "Stage 2" })
            .Take(10).ToList();
        Assert.NotEmpty(stage2);

        var cards = new List<int>();
        foreach (var nr in stage2) { cards.Add(nr); cards.Add(nr); }

        var report = Linter.Lint(cards, [EnergyType.Grass]);
        Assert.Contains(report.Of(LintSeverity.Error), f => f.Rule == "basic-pokemon");
    }

    [Fact]
    public void EveryCardNowHasStageData_SoTheBasicRuleIsCheckableEverywhere()
    {
        // Previously eight sets from B2 onward had no stage data at all, leaving this rule
        // unverifiable for them. Adopting a fully-covering source fixed that, so assert it —
        // if coverage ever regresses, the Basic rule silently stops working.
        //
        // Every card upstream has DESCRIBED, that is. A card nobody has published detail for yet
        // has no stage to check anywhere, and 2.11.0 brought one: Mega Garchomp ex, PROMO-B-99,
        // new in the promo run that arrived undescribed. Every other card in that run, and all
        // 429 of B4b, is a new printing of a card the table already knows, so the rule reaches
        // them regardless -- and they are still checked here, since the identity is what is
        // looked up. Only a card with no known identity may sit in Snapshot.AwaitingDetail.
        var missing = Ix.All
            .Where(c => Ix.DeckNrOf(c) is int nr && !Snapshot.Facts().Knows(nr))
            .Where(c => !Snapshot.AwaitingDetail().Contains(c.Key))
            .Select(c => $"{c.Key} {c.Name}")
            .ToList();

        Assert.Empty(missing);

        foreach (var set in new[] { "B2", "B3", "B4", "PROMO-B" })
        {
            var withStage = Ix.BySet[set]
                .Select(Ix.DeckNrOf).Where(nr => nr is not null)
                .Count(nr => Snapshot.Facts().For(nr!.Value)?.Stage is not null);
            Assert.True(withStage > 0, $"{set} still has no stage data");
        }
    }

    [Fact]
    public void WhenDetailIsMissing_TheBasicRuleReportsUnverifiedRatherThanLegal()
    {
        // The mechanism must survive full coverage: a future set can always land in the card
        // list before its detail does, and calling an unverifiable deck legal would hand
        // someone a deck that cannot start.
        var blind = new DeckLinter(Ix, CardFacts.Empty);
        var report = blind.Lint([1, 1], [EnergyType.Grass]);

        Assert.Contains(report.Of(LintSeverity.Unverified), f => f.Rule == "basic-pokemon");
        Assert.DoesNotContain(report.Of(LintSeverity.Error), f => f.Rule == "basic-pokemon");
        Assert.False(report.FullyVerified);
    }

    [Fact]
    public void AnEvolutionWithoutItsPreEvolution_IsAWarning()
    {
        var evolution = Ix.ByDeckBuilderNr.Keys
            .First(nr => Snapshot.Facts().For(nr)?.EvolvesFrom is { Length: > 0 });

        var report = Linter.Lint([evolution], []);
        Assert.Contains(report.Of(LintSeverity.Warning), f => f.Rule == "evolution-chain");
    }

    [Fact]
    public void AnEvolutionWithItsPreEvolution_IsFine()
    {
        var evolution = Ix.ByDeckBuilderNr.Keys.First(nr =>
            Snapshot.Facts().For(nr)?.EvolvesFrom is { Length: > 0 } from &&
            Ix.All.Any(c => c.Name.Equals(from, StringComparison.OrdinalIgnoreCase)));

        var from = Snapshot.Facts().For(evolution)!.EvolvesFrom!;
        var preEvo = Ix.All.First(c => c.Name.Equals(from, StringComparison.OrdinalIgnoreCase));
        var preNr = Ix.DeckNrOf(preEvo)!.Value;

        var report = Linter.Lint([evolution, preNr], []);
        Assert.DoesNotContain(report.Of(LintSeverity.Warning), f => f.Rule == "evolution-chain");
    }

    [Fact]
    public void APokemonWhoseEnergyIsNotRun_IsAWarning()
    {
        var fire = Ix.ByDeckBuilderNr.Keys
            .First(nr => Snapshot.Facts().For(nr) is { IsPokemon: true, Subtype: "Fire" });

        Assert.Contains(Linter.Lint([fire], [EnergyType.Water]).Of(LintSeverity.Warning),
            f => f.Rule == "energy-match");
        Assert.DoesNotContain(Linter.Lint([fire], [EnergyType.Fire]).Of(LintSeverity.Warning),
            f => f.Rule == "energy-match");
    }

    [Fact]
    public void CardsYouDoNotOwn_AreReportedAsInfo_NotAsIllegal()
    {
        var (cards, energy) = LegalDeck();
        var report = Linter.Lint(cards, energy, new Collection());

        var info = report.Of(LintSeverity.Info).ToList();
        Assert.Contains(info, f => f.Rule == "not-owned");
        Assert.False(report.HasErrors);   // borrowing a decklist is not an error
    }

    [Fact]
    public void OwningEverything_ClearsTheNotOwnedNotice()
    {
        var (cards, energy) = LegalDeck();
        var owned = cards.Distinct().Aggregate(new Collection(), (c, nr) =>
            c.With(Ix.ByDeckBuilderNr[nr][0].OwnershipKey, 2));

        Assert.DoesNotContain(Linter.Lint(cards, energy, owned).Of(LintSeverity.Info),
            f => f.Rule == "not-owned");
    }

    [Fact]
    public void WithNoFactsAtAll_HardRulesStillWork()
    {
        // Card count, copy limit and energy count need no external data, so they must keep
        // working even with the facts table entirely absent.
        var bare = new DeckLinter(Ix, CardFacts.Empty);
        var report = bare.Lint([1, 1, 1], [EnergyType.Fire, EnergyType.Water, EnergyType.Grass, EnergyType.Metal]);

        Assert.Contains(report.Of(LintSeverity.Error), f => f.Rule == "deck-size");
        Assert.Contains(report.Of(LintSeverity.Error), f => f.Rule == "copy-limit");
        Assert.Contains(report.Of(LintSeverity.Error), f => f.Rule == "energy-types");
        Assert.Contains(report.Of(LintSeverity.Unverified), f => f.Rule == "basic-pokemon");
    }
}
