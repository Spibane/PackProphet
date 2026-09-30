namespace PackProphet.Data;

/// <summary>A set the app has only part of something for — art, or card detail.</summary>
/// <param name="Have">Cards it has the thing for.</param>
/// <param name="Of">Cards in the set.</param>
public sealed record SetShortfall(string Set, int Have, int Of)
{
    public int Short => Of - Have;

    /// <summary>Nothing at all, as opposed to some. A brand-new set is almost always this.</summary>
    public bool Nothing => Have == 0;
}

/// <summary>
/// The notices the app can work out for itself, from what it already loaded.
///
/// WHAT BELONGS HERE, AND WHAT DOES NOT
/// ==================================================================================
/// Only things no setting can conjure. The app's own data ages at three different rates by
/// design — card data is fetched live, card art is a manual commit in a second repository, and
/// card detail is a separate 4.4 MB table topped up per set — so for days after a release the app
/// knows a card exists, cannot draw it, and cannot say what it does. That reads as a broken app
/// rather than a waiting one, and saying so is what this is for.
///
/// Pull rates deliberately do NOT belong here, although they lag in exactly the same way. An
/// unpriced set is already solvable inside the app: `/packs` offers to borrow the newest measured
/// set's distributions, and every figure in the app follows immediately. A bar for something the
/// user can already fix is a bar about a setting, and this one is about data nobody has published.
/// `AppSession.SetsAwaitingRates` still exists and is still read by `/packs` and `/settings`; it is
/// only this bar that stopped asking.
/// </summary>
public static class DataLag
{
    /// <summary>Sets named before the list turns into a count.</summary>
    private const int Named = 3;

    /// <summary>
    /// Below this share of a set, the set is called out rather than passed over.
    ///
    /// Measured rather than chosen: every numbered set in the vendored snapshot is either fully
    /// described or not described at all — B4b had 0 of 429 the day it released. So in practice
    /// the reading is 0% or 100% and any threshold between them behaves the same. The tenth of
    /// slack is for the case measurement cannot rule out: upstream publishing a set a few cards
    /// short.
    ///
    /// Not applied to a promo set, which is never published whole. It grows a few cards at a
    /// time, so a tenth of Promo B is ten promos, and nine arrived on 2026-09-30 without detail
    /// while the set read as complete. There any missing card is the news.
    ///
    /// The same number as <see cref="PackProphet.Vision.ArtHashCoverage"/>'s floor, and for the
    /// same reason — a handful of gaps is normal, a set's worth is not.
    /// </summary>
    public const double CompleteEnough = 0.9;

    /// <summary>
    /// Sets whose attack and ability detail has not arrived.
    ///
    /// Computed against the card data actually loaded rather than against a list of sets: the card
    /// list comes live from a CDN and is therefore ahead of anything vendored, which is the whole
    /// condition being reported.
    ///
    /// Cheap enough to run behind the memo in SiteNotices — one dictionary hit per card, over about
    /// four thousand cards — and it must not run before the detail table has loaded at all, or
    /// every set reads as missing for the second it takes to arrive. The caller owns that gate.
    /// </summary>
    public static IReadOnlyList<SetShortfall> DetailShortfalls(CardIndex index, CardFacts facts)
    {
        var short_ = new List<SetShortfall>();

        foreach (var (set, cards) in index.BySet)
        {
            if (cards.Count == 0) continue;

            var have = cards.Count(c => facts.ForPrinting(c) is not null);
            if (IsShort(set, have, cards.Count)) short_.Add(new SetShortfall(set, have, cards.Count));
        }

        return short_;
    }

    /// <summary>
    /// Sets this deployment knows it cannot draw, from the manifest the deploy workflow writes.
    ///
    /// Knowing this at all takes the workflow's help, and it is worth being precise about why. The
    /// app cannot discover a missing image without requesting it, and probing 3,879 of them to
    /// decide whether to show one sentence is absurd. The deploy already does the work for another
    /// reason — `tools/vendor-gap-art.py` asks the art repository which sets it has, and fills the
    /// gaps from a release archive — so it is the one place that knows, and it now writes down what
    /// it found rather than only what it fixed.
    ///
    /// WHAT THIS CANNOT SEE
    /// ----------------------------------------------------------------------------------
    /// The workflow asks for a set's DIRECTORY, so a set the art repository has started and not
    /// finished looks complete to it and is absent from the manifest. This under-reports and never
    /// over-reports, which is the right direction for a claim made on every page: a set named here
    /// is one nothing can draw, not one that might be fine.
    /// </summary>
    public static IReadOnlyList<SetShortfall> ArtShortfalls(IEnumerable<SetShortfall>? manifest) =>
        manifest?.Where(g => g.Of > 0 && IsShort(g.Set, g.Have, g.Of)).ToArray() ?? [];

