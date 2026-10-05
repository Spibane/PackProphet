namespace PackProphet.Domain;

/// <summary>
/// Where a card's artwork is fetched from, in the order to try.
///
/// There used to be one source, and one source is the whole problem. Card DATA and card ART are
/// published by the same author from two different repositories on two different cadences: the
/// data ships within days of a set going live, and the art is a manual commit that lands when it
/// lands. B4a's data was published on 2026-08-27 and its art was still absent a week later, so
/// every card in the newest set — the set people are actually opening — drew as a placeholder.
/// That is not a fault anyone can see from the outside: the app looks broken and the CDN is
/// serving exactly what it has.
///
/// So art is a chain rather than a URL, ordered cheapest-correct-answer first:
///
///   1. This deployment's own origin, for sets it was built with. Written into the published
///      output by the deploy workflow and never committed — see <see cref="UseVendored"/>.
///   2. TCGdex, for the sets the deploy found it has — see <see cref="UseTcgDex"/>. An open
///      project that serves its assets for exactly this, at 600x825, but sets reach it late.
///   3. Limitless TCG, where the card data itself is compiled from. Every set, on release day.
///   4. chase-mew/pokemon-tcg-pocket-cards, a mirror on an independent schedule. Already a
///      dependency of this app for card detail and expansion logos.
///
/// There used to be flibustier/pokemon-tcg-exchange in second place, the source this app was
/// built on. On 2026-10-05 its card and booster directories became symlinks to a checkout outside
/// the repository, and every URL under them 404s.
///
/// Each remote host is named in index.html's img-src, and a new one has to be too.
/// </summary>
public static class ArtSource
{
    /// <summary>
    /// {set}/{number}/high.webp, the number zero-padded to three and the promos spelled P-A and
    /// P-B — see <see cref="PublishedSetCode"/>.
    /// </summary>
    public const string TcgDex = "https://assets.tcgdex.net/en/tcgp";

    /// <summary>
    /// {set}/{set}_{number}_EN.webp, padded and spelled as <see cref="TcgDex"/> is.
    ///
    /// It sends no CORS headers, so a fetch from the browser cannot read its answer, and it
    /// answers a missing file with 403 rather than 404. An img needs neither. The deploy, which
    /// is not a browser, is what asks it which sets it has.
    /// </summary>
    public const string Limitless = "https://limitlesstcg.nyc3.cdn.digitaloceanspaces.com/pocket";

    /// <summary>
    /// The last resort. Its own dataset publishes these as raw.githubusercontent.com URLs and this
    /// deliberately does not use those: GitHub rate-limits raw as an asset host, and it is not in
    /// this app's img-src. jsDelivr serves the same bytes -- the ones it has cached: the repository
    /// is past jsDelivr's 50 MB limit, so a file it has not cached is refused with a 403.
    ///
    /// Note the repository name. The dataset says `chase-manning/...`, which 301-redirects to
    /// `chase-mew/...` — and jsDelivr does not follow the rename, so the old path 403s while the
    /// new one serves. Same repository either way.
    /// </summary>
    public const string Mirror =
        "https://cdn.jsdelivr.net/gh/chase-mew/pokemon-tcg-pocket-cards@main/images/webp/cards";

    /// <summary>
    /// Art this deployment serves itself.
    ///
    /// Relative rather than rooted, so it resolves against index.html's &lt;base href&gt; instead
    /// of assuming the app is at a domain root. This app is, and the deploy asserts as much, but a
    /// fork on project-page hosting is served from /&lt;repo&gt;/ and a rooted path would send every
    /// request to a path that host does not have.
    /// </summary>
    public const string OwnOrigin = "art";

    /// <summary>
    /// <see cref="OwnOrigin"/> as an absolute address once the app's base is known, and as the
    /// bare relative path until then.
    ///
    /// Absolute because a relative url() does not survive a CSS custom property. Every place that
    /// draws art as a background passes it in as --art, and the stylesheet that reads --art lives
    /// in css/, so "art/B4b/2.webp" was fetched as css/art/B4b/2.webp and 404'd: every vendored
    /// card and booster drawn that way was a placeholder while the same file loaded in an img. The
    /// base is the app's own, index.html's &lt;base href&gt;, so a fork served from /&lt;repo&gt;/
    /// still gets its own path.
    /// </summary>
    private static string _ownOrigin = OwnOrigin;

