namespace PackProphet.Services;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>What is stored remotely, as it comes back.</summary>
public sealed record RemoteDoc(
    string Payload,
    string Nonce,
    string Writer,
    long Version,
    DateTimeOffset UpdatedAt);

/// <summary>Why a push did not happen. Each one needs a different response from the caller.</summary>
public enum PushOutcome
{
    Stored,

    /// <summary>
    /// Another device wrote between our pull and our push. Not an error: the caller pulls again,
    /// merges what it now knows, and retries. Sync would lose data without this.
    /// </summary>
    Superseded,

    /// <summary>The document was swept away as abandoned. The pairing has to be remade.</summary>
    Gone,

    /// <summary>Right id, wrong code. Should be unreachable, since both derive from the same code.</summary>
    Refused,

    /// <summary>No network, or the host is down. The local collection is unaffected.</summary>
    Unreachable,

    /// <summary>Bigger than the host will accept.</summary>
    TooLarge,

    /// <summary>
    /// Written to too recently. The host enforces a floor between writes to one document, because
    /// every other throttle in this app is in the browser and therefore advisory. Like
    /// <see cref="Superseded"/> it is not a lost edit -- the caller waits and comes back with the
    /// same state -- but it needs a longer wait and no second merge.
    /// </summary>
    TooFast,

    /// <summary>
    /// We pushed believing nothing was stored, and something is. Not a race -- no other device has
    /// to have written for this to happen -- so it is told apart from <see cref="Superseded"/>:
    /// the pull that came back empty is the thing that was wrong, and a retry only helps if the
    /// document really did appear between the two calls.
    /// </summary>
    AlreadyThere,
}

public sealed record PushResult(PushOutcome Outcome, long Version = 0);

/// <summary>
/// The three calls this app makes to Supabase, and nothing else.
///
/// Only stored procedures, never the table: the published anon key can reach the functions in
/// db/sync.sql and cannot reach a row directly, so the worst an attacker with the key and no
/// pairing code can do is ask about documents they cannot name.
/// </summary>
public sealed class SyncTransport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SyncOptions _options;
    private readonly HttpClient _http;

    /// <summary>
    /// Its own HttpClient rather than the app's: the app's carries a BaseAddress of the site and no
    /// headers, and every request here needs the project key on it.
    /// </summary>
    /// <param name="handler">
    /// Substituted by tests. The outcome mapping below is the part of sync most worth covering and
    /// the part hardest to reach through a real server, since it is entirely about what a Postgres
    /// error means.
    /// </param>
    public SyncTransport(SyncOptions options, HttpMessageHandler? handler = null)
    {
        _options = options;
        _http = (handler is null ? new HttpClient() : new HttpClient(handler));
        _http.Timeout = TimeSpan.FromSeconds(20);

        if (!options.Configured) return;

        _http.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/rest/v1/rpc/");
        _http.DefaultRequestHeaders.Add("apikey", options.AnonKey);
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.AnonKey}");
    }

    /// <summary>
    /// Null when there is nothing stored under this id, which is the ordinary first pull.
    ///
    /// Throws on a network failure rather than returning null, because those two have to be told
    /// apart: "no document" means push this device's collection up, and "unreachable" must leave
    /// everything alone. Callers catch it.
    /// </summary>
    public async Task<RemoteDoc?> PullAsync(SyncKeys keys, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            "sync_pull", new { doc_id = keys.Id, doc_auth = keys.Auth }, Json, ct);

        response.EnsureSuccessStatusCode();

        // A table-returning function answers with an array; no document is an empty one rather
        // than a 404.
        var rows = await response.Content.ReadFromJsonAsync<List<RemoteDoc>>(Json, ct);
        return rows is { Count: > 0 } ? rows[0] : null;
    }

    /// <summary>
    /// Store a blob, but only if the document is still on the version we based it on. Pass null for
    /// <paramref name="expectedVersion"/> to mean "I believe nothing is stored here yet".
    /// </summary>
    public async Task<PushResult> PushAsync(
        SyncKeys keys,
        SealedState sealedState,
        string writer,
        long? expectedVersion,
        CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("sync_push", new
            {
                doc_id = keys.Id,
                doc_auth = keys.Auth,
                doc_payload = sealedState.Payload,
                doc_nonce = sealedState.Nonce,
                doc_writer = writer,
                expected_version = expectedVersion,
            }, Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new PushResult(PushOutcome.Unreachable);
        }

        if (response.IsSuccessStatusCode)
        {
            var version = await response.Content.ReadFromJsonAsync<long>(Json, ct);
            return new PushResult(PushOutcome.Stored, version);
        }

        // The functions raise with deliberate SQLSTATEs so the client can tell a race from a
        // refusal without matching on message text, which would break the day the wording changes.
        var body = await response.Content.ReadAsStringAsync(ct);
        var code = CodeOf(body);

        return new PushResult(code switch
        {
            "40001" => PushOutcome.Superseded,
            "P0002" => PushOutcome.Gone,
            "42501" => PushOutcome.Refused,
            "22001" => PushOutcome.TooLarge,
            "53400" => PushOutcome.TooFast,
            "55000" => PushOutcome.AlreadyThere,
            _ => PushOutcome.Unreachable,
        });
    }

    /// <summary>Delete the remote copy. False if there was nothing to delete.</summary>
    public async Task<bool> ForgetAsync(SyncKeys keys, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "sync_forget", new { doc_id = keys.Id, doc_auth = keys.Auth }, Json, ct);

            if (!response.IsSuccessStatusCode) return false;
            return await response.Content.ReadFromJsonAsync<bool>(Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static string? CodeOf(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
