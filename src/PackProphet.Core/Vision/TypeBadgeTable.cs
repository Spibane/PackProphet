namespace PackProphet.Vision;

/// <summary>
/// The types read off every card's badge, as generated offline by tools/CardHashGen and committed
/// to wwwroot beside the art fingerprints.
///
/// Why a committed table and not a read at runtime
/// ==============================================================================================
/// The browser already has the art on screen, so it could sample the badge itself and skip this
/// file entirely. Two things decided against it.
///
/// A new set needs a deploy regardless: its art fingerprints have to be generated for screenshot
/// import, and art the community CDN has not published yet is vendored into the published output.
/// So the deploy this table waits for is a deploy that was happening anyway, and the freshness a
/// runtime read would buy is freshness in the one dimension that was not the bottleneck.
///
/// And generating it offline means it can be checked against the truth. The card detail table
/// already carries the type for every card released so far, so a generated table can be compared
/// against 3,761 known answers rather than against the 140 the reader was calibrated on. That is a
/// far stronger claim than any test over a handful of fixtures, and a runtime read cannot make it.
///
/// The cost is that a set is only covered once a workflow run has seen its art, which is the same
/// bargain <see cref="ArtHashTable"/> already makes for the same reason.
///
/// Only consulted where the card detail is missing. Detail carries the type itself, and it is the
/// authority: this is the stand-in for the window between a set appearing in the card data and its
/// detail being published upstream.
/// </summary>
public sealed class TypeBadgeTable
{
    private readonly Dictionary<string, IReadOnlyList<string>> _byKey;

    /// <summary>When the table was generated, for the UI to be honest about staleness.</summary>
    public string? Generated { get; }

    public int Count => _byKey.Count;

    public TypeBadgeTable(
        IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> entries, string? generated)
    {
        _byKey = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, types) in entries)
            if (key is { Length: > 0 } && types.Count > 0)
                _byKey[key] = types;

        Generated = generated;
    }

    public static TypeBadgeTable Empty { get; } = new([], null);

    /// <summary>
    /// The types for a card, or empty if this table has never seen it — which is the ordinary
    /// answer for a set released since the last workflow run, and is a gap the caller reports
    /// rather than a failure.
    /// </summary>
    public IReadOnlyList<string> For(string cardKey) =>
        _byKey.TryGetValue(cardKey, out var types) ? types : [];

    /// <summary>Sets this table covers at all, for a coverage report.</summary>
    public IReadOnlyCollection<string> Sets =>
        _byKey.Keys.Select(SetOf).Where(s => s.Length > 0)
              .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string SetOf(string key)
    {
        var dash = key.LastIndexOf('-');
        return dash <= 0 ? "" : key[..dash];
    }

    /// <summary>
    /// Reads the committed format: a comment header, then one card per line as set, number and
    /// type. A dual-typed Pokémon carries both, slash-separated, in printed order.
    ///
    /// Tolerant by design and for the same reason the art table is: a line this cannot read is one
    /// card that falls back to a blank column, never a table that fails to load and takes the
    /// other three thousand with it.
    /// </summary>
    public static TypeBadgeTable Parse(string text)
    {
        var entries = new List<KeyValuePair<string, IReadOnlyList<string>>>();
        string? generated = null;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            if (trimmed[0] == '#')
            {
                const string marker = "# generated ";
                if (trimmed.StartsWith(marker, StringComparison.Ordinal))
                    generated = trimmed[marker.Length..].Trim();
                continue;
            }

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;
            if (!int.TryParse(parts[1], out var number)) continue;

            var types = parts[2].Split('/', StringSplitOptions.RemoveEmptyEntries
                                            | StringSplitOptions.TrimEntries);
            if (types.Length == 0) continue;

            entries.Add(new($"{parts[0]}-{number}", types));
        }

        return new TypeBadgeTable(entries, generated);
    }

    /// <summary>
    /// Writes the format <see cref="Parse"/> reads. Sorted by set and then number so a regenerated
    /// table diffs as the cards that changed rather than as a reordering of all of them.
    /// </summary>
    public string Serialize(string generated)
    {
        var lines = new List<string>
        {
            "# PackProphet card type badges",
            $"# generated {generated}",
            "# set number type[/type] · see src/PackProphet.Core/Vision/TypeBadge.cs",
        };

        foreach (var (key, types) in _byKey
                     .OrderBy(kv => SetOf(kv.Key), StringComparer.Ordinal)
                     .ThenBy(kv => NumberOf(kv.Key)))
        {
            lines.Add($"{SetOf(key)} {NumberOf(key)} {string.Join('/', types)}");
        }

        return string.Join('\n', lines) + "\n";
    }

    private static int NumberOf(string key)
    {
        var dash = key.LastIndexOf('-');
        return dash > 0 && int.TryParse(key[(dash + 1)..], out var n) ? n : 0;
    }
}
