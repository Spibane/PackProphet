using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Text;

namespace PackProphet.Vision;

/// <summary>
/// Turns the card slots found in a screenshot into named cards. This is the whole of the
/// recognition logic and none of the pixel handling, so every rule below is testable from a
/// hand-written <see cref="ShotScan"/> with no browser and no image.
///
/// The approach is nearest-neighbour over a table of art fingerprints generated offline, and the
/// reason it works on a device with no model and no server is that the search space is tiny and
/// closed: a screenshot can only contain cards that exist, there are about 3,761 of them, and the
/// game renders their art from the same files the table was built from. There is no generalisation
/// to do. The hard part is not recognition, it is the near-duplicates — see
/// <see cref="ArtHashTable.AmbiguityMargin"/> — and knowing when to say nothing.
/// </summary>
public sealed class ScreenshotReader
{
    /// <summary>
    /// Below this much large-scale structure a slot holds no art. On a collection grid that is the
    /// placeholder the game draws for a card you do not own, which is the single most useful thing
    /// in the whole screenshot: it is a card named by its neighbours and known to be missing.
    /// Elsewhere it is an empty slot at the end of a row, or furniture.
    ///
    /// Set low on purpose, because the two ways of being wrong here are not symmetrical. A
    /// placeholder mistaken for art becomes an unread slot: information lost, nothing harmed. Art
    /// mistaken for a placeholder becomes a card reported as <em>missing</em> — a card the user owns,
    /// proposed for deletion. Measured against synthetic art the gap is wide (art around 0.06,
    /// a flat slot under 0.01), and the floor sits nearer the bottom of it than the middle.
    /// <see cref="LooksLikeAPlaceholder"/> adds a second, relative condition for the same reason.
    /// </summary>
    public const double DetailFloor = 0.025;

    /// <summary>
    /// Saturation below which a slot that <em>did</em> fingerprint is treated as a card the user
    /// does not own. Some of the game's unowned renderings keep the artwork and drain the colour
    /// out of it rather than replacing it with a placeholder, and a gradient-sign hash is
    /// deliberately blind to that — it matches the greyed art perfectly well. So the colour has to
    /// be read separately, and it is read against an absolute floor rather than against the rest
    /// of the shot: a relative split would invent an owned/unowned boundary in a screenshot where
    /// every card is owned, which is the common case and the one it must not get wrong.
    /// </summary>
    public const double ColourlessFloor = 0.06;

    private readonly CardIndex _index;
    private readonly ArtHashTable _table;

    public ScreenshotReader(CardIndex index, ArtHashTable table)
    {
        _index = index;
        _table = table;
    }

    /// <summary>
    /// Read one screenshot. <paramref name="screen"/> is what the user said they photographed;
    /// null asks for a guess, which the result flags as a guess so the page can offer to change it.
    /// </summary>
    public ShotReading Read(ShotScan scan, CardScreen? screen = null)
    {
        var fallbackKind = screen ?? CardScreen.OwnershipGrid;

        if (!scan.Ok)
            return ShotReading.Failed(scan.Error ?? "That image could not be read.", fallbackKind);

        if (_table.Count == 0)
            return ShotReading.Failed(
                "The card fingerprints could not be loaded, so nothing in the image can be named.",
                fallbackKind);

        if (scan.Lattice is not { Cols: > 0, Rows: > 0 } lattice || scan.Cells.Count == 0)
            return ShotReading.Failed(
                "No cards were found in that image. A screenshot of the whole screen works better "
                + "than a crop of one card.", fallbackKind);

        var kind = screen ?? Infer(scan);
        var recognised = Recognise(scan.Cells);

        return kind switch
        {
            // Only the ownership list may reason about what is absent, because it is the only screen
            // where absence is drawn rather than filtered out.
            CardScreen.OwnershipGrid => ReadOwnershipGrid(scan, lattice, recognised, kind, screen is null),
            CardScreen.CopiesGrid => ReadCopiesGrid(scan, recognised, kind, screen is null),
            _ => ReadHand(scan, recognised, kind, screen is null),
        };
    }

