namespace PackProphet.App.Tests;

using System.Diagnostics;
using PackProphet.Services;

/// <summary>
/// The fallback to the vendored snapshot only runs when a CDN attempt returns. These cover the
/// case where it does not: a request that hangs instead of failing.
/// </summary>
public class CardDataLoaderTests
{
    /// <summary>Generous against the loader's own five-second deadline, so a slow CI box does not fail it.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The art-manifest deadline for every test that is not ABOUT the art-manifest deadline.
    ///
    /// The app ships three seconds, which is generous for a round trip to its own origin and not
    /// generous at all for three seconds of a machine running this suite in parallel with a dev
    /// server. When it fires, the loader reports "no vendored art" -- the same answer as the 404
    /// that legitimately means it -- so the manifest test failed about one run in twenty, alone,
    /// with an empty collection, and only ever when something else was using the CPU.
    ///
    /// A wall clock is not a measure of whether this code works, so these tests do not use one.
    /// </summary>
    private static readonly TimeSpan Unhurried = TimeSpan.FromSeconds(30);

    /// <summary>
    /// And the opposite, for the two tests whose subject IS the deadline. Explicit rather than
    /// leaning on whatever the app's default happens to be: they assert that a manifest which
    /// never arrives does not hold the boot open, and how long "never" waits is the point.
    /// </summary>
    private static readonly TimeSpan Impatient = TimeSpan.FromMilliseconds(250);

    private static CardDataLoader Loader(Func<CancellationToken, Task<HttpResponseMessage>>? onRemote,
                                        string? artManifest = null,
                                        TimeSpan? artDeadline = null) =>
        new(new HttpClient(new SnapshotHandler(onRemote, artManifest))
        {
            BaseAddress = new Uri("https://test.local/")
        }, artDeadline ?? Unhurried);

    /// <summary>A connection held open with no response — a network that drops packets to the CDN.</summary>
    private static async Task<HttpResponseMessage> Hang(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new UnreachableException();
    }

    [Fact]
    public async Task A_hanging_cdn_still_boots_from_the_snapshot()
    {
        var data = await Loader(Hang, artDeadline: Impatient).LoadAsync().WaitAsync(Patience);

        Assert.Equal(DataSource.VendoredSnapshot, data.Source);
        Assert.True(data.Index.All.Count > 3000, $"only {data.Index.All.Count} cards");
    }

    [Fact]
    public async Task The_art_manifest_decides_where_a_vendored_set_is_fetched_from()
    {
        // The link between the deploy and the app. The workflow writes this file; unless the
        // loader reads it, every card of the set that was vendored still asks the upstream CDN
        // first -- which is the gap the vendoring existed to close, so the whole feature would be
        // six megabytes of art nothing ever requests.
        //
        // Asserted on what this load reported rather than on ArtSource, which is process-wide:
        // every other test class boots a loader of its own and resets it, so a global assertion
        // here passes alone and races in the suite. What the URLs then look like is ArtSourceTests'
        // job. This is only the wiring.
        //
        // And it runs without the app's three-second manifest deadline, which is what actually made
        // this flake: under load it fired, the loader answered "no vendored art" exactly as it does
        // for a 404, and the assertion below failed on an empty collection.
        var data = await Loader(null, """{"sets":["B4a"],"packs":["Team Rocket"]}""").LoadAsync();

        Assert.Contains("B4a", data.VendoredArtSets);
    }

    [Fact]
    public async Task A_missing_art_manifest_leaves_every_card_on_the_remote_chain()
    {
        // The ordinary case: a development build, and any deploy where upstream was already
        // complete. A 404 here is an answer, not a fault.
        var data = await Loader(null).LoadAsync();

        Assert.Empty(data.VendoredArtSets);
    }

