using PackProphet.Data;
using PackProphet.Vision;

namespace PackProphet.Tests;

/// <summary>
/// The one test that runs on a real phone screenshot of the game, end to end, against the real
/// committed fingerprint table.
///
/// Everything below is verbatim output from <c>scan()</c> in wwwroot/js/cardshot.js applied to
/// <c>tests/PackProphet.Tests/fixtures/IMG_1152.jpeg</c> — the My Cards screen, three across,
/// showing A1-1 to A1-9 with their copy counts. Nothing here is synthetic and nothing was
/// hand-corrected: the boxes the fingerprints came from are the ones the detector chose for itself.
/// Three of the nine cards carry gold flair, which the game draws over any card held ten times or
/// more, and the bottom row is cut off by the edge of the screen.
///
/// This test exists because everything synthetic passed while the real thing did not, twice.
/// Sampling the whole card scored 0 bits against screenshots built from the artwork files and
/// recognised one card in nine here — flair alone was worth 19 to 26 bits. Trusting each detected
/// region's own extent as the card's box framed them eight pixels out, which is fatal at this
/// sampling rate. Both were invisible until a real screenshot was run through the whole chain, so a
/// real screenshot is what the thresholds are now justified against.
/// </summary>
public class ScreenshotEndToEndTests
{
    private static CardIndex Ix => Snapshot.Index();

