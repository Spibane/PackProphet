using System.Net.Http.Json;
using PackProphet.Data;
using PackProphet.Domain;

namespace PackProphet.Services;

/// <summary>Where the loaded card data came from, so the UI can be honest about staleness.</summary>
public enum DataSource
{
    Cdn,

    /// <summary>The snapshot, because the CDN could not be resolved or read. Possibly behind.</summary>
    VendoredSnapshot,

    /// <summary>
    /// The snapshot, because the CDN answered with a release no newer than it. The same bytes the
    /// CDN would have served, or newer ones, so nothing about it is stale and nothing should say so.
    /// </summary>
    CurrentSnapshot,
}

/// <param name="Rarities">
/// The raw rarity table, kept alongside the index because the point and shinedust prices live
/// here and RouteCost / PointsLedger both need them. Read from the data, never hardcoded: they
/// are balance numbers the developers can change.
/// </param>
/// <param name="VendoredArtSets">
/// Sets this deployment shipped card art for, from the manifest the deploy workflow writes.
/// Reported here as well as pushed into <see cref="ArtSource"/> because that is process-wide state
/// and this is a fact about one load — which is what makes it assertable, and what lets a
/// diagnostics view say where the art on screen is coming from.
/// </param>
/// <param name="ArtGaps">
/// How much of each set this deployment can actually draw, for the sets the art repository was
/// missing — from the same manifest as <paramref name="VendoredArtSets"/>.
///
/// The two are different questions about the same deploy. VendoredArtSets is "try our own origin
/// for this set", which has to exclude a set nothing was written for or every card spends a
/// request discovering a 404. This is "what is still not drawable anywhere", which has to INCLUDE
/// exactly that set, because it is the one worth telling the user about.
///
/// Empty when the manifest is absent, which is a development build, every test, and any deploy
/// where upstream had nothing missing — all of which are honestly "nothing known to be missing".
/// </param>
/// <summary>
/// What the boot-time art probe found: sets with no art at all, and promo sets short of their
/// newest few, as shortfalls for the notice bar; and those promos by card key, so their tiles can
/// draw the placeholder without asking.
/// </summary>
public sealed record ArtProbe(IReadOnlyList<SetShortfall> Gaps, IReadOnlySet<string> MissingCards)
{
    public static ArtProbe None { get; } = new([], new HashSet<string>());
}

public sealed record CardData(
    CardIndex Index, PullRates Rates, CardFacts Facts, SetCatalog Sets, PackArtCatalog PackArt,
    IReadOnlyDictionary<string, Rarity> Rarities,
    DataSource Source, string? Version,
    IReadOnlyCollection<string> VendoredArtSets,
    IReadOnlyList<SetShortfall> ArtGaps);

/// <summary>
/// Loads the card database, preferring the live CDN so newly released sets appear without a
/// redeploy, and falling back to the snapshot vendored in wwwroot. A CDN failure is a downgrade
/// rather than an error, so the app never boots to an empty state.
/// </summary>
public sealed class CardDataLoader
{
    private const string Local = "data/snapshot";

    /// <summary>
    /// The card database package, with no version and no path. Never fetched as it stands: every
    /// data file is read from <see cref="PinnedRoot"/> at the version <see cref="Resolver"/> named.
    ///
    /// It used to be fetched unpinned, which jsDelivr resolves to "latest" separately for every
    /// file and then caches for a week. The day a set launched, one browser got cards.min.json
    /// from 2.10.0 and sets.json from 2.11.0: a set list naming B4b over a card list without it.
    /// And a visitor could stay on the old card list for up to that week, while the vendored
    /// snapshot, which is only meant to be the fallback, already had the new set.
    /// </summary>
    private const string Package = "https://cdn.jsdelivr.net/npm/pokemon-tcg-pocket-database";

    private static string PinnedRoot(string version) => $"{Package}@{version}/dist";

    /// <summary>
    /// What "latest" is right now, asked once per load. jsDelivr's own answer rather than the npm
    /// registry's, for two reasons: it names a version the CDN is ready to serve, and it is a host
    /// run by the same operator connect-src already trusts with every dataset, rather than a new
    /// party. It is cached for five minutes, not a week, and a pinned file is immutable, so the
    /// week-long cache on the files stops mattering: a new release changes the URL.
    /// </summary>
    private const string Resolver =
        "https://data.jsdelivr.com/v1/packages/npm/pokemon-tcg-pocket-database/resolved?specifier=latest";

