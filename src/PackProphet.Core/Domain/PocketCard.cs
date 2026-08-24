namespace PackProphet.Domain;

/// <summary>
/// A card as published by flibustier/pokemon-tcg-pocket-database (cards.min.json). Only the fields
/// that file is trusted for.
/// </summary>
public sealed class PocketCard
{
    public string Set { get; set; } = "";
    public int Number { get; set; }
    public string Rarity { get; set; } = "";
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string[]? Packs { get; set; }

    /// <summary>
    /// Identifies this entry: a position in a set's numbered list. Used for display, navigation and
    /// set membership, not for ownership — see <see cref="OwnershipKey"/>.
    /// </summary>
    public string Key => $"{Set}-{Number}";

    /// <summary>
    /// Identifies the ownable card. The game treats owning a card as owning it for every set it
    /// appears in, and 215 of the 3,761 entries are re-listings of a card first printed elsewhere —
    /// A4b alone re-lists 214, because Deluxe packs reprint earlier sets. Keying ownership by
    /// set-number would demand the same card once per set and overstate what is left to collect:
    /// A4b would look like 379 cards to chase when 214 may already be owned.
    ///
    /// The artwork filename is the identity: re-listings share it exactly.
    /// </summary>
    public string OwnershipKey => Image;

    /// <summary>
    /// The artwork variant index from the filename ("..._00_..." -&gt; 0, "..._01_..." -&gt; 1).
    ///
    /// Its meaning is contextual. In Deluxe sets, a non-zero index at a diamond rarity is the foil
    /// printing (A4b pairs exactly: 64/64 C, 50/50 U, 25/25 R). Everywhere else a non-zero index is
    /// an alternate art, which is why foil handling keys off the set's pull rates naming CF/UF/RF
    /// rather than off this value. Excluded from DeckBuilderNr: a foil and a plain copy are the
    /// same card for deck purposes.
    /// </summary>
    public int VariantIndex =>
        System.Text.RegularExpressions.Regex.Match(Image ?? "", @"^c[A-Z]+_\d+_\d{6}_(\d+)_")
            is { Success: true } m && int.TryParse(m.Groups[1].Value, out var v) ? v : 0;

    /// <summary>
    /// A promo card. Promos are collectible and worth tracking, but their "Vol. N" groupings are
    /// distribution vehicles for event giveaways rather than packs anyone chooses to open, so they
    /// are excluded from every pack-facing surface: the pack picker, the pack ranking, and the
    /// missing-pull-rates warning.
    /// </summary>
    public bool IsPromo => Set.StartsWith("PROMO", StringComparison.OrdinalIgnoreCase);

    /// <summary>Cards with no pack are not obtainable from packs (promos, one A1 Immersive).</summary>
    public bool IsPackObtainable => Packs is { Length: > 0 };

    /// <summary>
    /// Sold in a pack a player can choose to open. Stricter than <see cref="IsPackObtainable"/>:
    /// most promos list a "Vol. N" pack, which records how the card was handed out at an event
    /// rather than something purchasable, so IsPackObtainable is true for them while no amount of
    /// opening will produce one.
    /// </summary>
    public bool Openable => IsPackObtainable && !IsPromo;

    /// <summary>
    /// Booster art for a pack, from the same community CDN as the card art. Files are named
    /// by pack name, so this maps straight from the card data's `packs` values.
    ///
    /// Coverage is 26 of 27 packs — "Paradox Drive" has no image at the time of writing — so
    /// callers must tolerate a 404 rather than assume art exists.
    /// </summary>
    public static string PackArtUrl(string packName) =>
        "https://cdn.jsdelivr.net/gh/flibustier/pokemon-tcg-exchange@main/public/images/packs/"
        + Uri.EscapeDataString(packName) + ".webp";

    /// <summary>
    /// A set's expansion logo. Verified present for every set, using the same casing as the
    /// set codes in the card data. Useful as a fallback where pack art is missing.
    /// </summary>
    public static string SetLogoUrl(string setCode) =>
        "https://cdn.jsdelivr.net/gh/flibustier/pokemon-tcg-exchange@main/public/images/sets/"
        + $"LOGO_expansion_{Uri.EscapeDataString(setCode)}_en_US.webp";

    /// <summary>
    /// Verified working CDN pattern: cards-by-set/{SET}/{number}.webp
    /// Note the number is NOT zero-padded here (unlike the TCGdex id).
    /// </summary>
    public string ArtUrl =>
        $"https://cdn.jsdelivr.net/gh/flibustier/pokemon-tcg-exchange@main/public/images/cards-by-set/{Set}/{Number}.webp";
}
