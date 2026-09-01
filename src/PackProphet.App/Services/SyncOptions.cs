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
    /// True when this build has somewhere to sync to. False hides the feature rather than offering
    /// a button that cannot work -- a fork of this repo gets a working app, not a broken setting.
    /// </summary>
    public bool Configured =>
        !string.IsNullOrWhiteSpace(Url)
        && !string.IsNullOrWhiteSpace(AnonKey)
        && !Url.Contains("YOUR-PROJECT", StringComparison.OrdinalIgnoreCase);
}