    private static ArtHashTable Table =>
        ArtHashTable.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "card-hashes.txt")));

    /// <summary>
    /// The detector's own output, cell by cell, in reading order. The three empty fingerprints are
    /// the bottom row: the detector found their regions much shorter than the consensus card height
    /// and reported them as slots it could not read rather than measuring part of a card.
    /// </summary>
    private static readonly (string Key, string Hash, double Luma, double Saturation, double Detail, string Note)[] Measured =
    [
        ("A1-1", "8ea2b6f886acdaf2c6fa05ff033c51b7", 0.6960, 0.4458, 0.0500, "Bulbasaur, 9 copies"),
        ("A1-2", "0eb2a60c2d33b1b2c6fa058c0130ccff", 0.6240, 0.6194, 0.0765, "Ivysaur, 11 copies, gold flair"),
        ("A1-3", "0ca6b6eecdb1d7e3c0f21f00c0bff7f8", 0.6072, 0.4504, 0.0813, "Venusaur, 1 copy"),
        ("A1-4", "8ea64a3a38595173c3ff00039cd7f01a", 0.5832, 0.5763, 0.0708, "Venusaur ex, 8 copies"),
        ("A1-5", "1c16a6af3894b11500f72c003ef70943", 0.5986, 0.6594, 0.0951, "Caterpie, 14 copies, gold flair"),
        ("A1-6", "86b2a62929375357c3ff00000013e8ff", 0.5617, 0.5724, 0.0913, "Metapod, 20 copies, gold flair"),
        ("A1-7", "", 0, 0, 1, "Butterfree, clipped by the screen edge"),
        ("A1-8", "", 0, 0, 1, "Weedle, clipped"),
        ("A1-9", "", 0, 0, 1, "Kakuna, clipped"),
    ];

    /// <summary>The six cards the screenshot shows whole. The other three are cut off at the bottom.</summary>
    private static readonly string[] Whole = ["A1-1", "A1-2", "A1-3", "A1-4", "A1-5", "A1-6"];

    /// <summary>The scan exactly as the module reported it, lattice and all.</summary>
    private static ShotScan Scan() => new()
    {
        Ok = true,
        Width = 1320,
        Height = 2868,
        Lattice = new ShotLattice
        {
            Rows = 3, Cols = 3, CellWidth = 192, CellHeight = 268,
            Confidence = 1.0, RelativeCellWidth = 0.2981366459627329,
        },
        Cells = Measured.Select((m, i) => new ShotCell
        {
            Row = i / 3, Col = i % 3, Hash = m.Hash,
            Luma = m.Luma, Saturation = m.Saturation, Detail = m.Detail,
        }).ToList(),
    };

    private static ShotReading Read() =>
        new ScreenshotReader(Ix, Table).Read(Scan(), CardScreen.CopiesGrid);

    /// <summary>
    /// IMG_1154: the same screen scrolled further down, A1-16 to A1-24 across three full rows, with
    /// badges reading 3, 3, 6, 4, 3, 9, 1, 5, 4 by eye. Verbatim <c>scan()</c> output again, and this
    /// is the fixture that made all ten digits available — it is where the 3 and the 6 came from.
    ///
    /// Copies is null for the last two. The badge segmentation returns a sliver rather than a digit
    /// on those two cards, so the count is reported as unknown — which is the required outcome, and
    /// the reason a partly-read count is never used: dropping a digit turns 14 into 4.
    /// </summary>
    private static readonly (string Key, string Hash, string Grey, double Aspect, int? Copies)[] Counted =
    [
        ("A1-16", "86b2a66667cb0e53c2ff0561009b947d", "039fffb23afffff77ffffffb8ffa7ffd48934dfd11128ffc0006fffa0005effc00015cff565118ffcfe318ffeffb7fff9ffffffc27befec5", 0.63, 3),
        ("A1-17", "1cb6a6e9c5c5c68600ff6d00401fe3ff", "04afffa23bfffff77ffffffa9ff98ffc59934ffc11128ffb0007fff90005fffb00015efe56510affdfe31afffffa7ffeaffffffb28cefdb4", 0.63, 3),
        ("A1-18", "8eb2a6868ecceecbc2ff05000a7fbb40", "00028b400006ef71002bfe50005ffb2001aff81003dffc613afffff97ffc7bfe9ff615ffafd403ffafe504ff7ffa49fe4cfffffc13adedb4", 0.69, 6),
        ("A1-19", "0eb2b698717175dbc2fa0500103c03cf", "00018fb10003fff20007fff2001bfff2004efff2019ffff205ee9ff21bfa7ff24efaaff48ffffffaaffffffc6cccfff923349ff400004b91", 0.75, 4),
        ("A1-20", "8e32a4d679da7b43c23800571cde21e7", "0137863115cfffc34dfffff88ffefffb6cc66ffc1113affb0006fff90008fffa0004effc45413cffdff52cffeffcaffd9ffffffb27beedb4", 0.59, 3),
        ("A1-21", "0ea2b63c7560f173c2fa050030788187", "04cffd611affffe44efdcff77ff74cfa8ff31afb9ff31afb6ffa7df92cfffff505dfffd3014bff71002bfd30004df910018ff500015b7100", 0.75, 9),
        ("A1-22", "0eb6b6b869c4e7c308f20f0800fe03c0", "fffffff8fffffffbccfffffb229ffffb006ffffb006ffffb006ffffb006ffffb006ffffb006ffffb006ffffb006ffffb006ffffb003bccb6", 0.38, 1),
        ("A1-23", "1e96268d2e95c97308f27f0032d46807", "8888888800000000000000000000000000000000000000000000000000000000000000000000000000000000000000001111000099993333", 0.07, null),
        ("A1-24", "1ea6b6b0010f8fc208b20d00c0bfffff", "88887777111100001111000011110000111100001111000011110000111100001111000011110000111100002222000077771111eeee9999", 0.07, null),
    ];

    private static ShotScan CountedScan() => new()
    {
        Ok = true, Width = 1320, Height = 2868,
        Lattice = new ShotLattice
        {
            Rows = 3, Cols = 3, CellWidth = 192, CellHeight = 268,
            Confidence = 1.0, RelativeCellWidth = 0.298,
        },
        Cells = Counted.Select((c, i) => new ShotCell
        {
            Row = i / 3, Col = i % 3, Hash = c.Hash,
            Luma = 0.64, Saturation = 0.49, Detail = 0.08,
            Digits = [new DigitGlyph { Grey = c.Grey, Aspect = c.Aspect }],
        }).ToList(),
    };

    [Fact]
    public void EveryCardOnTheSecondFixtureIsRecognisedToo()
    {
        // Nine cards, all of them whole. The first fixture's bottom row is cut off by the screen, so
        // this is the one that shows a full page reading cleanly.
        var reading = new ScreenshotReader(Ix, Table).Read(CountedScan(), CardScreen.CopiesGrid);

        Assert.Equal(Counted.Select(c => c.Key), reading.Matches.Select(m => m.Card.Key));
        Assert.Equal(0, reading.UnreadCells);
    }

    [Fact]
    public void TheCopyCountIsReadOffTheBadgeAndCarriedOnTheMatch()
    {
        // The whole point of the three-across list: it says how many. Nothing else does.
        var reading = new ScreenshotReader(Ix, Table).Read(CountedScan(), CardScreen.CopiesGrid);

        Assert.Equal(Counted.Select(c => c.Copies), reading.Matches.Select(m => m.Copies));
        Assert.Equal(7, reading.Matches.Count(m => m.Copies is not null));
    }

    [Fact]
    public void ACountThatCouldNotBeReadIsNullAndSaidOutLoud()
    {
        // Null means "the screen did not say", never "none". And the reading says how many cards it
        // happened to, because a reading that named every card and read no counts looks complete.
        var reading = new ScreenshotReader(Ix, Table).Read(CountedScan(), CardScreen.CopiesGrid);

        Assert.Equal(2, reading.Matches.Count(m => m.Copies is null));
        Assert.Contains(reading.Notes, n => n.Contains("could not be read for 2 of 9 cards"));
    }

    [Fact]
    public void NoCountIsEverWrong()
    {
        // The failure that matters. A card recorded as 1 copy when the badge said 14 is silent, and
        // afterwards indistinguishable from a count the user typed in themselves.
        var reading = new ScreenshotReader(Ix, Table).Read(CountedScan(), CardScreen.CopiesGrid);

        foreach (var (match, expected) in reading.Matches.Zip(Counted))
            if (match.Copies is not null) Assert.Equal(expected.Copies, match.Copies);
    }

    /// <summary>
    /// IMG_1151, a Wonder Pick line-up: Shining Revelry, the one screen of that kind whose set the
    /// card data actually has. Four of the five cards; the fifth is Raticate, a pale card the
    /// detector does not find at all.
    ///
    /// Verbatim output, and this fixture is why cells carry nudged crops. The detector puts these
    /// boxes two to four pixels off — it cannot do better, because the box comes from a mask whose
    /// extent depends on the card's own border — and at the centre crop the right cards score 21,
    /// 22, 21 and 12 bits, so three of four were rejected. With the nudged crops they score 5, 11,
    /// 12 and 2.
    ///
    /// Four cards, not five: this capture predates per-row columns, and the fifth card of the offer
    /// is pale Raticate, which the mask does not find. That the module now reads all five is covered
    /// by the pack reveal above, where the same fix recovers Dunsparce.
    /// </summary>
    private static readonly (string Key, string Hash, string[] Nearby)[] WonderPick =
    [
        ("A2b-42", "f4326a8446472743f7ba4fc76008b15b",
         ["f5736a8ccf032743f77bc6c76018635f", "f5776b8cc70b2753f772cbc74028305b", "fb756b0cc54b274301b0cfc74020345b",
          "b4326a8446072743f73fc6c76018a15f", "f4b26a8e4447274301b04fc74028b55b", "b4312c8647072343f7bfe6c76098a15f",
          "f5b2298647472743f7ba45e76090b15b", "f5b239864547274301b26fe740a0b14b"]),
        ("A2b-19", "18392237b2c96dabfc21ff013e4da6fb",
         ["1174ae27b2c969a7f826a001784da0f7", "18746a2732c969affc20ff013a4da0ff", "1870622f32d969aff820ff013e4927f7",
          "1838ae37b2c92dabf827e201386da0fb", "18396023b2c96dabdc20ff003e6927fb", "1830a6b3b36d2de3f837e2013867b0fb",
          "18302033b3e92dabfc21ff003e6db6fb", "18383033b3e96dabfc00ff00bc6c87fb"]),
        ("A2b-24", "96ae69a819f1e12a108778000038e487",
         ["952661e9317161ab00a778001078e78f", "972561e91171c12a008778000078e48f", "d5a563699173c12a1906fb000079f48f",
          "96a669a931f161aa00af78000078e787", "d6a661e89933e12a18807d000038f487", "a6a769e119a1e10200877c000038e787",
          "b2af61e419b1e12200877c000038f487", "d2ae616019b1e12218807d000038f483"]),
        ("A2b-33", "56f2f6c8d159180dffff05ec001c0f61",
         ["d6f2f4c19b59190df7ffcfe0021c0e63", "56f2f6c19b59190dfffb0dfc005c0e61", "56f6f3498359181dfff20dfc005c1e24",
          "d6f2f4c9d95818c5ffffc7e0801c0f71", "5ef2f2488059180dfffb0dfc004c1e24", "d2f2d4c8d9480845fffdc7e0812e0771",
          "96f2f5c8d148180dffff05e8002c0b31", "0ef2f7c8c149188cfffb05fc004c1aa4"]),
    ];

    private static ShotScan WonderPickScan() => new()
    {
        Ok = true, Width = 1320, Height = 2868,
        Lattice = new ShotLattice
        {
            Rows = 2, Cols = 4, CellWidth = 156, CellHeight = 220,
            Confidence = 0.5, RelativeCellWidth = 0.2422,
        },
        Cells = WonderPick.Select((c, i) => new ShotCell
        {
            Row = i / 4, Col = i % 4, Hash = c.Hash, Nearby = [.. c.Nearby],
            Luma = 0.6, Saturation = 0.5, Detail = 0.075,
        }).ToList(),
    };

    [Fact]
    public void AWonderPickLineUpIsReadFromTheNudgedCrops()
    {
        var reading = new ScreenshotReader(Ix, Table).Read(WonderPickScan(), CardScreen.WonderPick);

        Assert.Equal(WonderPick.Select(c => c.Key), reading.Matches.Select(m => m.Card.Key));
        Assert.All(reading.Matches, m => Assert.False(m.IsMarginal));
    }

    [Fact]
    public void WithoutTheNudgedCropsThreeOfThoseFourAreLost()
    {
        // The same cells with only the centre crop, which is what the reader used to get. Kept as a
        // test because it is the evidence for carrying nine crops instead of one: the cost is real
        // and this is what it buys.
        var scan = new ShotScan
        {
            Ok = true, Width = 1320, Height = 2868,
            Lattice = new ShotLattice
            {
                Rows = 2, Cols = 4, CellWidth = 156, CellHeight = 220,
                Confidence = 0.5, RelativeCellWidth = 0.2422,
            },
            Cells = WonderPick.Select((c, i) => new ShotCell
            {
                Row = i / 4, Col = i % 4, Hash = c.Hash,
                Luma = 0.6, Saturation = 0.5, Detail = 0.075,
            }).ToList(),
        };

        var reading = new ScreenshotReader(Ix, Table).Read(scan, CardScreen.WonderPick);

        Assert.Single(reading.Matches);
        Assert.Equal("A2b-33", reading.Matches[0].Card.Key);
    }

    /// <summary>
    /// IMG_1157, a pack's Opening Results: five cards from Wisdom of Sea and Sky, laid out three
    /// then two. Verbatim <c>scan()</c> output.
    ///
    /// This is the fixture that closed the pale-card gap. Dunsparce, at (1,1), is a white-bodied card
    /// the mask does not find at all — no colour to catch and only sparse text — so its slot exists
    /// only because the row's other card fixes the phase and the column pitch predicts where the
    /// second one must be. Before columns were worked out per row, a hand's second row was offset
    /// half a pitch from the first and its slots were never looked at.
    ///
    /// Three of the five are exclusive to the Lugia pack, so this also exercises the pack being named
    /// from the cards rather than asked for.
    /// </summary>
    private static readonly (string Key, string Hash, string[] Nearby, double Detail, double Saturation)[] PackReveal =
    [
        ("A4-114", "0c6a2f2c6572a4b7d93b00ff609b84cf",
         ["6beb6f6c6572adb7fb0004ff609b04ff", "596b6f6c6572a1a7d93b00ff60bb8c77", "dd6b6f7c6563b1f7003b00ff60338c67",
          "28ea2e646572a4b7ff0000ff609b04df", "cc6a2f2c6572b4a7003b00ff60338c6f", "296b2f286572a4b3ff0000ff709b04df",
          "4d6b2f2c6572b4b3f93b00ff709b04cb", "cc6b272e6572b4ab003b00ff70138c6f"], 0.091, 0.560),
        ("A4-90", "0ef2f7b3f9e87032fffb0db0fcff1802",
         ["06f2f6b1f8e87232ffff04b0feff1002", "56e2f6b1f1e87032fffb0db0fcff1002", "5ee6f2b1f1e870b21ff33db078ef1102",
          "06f2f4b1f8f87032ffff04b8feff1002", "5ee2f3b1f1e870721ffb1db07cff1802", "86f3f5b1f8757872fffd04b8feff0803",
          "0ef2f5b3f1f47932fffb0db07cff1802", "0ef2f3b3f1e479721ffb1db07cff9802"], 0.067, 0.293),
        ("A4-73", "5ef2f6141a32448cdfff0cff0378e5de",
         ["57f6f63632524c0effff0cff017ce700", "5ef6f2363272448cffff0cff0378e7fe", "5e77f2363272468c19f7aeff1378e7ff",
          "5ef2f6161a72c48effff0cff007ee508", "5e72f2141232468c19fb3fff1b38e5ff", "0ef3f5161332a48bffff04ff007ee508",
          "4e73f7141a32648effff04ff013ae5df", "4e73f39812326e8c19fb7fff1b38e5ff"], 0.056, 0.290),
        ("A4-144", "88c6ac1ca1e9687308433f0000ff06c3",
         ["8996ac1da1e1696300c73d00e07f06d3", "89d6ac1da1e1697300473f0000ff07d3", "89962c1da5e16973f802ff0000fb2f83",
          "c896bc0ca169787300d73d00f0ff06f3", "88c6bc1ca5f16873fc02ff0000ff2f83", "c886bc0c6068747300c33d0070ff06eb",
          "c886bc0c2078747308833f0000ff06c3", "8ccebc1c24707031fc02ff0000ff0781"], 0.154, 0.132),
        ("A4-59", "18ba606249095930f97f3870c00f11ff",
         ["18526864491d5b32f9ff2870c10f71fe", "18526a6445195b32f97728f4c10f31ff", "1852626045195b32fd23fff0c00f11ff",
          "18baa862490d5d33f8ff2070c10f71fe", "18ba706045095931fd23fff0c00f10ff", "18baa16289095d91f8ff3072c10f71fe",
          "98bab1f24b095d11fdb774f2c00f11ff", "98ba31e0c70b5d15fd33fff0c00f10ff"], 0.064, 0.399),
    ];

    private static ShotScan PackRevealScan() => new()
    {
        Ok = true, Width = 1320, Height = 2868,
        Lattice = new ShotLattice
        {
            Rows = 2, Cols = 3, CellWidth = 176, CellHeight = 245,
            Confidence = 1.0, RelativeCellWidth = 0.2733,
        },
        Cells =
        [
            .. PackReveal.Take(3).Select((c, i) => new ShotCell
            {
                Row = 0, Col = i, Hash = c.Hash, Nearby = [.. c.Nearby],
                Detail = c.Detail, Saturation = c.Saturation, Luma = 0.6,
            }),
            .. PackReveal.Skip(3).Select((c, i) => new ShotCell
            {
                Row = 1, Col = i + 1, Hash = c.Hash, Nearby = [.. c.Nearby],
                Detail = c.Detail, Saturation = c.Saturation, Luma = 0.6,
            }),
        ],
    };

    [Fact]
    public void AllFiveCardsOfAPackRevealAreRecognised()
    {
        var reading = new ScreenshotReader(Ix, Table).Read(PackRevealScan(), CardScreen.PackReveal);

        Assert.Equal(PackReveal.Select(c => c.Key), reading.Matches.Select(m => m.Card.Key));
        Assert.Equal(0, reading.UnreadCells);
        Assert.Empty(reading.Notes);
    }

    [Fact]
    public void AWhiteBodiedCardIsFoundBecauseItsRowPredictedTheSlot()
    {
        // Dunsparce has no colour for the mask to catch and only sparse text, so it is not found as
        // a region at all. It is read because the other card in its row fixes the row's phase and
        // the column pitch says where the second slot must be — the detector never saw a card there,
        // and the fingerprint decided.
        var reading = new ScreenshotReader(Ix, Table).Read(PackRevealScan(), CardScreen.PackReveal);
        var dunsparce = Assert.Single(reading.Matches, m => m.Card.Key == "A4-144");

        Assert.Equal("Dunsparce", dunsparce.Card.Name);
        Assert.False(dunsparce.IsMarginal);
    }

    [Fact]
    public void ThePackIsNamedFromTheCardsThatCameOutOfIt()
    {
        // Honchkrow, Slowpoke and Suicune are exclusive to Lugia, so the picture settles it.
        var reading = new ScreenshotReader(Ix, Table).Read(PackRevealScan(), CardScreen.PackReveal);
        var guess = PackIdentifier.Identify(Ix, reading);

        Assert.True(guess.IsCertain);
        Assert.Equal("A4:Lugia", guess.PackKey);
        Assert.Equal("A4", guess.Set);
    }

    [Fact]
    public void APackRevealDecidesNoOwnership()
    {
        // Every card present is a card just acquired; what that means belongs to the log screen.
        var reading = new ScreenshotReader(Ix, Table).Read(PackRevealScan(), CardScreen.PackReveal);

        Assert.All(reading.Matches, m => Assert.True(m.Owned));
        Assert.All(reading.Matches, m => Assert.Null(m.Copies));
    }

    [Fact]
    public void EveryWholeCardIsRecognisedFromARealScreenshot()
    {
        var reading = Read();

        Assert.True(reading.Ok, reading.Error);
        Assert.Equal(Whole, reading.Matches.Select(m => m.Card.Key).ToArray());
    }

    [Fact]
    public void GoldFlairNoLongerDecidesAnything()
    {
        // Three of the six are flair cards, and the game replaces a card's frame to draw it. Under
        // the old whole-card fingerprint they were 19, 21 and 26 bits out and unrecognisable.
        var reading = Read();

        foreach (var key in new[] { "A1-2", "A1-5", "A1-6" })
            Assert.Contains(reading.Matches, m => m.Card.Key == key);
    }

    [Fact]
    public void TheMeasuredDistancesAreWellInsideTheThreshold()
    {
        // Measured at 4 to 12 bits of 128. Asserted as a bound, since regenerating the table can
        // move a fingerprint a little — but 12 to 14 is the whole remaining margin, so drifting into
        // it is a regression worth failing on.
        var reading = Read();

        Assert.All(reading.Matches, m => Assert.InRange(m.Distance, 0, 13));
    }

    [Fact]
    public void NoCardIsMistakenForAnother()
    {
        // The failure that matters most. A wrong card recorded is silent, and indistinguishable
        // afterwards from one the user logged by hand.
        var reading = Read();

        foreach (var match in reading.Matches)
        {
            var slot = match.Row * 3 + match.Col;
            Assert.Equal(Measured[slot].Key, match.Card.Key);
        }
    }

    [Fact]
    public void CardsClippedByTheScreenEdgeAreLeftUnreadRatherThanGuessed()
    {
        // A partial card cannot match and must not be forced to. The detector reports the three as
        // slots holding something it could not read — detail above the floor, no fingerprint — and
        // the reader passes that through as unread rather than inventing an answer or an absence.
        var reading = Read();

        Assert.Equal(3, reading.UnreadCells);
        foreach (var key in new[] { "A1-7", "A1-8", "A1-9" })
            Assert.DoesNotContain(reading.Matches, m => m.Card.Key == key);
    }

    [Fact]
    public void AFoilPrintingIsNotConfusedWithItsPlainTwin()
    {
        // A4b-2 is the foil of A1-1 and a different ownable card. The two differ in the card's
        // treatment rather than its illustration, and an earlier, much tighter window put them 2
        // bits apart — close enough to return the wrong printing. This window separates them.
        var table = Table;
        var plain = table.Entries.First(e => e.Key == "A1-1").Hash;
        var foil = table.Entries.First(e => e.Key == "A4b-2").Hash;

        Assert.NotEqual(Ix.ByKey["A1-1"].OwnershipKey, Ix.ByKey["A4b-2"].OwnershipKey);
        Assert.True(plain.DistanceTo(foil) >= ArtHashTable.MaxDistance + ArtHashTable.AmbiguityMargin,
                    $"a foil and its plain twin are only {plain.DistanceTo(foil)} bits apart");
    }

    [Fact]
    public void TheScreenIsRecognisedAsTheThreeAcrossListWithoutBeingTold()
    {
        // Three large cards across. The other card list fits five small ones, and the detector's
        // relative card width is what separates them — 0.30 of the screen against about 0.17.
        var reading = new ScreenshotReader(Ix, Table).Read(Scan());

        Assert.Equal(CardScreen.CopiesGrid, reading.Screen);
        Assert.True(reading.ScreenWasInferred);
    }

    [Fact]
    public void NothingIsReportedMissingFromACopiesGrid()
    {
        // This screen omits cards you do not own rather than drawing them blank, so it can never
        // report an absence — including for the three slots it could not read.
        var reading = Read();

        Assert.All(reading.Matches, m => Assert.True(m.Owned));
        Assert.All(reading.Matches, m => Assert.Equal(MatchSource.Art, m.Source));
    }
}