    /// <summary>Short enough to say so: under <see cref="CompleteEnough"/>, or any card at all for a promo set.</summary>
    private static bool IsShort(string set, int have, int of) =>
        CardIndex.IsPromoSet(set) ? have < of : have < of * CompleteEnough;

    /// <summary>
    /// One notice for both gaps, or null when there is neither.
    ///
    /// ONE NOTICE, NOT TWO
    /// ==================================================================================
    /// A new set is usually missing both at once, and these are two readings of one fact: the site
    /// is behind on that set. Offered as two notices, only the first is shown — the bar holds one —
    /// and dismissing it would reveal the second, which is the exact shape of nagging this bar
    /// exists to avoid. So they are said together and dismissed together.
    ///
    /// Keyed on the SETS, not on what is missing about them. A set that gains its art and is still
    /// short of detail is the same subject half answered, and re-announcing it would be the bar
    /// reporting its own progress.
    /// </summary>
    public static SiteNotice? Waiting(
        IReadOnlyList<SetShortfall> art, IReadOnlyList<SetShortfall> detail, SetCatalog sets)
    {
        if (art.Count == 0 && detail.Count == 0) return null;

        var artSets = Ordered(art, sets);
        var detailSets = Ordered(detail, sets);

        // The common case by a distance: one new set, and the app has neither its pictures nor its
        // text. Said as one clause because it is one fact about one set.
        var text = artSets.SequenceEqual(detailSets, StringComparer.OrdinalIgnoreCase)
            ? Clause(artSets, "card art and attack detail", sets, art)
            : string.Join(" ", new[]
                {
                    artSets.Count > 0 ? Clause(artSets, "card art", sets, art) : null,
                    detailSets.Count > 0 ? Clause(detailSets, "attack and ability detail", sets, detail) : null,
                }.Where(c => c is not null));

        var subject = artSets.Concat(detailSets)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        // Built from set CODES rather than display names: a name is localised and a code is not, so
        // keying on the name would forget a dismissal the day upstream fills in a translation.
        //
        // Info, not Warning. Nothing has gone wrong — the cards are there, countable and
        // collectable, and what is missing is decoration and reference text. A yellow bar for the
        // ordinary state of release week is how a bar earns being ignored.
        //
        // No action, because there is none: this resolves when upstream publishes, and offering a
        // button that cannot help would be worse than the silence it replaced.
        return new SiteNotice(
            Key: "waiting:" + string.Join("+", subject),
            Level: NoticeLevel.Info,
            Text: text);
    }

    /// <summary>
    /// Set codes, newest first — the same argument as ArtHashCoverage's: the set somebody is
    /// opening packs of is the one that just came out, and it is also the one most likely to be
    /// short of everything.
    /// </summary>
    private static List<string> Ordered(IReadOnlyList<SetShortfall> gaps, SetCatalog sets) =>
        gaps.Select(g => g.Set)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(sets.SortKey, StringComparer.Ordinal)
            .ToList();

    private static string Clause(
        List<string> codes, string missing, SetCatalog sets, IReadOnlyList<SetShortfall> gaps)
    {
        var named = codes.Take(Named).Select(sets.DisplayName).ToArray();
        var list = string.Join(", ", named);
        if (codes.Count > named.Length) list += $" and {codes.Count - named.Length} more";

        // A set nearly all there is said with its count. "Promo B is still missing detail" reads
        // as the whole set, when it is the newest nine.
        var some = codes.Count == 1
                   && gaps.FirstOrDefault(g => g.Set == codes[0]) is { } gap
                   && gap.Have >= gap.Of * CompleteEnough
            ? $" for {gap.Short} {(gap.Short == 1 ? "card" : "cards")}"
            : "";

        return $"{list} {(codes.Count == 1 ? "is" : "are")} still missing {missing}{some}.";
    }

    /// <summary>
    /// The live card database could not be reached, so everything on screen is the copy that
    /// shipped with the build.
    ///
    /// Worth saying because of what it silently does: a set released since the last deploy is
    /// simply absent, so the app looks complete and is a set short. That was the exact shape of a
    /// real bug — the CDN deadline used to expire mid-parse on every device, and the visible
    /// symptom was that the newest set did not exist.
    ///
    /// Not persisted. It is re-tested on every boot, so a remembered dismissal would silence a
    /// genuine outage months later on the strength of one bad afternoon.
    /// </summary>
    public static SiteNotice Offline(string? version) => new(
        Key: "offline",
        Level: NoticeLevel.Warning,
        Text: version is null
            ? "The card database is unreachable. Cards and odds are from the bundled copy."
            : $"The card database is unreachable. Cards and odds are from the bundled copy (v{version}).",
        Persist: false);
}
