namespace PackProphet.Services;

/// <summary>
/// Where the encrypted blobs go, read from wwwroot/appsettings.json at runtime rather than
/// compiled in, so a fork can point at its own Supabase project without touching C#.
///
/// The anon key belongs in a public build. That is what it is for: it identifies the project and
/// grants nothing on its own, because the sync table has row-level security with no policies and
/// the only reachable surface is three functions that each demand a token derived from a pairing
/// code (see db/sync.sql).
///
/// One caveat the config file cannot express on its own: index.html's connect-src has to name the
/// same host, or the browser blocks every request before it is made. The two move together.
/// </summary>
public sealed class SyncOptions
{
    /// <summary>Project URL, e.g. https://abcdefgh.supabase.co. Blank turns sync off entirely.</summary>
    public string Url { get; set; } = "";

    /// <summary>The project's anonymous (publishable) key.</summary>
    public string AnonKey { get; set; } = "";

    /// <summary>
    /// A proxy in front of the project, e.g. https://packprophet-sync.someone.workers.dev.
    ///
    /// Set, and the app sends its three calls here and carries no key at all. That is the point:
    /// the key above has to be in the page for the app to reach Supabase directly, so anyone can
    /// read it and call the project as fast as they like, and a call spends the quota whether or
    /// not it succeeds. Nothing in the database can refuse a request that has already arrived.
    /// Behind a proxy the key is a secret the page does not hold, and requests over the rate are
    /// turned away before Supabase is involved.
    ///
    /// See workers/sync-proxy. Blank keeps the direct path, which is right for a fork that would
    /// rather not run one.
    /// </summary>
    public string ProxyUrl { get; set; } = "";

    /// <summary>True when the calls go through <see cref="ProxyUrl"/> rather than to Supabase.</summary>
    public bool Proxied => !string.IsNullOrWhiteSpace(ProxyUrl);

    /// <summary>
    /// True when this build has somewhere to sync to. False hides the feature rather than offering
    /// a button that cannot work -- a fork of this repo gets a working app, not a broken setting.
    /// </summary>
    public bool Configured =>
        Proxied
        || (!string.IsNullOrWhiteSpace(Url)
            && !string.IsNullOrWhiteSpace(AnonKey)
            && !Url.Contains("YOUR-PROJECT", StringComparison.OrdinalIgnoreCase));
}