    /// <summary>
    /// Which screen this is, from geometry alone — a default the user can override, never a
    /// conclusion. Three shapes are distinguishable and one pair is not.
    ///
    /// The two card lists differ in how many cards they fit across, which is the whole of the
    /// difference in the pixels: the ownership list packs five to a row, the copies list three. So
    /// slot width as a fraction of the screen separates them, and it separates them by a wide
    /// margin — a fifth of the screen against a third.
    ///
    /// A pack's reveal and a Wonder Pick's line-up are both five large cards in a three-then-two
    /// arrangement and are not distinguishable at all. The user says which, and until they do this
    /// answers with the pack, because a pack is opened far more often than a Wonder Pick is
    /// screenshotted.
    /// </summary>
    public static CardScreen Infer(ShotScan scan)
    {
        if (scan.Lattice is not { } lattice) return CardScreen.OwnershipGrid;

        // Rows that hold something, not rows the lattice reported. The detector tiles the whole
        // screenshot, so a shot of five cards on a tall screen comes back with rows that are really
        // the empty space around them.
        var populated = scan.Cells
            .Where(c => c.Detail >= DetailFloor)
            .Select(c => c.Row)
            .Distinct()
            .Count();

        var width = lattice.RelativeCellWidth;

        // A hand of five: two populated rows at most, and cards taking a third of the width or more.
        if (populated <= 2 && width >= 0.28) return CardScreen.PackReveal;

        return width >= 0.28 ? CardScreen.CopiesGrid : CardScreen.OwnershipGrid;
    }

    private readonly record struct Recognition(ShotCell Cell, PocketCard Card, int Distance);

    /// <summary>Fingerprint every slot with something in it and look each one up. Slots that stay unnamed fall out here.</summary>
    private Dictionary<int, Recognition> Recognise(List<ShotCell> cells)
    {
        var found = new Dictionary<int, Recognition>();

        foreach (var cell in cells)
        {
            if (cell.Detail < DetailFloor) continue;

            var identified = Identify(cell);
            if (identified is null) continue;
            var (card, distance) = identified.Value;

            // Keyed by slot so a lattice that reported a slot twice cannot produce the same card
            // twice; the closer reading wins.
            var key = CellKey(cell);
            if (!found.TryGetValue(key, out var existing) || distance < existing.Distance)
                found[key] = new Recognition(cell, card, distance);
        }

        return found;
    }

    /// <summary>
    /// Which card a fingerprint belongs to, or null when the answer is not clear enough to use.
    ///
    /// The ambiguity rule lives here rather than in <see cref="ArtHashTable"/> because it is a rule
    /// about <em>cards</em>, not about distances. Candidates are grouped by
    /// <see cref="PocketCard.OwnershipKey"/> first: entries that resolve to the same ownable card are
    /// one answer however many of them there are, which is what makes the 215 reprints harmless. The
    /// winning group then has to be <see cref="ArtHashTable.AmbiguityMargin"/> bits clear of the next
    /// group that is a genuinely different card.
    ///
    /// The case that forced this to be about ownership rather than about identical fingerprints is
    /// the foil printing. A foil differs from its plain twin only in the card's frame, which is the
    /// one part the art window deliberately excludes — so the two sit 0 to 2 bits apart while being
    /// different ownable cards. Grouping by fingerprint would call them the same answer and return
    /// whichever came first; grouping by ownership calls them a tie, and a tie is reported as unread.
    /// That costs a few percent of cards and is the right way round.
    /// </summary>
    /// <summary>
    /// The card a cell holds, trying each crop it offers and keeping the one identified most
    /// confidently.
    ///
    /// Every crop has to pass <see cref="Identify(ArtHash)"/> on its own — close enough, and clear of
    /// the next different card by the full margin — so offering nine crops cannot turn a doubtful
    /// reading into a confident one. What it does is absorb the two to four pixels of error the
    /// detector cannot remove: on a Wonder Pick line-up the centre crop scores 21 to 22 bits against
    /// the right cards, where a crop three pixels over scores 0 to 3.
    ///
    /// Among the crops that pass, the nearest wins. Not the one with the widest margin, which was
    /// tried and is a trap: margin alone ignores distance, so a crop where the card is 40 bits away
    /// and everything else is 43 beats a crop where it is 4 bits away and clear by 20.
    /// </summary>
    private (PocketCard Card, int Distance)? Identify(ShotCell cell)
    {
        // The centre crop first, and if it already identifies the card comfortably that is the
        // answer. Searching all nine costs nine passes over 3,761 fingerprints per cell, which is
        // several seconds on a phone for a full page of cards — and most crops are good enough that
        // the extra eight would only confirm what the first one said.
        if (ArtHash.TryParse(cell.Hash, out var centre)
            && Identify(centre) is { } quick
            && quick.Distance <= ArtHashTable.ComfortableDistance)
        {
            return quick;
        }

        (PocketCard Card, int Distance)? best = null;

        foreach (var text in cell.AllHashes)
        {
            if (!ArtHash.TryParse(text, out var hash)) continue;

            var found = Identify(hash);
            if (found is null) continue;
            if (best is null || found.Value.Distance < best.Value.Distance) best = found;
        }

        return best;
    }

