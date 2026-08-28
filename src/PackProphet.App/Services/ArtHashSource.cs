using PackProphet.Vision;

namespace PackProphet.Services;

/// <summary>
/// Fetches the card art fingerprint table, once, the first time a screenshot import is attempted.
///
/// Deliberately not fetched at boot. It is 150 KB of text that every other page in the app has no
/// use for, and the same reasoning already keeps the 4.4 MB of card detail off the boot path — see
/// <see cref="CardDataLoader"/>.
///
/// To be precise about what that does and does not save: the request and the parse are deferred
/// until someone opens the import, but the file is still precached by the service worker along with
/// everything else under data/, so a first visit does pay for it. That is a deliberate trade the
/// other way — recognising a card is a comparison against this file and nothing else, so a
/// screenshot import is the most thoroughly offline thing the app does, and it would be absurd for
/// it to be the one feature that needs a network.
///
/// The table ships with the build rather than being fetched from a CDN, which is what makes it the
/// one piece of card data in the app that cannot be newer than the deploy. That asymmetry is
/// visible to users and has to be explained rather than hidden — <see cref="ArtHashCoverage"/> does
/// the explaining.
/// </summary>
public sealed class ArtHashSource
{
    private const string Path = "data/card-hashes.txt";

    private readonly HttpClient _http;
    private Task<ArtHashTable>? _load;

    public ArtHashSource(HttpClient http) => _http = http;

    /// <summary>
    /// The table, or an empty one if it could not be fetched. Memoised on the task, so two imports
    /// started in quick succession share one request — and a failed load is remembered as a failure
    /// rather than retried on every keystroke.
    /// </summary>
    public Task<ArtHashTable> GetAsync(CancellationToken ct = default) => _load ??= LoadAsync(ct);

    private async Task<ArtHashTable> LoadAsync(CancellationToken ct)
    {
        try
        {
            return ArtHashTable.Parse(await _http.GetStringAsync(Path, ct));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Empty rather than thrown. The reader turns an empty table into a sentence the user
            // can act on, and an exception here would surface as a stack trace on a page they
            // reached by picking a file.
            return ArtHashTable.Empty;
        }
    }
}
