namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Deck;
using PackProphet.Domain;
using PackProphet.Text;

/// <param name="Blocks">
/// Cards you already own that cannot be played without it. Deduplicated by identity, because
/// holding three arts of one Charizard is still one card that cannot evolve.
/// </param>
/// <param name="Candidates">
/// Printings that would fill the gap, easiest first. ANY of them does - evolution matches on the
/// printed name, so the cheapest common is as good as the art rare.
/// </param>
/// <param name="Requires">
/// What the missing card itself needs and you also lack, or null. A Stage 2 with neither of its
/// lower stages is two purchases from playable.
/// </param>
public sealed record EvolutionGap(
    string MissingName,
    IReadOnlyList<PocketCard> Blocks,
    IReadOnlyList<PocketCard> Candidates,
    string? Requires = null)
{
    /// <summary>The printing to go after: lowest rarity that a pack can actually yield.</summary>
    public PocketCard? Easiest => Candidates.FirstOrDefault();

    /// <summary>
    /// No printing of it comes from a pack, so no amount of opening fills this gap. Rare but real -
    /// a chain whose middle stage exists only as a promo.
    /// </summary>
    public bool NoPackRoute => Candidates.All(c => !c.Openable);
}

/// <param name="Unverified">
/// Owned Pokémon with no stage data published yet. A clean result over an incomplete database is
/// not the same as having no gaps, and the newest sets are where the data is missing.
/// </param>
public sealed record EvolutionReport(
    IReadOnlyList<EvolutionGap> Gaps,
    int Checked,
    int Unverified)
{
    public bool Complete => Gaps.Count == 0;

    /// <summary>Cards blocked across every gap.</summary>
    public int BlockedCards =>
        Gaps.SelectMany(g => g.Blocks).DistinctBy(c => c.Name).Count();
}

/// <summary>
/// Evolutions you own but cannot play, because you do not own what they evolve from.
///
/// The collection view cannot show this: a Charizard sits in the grid looking like an asset, and
/// its being unplayable without a Charmeleon is invisible until you build a deck. The missing card
/// is almost always a common.
///
/// Matching is by name. Name-based decklist import was dropped because 40% of names map to several
/// card identities, but the question here is different: import asks "which Bulbasaur is this?",
/// which is ambiguous, while this asks "do I own anything called Bulbasaur?" — and the game evolves
/// on the printed name, so every answer is equally correct.
/// </summary>
public sealed class EvolutionGaps
{
    private readonly CardIndex _index;
    private readonly CardFacts _facts;
    private readonly IReadOnlyDictionary<string, double> _rates;

    /// <summary>Printings of each name, so a gap can be priced and pointed at.</summary>
    private readonly Dictionary<string, List<PocketCard>> _byName;

    /// <summary>Name to the name it evolves from, so a chain can be walked without owning it.</summary>
    private readonly Dictionary<string, string> _evolvesFrom;

    /// <summary>
    /// Basic, Stage 1, Stage 2 - so two steps is the deepest a chain goes. Also a hard cap, since
    /// bad upstream data could describe a cycle.
    /// </summary>
    private const int MaxChainDepth = 2;

    public EvolutionGaps(CardIndex index, CardFacts facts, PackOdds odds)
    {
        _index = index;
        _facts = facts;
        _rates = odds.BestRatesByCard();

        // CardName rather than a plain ordinal comparer, and it matters here more than anywhere:
        // the names on the LEFT of these tables come from the card index and the names on the
        // right come from the facts dataset, which punctuate an apostrophe differently. See
        // PackProphet.Text.CardName.
        _byName = new Dictionary<string, List<PocketCard>>(CardName.Comparer);
        _evolvesFrom = new Dictionary<string, string>(CardName.Comparer);

        // Trainers are not excluded here, because of the fossils: Omanyte evolves from Helix
        // Fossil, which is a Trainer card. Excluding trainers from the name tables reported eleven
        // missing fossils to a player who owned every card in the game.
        foreach (var card in index.All)
        {
            if (!_byName.TryGetValue(card.Name, out var list)) _byName[card.Name] = list = [];
            list.Add(card);

            if (index.DeckNrOf(card) is int nr
                && facts.For(nr)?.EvolvesFrom is { Length: > 0 } from)
            {
                _evolvesFrom[card.Name] = from;
            }
        }
    }

