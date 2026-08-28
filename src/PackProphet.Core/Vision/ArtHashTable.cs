using PackProphet.Data;
using PackProphet.Domain;

namespace PackProphet.Vision;

/// <summary>One art fingerprint and the set entry it was generated from.</summary>
/// <param name="Set">Set code as it appears in the card data, e.g. "A1".</param>
/// <param name="Number">Position in that set's numbered list.</param>
public readonly record struct ArtHashEntry(string Set, int Number, ArtHash Hash)
{
    public string Key => $"{Set}-{Number}";
}

/// <summary>
/// The fingerprints of every card art the app can recognise, as generated offline by
/// tools/CardHashGen and committed to wwwroot. Recognition is a nearest-neighbour search over
/// this, which is the whole reason a screenshot can be read on the device with no model, no
/// service and no upload.
///
/// Keyed by set and card number rather than by <see cref="PocketCard.OwnershipKey"/> even though
/// ownership is what the app records. Two reasons. The table is a third of the size, because a
/// number is shorter than an artwork filename and there are 3,761 of them. And the mapping from
/// set entry to ownable card is then made by the card data loaded at runtime rather than frozen
/// into the table at generation time: the 215 re-listings share their art, so they share a hash,
/// and resolving through <see cref="CardIndex.ByKey"/> collapses them onto one ownership key
/// without the generator having to know that rule.
///
/// A table generated before a set existed simply does not contain it. That is a coverage gap the
/// UI is required to state rather than a failure — see <see cref="ArtHashCoverage"/> — because the
/// app prefers live CDN card data, so a new set's cards appear in the collection days or weeks
/// before a workflow run adds their fingerprints here.
/// </summary>
public sealed class ArtHashTable
{
    /// <summary>
    /// How far apart two 128-bit hashes may be and still be called the same art.
    ///
    /// Measured, not guessed, though not yet against real screenshots — those are the calibration
    /// this still wants. Two things were measured. Re-framing and rescaling the same art moves its
    /// fingerprint by 0 to 1 bit, and by no more than 4 in the worst combination tested
    /// (ArtSamplerTests). And across the 3,546 distinct fingerprints in the committed table, the
    /// distance from a card to the nearest <em>different</em> card is 23 bits at the median, 13 at
    /// the 5th percentile, and 3 at the closest pair.
    ///
    /// 18 sits between those. Real screenshots, measured on the two three-across fixtures, land
    /// between 1 and 16 bits from their entries — the far end being a holo card — so the threshold
    /// has to clear 16 with a little room. It was 14 while only one fixture was available, and 22
    /// before that, which was the median separation itself: a threshold that wide will happily match
    /// a card that is not in the table at all, and that is the failure that matters most here,
    /// because a set released since the last workflow run is precisely a screenful of cards that are
    /// not in the table. <see cref="AmbiguityMargin"/> is what makes 18 safe rather than merely
    /// generous.
    /// </summary>
    public const int MaxDistance = 18;

    /// <summary>
    /// How much closer the best match must be than the runner-up before it is trusted.
    ///
    /// The guard against the other failure. Card art is full of near duplicates — the same
    /// illustration at two rarities, a full art beside a plain printing — so the risk is not
    /// "nothing is within range", it is "two things are, and the closest is closest by one bit".
    /// A tie is reported as unread rather than guessed at, because a wrong card silently added to a
    /// collection is worse than a slot the user has to fill in.
    ///
    /// The cost is known and accepted: 2 of the 3,546 distinct fingerprints have a different card
    /// within 6 bits, and about 1% have one within 9 — so the very tightest pairs, mostly full arts
    /// of a card that also has a plain printing, come back unread however good the screenshot is.
    /// A percent of coverage for not inventing ownership is the right way round.
    /// </summary>
    public const int AmbiguityMargin = 6;

    /// <summary>
    /// Distance up to which a match is simply a match, and beyond which it is worth telling the user
    /// the reading was a close-run thing.
    ///
    /// Measured on real screenshots: the cards on the two three-across fixtures land between 1 and
    /// 17 bits from the table, most of them under 12. So 12 is the top of the normal range, and 13
    /// to <see cref="MaxDistance"/> is the band where something about the picture was unusual — a
    /// holo treatment, a compression artefact, a slightly misplaced crop.
    ///
    /// Presentation only; nothing is accepted or rejected on this. It exists because the first
    /// version of the page hedged at three quarters of <see cref="MaxDistance"/>, which after the
    /// threshold came down to 14 meant a perfectly ordinary 4-bit match was labelled "less
    /// certainly" — hedging on almost every row, which teaches the user to ignore the hedge.
    /// </summary>
    public const int ComfortableDistance = 12;

    private readonly ArtHashEntry[] _entries;
    private readonly Dictionary<string, int> _countBySet;

    /// <summary>Every fingerprint, in the order given. For the generator, which merges tables.</summary>
    public IReadOnlyList<ArtHashEntry> Entries => _entries;

