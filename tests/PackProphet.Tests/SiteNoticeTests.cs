namespace PackProphet.Tests;

using PackProphet.Data;

/// <summary>
/// The notice bar's two sources, checked without a browser or a network.
///
/// The half that matters most is the refusals. The authored feed is a document on a third-party
/// host that the app renders into its own chrome, which makes it the only text in PackProphet that
/// neither the build nor the user wrote — so every check in <see cref="NoticeFeedReader"/> has an
/// assertion here, and the wording of the notices does not.
/// </summary>
public class SiteNoticeTests
{
    private static readonly DateOnly Today = new(2026, 9, 18);

    private static SetCatalog Catalog() => new(
        new Dictionary<string, List<SetInfo>>
        {
            ["A"] =
            [
                new SetInfo { Code = "A1", ReleaseDate = "2024-10-30", Name = new() { ["en"] = "Genetic Apex" } },
                new SetInfo { Code = "A2", ReleaseDate = "2025-01-29", Name = new() { ["en"] = "Space-Time Smackdown" } },
            ],
            ["B"] =
            [
                new SetInfo { Code = "B4a", ReleaseDate = "2026-09-01", Name = new() { ["en"] = "Team Rocket's Ambition" } },
            ],
        },
        []);

    // ---- derived: what the site is still waiting on ----------------------------------

    private static CardIndex IndexOf(params (string Set, int Cards)[] sets)
    {
        var cards = sets
            .SelectMany(s => Enumerable.Range(1, s.Cards).Select(n => new PocketCard
            {
                Set = s.Set, Number = n, Rarity = "C", Name = $"{s.Set}-{n}", Image = $"{n}.webp",
            }))
            .ToList();

        return new CardIndex(cards, Snapshot.Rarities());
    }

    /// <summary>Detail for the first <paramref name="covered"/> cards of each named set.</summary>
    private static CardFacts FactsFor(params (string Set, int Covered)[] sets) =>
        new(sets.SelectMany(s => Enumerable.Range(1, s.Covered).Select(n => new CardFact
        {
            SetCode = s.Set, Id = $"{s.Set.ToLowerInvariant()}-{n:D3}", Name = $"{s.Set}-{n}",
        })).ToList());

    [Fact]
    public void The_shipped_snapshot_has_detail_for_every_card_it_lists()
    {
        // The measurement this feature's threshold was chosen from: 3,879 of 3,879, every set
        // complete. Pinned here because the threshold is only defensible while the reading stays
        // bimodal — a set is covered or it has not arrived — and a snapshot that starts coming in
        // 85% covered would make a tenth of slack the wrong number rather than a safe one.
        var short_ = DataLag.DetailShortfalls(Snapshot.Index(), Snapshot.Facts());

        Assert.True(short_.Count == 0,
            "sets short of detail in the shipped snapshot: "
            + string.Join(", ", short_.Select(g => $"{g.Set} {g.Have}/{g.Of}")));
    }

    [Fact]
    public void A_set_the_detail_table_has_never_heard_of_is_reported()
    {
        var short_ = DataLag.DetailShortfalls(
            IndexOf(("A1", 10), ("B5", 20)), FactsFor(("A1", 10)));

        var gap = Assert.Single(short_);
        Assert.Equal("B5", gap.Set);
        Assert.Equal(20, gap.Short);
        Assert.True(gap.Nothing);
    }

    [Fact]
    public void A_set_missing_a_card_or_two_is_left_alone()
    {
        // A handful of gaps is upstream's ordinary state and not worth a bar on every page. A
        // set's worth is.
        Assert.Empty(DataLag.DetailShortfalls(IndexOf(("A1", 100)), FactsFor(("A1", 95))));
        Assert.Single(DataLag.DetailShortfalls(IndexOf(("A1", 100)), FactsFor(("A1", 50))));
    }

