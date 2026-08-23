using System.Text.Json.Serialization;

namespace PackProphet.Domain;

/// <summary>
/// One pack variant from pullRates.json, e.g. "Regular Pack", "Rare Pack",
/// "Regular Pack +1", "Themed Rare Pack".
///
/// Do not assume the shape of this data. Observed in the live snapshot:
///   - a set has 2, 3 or 4 variants, not always just Regular + Rare
///   - <see cref="Cards"/> is 4, 5 OR 6 depending on the variant
///   - slot KEYS are not consistently 1-based: "Themed Rare Pack" numbers its slots
///     0..4 while every other variant uses 1..n. Never index slots by position —
///     always enumerate <see cref="Slots"/>.
///   - <see cref="AppearanceRate"/> across a set's variants sums to 99.999 for some
///     sets, not exactly 100, so it must be normalised rather than trusted.
/// </summary>
public sealed class PackVariant
{
    /// <summary>
    /// Percentage chance this variant is the one you open. Normalise before use.
    ///
    /// The explicit name is REQUIRED: the JSON key is snake_case, which the web-default
    /// camelCase policy cannot map even case-insensitively, because of the underscore.
    /// Without this every rate parses as 0 and the odds engine silently computes nothing.
    /// </summary>
    [JsonPropertyName("appearance_rate")]
    public double AppearanceRate { get; set; }

    /// <summary>Cards in this variant. Informational — the authority is Slots.Count.</summary>
    public int Cards { get; set; }

    /// <summary>Slot key to (rarity code -> percentage). Keys are opaque; do not order by them.</summary>
    public Dictionary<string, Dictionary<string, double>> Slots { get; set; } = new();
}
