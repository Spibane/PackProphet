using PackProphet.Data;

namespace PackProphet.Vision;

/// <summary>
/// What the fingerprint table can and cannot recognise, measured against the card data actually
/// loaded rather than against the card data that existed when the table was generated.
///
/// This exists because the two halves of the app age at different rates, by design. Card data is
/// fetched live from the CDN so a new set's cards show up in the collection the day the community
/// database has them, with no redeploy. Fingerprints cannot work that way: generating one means
/// downloading the art, so they arrive when a workflow next runs. Between those two moments the
/// app knows a card exists and cannot recognise it in a screenshot.
///
/// The honest response to that is to say so, with the set named and the date attached, which is
/// what this type is for. The alternative — a screenshot of a brand-new set quietly reading as
/// "no cards found" — invites the user to conclude the feature is broken.
/// </summary>
/// <param name="Generated">When the table was built, as an ISO date, or null if the header lacked one.</param>
/// <param name="Fingerprints">Entries in the table.</param>
/// <param name="Missing">Set codes present in the card data with no fingerprints at all, newest first.</param>
/// <param name="Partial">
/// Sets the table covers incompletely, as code and the number of cards it is short. A handful of
/// gaps is normal — the art CDN 404s for the occasional card — and a large gap means the table
/// predates a set's cards being finished.
/// </param>
public sealed record ArtHashCoverage(
    string? Generated,
    int Fingerprints,
    IReadOnlyList<string> Missing,
    IReadOnlyList<(string Set, int Short)> Partial)
{
    /// <summary>Below this fraction of a set's entries, the set is called out rather than passed over.</summary>
    private const double PartialFloor = 0.9;

    public bool IsComplete => Missing.Count == 0 && Partial.Count == 0;

    public static ArtHashCoverage Of(ArtHashTable table, CardIndex index, SetCatalog sets)
    {
        var missing = new List<string>();
        var partial = new List<(string, int)>();

        foreach (var (set, cards) in index.BySet)
        {
            var covered = table.Covered(set);
            if (covered == 0) { missing.Add(set); continue; }
            if (covered < cards.Count * PartialFloor) partial.Add((set, cards.Count - covered));
        }

        // Newest first, because the set a user is most likely to be photographing is the one that
        // just came out, and that is also the one most likely to be missing.
        missing.Sort((a, b) => string.CompareOrdinal(sets.SortKey(b), sets.SortKey(a)));

        return new ArtHashCoverage(table.Generated, table.Count, missing, partial);
    }

    /// <summary>
    /// The warning to show, or null when the table covers everything loaded. Written to be read by
    /// someone who does not know what a fingerprint table is: it names the sets, says what will
    /// happen, and says what fixes it.
    /// </summary>
    public string? Warning(SetCatalog sets)
    {
        if (IsComplete) return null;

        var named = Missing.Concat(Partial.Select(p => p.Set)).Take(3).Select(sets.DisplayName).ToArray();
        var total = Missing.Count + Partial.Count;
        var list = string.Join(", ", named);
        if (total > named.Length) list += $" and {total - named.Length} more";

        var age = Generated is null ? "" : $" The fingerprints in this build are from {Generated}.";

        // Said in two sentences: which sets, and what that looks like. Why a set can be browsable
        // and unrecognisable at once belongs in the docs, not on top of the file picker.
        return $"Cards from {list} cannot be recognised yet.{age} "
             + "A screenshot of one of those sets will read as empty.";
    }
}
