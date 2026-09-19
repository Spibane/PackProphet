using System.Text.Json;
using System.Text.Json.Serialization;

namespace PackProphet.Data;

/// <summary>One entry as written in the gist. Every field optional, because a human types this.</summary>
public sealed class FeedEntry
{
    public string? Id { get; set; }
    public string? Level { get; set; }
    public string? Text { get; set; }
    public string? Until { get; set; }
    public string? ActionLabel { get; set; }
    public string? ActionHref { get; set; }
}

/// <summary>
/// Turns the authored notice feed into notices, refusing anything it cannot vouch for.
///
/// WHY THIS IS A SEPARATE, PURE TYPE
/// ==================================================================================
/// The feed is a document on a third-party host that the app renders into its own chrome, which
/// makes it the only text in PackProphet that neither the build nor the user wrote. Everything
/// below is therefore a check rather than a convenience, and it lives here — away from the
/// HttpClient — so every refusal can be asserted without a network.
///
/// The feed exists for the things the app cannot work out for itself: an outage, an import that
/// has started failing, and the one new-pack case <see cref="DataLag"/> is blind to — a pack that
/// launches today whose CARDS the community database has not published either. The app cannot
/// know about a set it has never seen, so on the day of a launch that is the only channel there
/// is.
///
/// FAIL CLOSED, ALWAYS
/// ==================================================================================
/// Every ambiguity resolves to "show nothing". A banner is a small thing to lose and the wrong
/// banner is on every page of the app until somebody notices, so a malformed entry is dropped
/// rather than guessed at. The specific traps, all of which this refuses:
///
///   * a link with a scheme that executes. <c>javascript:</c> in ActionHref would be a script in
///     the app's own chrome, reached by a document nothing in the build signs.
///   * a protocol-relative <c>//host/path</c>, which reads as a path and resolves as an origin.
///   * an <c>until</c> date that cannot be parsed. That is the field whose whole job is to make a
///     notice stop, so a date this cannot read drops the notice instead of publishing it forever.
///   * an entry with no id, which could be dismissed but not REMEMBERED as dismissed, so it would
///     come back on every reload.
///   * length and count, so a runaway feed cannot paper over the app.
/// </summary>
public static class NoticeFeedReader
{
    /// <summary>
    /// How many notices the bar will take. One is shown at a time, so this is only a ceiling on
    /// how much is parsed and held.
    /// </summary>
    public const int MostNotices = 3;

    /// <summary>
    /// The longest a notice may be. Roughly two lines on a phone, which is the whole budget the
    /// bar has before it starts pushing the page it sits above off the screen.
    /// </summary>
    public const int LongestText = 300;

    /// <summary>The longest an action's label may be. It is a button, so this is generous.</summary>
    public const int LongestLabel = 30;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // A human edits this file in a browser. A trailing comma and a `// note` line are the two
        // mistakes they will make, and neither is a reason to drop a notice about an outage.
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Parse the feed. Returns empty for anything it cannot read, including valid JSON of the
    /// wrong shape — there is no error to report to, since the reader of this app is not the
    /// author of that file.
    /// </summary>
    /// <param name="today">
    /// Taken as an argument rather than read from the clock, so expiry is assertable. The caller
    /// passes the local date, matching how the author of the feed thinks about "until Friday".
    /// </param>
    public static IReadOnlyList<SiteNotice> Parse(string? json, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        List<FeedEntry?>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<FeedEntry?>>(json, Options);
        }
        catch (JsonException)
        {
            // Includes a bare object where a list was expected, which is the likeliest way to
            // write this file wrong. Documented in appsettings.json: it is a list, even for one.
            return [];
        }

        if (entries is null) return [];

        var notices = new List<SiteNotice>();

        foreach (var entry in entries)
        {
            if (notices.Count == MostNotices) break;
            if (Read(entry, today) is { } notice) notices.Add(notice);
        }