    private sealed record Resolved(string? Version);

    /// <summary>
    /// What a version has to look like before it goes into a URL. The answer is read from the
    /// network, and a path segment such as "../../gh/someone/else@main" would otherwise point the
    /// whole card load at a different package on the same allowed host.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex Semver =
        new(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Card detail comes from a different project to the rest, so it has its own root rather than
    /// sitting under the same one. See NOTICE.md: it is AGPL-3.0-or-later, which is why this
    /// project is.
    ///
    /// Two roots for the same files, and the order they are tried in depends on what is being
    /// asked for.
    ///
    /// The npm package is the reliable one. That project's git repository also carries every card
    /// image and is now 1.84 GB, which is far past the 50 MB jsDelivr allows a /gh/ package — and
    /// the 4.4 MB detail table is on the edge of being refused for it. The same URL answered 403
    /// ("Package size exceeded the configured limit of 50 MB") and then 200 minutes apart. The npm
    /// tarball carries no images, so it is not subject to that at all.
    ///
    /// The repository is the fresh one. npm is published per release and can sit a version behind
    /// the branch, which matters for exactly one thing: a set that has just appeared.
    ///
    /// Pinned to major 5. A 6.x would be free to move the fields this app reads.
    /// </summary>
    private const string FactsNpm = "https://cdn.jsdelivr.net/npm/pokemon-tcg-pocket-cards@5/data/v5";

    private const string FactsRepo =
        "https://cdn.jsdelivr.net/gh/chase-mew/pokemon-tcg-pocket-cards@main/data/v5";

    /// <summary>
    /// How long a CDN request gets to ANSWER before it is treated as a failure. The fallback below
    /// only runs when an attempt *returns*, so a request that hangs — connection held open, no
    /// response, which is what a network that drops packets to the CDN rather than refusing them
    /// looks like — would otherwise leave the app on "Loading card data…" for good.
    ///
    /// Short because it is only the round trip: five seconds is generous for 470 KB from a CDN and
    /// nowhere near enough to parse it, which is why <see cref="GetAsync"/> is careful to stop the
    /// clock at the end of the exchange. Read that note before shortening this further, and before
    /// assuming a timeout here means the network.
    /// </summary>
    private static readonly TimeSpan CdnDeadline = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Longer than <see cref="CdnDeadline"/> because the upstream card detail is 4.4 MB and this
    /// runs after the UI is already usable, so waiting costs some columns rather than the boot.
    /// It is still a deadline: the point is that a hang ends.
    /// </summary>
    private static readonly TimeSpan FactsDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// One set's detail is about 270 KB against the whole table's 4.4 MB, so it gets a
    /// proportionate deadline rather than the same thirty seconds.
    /// </summary>
    private static readonly TimeSpan SetFactsDeadline = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Above this many missing sets, one request for the whole table beats a request each.
    ///
    /// The per-set files are ~270 KB and the whole table is 4.4 MB, so the arithmetic turns over
    /// around sixteen — but the case this guards is not a near-miss. It is a vendored snapshot
    /// that could not be read, or one so old that it predates most of the game: fetching twenty
    /// sets one at a time would be slower AND larger than the file that contains all of them.
    /// Four keeps the per-set path to what it is for, which is a set or two that has appeared
    /// since the last deploy.
    /// </summary>
    private const int MostGapsWorthFetchingSetBySet = 4;

    private readonly HttpClient _http;
    private readonly TimeSpan _artManifestDeadline;
    private readonly bool _probeArt;
    private readonly TimeProvider _clock;
    private CardData? _cached;

    /// <param name="artManifestDeadline">
    /// How long to wait for the art manifest before giving up on it. Defaults to
    /// <see cref="DefaultArtManifestDeadline"/>, which is what the app uses; a caller supplies its
    /// own only where the wall clock is not a measure of anything, which in practice means tests.
    /// </param>
    /// <param name="probeArt">
    /// Whether to ask the art CDN about new sets at boot — see <see cref="ProbeArtAsync"/>. Off
    /// only in tests, where every remote request is a 404 and would read as every new set missing.
    /// </param>
    /// <param name="clock">What "recent" is measured from. The system clock but in tests.</param>
    public CardDataLoader(HttpClient http, TimeSpan? artManifestDeadline = null, bool probeArt = true,
                          TimeProvider? clock = null)
    {
        _http = http;
        _artManifestDeadline = artManifestDeadline ?? DefaultArtManifestDeadline;
        _probeArt = probeArt;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Fetch JSON, putting a deadline on anything that leaves the origin. Local reads are left
    /// alone: they are files the app shipped with, and a slow parse on a phone is not a hang.
    ///
    /// The deadline covers the EXCHANGE and stops there, which is the distinction this method
    /// exists to make. It used to hand the deadline's token to GetFromJsonAsync, and that call
    /// deserialises under the same token — so the clock ran through the parse as well as the
    /// download, and the parse is the slow half by two orders of magnitude. This app publishes
    /// without AOT, so System.Text.Json runs in the IL interpreter: 4,317 cards is 540 KB that
    /// arrives in under ten milliseconds and then takes seconds to turn into objects.
    ///
    /// The result was a fallback that fired every single time, on every device, and said the wrong
    /// thing about why. The CDN answered 200 with the whole body, the deadline expired mid-parse,
    /// TaskCanceledException came back, and the app reported "CDN unreachable" and loaded the
    /// vendored snapshot instead. Live card data had quietly stopped working, and the visible
    /// symptom was that a set released since the last deploy did not exist — the newest set, which
    /// is the one whose packs somebody is opening, and the one whose screenshots then read as
    /// nothing but unrecognised cards.
    ///
    /// A parse cannot hang, so nothing is lost by leaving it unbounded: it is CPU-bound work on
    /// bytes already in hand. What the deadline is for is a connection that is held open and never
    /// answers, which is what a network that drops packets to the CDN looks like, and that is
    /// entirely inside GetStringAsync.
    /// </summary>
    private async Task<T?> GetAsync<T>(string root, string url, TimeSpan deadline, CancellationToken ct)
    {
        if (root == Local) return await _http.GetFromJsonAsync<T>(url, ct);

        string json;
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(deadline);
            json = await _http.GetStringAsync(url, cts.Token);
        }

        return System.Text.Json.JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    /// <summary>
    /// What <c>GetFromJsonAsync</c> uses when it is given none, kept here so the hand-rolled
    /// deserialise above reads the same JSON the same way — camelCase names, case-insensitive.
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>
    /// Which sets this deployment shipped art for.
    ///
    /// Written into the published output by the deploy workflow and deliberately NOT committed, so
    /// it is absent in development, absent in tests, and absent on a deploy that found nothing
    /// missing upstream. All three of those are a 404, and a 404 means "none" rather than a fault.
    ///
    /// It is also not precached: the service worker's include list matches `^data/`, and this
    /// deliberately does not live under data/ -- a manifest that came back from the install-time
    /// cache would name the sets of whichever deploy the user first visited.
    /// </summary>
    private const string ArtManifest = "art/index.json";

    /// <param name="Art">
    /// Per-set art coverage, keyed by set code. Absent in a manifest written before the workflow
    /// recorded it, which reads as "nothing known to be missing" rather than as an error — the
    /// field is additive on purpose, so an old deploy and a new app do not disagree.
    /// </param>
    private sealed record VendoredArt(
        List<string>? Sets, List<string>? Packs, Dictionary<string, ArtHave>? Art);

    private sealed record ArtHave(int Have, int Of);

    /// <summary>
    /// Its own deadline, and a short one.
    ///
    /// This is a few dozen bytes from the host already serving the page, and the app is on
    /// "Loading card data…" until it answers. It also has no fallback worth waiting for: the
    /// answer to a manifest that does not arrive is "no vendored art", which is what a
    /// development build and most deploys say anyway. A hang here used to hold the boot open
    /// indefinitely -- caught by the test that hangs every remote request, which is exactly the
    /// shape of a network that drops packets rather than refusing them.
    ///
    /// A BUDGET, NOT A MEASUREMENT
    /// ----------------------------------------------------------------------------------
    /// Three seconds is generous for a round trip to your own origin and is not generous at all
    /// for three seconds of a machine that is busy. When it fires spuriously, nothing says so:
    /// the answer is "no vendored art", identical to the 404 that means it. That made a test
    /// asserting the manifest was read fail about one run in twenty -- always alone, always with
    /// an empty collection, and only when something else was using the CPU.
    ///
    /// So it is settable. The app keeps the short one, because a user on a slow phone genuinely
    /// would rather boot than wait; a test supplies its own, because the wall clock on a machine
    /// running the whole suite in parallel is not a measure of whether this code works.
    /// </summary>
    public static readonly TimeSpan DefaultArtManifestDeadline = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Read the art manifest and tell <see cref="ArtSource"/> what it found. Awaited inside
    /// <see cref="LoadAsync"/> rather than fired off beside it: it decides what every tile's
    /// data-src is, so it has to be answered before the first grid renders or the newest set
    /// spends a request per card discovering the upstream gap it was vendored to fill.
    ///
    /// Same origin and a couple of dozen bytes, so awaiting it costs a round trip to the host
    /// already serving the page.
    /// </summary>
    private async Task<(IReadOnlyCollection<string> Sets, IReadOnlyList<SetShortfall> Gaps)>
        ReadArtManifestAsync(CancellationToken ct)
    {
        VendoredArt? manifest;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_artManifestDeadline);

            manifest = await _http.GetFromJsonAsync<VendoredArt>(ArtManifest, cts.Token);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Absent, malformed, or offline with nothing cached. Every card falls back to the
            // remote chain, which is where it came from before any of this existed.
            manifest = null;
        }

        ArtSource.UseVendored(manifest?.Sets, manifest?.Packs, _http.BaseAddress);

        // Reported from what THIS load read, not from the static it has just written.
        //
        // ArtSource is process-wide by design -- it is read from PocketCard.ArtUrl, which markup
        // calls from a dozen places, and it is set once during boot in a single-threaded
        // WebAssembly runtime. Writing it and reading it back is safe in the app and is a race
        // anywhere a second loader exists, which is every test run: another boot calling
        // UseVendored between these two lines makes this load report the other one's manifest.
        //
        // The value is a fact about the response that just arrived, so it comes from the response.
        // Blanks dropped to match what UseVendored stores, so the pair cannot disagree about how
        // many sets were vendored.
        var sets = manifest?.Sets?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() ?? [];

        // Nonsense dropped rather than carried: a set with Of <= 0 divides into nothing downstream,
        // and one claiming more art than cards is a manifest this app cannot reason about. Both
        // read as "nothing known about that set", which is the same answer as an absent manifest.
        var gaps = manifest?.Art?
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .Where(kv => kv.Value is { Of: > 0 } v && v.Have >= 0 && v.Have <= v.Of)
            .Select(kv => new SetShortfall(kv.Key, kv.Value.Have, kv.Value.Of))
            .ToArray() ?? [];

        return (sets, gaps);
    }