    [Fact]
    public async Task A_hanging_art_manifest_does_not_hold_the_boot_open()
    {
        // The manifest says which sets this deployment vendored art for, and it is read before the
        // card data so the first grid renders with the right urls. It is a few dozen bytes from
        // the app's own host -- and it went in with no deadline, which turned a network that drops
        // packets into an app stuck on "Loading card data…" for good.
        //
        // Its absence is a supported answer, so failing to reach it must cost nothing. Asserted
        // through the reported set list as well as the clock: a boot that returned but claimed a
        // vendored set would pass a timing check and then serve local urls for art it does not
        // have.
        var data = await Loader(Hang, artDeadline: Impatient).LoadAsync().WaitAsync(Patience);

        Assert.Equal(DataSource.VendoredSnapshot, data.Source);
        Assert.Empty(data.VendoredArtSets);
    }

    [Fact]
    public async Task A_hanging_cdn_still_returns_card_facts_from_the_snapshot()
    {
        // The local copy is tried first here, so this proves the deadline did not break the
        // ordinary path rather than that it fired.
        var facts = await Loader(Hang, artDeadline: Impatient).LoadFactsAsync().WaitAsync(Patience);

        Assert.True(facts.Count > 0, "no card detail loaded");
    }

    // ------------------------------------------------------------------ card detail top-up
    //
    // The vendored detail table is read on every visit and is whatever was last deployed; the card
    // DATA comes live from a CDN and runs ahead of it whenever a set is released. What closes that
    // gap is a per-set fetch, and these cover the part that is easy to get wrong: which files it
    // asks for. A top-up that requests the whole table, or re-requests sets it already has, would
    // pass any test that only looked at the facts it ended up with.

    /// <summary>
    /// One card of fabricated detail, in the upstream schema. Assembled rather than interpolated:
    /// the payload is mostly braces, and a raw interpolated literal cannot hold "}}}" as content.
    /// </summary>
    private static string SetFile(string setCode, int nr, string cardName, string attack) =>
        """
        [{"id":"SET-001","name":"NAME","set_code":"SET","deckBuilderNr":NR,
          "type":"Pokémon","stage":"Basic","health":60,
          "attacks":{"1":{"cost":"C","name":"ATTACK","damage":10,"effect":null}}}]
        """
        .Replace("SET", setCode, StringComparison.Ordinal)
        .Replace("NAME", cardName, StringComparison.Ordinal)
        .Replace("NR", nr.ToString(), StringComparison.Ordinal)
        .Replace("ATTACK", attack, StringComparison.Ordinal);

    /// <summary>The sets the vendored snapshot covers, in this app's spelling.</summary>
    private static readonly string[] Vendored =
    [
        "A1", "A1a", "A2", "A2a", "A2b", "A3", "A3a", "A3b", "A4", "A4a", "A4b",
        "B1", "B1a", "B2", "B2a", "B2b", "B3", "B3a", "B3b", "B4", "B4a", "PROMO-A", "PROMO-B",
    ];

    /// <summary>
    /// A set code upstream will never publish, standing in for "released since the last deploy".
    ///
    /// These three tests used B4a, which was that set when they were written and stopped being it
    /// the moment the snapshot was refreshed: its detail arrived, the gap closed, and three tests
    /// about how a gap behaves had no gap left to describe. A code that cannot exist keeps them
    /// about the behaviour rather than about the calendar.
    ///
    /// MissingSets is a set difference and nothing more -- no release check, no known-set filter --
    /// so an invented code is a gap by the same rule a real one is, and MirrorSetCode lowercases
    /// anything it does not recognise, which is what builds the URL.
    /// </summary>
    private const string Unpublished = "Z9z";

    private static (CardDataLoader Loader, SnapshotHandler Handler) Detail(
        IReadOnlyDictionary<string, string>? remoteFiles)
    {
        var handler = new SnapshotHandler(remoteFiles: remoteFiles);
        return (new CardDataLoader(new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") },
                                   Unhurried),
                handler);
    }

