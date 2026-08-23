namespace PackProphet.Domain;

/// <summary>
/// What the user owns, as copy counts keyed by <see cref="PocketCard.OwnershipKey"/>.
/// Zero-count entries are omitted, so absence means "none".
///
/// Keyed by ownable card, NOT by set entry: the game treats owning a card as owning it for
/// every set it appears in, so a Deluxe reprint is already owned if you have the original.
/// </summary>
public sealed class Collection
{
    private readonly Dictionary<string, int> _counts;

    public Collection(IDictionary<string, int>? counts = null) =>
        _counts = counts is null ? new() : new(counts.Where(kv => kv.Value > 0));

    public int this[string ownershipKey] => _counts.TryGetValue(ownershipKey, out var n) ? n : 0;

    public int Of(PocketCard card) => this[card.OwnershipKey];

    public int DistinctOwned => _counts.Count;

    public IReadOnlyDictionary<string, int> Raw => _counts;

    public Collection With(string ownershipKey, int count)
    {
        var next = new Dictionary<string, int>(_counts);
        if (count > 0) next[ownershipKey] = count; else next.Remove(ownershipKey);
        return new Collection(next);
    }

    public Collection With(PocketCard card, int count) => With(card.OwnershipKey, count);

    /// <summary>
    /// Copies held across a card identity. Deck demand is satisfied by this total, since any
    /// printing fills a deck slot and two copies of the same printing work as well as two
    /// different ones. Printings are de-duplicated by ownership key first, so a card re-listed
    /// in several sets is not counted twice.
    /// </summary>
    public int OfIdentity(IEnumerable<PocketCard> printings) =>
        printings.DistinctBy(p => p.OwnershipKey).Sum(Of);
}
