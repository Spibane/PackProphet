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
    /// All nine counts read. Two of them did not, once: the badge segmentation returned a sliver
    /// rather than a digit on the last two cards, and the count came back unknown. That was the same
    /// defect IMG_1188 shows at its worst — a span measured from its topmost ink to its bottommost,
    /// with nothing in between — and re-recording this fixture after the fix is what showed the two
    /// were never a property of these cards. See <see cref="Scrolled"/>.
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
        ("A1-23", "1e96268d2e95c97308f27f0032d46807", "02affffb04fffffc06fffff918ff63312aff95303dffffa33cdcfff913215ffe00002cff22102cff89526ffeffeafffcfffffff95acedb82", 0.63, 5),
        ("A1-24", "1ea6b6b0010f8fc208b20d00c0bfffff", "0002cfc10005ffe2001affe2003dffe2017fffe202dfffe205fedfe21afcbfe23dfaafe27ffadff5cffffffadffffffb8cccfff811129fd2", 0.80, 4),
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
        Assert.Equal(9, reading.Matches.Count(m => m.Copies is not null));
    }

    [Fact]
    public void ACountThatCouldNotBeReadIsNullAndSaidOutLoud()
    {
        // Null means "the screen did not say", never "none". And the reading says how many cards it
        // happened to, because a reading that named every card and read no counts looks complete.
        //
        // The unreadable badges are made rather than found, now that every badge in every fixture
        // reads. A sliver is exactly what the segmentation used to hand back on a badge it could not
        // cut — see the note on Counted — so that is what two of these cards carry.
        var scan = CountedScan();
        foreach (var cell in scan.Cells.TakeLast(2))
            cell.Digits = [new DigitGlyph { Grey = new string('0', 112), Aspect = 0.07 }];

        var reading = new ScreenshotReader(Ix, Table).Read(scan, CardScreen.CopiesGrid);

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
    /// IMG_1188: the same three-across list as IMG_1152, scrolled so that all nine of A1-1 to A1-9
    /// are whole, with badges reading 9, 11, 1, 8, 14, 20, 2, 6, 6 by eye. Verbatim <c>scan()</c>
    /// output again.
    ///
    /// This is the regression fixture for the badge bug that lost a whole row of counts. The top row
    /// of this screenshot has a stray bright pixel on the badge's own top row, in the same columns as
    /// the ribbon's rounded bottom-left corner. Nothing is between them, but the two specks sat 27
    /// rows apart in one column span, and the vertical extent was measured end to end — so an empty
    /// span measured as tall as the badge, became the tallest span on the card, and the relative
    /// filter then discarded every real digit for being shorter than it. Three cards in a row came
    /// back with one nonsense glyph each and no count at all.
    ///
    /// The other two rows of the same picture read correctly throughout, which is what made it look
    /// like a property of those three cards rather than of that one pixel.
    /// </summary>
    private static readonly (string Key, string Hash, string[] Nearby, (string Grey, double Aspect)[] Digits, int? Copies)[] Scrolled =
    [
        ("A1-1", "8e32a6d8b6a8dae3c2ff057f833c7387",
         [
          "8e36acdcb6b9f2c702ffcf5f81087781",
          "8e36a4d8b6b8d2e7c2ff057f833c53a7",
          "0ea6a4f896acd3e6c6fb05ff833c51af",
          "8e32a4dcb6a8fac302ffc75f81287381",
          "8ea2b6d886acdae3c6fb05ff833c51b7",
          "8eb2965cb6aaead382ffc74f812c7381",
          "8eb2b65ca6a8eaf3c2ff457f833c7b95",
          "8ea2b6d88fa8daf3c2bb05ff033c5ab7"
         ],
         [("016aa83016efffb25efffff7bffa8dfaefc218fcffb106fdefd429fc9fffeffa3afffff603affff2002aff81002dfd30006ffa00009fd300", 0.69)], 9),
        ("A1-2", "8632ae0d2d31b1f2c2ff01041130fc7f",
         [
          "8634ae1d2531b1f602ff81043310fe7f",
          "8636ae0d2533b1f2c6ff010c3330fc7f",
          "0eb6240d7d33b1a2c6fe018c0130c8ff",
          "8636ae0d2d31b1d202ff01043110fe7f",
          "8eb2a60d2d33b1b2c2fb058c0130ccff",
          "8e32ae0c3531b1d282ff01043110fe7f",
          "8eb2ae0c3531b1d1c2ff01841130fc7f",
          "8eb2a68c2d33b1f1c2fb05841130ccff"
         ],
         [("44788873aafffff8aafffffa66cffffa114dfffa002cfffa002cfffa002cfffa002cfffa002cfffa002cfffa002cfffa002cfffa002afff9", 0.38),
          ("66888885fffffffcfffffffe99ffffff11aaffff0099ffff0099fffe0099fffe0099fffe0099fffe0099fffe0099fffe0099fffe0077effc", 0.31)], 11),
        ("A1-3", "0ea2a6efc9b0d3c3c0ff0700e8bfff2c",
         [
          "84b6a4cfdbb5d3c300ffc700f8beff0f",
          "1ca4a4eecbb1d3c3c0ff0f00c8beff28",
          "1ca4a4eecbb1d7c3c0f41f00c0bef7f8",
          "8eb2b6efc9b0c3c380ffc500f8bfff0e",
          "0ea6b6eec9b1d7e3c0fa1d00c0bff7f8",
          "8eb2966fc9b2e3e380ff4500f81fff06",
          "8ea2b66fc9b2e3e3c0ff4500e81fff04",
          "0ea2b66fcdb0e3e3c0fa1d00c03ffff0"
         ],
         [("338bbb7277fffff688fffff844dffff8003cfff8001afff8001afff8001afff8001afff8001afff8001afff8001afff8001afff80019fff6", 0.44)], 1),
        ("A1-4", "8ea64a7a38595173c3ff00039cc7f01b",
         [
          "8ca64a7639593173c2ff00139cf7f147",
          "8ca64a763819717383ff00039cc7f15a",
          "8ca64a5e3a19557381ff0803bcc7f03a",
          "8ea64a3a385971f3c2ff00139cf7f117",
          "8ea66ace3a19557181bf0803bcc5f01b",
          "cea24e3a385951f142ff00139cf7f013",
          "cea24e1b385d59f1c3ff00039cc5f01b",
          "ceaa6a8b381d557101bf00019cc5f23f"
         ],
         [("015bcb8216dffff63bfecffb5ffb4bfd6ff828fd5efb5bfc28ffeff839fffff97ffc8cfdaff826ffbff715ffaffa49ff6ffecffd16befec6", 0.63)], 8),
        ("A1-5", "1e16a6af3994b91500f73d003cf70943",
         [
          "1c16aeaf71b5b91500f72d007ef709c3",
          "1c1624af3195b91500f72d003cf60943",
          "1c1624af3194b9111e00ff001cfe0943",
          "1e16aeaf3894b51500f72d007ef70943",
          "1c16a6af3994b9151e01ff001cff0941",
          "1e16ae0f3894b51500f72d007ee71043",
          "0e16a60f3894b41500f33d003ef70843",
          "0e16b62f38d4bc158e01ff001cff8841"
         ],
         [("4488887488efffe777dffff8337cfff80029fff80029fff80029fff80029fff80029fff80029fff80029fff80029fff80029fff80016ccb6", 0.38),
          ("000028a200015ef40003aff40007dff4002bfff4005edff402ae8df416e93cf44bf73cf67efa8ef9affffffd9ccccffc34555df7000009c3", 0.69)], 14),
        ("A1-6", "86b2a62929375357c3fb04000013e8ff",
         [
          "8e32ae692937d3f7c3fe01000013e8ff",
          "0ea6a4a92937d3d7c7fa05000017e8ff",
          "1ea624a92937f3d79ef11c000117f8ff",
          "86b2ae292d375157c3ff01000013e8ff",
          "0ea6b629293573578fb11c000017f8ff",
          "86b2a63925336155c3ff01000013e8ff",
          "86b2b63925716155c3fb04000013e8ff",
          "8ea2b629217573558fb00c000013f8ff"
         ],
         [("027aa83027effe825cfddfd48fe76df78da21af947511af900004df800039fb50017ee63004cfb21018ff71114cfe75338fffdc75acdccc7", 0.69),
          ("015aa93004cfffa228fedff55cf96bf87ed317fb9fc204fcbfa103ddbfa103deafa103ed9fc204fc7ed317fb5ce85bf938fecef5129dec71", 0.69)], 20),
        ("A1-7", "963216f89adccd9b02b8007f8bc0701b",
         [
          "963292f89a9c85939238007f81c0f113",
          "963252f89a9c859312f8007f8bc0f03b",
          "963252fc9a9c9d930038007f8fc0f03b",
          "963294f89adcc5cbd238007f8160711b",
          "863212fc9a9ccdb90038007f8fc0f03b",
          "863284fa9eccc5cbd238007f8160300b",
          "863294fa9acecde902bc007f8340701b",
          "863294fe9acecce90038007f8fc0703b"
         ],
         [("039fffc33bfffffa7fffdffdbffa39ffaff515ff344116ff00005dfe0003eff90007fff4002cff91017ffc5215effda84cffffff6bcccccc", 0.63)], 2),
        ("A1-8", "1eb6a60c382c2d321eb00d0009b7b9da",
         [
          "14b6241d396c2a32d6fa01000ff7bbd2",
          "1cb6240d396c2c321ef00d000b7fbbd2",
          "1cb6649d1b6c2c320c306f00007fbbba",
          "16b6a60c282c2d32d6fa05000fb7b9d0",
          "1eb6268c192c2c320e306f00003fb9da",
          "86b2b68c2c243533d2fa05000fb7b950",
          "0eb2b68c2c2435320eb00d000db7b9da",
          "0eb2b68c0d2c35320e302f0000b799db"
         ],
         [("0004e810000afc20003df910018ff40003efc30007fff9302cffffd45ff85efa8ff31bfb8fd20afb7ff41bfb5ff97ef92cfffff504bddc61", 0.75)], 6),
        ("A1-9", "0ea6b693227265530eb01dc03a4ea5cb",
         [
          "0ea6a49322646553c6fa05803a4481cb",
          "1ea62693226665531eb01d803a4ea1cb",
          "1ea62693226265730e207f80324e25cb",
          "86b2b69322627553c2fa05803a4681cb",
          "0ea6b693b37264330e303fc0384e25c1",
          "86b2b693b2723553c2fa05c03a4681cf",
          "0ea2b693b37274538eb01dc03a4e85c9",
          "0ea6b693b37274310c303fc0386e04c1"
         ],
         [("0002ad400017ff71003efe40007ffb1002bff81005effc724cfffff98ffa5bfebfe515ffbfd304ffafe516ff7ffb7cfd4cfffffb03adeda3", 0.69)], 6),
    ];

    private static ShotScan ScrolledScan() => new()
    {
        Ok = true, Width = 1320, Height = 2868,
        Lattice = new ShotLattice
        {
            Rows = 3, Cols = 3, CellWidth = 192, CellHeight = 268,
            Confidence = 1.0, RelativeCellWidth = 0.2981366459627329,
        },
        Cells = Scrolled.Select((c, i) => new ShotCell
        {
            Row = i / 3, Col = i % 3, Hash = c.Hash, Nearby = [.. c.Nearby],
            Luma = 0.64, Saturation = 0.49, Detail = 0.08,
            Digits = c.Digits.Select(d => new DigitGlyph { Grey = d.Grey, Aspect = d.Aspect }).ToList(),
        }).ToList(),
    };

    [Fact]
    public void EveryCountOnAFullPageIsRead()
    {
        // Nine cards, nine counts, none of them null. The row that used to come back empty is the
        // first one — 9, 11 and 1.
        var reading = new ScreenshotReader(Ix, Table).Read(ScrolledScan(), CardScreen.CopiesGrid);

        Assert.Equal(Scrolled.Select(c => c.Key), reading.Matches.Select(m => m.Card.Key));
        Assert.Equal(Scrolled.Select(c => c.Copies), reading.Matches.Select(m => m.Copies));
        Assert.DoesNotContain(reading.Notes, n => n.Contains("could not be read"));
    }

    [Fact]
    public void ATwoDigitCountIsNotReadAsOneDigit()
    {
        // The specific way this failure is dangerous. 11 and 14 and 20 all begin with a digit that is
        // a valid count on its own, so dropping the second one is silently plausible — and a card
        // recorded as holding 1 when it holds 11 is indistinguishable afterwards from a typo.
        var reading = new ScreenshotReader(Ix, Table).Read(ScrolledScan(), CardScreen.CopiesGrid);
        var byKey = reading.Matches.ToDictionary(m => m.Card.Key, m => m.Copies);

        Assert.Equal(11, byKey["A1-2"]);
        Assert.Equal(14, byKey["A1-5"]);
        Assert.Equal(20, byKey["A1-6"]);
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

    /// <summary>
    /// IMG_1150, a pack's Opening Results for Team Rocket's Ambition: five cards laid out three then
    /// two, and every one of them white-bodied. Verbatim <c>scan()</c> output.
    ///
    /// This is the picture the detector used to return nothing at all for. A card with a small
    /// illustration panel over a large white body masks as two pieces — a coloured panel and a strip
    /// of attack text — with an unmasked white band between them, and neither piece is card-shaped.
    /// On every other screen a pale card is recovered because its row holds a card that was found,
    /// which fixes the phase; here there was no such card anywhere, so there was nothing to extend
    /// from and no size to pin the grid to.
    ///
    /// The pieces are now joined back into a card when, and only when, the mask found nothing
    /// card-shaped in the whole picture. The lattice below is what that produces: 172x240, against
    /// the 176x245 the same screen measures on IMG_1157. It is four pixels narrow because nothing in
    /// a picture of only pale cards ever reaches a card's border — the mask can fall short of an edge
    /// but never past one, and here every region falls short. That is what the grown crops in
    /// <c>nudged</c> are for.
    ///
    /// Its set is not in the fingerprint table, so this fixture cannot show a card being named. What
    /// it does show is the other half of the contract: five slots found, and every one of them left
    /// unread rather than pushed onto the nearest thing in the table. The nearest entry to any of
    /// these crops is 21 bits away against a threshold of 18.
    /// </summary>
    private static readonly (int Row, int Col, string Hash, string[] Nearby, double Detail, double Saturation, double Luma)[] PaleReveal =
    [
        (0, 0, "30c6ceae97b331f1f1628e0000f8ff7b",
         ["90e6ce969399a93bf1630c0000fcff1f", "14c6cca5b791b3b3f1620c0000fcff1f", "15c6dcada73133f3f1660c0001f8ff5e",
          "14c6cca5a52133f3f1628c0000f8ff7f", "30c6ceada5a321f3f1429e0000f8ff7f", "30c6ceaca5a321f3fb429e0000f0ff7f",
          "14c6cea697b1b1f1f1638c0000f8ff7f", "38c6ceac95b321b3fb604e0000f8ff7b", "90e6ceb6939191b9f1630e0000f8ff3f",
          "30e6cea6979391b9f9624e0000f8ff3b", "18e6cea6969311b9fb604f0000f8ff3b"], 0.118, 0.212, 0.661),
        (0, 1, "89a669a1c9a3569a00876000c837469d",
         ["acb229a1e927dfdb00ff2000ff07ff10", "8cb629a1e9369a9300ff0000ff07ff30", "8d3649a1d34a9a9300ff0000ff07df30",
          "1cb649a1d972da9a00ff0000d807df9c", "98a649a1d933d29a00874000c8274e9d", "88a649a9d122529a1886600000bf479f",
          "acb629a1c933de9a00ff0000fc07df9c", "89ae69a9c923569a0887600440bf479f", "acb229a9e927579a00ff0000fc07ff9c",
          "88a629a9c9a5579a08876000489747ce", "88ae29a9c9a1579b0887600240b747df"], 0.077, 0.153, 0.779),
        (0, 2, "8ccc6c6b3c773fedff003f000471cf28",
         ["8ca6bc2f3c3f3ffde03a1f0004e7e97c", "8ccc2c2d3d3f3fedc03a1f000ce7e8f8", "8cc4783d797f7fedc13a1b0008c7f8f0",
          "8cc4787739773fedc03a3f000cf3ce6c", "8ccc68673d773fedff003f000c71cf20", "8ccc786d3d753fedff003f000071cf29",
          "8ccc2c6f3c773fedc03a1f000cf3cf2c", "8c8c7c6d3c753fedff003f000071cf29", "84a6ac2b3c3f3fedc03a1f0004f3cf3c",
          "8c862c6b3c3d3fedff003f000471cf38", "8c847c6f3e353fedff003f000071cf28"], 0.087, 0.129, 0.730),
        (1, 0, "30e6cecde3d9e175f163ce0000fe79b7",
         ["b0e6ce6973696155b1730c0001fcf1cf", "b4e6cee3e3f96137b1670c0001fc718f", "35c6ccf3e3d9e1b731e68c0001fc719f",
          "34e6ccd1e3d9e16531e78c0000fc71af", "30e6ce99e3f9f1e1f162ce0000fc71a7", "30e6ce8ce3f9d1e1f900ce0000fe7927",
          "34e6cef1e3d9e17531670c0000fc719f", "38a6ce8ce3f9e165f900ee0000fe78b7", "b2e6cee9e3f86175b1730c0000fe70df",
          "38a6cecce3f86175f1234e0000fe7897", "18a6cecc61f86975f9006f0000fe78b5"], 0.118, 0.250, 0.632),
        (1, 1, "31e6cab119b7d5b57900c01100f7ddac",
         ["31e6c8731d339da87900cc108773ef38", "31c7c873b93795b37100dc118f71cf3a", "71c799d3b9359da37300d8118d71cf30",
          "71c798d3b9b795b97100c8110977dfa8", "31c79b9399a795bd7100c81100f7df28", "11e7cb9593afd5bdf100c83400e755ac",
          "31c6887399b795b57100c8110cf7cfb8", "14e7ca9411a7d595f900c81500e75d8c", "31e6ca7319b695957900cc1104f3cfbc",
          "31e6ca3119b6d5b57900c01100f74d94", "19e7ca1019b6d595f900c81500f7498c"], 0.074, 0.254, 0.697),
    ];

    private static ShotScan PaleRevealScan() => new()
    {
        Ok = true, Width = 1320, Height = 2868,
        Lattice = new ShotLattice
        {
            Rows = 2, Cols = 3, CellWidth = 172, CellHeight = 240,
            Confidence = 1.0, RelativeCellWidth = 0.2671,
        },
        Cells =
        [
            .. PaleReveal.Select(c => new ShotCell
            {
                Row = c.Row, Col = c.Col, Hash = c.Hash, Nearby = [.. c.Nearby],
                Detail = c.Detail, Saturation = c.Saturation, Luma = c.Luma,
            }),
        ],
    };

    [Fact]
    public void APackRevealOfOnlyPaleCardsIsFoundAtAll()
    {
        // The whole point of the fixture. Five slots, three then two, on a screen that produced no
        // regions the mask called card-shaped — so every one of them is a card assembled out of its
        // own illustration panel and attack text.
        var scan = PaleRevealScan();

        Assert.Equal(5, scan.Cells.Count);
        Assert.Equal([0, 0, 0, 1, 1], scan.Cells.Select(c => c.Row));
        Assert.Equal([0, 1, 2, 0, 1], scan.Cells.Select(c => c.Col));
        Assert.All(scan.Cells, c => Assert.True(c.Detail >= ScreenshotReader.DetailFloor));
    }

    /// <summary>
    /// The committed table with B4a taken out of it, which is the state it was in when this
    /// fixture was captured -- and the state any set is in until a refresh has seen its art.
    ///
    /// Worth keeping as a table rather than deleting the two tests below it. The property they
    /// assert is that a card the table does not hold is left UNREAD rather than named, and that
    /// property does not stop mattering because one set stopped being an example of it. It is the
    /// reason the importer can be trusted at all on the week a set lands.
    /// </summary>
    private static ArtHashTable WithoutTheNewestSet =>
        new(Table.Entries.Where(e => !e.Set.Equals("B4a", StringComparison.OrdinalIgnoreCase)),
            Table.Generated);

    [Fact]
    public void ASetTheFingerprintTableDoesNotHaveIsLeftUnreadRatherThanGuessedAt()
    {
        // Finding the cards must not turn into naming them: with B4a absent, the nearest entry to
        // any crop here is 21 bits away, against a threshold of 18 and a margin of 6.
        var reading = new ScreenshotReader(Ix, WithoutTheNewestSet)
            .Read(PaleRevealScan(), CardScreen.PackReveal);

        Assert.Empty(reading.Matches);
        Assert.Equal(5, reading.UnreadCells);
    }

    [Fact]
    public void EveryCropOfAnUnknownPaleCardStaysOutsideTheMatchingThreshold()
    {
        // The assertion behind the one above, stated in bits rather than in outcomes, so that a
        // future threshold change cannot quietly turn these five into wrong answers.
        var table = WithoutTheNewestSet;

        foreach (var text in PaleReveal.SelectMany(c => c.Nearby.Prepend(c.Hash)))
        {
            Assert.True(ArtHash.TryParse(text, out var hash));

            // Nearest already refuses anything past the threshold, so an empty result IS the
            // assertion: no entry in the table is close enough to be offered as a candidate.
            Assert.Empty(table.Nearest(hash));
        }
    }

    [Fact]
    public void TheSameRevealIsReadOnceTheArchiveHasSuppliedItsArt()
    {
        // The payoff, end to end, on a real screenshot.
        //
        // This fixture is a Team Rocket's Ambition pack reveal, and for a fortnight it was
        // unreadable: B4a's card data was published on 2026-08-27 and its ART was not, so the
        // fingerprint refresh -- which downloads from the art CDN -- had 0 of its 110 cards. The
        // art existed the whole time inside the release archive that the DEPLOY was already
        // extracting from, and once the refresh read the same archive all five slots resolved.
        //
        // Which is why this test is here rather than a count in a workflow log. A directory
        // argument going missing is invisible from a green run; a pack reveal going back to
        // unreadable is not.
        var reading = new ScreenshotReader(Ix, Table).Read(PaleRevealScan(), CardScreen.PackReveal);

        // Ix is the snapshot and predates B4a, so the reader cannot name these cards -- it has no
        // card to name. The fingerprints are the half this fixed, and they are what is asserted.
        var matched = PaleReveal
            .Select(c => c.Nearby.Prepend(c.Hash)
                          .SelectMany(text => ArtHash.TryParse(text, out var h)
                                                  ? Table.Nearest(h) : [])
                          .Select(m => m.Entry.Key)
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToArray())
            .ToArray();

        // Each cell agrees with itself. Eleven crops of one slot -- the cell's own fingerprint and
        // ten neighbouring offsets -- all landing on ONE card is what separates a real
        // identification from a table dense enough to match anything: a coincidence does not
        // survive being re-cropped ten times.
        Assert.All(matched, m => Assert.True(m.Length == 1,
            $"one slot matched {m.Length} different cards: {string.Join(", ", m)}"));

        // Tinkatink, Furfrou, Lechonk, Tinkatuff, Gholdengo -- in reading order, and every one of
        // them a Common or Uncommon, which is what a pack reveal is mostly made of.
        Assert.Equal(["B4a-48", "B4a-64", "B4a-65", "B4a-49", "B4a-51"],
                     matched.Select(m => m[0]));

        // And no cell is offered as a match against the snapshot, because the snapshot has no B4a
        // cards for a fingerprint to point at. The reader is right to hold them back; a deploy
        // takes the live card data and the same five become named.
        Assert.Empty(reading.Matches);
    }
}
