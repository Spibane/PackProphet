using PackProphet.Domain;

namespace PackProphet.Vision;

/// <summary>
/// The in-game screen a screenshot was taken of. Each one is read the same way — find the card
/// slots, fingerprint them, look them up — and differs only in what the result means.
/// </summary>
public enum CardScreen
{
    /// <summary>
    /// Cards &rarr; a set, showing everything in the set: five to a row, with the cards you do not
    /// own drawn as blank slots and no copy counts anywhere.
    ///
    /// The only screen where a reading carries ownership <em>both</em> ways, and the only one where a
    /// card can be identified without being recognised — the list is in number order, so a
    /// recognised card names the blanks beside it. That inference is sound here precisely because
    /// nothing is filtered out: every slot is a card, so a gap in the numbering is a card the game
    /// drew as blank rather than a card that was simply not shown.
    /// </summary>
    OwnershipGrid,

    /// <summary>
    /// The other card list: three to a row, showing only cards you own, each with the number of
    /// copies. No blanks at all.
    ///
    /// Read very differently from <see cref="OwnershipGrid"/>, and the difference is a safety rule
    /// rather than a detail. Because unowned cards are absent rather than blank, a gap in the
    /// numbering carries no information — card 7 missing from the grid means either that it is not
    /// owned or that it is on the next page. So positional inference is switched off here entirely,
    /// not merely allowed to fail, and nothing is ever reported as missing from this screen. What it
    /// does carry that no other screen does is how many copies of each card are held.
    /// </summary>
    CopiesGrid,

    /// <summary>
    /// The cards from an opened pack. Every card present is a card just acquired, so a reading is a
    /// list of additions and never a removal — and the cards themselves say which pack was opened,
    /// which is what <see cref="PackIdentifier"/> is for.
    /// </summary>
    PackReveal,

    /// <summary>
    /// A Wonder Pick's line-up. Geometrically much like a pack's reveal — a few large cards in a row
    /// — so the pixels alone do not separate the two and the user says which. What differs is
    /// entirely in the meaning: these are cards on offer, one of which will be picked, not cards
    /// already in hand, so a reading feeds an appraisal rather than a collection.
    /// </summary>
    WonderPick,
}

/// <summary>How a card in a reading was identified, which is what decides how much to trust it.</summary>
public enum MatchSource
{
    /// <summary>Its art was recognised. Carries a distance.</summary>
    Art,

    /// <summary>
    /// Its art was not visible — the game draws a card you do not own as a placeholder — but the
    /// slot's place in a number-ordered list was pinned by cards around it that were recognised.
    /// Sound where the list really is in order, which is why it is only ever inferred on
    /// <see cref="CardScreen.OwnershipGrid"/>, and is labelled in the review so a user can see
    /// which rows the app is reasoning about rather than reading.
    /// </summary>
    GridPosition,

    /// <summary>
    /// The user said so, having been shown the slot the reader could not name. The strongest source
    /// here and the only one that is not a measurement: a person looking at a picture of a card and
    /// choosing it from the card list is not making an inference that can be wrong in the way the
    /// other two can.
    ///
    /// Kept as its own source rather than passed off as <see cref="Art"/> because the review table
    /// says where every row came from, and "you told me" is a different claim from "the artwork
    /// matched". It carries no distance, for the same reason a positioned card carries none.
    /// </summary>
    Manual,
}

/// <summary>
/// A card slot the reader found and could not name, and the picture of it.
///
/// The reading's loose ends, and the reason they are a list rather than the count they used to be:
/// a count can only tell someone the reading is incomplete, where a slot with its own picture beside
/// a search box lets them finish it. Every automatic answer has already been tried by the time one
/// of these exists — the fingerprint was too far out, or two cards were too alike to choose between,
/// or the card is cut off by the edge of the screen — so the remaining move is to ask.
/// </summary>
/// <param name="Thumb">
/// A data URL of the slot as it appears in the screenshot, or empty where the scanner cut none.
/// Empty is not an error and the row is still worth offering: a blank slot on a card list has
/// nothing to show and can still be named.
/// </param>
/// <param name="Owned">
/// What naming this slot would assert about ownership, decided the same way it is for a slot that
/// WAS recognised — the colour the game drained out of it, on the one screen where that means
/// anything. Carried here because the pixels are gone by the time anyone picks a card.
/// </param>
public sealed record ShotSlot(int Row, int Col, string Thumb, bool Owned);