    /// <summary>
    /// Booster and expansion-logo art, which is not a chain.
    ///
    /// Both are drawn as CSS backgrounds rather than as &lt;img&gt; elements, and CSS has no
    /// fallback for an image that fails -- `background-image: url(a), url(b)` layers the two, it
    /// does not try the second one. So these get one URL, chosen from the manifest, and a set
    /// whose booster has not been vendored keeps the drawn placeholder it has always had.
    ///
    /// From the database repository, which is where pokemon-tcg-exchange's own copies now point.
    /// It holds data and these and nothing else, 5.5 MB in all, so jsDelivr serves every file of it.
    /// </summary>
    public const string DatabasePacks =
        "https://cdn.jsdelivr.net/gh/flibustier/pokemon-tcg-pocket-database@main/dist/images/packs";

    public const string DatabaseSets =
        "https://cdn.jsdelivr.net/gh/flibustier/pokemon-tcg-pocket-database@main/dist/images/sets";

    private static IReadOnlySet<string> _tcgDex =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlySet<string> _vendored =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlySet<string> _vendoredPacks =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlySet<string> _missing =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Declare the sets neither remote source has art for yet, from the loader's boot-time probe.
    /// A set here that this deployment did not vendor has no candidates at all, so its tiles draw
    /// the placeholder at once instead of finding out one request at a time.
    /// </summary>
    public static void UseMissing(IEnumerable<string>? sets, IEnumerable<string>? cards = null)
    {
        _missing = Names(sets);
        _missingCards = Names(cards);
    }

    /// <summary>Single cards with no art yet, by card key: a promo set's newest few.</summary>
    private static IReadOnlySet<string> _missingCards =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, (string Set, int Number)> _standIns =
        new Dictionary<string, (string Set, int Number)>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Other printings to draw reprints from, by card key, for sets with no art of their own.
    ///
    /// A reprint's artwork file is its original's, byte for byte, because the file name is the
    /// card's identity (see <see cref="PocketCard.OwnershipKey"/>). So a Deluxe set whose own
    /// directory is empty can still draw every card it reprints from the set it came from: 236
    /// of B4b's 429. Only its new cards, and its foils, have to wait.
    /// </summary>
    public static void UseStandIns(IReadOnlyDictionary<string, (string Set, int Number)>? standIns) =>
        _standIns = standIns ?? new Dictionary<string, (string Set, int Number)>();

    /// <summary>Whether a card from this set has anywhere to be drawn from.</summary>
    public static bool HasArt(string set) => _vendored.Contains(set) || !_missing.Contains(set);

    /// <summary>Whether this card has anywhere to be drawn from, its own set's or a stand-in's.</summary>
    public static bool HasArt(PocketCard card) =>
        (HasArt(card.Set) && !_missingCards.Contains(card.Key)) || _standIns.ContainsKey(card.Key);

    /// <summary>Sets this deployment holds art for. Empty in development and in tests.</summary>
    public static IReadOnlySet<string> VendoredSets => _vendored;

    /// <summary>Packs this deployment holds booster art for.</summary>
    public static IReadOnlySet<string> VendoredPacks => _vendoredPacks;

    /// <summary>
    /// Declare which sets the deployment shipped art for, from the manifest the deploy workflow
    /// writes. Null or empty means none, which is the correct answer for a development build and
    /// for any deploy where upstream had nothing missing.
    ///
    /// Settable rather than a constructor argument because the alternative is threading a service
    /// through <see cref="PocketCard.ArtUrl"/>, which is read from a dozen places in markup. This
    /// is set once during boot, before the first grid renders, in a single-threaded WebAssembly
    /// runtime.
    /// </summary>
    /// <param name="root">The app's base address, which vendored art is served under.</param>
    public static void UseVendored(IEnumerable<string>? sets, IEnumerable<string>? packs = null, Uri? root = null)
    {
        _vendored = Names(sets);
        _vendoredPacks = Names(packs);
        _ownOrigin = root is null ? OwnOrigin : new Uri(root, OwnOrigin).ToString();
    }

