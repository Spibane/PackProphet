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
        CardImageName.TryParse(Image, out var name) ? name.Variant : 0;

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
    public static string PackArtUrl(string packName) => ArtSource.PackArt(packName);

    /// <summary>
    /// A set's expansion logo. Verified present for every set, using the same casing as the
    /// set codes in the card data. Useful as a fallback where pack art is missing.
    /// </summary>
    public static string SetLogoUrl(string setCode) => ArtSource.SetLogo(setCode);

    /// <summary>
    /// Where to look for this card's art first. See <see cref="ArtSource"/> for why there is more
    /// than one place to look and what order they go in.
    /// </summary>
    public string ArtUrl => ArtSource.Candidates(this)[0];

    /// <summary>
    /// The rest of the chain, in order, pipe-separated for the markup to hand to js/imgloader.js.
    ///
    /// A string rather than a list because it is written into a data- attribute, and because the
    /// loader compares it against the previous value to notice a recycled row pointing at a
    /// different card. A pipe cannot occur in any of these URLs: set codes are alphanumeric with a
    /// hyphen and the rest is a fixed prefix.
    ///
    /// Empty where there is nothing else to try, which is never today but is not this property's
    /// business to promise.
    /// </summary>
    public string ArtFallbackUrls => string.Join('|', ArtSource.Candidates(this).Skip(1));

    /// <summary>
    /// The whole candidate chain as inline custom properties, for the places that draw a card as a
    /// background rather than as an img.
    ///
    /// An img gets the chain from js/imgloader.js, which listens for the error and moves down the
    /// list. A background image has no error to listen for, so every place in the app that draws a
    /// card this way -- a wishlist slot, a pack slot, a deck's face, a picker thumbnail -- took the
    /// first candidate and fell straight through to the generic placeholder whenever it was
    /// missing, which for a newly released set is every card.
    ///
    /// CSS does the falling back on its own: a background-image layer that fails to load is simply
    /// not painted, and the layers behind it show through. So the candidates stacked front to back
    /// ARE the chain, resolved by the browser, with no script and no error handling.
    ///
    /// Three NAMED properties rather than one comma-separated list, because the rule that consumes
    /// them also stacks the placeholder mark and weave behind the art, and background-position,
    /// -repeat and -size are per-layer lists that have to line up with the images. A list of
    /// variable length cannot line up with anything; three slots and `none` for the ones a card
    /// does not have can. See the rule on [style*="--art"] in app.css.
    /// </summary>
    public string ArtVars
    {
        get
        {
            var urls = ArtSource.Candidates(this);
            var css = new System.Text.StringBuilder();

            for (var i = 0; i < 3; i++)
            {
                var name = i == 0 ? "--art" : $"--art{i + 1}";
                css.Append(name).Append(':')
                   .Append(i < urls.Count ? $"url('{urls[i]}')" : "none")
                   .Append(';');
            }

            return css.ToString();
        }
    }

}