    private (PocketCard Card, int Distance)? Identify(ArtHash hash)
    {
        var best = new Dictionary<string, (PocketCard Card, int Distance)>();
        var order = new List<string>();

        foreach (var (entry, distance) in _table.Nearest(hash))
        {
            if (!_index.ByKey.TryGetValue(entry.Key, out var card)) continue;

            var key = card.OwnershipKey;
            if (best.TryGetValue(key, out var held))
            {
                if (distance < held.Distance) best[key] = (card, distance);
                continue;
            }

            best[key] = (card, distance);
            order.Add(key);
        }

        if (order.Count == 0) return null;

        var ranked = order.Select(k => best[k]).OrderBy(c => c.Distance).ToArray();
        var winner = ranked[0];

        if (winner.Distance > ArtHashTable.MaxDistance) return null;
        if (ranked.Length > 1 && ranked[1].Distance - winner.Distance < ArtHashTable.AmbiguityMargin)
            return null;

        return winner;
    }

    private static int CellKey(ShotCell cell) => cell.Row * 1000 + cell.Col;

    /// <summary>
    /// Slots that hold something and came back without a name, in reading order.
    ///
    /// "Hold something" is <see cref="DetailFloor"/>, the same line that decides whether a slot is
    /// searched for at all: a slot below it was never looked up, and on these screens it is the
    /// empty space at the end of a row rather than a card. Offering those for naming would be
    /// offering to name nothing. The ownership list keeps its own version of this, because there a
    /// flat slot is a card — see <see cref="ReadOwnershipGrid"/>.
    ///
    /// Deduplicated by slot for the same reason the recognition is: a lattice that reported one
    /// slot twice must not ask the user about it twice.
    /// </summary>
    private static List<ShotSlot> Unnamed(
        ShotScan scan, Dictionary<int, Recognition> recognised, Func<ShotCell, bool> owned) =>
        scan.Cells
            .Where(c => c.Detail >= DetailFloor && !recognised.ContainsKey(CellKey(c)))
            .DistinctBy(CellKey)
            .OrderBy(c => c.Row).ThenBy(c => c.Col)
            .Select(c => new ShotSlot(c.Row, c.Col, c.Thumb, owned(c)))
            .ToList();

    /// <summary>
    /// A pack's five cards, or a Wonder Pick's. No ownership is decided here: everything visible is
    /// a card that is there, and what that means — added to a collection, or weighed up as an offer
    /// — belongs to the page, not to the reader.
    /// </summary>
    private ShotReading ReadHand(
        ShotScan scan, Dictionary<int, Recognition> recognised, CardScreen kind, bool inferred)
    {
        var matches = recognised.Values
            .OrderBy(r => r.Cell.Row).ThenBy(r => r.Cell.Col)
            .Select(r => new ShotMatch(r.Card, r.Cell.Row, r.Cell.Col, r.Distance, true, MatchSource.Art))
            .ToArray();

        var unread = Unnamed(scan, recognised, _ => true);
        var notes = new List<string>();

        // No note here about a hand coming up short of five. It was one, and it could not stay one:
        // a note is written when the reading is made, and this one stops being worth saying the
        // moment the user names the missing slot themselves. What is left of a hand is a live
        // question about the reading as it now stands rather than a fact about how it was read, so
        // it is asked where it can be re-asked — see ShotImport.

        AddSetSpreadNote(matches, notes);
        return new ShotReading(true, null, kind, inferred, matches, unread, notes);
    }