    /// <summary>
    /// Trainers, from the identity namespace rather than from the facts table. Identity comes from
    /// the artwork filename, so it is known for every card including the newest sets where stage
    /// data is missing.
    ///
    /// Used only to keep them out of the unverified count. A trainer has no pre-evolution so it
    /// cannot open a gap, but it can close one - the fossils are trainers, and a fossil is the
    /// pre-evolution of a real Pokémon line.
    /// </summary>
    private bool IsTrainer(PocketCard card) =>
        _index.DeckNrOf(card) is int nr && nr >= DeckBuilderNr.TrainerOffset;

    public EvolutionReport Find(Collection owned)
    {
        var ownedNames = new HashSet<string>(CardName.Comparer);
        var held = new List<PocketCard>();

        foreach (var card in _index.All.DistinctBy(c => c.OwnershipKey))
        {
            if (owned.Of(card) <= 0) continue;
            ownedNames.Add(card.Name);
            held.Add(card);
        }

        var blocks = new Dictionary<string, List<PocketCard>>(CardName.Comparer);
        var unverified = 0;

        foreach (var card in held)
        {
            var nr = _index.DeckNrOf(card);
            var fact = nr is null ? null : _facts.For(nr.Value);

            if (fact is null)
            {
                // A trainer with no stage data is not an unknown: it has no pre-evolution to be
                // missing.
                if (IsTrainer(card)) continue;

                // No stage data, so this card cannot be checked either way. Counted rather than
                // assumed safe: the newest sets are where the gaps and the missing data both live.
                unverified++;
                continue;
            }

            if (fact.EvolvesFrom is not { Length: > 0 } from) continue;

            // Walk down the chain rather than one step. A Stage 2 with neither lower stage owned
            // has two gaps, and the deeper one is invisible from the owned cards alone, since the
            // middle card is not owned.
            var next = from;
            for (var depth = 0; depth < MaxChainDepth && next is not null; depth++)
            {
                if (ownedNames.Contains(next)) break;

                if (!blocks.TryGetValue(next, out var list)) blocks[next] = list = [];
                list.Add(card);

                next = _evolvesFrom.GetValueOrDefault(next);
            }
        }

        var gaps = blocks
            .Select(kv => new EvolutionGap(
                kv.Key,
                kv.Value.DistinctBy(c => c.Name)
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
                Candidates(kv.Key),
                Deeper(kv.Key, ownedNames)))
            // Most cards unblocked first, then the cheapest fix.
            .OrderByDescending(g => g.Blocks.Count)
            .ThenBy(g => g.Easiest is null ? int.MaxValue : Rung(g.Easiest))
            .ThenBy(g => g.MissingName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new EvolutionReport(gaps, held.Count, unverified);
    }

    /// <summary>
    /// Printings that fill a gap, easiest first: lowest rarity, then actually sold in packs, then
    /// the best pull rate. Any of them works.
    /// </summary>
    private IReadOnlyList<PocketCard> Candidates(string name) =>
        (_byName.GetValueOrDefault(name) ?? [])
            .DistinctBy(c => c.OwnershipKey)
            // Obtainable ranks ahead of rarity: a card nobody can open is never the easiest fix
            // however common it is. The promo Charmeleon is a 1-diamond while every openable one is
            // a 2-diamond, so ranking by rarity first recommended the one card you cannot get.
            .OrderByDescending(c => c.Openable)
            .ThenBy(Rung)
            .ThenByDescending(c => _rates.GetValueOrDefault(c.Key))
            .ToArray();

    private int Rung(PocketCard card) => _index.Ladder.IndexOf(card.Rarity) ?? int.MaxValue;

    /// <summary>The missing card's own missing pre-evolution, or null if it needs nothing more.</summary>
    private string? Deeper(string name, IReadOnlySet<string> ownedNames) =>
        _evolvesFrom.GetValueOrDefault(name) is { Length: > 0 } from && !ownedNames.Contains(from)
            ? from
            : null;
}
