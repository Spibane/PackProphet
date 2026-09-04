using PackProphet.Data;
using PackProphet.Domain;

namespace PackProphet.Vision;

/// <summary>
/// Which pack a handful of recognised cards came out of, or why that cannot be said.
/// </summary>
/// <param name="PackKey">
/// The one pack that fits, as "A1:Mewtwo", or null when the cards do not narrow it to one.
/// </param>
/// <param name="Candidates">
/// Every pack that could have produced all of these cards. One entry means <paramref name="PackKey"/>
/// is set; several means the cards are genuinely shared and the user has to choose; none means no
/// single pack holds them all.
/// </param>
/// <param name="Set">The set, where every card agrees on one. Narrower than the pack and much more often known.</param>
/// <param name="Note">What stopped it being certain, phrased for the user, or null when it is.</param>
public sealed record PackGuess(
    string? PackKey,
    IReadOnlyList<string> Candidates,
    string? Set,
    string? Note)
{
    public bool IsCertain => PackKey is not null;

    public static PackGuess Nothing(string note) => new(null, [], null, note);
}

/// <summary>
/// Works out which pack a reveal screenshot came from, using nothing but the cards in it.
///
/// This is worth doing rather than asking, because the answer is usually already determined and the
/// user has just watched the game tell them. A card lists the packs it can come from; five cards
/// from one pack must all be in that pack; so the packs that could have produced the whole hand are
/// the intersection of five short lists. In Genetic Apex, where each of the three packs has about 80
/// exclusive cards against 46 shared ones, one exclusive card in the hand settles it — and a hand of
/// five almost always contains one.
///
/// It is not always determined, and the cases where it is not are real rather than hypothetical: a
/// God Pack holds five cards of the highest rarities, which are exactly the cards a set shares
/// across all of its packs. So this reports candidates rather than guessing among them, and the page
/// asks only in the cases that genuinely need asking.
/// </summary>
public static class PackIdentifier
{
    /// <summary>
    /// Identify the pack behind a reading. Everything except a card placed by its position in a
    /// list — that is not evidence about a pack, and on a reveal screen there is nothing placed
    /// that way anyway.
    ///
    /// A slot the user named by hand counts, and counts fully. It is the one card in the hand
    /// somebody has actually looked at, so treating it as weaker evidence than a fingerprint would
    /// be exactly backwards — and a five-card hand where the fifth was named is often the hand that
    /// finally narrows to one pack.
    /// </summary>
    public static PackGuess Identify(CardIndex index, ShotReading reading) =>
        Identify(index, reading.Matches
            .Where(m => m.Source != MatchSource.GridPosition)
            .Select(m => m.Card));

    public static PackGuess Identify(CardIndex index, IEnumerable<PocketCard> cards)
    {
        var found = cards.DistinctBy(c => c.Key).ToArray();
        if (found.Length == 0) return PackGuess.Nothing("No cards were recognised, so there is nothing to identify a pack from.");

        var sets = found.Select(c => c.Set).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var set = sets.Length == 1 ? sets[0] : null;

        if (sets.Length > 1)
            return new PackGuess(null, [], null,
                $"These cards come from {sets.Length} different sets ({string.Join(", ", sets)}), "
                + "so they are not one pack. A reprint recognised as its original printing can cause "
                + "this — check the list.");

        // Cards the data says come from no pack at all are set aside rather than allowed to empty
        // the intersection. One A1 card really is listed with no pack, and promos list "Vol. N"
        // groupings that are event giveaways rather than anything openable — neither is evidence
        // about which pack a hand came from, and treating them as such would report "no pack fits"
        // for a hand that plainly came from one.
        var evidence = found.Where(c => c.Openable).ToArray();
        var setAside = found.Length - evidence.Length;

        if (evidence.Length == 0)
            return new PackGuess(null, [], set,
                "None of these cards is sold in a pack anyone can open, so there is no pack to name.");

        var candidates = evidence
            .Select(c => index.PacksContaining(c).ToHashSet(StringComparer.Ordinal))
            .Aggregate((a, b) => { a.IntersectWith(b); return a; })
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        var aside = setAside == 0 ? "" :
            $" {setAside} card{(setAside == 1 ? "" : "s")} in the picture {(setAside == 1 ? "is" : "are")} "
            + "not sold in any pack, and was left out of this.";

        return candidates.Length switch
        {
            1 => new PackGuess(candidates[0], candidates, set, setAside == 0 ? null : aside.Trim()),

            0 => new PackGuess(null, [], set,
                    "No single pack holds all of these cards, which should not happen for one "
                    + "opening — check the list for a card read wrongly." + aside),

            // Every card in the hand is one the set shares across these packs. A God Pack looks
            // exactly like this, since it holds only the rarities that are in every pack.
            _ => new PackGuess(null, candidates, set,
                    $"Every card here is in {candidates.Length} of {set}'s packs, so the picture "
                    + "does not say which one was opened. That is normal for a God Pack." + aside),
        };
    }

    /// <summary>
    /// Whether a hand of this size is a whole pack for this set, given the set's published card
    /// counts. A set with two pack sizes accepts either.
    ///
    /// Reported rather than enforced: a screenshot taken mid-animation, or one where a card was not
    /// recognised, is a short hand that is still worth logging with the cards it did find.
    /// </summary>
    public static bool IsWholePack(PullRates rates, string? set, int cards) =>
        set is not null && rates.CardCounts(set) is { Count: > 0 } sizes && sizes.Contains(cards);
}