    /// <summary>
    /// The copies list: three to a row, only cards you own, each carrying how many copies.
    ///
    /// The shortest read of the four, and the rules are all rules about restraint. Nothing here is
    /// evidence about a card that is absent, because absence on this screen means "not shown" — the
    /// card may be unowned, or it may be on the next page, or the list may be filtered. So no slot
    /// is named by its position and nothing is ever reported missing, and those are not degradations
    /// of the ownership-list behaviour that happen to fail safely; they are switched off. A gap in
    /// the numbering on a filtered list can agree with an arithmetic run by coincidence, and a
    /// coincidence is all it would take to propose deleting a card someone owns.
    ///
    /// What this screen does carry that no other does is the copy count printed on each card. Until
    /// that is read, a card recognised here says "at least one", which is what
    /// <see cref="ShotMatch.Copies"/> being null means.
    /// </summary>
    private ShotReading ReadCopiesGrid(
        ShotScan scan, Dictionary<int, Recognition> recognised, CardScreen kind, bool inferred)
    {
        var matches = recognised.Values
            .OrderBy(r => r.Cell.Row).ThenBy(r => r.Cell.Col)
            // The one screen that says how many. A count that could not be read stays null, which
            // means "this screen did not say" and never "none" — see CountReader.Read.
            .Select(r => new ShotMatch(r.Card, r.Cell.Row, r.Cell.Col, r.Distance, true, MatchSource.Art)
                         { Copies = CountReader.Read(r.Cell.Digits) })
            .DistinctBy(m => m.Card.OwnershipKey)
            .ToArray();

        var unread = Unnamed(scan, recognised, _ => true);
        var notes = new List<string>();

        // No note about this list being unable to report what is missing. It is an import: it adds
        // the cards it read, and nothing about the screen implies otherwise. The warning answered a
        // question nobody asked, and answered it in the one place where the reading itself is the
        // thing to read.

        // Counted rather than passed over: a reading that recognised every card and could read none
        // of their counts is a reading the user should be told about, because it looks complete.
        var unreadCounts = matches.Count(m => m.Copies is null);
        if (unreadCounts > 0 && matches.Length > 0)
            notes.Add($"The number of copies could not be read for {unreadCounts} of "
                      + $"{matches.Length} {Fmt.S(matches.Length, "card")}. Those are recorded as one "
                      + "copy, and never fewer than you already have.");

        AddSetSpreadNote(matches, notes);
        return new ShotReading(true, null, kind, inferred, matches, unread, notes);
    }

    /// <summary>
    /// A page of the ownership list: every card in the set, five to a row, with the ones you do not
    /// own drawn as blank slots carrying their card number. The one screen where the list's own
    /// ordering can name a slot whose art was never drawn.
    /// </summary>
    private ShotReading ReadOwnershipGrid(
        ShotScan scan, ShotLattice lattice, Dictionary<int, Recognition> recognised,
        CardScreen kind, bool inferred)
    {
        var notes = new List<string>();
        var matches = new List<ShotMatch>();

        foreach (var r in recognised.Values)
        {
            // Art that fingerprinted but has had the colour drained out of it is the game showing
            // a card the user does not own.
            var owned = r.Cell.Saturation >= ColourlessFloor;
            matches.Add(new ShotMatch(r.Card, r.Cell.Row, r.Cell.Col, r.Distance, owned, MatchSource.Art));
        }

        var anchors = RowAnchors(lattice, recognised, notes);
        var placeholderCeiling = PlaceholderCeiling(recognised.Values);
        var unread = new List<ShotSlot>();

        foreach (var cell in scan.Cells)
        {
            if (recognised.ContainsKey(CellKey(cell))) continue;

            // A slot with art in it that could not be named is not evidence of anything. Set aside
            // and left alone: guessing its number from its neighbours would be sound, but calling
            // an unreadable card *missing* when the reason it was unreadable might be a foil or a
            // crop would delete something the user owns. It is handed back as a slot the user can
            // name, which is the one thing that does settle it.
            if (!LooksLikeAPlaceholder(cell, placeholderCeiling))
            {
                unread.Add(new ShotSlot(cell.Row, cell.Col, cell.Thumb,
                                        cell.Saturation >= ColourlessFloor));
                continue;
            }

            if (!anchors.TryGetValue(cell.Row, out var anchor)) continue;

            var (set, first) = anchor;
            var number = first + cell.Col;
            if (number < 1) continue;
            if (!_index.ByKey.TryGetValue($"{set}-{number}", out var card)) continue;

            matches.Add(new ShotMatch(card, cell.Row, cell.Col, -1, false, MatchSource.GridPosition));
        }

        AddSetSpreadNote(matches, notes);

        var ordered = matches
            .OrderBy(m => m.Row).ThenBy(m => m.Col)
            .DistinctBy(m => m.Card.OwnershipKey)
            .ToArray();

        return new ShotReading(true, null, kind, inferred, ordered, unread, notes);
    }

