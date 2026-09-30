namespace PackProphet.Vision;

/// <summary>
/// Several packs opened at once, read back out of the screenshots of their results list, one
/// reading per pack.
/// </summary>
/// <param name="Packs">
/// Each pack as a reading of its own, in the order the list gave them, so a host logs them exactly
/// as it logs a run of single-pack screenshots. A card no screenshot showed is an unread slot
/// without a picture, to be named by hand.
/// </param>
/// <param name="Notes">Anything the user should know about how the pictures fitted together.</param>
public sealed record Opening(IReadOnlyList<ShotReading> Packs, IReadOnlyList<string> Notes);

/// <summary>
/// Puts the screenshots of a several-pack opening back together into packs.
///
/// The results list is longer than a screen, so it arrives as a run of screenshots that overlap:
/// one scrolled a little further than the last, or back up again. Each is a stretch of rows, and
/// the work is laying the stretches over one another.
///
/// ROWS ARE MATCHED BY THEIR CARDS, IN ORDER
/// ----------------------------------------------------------------------------------
/// Not by collecting distinct cards: one opening can hold the same card twice, in two packs, and
/// B4b's did -- Dedenne ex ended packs 7 and 8. A stretch is laid where its rows agree with rows
/// already placed, card for card, and anything past the end of what is placed is added. A stretch
/// that agrees nowhere is added after everything, and the join is marked as a place rows may be
/// missing from.
///
/// PACKS COME FROM THE HEADINGS
/// ----------------------------------------------------------------------------------
/// Every pack sits under a "Pack no. N" heading, and the reader marks a row whose gap above is a
/// heading's (<see cref="ShotRow.StartsPack"/>). That is what splits the rows, so a pack of four,
/// five or six is split alike. A heading that no screenshot shows -- above the first row of a
/// screenshot, hidden under the title bar -- is decided from the pack sizes the set allows: the
/// rows since the last known heading either finish the pack they follow or begin the next, and a
/// pack left short says how many of its cards no picture held.
/// </summary>
public static class OpeningStitcher
{
    private sealed class Row
    {
        public required ShotMatch?[] Cards { get; init; }
        public required ShotSlot?[] Unread { get; init; }
        public bool? StartsPack { get; set; }
    }

