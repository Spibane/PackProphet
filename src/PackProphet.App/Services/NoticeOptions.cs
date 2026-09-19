namespace PackProphet.Services;

/// <summary>
/// Where the authored notice feed lives, read from wwwroot/appsettings.json at runtime rather than
/// compiled in — so posting a notice does not mean a deploy, which is the entire point of having
/// the channel.
///
/// A gist, and not a file in this repository, for one reason: the service worker precaches
/// everything in the published output that matches <c>/\.json$/</c> and then serves it cache-first
/// forever. A committed notice.json would be frozen at whichever build the visitor first installed
/// and could never announce anything. (<c>art/index.json</c> escapes that only because the deploy
/// workflow writes it AFTER <c>dotnet publish</c>, so it never reaches
/// service-worker-assets.js — a file the worker has no hash for is a file it does not precache.)
///
/// One caveat the config file cannot express on its own, the same one <see cref="SyncOptions"/>
/// carries: index.html's connect-src has to name the host, or the browser blocks the request
/// before it is made. The two move together.
/// </summary>
public sealed class NoticeOptions
{
    /// <summary>
    /// The gist's raw URL, e.g.
    /// <c>https://gist.githubusercontent.com/you/1234abcd/raw/notice.json</c>.
    ///
    /// Without the revision SHA, so it always serves the latest edit. With one it would serve the
    /// revision and never change, which is the opposite of what this is for.
    ///
    /// Blank makes no request at all, which is the right state for a fork of this repo and the
    /// state every test runs in.
    /// </summary>
    public string GistUrl { get; set; } = "";

    /// <summary>
    /// True when this build has a feed to read. The placeholder counts as unconfigured, so the
    /// committed appsettings.json can document the shape without anyone's app trying to fetch it.
    /// </summary>
    public bool Configured =>
        !string.IsNullOrWhiteSpace(GistUrl)
        && !GistUrl.Contains("YOUR-GIST", StringComparison.OrdinalIgnoreCase);
}
