namespace PackProphet.Data;

using PackProphet.Domain;

/// <summary>
/// One search across every facet a deck builder actually asks about.
///
/// Name-only search is not enough to build a deck with. The questions people have are
/// "what puts something to Sleep?", "show me the Supporters", "which Basics do I own" —
/// none of which a name contains. So the text term searches printed rules text as well as
/// names, and the facets narrow by the structured fields.
///
/// Every specified facet must match (AND), because a filter row that ORed its controls would
/// widen results as you narrowed the filters.
/// </summary>
/// <param name="Text">Free text, matched against names AND printed rules text.</param>
/// <param name="Kind">"Pokémon" or "Trainer".</param>
/// <param name="Subtype">
/// An energy for a Pokémon (Grass, Fire, …) or a kind for a Trainer (Item, Supporter, Tool).
/// One field, because that is how the game presents it and how the list view shows it.
/// </param>
/// <param name="Stage">"Basic", "Stage 1", "Stage 2".</param>
/// <param name="OwnedOnly">Restrict to cards the user holds at least one copy of.</param>
/// <param name="Set">A single set code, e.g. "A1".</param>
/// <param name="RarityCodes">
/// Rarity codes to accept. A SET of codes rather than one, because the useful selections are
/// whole families — "all diamonds" is four rungs and eight codes — and because a rung like
/// 2-star already covers two codes (SR and SAR).
///
/// Set and rarity come off the card itself rather than from the printed detail, so they filter
/// correctly before card detail has finished downloading. That matters: "every diamond in A1" is
/// exactly the sort of thing someone does on their first visit.
/// </param>
public readonly record struct CardQuery(
    string Text = "",
    string? Kind = null,
    string? Subtype = null,
    string? Stage = null,
    bool OwnedOnly = false,
    string? Set = null,
    IReadOnlySet<string>? RarityCodes = null)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Text) && Kind is null && Subtype is null && Stage is null
        && Set is null && RarityCodes is null or { Count: 0 };
}

public static class CardSearch
{
    private const StringComparison Ci = StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Where a card matched, so results can be ordered by relevance. A name hit is what the
    /// user meant when they typed a name; a rules-text hit is what they meant when they typed
    /// "sleep". Ranking name hits first serves both without asking which they intended.
    /// </summary>
    public enum Hit { None = 0, RulesText = 1, NameContains = 2, NamePrefix = 3, NameExact = 4 }

    public static bool Matches(PocketCard card, CardFact? fact, CardQuery query) =>
        Rank(card, fact, query) != Hit.None;

    /// <summary>
    /// All of a card's searchable rules text as one lowercase string.
    ///
    /// Built once per card and handed back to <see cref="Rank"/> so a keystroke does not walk a
    /// card's attacks again. That walk was surprisingly expensive: <c>CardFact.AttackList</c>
    /// sorts and allocates a fresh array on every access, so scanning 3,700 cards per keystroke
    /// meant 3,700 sorts and allocations — and it only began at the third character, which is
    /// where rules text starts being searched at all.
    /// </summary>
    public static string RulesBlob(CardFact? fact)
    {
        if (fact is null) return "";

        var sb = new System.Text.StringBuilder(160);
        Append(sb, fact.CardText);
        Append(sb, fact.Ability?.Name);
        Append(sb, fact.Ability?.Effect);

        foreach (var attack in fact.AttackList)
        {
            Append(sb, attack.Name);
            Append(sb, attack.Effect);
        }

        // Flavour text is deliberately absent: prose about the creature makes "asleep" match a
        // dozen Pokémon whose rules do nothing of the kind.
        return sb.ToString();
    }

    private static void Append(System.Text.StringBuilder sb, string? text)
    {
        if (text is not { Length: > 0 }) return;

        if (sb.Length > 0) sb.Append('\n');
        sb.Append(text.ToLowerInvariant());
    }

    /// <param name="rulesBlob">
    /// Precomputed <see cref="RulesBlob"/> for this card. Optional: pass it when searching
    /// repeatedly over the same cards, omit it for a one-off.
    /// </param>
    public static Hit Rank(PocketCard card, CardFact? fact, CardQuery query, string? rulesBlob = null)
    {
        // Facets first: they are cheap field comparisons, while the text pass walks attack and
        // ability strings. Set and rarity lead because they need no printed detail at all.
        if (query.Set is { Length: > 0 } set && !card.Set.Equals(set, Ci)) return Hit.None;
        if (query.RarityCodes is { Count: > 0 } codes && !codes.Contains(card.Rarity)) return Hit.None;

        if (query.Kind is { Length: > 0 } kind && !KindMatches(fact, kind)) return Hit.None;
        if (query.Subtype is { Length: > 0 } sub && !Equals(fact?.Subtype, sub)) return Hit.None;
        if (query.Stage is { Length: > 0 } stage && !Equals(fact?.Stage, stage)) return Hit.None;

        var text = query.Text?.Trim() ?? "";
        if (text.Length == 0) return Hit.RulesText;   // facets alone matched

        if (card.Name.Equals(text, Ci)) return Hit.NameExact;
        if (card.Name.StartsWith(text, Ci)) return Hit.NamePrefix;
        if (card.Name.Contains(text, Ci)) return Hit.NameContains;

        // Rules text is searched only for terms of three characters or more. Two-letter
        // fragments match inside almost every card's text, which turns the result list into
        // noise exactly when the user has typed too little to disambiguate.
        if (text.Length < 3) return Hit.None;

        var found = rulesBlob is null
            ? RulesTextContains(fact, text)
            : rulesBlob.Contains(text, Ci);

        return found ? Hit.RulesText : Hit.None;
    }

    private static bool KindMatches(CardFact? fact, string kind)
    {
        if (fact?.Type is not { Length: > 0 } type) return false;

        // "Pokémon" arrives with and without its accent depending on the source, so match on
        // the prefix rather than the whole word.
        return kind.StartsWith("Pok", Ci)
            ? type.StartsWith("Pok", Ci)
            : type.Equals(kind, Ci);
    }

    private static bool RulesTextContains(CardFact? fact, string text)
    {
        if (fact is null) return false;

        if (Has(fact.CardText, text)) return true;
        if (Has(fact.Ability?.Name, text) || Has(fact.Ability?.Effect, text)) return true;

        foreach (var attack in fact.AttackList)
            if (Has(attack.Name, text) || Has(attack.Effect, text)) return true;

        // Flavour text is deliberately NOT searched: it is prose about the creature, so "sleep"
        // matches a dozen Pokémon whose rules do nothing of the kind.
        return false;
    }

    private static bool Has(string? haystack, string needle) =>
        haystack is { Length: > 0 } && haystack.Contains(needle, Ci);

    private static bool Equals(string? value, string other) =>
        value is { Length: > 0 } && value.Equals(other, Ci);
}
