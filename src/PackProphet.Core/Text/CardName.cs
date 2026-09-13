namespace PackProphet.Text;

using System.Collections.Generic;

/// <summary>
/// Comparing two printed card names that came out of two different datasets.
///
/// WHY THIS EXISTS
/// ==================================================================================
/// The app reads from two community sources and they punctuate differently. The card index spells
/// the Team Rocket line with a typographic apostrophe — Team Rocket’s Houndour, U+2019 — and the
/// card-detail dataset, which is where <c>evolves_from</c> comes from, spells the same name with
/// the ASCII one. Eleven of B4a's pre-evolutions are named that way.
///
/// Ordinal comparison therefore said you owned none of a card you owned four of: the detail page
/// badged every Team Rocket evolution "You own none", the collection's evolution gaps invented
/// eleven gaps that were not there, and the deck linter warned that a chain was broken in a deck
/// holding both halves of it.
///
/// It is a real fix rather than a patch over one dataset, because the game evolves on the PRINTED
/// name and neither source is wrong about what is printed — they disagree about which glyph to
/// write it with, and no player can see the difference.
///
/// WHAT IT DOES NOT DO
/// ----------------------------------------------------------------------------------
/// Nothing clever. It folds the quotation marks that mean an apostrophe and collapses whitespace;
/// it does not strip accents (Pokémon's é is the same character in both sources and Nidoran's
/// ♀/♂ carry meaning), and it does not do anything approximate. Two names either say the same
/// thing or they do not.
/// </summary>
public static class CardName
{
    /// <summary>
    /// A name reduced to what can be compared: one kind of apostrophe, single spaces, trimmed.
    /// Case is left alone, because the comparer below is case-insensitive anyway and a key that
    /// has been lowercased is no longer printable.
    /// </summary>
    public static string Key(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var built = new System.Text.StringBuilder(name.Length);
        var space = false;

        foreach (var ch in name.Trim())
        {
            // Every mark a dataset might use where the printed card has an apostrophe: the right
            // single quote, the modifier letter apostrophe, the grave and the acute. Farfetch'd
            // and the Team Rocket line are the names this decides.
            if (ch is '’' or 'ʼ' or '‘' or '`' or '´')
            {
                built.Append('\'');
                space = false;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                // One space, whatever was there. A non-breaking space is a space.
                if (!space) built.Append(' ');
                space = true;
                continue;
            }

            built.Append(ch);
            space = false;
        }

        return built.ToString();
    }

    /// <summary>Whether two printed names name the same card.</summary>
    public static bool Same(string? a, string? b) =>
        string.Equals(Key(a), Key(b), System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// For the dictionaries and sets that index cards by name. Drop-in for
    /// <c>StringComparer.OrdinalIgnoreCase</c>, which is what every one of them used to pass.
    /// </summary>
    public static IEqualityComparer<string> Comparer { get; } = new ByName();

    private sealed class ByName : IEqualityComparer<string>
    {
        public bool Equals(string? a, string? b) => Same(a, b);

        public int GetHashCode(string name) =>
            System.StringComparer.OrdinalIgnoreCase.GetHashCode(Key(name));
    }
}
