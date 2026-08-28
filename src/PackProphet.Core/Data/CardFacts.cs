namespace PackProphet.Data;

using System.Text.Json.Serialization;
using PackProphet.Domain;

/// <summary>One attack: its energy cost, damage and effect text.</summary>
public sealed class CardAttack
{
    public string? Name { get; set; }

    /// <summary>
    /// Cost as one letter per energy: G R W L P F D M for the types, C for Colorless, and
    /// "0" for a free attack. Expand with <see cref="CostSymbols"/> rather than showing raw.
    /// </summary>
    public string? Cost { get; set; }

    public int? Damage { get; set; }
    public string? Effect { get; set; }

    /// <summary>
    /// Damage as the card actually prints it: "50×", "30+" or "60". Empty when the attack does no
    /// fixed damage.
    ///
    /// The data stores only the base number, so the modifier is read from the effect text:
    ///
    ///   "50×"  the damage is multiplied ("does 50 damage for each heads")
    ///   "30+"  a conditional bonus is added ("if heads, this attack does 30 more damage")
    ///
    /// A bare "50" for Pinsir's Double Horn understates it by up to 100.
    ///
    /// The patterns are anchored tightly. "Heal 30 damage" and "does 20 damage to itself" both
    /// mention damage without modifying this attack's, and get no suffix. Across the full dataset,
    /// 167 attacks are multipliers and 484 are bonuses with no overlap, and the remaining 1,038
    /// damage-mentioning effects are heals and self-damage that get nothing.
    /// </summary>
    public string DamageLabel
    {
        get
        {
            if (Damage is not int damage) return "";

            return Modifier(Effect ?? "") switch
            {
                DamageModifier.Multiplier => $"{damage}×",
                DamageModifier.Bonus => $"{damage}+",
                _ => damage.ToString()
            };
        }
    }

    private enum DamageModifier { None, Multiplier, Bonus }

    /// <summary>
    /// Which modifier, if either, the effect text applies to this attack's damage. Both phrases
    /// open "does &lt;number&gt;" and then diverge:
    ///
    ///   "does 50 damage for each heads"   a multiplier
    ///   "does 30 more damage"             a bonus
    ///
    /// "for each", not "to each": "does 30 damage to each of your opponent's Benched Pokémon"
    /// hits every Bench member for a fixed 30 and is printed as a plain number.
    ///
    /// Was a pair of regular expressions. Scanning by hand keeps the app from shipping
    /// System.Text.RegularExpressions, which is 114 KB gzipped for these two phrases and three
    /// filename shapes. The whole text is searched for a multiplier before any bonus is accepted,
    /// which is what the two patterns did when they ran in order.
    /// </summary>
    private static DamageModifier Modifier(string effect)
    {
        var found = DamageModifier.None;

        for (var i = 0; i < effect.Length; i++)
        {
            if (!effect.AsSpan(i).StartsWith("does", StringComparison.OrdinalIgnoreCase)) continue;

            var j = i + 4;
            if (!SkipRun(effect, ref j, char.IsWhiteSpace)) continue;
            if (!SkipRun(effect, ref j, char.IsAsciiDigit)) continue;
            if (!SkipRun(effect, ref j, char.IsWhiteSpace)) continue;

            var rest = effect.AsSpan(j);

            if (rest.StartsWith("damage", StringComparison.OrdinalIgnoreCase))
            {
                var k = j + 6;
                if (SkipRun(effect, ref k, char.IsWhiteSpace)
                    && effect.AsSpan(k).StartsWith("for each", StringComparison.OrdinalIgnoreCase))
                    return DamageModifier.Multiplier;
            }

            if (rest.StartsWith("more damage", StringComparison.OrdinalIgnoreCase))
                found = DamageModifier.Bonus;
        }

        return found;
    }

    /// <summary>Consumes one or more matching characters, reporting whether there were any.</summary>
    private static bool SkipRun(string text, ref int at, Func<char, bool> take)
    {
        var start = at;
        while (at < text.Length && take(text[at])) at++;
        return at > start;
    }

    /// <summary>Cost as readable energy names, e.g. "GCC" to Grass, Colorless, Colorless.</summary>
    public IReadOnlyList<string> CostSymbols =>
        (Cost ?? "").Where(c => c != '0').Select(EnergyName).ToArray();

    public static string EnergyName(char letter) => letter switch
    {
        'G' => "Grass",
        'R' => "Fire",
        'W' => "Water",
        'L' => "Lightning",
        'P' => "Psychic",
        'F' => "Fighting",
        'D' => "Darkness",
        'M' => "Metal",
        'C' => "Colorless",
        _ => letter.ToString()
    };
}

public sealed class CardAbility
{
    public bool Exists { get; set; }
    public string? Name { get; set; }
    public string? Effect { get; set; }
}

