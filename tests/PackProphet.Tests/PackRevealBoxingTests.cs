using PackProphet.Vision;

namespace PackProphet.Tests;

/// <summary>
/// Three real pack-reveal screenshots of the newest set, as <c>scan()</c> in wwwroot/js/cardshot.js
/// reports them, checked against the committed fingerprint table.
///
/// What is on trial is BOXING: where the detector cut, not how the table is searched. A bulk import
/// of twenty-three pack openings came back with most cards unread, and all of it was three ways of
/// putting the box in the wrong place — recorded one per fixture below. The fingerprint has no
/// tolerance for that, four pixels being the difference between a card at 4 bits and the same card
/// at 30, so the distances asserted here are a direct measurement of where each box landed.
///
/// They are a measurement and not a guard, and the difference matters. The cells are the fixed
/// detector's output frozen into C#, because the detector is JavaScript and the repository has no
/// way to run it — the same arrangement, and the same limitation, as
/// <see cref="ScreenshotEndToEndTests"/>. So editing wwwroot/js/cardshot.js cannot fail this test;
/// it obliges someone to re-record it. What it does hold to account is everything downstream of the
/// cut — the table, the thresholds, the ambiguity rule, a regenerated set of fingerprints — against
/// screenshots whose right answers are now known, and it keeps the three bugs written down where
/// the next person to touch the detector will find them.
///
/// Checked against <see cref="ArtHashTable"/> rather than read through <see cref="ScreenshotReader"/>,
/// which is the one thing here that is not the app's own path. These are B4a cards; B4a's
/// fingerprints are committed but its CARD DATA is not in the vendored snapshot the tests load, and
/// the reader resolves every match through <c>CardIndex.ByKey</c> before returning it. So a reading
/// would come back empty for a reason that has nothing to do with what is being tested. The rule
/// applied below is the reader's own — nearest within <see cref="ArtHashTable.MaxDistance"/>, clear
/// of the runner-up by <see cref="ArtHashTable.AmbiguityMargin"/> — with entry keys standing in for
/// ownership keys, which is exact here because B4a reprints nothing.
///
/// The three were picked for the three distinct ways the boxes were wrong, each recorded on its own
/// fixture below. All of them read 5 of 5 now; they read 0, 2 and 4 before.
/// </summary>
public class PackRevealBoxingTests
{
    private static ArtHashTable Table =>
        ArtHashTable.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "card-hashes.txt")));

    /// <summary>
    /// IMG_1147. Every card carries the game's "NEW" flash, which is drawn ABOVE the card's top
    /// edge, so every masked region came back 264 tall where the card is 245 and the consensus
    /// height was taken from the tallest of them. All five boxes were a row of pixels too tall and
    /// too high, and the five cards read at 21 to 30 bits: nothing recognised at all.
    ///
    /// The worst case rather than an unlucky one. The flash marks a card the player does not own,
    /// so it is on every card of a set they have just started opening — which is the newest set,
    /// which is the set being opened.
    /// </summary>
    private static ShotScan AllFlashed() => Reveal(0.8,
    [
        ("B4a-41", 0.5062, 0.0742, "9c6a2f1e3a0e3b95dffb00ff00e77987",
         ["9dca6f1e3e2e13b7ff7300ff008f718f", "9dea6f1e3a2e1b95dffb00ff00c77987", "ddca6f1e322c3ab500fb00ff11c6f985",
          "9c4a2e1e3e0e1197ff7300ff00c7718f", "dc6a2f8e320e3b9510fb00ff11e6f985", "8c4a2f1e3f1e119fff7300ff00c7788f",
          "cc4a2f8e3b1e3997fffb00ff00c77883", "dc4a278e1316291700fb00ff11e67942"]),

        ("B4a-6", 0.5967, 0.0808, "1eb3228ee735b1790419fe06009cf805",
         ["3432628e6e35b17900bfe806209cf94f", "1c33621e4e3db1790433fe06009cf90c", "1e33329e4e2db1790011ff0600bcf805",
          "1833228eaf35b17d00bf6407209cf907", "1eb3328ecf35b1790011ff06009cf805", "1abb338ea714b17d00bf740300dcf807",
          "1ebb330ea714b17d8e19fe07009cf805", "1ebb338e2734b17d0011ff06009cf805"]),

        ("B4a-1", 0.4912, 0.0671, "1ca664c49e362709def00d408837f8cc",
         ["0c646ccc9a64275bc6ff01448977b8c8", "1c6464cc9a6667199f7009408837f0c8", "1c6464ed9e2667091f703f408237f0c8",
          "067624c49a36234bc6ff01448937b8cc", "1ea666e49e3637091e303f00823770c8", "86b2a6c49d36370bc6fb05448937b8c8",
          "1eb626e49e363709dff00d44803778cc", "1ea626e4ce3737091e303f04023770c8"]),

        ("B4a-19", 0.5852, 0.0620, "96876932d1899954c2b33c1af08703f6",
         ["96266872d189b197807f0810f00f12e3", "96866972f1899994c6237818f08f02f6", "56966132f3899994ff007c0af08f03f6",
          "96a36972d189985680ff0018f0871277", "50966138f1899996ff007c0a708f03f6", "96a36930d1e99855c0bf0018f0870a77",
          "96976138d1c99854c2b33c1870870bf7", "c196613871c1a994fd803c0870c703f6"]),

        ("B4a-68", 0.2344, 0.1115, "acacacf3d7d96d8900de0000c40d20ff",
         ["2dacfde7dbf9699901fe0000883c00ff", "2dacace7d7d9699901de0000800c20ff", "acacacf7d7db698901020000c00df4f3",
          "acacfde3dbf9698800de0000883c00ff", "acacacf3d7d96d0900021000c20df4fb", "acacfdf3dbfd6d8800ff0000c43c00ff",
          "acacacf3cbd96d0900ff0000c40d20ff", "acacacf3d3d96d4900030000c20df4fb"]),
    ]);

    /// <summary>
    /// IMG_1145. A mixed pack: three grass cards with coloured bodies, and two Team Rocket cards
    /// that are mostly white. The mask finds a coloured card as one shape and a white-bodied one as
    /// two or three disconnected pieces, so the bottom row of this reveal was not found at all —
    /// the scan reported one row of three, and the two cards in the second row were never looked at.
    ///
    /// Both failures at once, which is why this one is here: the three cards that WERE found are
    /// also all flashed, so their boxes carried the height error too. Two of five read.
    /// </summary>
    private static ShotScan MixedWithAPaleRow() => Reveal(1.0,
    [
        ("B4a-3", 0.4381, 0.0625, "1ca6269eac54272700b67ddf255181f8",
         ["1eb6a6de4c1637c300ff6ddf2593b068", "1ca6ac9c4d1627d300feefdf6592a858", "3da5ad9ccc5626d301feefdf4512a0d0",
          "1c246c9ccc542e0700f669ff451320f0", "1c34649c8cd42e2600b67ddf2551a2f0", "1c34649cacd526260800ffdf2751a2f8",
          "1ca6249ecc54270300f67dff2511a1f8", "1cb6669eac55172708807fdf275183f8", "1eb6a69ecc55378300f76dff251181f8",
          "1eb6269ecc55172300b27dff271181f8", "1eb6269ecc4517230c807fcf270191f8"]),

        ("B4a-4", 0.4503, 0.0462, "0ea626fcf2cac2f6cef01d0c708ff117",
         ["8eb2b660f0eae653c2ff4720fee19708", "8eb2a6e8f0eac6d3c6ff4720fee19708", "8c34adf1d5c3c5b3c6ff8f10fee3b718",
          "0e662478d0c2c6f6c6ff017c7881e717", "1ce6647cf2d0c276def01d1c7085e117", "1ca6647ce2d4caf41e603f0c708fe017",
          "0ea6a6f8d0eac6f6c6ff017c78a1f31d", "1ea626bce2d8caf61e203f0c708fe017", "8eb2b6f4f2eac2f2c2ff013e78e1d31d",
          "0ea6b6bcf2eac272ceb01d0c788df117", "0ea636bcf2cacaf60e203f0c788ff117"]),

        ("B4a-2", 0.4936, 0.0787, "18a6641aa793b0b118725f0020dcff01",
         ["1cb6b41b57d091d300f74f00f1dead63", "19a6ac3b6793b1c300f6cd00f1fe8d62", "19e5ad33a7b3b1a301f69f00e1fe15e2",
          "19e66c33afb3b1b500f60900e1de7e41", "18e46433afb3b1b118669f0021d87f41", "186464b3afb3b1311820ff0000d8ff01",
          "18a6241aa793b0a100f70d0061debe61", "18a6669aa793b0311820ff0000d8ff01", "1ca6a61ae791905100f70d0061debe61",
          "1ca6261aa791b01108324f0020dcbe41", "1ca6261aa79390311c30ff0800d8bf01"]),

        ("B4a-60", 0.1217, 0.0733, "86a6acdb630b7399f03a1f0032893285",
         ["c6babe5363091b49e03f00003ec90264", "86b2ac5b6b1b3b19e03f000036d90244", "84f2acdb6b1bb39be03f00003e9800c4",
          "84f2a8db621b339be03f000036893004", "84e6acdb621b331bf03a1f003288308e", "8ce66cdb63cb73bbff201f003288308e",
          "86baacdb630b3399e03f040036893205", "8ca62cdb634b739bff201f0032c83086", "86babc5b63093309e03f040036cd3a85",
          "86a2ac5b73497389f03a0f0032cd3a87", "8ca6bc5b73497399ff201f0032cc3a87"]),

        ("B4a-49", 0.2465, 0.1218, "b0e6cee9e3f86175f1634e0000fe70df",
         ["9ae6ce6971697118b07784007f7cf1df", "b2e6ce637b696119b1778c007f7cb1df", "b5e6cce3f7796139316f84007f78b3df",
          "34e6cee3e3d9e13721e70c0001fc718f", "30e6cef1e3d9e175f163ce0000fc719f", "30a6cecde3d9e175f900ce0000fe7197",
          "b2e6cee9f1f8617530770c0001fc71cf", "38a6cecc63f86175f9026e0000fe7897", "bae7ce6971686155b077060000fc71cf",
          "bae6ce6871686075f1634e0000fe78d7", "38a6ce4c71786075f9006f0000fe7897"]),
    ]);

    /// <summary>
    /// IMG_1207. The middle card of the top row is pale and was not found, so the only two cards the
    /// top row offered sat two columns apart and their gap was taken as the column spacing. At
    /// double pitch the row tiles to two slots and the card between them is never emitted: four
    /// slots for a hand of five.
    ///
    /// It is also the fixture that showed tiled positions displacing found ones. The bottom row's
    /// right-hand card sits at x=332 and the tiling put a slot at 326; the two are half a pitch
    /// apart, so they were treated as one slot and the tiled one won on sort order. Six pixels: that
    /// card reads at 10 bits from its own box and 27 from the tiled one.
    /// </summary>
    private static ShotScan APaleCardMidRow() => Reveal(1.0,
    [
        ("B4a-40", 0.5178, 0.0935, "8c792f2263565543ff0900ff601799cb",
         ["c869a6636356418fff00fff92097fd87", "9979a7636355510fff00fffb6096f987", "995b2f634355514fff00fffb6306f98f",
          "9d596f62c354554fff0906ff6016b9c7", "8d596f22c3565557fd1900ff6016b9cb", "8d596f42c3565553001900ff601f81db",
          "8c792e224356514fff0902ff6017b9c3", "cc792f2263465553003900ff601f90eb", "8c692f236356514fff0802ff6017bcc3",
          "8c692f326347554bff0900ff600f94eb", "cc2927226343554b003900ff600f94eb"]),

        ("B4a-58", 0.2317, 0.1181, "8ea6aca6c961b90f00e21f00003c81c7",
         ["c6a6ac2229311d4900ff0d0008f503ff", "84a6aca769713b5900ff0d0008f103ff", "8ca6ace771e12b1300fe190008f803ff",
          "9ce62ce6d961293f00e71d00007803c7", "9ce62ce6d961e90f00e61f0000388987", "8ca66ca3c561e12fdea23f000038b907",
          "8ea6ace6e961391700f71d00083c03c7", "8ca63ca3c969f10fdea23f000038f103", "8ea6bca26860391700f31d00083c01c3",
          "8ea6bca24a68310f08f31f00003c81c3", "8ea6bca36a683117eca05f00003cf103"]),

        ("B4a-4", 0.4494, 0.0483, "0ea664fcf2d8c276def01d0c7085e117",
         ["8eb2a6e0f0eae6d3c2ff4720fee19709", "8e34ace8d0cac7d7c6ff8700fee19709", "8d65adf5d5c3c5b7c4fe8f10fdc3a71b",
          "0c646c7cf4d2c677c6ff017c7083e717", "1c64647ce4d282659f70091c7083e017", "1c64647ce4d4cae51f607f0c708fe017",
          "0e6624f8d0cac6f6c6ff017c7881f71f", "1ea666fce2d0caf61e203f0c708fe017", "8eb6a6f8f0eac2f6c2ff017e78a1d31f",
          "0ea626fcf2eac276cff01d0c788ff117", "1ea626bcf2cacaf61e203f0c708ff117"]),

        ("B4a-35", 0.5514, 0.0819, "b4326ad59c4c6471d7bb64108f603188",
         ["9e31ae958c643167c3ffe600ef3018e7", "9633ae958c64616793ffe600ef3038c7", "b7732f9119656167977be600ef3039c7",
          "b6726a511c4c6561977fc400cf603181", "b6326a511c4c6561d73bc0108f643189", "f1366a711d4c64611182ed0087ec3008",
          "b6336ad59c4c6461937fc600cf703181", "f5b268d59c4c647111826d1087ee3108", "9e312c959c44647583ffe200cf7018c1",
          "bdbb2d959c4c6471d7bb64108f701088", "f5ba29949c4c6470118a7d00876e1018"]),

        ("B4a-78", 0.5758, 0.0793, "4c9382c391d36ca97580f4f1dd1f2ef9",
         ["6dca03c1d31aad6b33c0b5f90a1e65e8", "75c287c3d313ad2b57c0f7f9171f2df0", "5d938783d317adbb5380e3f9171f2dd0",
          "dd9286c393976db15780eff19d1f2fd1", "5c9382838b936fb56780ecf19d172fd1", "dc938ac38b937fbd4581e871dd172ff1",
          "549287c391d72ca957c0e5f19d1f2ef9", "cc9382c381d37ea96581e471dd1f2ea9", "64ca87c1915a3ca957c0f5f10c1e2ee9",
          "6ccb83c381533ca975c0f4f1cc1e2ee9", "eceb82c381d33ea96481f471cd1e2ea9"]),
    ]);

    /// <summary>
    /// A hand of five as the module reports one: two rows, three across, at the size and relative
    /// width these screenshots actually measure. The cells go in reading order; a reveal's second
    /// row holds two, so the last cell of each takes the column the layout gives it.
    /// </summary>
    private static ShotScan Reveal(
        double confidence,
        (string Key, double Saturation, double Detail, string Hash, string[] Nearby)[] cells) => new()
    {
        Ok = true,
        Width = 1320,
        Height = 2868,
        Lattice = new ShotLattice
        {
            Rows = 2, Cols = 3, CellWidth = 176, CellHeight = 245,
            Confidence = confidence, RelativeCellWidth = 0.2732919254658385,
        },
        Cells = cells.Select((c, i) => new ShotCell
        {
            Row = i / 3,
            Col = i % 3,
            Hash = c.Hash,
            Nearby = [.. c.Nearby],
            Luma = 0.6,
            Saturation = c.Saturation,
            Detail = c.Detail,
        }).ToList(),
    };

    /// <summary>
    /// The reader's rule, applied to entries rather than to ownable cards: the nearest fingerprint
    /// across every crop the cell offers, accepted only if it is within
    /// <see cref="ArtHashTable.MaxDistance"/> and no different entry sits within
    /// <see cref="ArtHashTable.AmbiguityMargin"/> of it.
    /// </summary>
    private static (string Key, int Distance)? Identify(ArtHashTable table, ShotCell cell)
    {
        (string Key, int Distance)? best = null;

        foreach (var text in cell.AllHashes)
        {
            if (!ArtHash.TryParse(text, out var hash)) continue;

            var near = table.Nearest(hash);
            if (near.Count == 0) continue;

            var winner = near[0];
            if (winner.Distance > ArtHashTable.MaxDistance) continue;

            // Counted rather than fetched with a default: the entry is a struct and its Key is
            // built from its fields, so a missing rival comes back as "-0" at distance 0 and would
            // read as a tie with everything.
            var rivals = near.Where(n => n.Entry.Key != winner.Entry.Key).ToArray();
            if (rivals.Length > 0
                && rivals[0].Distance - winner.Distance < ArtHashTable.AmbiguityMargin) continue;

            if (best is null || winner.Distance < best.Value.Distance)
                best = (winner.Entry.Key, winner.Distance);
        }

        return best;
    }

    public static TheoryData<string, ShotScan, string[]> Reveals => new()
    {
        { "IMG_1147, every card flashed", AllFlashed(),
          ["B4a-41", "B4a-6", "B4a-1", "B4a-19", "B4a-68"] },
        { "IMG_1145, a pale second row", MixedWithAPaleRow(),
          ["B4a-3", "B4a-4", "B4a-2", "B4a-60", "B4a-49"] },
        { "IMG_1207, a pale card mid-row", APaleCardMidRow(),
          ["B4a-40", "B4a-58", "B4a-4", "B4a-35", "B4a-78"] },
    };

    [Theory]
    [MemberData(nameof(Reveals))]
    public void EveryCardOnAPackRevealIsBoxedWellEnoughToRecognise(
        string fixture, ShotScan scan, string[] expected)
    {
        var table = Table;

        // The fixtures are the newest set, and the point of the test is lost if the table happens
        // not to carry it — every card would come back unread and the assertion would be measuring
        // a missing table rather than a misplaced box.
        Assert.True(table.Covered("B4a") > 0, "the committed table has no B4a fingerprints");

        var read = scan.Cells.Select(c => Identify(table, c)).ToArray();

        Assert.Equal(expected, read.Select(r => r?.Key).ToArray());

        // Named is the assertion above; named with room to spare is this one, and it is the half
        // that would catch the boxing going wrong again by less than it did. A misplaced box does
        // not move one card, it moves every card on the screen — the height error put all five of
        // this fixture between 21 and 30 bits — so the AVERAGE over a fixture is the quantity that
        // tracks it, where any single card's distance also carries how kindly that particular art
        // happens to hash. These three average 8 to 10 bits, against a threshold of 18 and a
        // close-run band starting at 13.
        var mean = read.Average(r => r!.Value.Distance);
        Assert.True(mean < ArtHashTable.ComfortableDistance,
            $"{fixture}: the five cards average {mean:0.0} bits from the table, which is over "
            + $"{ArtHashTable.ComfortableDistance} and means the boxes have drifted even though the "
            + $"cards still just about read ({string.Join(", ", read.Select(r => $"{r!.Value.Key} at {r.Value.Distance}"))}).");
    }
}
