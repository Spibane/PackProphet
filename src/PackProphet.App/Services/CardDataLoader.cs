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
public sealed record CardData(
    CardIndex Index, PullRates Rates, CardFacts Facts, SetCatalog Sets, PackArtCatalog PackArt,
    IReadOnlyDictionary<string, Rarity> Rarities,
    DataSource Source, string? Version);

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
    /// Card detail comes from a different project to the rest, so it has its own URL rather than
    /// sitting under the same root. See NOTICE.md: it is AGPL-3.0-or-later, which is why this
    /// project is.
    /// </summary>
    private const string FactsCdn =
        "https://cdn.jsdelivr.net/gh/chase-mew/pokemon-tcg-pocket-cards@main/data/v5/cards.min.json";

    private static string FactsUrl(string root) =>
        root == Local ? $"{root}/cards.v5.json" : FactsCdn;

    private readonly HttpClient _http;
    private CardData? _cached;

    public CardDataLoader(HttpClient http) => _http = http;

    public async Task<CardData> LoadAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        var fromCdn = await TryLoadAsync(Cdn, DataSource.Cdn, ct);
        return _cached = fromCdn ?? await TryLoadAsync(Local, DataSource.VendoredSnapshot, ct)
            ?? throw new InvalidOperationException(
                "Neither the CDN nor the vendored snapshot could be loaded.");
    }

    private async Task<CardData?> TryLoadAsync(string root, DataSource source, CancellationToken ct)
    {
        try
        {
            var cards = await _http.GetFromJsonAsync<List<PocketCard>>($"{root}/cards.min.json", ct);
            var rarities = await _http.GetFromJsonAsync<Dictionary<string, Rarity>>($"{root}/rarities.json", ct);
            var rates = await _http.GetFromJsonAsync<Dictionary<string, Dictionary<string, PackVariant>>>(
                $"{root}/pullRates.json", ct);

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
            return new CardData(index, new PullRates(rates), CardFacts.Empty, catalog, packArt,
                                rarities, source, version);
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

        try { return await _http.GetFromJsonAsync<List<ExpansionInfo>>(url, ct); }
        catch { return null; }   // falls back to the lower-resolution art
    }

    private async Task<Dictionary<string, List<SetInfo>>?> TryLoadSetsAsync(
        string root, CancellationToken ct)
    {
        // Optional: without it the catalogue derives series from set codes instead, which is
        // a slightly worse grouping rather than a broken app.
        try { return await _http.GetFromJsonAsync<Dictionary<string, List<SetInfo>>>($"{root}/sets.json", ct); }
        catch { return null; }
    }

    /// <summary>
    /// Fetch card detail (attacks, abilities, stage, stats). Separate from <see cref="LoadAsync"/>
    /// and called after the UI is usable: it is the largest payload and none of it is needed to
    /// render a collection.
    ///
    /// Returns <see cref="CardFacts.Empty"/> on failure, so a fetch that cannot be completed costs
    /// some columns rather than the app.
    /// </summary>
    public async Task<CardFacts> LoadFactsAsync(CancellationToken ct = default)
    {
        // Local first. The vendored copy carries only the fields this app reads — 1.5 MB against
        // the upstream 4.4 MB — so it parses roughly three times faster. The CDN is the fallback,
        // and the reason a brand-new set still gets detail without a redeploy.
        foreach (var root in new[] { Local, Cdn })
        {
            try
            {
                var facts = await _http.GetFromJsonAsync<List<CardFact>>(FactsUrl(root), ct);
                if (facts is { Count: > 0 }) return new CardFacts(facts);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // try the other source
            }
        }
        return CardFacts.Empty;
    }

    private async Task<string?> TryReadVersionAsync(string root, CancellationToken ct)
    {
        try { return (await _http.GetStringAsync($"{root}/VERSION", ct)).Trim(); }
        catch { return null; }
    }
}
