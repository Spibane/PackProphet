namespace PackProphet.Domain;

/// <summary>
/// One entry from the upstream rarities.json. Shipped verbatim: its economics
/// (points, tradePrice, tradeable) were checked against the live game and are correct,
/// so these values are READ, never hardcoded. Contrast cards.extra.json, whose stats
/// are wrong and which this app does not use.
/// </summary>
public sealed class Rarity
{
    /// <summary>Human label, e.g. "Double Rare".</summary>
    public string Label { get; set; } = "";

    public string Image { get; set; } = "";

    /// <summary>How many symbols are shown, e.g. 4 for a 4-diamond card.</summary>
    public int Count { get; set; }

    /// <summary>Symbol family: Diamond, Star, Shiny or Crown.</summary>
    public string Group { get; set; } = "";

    public bool Tradeable { get; set; }

    /// <summary>Shinedust cost to obtain by trade; null when untradeable.</summary>
    public int? TradePrice { get; set; }

    /// <summary>Pack Points cost to buy outright. Spendable only within the card's own set.</summary>
    public int Points { get; set; }
}
