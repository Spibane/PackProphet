using System.Collections.Concurrent;
using System.Text.Json;
using PackProphet.Domain;
using PackProphet.Vision;
using SkiaSharp;

namespace PackProphet.Tools;

/// <summary>
/// Generates the card art fingerprints that wwwroot/data/card-hashes.txt holds, by downloading every
/// card's artwork once and reducing each one to 128 bits.
///
/// This runs offline — from a maintainer's machine or from .github/workflows/card-hashes.yml — and
/// never in a browser. That division is the point of the whole design: recognising a card on the
/// device costs a Hamming distance against a 90 KB table, because the expensive half was paid for
/// here, once, and committed.
///
/// The table is merged rather than replaced. An art file that 404s today, or a CDN having a bad
/// afternoon, must not remove a card the app can currently recognise, so a run that fetches less
/// than the table already holds keeps the difference and says so — and refuses outright if it lost
/// enough to look like an outage rather than a change upstream.
/// </summary>
internal static class Program
{
    private const string CardsUrl =
        "https://cdn.jsdelivr.net/npm/pokemon-tcg-pocket-database/dist/cards.min.json";

    private const string DefaultOut = "src/PackProphet.App/wwwroot/data/card-hashes.txt";

    /// <summary>
    /// Simultaneous downloads. Eight is polite to a public CDN and still finishes 3,761 files in a
    /// couple of minutes; the run is not the thing anyone is waiting on.
    /// </summary>
    private const int Concurrency = 8;

    /// <summary>
    /// A run that ends up with less than this share of what the table already had is treated as a
    /// failed run, not a smaller table. Sets do get restructured upstream, but not by a third in an
    /// afternoon — that shape of loss is a network or CDN fault, and writing it out would quietly
    /// break recognition for cards that were working.
    /// </summary>
    private const double RegressionFloor = 0.67;