/// <summary>
/// A card's printed detail, from chase-mew/pokemon-tcg-pocket-cards v5 (AGPL-3.0-or-later).
///
/// This replaced flibustier's cards.extra.json, which was wrong on stats (it reported Venusaur ex
/// as 50 HP / retreat 1 against the real 190 / 3) and stale — it stopped before B2, leaving eight
/// sets with no evolution stage and the "at least one Basic Pokémon" legality rule uncheckable for
/// them. This source is correct on those stats and covers every set, which is why the project
/// takes on its copyleft licence.
/// </summary>
public sealed class CardFact
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>"Pokémon" or "Trainer".</summary>
    public string? Type { get; set; }

    /// <summary>
    /// For a Pokémon its energy (Grass, Fire, … Dragon, Colorless); for a Trainer its kind (Item,
    /// Supporter, Tool, Stadium). Shown as one column in the UI.
    /// </summary>
    public string? Subtype { get; set; }

    /// <summary>"Basic", "Stage 1" or "Stage 2". Null for Trainers.</summary>
    public string? Stage { get; set; }

    [JsonPropertyName("evolves_from")] public string? EvolvesFrom { get; set; }

    public int? Health { get; set; }
    public int? Retreat { get; set; }
    public string? Weakness { get; set; }

    public CardAbility? Ability { get; set; }

    /// <summary>Keyed "1" and "2"; a card has at most two attacks.</summary>
    public Dictionary<string, CardAttack>? Attacks { get; set; }

    [JsonPropertyName("card_text")] public string? CardText { get; set; }
    [JsonPropertyName("flavour_text")] public string? FlavourText { get; set; }
    public string? Artist { get; set; }

    /// <summary>
    /// Pack-point cost of this printing. Per-printing, unlike the rest of this record: an art rare
    /// and its base card share attacks and stage but not price. Only meaningful when looked up by
    /// card key, never by identity — see <see cref="CardFacts.ForPrinting"/>.
    /// </summary>
    [JsonPropertyName("pack_points")] public int? PackPoints { get; set; }

    [JsonPropertyName("set_code")] public string? SetCode { get; set; }

    /// <summary>
    /// This printing as a "A1-227" style key, matching PocketCard.Key. Their promo sets are
    /// coded "pa"/"pb" where ours are "PROMO-A"/"PROMO-B".
    /// </summary>
    public string? CardKey
    {
        get
        {
            if (SetCode is not { Length: > 0 } code) return null;

            var set = code.ToUpperInvariant() switch
            {
                "PA" => "PROMO-A",
                "PB" => "PROMO-B",
                var other => other
            };

            var dash = Id.LastIndexOf('-');
            if (dash < 0 || !int.TryParse(Id[(dash + 1)..], out var number)) return null;

            return $"{set}-{number}";
        }
    }

    /// <summary>
    /// Card identity, published directly here. It matches this project's own derivation from
    /// artwork filenames on all 3,558 joinable cards with zero mismatches.
    /// </summary>
    public int? DeckBuilderNr { get; set; }

    public bool IsPokemon => Type?.StartsWith("Pok", StringComparison.OrdinalIgnoreCase) == true;

    public bool IsBasic => Stage?.Equals("Basic", StringComparison.OrdinalIgnoreCase) == true;

    public bool HasAbility => Ability is { Exists: true, Name: not null };

    public IReadOnlyList<CardAttack> AttackList =>
        Attacks is null ? [] : Attacks.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();
}

/// <summary>
/// Card detail keyed by identity, so an alternate art inherits the base card's printed text.
/// Different arts of one card share attacks, stage and stats.
/// </summary>
public sealed class CardFacts
{
    private readonly Dictionary<int, CardFact> _byIdentity;
    private readonly Dictionary<string, CardFact> _byPrinting;

    public CardFacts(IEnumerable<CardFact>? facts)
    {
        _byIdentity = new();
        _byPrinting = new(StringComparer.OrdinalIgnoreCase);

        foreach (var f in facts ?? [])
        {
            // Two indexes, because the record mixes two kinds of fact. Attacks, stage, element and
            // stats belong to the card and are shared by all its arts. Pack points belong to the
            // printing — an art rare costs 400 where its base card costs 35 — so reading those off
            // an identity lookup returns the base card's price.
            if (f.DeckBuilderNr is int nr && nr > 0) _byIdentity.TryAdd(nr, f);
            if (f.CardKey is { Length: > 0 } key) _byPrinting.TryAdd(key, f);
        }
    }

    /// <summary>Detail for one specific printing, for anything that varies between arts.</summary>
    public CardFact? ForPrinting(string cardKey) =>
        _byPrinting.TryGetValue(cardKey, out var f) ? f : null;

    public CardFact? ForPrinting(PocketCard card) => ForPrinting(card.Key);

    public static CardFacts Empty { get; } = new(null);

    public int Count => _byIdentity.Count;

    public CardFact? For(int deckBuilderNr) =>
        _byIdentity.TryGetValue(deckBuilderNr, out var f) ? f : null;

    public bool Knows(int deckBuilderNr) => _byIdentity.ContainsKey(deckBuilderNr);

    /// <summary>
    /// Every known card, one per identity. Used to build search facets from the data rather than
    /// from a hardcoded list, so a new trainer kind or element appears on its own.
    /// </summary>
    public IEnumerable<CardFact> All => _byIdentity.Values;

    /// <summary>
    /// Identities with no detail, so callers can disclose a gap rather than imply a fact. Expected
    /// to be empty today; the mechanism stays because coverage is an upstream property and a new
    /// set can land in the card list before its detail does.
    /// </summary>
    public IReadOnlyList<int> Unknown(IEnumerable<int> deckBuilderNrs) =>
        deckBuilderNrs.Distinct().Where(nr => !Knows(nr)).ToArray();
}