    [Fact]
    public void Art_coverage_reports_only_the_sets_that_are_short()
    {
        var gaps = DataLag.ArtShortfalls(
        [
            new SetShortfall("B4", 233, 233),   // complete, nothing to say
            new SetShortfall("B5", 84, 110),    // partly drawable
            new SetShortfall("B6", 0, 90),      // nothing anywhere
        ]);

        Assert.Equal(["B5", "B6"], gaps.Select(g => g.Set));
        Assert.False(gaps[0].Nothing);
        Assert.True(gaps[1].Nothing);
    }

    [Fact]
    public void An_absent_manifest_is_nothing_known_rather_than_everything_missing()
    {
        // Which is a development build, every test, and any deploy where upstream had nothing
        // missing. The app cannot see a missing image without requesting it, so silence is the
        // only honest answer.
        Assert.Empty(DataLag.ArtShortfalls(null));
        Assert.Empty(DataLag.ArtShortfalls([]));
    }

    [Fact]
    public void Nothing_missing_is_no_notice()
    {
        Assert.Null(DataLag.Waiting([], [], Catalog()));
    }

    [Fact]
    public void A_set_missing_both_is_one_sentence_rather_than_two_notices()
    {
        // The common case by a distance, and the reason these are one notice: offered as two, the
        // bar shows one and dismissing it reveals the other, which is the nagging this avoids.
        var notice = DataLag.Waiting(
            [new SetShortfall("B4a", 0, 110)], [new SetShortfall("B4a", 0, 110)], Catalog());

        Assert.NotNull(notice);
        Assert.Equal("Team Rocket's Ambition is still missing card art and attack detail.",
                     notice.Text);
    }

    [Fact]
    public void One_kind_of_gap_says_only_that()
    {
        var art = DataLag.Waiting([new SetShortfall("B4a", 12, 110)], [], Catalog());
        Assert.Equal("Team Rocket's Ambition is still missing card art.", art!.Text);

        var detail = DataLag.Waiting([], [new SetShortfall("B4a", 0, 110)], Catalog());
        Assert.Equal("Team Rocket's Ambition is still missing attack and ability detail.",
                     detail!.Text);
    }

    [Fact]
    public void Gaps_in_different_sets_are_two_clauses()
    {
        var notice = DataLag.Waiting(
            [new SetShortfall("B4a", 0, 110)], [new SetShortfall("A2", 0, 207)], Catalog());

        Assert.NotNull(notice);
        Assert.Equal(
            "Team Rocket's Ambition is still missing card art. "
            + "Space-Time Smackdown is still missing attack and ability detail.",
            notice.Text);
    }

    [Fact]
    public void Several_sets_read_as_a_list_newest_first()
    {
        var notice = DataLag.Waiting(
            [new SetShortfall("A2", 0, 207), new SetShortfall("B4a", 0, 110)], [], Catalog());

        // Newest first: the set somebody is opening packs of is the one that just came out, and it
        // is also the one most likely to be short of everything.
        Assert.StartsWith("Team Rocket's Ambition, Space-Time Smackdown are", notice!.Text);
    }

    [Fact]
    public void Past_three_sets_the_rest_are_counted_rather_than_listed()
    {
        var gaps = new[] { "A1", "A2", "B4a", "B8", "B9" }
            .Select(s => new SetShortfall(s, 0, 50)).ToArray();

        Assert.Contains("and 2 more", DataLag.Waiting(gaps, [], Catalog())!.Text);
    }

    [Fact]
    public void The_key_is_the_set_codes_so_a_translation_cannot_forget_a_dismissal()
    {
        // The display name is localised and the code is not. Keying on the name would bring a
        // dismissed notice back the day upstream fills in an English name for a new set.
        var notice = DataLag.Waiting([new SetShortfall("B4a", 0, 110)], [], Catalog());

        Assert.Equal("waiting:B4a", notice!.Key);
        Assert.DoesNotContain("Rocket", notice.Key);
    }