    public async Task<CardData> LoadAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        var art = await ReadArtManifestAsync(ct);

        var vendored = await TryReadVersionAsync(Local, ct);

        var loaded = await TryLoadLiveAsync(vendored, ct)
            ?? await TryLoadAsync(Local, DataSource.VendoredSnapshot, vendored, ct)
            ?? throw new InvalidOperationException(
                "Neither the CDN nor the vendored snapshot could be loaded.");

        return _cached = loaded with { VendoredArtSets = art.Sets, ArtGaps = art.Gaps };
    }

    /// <summary>How recent a set has to be for its art to be asked about at boot.</summary>
    public static readonly TimeSpan ArtProbeWindow = TimeSpan.FromDays(60);

    /// <summary>
    /// How long to wait for the art CDN's answer. Asked after boot rather than during it, because
    /// a cold jsDelivr edge took over three seconds to 404 B4b's missing art on release day, and a
    /// present file it has not cached yet up to eleven. A timeout reads as "not known", which
    /// leaves the set on the ordinary chain, so waiting longer costs nothing but the answer.
    /// </summary>
    public static readonly TimeSpan ArtProbeDeadline = TimeSpan.FromSeconds(20);

    /// <summary>
    /// New sets that neither art source has, found by asking about one card of each.
    ///
    /// The manifest can only say what was missing when the site was deployed, and a set released
    /// since has no entry in it. Left to the grid, such a set is discovered card by card: every
    /// tile walks both sources, twice each with a pause between, six at a time, so a screenful of
    /// a new set spends twenty seconds or more looking like a connection problem before it draws
    /// the placeholder. One HEAD per source for its first card answers the same question for the
    /// whole set, which is what the deploy's own check does with a directory.
    ///
    /// A fetch sees the status where an img does not, so a 404 is told apart from throttling or a
    /// timeout, and only two 404s count. Anything else is "not known" and changes nothing. Asked
    /// only of sets released in the last <see cref="ArtProbeWindow"/>, or not yet in the set list,
    /// that this deployment did not vendor or already report.
    ///
    /// Run after boot, by AppSession, beside the card detail. The grid is already up and queued by
    /// then; what the answer does is re-render the set's tiles with nowhere to look, and tell the
    /// notice bar. Empty when the probe is switched off.
    /// </summary>
    public async Task<ArtProbe> ProbeArtAsync(CardData data, CancellationToken ct = default)
    {
        if (!_probeArt) return ArtProbe.None;
        var art = (Sets: data.VendoredArtSets, Gaps: data.ArtGaps);

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var since = today.AddDays(-ArtProbeWindow.Days);

        var unknown = data.Index.BySet
            .Where(kv => kv.Value.Count > 0)
            .Where(kv => !art.Sets.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)
                         && !art.Gaps.Any(g => string.Equals(g.Set, kv.Key, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var asked = unknown
            .Where(kv => !CardIndex.IsPromoSet(kv.Key))
            .Where(kv => data.Sets.Info(kv.Key)?.ReleasedOn is not { } released || released >= since)
            .ToArray();

        var promos = unknown.Where(kv => CardIndex.IsPromoSet(kv.Key)).ToArray();

        if (asked.Length == 0 && promos.Length == 0) return ArtProbe.None;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ArtProbeDeadline);

        var sets = Task.WhenAll(asked.Select(async kv =>
            await MissingAsync(kv.Value.MinBy(c => c.Number)!, cts.Token) == true
                ? new SetShortfall(kv.Key, 0, kv.Value.Count)
                : null));

        var runs = Task.WhenAll(promos.Select(async kv =>
        {
            var run = await NewestMissingAsync(kv.Value, cts.Token);
            return (Gap: run.Count == 0 ? null : new SetShortfall(kv.Key, kv.Value.Count - run.Count, kv.Value.Count),
                    Cards: run);
        }));

        var bare = (await sets).OfType<SetShortfall>();
        var partial = await runs;

        return new ArtProbe(
            [.. bare, .. partial.Select(r => r.Gap).OfType<SetShortfall>()],
            partial.SelectMany(r => r.Cards).Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>How far down a promo set's newest cards to look, once the newest has no art.</summary>
    private const int PromoRun = 16;

    /// <summary>How many of a promo run to ask about at a time.</summary>
    private const int PromoBatch = 4;

    /// <summary>
    /// A promo set's newest cards that neither source has art for, newest first.
    ///
    /// A promo set is never published whole, so its directory is always there and asking about
    /// one card says nothing about the rest. What goes missing is the newest few, added to the end
    /// of the list since the art was last drawn: PROMO-B 95 to 103 in 2.11.0. So the newest card
    /// is asked about first, and when it has art that is the whole cost. When it does not, the
    /// next <see cref="PromoRun"/> are asked a few at a time, counting down from the top to the
    /// first card that has art or cannot be answered. A few at a time because the grid is loading
    /// its own art on the same connection, and a burst of thirty requests on top of it lost some.
    /// </summary>
    private async Task<IReadOnlyList<PocketCard>> NewestMissingAsync(
        IReadOnlyList<PocketCard> cards, CancellationToken ct)
    {
        var newest = cards.OrderByDescending(c => c.Number).Take(PromoRun + 1).ToArray();
        if (await MissingAsync(newest[0], ct) != true) return [];

        var run = new List<PocketCard> { newest[0] };
        foreach (var batch in newest.Skip(1).Chunk(PromoBatch))
        {
            var answers = await Task.WhenAll(batch.Select(c => MissingAsync(c, ct)));
            foreach (var (card, missing) in batch.Zip(answers))
            {
                if (missing != true) return run;
                run.Add(card);
            }
        }

        return run;
    }

    /// <summary>
    /// True when neither source has this card's art, false when either does, null when that could
    /// not be told. Both at once: asked in turn, two cold 404s came to 2.5 seconds.
    /// </summary>
    private async Task<bool?> MissingAsync(PocketCard card, CancellationToken ct)
    {
        var absent = await Task.WhenAll(ArtSource.RemoteCandidates(card.Set, card.Number)
                                                 .Select(url => AbsentAsync(url, ct)));

        if (absent.All(a => a == true)) return true;
        if (absent.Any(a => a == false)) return false;
        return null;
    }

    /// <summary>A second go for an answer that did not come, or came as a 403.</summary>
    private static readonly TimeSpan ReaskAfter = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether one source lacks one file: true on a 404 or on jsDelivr refusing the file outright,
    /// false on a success, null when neither came back after a second try.
    ///
    /// A 403 is read rather than trusted either way, because jsDelivr sends two. One is throttling,
    /// which passes. The other is permanent for a file it cannot serve: the mirror repository is
    /// past jsDelivr's 50 MB package limit, and a file jsDelivr has not already indexed is refused
    /// with "Package size exceeded", which for this app is the same as the file not being there.
    /// </summary>
    private async Task<bool?> AbsentAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            switch (await StatusOfAsync(url, ct))
            {
                case System.Net.HttpStatusCode.NotFound: return true;
                case { } ok when (int)ok is >= 200 and < 300: return false;
                case System.Net.HttpStatusCode.Forbidden when await RefusedAsync(url, ct): return true;
            }

            try { await Task.Delay(ReaskAfter, ct); }
            catch (OperationCanceledException) { return null; }
        }

        return null;
    }

    /// <summary>Whether a 403 from jsDelivr is its package-size refusal, which does not pass.</summary>
    private async Task<bool> RefusedAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct);
            return response.StatusCode == System.Net.HttpStatusCode.Forbidden
                   && (await response.Content.ReadAsStringAsync(ct))
                          .Contains("Package size exceeded", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>The status a HEAD comes back with, or null when it does not come back.</summary>
    private async Task<System.Net.HttpStatusCode?> StatusOfAsync(string url, CancellationToken ct)
    {
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _http.SendAsync(head, ct);
            return response.StatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// The live card data, all four files from one release, or null for the snapshot fallback.
    ///
    /// A resolution that fails is a fallback, not a reason to read the unpinned URLs instead: those
    /// are the mixed-release files this exists to avoid, and the snapshot is at least one release
    /// throughout.
    ///
    /// A snapshot at or past the resolved release is read in place of the CDN. Equal means the same
    /// files, since an npm version cannot be republished, so there is nothing to download. Newer is
    /// the resolver's five-minute cache or a CDN edge lagging a deploy, and loading the older CDN
    /// copy then would take a set away that the snapshot already has.
    /// </summary>
    private async Task<CardData?> TryLoadLiveAsync(string? vendored, CancellationToken ct)
    {
        var live = await TryResolveVersionAsync(ct);
        if (live is null) return null;

        if (IsAtLeast(vendored, live))
            return await TryLoadAsync(Local, DataSource.CurrentSnapshot, vendored, ct);

        return await TryLoadAsync(PinnedRoot(live), DataSource.Cdn, live, ct);
    }

    private async Task<string?> TryResolveVersionAsync(CancellationToken ct)
    {
        try
        {
            var resolved = await GetAsync<Resolved>(Resolver, Resolver, CdnDeadline, ct);
            return resolved?.Version is { } v && Semver.IsMatch(v) ? v : null;
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Compared as numbers, because as strings 2.9.0 sorts after 2.10.0. A prerelease suffix is
    /// dropped rather than ordered: "latest" never names one, and a snapshot is only ever written
    /// from "latest". Anything that does not parse is not at least anything, so an unreadable
    /// VERSION file leaves the CDN in charge.
    /// </summary>
    private static bool IsAtLeast(string? vendored, string live) =>
        Core(vendored) is { } have && Core(live) is { } want && have >= want;

    private static Version? Core(string? semver) =>
        semver is not null && Version.TryParse(semver.Split('-', 2)[0], out var v) ? v : null;

    private async Task<CardData?> TryLoadAsync(
        string root, DataSource source, string? version, CancellationToken ct)
    {
        try
        {
            var cards = await GetAsync<List<PocketCard>>(root, $"{root}/cards.min.json", CdnDeadline, ct);
            var rarities = await GetAsync<Dictionary<string, Rarity>>(root, $"{root}/rarities.json", CdnDeadline, ct);
            var rates = await GetAsync<Dictionary<string, Dictionary<string, PackVariant>>>(
                root, $"{root}/pullRates.json", CdnDeadline, ct);

            if (cards is null or { Count: 0 } || rarities is null or { Count: 0 } || rates is null)
                return null;

            var index = new CardIndex(cards, rarities);
            var published = await TryLoadSetsAsync(root, ct);
            var catalog = new SetCatalog(published, index.BySet.Keys, index.AllPackKeys);

            // Small (about 20 KB) and it decides which booster image every tile shows, so it is
            // loaded up front unlike the card detail.
            var packArt = new PackArtCatalog(await TryLoadExpansionsAsync(root, ct), index.AllPackKeys);

            // Card detail is not loaded here. It is 4.4 MB against about 500 KB for everything
            // else, and parsing 4,317 nested records in the WebAssembly interpreter takes long
            // enough to look like the app has hung. It is enrichment — attacks and abilities —
            // rather than something a screen needs to function, so LoadFactsAsync fetches it
            // afterwards and folds it in.
            // VendoredArtSets is filled in by LoadAsync, which read the manifest before either
            // source was tried: it is a fact about the deployment rather than about which of the
            // two data sources answered.
            return new CardData(index, new PullRates(rates), CardFacts.Empty, catalog, packArt,
                                rarities, source, version, [], []);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or NotSupportedException
                                      or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task<List<ExpansionInfo>?> TryLoadExpansionsAsync(string root, CancellationToken ct)
    {
        var url = root == Local
            ? $"{root}/expansions.json"
            : "https://cdn.jsdelivr.net/gh/chase-mew/pokemon-tcg-pocket-cards@main/data/v5/expansions.json";

        try { return await GetAsync<List<ExpansionInfo>>(root, url, CdnDeadline, ct); }
        catch { return null; }   // falls back to the lower-resolution art
    }

    private async Task<Dictionary<string, List<SetInfo>>?> TryLoadSetsAsync(
        string root, CancellationToken ct)
    {
        // Optional: without it the catalogue derives series from set codes instead, which is
        // a slightly worse grouping rather than a broken app.
        try { return await GetAsync<Dictionary<string, List<SetInfo>>>(root, $"{root}/sets.json", CdnDeadline, ct); }
        catch { return null; }
    }

    /// <summary>
    /// Fetch card detail (attacks, abilities, stage, stats). Separate from <see cref="LoadAsync"/>
    /// and called after the UI is usable: it is the largest payload and none of it is needed to
    /// render a collection.
    ///
    /// The vendored snapshot, then whatever it is missing.
    /// This used to be a list of two sources returning the first that answered — the local copy
    /// and then the CDN — with a comment saying the CDN was "the reason a brand-new set still gets
    /// detail without a redeploy". It could not do that, and never had: the local read succeeds on
    /// every visit, so the loop returned on the first pass and the second source was unreachable
    /// in the only case that mattered. Card detail was frozen at whatever had last been vendored,
    /// and a new set showed empty attack, ability and HP columns until somebody redeployed.
    ///
    /// Both halves of that were right on their own, so both are kept and the gap is what decides:
    ///
    ///   * the vendored copy is read first, always. It carries only the fields this app reads —
    ///     1.5 MB against 4.4 MB — so it parses roughly three times faster, and on any ordinary
    ///     visit it is the whole answer and nothing leaves the origin.
    ///   * then whichever sets it does not cover are fetched one file each, about 270 KB, from
    ///     the same project's per-set files. That is a request for a set that appeared since the
    ///     last deploy, and nothing at all when there has not been one.
    ///
    /// <paramref name="coveringSets"/> is what the card data knows about, which is live from a CDN
    /// and therefore ahead of anything vendored. Passing none disables the top-up, which is what a
    /// caller that only wants the vendored table asks for.
    ///
    /// Returns <see cref="CardFacts.Empty"/> on failure, so a fetch that cannot be completed costs
    /// some columns rather than the app.
    /// </summary>
    public async Task<CardFacts> LoadFactsAsync(
        IReadOnlyCollection<string>? coveringSets = null, CancellationToken ct = default)
    {
        var (facts, _) = await TryFactsAsync(Local, $"{Local}/cards.v5.json", FactsDeadline, ct);

        // Only when the vendored copy could not be read at all — a corrupt or absent file. The
        // whole table from upstream is the answer to that, not a per-set walk.
        if (facts is null or { Count: 0 }) facts = await WholeTableAsync(ct);
        if (facts is null or { Count: 0 }) return CardFacts.Empty;

        var gaps = MissingSets(facts, coveringSets);

        if (gaps.Count > MostGapsWorthFetchingSetBySet)
        {
            var whole = await WholeTableAsync(ct);
            if (whole is { Count: > 0 }) return new CardFacts(whole);
        }
        else
        {
            foreach (var code in gaps)
            {
                // Best effort, one set at a time. A set whose detail upstream has not published
                // yet answers 404 and is simply not added, which is the state the app already
                // renders: the columns that need it stay blank and everything else works.
                var added = await SetTableAsync(code, ct);
                if (added is { Count: > 0 }) facts.AddRange(added);
            }
        }

        // CardFacts indexes with TryAdd, so the vendored entries win any collision. Ordering the
        // concatenation the other way would let a per-set file silently replace the trimmed copy
        // the app was built against.
        return new CardFacts(facts);
    }

    /// <summary>
    /// Sets the card data knows about that this detail table has nothing for, in the spelling the
    /// per-set files use.
    ///
    /// The two projects code the promos differently — PROMO-A here, pa there — which is the one
    /// difference that would silently make every promo look like a permanent gap and refetch it on
    /// every single visit. <see cref="ArtSource.MirrorSetCode"/> is the same translation the art
    /// URLs use, shared so the pair cannot drift.
    /// </summary>
    private static List<string> MissingSets(
        List<CardFact> have, IReadOnlyCollection<string>? coveringSets)
    {
        if (coveringSets is null or { Count: 0 }) return [];

        var covered = have
            .Select(f => f.SetCode)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return coveringSets
            .Select(ArtSource.MirrorSetCode)
            .Where(code => !covered.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// One set's detail. The repository first and npm second, which is the opposite order to
    /// <see cref="WholeTableAsync"/> and deliberately so: this call exists for freshness, and the
    /// repository is the fresher of the two. A 270 KB file is also well clear of the size that
    /// makes the repository route unreliable — measured, three requests in a row, all served.
    /// </summary>
    private async Task<List<CardFact>?> SetTableAsync(string code, CancellationToken ct)
    {
        foreach (var root in new[] { FactsRepo, FactsNpm })
        {
            var (facts, absent) = await TryFactsAsync(root, $"{root}/{code}/{code}.min.json",
                                                      SetFactsDeadline, ct);
            if (facts is { Count: > 0 }) return facts;

            // A plain 404 from the repository settles it for npm as well. The package is published
            // FROM that repository, so it is never ahead of the branch: a file the branch does not
            // have cannot be in the package. Which is not a hypothetical -- it is B4a's state right
            // now, and without this every visit would spend two requests learning it twice.
            //
            // A route failure is different, and still worth the second try: that is the 403 the
            // repository answers when jsDelivr refuses it for the repository's size.
            if (absent) return null;
        }
        return null;
    }

    /// <summary>
    /// The complete table. npm first here: this is the 4.4 MB file, which is the one the
    /// repository route can be refused for.
    /// </summary>
    private async Task<List<CardFact>?> WholeTableAsync(CancellationToken ct)
    {
        foreach (var root in new[] { FactsNpm, FactsRepo })
        {
            // No short-circuit on absence here, unlike SetTableAsync: that argument runs one way
            // only. npm trailing the branch means a 404 from npm says nothing about the branch.
            var (facts, _) = await TryFactsAsync(root, $"{root}/cards.min.json", FactsDeadline, ct);
            if (facts is { Count: > 0 }) return facts;
        }
        return null;
    }

    /// <summary>
    /// One attempt at a detail file, and whether the answer was a definite "no such file".
    ///
    /// The distinction earns its place because the two failures mean opposite things about trying
    /// somewhere else: a 404 is upstream telling you it does not have this, and every mirror of
    /// upstream will say the same; a timeout or a 403 is one route being unavailable, and another
    /// route may well serve.
    ///
    /// A failure is never a fault here. Only the caller's own cancellation propagates, since the
    /// deadline arrives as a cancellation too.
    /// </summary>
    private async Task<(List<CardFact>? Facts, bool Absent)> TryFactsAsync(
        string root, string url, TimeSpan deadline, CancellationToken ct)
    {
        try
        {
            return (await GetAsync<List<CardFact>>(root, url, deadline, ct), false);
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return (null, true);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (null, false);
        }
    }

    private async Task<string?> TryReadVersionAsync(string root, CancellationToken ct)
    {
        try { return (await _http.GetStringAsync($"{root}/VERSION", ct)).Trim(); }
        catch { return null; }
    }
}
