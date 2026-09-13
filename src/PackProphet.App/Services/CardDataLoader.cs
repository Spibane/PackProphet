using System.Net.Http.Json;
using PackProphet.Data;
using PackProphet.Domain;

namespace PackProphet.Services;

/// <summary>Where the loaded card data came from, so the UI can be honest about staleness.</summary>
public enum DataSource { Cdn, VendoredSnapshot }

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
public sealed record CardData(
    CardIndex Index, PullRates Rates, CardFacts Facts, SetCatalog Sets, PackArtCatalog PackArt,
    IReadOnlyDictionary<string, Rarity> Rarities,
    DataSource Source, string? Version,
    IReadOnlyCollection<string> VendoredArtSets);

/// <summary>
/// Loads the card database, preferring the live CDN so newly released sets appear without a
/// redeploy, and falling back to the snapshot vendored in wwwroot. A CDN failure is a downgrade
/// rather than an error, so the app never boots to an empty state.
/// </summary>
public sealed class CardDataLoader
{
    private const string Cdn = "https://cdn.jsdelivr.net/npm/pokemon-tcg-pocket-database/dist";
    private const string Local = "data/snapshot";

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
    private CardData? _cached;

    /// <param name="artManifestDeadline">
    /// How long to wait for the art manifest before giving up on it. Defaults to
    /// <see cref="DefaultArtManifestDeadline"/>, which is what the app uses; a caller supplies its
    /// own only where the wall clock is not a measure of anything, which in practice means tests.
    /// </param>
    public CardDataLoader(HttpClient http, TimeSpan? artManifestDeadline = null)
    {
        _http = http;
        _artManifestDeadline = artManifestDeadline ?? DefaultArtManifestDeadline;
    }

    /// <summary>
    /// Fetch JSON, putting a deadline on anything that leaves the origin. Local reads are left
    /// alone: they are files the app shipped with, and a slow parse on a phone is not a hang.
    ///
    /// The deadline covers the EXCHANGE and stops there, which is the distinction this method
    /// exists to make. It used to hand the deadline's token to GetFromJsonAsync, and that call
    /// deserialises under the same token — so the clock ran through the parse as well as the
    /// download, and the parse is the slow half by two orders of magnitude. This app publishes
    /// without AOT, so System.Text.Json runs in the IL interpreter: 3,879 cards is 470 KB that
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

    private sealed record VendoredArt(List<string>? Sets, List<string>? Packs);

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
    private async Task<IReadOnlyCollection<string>> ReadArtManifestAsync(CancellationToken ct)
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

        ArtSource.UseVendored(manifest?.Sets, manifest?.Packs);

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
        return manifest?.Sets?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() ?? [];
    }

    public async Task<CardData> LoadAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        var art = await ReadArtManifestAsync(ct);

        var loaded = await TryLoadAsync(Cdn, DataSource.Cdn, ct)
            ?? await TryLoadAsync(Local, DataSource.VendoredSnapshot, ct)
            ?? throw new InvalidOperationException(
                "Neither the CDN nor the vendored snapshot could be loaded.");

        return _cached = loaded with { VendoredArtSets = art };
    }

    private async Task<CardData?> TryLoadAsync(string root, DataSource source, CancellationToken ct)
    {
        try
        {
            var cards = await GetAsync<List<PocketCard>>(root, $"{root}/cards.min.json", CdnDeadline, ct);
            var rarities = await GetAsync<Dictionary<string, Rarity>>(root, $"{root}/rarities.json", CdnDeadline, ct);
            var rates = await GetAsync<Dictionary<string, Dictionary<string, PackVariant>>>(
                root, $"{root}/pullRates.json", CdnDeadline, ct);

            if (cards is null or { Count: 0 } || rarities is null or { Count: 0 } || rates is null)
                return null;

            var version = source == DataSource.VendoredSnapshot
                ? await TryReadVersionAsync(root, ct)
                : null;

            var index = new CardIndex(cards, rarities);
            var published = await TryLoadSetsAsync(root, ct);
            var catalog = new SetCatalog(published, index.BySet.Keys);

            // Small (about 20 KB) and it decides which booster image every tile shows, so it is
            // loaded up front unlike the card detail.
            var packArt = new PackArtCatalog(await TryLoadExpansionsAsync(root, ct), index.AllPackKeys);

            // Card detail is not loaded here. It is 4.4 MB against about 500 KB for everything
            // else, and parsing 3,761 nested records in the WebAssembly interpreter takes long
            // enough to look like the app has hung. It is enrichment — attacks and abilities —
            // rather than something a screen needs to function, so LoadFactsAsync fetches it
            // afterwards and folds it in.
            // VendoredArtSets is filled in by LoadAsync, which read the manifest before either
            // source was tried: it is a fact about the deployment rather than about which of the
            // two data sources answered.
            return new CardData(index, new PullRates(rates), CardFacts.Empty, catalog, packArt,
                                rarities, source, version, []);
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
