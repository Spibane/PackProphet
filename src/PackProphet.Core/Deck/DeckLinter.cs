namespace PackProphet.Deck;

using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Text;

public enum LintSeverity
{
    /// <summary>The deck is illegal and the game will refuse it.</summary>
    Error,
    /// <summary>Legal but probably not what you meant.</summary>
    Warning,
    /// <summary>Informational, e.g. cards you do not own yet.</summary>
    Info,
    /// <summary>
    /// A rule that could not be checked because the data is missing. Distinct from a pass:
    /// reporting an unverifiable deck as legal hands someone an unplayable deck.
    /// </summary>
    Unverified
}

public sealed record LintFinding(LintSeverity Severity, string Rule, string Message);

public sealed record LintReport(IReadOnlyList<LintFinding> Findings)
{
    public bool HasErrors => Findings.Any(f => f.Severity == LintSeverity.Error);
    public bool FullyVerified => Findings.All(f => f.Severity != LintSeverity.Unverified);

    public IEnumerable<LintFinding> Of(LintSeverity severity) =>
        Findings.Where(f => f.Severity == severity);
}

/// <summary>
/// Checks a deck against the game's rules, and against common mistakes that are legal but
/// self-defeating.
/// </summary>
public sealed class DeckLinter
{
    private readonly CardIndex _index;
    private readonly CardFacts _facts;

    public DeckLinter(CardIndex index, CardFacts? facts = null)
    {
        _index = index;
        _facts = facts ?? CardFacts.Empty;
    }

    public LintReport Lint(
        IReadOnlyList<int> deckBuilderNrs,
        IReadOnlyList<EnergyType> energies,
        Collection? owned = null)
    {
        var findings = new List<LintFinding>();

        CheckSize(deckBuilderNrs, findings);
        CheckCopiesPerName(deckBuilderNrs, findings);
        CheckEnergyTypes(energies, findings);
        CheckBasics(deckBuilderNrs, findings);
        CheckEvolutionChains(deckBuilderNrs, findings);
        CheckEnergyMatch(deckBuilderNrs, energies, findings);
        CheckOwnership(deckBuilderNrs, owned, findings);

        return new LintReport(findings);
    }

    private void CheckSize(IReadOnlyList<int> nrs, List<LintFinding> findings)
    {
        if (nrs.Count == GameRules.DeckSize) return;

        var direction = nrs.Count < GameRules.DeckSize ? "short of" : "over";
        findings.Add(new(LintSeverity.Error, "deck-size",
            $"A deck must hold exactly {GameRules.DeckSize} cards; this has {nrs.Count} " +
            $"({Math.Abs(nrs.Count - GameRules.DeckSize)} {direction} the limit)."));
    }

    private void CheckCopiesPerName(IReadOnlyList<int> nrs, List<LintFinding> findings)
    {
        // The limit is per card NAME, not per identity: two different printings of the same
        // name still count together.
        var byName = new Dictionary<string, int>();
        foreach (var nr in nrs)
        {
            var name = NameOf(nr);
            if (name is null) continue;
            byName[name] = byName.TryGetValue(name, out var c) ? c + 1 : 1;
        }

        foreach (var (name, count) in byName.Where(kv => kv.Value > GameRules.MaxCopiesPerName))
            findings.Add(new(LintSeverity.Error, "copy-limit",
                $"{count} copies of \"{name}\" — the limit is {GameRules.MaxCopiesPerName} per name."));
    }

    private void CheckEnergyTypes(IReadOnlyList<EnergyType> energies, List<LintFinding> findings)
    {
        if (energies.Count > GameRules.MaxEnergyTypes)
            findings.Add(new(LintSeverity.Error, "energy-types",
                $"{energies.Count} energy types selected; the limit is {GameRules.MaxEnergyTypes}."));

        if (energies.Count == 0)
            findings.Add(new(LintSeverity.Warning, "energy-types",
                "No energy type selected, so the Energy Zone will never produce anything usable."));
    }

    private void CheckBasics(IReadOnlyList<int> nrs, List<LintFinding> findings)
    {
        var basics = nrs.Distinct().Count(nr => _facts.For(nr)?.IsBasic == true);
        if (basics >= GameRules.MinBasicPokemon) return;

        // Without stage data the rule cannot be checked, and it is the difference between a deck
        // that plays and one that cannot start.
        var unknown = _facts.Unknown(nrs.Where(nr => _index.ByDeckBuilderNr.ContainsKey(nr)));
        if (unknown.Count > 0)
        {
            findings.Add(new(LintSeverity.Unverified, "basic-pokemon",
                $"Cannot confirm a Basic Pokémon: {unknown.Count} of these cards have no stage " +
                "data in the community database yet. A deck with no Basic cannot be played."));
            return;
        }

        findings.Add(new(LintSeverity.Error, "basic-pokemon",
            "No Basic Pokémon. The deck cannot be played at all — there is nothing to start with."));
    }

    private void CheckEvolutionChains(IReadOnlyList<int> nrs, List<LintFinding> findings)
    {
        // Both sides of this comparison are the same printed name spelled by two datasets --
        // the deck's cards come from the index and EvolvesFrom comes from the facts. See
        // PackProphet.Text.CardName.
        var names = nrs.Select(NameOf).Where(n => n is not null).ToHashSet(CardName.Comparer!);

        foreach (var nr in nrs.Distinct())
        {
            var fact = _facts.For(nr);
            if (fact?.EvolvesFrom is not { Length: > 0 } from) continue;
            if (names.Contains(from)) continue;

            findings.Add(new(LintSeverity.Warning, "evolution-chain",
                $"{fact.Name} evolves from {from}, which is not in the deck — so it can never " +
                "be played."));
        }
    }

    private void CheckEnergyMatch(
        IReadOnlyList<int> nrs, IReadOnlyList<EnergyType> energies, List<LintFinding> findings)
    {
        if (energies.Count == 0) return;

        var running = energies.Select(e => e.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var nr in nrs.Distinct())
        {
            var fact = _facts.For(nr);
            if (fact is null || !fact.IsPokemon) continue;

            // Colorless attacks on anything, so it is never a mismatch.
            var elements = fact.Subtypes
                .Where(e => !e.Equals("colorless", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (elements.Count == 0) continue;

            // A dual-typed Pokémon needs only ONE of its energies to be in the deck: it can be
            // powered either way. Warning on the one that is absent would tell someone running a
            // perfectly good Grass deck that their Grass/Water attacker is unsupported.
            if (elements.Any(running.Contains)) continue;

            var named = string.Join(" or ", elements);
            findings.Add(new(LintSeverity.Warning, "energy-match",
                $"{fact.Name} is {named}, but the deck does not run {named} energy."));
        }
    }

    private void CheckOwnership(IReadOnlyList<int> nrs, Collection? owned, List<LintFinding> findings)
    {
        if (owned is null) return;

        var missing = new List<string>();
        foreach (var group in nrs.GroupBy(nr => nr))
        {
            if (!_index.ByDeckBuilderNr.TryGetValue(group.Key, out var printings)) continue;

            var have = owned.OfIdentity(printings.DistinctBy(p => p.OwnershipKey));
            var need = group.Count() - have;
            if (need > 0)
                missing.Add($"{need}x {printings[0].Name}");
        }

        if (missing.Count > 0)
            findings.Add(new(LintSeverity.Info, "not-owned",
                $"You are missing {missing.Count} {Text.Fmt.S(missing.Count, "card")}: {string.Join(", ", missing)}."));
    }

    private string? NameOf(int nr) =>
        _index.ByDeckBuilderNr.TryGetValue(nr, out var printings) ? printings[0].Name : null;
}
