namespace PackProphet.State;

/// <summary>
/// Editing rules for a chase list, kept out of the UI so they can be tested and so every surface
/// that touches a chase list agrees on them.
///
/// A chase list is keyed by ownership key — a printing rather than an identity, unlike a deck. A
/// deck slot is filled by any printing of a card, while a chase list names particular artwork:
/// wanting the alternate-art Pikachu is not satisfied by owning the plain one.
/// </summary>
public static class ChaseListEdit
{
    /// <summary>
    /// The same ceiling the collection itself uses: a count beyond it is a typo rather than a want.
    ///
    /// The two-per-name deck limit does not apply here. That number is about what a deck may
    /// legally hold, while extra copies on a chase list are wanted as trade fodder and flair
    /// material.
    /// </summary>
    public const int MaxCopies = 99;

    public static ChaseList SetWanted(ChaseList list, string ownershipKey, int copies)
    {
        var next = new Dictionary<string, int>(list.Wanted);
        var clamped = Math.Clamp(copies, 0, MaxCopies);

        // Zero removes rather than storing a zero: a card wanted zero times is a card not on
        // the list, and keeping the entry would make it show up in every count.
        if (clamped > 0) next[ownershipKey] = clamped; else next.Remove(ownershipKey);

        return list with { Wanted = next };
    }

    /// <summary>
    /// Add or subtract copies. Not a cycler: there is no small ceiling to wrap around, so stepping
    /// is how a count like six is reached.
    /// </summary>
    public static ChaseList Bump(ChaseList list, string ownershipKey, int delta) =>
        SetWanted(list, ownershipKey, list.Wanted.GetValueOrDefault(ownershipKey) + delta);

    /// <summary>
    /// A name that is not already taken, so two lists are never indistinguishable in a
    /// dropdown. "Chase list", then "Chase list 2", and so on.
    /// </summary>
    public static string FreshName(IEnumerable<ChaseList> existing, string stem = "Chase list")
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
