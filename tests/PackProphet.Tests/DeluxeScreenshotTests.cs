using PackProphet.Data;
using PackProphet.Vision;

namespace PackProphet.Tests;

/// <summary>
/// B4b, Deluxe Pack: Mega, read off real screenshots of the game.
///
/// The cells are verbatim <c>scan()</c> output from wwwroot/js/cardshot.js on two phone
/// screenshots taken the day the set released: the results of opening one Deluxe pack, and a
/// Deluxe Wonder Pick with the Pack Hourglasses second of five. The pictures themselves are not
/// committed, only what the detector measured in them. Digit glyphs are left out; nothing here
/// is about copy counts.
///
/// Every card in both is a reprint, which is the whole difficulty: the artwork is shared with the
/// set each card first appeared in, so the fingerprints alone named four sets for one pack.
/// </summary>
public class DeluxeScreenshotTests
{
    private static CardIndex Ix => Snapshot.Index();

    private static ArtHashTable Table =>
        ArtHashTable.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "card-hashes.txt")));

    private static ShotCell Cell(int row, int col, int[] box, string hash,
                                 double luma, double saturation, double detail, List<string> nearby) => new()
    {
        Row = row, Col = col, Box = box.Select(b => (double)b).ToArray(), Hash = hash,
        Luma = luma, Saturation = saturation, Detail = detail, Nearby = nearby,
    };

    /// <summary>Meowth, Delcatty, Iron Thorns and Indeedee ex: four cards, two by two.</summary>
    private static ShotScan DeluxePack() => new()
    {
        Ok = true, Width = 1320, Height = 2868,
        Lattice = new ShotLattice { Rows = 2, Cols = 2, CellWidth = 176, CellHeight = 245, Confidence = 0.75, RelativeCellWidth = 0.2733 },
        Cells =
        [
        Cell(0, 0, [132, 471, 176, 245], "869a19ab69b9aa93c039000120d802f9", 0.7388, 0.1889, 0.0933,
             ["c689992f29316b43c0198000fc1a43ff", "8492992ba931cbc3c0198000f810c3ff", "8493892bebd18bc3c0198000f81483ff", "849289abeb91eb93c0190000689802f7", "849289ab6bb9fb93c0390001689806d1", "8492992d6bb97393c039000320d806c1", "8493992be9b9aa93c0190000789802fd", "8698112b69a9f291c039000320d802c1", "8699982badb9aa83c019000034d802fd", "8689112b2da92a8bc01d000130d802f9", "8689112b2da93289c01d000320d812c1"]),
        Cell(0, 1, [332, 471, 176, 245], "8cb229eea684a622ff008047e180e31c", 0.7853, 0.1163, 0.0714,
             ["ccb391cea686a243fd00fc03f0c43b4d", "8c32a9cea785a657ff00e847e0c43b5d", "8cba29cea585a6c7ff00e846e1cc3b59", "8cb329ce2505a624ff00e847e194f31c", "8cb229ae4405a624ff000047e590e31c", "8cb029eec4058624fe000067e490e73c", "8cb329cea784a622ff00e847e18cb31c", "86b029e686a48622fe000047e080e33c", "8c3128c6a686a622fd00ec43e1cc331d", "8e3028c6a686a622ff008047e188731c", "86b0296686a2a622fe008077e188e31c"]),
        Cell(1, 0, [132, 739, 176, 245], "fbf6f9ecabb43969e0033cc008bc09e3", 0.5612, 0.4801, 0.1053,
             ["fb7ef8efb2b4b97de007fc809fbc637f", "fb76f8edb5b1397de007fc00bfbc637f", "f3f5f9edb531717bc016f800bfbc437f", "f3f6f8edabb5716bc007f8803bbc49c7", "f3f6f96dabb479f9d0073cc009fc09c3", "f0f6f9edabb4797918c03fc000b609c3", "fbf7f9edaab47969e007fcc01bbc49e3", "f1f7f96dabb4396918c03fc000be09e3", "fb7ef9eeaeb43869e007fcc01ebc08e3", "f97ef9eeeeb43868e0033cc000bc08e3", "f9f6f9eeeeb4386818c03dc000be1ce3"]),
        Cell(1, 1, [332, 739, 176, 245], "c672ca2264050f0afeffc3bb7481097e", 0.5758, 0.3371, 0.0818,
             ["86f3e6aa64850f77ff7de7bb45800b70", "c6f2ca2a448d1b63ff78e7bb45800b70", "c672ce62640d1e6afe78cfff44811a70", "86f2ca62640d1f5afff8c77b44810b7a", "86f2d26244050f1afefbc37b5c8109fe", "8672d2626c070f1ad6ff00fb7c8109fe", "c672ca2a64050f4aff79c7bb54800b7a", "c672d22264850d0a52ff00fb7c81087e", "c673ce2b64858d0bff7de7bb5680097a", "c673cb2264858d0bfffdc6bb7480097e", "8673d3a264858d0b52ff00fb34c1087f"]),
        ],
    };

    /// <summary>
    /// Mega Manectric ex, the hourglasses, Frigibax, then Mesagoza and Trapinch. The hourglass
    /// slot is the second cell, and it fingerprints as nothing.
    /// </summary>
    private static ShotScan DeluxeWonderPick() => new()
    {
        Ok = true, Width = 1320, Height = 2868,
        Lattice = new ShotLattice { Rows = 2, Cols = 3, CellWidth = 164, CellHeight = 229, Confidence = 0.8, RelativeCellWidth = 0.2547 },
        Cells =
        [
        Cell(0, 0, [36, 455, 164, 229], "35b2703cd319b97b00ff000f409cff63", 0.6257, 0.6367, 0.0999,
             ["96b2b4be4958395d00fa00ab4dbeb343", "b42234b4595939d800fa00af49bcb347", "b4663435da9931aa00fb00afc8bcf346", "2462743cdb1939ba00fa000fc0bcf3e3", "3426703cdb99393300ff000fc09cffe3", "1026403cd399b9330006000fc09cbe63", "b522343ccb19395b00fb000f409cf363", "9136423cd35999790007000fc09cbe73", "963230bccd58b95900fb008f419eb573", "94b272bccf58985900ff000f609cff73", "90b6623cc35998590003000f60ccfe73"]),
        Cell(0, 1, [240, 455, 164, 229], "0f0f1e3f2e4c44c0f8ff0633ff7cf818", 0.8481, 0.1221, 0.0657,
             ["0f0f0e3f2e64e004f8fe06bb7ef4f800", "0b0f1e2f6c44e002f8fe06b37ef0f800", "0b0f1f2f7c4cc00af8ff07b37ee8f800", "0b0f1e2f6e4ccc40f8fe06337ee8f800", "0b0f1e3f2e4c4cc0f0fe0e33ff7cf818", "0f0f1f3f2e4c4cc0f0ff0e33ff7cf818", "0f0f1e3f2e44cc40f8fe06337f74f800", "0f0f0f3f2e4c44c0f0ff0e33ff7cf018", "0f0f0e3f2666c440f8fe063b7f74f800", "0f0f0e3f2664c4c0f8fe0633ff7cf818", "0f0f0f1f276c44c0f0ff0613ff7cf018"]),
        Cell(0, 2, [444, 455, 164, 229], "b0f1c307c6ce29e179ffc700ff3f08f9", 0.6064, 0.3405, 0.1060,
             ["31714743c45c286139f7c0c0ff1f00ff", "3163c747cc4c296171ffc0c0ff1f00ff", "f1e38f078d6829e373efc180ff3e00ff", "f1e38707048c29e171ffc700ff3f00ff", "f1e3c707068c29e171ffcf00ff3f00f1", "b0e3e307868c69e179ffc700ff3f08f0", "f1e1c747c6cc29e171ffc600ff3f00ff", "b0f1c387c6cc2961f9ffc780ff3f0970", "b1f1c743c6de296139ffc200ff3f00ff", "b071c707c6de296179ffc700ff3f0078", "b871e30746ce2961f9ffe780ff3f0870"]),
        Cell(1, 0, [132, 751, 164, 229], "24acae2b76734a5800ff0000f601caff", 0.7462, 0.1639, 0.0859,
             ["a5ac2e2b777b2b6800ff000010a8efff", "2dacee6b77735b6801fe00001108e7fe", "2dacdd7b777357e901fe000031c0defe", "2dacee7b77735bd801fe0000770086ff", "2d2cec7b77735bd901fe0000f701c2ff", "2dacac6b77735b5301fe0000f501c8ff", "24acee2b77735ac800ff00007700c6ff", "24acac6b76734a5900fe0000f611cafd", "24acae6956734ac800ff00005728c2ff", "24acae2956734a4800ff0000d601c2ff", "a4aeac6b54734a4800ff0000d611eaff"]),
        Cell(1, 1, [340, 751, 164, 229], "a7336a1e1e06da9de7bb40ff0020fe01", 0.6286, 0.5681, 0.0677,
             ["a731ae1e8ed2dad1f3bee6dfc038ff00", "a733ae1e0ed7d8dbf7fee6dfc130ff01", "a773af1e0c459089e77aee9fc170ff01", "a7732a1e1e06d0cdf77ec6ffc030ff01", "f7736a1e1c06d09df77bc0ff0020fe01", "f9676a1c1c06d09c13a3ccff0780fc83", "a7332e1e0e06dacde77fe6ffc030ff00", "fd276a1e1e06d29b11a35cff07c07c85", "af31ae1e0e86dac5e3bfe6ffc030ff00", "af332d1a0e87da9de7bf40ff0000fe01", "fdb3291a0e86da9f11b36cff07c07e85"]),
        ],
    };

    [Fact]
    public void A_Deluxe_pack_is_read_as_a_pack_and_not_as_the_copies_list()
    {
        // Three to a slot wide, and a copy count on every card, which is the copies list's
        // signature. Two rows of exactly two is what a list cannot draw.
        var reading = new ScreenshotReader(Ix, Table).Read(DeluxePack());

        Assert.Equal(CardScreen.PackReveal, reading.Screen);
        Assert.Equal(4, reading.RecognisedCount);
    }

    [Fact]
    public void A_Deluxe_pack_of_reprints_is_named_as_the_Deluxe_pack()
    {
        var reading = new ScreenshotReader(Ix, Table).Read(DeluxePack());

        Assert.All(reading.Matches, m => Assert.Equal("B4b", m.Card.Set));
        Assert.Equal(["Meowth", "Delcatty", "Iron Thorns", "Indeedee ex"], reading.Matches.Select(m => m.Card.Name));
        Assert.Empty(reading.Notes);
        Assert.Equal("B4b:Deluxe Pack Mega", PackIdentifier.Identify(Ix, reading).PackKey);
    }

    [Fact]
    public void A_Deluxe_Wonder_Pick_names_four_cards_and_leaves_the_hourglasses_unread()
    {
        var reading = new ScreenshotReader(Ix, Table).Read(DeluxeWonderPick(), CardScreen.WonderPick);

        Assert.Equal(["Mega Manectric ex", "Frigibax", "Mesagoza", "Trapinch"], reading.Matches.Select(m => m.Card.Name));
        Assert.All(reading.Matches, m => Assert.Equal("B4b", m.Card.Set));

        // The hourglasses are a slot the reader found and could not name, never a card.
        var unread = Assert.Single(reading.UnreadSlots);
        Assert.Equal((0, 1), (unread.Row, unread.Col));
    }

    [Fact]
    public void A_hand_whose_cards_share_no_one_set_is_left_as_the_art_named_it()
    {
        // Mixed sets with no set in common is a misread, and saying which sets were seen is the
        // useful thing. Only an unambiguous single set is taken.
        var reader = new ScreenshotReader(Ix, Table);
        var scan = DeluxePack();
        scan.Cells[0].Hash = Table.Entries.First(e => e.Key == "A1-1").Hash.ToString();
        scan.Cells[0].Nearby = [];

        var reading = reader.Read(scan, CardScreen.PackReveal);

        Assert.Contains(reading.Matches, m => m.Card.Set == "A1");
        Assert.NotEmpty(reading.Notes);
    }

    // ---- foils --------------------------------------------------------------------------

    [Fact]
    public void A_Deluxe_diamond_card_pairs_with_its_foil_both_ways()
    {
        var plain = Ix.ByKey["B4b-78"];
        var foil = Ix.ByKey["B4b-288"];

        Assert.Equal("Frigibax", plain.Name);
        Assert.Equal(foil, Ix.FoilTwin(plain));
        Assert.Equal(plain, Ix.FoilTwin(foil));

        // A4b pairs the same way, which is where the rule came from.
        var a4b = Ix.BySet["A4b"].First(c => c.Rarity == "C" && c.VariantIndex == 0);
        Assert.NotNull(Ix.FoilTwin(a4b));
    }

    [Fact]
    public void Only_a_Deluxe_diamond_card_has_a_foil_twin()
    {
        // An RR alternate art is not a foil, and an ordinary set's second print is an alternate art.
        Assert.Null(Ix.FoilTwin(Ix.BySet["B4b"].First(c => c.Rarity == "RR")));
        Assert.Null(Ix.FoilTwin(Ix.ByKey["B2a-34"]));
    }

    [Fact]
    public void A_swap_replaces_the_card_and_keeps_where_it_was_found()
    {
        var reading = new ScreenshotReader(Ix, Table).Read(DeluxeWonderPick(), CardScreen.WonderPick);
        var frigibax = reading.Matches.Single(m => m.Card.Name == "Frigibax");
        var twin = Ix.FoilTwin(frigibax.Card)!;

        var swapped = reading.WithSwaps(new Dictionary<(int, int), PocketCard> { [(frigibax.Row, frigibax.Col)] = twin });
        var after = swapped.Matches.Single(m => m.Row == frigibax.Row && m.Col == frigibax.Col);

        Assert.Equal(twin, after.Card);
        Assert.Equal(frigibax.Distance, after.Distance);
        Assert.Equal(MatchSource.Art, after.Source);
    }
}
