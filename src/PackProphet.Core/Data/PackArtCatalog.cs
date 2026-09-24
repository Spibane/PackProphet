namespace PackProphet.Data;

using PackProphet.Domain;

/// <summary>One entry from the expansions index: a set and the packs it sold.</summary>
public sealed class ExpansionInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ExpansionPack>? Packs { get; set; }
}

public sealed class ExpansionPack
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// Higher-resolution booster art, mapped from the expansions index published alongside the card
/// data.
///
/// The other source's booster images are 160x256, which upscales visibly at any reasonable tile
/// size; these are 334x644. The two projects name packs differently — for single-pack sets one
/// says "Paradox Drive" and the other just "Booster" — so URLs are matched rather than
/// constructed: by name where names agree, and by position where a set has exactly one pack in
/// both.
/// </summary>
public sealed class PackArtCatalog
{
    private const string Cdn =
        "https://cdn.jsdelivr.net/gh/chase-mew/pokemon-tcg-pocket-cards@main/images/webp/packs";

    // "A1:Mewtwo" -> their pack id
    private readonly Dictionary<string, string> _packIds = new(StringComparer.OrdinalIgnoreCase);

    // Pack names more than one set uses, "Deluxe" for A4b and B4b.
    private readonly HashSet<string> _sharedNames = new(StringComparer.OrdinalIgnoreCase);

    public PackArtCatalog(
        IEnumerable<ExpansionInfo>? expansions,
        IEnumerable<string> ourPackKeys)
    {
        var ours = ourPackKeys
            .Select(k => k.Split(':', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(p => p[1]).ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var name in ours.SelectMany(kv => kv.Value.Distinct(StringComparer.OrdinalIgnoreCase))
                     .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
            _sharedNames.Add(name.Key);

        foreach (var exp in expansions ?? [])
        {
            var theirs = exp.Packs ?? [];
            if (theirs.Count == 0) continue;

            var setCode = MatchSetCode(exp.Id, ours.Keys);
            if (setCode is null || !ours.TryGetValue(setCode, out var ourNames)) continue;

            foreach (var ourName in ourNames)
            {
                var match = theirs.FirstOrDefault(t => Slug(t.Name) == Slug(ourName));

                // Names disagree for single-pack sets — "Paradox Drive" against "Booster" —
                // but if each side lists exactly one pack for the set, the pairing is
                // unambiguous regardless of what it is called.
                if (match is null && theirs.Count == 1 && ourNames.Length == 1)
                    match = theirs[0];

                if (match is not null) _packIds[$"{setCode}:{ourName}"] = match.Id;
            }
        }
    }

    public static PackArtCatalog Empty { get; } = new(null, []);

    public int Count => _packIds.Count;

    /// <summary>Higher-resolution art for a pack key, or null when it is not published.</summary>
    public string? UrlFor(string packKey) =>
        _packIds.TryGetValue(packKey, out var id) ? $"{Cdn}/{id}.webp" : null;

    public string? UrlFor(string setCode, string packName) => UrlFor($"{setCode}:{packName}");

    /// <summary>
    /// Booster art for a pack key: the higher-resolution image where it is published, otherwise
    /// the lower-resolution one, which is named by pack alone. Empty — the drawn placeholder —
    /// where neither can be trusted.
    ///
    /// The fallback is skipped for a name more than one set uses. "Deluxe.webp" is one file for
    /// both A4b and B4b, so it shows one set's booster on the other's tiles, and the wrong booster
    /// is worse than a placeholder. Once the expansions index lists the new set, its tile gets
    /// its own art from <see cref="UrlFor(string)"/>.
    /// </summary>
    public string Url(string packKey)
    {
        if (UrlFor(packKey) is { } better) return better;
        return packKey.Split(':', 2) is [_, var name] && !_sharedNames.Contains(name)
            ? ArtSource.PackArt(name)
            : "";
    }

    /// <summary>Their set ids are lower-case and promos are "pa"/"pb" where ours are "PROMO-A".</summary>
    private static string? MatchSetCode(string theirId, IEnumerable<string> ourSets)
    {
        var candidates = theirId.ToUpperInvariant() switch
        {
            "PA" => new[] { "PROMO-A" },
            "PB" => new[] { "PROMO-B" },
            var other => [other]
        };

        foreach (var candidate in candidates)
            foreach (var ourSet in ourSets)
                if (string.Equals(ourSet, candidate, StringComparison.OrdinalIgnoreCase))
                    return ourSet;

        return null;
    }

    /// <summary>Compare names ignoring case, spaces and punctuation: "Ho-Oh" matches "hooh".</summary>
    private static string Slug(string? name) =>
        new((name ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