        return notices;
    }

    private static SiteNotice? Read(FeedEntry? entry, DateOnly today)
    {
        if (entry is null) return null;

        var id = entry.Id?.Trim();
        var text = entry.Text?.Trim();

        // No id, no dismissal that survives a reload, so the notice would be undismissable in
        // practice. Refused rather than given a generated key: a key derived from the text would
        // change the moment the author fixed a typo, and the notice would come back.
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(text)) return null;
        if (text.Length > LongestText) return null;

        if (!Expiry(entry.Until, today, out var live) || !live) return null;

        var (label, href) = Action(entry.ActionLabel, entry.ActionHref);

        return new SiteNotice(
            // Prefixed, so the author's id cannot collide with a derived key: DataLag's are
            // "waiting:..." and "offline", and an author writing `id: "offline"` would otherwise
            // inherit -- or silence -- the app's own outage notice.
            Key: "feed:" + id,
            Level: Level(entry.Level),
            Text: text,
            ActionLabel: label,
            ActionHref: href);
    }

    /// <summary>
    /// Whether the notice has expired, and whether the field could be read at all.
    ///
    /// Absent is fine and means "no end date". Present and unreadable is NOT fine: that field is
    /// the only thing that makes a notice stop on its own, so a date this cannot parse is refused
    /// rather than treated as absent. Misreading it the other way publishes a notice forever on
    /// the strength of a typo.
    ///
    /// Exact ISO only, matching <see cref="SetInfo.ReleasedOn"/> — this app builds with
    /// InvariantGlobalization, where a loose parse is a guess rather than a reading.
    /// </summary>
    private static bool Expiry(string? until, DateOnly today, out bool live)
    {
        live = false;

        if (string.IsNullOrWhiteSpace(until))
        {
            live = true;
            return true;
        }

        if (!DateOnly.TryParseExact(until.Trim(), "yyyy-MM-dd", out var date)) return false;

        // Inclusive: "until 2026-09-25" reads as through the 25th, which is what the author meant.
        live = today <= date;
        return true;
    }

    private static NoticeLevel Level(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "problem" or "error" or "danger" => NoticeLevel.Problem,
        "warning" or "warn" => NoticeLevel.Warning,

        // Anything else, including absent and misspelt. The quietest level is the safe default:
        // a real outage written with a typo in `level` still shows, just politely.
        _ => NoticeLevel.Info,
    };

    /// <summary>
    /// The action, or neither half of one.
    ///
    /// Both or nothing. A label with nowhere to go is a dead button and a link with no label has
    /// nothing to render, so a half-written action is dropped and the TEXT is still shown — the
    /// sentence is the part that matters, and refusing the whole notice over its link would lose
    /// the outage to a broken URL.
    /// </summary>
    private static (string? Label, string? Href) Action(string? label, string? href)
    {
        label = label?.Trim();
        href = href?.Trim();

        if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(href)) return (null, null);
        if (label.Length > LongestLabel) return (null, null);
        return Safe(href) ? (label, href) : (null, null);
    }

    /// <summary>
    /// Whether a URL from the feed may be put in an href.
    ///
    /// An allow-list, because the failure is a script running in the app's own chrome. Two shapes
    /// pass and nothing else does:
    ///
    ///   * an absolute https URL. Not http: this app is served over TLS and a mixed-content link
    ///     from a banner is not worth supporting.
    ///   * a relative in-app path, e.g. "packs" or "settings". These are resolved against the
    ///     app's base, which is why the leading-slash and protocol-relative forms are refused —
    ///     "//evil.example/x" reads as a path and resolves as an origin.
    ///
    /// A scheme is anything before the first colon, so a colon anywhere in what should be a
    /// relative path is enough to refuse it. That is stricter than the URL grammar and
    /// deliberately so: this is deciding, not parsing.
    /// </summary>
    private static bool Safe(string href)
    {
        if (href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(href, UriKind.Absolute, out var url)
                && url.Scheme == Uri.UriSchemeHttps;

        return !href.StartsWith('/')
            && !href.StartsWith('\\')
            && !href.Contains(':')
            && !href.Contains("//", StringComparison.Ordinal);
    }
}
