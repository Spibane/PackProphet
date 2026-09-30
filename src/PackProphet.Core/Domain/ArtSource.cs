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
///   2. flibustier/pokemon-tcg-exchange, the complete back catalogue and the source this app has
///      always used.
///   3. chase-mew/pokemon-tcg-pocket-cards, a second mirror on an independent schedule. Already a
///      dependency of this app for card detail and expansion logos, and already credited in
///      NOTICE.md, so it costs no new origin and no new licence.
///
/// Every remote entry is jsDelivr, which is the single remote host index.html's img-src allows.
/// Adding a source that is not on that origin means editing the Content-Security-Policy, and the
/// policy is deliberately one host wide.
/// </summary>
public static class ArtSource
{
    /// <summary>
    /// Verified working pattern: cards-by-set/{SET}/{number}.webp, with the number NOT zero-padded
    /// (unlike the TCGdex id, and unlike <see cref="Mirror"/> below).
    /// </summary>
    public const string Exchange =
        "https://cdn.jsdelivr.net/gh/flibustier/pokemon-tcg-exchange@main/public/images/cards-by-set";

    /// <summary>
    /// The second mirror. Its own dataset publishes these as raw.githubusercontent.com URLs and
    /// this deliberately does not use those: GitHub rate-limits raw as an asset host, and it is not
    /// on the one origin this app's img-src permits. jsDelivr serves the same bytes.
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
    /// Booster and expansion-logo art, which is not a chain.
    ///
    /// Both are drawn as CSS backgrounds rather than as &lt;img&gt; elements, and CSS has no
    /// fallback for an image that fails -- `background-image: url(a), url(b)` layers the two, it
    /// does not try the second one. So these get one URL, chosen from the manifest, and a set
    /// whose booster has not been vendored keeps the drawn placeholder it has always had.
    /// </summary>
    public const string ExchangePacks =
        "https://cdn.jsdelivr.net/gh/flibustier/pokemon-tcg-exchange@main/public/images/packs";

    public const string ExchangeSets =
        "https://cdn.jsdelivr.net/gh/flibustier/pokemon-tcg-exchange@main/public/images/sets";

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
    public static void UseVendored(IEnumerable<string>? sets, IEnumerable<string>? packs = null)
    {
        _vendored = Names(sets);
        _vendoredPacks = Names(packs);
    }

    private static HashSet<string> Names(IEnumerable<string>? values) =>
        new(values?.Where(v => !string.IsNullOrWhiteSpace(v)) ?? [], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The two remote sources for one card, whatever is known about them. What the loader asks
    /// when it wants to know whether a set has art anywhere yet.
    /// </summary>
    public static IReadOnlyList<string> RemoteCandidates(string set, int number) =>
        [$"{Exchange}/{set}/{number}.webp", $"{Mirror}/{MirrorSetCode(set)}/{number:D3}.webp"];

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
        var urls = new List<string>(3);
        if (_vendored.Contains(set)) urls.Add($"{OwnOrigin}/{set}/{number}.webp");

        urls.Add($"{Exchange}/{set}/{number}.webp");
        urls.Add($"{Mirror}/{MirrorSetCode(set)}/{number:D3}.webp");
        return urls;
    }

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
            ? $"{OwnOrigin}/packs/{Uri.EscapeDataString(packName)}.webp"
            : $"{ExchangePacks}/{Uri.EscapeDataString(packName)}.webp";

    /// <summary>A set's expansion logo, used where a pack has no art of its own.</summary>
    public static string SetLogo(string setCode) =>
        _vendored.Contains(setCode)
            ? $"{OwnOrigin}/sets/LOGO_expansion_{Uri.EscapeDataString(setCode)}_en_US.webp"
            : $"{ExchangeSets}/LOGO_expansion_{Uri.EscapeDataString(setCode)}_en_US.webp";
}
