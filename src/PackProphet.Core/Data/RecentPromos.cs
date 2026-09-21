namespace PackProphet.Data;

using PackProphet.Domain;

/// <summary>
/// The promo cards that have just turned up in the game, as far as the data can say.
///
/// WHY THIS IS AN APPROXIMATION, AND WHY IT IS STILL WORTH HAVING
/// ==================================================================================
/// A promo is earned one at a time — a Wonder Pick event, a mission, a campaign — so there is no
/// pack to log and a screenshot saves nothing. Recording one meant opening the collection,
/// filtering to the promo set, scrolling past ninety cards you already have, tapping one, and
/// leaving. The work is entirely in the finding.
///
/// The obvious fix is to show only what is currently obtainable, and nothing in the data says
/// that. Neither community dataset carries an event calendar; every promo card has a null release
/// date, and the promo sets have none either. The only event listings are editorial web pages.
///
/// What the data does carry is order. Promo numbers are issued as cards are released, and the
/// volume groupings prove it: in both promo sets the volumes are strictly monotonic in number and
/// never overlap — PROMO-B runs Vol. 1 at 2-6 through Vol. 12 at 88-92 — with the promos that
/// belong to no volume sitting at the numbers matching when they landed. So "the newest N" is
/// exact, and it approximates "what is live" as closely as anything here can.
///
/// The window is therefore a stated fact rather than a claim: the caller says how many it is
/// showing, and never that those are the obtainable ones.
///
/// WHAT IT DOES NOT DO
/// ----------------------------------------------------------------------------------
/// It does not drop cards already owned. A promo can be earned more than once — repeatable
/// missions, an event run twice — so a second copy is an ordinary thing to record, and a list
/// that hid a card the moment you logged one would hide it exactly when you went to log the next.
///
/// It does not group by volume, although the data would allow it. A third of the promos are in no
/// volume at all — 34 of PROMO-B's 94, the Wonder Pick and mission cards — so volumes would file
/// two thirds of a set tidily and leave the rest nowhere, and the ones with no volume are exactly
/// the ones an event hands out. A flat run of the newest holds both kinds in the order they
/// arrived, which is the order they are being looked for in.
/// </summary>
public static class RecentPromos
{
    /// <summary>
    /// How many count as recent.
    ///
    /// Measured against the one promo set that has run its course. PROMO-A holds 117 cards across
    /// the whole of series A, roughly thirteen a month, and an event yields somewhere between two
    /// and five of them — so the current drop and the one before it is about ten cards. Generous
    /// rather than tight, because the cost of one card too many is a row nobody taps, and the cost
    /// of one too few is the scroll this exists to remove.
    /// </summary>
    public const int Window = 10;

    /// <summary>
    /// The promo set currently being filled: the promo set of the newest series.
    ///
    /// By the catalogue's own ordering rather than by release date, which promo sets do not have —
    /// <see cref="SetCatalog.SortKey(string)"/> already puts a series' promo set last within it,
    /// so the highest key among the promo sets is the newest series' one. Null where the data has
    /// no promo set at all, which is no set of test fixtures this app ships and is still not a
    /// reason to throw.
    /// </summary>
    public static string? CurrentSet(CardIndex index, SetCatalog sets)
    {
        string? best = null;

        foreach (var set in index.BySet.Keys)
        {
            if (!CardIndex.IsPromoSet(set)) continue;

            if (best is null
                || string.CompareOrdinal(sets.SortKey(set), sets.SortKey(best)) > 0)
            {
                best = set;
            }
        }

        return best;
    }

    /// <summary>
    /// The newest promos in <paramref name="set"/>, highest card number first.
    ///
    /// Fewer than <paramref name="count"/> where the set holds fewer, and empty for a set that is
    /// not in the data — a caller rendering a list wants an empty list, not an exception.
    /// </summary>
    public static IReadOnlyList<PocketCard> Newest(CardIndex index, string set, int count = Window)
    {
        if (count <= 0 || !index.BySet.TryGetValue(set, out var cards)) return [];

        return cards.OrderByDescending(c => c.Number).Take(count).ToArray();
    }

    /// <summary>The newest promos in the set <see cref="CurrentSet"/> names.</summary>
    public static IReadOnlyList<PocketCard> Newest(
        CardIndex index, SetCatalog sets, int count = Window) =>
        CurrentSet(index, sets) is { } set ? Newest(index, set, count) : [];
}