/// <summary>One card found in a screenshot.</summary>
/// <param name="Owned">
/// Whether the shot says the user has it. Only meaningful on a collection grid; on the other
/// screens every card present is present, and the page decides what that implies.
/// </param>
/// <param name="Distance">
/// Fingerprint distance out of <see cref="ArtHash.Bits"/>, or -1 when the card was placed by
/// position rather than recognised.
/// </param>
public sealed record ShotMatch(
    PocketCard Card,
    int Row,
    int Col,
    int Distance,
    bool Owned,
    MatchSource Source)
{
    /// <summary>
    /// Copies held, where the screen shows a count and it was read. Only
    /// <see cref="CardScreen.CopiesGrid"/> shows one; everywhere else this stays null, and null
    /// means "this screen does not say", never "none".
    /// </summary>
    public int? Copies { get; init; }

    /// <summary>0 to 1, for display. A recognised card at distance 0 is 1; anything positioned is left at 0.</summary>
    public double Confidence => Source == MatchSource.Art
        ? Math.Clamp(1 - (double)Distance / ArtHashTable.MaxDistance, 0, 1)
        : 0;

    /// <summary>
    /// A recognised card whose artwork matched further out than real screenshots normally manage —
    /// see <see cref="ArtHashTable.ComfortableDistance"/>. Worth saying so, and not worth saying
    /// about the ordinary case.
    /// </summary>
    public bool IsMarginal =>
        Source == MatchSource.Art && Distance > ArtHashTable.ComfortableDistance;
}

/// <summary>
/// Everything read out of one screenshot, ready for the user to check before any of it touches
/// their collection. Nothing here is applied automatically: a fingerprint match is strong evidence
/// and still not a reason to silently rewrite what someone owns.
/// </summary>
/// <param name="UnreadSlots">
/// Card slots the reader found and could not name. Reported rather than hidden — a shot where nine
/// of twenty cells came back unread is a shot the user should retake — and reported one by one with
/// their pictures rather than as a total, so the ones worth naming by hand can be.
/// </param>
/// <param name="Notes">Anything the user should know about this particular reading, already phrased for them.</param>
public sealed record ShotReading(
    bool Ok,
    string? Error,
    CardScreen Screen,
    bool ScreenWasInferred,
    IReadOnlyList<ShotMatch> Matches,
    IReadOnlyList<ShotSlot> UnreadSlots,
    IReadOnlyList<string> Notes)
{
    public static ShotReading Failed(string error, CardScreen screen) =>
        new(false, error, screen, false, [], [], []);

    public int UnreadCells => UnreadSlots.Count;

    public int OwnedCount => Matches.Count(m => m.Owned);

    /// <summary>Cards the artwork identified. What "recognised" means, and never more than that.</summary>
    public int RecognisedCount => Matches.Count(m => m.Source == MatchSource.Art);

    /// <summary>
    /// Cards this reading actually names — recognised or named by hand, but not placed by their
    /// position in a list. The number a host acts on, which is why it is not
    /// <see cref="RecognisedCount"/>: a button offering to log "these 4 cards" over a hand where
    /// the fifth has just been named by hand is offering the wrong thing and counting it wrong.
    /// </summary>
    public int NamedCount => Matches.Count(m => m.Source != MatchSource.GridPosition);

    /// <summary>
    /// The same reading with slots the user named by hand folded in, as though they had been read
    /// all along.
    ///
    /// Folded in here rather than kept beside the reading because every host consumes
    /// <see cref="Matches"/>, and a card the user has pointed at is a card in the picture — a
    /// second list to remember to consult is a second list somebody will forget. A named slot
    /// leaves <see cref="UnreadSlots"/> at the same time, so the review's own count of what is
    /// still outstanding goes down as the work is done.
    ///
    /// Nothing is deduplicated. Each reader has already applied its own rule about repeats — a pack
    /// can genuinely contain two of a card and a copies list cannot — and re-applying one here
    /// would override the screen's own logic with a guess.
    /// </summary>
    public ShotReading WithManual(IReadOnlyDictionary<(int Row, int Col), PocketCard> named)
    {
        if (named.Count == 0) return this;

        var added = UnreadSlots
            .Where(s => named.ContainsKey((s.Row, s.Col)))
            .Select(s => new ShotMatch(
                named[(s.Row, s.Col)], s.Row, s.Col, -1, s.Owned, MatchSource.Manual));

        return this with
        {
            Matches = Matches.Concat(added).OrderBy(m => m.Row).ThenBy(m => m.Col).ToArray(),
            UnreadSlots = UnreadSlots.Where(s => !named.ContainsKey((s.Row, s.Col))).ToArray(),
        };
    }

    /// <summary>Sets the reading touched, for the coverage warning: a set absent from the table cannot appear here at all.</summary>
    public IReadOnlyList<string> SetsSeen =>
        Matches.Select(m => m.Card.Set).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