    [Fact]
    public async Task Detail_for_a_set_released_since_the_last_deploy_is_fetched_on_its_own()
    {
        // The whole point. A set's cards exist in the live card data and its detail is not in
        // the vendored table, so its attacks and abilities were blank until somebody redeployed.
        var (loader, handler) = Detail(new Dictionary<string, string>
        {
            ["/z9z/z9z.min.json"] = SetFile("z9z", 90001, "Volbeat", "Tackle"),
        });

        var facts = await loader.LoadFactsAsync([.. Vendored, Unpublished]);

        var added = facts.ForPrinting($"{Unpublished}-1");
        Assert.NotNull(added);
        Assert.Equal("Volbeat", added.Name);
        Assert.Equal("Tackle", added.Attacks!["1"].Name);

        // The vendored table is still there underneath it.
        Assert.NotNull(facts.ForPrinting("A1-1"));

        // One remote file, for the one missing set. Not the 4.4 MB whole table, and not a request
        // for any set already covered.
        var remote = handler.Requests.Where(u => !u.Contains("data/snapshot/")).ToList();
        Assert.Single(remote);
        Assert.EndsWith("/z9z/z9z.min.json", remote[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_gap_means_nothing_leaves_the_origin()
    {
        // The ordinary visit, which is nearly all of them. The vendored table answers completely
        // and the top-up must cost exactly nothing -- otherwise every user pays a request per load
        // for a set that has not been released.
        var (loader, handler) = Detail(null);

        var facts = await loader.LoadFactsAsync(Vendored);

        // Count is distinct CARDS, not rows: the entries collapse to rather fewer identities,
        // since alternate arts share a card and trainers carry no deck-builder number at all.
        Assert.True(facts.Count > 2000, $"only {facts.Count} facts");
        Assert.NotNull(facts.ForPrinting("A1-1"));
        Assert.DoesNotContain(handler.Requests, u => !u.Contains("data/snapshot/"));
    }

    [Fact]
    public async Task The_promos_are_not_mistaken_for_a_permanent_gap()
    {
        // The two projects spell the promos differently -- PROMO-A here, pa there. Compared without
        // translating, both promo sets look missing on every single load, so every user would fetch
        // two files forever to be told what the vendored table already said.
        var (loader, handler) = Detail(null);

        await loader.LoadFactsAsync(["PROMO-A", "PROMO-B"]);

        Assert.DoesNotContain(handler.Requests, u => u.Contains("/pa/") || u.Contains("/promo"));
    }

    [Fact]
    public async Task A_set_upstream_has_not_published_yet_is_not_a_failure()
    {
        // A set whose cards exist and whose detail does not exist anywhere. A 404 has to leave
        // the vendored table intact rather than emptying it.
        var (loader, _) = Detail(null);

        var facts = await loader.LoadFactsAsync([.. Vendored, Unpublished]);

        // Count is distinct CARDS, not rows: the entries collapse to rather fewer identities,
        // since alternate arts share a card and trainers carry no deck-builder number at all.
        Assert.True(facts.Count > 2000, $"only {facts.Count} facts");
        Assert.NotNull(facts.ForPrinting("A1-1"));
        Assert.Null(facts.ForPrinting($"{Unpublished}-1"));
    }

    [Fact]
    public async Task A_definite_404_stops_it_asking_the_second_route()
    {
        // The npm package is published FROM the repository, so it is never ahead of the branch --
        // a file the branch does not have cannot be in the package. Asking anyway would double the cost of a gap that upstream simply has not
        // filled, on every single visit.
        var (loader, handler) = Detail(null);

        await loader.LoadFactsAsync([.. Vendored, Unpublished]);

        var remote = handler.Requests.Where(u => !u.Contains("data/snapshot/")).ToList();
        Assert.Single(remote);
        Assert.Contains("/gh/", remote[0]);      // the repository, which is the fresher of the two
        Assert.DoesNotContain(handler.Requests, u => u.Contains("/npm/pokemon-tcg-pocket-cards"));
    }

    [Fact]
    public async Task Enough_missing_sets_and_it_asks_for_the_whole_table_instead()
    {
        // A vendored table that could not be read, or one so old it predates most of the game.
        // Twenty per-set requests would be slower AND larger than the one file containing all of
        // them, so past a handful the walk is abandoned.
        var many = Enumerable.Range(1, 12).Select(i => $"Z{i}").ToArray();
        var (loader, handler) = Detail(new Dictionary<string, string>
        {
            ["/cards.min.json"] = SetFile("z1", 90002, "Nothing", "Nothing"),
        });

        await loader.LoadFactsAsync([.. Vendored, .. many]);

        var remote = handler.Requests.Where(u => !u.Contains("data/snapshot/")).ToList();
        Assert.Single(remote);
        Assert.EndsWith("/cards.min.json", remote[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_untrimmed_upstream_row_parses_into_the_fields_the_app_reads()
    {
        // The vendored table is a TRIMMED copy: same rows, fewer fields. A topped-up set arrives
        // untrimmed, carrying a dozen fields this app has no use for, so this is a real upstream
        // row verbatim -- b4-005 Dustox -- checking that the ones it does read still land and the
        // rest are ignored rather than throwing.
        //
        // Includes the shapes that would break a careless reader: an ability wrapped in an
        // "exists" object, a second attack slot present but entirely null, and nulls in fields
        // typed as strings.
        const string upstream = """
        [{"id":"b4-005","name":"Dustox","set_code":"b4","set_name":"Ruler of the Skies",
          "pack":"Ruler of the Skies","release_date":"2026-07-30","type":"Pokémon",
          "subtype":"Grass","stage":"Stage 2","evolves_from":"Cascoon","rarity":"◊◊◊",
          "pack_points":150,"ex":false,"mega":false,"shiny":false,"special_tags":null,
          "art_style":null,"health":120,"retreat":1,"weakness":"Fire",
          "ability":{"exists":true,"name":"Variety Powder","effect":"Once during your turn, you may use this Ability."},
          "card_text":null,
          "attacks":{"1":{"cost":"CC","name":"Cutting Wind","damage":60,"effect":null},
                     "2":{"cost":null,"name":null,"damage":null,"effect":null}},
          "points":1,"deckBuilderNr":1966,"artist":"Midori Harada",
          "image":"https://example.invalid/b4/005.webp","alternate_versions":[]}]
        """;

        var (loader, _) = Detail(new Dictionary<string, string> { ["/zz/zz.min.json"] = upstream });

        var facts = await loader.LoadFactsAsync([.. Vendored, "ZZ"]);

        var dustox = facts.ForPrinting("B4-5");
        Assert.NotNull(dustox);
        Assert.Equal("Dustox", dustox.Name);
        Assert.Equal("Stage 2", dustox.Stage);
        Assert.Equal("Cascoon", dustox.EvolvesFrom);
        Assert.Equal(120, dustox.Health);
        Assert.Equal("Fire", dustox.Weakness);
        Assert.Equal(150, dustox.PackPoints);
        Assert.Equal("Variety Powder", dustox.Ability?.Name);
        Assert.True(dustox.HasAbility);

        // Two energies, expanded from the packed "CC" the data stores.
        Assert.Equal("Cutting Wind", dustox.Attacks!["1"].Name);
        Assert.Equal("60", dustox.Attacks["1"].DamageLabel);
        Assert.Equal(2, dustox.Attacks["1"].CostSymbols.Count);

        // The empty second slot survives as an empty slot rather than as an attack.
        Assert.Null(dustox.Attacks["2"].Name);
    }

    [Fact]
    public async Task Passing_no_set_list_leaves_the_vendored_table_alone()
    {
        // The caller that wants only what shipped. Also what every other test in this suite gets,
        // so a top-up cannot start making requests behind them.
        var (loader, handler) = Detail(null);

        var facts = await loader.LoadFactsAsync();

        // Count is distinct CARDS, not rows: the entries collapse to rather fewer identities,
        // since alternate arts share a card and trainers carry no deck-builder number at all.
        Assert.True(facts.Count > 2000, $"only {facts.Count} facts");
        Assert.NotNull(facts.ForPrinting("A1-1"));
        Assert.DoesNotContain(handler.Requests, u => !u.Contains("data/snapshot/"));
    }

    [Fact]
    public async Task A_cancelled_caller_is_not_mistaken_for_a_slow_source()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Loader(Hang).LoadFactsAsync(ct: cancelled.Token));
    }
}