    /// <param name="shots">The readings, in the order the pictures were chosen.</param>
    /// <param name="sizesOf">
    /// How many cards a pack of this set can hold. Four for a Deluxe set; five or six for most, since
    /// a ten-pack can mix the two.
    /// </param>
    public static Opening Stitch(IReadOnlyList<ShotReading> shots, Func<string?, IReadOnlySet<int>> sizesOf)
    {
        var placed = new List<Row>();
        var notes = new List<string>();

        foreach (var shot in shots)
        {
            var rows = RowsOf(shot);
            if (rows.Count == 0) continue;

            if (placed.Count == 0)
            {
                rows[0].StartsPack = true;
                placed.AddRange(rows);
                continue;
            }

            var at = BestOffset(placed, rows);
            if (at is null)
            {
                placed.AddRange(rows);
                continue;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var pos = at.Value + i;
                if (pos >= placed.Count) { placed.Add(rows[i]); continue; }
                Merge(placed[pos], rows[i]);
            }
        }

        var set = placed.SelectMany(r => r.Cards).OfType<ShotMatch>()
            .GroupBy(m => m.Card.Set, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        var sizes = sizesOf(set);

        var packs = Split(placed, sizes);
        var short_ = packs.Sum(p => p.Missing);

        // Only said when it cost something. Pictures often meet without overlapping -- the
        // one ends at a heading and the next begins under it -- and the headings and pack sizes
        // then account for every row, so a picture that did not overlap is not news on its own.
        if (short_ > 0)
            notes.Add($"{short_} {(short_ == 1 ? "card was" : "cards were")} not in any picture. Name "
                      + $"{(short_ == 1 ? "it" : "them")} below, or take one more screenshot.");

        return new Opening(packs.Select(p => p.Reading).ToArray(), notes);
    }

    /// <summary>Rows with at least one card named: a row with none cannot be placed against anything.</summary>
    private static List<Row> RowsOf(ShotReading reading)
    {
        var rows = new List<Row>();
        foreach (var row in reading.Rows.OrderBy(r => r.Row))
        {
            var matches = reading.Matches.Where(m => m.Row == row.Row).ToArray();
            if (matches.Length == 0) continue;

            var unread = reading.UnreadSlots.Where(u => u.Row == row.Row).ToArray();
            var cols = matches.Select(m => m.Col).Concat(unread.Select(u => u.Col)).Distinct().Order().ToArray();

            rows.Add(new Row
            {
                Cards = cols.Select(c => matches.FirstOrDefault(m => m.Col == c)).ToArray(),
                Unread = cols.Select(c => unread.FirstOrDefault(u => u.Col == c)).ToArray(),
                // Kept when the row above was dropped for having nothing named: the gap to it is
                // still the gap on the screen.
                StartsPack = row.StartsPack,
            });
        }

        return rows;
    }

    /// <summary>Cards the two rows agree on, or -1 where they disagree anywhere.</summary>
    private static int Agree(Row a, Row b)
    {
        if (a.Cards.Length != b.Cards.Length) return -1;

        var same = 0;
        for (var i = 0; i < a.Cards.Length; i++)
        {
            if (a.Cards[i] is not { } x || b.Cards[i] is not { } y) continue;
            if (x.Card.OwnershipKey != y.Card.OwnershipKey) return -1;
            same++;
        }
        return same;
    }

    /// <summary>
    /// Where a stretch's first row goes among the rows already placed, or null when it agrees with
    /// none. The offset agreeing on the most cards wins, and a later one wins a tie: the pictures
    /// are usually taken scrolling down, so the stretch most often continues the last one.
    /// </summary>
    private static int? BestOffset(List<Row> placed, List<Row> rows)
    {
        int? best = null;
        var bestScore = 0;

        for (var at = 0; at < placed.Count; at++)
        {
            var score = 0;
            var fits = true;
            for (var i = 0; i < rows.Count && at + i < placed.Count; i++)
            {
                var agreed = Agree(placed[at + i], rows[i]);
                if (agreed < 0) { fits = false; break; }
                score += agreed;
            }

            if (fits && score > 0 && score >= bestScore) { best = at; bestScore = score; }
        }

        return best;
    }

    private static void Merge(Row into, Row from)
    {
        for (var i = 0; i < into.Cards.Length; i++)
        {
            if (into.Cards[i] is null && from.Cards[i] is { } card) { into.Cards[i] = card; into.Unread[i] = null; }
            else if (into.Cards[i] is null && into.Unread[i] is null) into.Unread[i] = from.Unread[i];
        }

        into.StartsPack ??= from.StartsPack;
    }

    private sealed record Built(ShotReading Reading, int Missing);

    private static List<Built> Split(List<Row> rows, IReadOnlySet<int> sizes)
    {
        var groups = new List<List<Row>>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var current = groups.LastOrDefault();

            var starts = row.StartsPack ?? (current is null || Decide(current, rows, i, sizes));
            if (current is null || starts) groups.Add([row]);
            else current.Add(row);
        }

        return groups.Select(g => Build(g, sizes)).ToList();
    }

    /// <summary>
    /// Whether a row whose heading nothing showed begins a pack. The rows from it to the next known
    /// heading are one stretch of the list: if they bring the pack before them to a size the set
    /// allows, they are the rest of it; if that pack is already whole, or they would overfill it,
    /// they are the next pack, and the one before is short of cards no picture held.
    /// </summary>
    private static bool Decide(List<Row> current, List<Row> rows, int from, IReadOnlySet<int> sizes)
    {
        var have = current.Sum(r => r.Cards.Length);
        if (sizes.Contains(have)) return true;

        var stretch = 0;
        for (var i = from; i < rows.Count; i++)
        {
            if (i > from && rows[i].StartsPack == true) break;
            stretch += rows[i].Cards.Length;
        }

        return !sizes.Contains(have + stretch);
    }

    private static Built Build(List<Row> rows, IReadOnlySet<int> sizes)
    {
        var matches = new List<ShotMatch>();
        var unread = new List<ShotSlot>();
        var col = 0;

        foreach (var row in rows)
            for (var i = 0; i < row.Cards.Length; i++, col++)
            {
                if (row.Cards[i] is { } m) matches.Add(m with { Row = 0, Col = col });
                else unread.Add(new ShotSlot(0, col, row.Unread[i]?.Thumb ?? "", true));
            }

        var target = sizes.Where(s => s >= col).DefaultIfEmpty(col).Min();
        for (var missing = col; missing < target; missing++)
            unread.Add(new ShotSlot(0, missing, "", true));

        var reading = new ShotReading(true, null, CardScreen.PackReveal, false, matches, unread, []);
        return new Built(reading, target - col);
    }
}
