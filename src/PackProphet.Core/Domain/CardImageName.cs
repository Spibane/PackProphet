namespace PackProphet.Domain;

/// <summary>
/// The three facts encoded in a card's artwork filename.
///
///   cPK_10_000010_00_FUSHIGIDANE_C.webp
///    ^^  ^^ ^^^^^^ ^^
///    |   |  |      variant index — the foil or alternate-art printing
///    |   |  the deck-builder id, times ten
///    |   ignored
///    kind: PK for a Pokémon, TR for a Trainer
///
/// Two places read this name, and each had its own regular expression for it — one for the kind
/// and id, one for the variant — which is two descriptions of one format that could disagree.
/// Reading it by hand rather than by regex is also what lets the app ship without
/// System.Text.RegularExpressions, 114 KB gzipped of a first visit, and it takes the parse off
/// the interpreter: <see cref="PocketCard.VariantIndex"/> is read inside the odds engine's
/// per-card loop.
///
/// Anything that does not fit the shape is rejected rather than guessed at. A filename upstream
/// has changed the format of should cost one card its deck support, not silently decode as a
/// different card.
/// </summary>
public readonly record struct CardImageName(string Kind, int Id, int Variant)
{
    /// <summary>The deck-builder id the six-digit group encodes, or null if it is not a tenfold.</summary>
    public int? DeckId => Id % 10 == 0 && Id / 10 > 0 ? Id / 10 : null;

    public bool IsTrainer => Kind == "TR";

    public static bool TryParse(string? image, out CardImageName name)
    {
        name = default;
        if (string.IsNullOrEmpty(image) || image[0] != 'c') return false;

        var rest = image.AsSpan(1);

        // Kind: a run of capitals, then a separator.
        var i = 0;
        while (i < rest.Length && rest[i] is >= 'A' and <= 'Z') i++;
        if (i == 0 || i >= rest.Length || rest[i] != '_') return false;
        var kind = rest[..i];
        rest = rest[(i + 1)..];

        // A run of digits nothing reads, then a separator.
        i = 0;
        while (i < rest.Length && char.IsAsciiDigit(rest[i])) i++;
        if (i == 0 || i >= rest.Length || rest[i] != '_') return false;
        rest = rest[(i + 1)..];

        // The id group: exactly six digits, then a separator. Exactly, not at least — a
        // seven-digit group means the format moved and the value is not what this thinks it is.
        if (rest.Length < 7 || rest[6] != '_') return false;
        for (var d = 0; d < 6; d++)
            if (!char.IsAsciiDigit(rest[d])) return false;
        if (!int.TryParse(rest[..6], out var id)) return false;
        rest = rest[7..];

        // The variant, which unlike the rest is optional: a name that stops here is still a
        // readable name, and its variant is the zeroth printing.
        i = 0;
        while (i < rest.Length && char.IsAsciiDigit(rest[i])) i++;
        var variant = 0;
        if (i > 0 && i < rest.Length && rest[i] == '_' && !int.TryParse(rest[..i], out variant))
            variant = 0;

        // The two kinds that exist are handed back as constants rather than as a fresh string.
        // This runs inside the odds engine's per-card loop, where an allocation per card is the
        // kind of cost that only shows up on a phone.
        var kindText = kind switch { "PK" => "PK", "TR" => "TR", _ => kind.ToString() };

        name = new CardImageName(kindText, id, variant);
        return true;
    }
}
