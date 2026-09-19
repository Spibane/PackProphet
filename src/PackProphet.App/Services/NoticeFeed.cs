using PackProphet.Data;

namespace PackProphet.Services;

/// <summary>
/// Fetches the authored notice feed, once, after the app is already usable.
///
/// Deliberately off the boot path. It is a few hundred bytes from a third party, and the two
/// things it can say — "there is an outage" and "a pack launched that the database has not caught
/// up with" — are both worth showing a second late and neither is worth holding first paint for.
/// <see cref="CardDataLoader.ReadArtManifestAsync"/> is awaited because it decides every tile's
/// image; this decides one bar, so it is fired off and folded in when it lands.
///
/// Absent, malformed, blocked or slow all mean the same thing: no authored notices. There is
/// nobody to report a failure to — the reader of this app is not the author of that file.
/// </summary>
public sealed class NoticeFeed
{
    /// <summary>
    /// Short, and for the same reason the art manifest's is: nothing waits on this, and its
    /// failure mode is the state most builds are in anyway. A hang must end.
    /// </summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How coarsely the cache-busting stamp moves.
    ///
    /// A gist's raw URL is served through GitHub's CDN with its own caching, so the same URL can
    /// keep answering with yesterday's text. A stamp in the query makes the URL change, and the
    /// granularity is the trade: fine enough that an outage notice is live within minutes, coarse
    /// enough that repeat visits inside that window still hit a cache rather than the origin.
    ///
    /// GitHub ignores query parameters it does not know, so this is purely a cache key.
    ///
    /// MEASURED, NOT PRECAUTIONARY
    /// ----------------------------------------------------------------------------------
    /// Checked against a real gist immediately after an edit: the bare raw URL answered with the
    /// PREVIOUS contents, and the same URL with a stamp answered with the new ones. The response
    /// carries `cache-control: max-age=300`, which is where five minutes comes from — a finer step
    /// buys nothing, because it is the edge's window that is being stepped over.
    ///
    /// Without this, an edit is invisible for up to five minutes, and that is the one window an
    /// outage notice exists to be seen in.
    /// </summary>
    private static readonly TimeSpan StampStep = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly NoticeOptions _options;
    private Task? _load;

    public NoticeFeed(HttpClient http, NoticeOptions options)
    {
        _http = http;
        _options = options;
    }

    /// <summary>
    /// What the feed said. Empty until it has answered, and empty forever if it never does, so a
    /// caller never has to distinguish "not yet" from "nothing" — both render no bar.
    /// </summary>
    public IReadOnlyList<SiteNotice> Notices { get; private set; } = [];

    /// <summary>True once the fetch has finished, however it finished.</summary>
    public bool Read { get; private set; }

    /// <summary>
    /// Fetch it, at most once per app lifetime. Memoised on the task, so two callers share one
    /// request and a failure is remembered as a failure rather than retried on every render.
    /// </summary>
    public Task LoadAsync(CancellationToken ct = default) => _load ??= LoadCoreAsync(ct);

    private async Task LoadCoreAsync(CancellationToken ct)
    {
        if (!_options.Configured)
        {
            // No feed configured: settled without a request, which is what a fork of this repo
            // and every test run should cost.
            Read = true;
            return;
        }

        string? json = null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Deadline);

            json = await _http.GetStringAsync(Url(), cts.Token);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Blocked by CSP, offline, 404, deadline. All the same answer.
        }

        // Local date, matching how the author of the feed thinks about "until Friday".
        Notices = NoticeFeedReader.Parse(json, DateOnly.FromDateTime(DateTime.Now));
        Read = true;
    }

    private string Url()
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (long)StampStep.TotalSeconds;
        var join = _options.GistUrl.Contains('?') ? '&' : '?';
        return $"{_options.GistUrl}{join}t={stamp}";
    }
}
