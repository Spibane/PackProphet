namespace PackProphet.App.Tests;

using System.Text.RegularExpressions;

/// <summary>
/// What the published service worker will and will not precache.
///
/// This file is the one part of the app that never runs in development — the dev worker is a no-op
/// — and never runs in CI either. Its only execution is on a real visitor's first load.
///
/// The rules are read out of the JavaScript rather than restated here, so the two cannot drift.
/// Precaching too much shows up only as a slow first visit, and precaching too little only as a
/// broken offline mode.
/// </summary>
public class ServiceWorkerPrecacheTests
{
    private static readonly string Source =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "service-worker.published.js"));

    /// <summary>
    /// Pulls the regex literals out of one of the worker's two lists. JavaScript and .NET agree on
    /// the syntax actually used here — character classes, anchors, and one negative lookahead.
    /// </summary>
    private static Regex[] Patterns(string name)
    {
        var list = Regex.Match(Source, $@"const {name} = \[(?<body>.*?)\];", RegexOptions.Singleline);
        Assert.True(list.Success, $"could not find {name} in the service worker");

        // Comments first, or their own slashes are read as pattern delimiters: the line
        // "and the grid/reboot/utilities" yielded a regex of "and the grid". That one was
        // harmless because it matches no URL, but a comment mentioning a real path would
        // silently add an exclusion the worker does not have.
        var body = string.Join("\n", list.Groups["body"].Value
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        return Regex.Matches(body, @"/(?<re>(?:[^/\\\n]|\\.)+)/")
            .Select(m => new Regex(m.Groups["re"].Value))
            .ToArray();
    }

    private static bool Precached(string url)
    {
        var include = Patterns("offlineAssetsInclude");
        var exclude = Patterns("offlineAssetsExclude");

        return include.Any(p => p.IsMatch(url)) && !exclude.Any(p => p.IsMatch(url));
    }

    [Theory]
    // Everything the app loads has to survive, or offline support does not work.
    [InlineData("_framework/PackProphet.ux03ahoajj.wasm")]
    [InlineData("_framework/dotnet.native.lore6p3j3e.wasm")]
    [InlineData("index.html")]
    [InlineData("css/app.css")]
    [InlineData("js/gridkeys.js")]
    [InlineData("lib/bootstrap/dist/css/bootstrap.min.css")]
    [InlineData("lib/bootstrap/dist/js/bootstrap.bundle.min.js")]
    // The vendored snapshot: without it, a first visit with no network shows an empty app.
    [InlineData("data/snapshot/cards.min.json")]
    [InlineData("data/snapshot/VERSION")]
    // Kept despite being loaded only on demand: importing a deck from a screenshot should still
    // work with no connection.
    [InlineData("js/vendor-jsQR.js")]
    // Likewise the screenshot import, which is the one feature in the app that does its whole job
    // locally — recognising a card is a comparison against this file, so leaving it out would make
    // the most obviously offline feature the one that needs a network.
    [InlineData("js/cardshot.js")]
    [InlineData("data/card-hashes.txt")]
    public void Is_precached(string url) => Assert.True(Precached(url), $"{url} must be precached");

    [Theory]
    // Bootstrap is vendored whole and index.html links two files out of it.
    [InlineData("lib/bootstrap/dist/js/bootstrap.min.js")]
    [InlineData("lib/bootstrap/dist/js/bootstrap.js")]
    [InlineData("lib/bootstrap/dist/js/bootstrap.esm.min.js")]
    [InlineData("lib/bootstrap/dist/css/bootstrap.css")]
    [InlineData("lib/bootstrap/dist/css/bootstrap.rtl.min.css")]
    [InlineData("lib/bootstrap/dist/css/bootstrap-grid.min.css")]
    [InlineData("lib/bootstrap/dist/css/bootstrap-reboot.min.css")]
    // Caching the worker itself would pin the old one forever.
    [InlineData("service-worker.js")]
    public void Is_not_precached(string url) =>
        Assert.False(Precached(url), $"{url} must not be precached");

    [Fact]
    public void Source_maps_are_left_out()
    {
        // True today only because .map matches no include pattern, not because anything excludes
        // it — and there is about 700 KB of them. Pinned so a widened include pattern cannot
        // quietly pull them in.
        Assert.False(Precached("lib/bootstrap/dist/css/bootstrap.css.map"));
        Assert.False(Precached("lib/bootstrap/dist/js/bootstrap.bundle.min.js.map"));
    }

    [Fact]
    public void The_extractor_would_notice_if_the_lists_moved()
    {
        // A guard on the guard: if either list is renamed or reshaped, these tests must fail loudly
        // rather than silently classifying everything as not-precached.
        Assert.NotEmpty(Patterns("offlineAssetsInclude"));
        Assert.NotEmpty(Patterns("offlineAssetsExclude"));
        Assert.Contains(Patterns("offlineAssetsExclude"), p => p.ToString().Contains("bootstrap"));
    }
}
