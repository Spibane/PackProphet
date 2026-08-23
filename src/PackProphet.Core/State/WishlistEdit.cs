namespace PackProphet.State;

/// <summary>
/// Editing rules for a wishlist, kept out of the UI so they can be tested and so every
/// surface that touches a wishlist agrees on them.
///
/// A wishlist is keyed by ownership key — a PRINTING, not an identity — deliberately, and
/// unlike a deck. A deck slot is filled by any printing of a card, but "the cards I think are
/// cool" is exactly a statement about particular artwork: wanting the alternate-art Pikachu is
/// not satisfied by owning the plain one.
/// </summary>
public static class WishlistEdit
{
    /// <summary>
    /// The same ceiling the collection itself uses, and for the same reason: a count beyond it
    /// is a typo rather than a want.
    ///
    /// Note what is deliberately NOT here — the two-per-name deck limit. That number is about
    /// what a deck may legally hold, and a wishlist is not a deck: extra copies are wanted as
    /// trade fodder and as flair material, so capping a wishlist at two would refuse the most
    /// ordinary reason to want a fourth.
    /// </summary>
    public const int MaxCopies = 99;

    public static Wishlist SetWanted(Wishlist list, string ownershipKey, int copies)
    {
        var next = new Dictionary<string, int>(list.Wanted);
        var clamped = Math.Clamp(copies, 0, MaxCopies);

        // Zero removes rather than storing a zero: a card wanted zero times is a card not on
        // the list, and keeping the entry would make it show up in every count.
        if (clamped > 0) next[ownershipKey] = clamped; else next.Remove(ownershipKey);

        return list with { Wanted = next };
    }

    /// <summary>
    /// Add or subtract copies. Deliberately not a cycler: with no small ceiling to wrap around,
    /// stepping is the only sane way to reach "six", and a control that silently emptied itself
    /// on the way past a limit would be worse than one that just stops.
    /// </summary>
    public static Wishlist Bump(Wishlist list, string ownershipKey, int delta) =>
        SetWanted(list, ownershipKey, list.Wanted.GetValueOrDefault(ownershipKey) + delta);

    /// <summary>
    /// A name that is not already taken, so two lists are never indistinguishable in a
    /// dropdown. "Wishlist", then "Wishlist 2", and so on.
    /// </summary>
    public static string FreshName(IEnumerable<Wishlist> existing, string stem = "Wishlist")
    {
        var taken = existing.Select(w => w.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!taken.Contains(stem)) return stem;

        for (var n = 2; ; n++)
        {
            var candidate = $"{stem} {n}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}