    private static async Task<int> Main(string[] args)
    {
        var outPath = Arg(args, "--out") ?? DefaultOut;
        var cardsSource = Arg(args, "--cards") ?? CardsUrl;
        var only = Arg(args, "--set");
        var reportPath = Arg(args, "--report");

        // The mode the scheduled workflow runs in. Downloading 3,761 images to discover that 120 of
        // them are new is the whole cost of the job, and skipping what is already fingerprinted
        // turns a weekly two-minute run into a weekly two-second one.
        var onlyMissing = args.Contains("--only-missing");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PackProphet-CardHashGen/1.0");

        Console.WriteLine($"cards:  {cardsSource}");
        var cards = await LoadCardsAsync(http, cardsSource);
        if (cards is null or { Count: 0 })
        {
            Console.Error.WriteLine("::error::the card list could not be read");
            return 1;
        }

        var wanted = cards
            .Where(c => only is null || c.Set.Equals(only, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(c => c.Key)
            .OrderBy(c => c.Set, StringComparer.Ordinal).ThenBy(c => c.Number)
            .ToArray();

        Console.WriteLine($"cards:  {wanted.Length} entries across {wanted.Select(c => c.Set).Distinct().Count()} sets");

        var existing = ReadExisting(outPath);
        Console.WriteLine($"table:  {existing.Count} fingerprints already committed");

        var todo = onlyMissing
            ? wanted.Where(c => !existing.ContainsKey(c.Key)).ToArray()
            : wanted;

        if (onlyMissing)
            Console.WriteLine($"todo:   {todo.Length} without a fingerprint"
                              + (todo.Length == 0 ? " — nothing to do" : ""));

        var fetched = await FingerprintAllAsync(http, todo);
        Console.WriteLine($"fetched: {fetched.Count} of {todo.Length}");

        // Everything previously known, with this run's results laid over the top. A card whose art
        // has been redrawn upstream gets the new fingerprint; a card whose art failed to download
        // keeps the old one.
        var merged = new Dictionary<string, ArtHashEntry>(existing);
        foreach (var (key, entry) in fetched) merged[key] = entry;

        if (only is null && !onlyMissing && existing.Count > 0
            && fetched.Count < existing.Count * RegressionFloor)
        {
            Console.Error.WriteLine(
                $"::error::only {fetched.Count} of {existing.Count} known fingerprints could be "
                + "fetched. That is an outage, not a change upstream — nothing was written.");
            return 1;
        }

        var table = new ArtHashTable(merged.Values, null);
        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        await File.WriteAllTextAsync(outPath, table.Serialize(stamp));

        Report(table, wanted);
        if (reportPath is not null) await WriteReportAsync(reportPath, table, wanted, fetched.Count, stamp);

        Console.WriteLine($"wrote:  {outPath} — {table.Count} fingerprints, stamped {stamp}");
        return 0;
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static async Task<List<PocketCard>?> LoadCardsAsync(HttpClient http, string source)
    {
        var json = File.Exists(source)
            ? await File.ReadAllTextAsync(source)
            : await http.GetStringAsync(source);

        return JsonSerializer.Deserialize<List<PocketCard>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    /// <summary>Whatever is already committed, so a partial run is a merge rather than a truncation.</summary>
    private static Dictionary<string, ArtHashEntry> ReadExisting(string path)
    {
        if (!File.Exists(path)) return [];

        var table = ArtHashTable.Parse(File.ReadAllText(path));
        var byKey = new Dictionary<string, ArtHashEntry>();

        // Re-parsed through the same reader the app uses, which means a table this tool cannot read
        // is a table the app cannot read either, and is better rebuilt than preserved.
        foreach (var entry in table.Entries) byKey[entry.Key] = entry;

        return byKey;
    }

    private static async Task<Dictionary<string, ArtHashEntry>> FingerprintAllAsync(
        HttpClient http, PocketCard[] cards)
    {
        var results = new ConcurrentDictionary<string, ArtHashEntry>();
        var failures = new ConcurrentBag<string>();
        var done = 0;

        await Parallel.ForEachAsync(
            cards,
            new ParallelOptions { MaxDegreeOfParallelism = Concurrency },
            async (card, ct) =>
            {
                var hash = await FingerprintAsync(http, card, ct);
                if (hash is not null) results[card.Key] = new ArtHashEntry(card.Set, card.Number, hash.Value);
                else failures.Add(card.Key);

                var n = Interlocked.Increment(ref done);
                if (n % 250 == 0) Console.WriteLine($"        {n}/{cards.Length}…");
            });

        if (!failures.IsEmpty)
        {
            var sample = failures.OrderBy(k => k, StringComparer.Ordinal).Take(12);
            Console.WriteLine($"missed: {failures.Count} — {string.Join(", ", sample)}"
                              + (failures.Count > 12 ? ", …" : ""));
        }

        return new Dictionary<string, ArtHashEntry>(results);
    }

    /// <summary>
    /// One card. Two attempts: the art CDN serves the odd 503 under load, and a card lost to that
    /// rather than to not existing is worth the second request.
    /// </summary>
    private static async Task<ArtHash?> FingerprintAsync(
        HttpClient http, PocketCard card, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(card.ArtUrl, ct);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                return Fingerprint(bytes);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                if (attempt == 1) return null;
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes the art and reduces it to luma, which is all <see cref="ArtSampler"/> wants.
    ///
    /// Composited onto black rather than dropped, because a card image with an alpha channel would
    /// otherwise fingerprint from undefined colour in its transparent regions. Black matches what the
    /// browser sees: a canvas starts transparent and <c>getImageData</c> reports premultiplied zero
    /// there, so the two halves agree on what "nothing" looks like.
    /// </summary>
    private static ArtHash? Fingerprint(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null) return null;

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height,
                                   SKColorType.Rgba8888, SKAlphaType.Premul);
        if (info.Width < 8 || info.Height < 8) return null;

        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            return null;

        var pixels = bitmap.GetPixelSpan();
        var luma = new byte[info.Width * info.Height];
        for (var i = 0; i < luma.Length; i++)
        {
            var p = i * 4;
            luma[i] = ArtSampler.Luma(pixels[p], pixels[p + 1], pixels[p + 2]);
        }

        return ArtSampler.Fingerprint(luma, info.Width, info.Height);
    }

    /// <summary>
    /// The same coverage figures as JSON, for the workflow that opens the pull request. Its body
    /// should say which sets became recognisable and how completely, and that is not something to
    /// reconstruct by parsing a log.
    /// </summary>
    private static async Task WriteReportAsync(
        string path, ArtHashTable table, PocketCard[] cards, int fetchedNow, string stamp)
    {
        var sets = cards
            .GroupBy(c => c.Set)
            .Select(g => new { set = g.Key, have = table.Covered(g.Key), want = g.Count() })
            .OrderBy(s => s.set, StringComparer.Ordinal)
            .ToArray();

        var report = new { generated = stamp, fingerprints = table.Count, fetchedNow, sets };
        await File.WriteAllTextAsync(
            path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Per-set coverage, so an incomplete run is visible in the log rather than only in the file.</summary>
    private static void Report(ArtHashTable table, PocketCard[] cards)
    {
        var gaps = cards
            .GroupBy(c => c.Set)
            .Select(g => (Set: g.Key, Have: table.Covered(g.Key), Want: g.Count()))
            .Where(s => s.Have < s.Want)
            .OrderBy(s => s.Set, StringComparer.Ordinal)
            .ToArray();

        if (gaps.Length == 0) { Console.WriteLine("cover:  every card in every set"); return; }

        Console.WriteLine("cover:  incomplete —");
        foreach (var (set, have, want) in gaps)
            Console.WriteLine($"          {set,-10} {have,4}/{want}");
    }
}
