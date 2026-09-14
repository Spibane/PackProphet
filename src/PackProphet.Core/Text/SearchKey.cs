namespace PackProphet.Text;

/// <summary>
/// What someone TYPED, reduced to what it can be matched against.
///
/// WHY THIS EXISTS
/// ==================================================================================
/// "poke ball" found nothing, because the card is Poké Ball. Nobody reaches for Option-E to search
/// a card list, and a search box that requires the accent is a search box that says a card the
/// user is looking at does not exist. The same failure covers "pokemon", "flabebe" and "nidoran" —
/// four accented spellings across the index, and one of them is in the name of the game.
///
/// The apostrophe is the same bug wearing a different glyph. The index spells the Team Rocket line
/// with a typographic apostrophe (U+2019) — about thirty cards, plus Farfetch'd, Professor's
/// Research, Clemont's Backpack and Kid's Room — and a keyboard produces the ASCII one. So
/// "rocket's meowth" missed too, and so did "rockets meowth", which is what most people type.
///
/// HOW IT DIFFERS FROM <see cref="CardName"/>
/// ----------------------------------------------------------------------------------
/// CardName compares two printed names from two datasets that disagree about which glyph to write
/// an apostrophe with. Both sides are names a publisher printed, so it folds the punctuation and
/// deliberately leaves accents alone: é is é in both sources, and two printed names either say the
/// same thing or they do not.
///
/// This is the other question — does what a person typed reach the name — and there the accents
/// have to go, because the person is typing on a keyboard that does not have them. Two folds for
/// two jobs, rather than one fold that is wrong about one of them.
///
/// WHAT IT DOES NOT DO
/// ----------------------------------------------------------------------------------
/// No stemming, no edit distance, no transliteration table. It strips the accent off a Latin
/// letter and drops the marks that mean an apostrophe. ♀ and ♂ are left alone: they are the whole
/// of the difference between two Nidoran, and a search that folded them could not tell them apart.
/// </summary>
public static class SearchKey
{
    /// <summary>
    /// A string reduced to the letters a keyboard produces. Case is left alone — every comparison
    /// below is case-insensitive, and lowercasing here would mean the callers that print what they
    /// matched could no longer use it.
    ///
    /// Returns the SAME INSTANCE when there is nothing to fold, which is the overwhelming majority:
    /// this runs over every card name on every keystroke, and 3,829 of 3,879 names are plain ASCII.
    /// </summary>
    public static string Fold(string? text)
    {
        if (text is not { Length: > 0 }) return "";
        if (!NeedsFolding(text)) return text;

        var built = new System.Text.StringBuilder(text.Length);

        foreach (var ch in text)
        {
            // Dropped rather than normalised to '\''. Folding U+2019 to the ASCII apostrophe would
            // fix "rocket's" and leave "rockets" — which is what a phone keyboard and most typists
            // produce — still finding nothing. Dropping it accepts both spellings, and no two cards
            // in the index are told apart by an apostrophe.
            if (Apostrophe(ch)) continue;

            built.Append(Base(ch));
        }

        return built.ToString();
    }

    /// <summary>
    /// Whether <paramref name="needle"/> appears in <paramref name="haystack"/>, with both sides
    /// folded. Case-insensitive, like every other comparison in search.
    /// </summary>
    public static bool Has(string? haystack, string foldedNeedle) =>
        haystack is { Length: > 0 }
        && Fold(haystack).Contains(foldedNeedle, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The cheap pass that keeps <see cref="Fold"/> from allocating for an ASCII name. A char
    /// above the ASCII range might fold and might not, and the apostrophe family is partly inside
    /// it, so both are asked about.
    /// </summary>
    private static bool NeedsFolding(string text)
    {
        foreach (var ch in text)
            if (ch > 127 || Apostrophe(ch)) return true;

        return false;
    }

    private static bool Apostrophe(char ch) => ch is '\'' or '’' or 'ʼ' or '‘' or '`' or '´';

    /// <summary>
    /// The unaccented letter, for the accents that appear in this game's names.
    ///
    /// A table rather than Unicode decomposition, because the app is built with
    /// InvariantGlobalization: the ICU payload is not in the wasm bundle, so <c>string.Normalize</c>
    /// has nothing to decompose with. Which is no loss at this size — the index holds é and nothing
    /// else today, and the rest of the list is the accents Pokémon names carry in the languages
    /// this could be localised into.
    /// </summary>
    private static char Base(char ch) => ch switch
    {
        'á' or 'à' or 'â' or 'ä' or 'ã' or 'å' => 'a',
        'Á' or 'À' or 'Â' or 'Ä' or 'Ã' or 'Å' => 'A',
        'é' or 'è' or 'ê' or 'ë' => 'e',
        'É' or 'È' or 'Ê' or 'Ë' => 'E',
        'í' or 'ì' or 'î' or 'ï' => 'i',
        'Í' or 'Ì' or 'Î' or 'Ï' => 'I',
        'ó' or 'ò' or 'ô' or 'ö' or 'õ' => 'o',
        'Ó' or 'Ò' or 'Ô' or 'Ö' or 'Õ' => 'O',
        'ú' or 'ù' or 'û' or 'ü' => 'u',
        'Ú' or 'Ù' or 'Û' or 'Ü' => 'U',
        'ñ' => 'n',
        'Ñ' => 'N',
        'ç' => 'c',
        'Ç' => 'C',
        _ => ch,
    };
}