    /// <summary>Set codes the table has at least one fingerprint for.</summary>
    public IReadOnlySet<string> Sets { get; }

    /// <summary>Fingerprint count. Below the card count when art was missing from the CDN at generation time.</summary>
    public int Count => _entries.Length;

    /// <summary>
    /// The generator's stamp, as written into the table's header — an ISO date. Shown to the user
    /// so "your newest set is not recognised yet" comes with a date attached rather than being
    /// an unfalsifiable apology.
    /// </summary>
    public string? Generated { get; }

    public static ArtHashTable Empty { get; } = new([], null);

    public ArtHashTable(IEnumerable<ArtHashEntry> entries, string? generated)
    {
        _entries = entries.ToArray();
        Generated = generated;
        Sets = _entries.Select(e => e.Set).ToHashSet(StringComparer.OrdinalIgnoreCase);

        _countBySet = _entries
            .GroupBy(e => e.Set, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Number).Distinct().Count(),
                          StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>How many of a set's cards have a fingerprint. Zero for a set generated before it existed.</summary>
    public int Covered(string set) => _countBySet.GetValueOrDefault(set);

    /// <summary>
    /// The nearest entry to <paramref name="hash"/>, or null when nothing is close enough or the
    /// two closest are too alike to choose between. <paramref name="distance"/> carries the
    /// winning distance so callers can rank and display confidence.
    /// </summary>
    /// <summary>
    /// Every entry within <paramref name="within"/> bits of <paramref name="hash"/>, nearest first.
    ///
    /// Ranked candidates rather than an answer, because deciding between them needs something this
    /// class deliberately does not have: which ownable card each entry belongs to. Two entries can be
    /// a hair apart and be the same card — A4b reprints 214 earlier cards — or a hair apart and be
    /// two different cards, which is what a foil printing and its plain twin are. Only the card data
    /// knows which, so <see cref="ScreenshotReader"/> makes the call.
    ///
    /// That split was not the original design. This class used to return a single match and treat
    /// any rival carrying the winner's exact fingerprint as harmless, on the grounds that reprints
    /// share their artwork. Once only the art window was fingerprinted, that reasoning broke: the
    /// window excludes the card's frame, and a foil printing differs from its plain twin in nothing
    /// but the frame — so a foil sits at distance 0 from a different ownable card and was being
    /// waved through as "the same card". It would have returned the wrong printing silently.
    /// </summary>
    public IReadOnlyList<(ArtHashEntry Entry, int Distance)> Nearest(
        ArtHash hash, int within = MaxDistance, int limit = 12)
    {
        if (_entries.Length == 0 || hash.IsFeatureless) return [];

        var found = new List<(ArtHashEntry Entry, int Distance)>();
        foreach (var entry in _entries)
        {
            var d = hash.DistanceTo(entry.Hash);
            if (d <= within) found.Add((entry, d));
        }

        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return found.Count > limit ? found.GetRange(0, limit) : found;
    }

    /// <summary>
    /// Reads the committed table. Lines are "SET NUMBER HASH"; "#" starts a comment, and a
    /// "# generated YYYY-MM-DD" comment supplies <see cref="Generated"/>.
    ///
    /// A malformed line is skipped rather than thrown on. The file is generated, so a bad line
    /// means the generator or the upstream art changed shape — and losing one card's
    /// recognisability is a far better outcome than an exception on a code path the user reached
    /// by picking a screenshot.
    /// </summary>
    public static ArtHashTable Parse(string text)
    {
        var entries = new List<ArtHashEntry>();
        string? generated = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.AsSpan().Trim();
            if (line.IsEmpty) continue;

            if (line[0] == '#')
            {
                const string stamp = "# generated ";
                if (line.StartsWith(stamp)) generated = line[stamp.Length..].Trim().ToString();
                continue;
            }

            var first = line.IndexOf(' ');
            if (first <= 0) continue;
            var rest = line[(first + 1)..].TrimStart();
            var second = rest.IndexOf(' ');
            if (second <= 0) continue;

            if (!int.TryParse(rest[..second], out var number)) continue;
            if (!ArtHash.TryParse(rest[(second + 1)..].Trim(), out var hash)) continue;

            entries.Add(new ArtHashEntry(line[..first].ToString(), number, hash));
        }

        return new ArtHashTable(entries, generated);
    }

    /// <summary>The table as its file format, generator side. Sorted so a regeneration diffs cleanly.</summary>
    public string Serialize(string generated)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("# PackProphet card art fingerprints\n");
        sb.Append("# generated ").Append(generated).Append('\n');
        sb.Append("# set number 128-bit-dhash · see src/PackProphet.Core/Vision/ArtHash.cs\n");
        foreach (var e in _entries.OrderBy(e => e.Set, StringComparer.Ordinal).ThenBy(e => e.Number))
            sb.Append(e.Set).Append(' ').Append(e.Number).Append(' ').Append(e.Hash).Append('\n');
        return sb.ToString();
    }
}
