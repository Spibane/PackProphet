namespace PackProphet.Deck;

using PackProphet.Domain;

/// <summary>
/// A card's internal game id, recovered from its artwork filename.
///
/// This is card identity rather than printing. Every alternate art of the same card shares one
/// DeckBuilderNr, matching how the game treats decks: owning any printing satisfies a deck slot.
/// Deck membership is therefore compared by this number and never by set+number.
///
/// Pokémon and Trainers occupy separate id namespaces that both start at 1, so Trainers
/// are offset by <see cref="TrainerOffset"/> to keep them distinguishable in one value.
///
/// Derivation (reverse-engineered; see Nirostar/ptcgp-deck-qr, MIT):
///   cPK_10_000010_00_FUSHIGIDANE_C.webp -> 1        (Pokémon #1)
///   cTR_10_000080_00_KAINOKASEKI_C.webp -> 1000008  (Trainer #8)
/// The six-digit group is the id times ten.
/// </summary>
public static class DeckBuilderNr
{
    public const int TrainerOffset = 1_000_000;

    /// <summary>Numbers at or above this belong to the leading (trainer) segment of a deck code.</summary>
    public const int SpecialThreshold = 100_000;

    /// <summary>True when this number denotes a Trainer card.</summary>
    public static bool IsTrainer(int nr) => nr >= SpecialThreshold;

    /// <summary>
    /// Recover the number from an artwork filename, or null when the name does not fit the expected
    /// shape. Returns null rather than throwing, so an unparseable filename from upstream costs one
    /// card's deck support rather than the data load.
    /// </summary>
    public static int? FromImage(string? image)
    {
        // The embedded value is always the id x10; anything else means the format moved, which
        // DeckId reports as no id rather than as a number that would decode to the wrong card.
        if (!CardImageName.TryParse(image, out var name)) return null;
        if (name.DeckId is not int nr) return null;

        return name.IsTrainer ? TrainerOffset + nr : nr;
    }
}