    [Fact]
    public void Half_an_answer_is_not_re_announced()
    {
        // Art lands and detail has not. That is the same subject half answered, and a new key
        // would be the bar reporting its own progress back to somebody who dismissed it.
        var both = DataLag.Waiting(
            [new SetShortfall("B4a", 0, 110)], [new SetShortfall("B4a", 0, 110)], Catalog());
        var detailOnly = DataLag.Waiting([], [new SetShortfall("B4a", 0, 110)], Catalog());

        Assert.Equal(both!.Key, detailOnly!.Key);
        Assert.NotEqual(both.Text, detailOnly.Text);
    }

    [Fact]
    public void A_different_set_is_a_different_key()
    {
        // The whole reason dismissals are keyed rather than a boolean: silencing this release must
        // leave the next one free to appear.
        var first = DataLag.Waiting([new SetShortfall("B4a", 0, 110)], [], Catalog());
        var second = DataLag.Waiting(
            [new SetShortfall("B4a", 0, 110), new SetShortfall("B5", 0, 90)], [], Catalog());

        Assert.NotEqual(first!.Key, second!.Key);
    }

    [Fact]
    public void Waiting_is_stated_rather_than_warned_about_and_offers_nothing_to_do()
    {
        // Nothing has gone wrong: the cards are there, countable and collectable, and what is
        // missing is decoration and reference text. There is also nothing the reader can do — it
        // resolves when upstream publishes — so a button would be worse than the silence.
        var notice = DataLag.Waiting([new SetShortfall("B4a", 0, 110)], [], Catalog());

        Assert.Equal(NoticeLevel.Info, notice!.Level);
        Assert.False(notice.HasAction);
        Assert.True(notice.Persist);
    }

    [Fact]
    public void An_outage_is_louder_than_waiting_and_is_not_remembered()
    {
        // The CDN is re-tested on every boot, so a remembered dismissal would silence a genuine
        // outage months later on the strength of one bad afternoon.
        Assert.Equal(NoticeLevel.Warning, DataLag.Offline(null).Level);
        Assert.False(DataLag.Offline("2.10.0").Persist);
    }

    [Fact]
    public void The_offline_notice_names_the_bundled_version_when_there_is_one()
    {
        Assert.Contains("v2.10.0", DataLag.Offline("2.10.0").Text);
        Assert.DoesNotContain("(", DataLag.Offline(null).Text);
    }

    // ---- authored: what the feed is allowed to say ------------------------------------

    private static IReadOnlyList<SiteNotice> Read(string json) =>
        NoticeFeedReader.Parse(json, Today);

    private const string OneNotice =
        """[{"id":"b5","level":"warning","text":"B5 launches today.","until":"2026-09-25"}]""";

    [Fact]
    public void A_well_formed_entry_is_read()
    {
        var notice = Assert.Single(Read(OneNotice));

        Assert.Equal("feed:b5", notice.Key);
        Assert.Equal(NoticeLevel.Warning, notice.Level);
        Assert.Equal("B5 launches today.", notice.Text);
    }