    private static HashSet<string> Names(IEnumerable<string>? values) =>
        new(values?.Where(v => !string.IsNullOrWhiteSpace(v)) ?? [], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Declare which sets TCGdex has, from the manifest the deploy workflow writes. Its catalogue
    /// trails the game by months, so a set it does not list is left off the chain rather than
    /// costing a 404 per card. Empty in development and in tests, where the chain starts at
    /// <see cref="Limitless"/>.
    /// </summary>
    public static void UseTcgDex(IEnumerable<string>? sets) => _tcgDex = Names(sets);

    /// <summary>
    /// The remote sources for one card, whatever is known about them. What the loader asks when it
    /// wants to know whether a set has art anywhere yet.
    /// </summary>
    public static IReadOnlyList<string> RemoteCandidates(string set, int number)
    {
        var code = PublishedSetCode(set);
        var urls = new List<string>(3);
        if (_tcgDex.Contains(set)) urls.Add($"{TcgDex}/{code}/{number:D3}/high.webp");
        urls.Add($"{Limitless}/{code}/{code}_{number:D3}_EN.webp");
        urls.Add($"{Mirror}/{MirrorSetCode(set)}/{number:D3}.webp");
        return urls;
    }

    /// <summary>
    /// Every place this card's art might be, best first. Empty for a set known to have no art
    /// yet, which is what draws the placeholder without a request.
    /// </summary>
    public static IReadOnlyList<string> Candidates(PocketCard card)
    {
        var own = HasArt(card.Set) && !_missingCards.Contains(card.Key);
        if (own) return Candidates(card.Set, card.Number);
        return _standIns.TryGetValue(card.Key, out var standIn) ? Candidates(standIn.Set, standIn.Number) : [];
    }

    public static IReadOnlyList<string> Candidates(string set, int number)
    {
        if (!HasArt(set)) return [];

        // Own origin only for a set it was actually built with. Trying it for every card would
        // spend one request per card of the back catalogue discovering a 404 on a set the deploy
        // never vendored -- 3,769 of them at the time of writing, all against this app's own host.
        var remote = RemoteCandidates(set, number);
        return _vendored.Contains(set) ? [$"{_ownOrigin}/{set}/{number}.webp", .. remote] : remote;
    }

    /// <summary>
    /// How TCGdex and Limitless spell a set code: as this app does, but for the promos, which are
    /// P-A and P-B there.
    /// </summary>
    public static string PublishedSetCode(string set) => set.ToUpperInvariant() switch
    {
        "PROMO-A" => "P-A",
        "PROMO-B" => "P-B",
        _ => set,
    };

    /// <summary>
    /// The mirror's own spelling of a set code: lower case, and the promos are two letters rather
    /// than the word. Its numbers are zero-padded to three, which is why this is a translation
    /// rather than a second string format.
    ///
    /// Public because that repository names its DATA files the same way it names its images, so
    /// the card-detail top-up in CardDataLoader builds a URL from this too. Two copies of a
    /// mapping is how the two drift, and the promos are the pair that would drift first.
    ///
    /// The reverse direction — their spelling back into this app's — lives on
    /// <see cref="PackProphet.Data.CardFact.CardKey"/>.
    /// </summary>
    public static string MirrorSetCode(string set) => set.ToLowerInvariant() switch
    {
        "promo-a" => "pa",
        "promo-b" => "pb",
        var other => other,
    };

    /// <summary>
    /// Booster art for a pack. Escaped because pack names carry spaces -- "Team Rocket" -- and
    /// the file is named by the pack.
    /// </summary>
    public static string PackArt(string packName) =>
        _vendoredPacks.Contains(packName)
            ? $"{_ownOrigin}/packs/{Uri.EscapeDataString(packName)}.webp"
            : $"{DatabasePacks}/{Uri.EscapeDataString(packName)}.webp";

    /// <summary>A set's expansion logo, used where a pack has no art of its own.</summary>
    public static string SetLogo(string setCode) =>
        _vendored.Contains(setCode)
            ? $"{_ownOrigin}/sets/LOGO_expansion_{Uri.EscapeDataString(setCode)}_en_US.webp"
            : $"{DatabaseSets}/LOGO_expansion_{Uri.EscapeDataString(setCode)}_en_US.webp";
}