    /// <summary>
    /// Pins each row of the grid to the set's numbering: which set it belongs to, and what card
    /// number sits in its leftmost slot. Every recognised card in the row votes — its number minus
    /// its column is the number column zero must hold, if the row really is a run of the list — and
    /// the majority wins.
    ///
    /// Row by row rather than once for the whole screen, which is a deliberate concession to what
    /// the lattice detector can and cannot promise. It finds a repeating period in the screenshot's
    /// edges, and a full-screen shot has a status bar above the list and navigation below it, so the
    /// grid it reports may well tile over furniture and include a phantom row. A single
    /// screen-wide offset would be thrown off by one row of that kind; per-row offsets are not,
    /// because a row of chrome recognises nothing and simply anchors nothing.
    ///
    /// A row is left unanchored, and says why, when its votes do not agree — the list was sorted by
    /// rarity rather than number, a filter was on, the shot spans a page boundary. The positional
    /// reasoning is invalid in all of those, and the reading falls back to naming only what it
    /// recognised.
    /// </summary>
    private static Dictionary<int, (string Set, int First)> RowAnchors(
        ShotLattice lattice, Dictionary<int, Recognition> recognised, List<string> notes)
    {
        var anchors = new Dictionary<int, (string Set, int First)>();
        var disagreed = false;

        foreach (var row in recognised.Values.GroupBy(r => r.Cell.Row))
        {
            var votes = new Dictionary<(string Set, int First), int>();
            foreach (var r in row) votes[(r.Card.Set, r.Card.Number - r.Cell.Col)] =
                votes.GetValueOrDefault((r.Card.Set, r.Card.Number - r.Cell.Col)) + 1;

            var best = votes.OrderByDescending(v => v.Value).First();

            // Two agreeing cards, and a majority of what the row recognised. One card is not a
            // pattern: on its own it would let a single match invent numbers for a whole row.
            if (best.Value >= 2 && best.Value * 2 >= row.Count()) anchors[row.Key] = best.Key;
            else if (row.Count() >= 2) disagreed = true;
        }

        if (disagreed)
            notes.Add("Some cards in this shot are not in set-number order, so the blank slots "
                      + "around them were left alone rather than guessed at.");

        ExtendToUnanchoredRows(lattice, recognised, anchors);
        return anchors;
    }

    /// <summary>
    /// Carries the numbering into rows that anchored nothing themselves, using the fact that one row
    /// of a list starts <see cref="ShotLattice.Cols"/> cards after the row above it.
    ///
    /// Only ever from two rows that already agree on that stride. Two is what makes it evidence: a
    /// pair of anchored rows exactly one row-width apart in the numbering is a strong statement that
    /// the detected grid has the same shape as the game's, and it is that shape — not any single
    /// row — that the extension depends on. Rows holding recognised cards that contradict the
    /// extension keep their own anchor, since art beats arithmetic.
    /// </summary>
    private static void ExtendToUnanchoredRows(
        ShotLattice lattice, Dictionary<int, Recognition> recognised,
        Dictionary<int, (string Set, int First)> anchors)
    {
        if (anchors.Count < 2 || lattice.Cols <= 0) return;

        var strides = anchors
            .GroupBy(a => (a.Value.Set, Offset: a.Value.First - a.Key * lattice.Cols))
            .OrderByDescending(g => g.Count())
            .First();

        if (strides.Count() < 2) return;
        var (set, offset) = strides.Key;

        var rows = recognised.Values.Select(r => r.Cell.Row)
            .Concat(anchors.Keys).Distinct().ToArray();

        foreach (var row in rows)
        {
            if (anchors.ContainsKey(row)) continue;
            var first = offset + row * lattice.Cols;
            if (first >= 1) anchors[row] = (set, first);
        }
    }

    /// <summary>
    /// Whether a slot is the game's rendering of a card the user does not own, and so may be named
    /// from its position and reported missing.
    ///
    /// Two conditions, both of which have to hold. The absolute one is <see cref="DetailFloor"/>.
    /// The relative one is that the slot has to look unlike the cards this same screenshot did
    /// recognise — a placeholder is far flatter than any card art beside it, and comparing within
    /// the shot costs nothing and cannot be fooled by a device that renders everything darker or
    /// softer than the reference. The relative test can only ever withhold a "missing", never
    /// produce one, which is the property that makes it safe to add.
    /// </summary>
    private static bool LooksLikeAPlaceholder(ShotCell cell, double ceiling) =>
        cell.Detail < DetailFloor && cell.Detail < ceiling;

    /// <summary>
    /// Half the typical structure of the cards actually recognised in this shot. With nothing
    /// recognised there is nothing to compare against, and the absolute floor stands alone.
    /// </summary>
    private static double PlaceholderCeiling(IEnumerable<Recognition> recognised)
    {
        var details = recognised.Select(r => r.Cell.Detail).OrderBy(d => d).ToArray();
        return details.Length == 0 ? double.MaxValue : details[details.Length / 2] * 0.5;
    }

    private static void AddSetSpreadNote(IReadOnlyCollection<ShotMatch> matches, List<string> notes)
    {
        var sets = matches.Select(m => m.Card.Set).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (sets.Length > 1)
            notes.Add($"Cards from {sets.Length} sets were recognised ({string.Join(", ", sets)}). "
                      + "Check the list below before applying it.");
    }
}