    [Fact]
    public void The_feed_key_is_prefixed_so_it_cannot_collide_with_a_derived_one()
    {
        // An author could write `id: "offline"`, and without the prefix that would inherit — or
        // silence — the app's own outage notice.
        var notice = Assert.Single(Read("""[{"id":"offline","text":"x."}]"""));
        Assert.Equal("feed:offline", notice.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{}")]                                  // an object where a list was expected
    [InlineData("""{"id":"x","text":"y."}""")]           // the likeliest way to write it wrong
    [InlineData("[]")]
    [InlineData("[null]")]
    public void Anything_it_cannot_read_is_no_notice(string json)
    {
        Assert.Empty(Read(json));
    }

    [Fact]
    public void A_human_editing_it_in_a_browser_is_forgiven_a_comma_and_a_comment()
    {
        var json = """
            [
              // the launch
              {"id":"b5","text":"B5 launches today.",},
            ]
            """;

        Assert.Single(Read(json));
    }

    /// <summary>
    /// The published feed documents itself in a comment block above the list, and the list below
    /// it is usually empty. That is the file's resting state — so this is the shape the parser
    /// meets on almost every fetch, and the one nothing else here covers: the test above puts a
    /// comment INSIDE the array, and leading trivia before the root token is a different position
    /// in the grammar.
    ///
    /// Worth a test of its own because the failure is silent in both directions. Drop
    /// <c>JsonCommentHandling.Skip</c> from the reader's options and the whole feed stops parsing;
    /// an unreadable feed and an empty one are indistinguishable by design, so the authored
    /// channel would simply go quiet and nothing would say why.
    /// </summary>
    private const string DocumentedFeed = """
        // PackProphet notices. Everything above the brackets is a comment.
        //
        //   id     REQUIRED. What a dismissal is remembered against.
        //   until  Optional, inclusive, yyyy-MM-dd.
        //
        // TEMPLATE -- copy the block between the brackets and edit it:
        //
        //   {
        //     "id": "2026-09-19-something",
        //     "text": "Screenshot import is failing for the newest set."
        //   }

        [BODY]
        """;

    [Fact]
    public void The_feed_may_document_itself_above_the_list()
    {
        Assert.Empty(Read(DocumentedFeed.Replace("[BODY]", "[]")));
    }

    [Fact]
    public void A_documented_feed_still_reads_the_entries_under_it()
    {
        // The comment block must not cost the notice beneath it, which is the case that matters:
        // the header is only ever read on the day somebody posts something.
        var notice = Assert.Single(Read(DocumentedFeed.Replace(
            "[BODY]", """[{"id":"down","level":"problem","text":"Import is failing."}]""")));

        Assert.Equal("feed:down", notice.Key);
        Assert.Equal(NoticeLevel.Problem, notice.Level);
    }

    [Fact]
    public void An_entry_with_no_id_is_refused()
    {
        // It could be dismissed but not remembered as dismissed, so it would come back on every
        // reload — which is worse than never appearing.
        Assert.Empty(Read("""[{"text":"B5 launches today."}]"""));
        Assert.Empty(Read("""[{"id":"  ","text":"B5 launches today."}]"""));
    }

    [Fact]
    public void An_entry_with_no_text_is_refused()
    {
        Assert.Empty(Read("""[{"id":"b5"}]"""));
    }

    [Fact]
    public void Text_past_the_ceiling_is_refused()
    {
        var long_ = new string('x', NoticeFeedReader.LongestText + 1);
        Assert.Empty(Read($$"""[{"id":"b5","text":"{{long_}}"}]"""));

        var just_fits = new string('x', NoticeFeedReader.LongestText);
        Assert.Single(Read($$"""[{"id":"b5","text":"{{just_fits}}"}]"""));
    }

    [Fact]
    public void An_expired_notice_is_dropped_and_its_last_day_is_included()
    {
        Assert.Empty(Read("""[{"id":"b5","text":"x.","until":"2026-09-17"}]"""));
        Assert.Single(Read("""[{"id":"b5","text":"x.","until":"2026-09-18"}]"""));
        Assert.Single(Read("""[{"id":"b5","text":"x.","until":"2026-09-19"}]"""));
    }

    [Fact]
    public void An_until_date_that_cannot_be_read_drops_the_notice()
    {
        // Fails closed, and this is the field where that matters most: `until` is the only thing
        // that makes a notice stop, so treating an unreadable one as absent would publish it
        // forever on the strength of a typo.
        Assert.Empty(Read("""[{"id":"b5","text":"x.","until":"25 Sep 2026"}]"""));
        Assert.Empty(Read("""[{"id":"b5","text":"x.","until":"2026-9-25"}]"""));
        Assert.Empty(Read("""[{"id":"b5","text":"x.","until":"soon"}]"""));

        // Absent is fine, and means no end date.
        Assert.Single(Read("""[{"id":"b5","text":"x."}]"""));
    }

    [Theory]
    [InlineData("problem", NoticeLevel.Problem)]
    [InlineData("error", NoticeLevel.Problem)]
    [InlineData("warning", NoticeLevel.Warning)]
    [InlineData("WARN", NoticeLevel.Warning)]
    [InlineData("info", NoticeLevel.Info)]
    [InlineData("urgent", NoticeLevel.Info)]
    [InlineData(null, NoticeLevel.Info)]
    public void An_unknown_level_reads_as_the_quietest_one(string? level, NoticeLevel expected)
    {
        // A real outage written with a typo in `level` still shows, just politely.
        var written = level is null ? "" : $"""  ,"level":"{level}" """;
        var notice = Assert.Single(Read($$"""[{"id":"b5","text":"x."{{written}}}]"""));

        Assert.Equal(expected, notice.Level);
    }

    [Fact]
    public void An_https_action_and_a_relative_one_are_both_allowed()
    {
        var remote = Assert.Single(Read(
            """[{"id":"b5","text":"x.","actionLabel":"Read More","actionHref":"https://example.com/x"}]"""));
        Assert.True(remote.HasAction);
        Assert.Equal("https://example.com/x", remote.ActionHref);

        var local = Assert.Single(Read(
            """[{"id":"b5","text":"x.","actionLabel":"Open Settings","actionHref":"settings"}]"""));
        Assert.Equal("settings", local.ActionHref);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("//evil.example/x")]      // reads as a path, resolves as an origin
    [InlineData("/absolute")]
    [InlineData("\\\\evil.example")]
    [InlineData("http://example.com/x")]  // no mixed content from a banner
    [InlineData("vbscript:x")]
    public void An_unsafe_action_loses_the_link_and_keeps_the_sentence(string href)
    {
        // The failure this refuses is a script running in the app's own chrome, reached through a
        // document nothing in the build signs. The sentence survives, because losing an outage
        // notice to a broken URL would be the worse trade.
        var notice = Assert.Single(Read(
            $$"""[{"id":"b5","text":"Something is broken.","actionLabel":"Fix","actionHref":"{{href.Replace("\\", "\\\\")}}"}]"""));

        Assert.False(notice.HasAction);
        Assert.Null(notice.ActionHref);
        Assert.Equal("Something is broken.", notice.Text);
    }

    [Fact]
    public void Half_an_action_is_no_action()
    {
        Assert.False(Assert.Single(Read(
            """[{"id":"b5","text":"x.","actionLabel":"Fix"}]""")).HasAction);

        Assert.False(Assert.Single(Read(
            """[{"id":"b5","text":"x.","actionHref":"settings"}]""")).HasAction);
    }

    [Fact]
    public void An_overlong_action_label_loses_the_link()
    {
        var label = new string('x', NoticeFeedReader.LongestLabel + 1);
        Assert.False(Assert.Single(Read(
            $$"""[{"id":"b5","text":"y.","actionLabel":"{{label}}","actionHref":"settings"}]""")).HasAction);
    }

    [Fact]
    public void The_feed_is_capped_so_a_runaway_one_cannot_paper_over_the_app()
    {
        var entries = string.Join(",", Enumerable.Range(0, 20)
            .Select(i => $$"""{"id":"n{{i}}","text":"x."}"""));

        Assert.Equal(NoticeFeedReader.MostNotices, Read($"[{entries}]").Count);
    }

    [Fact]
    public void One_bad_entry_does_not_take_the_good_ones_with_it()
    {
        var notices = Read("""
            [
              {"text":"no id, dropped."},
              {"id":"good","text":"kept."}
            ]
            """);

        Assert.Equal("kept.", Assert.Single(notices).Text);
    }
}
