using System.Text.Json;
using PackProphet.Data;
using PackProphet.Vision;

namespace PackProphet.Tests;

/// <summary>
/// Ten B4b packs opened at once, read back out of eight screenshots of the results list.
///
/// fixtures/ten-pack-b4b.scans.json is the detector's own output on those screenshots, taken on
/// the release day, in the order they were chosen: scrolling down, then back up once (1384), then
/// down to the bottom. The pictures are not committed. What the packs held is read off them by eye
/// and written below; it is what every reading here is held to.
/// </summary>
public class OpeningStitcherTests
{
    private static CardIndex Ix => Snapshot.Index();

    private static ArtHashTable Table =>
        ArtHashTable.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "card-hashes.txt")));

    private static IReadOnlyList<ShotScan> Scans() =>
        JsonSerializer.Deserialize<Dictionary<string, ShotScan>>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "ten-pack-b4b.scans.json")),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Value)
            .ToArray();

    private static readonly string[][] Truth =
    [
        ["Juliana", "Meloetta", "Dolliv", "Swanna ex"],
        ["Wynaut", "Arena of Antiquity", "Dratini", "Vaporeon ex"],
        ["Helioptile", "Fuecoco", "Fidough", "Mega Diancie ex"],
        ["Inteleon", "Dwebble", "Juliana", "Rapidash ex"],
        ["Axew", "Growlithe", "Puppy-Loving Girl", "Mega Sableye ex"],
        ["Skrelp", "Serena", "Numel", "Typhlosion ex"],
        ["Sylveon", "Tadbulb", "Ponyta", "Dedenne ex"],
        ["Crocalor", "Pikachu", "Darkrai", "Dedenne ex"],
        ["Sobble", "Galarian Perrserker", "Hisuian Zorua", "Iron Bundle ex"],
        ["Heliolisk", "Squirtle", "Klefki", "Mega Pinsir ex"],
    ];

    private static readonly IReadOnlySet<int> Deluxe = new HashSet<int> { 4 };

    private static IReadOnlyList<ShotReading> Read(IEnumerable<ShotScan> scans) =>
        scans.Select(s => new ScreenshotReader(Ix, Table).Read(s, CardScreen.PackReveal)).ToArray();

    [Fact]
    public void The_list_is_read_as_a_list_where_it_shows_a_heading()
    {
        // Rows at two spacings. A picture showing only one pack's two rows has no heading gap in it,
        // and reads as the pack it shows -- which is still a stretch of the same list.
        var screens = Read(Scans()).Select(r => r.Screen).ToArray();

        Assert.Equal(6, screens.Count(s => s == CardScreen.PackList));
        Assert.All(screens, s => Assert.Contains(s, new[] { CardScreen.PackList, CardScreen.PackReveal }));
    }

    [Fact]
    public void Eight_pictures_make_the_ten_packs_that_were_opened()
    {
        var opening = OpeningStitcher.Stitch(Read(Scans()), _ => Deluxe);

        Assert.Equal(Truth.Length, opening.Packs.Count);
        for (var i = 0; i < Truth.Length; i++)
        {
            Assert.Equal(Truth[i], opening.Packs[i].Matches.Select(m => m.Card.Name));
            Assert.Empty(opening.Packs[i].UnreadSlots);
        }

        Assert.Empty(opening.Notes);
    }

    [Fact]
    public void A_card_repeated_across_two_packs_is_kept_in_both()
    {
        // Dedenne ex ends packs 7 and 8. Matching pictures by which cards they share, rather than
        // by the order of the rows, would have merged the two.
        var opening = OpeningStitcher.Stitch(Read(Scans()), _ => Deluxe);

        Assert.Equal(2, opening.Packs.Count(p => p.Matches.Any(m => m.Card.Name == "Dedenne ex")));
    }

    [Fact]
    public void Every_pack_is_the_one_set_the_list_is_of()
    {
        var opening = OpeningStitcher.Stitch(Read(Scans()), _ => Deluxe);

        Assert.All(opening.Packs, p =>
            Assert.Equal("B4b:Deluxe Pack Mega", PackIdentifier.Identify(Ix, p).PackKey));
    }

    [Fact]
    public void The_order_they_are_chosen_in_does_not_change_the_packs_between_overlaps()
    {
        // The scroll back up (1384) lies wholly inside what the pictures either side already hold.
        // Taken out, nothing is lost; moved to the end, it still lands where it belongs.
        var scans = Scans().ToList();
        var back = scans[5];
        scans.RemoveAt(5);
        scans.Add(back);

        var opening = OpeningStitcher.Stitch(Read(scans), _ => Deluxe);

        Assert.Equal(Truth, opening.Packs.Select(p => p.Matches.Select(m => m.Card.Name).ToArray()));
    }

    [Fact]
    public void Whole_packs_nobody_photographed_are_missing_and_the_rest_keep_their_cards()
    {
        // Without 1385, packs 7 and 8 were never on screen. The pictures either side meet at
        // headings, so every later pack keeps its own cards and nothing slides into the gap.
        //
        // What cannot be said is that two packs are missing: a whole pack leaves no short pack
        // behind, and the headings' numbers are not read. The count is what tells -- the page
        // says how many packs the pictures made, and eight is not the ten that were opened.
        var scans = Scans().Where((_, i) => i != 6).ToArray();

        var opening = OpeningStitcher.Stitch(Read(scans), _ => Deluxe);

        Assert.Equal(8, opening.Packs.Count);
        Assert.Equal(Truth[5], opening.Packs[5].Matches.Select(m => m.Card.Name));
        Assert.Equal(Truth[8], opening.Packs[6].Matches.Select(m => m.Card.Name));
        Assert.Equal(Truth[9], opening.Packs[7].Matches.Select(m => m.Card.Name));
    }

    [Fact]
    public void Half_a_pack_nobody_photographed_leaves_it_short_and_says_so()
    {
        // Without the guessed row under the Next button in 1385, pack 8's second row is on no
        // picture. Pack 9 begins under a heading, so the two cards are missing from pack 8 rather
        // than borrowed from pack 9.
        var scans = Scans().ToArray();
        scans[6].Cells.RemoveAll(c => c.Guessed);

        var opening = OpeningStitcher.Stitch(Read(scans), _ => Deluxe);

        Assert.Equal(10, opening.Packs.Count);
        Assert.Equal(["Crocalor", "Pikachu"], opening.Packs[7].Matches.Select(m => m.Card.Name));
        Assert.Equal(2, opening.Packs[7].UnreadSlots.Count);
        Assert.Equal(Truth[8], opening.Packs[8].Matches.Select(m => m.Card.Name));
        Assert.NotEmpty(opening.Notes);
    }

    [Fact]
    public void Packs_of_five_split_at_their_headings_not_at_a_row_count()
    {
        // No screenshot of a five-card ten-pack yet, so this is the shape the headings imply: three
        // then two to a pack. What is being checked is that the split follows StartsPack and the
        // allowed sizes, whatever the rows hold.
        var cards = Ix.BySet["A1"].Where(c => c.Rarity == "C").Take(10).ToArray();
        ShotMatch At(int i, int row, int col) => new(cards[i], row, col, 5, true, MatchSource.Art);

        var shot = new ShotReading(true, null, CardScreen.PackList, false,
            [At(0, 0, 0), At(1, 0, 1), At(2, 0, 2), At(3, 1, 0), At(4, 1, 1),
             At(5, 2, 0), At(6, 2, 1), At(7, 2, 2), At(8, 3, 0), At(9, 3, 1)], [], [])
        {
            Rows = [new ShotRow(0, null, [0, 1, 2]), new ShotRow(1, false, [0, 1]), new ShotRow(2, true, [0, 1, 2]), new ShotRow(3, false, [0, 1])],
        };

        var opening = OpeningStitcher.Stitch([shot], _ => new HashSet<int> { 5, 6 });

        Assert.Equal(2, opening.Packs.Count);
        Assert.All(opening.Packs, p => Assert.Equal(5, p.Matches.Count));
    }

    // ---- a second opening ------------------------------------------------------------------

    /// <summary>
    /// Ten more B4b packs, five screenshots, none overlapping: each holds two packs, the second
    /// pack's lower row under the Next button. fixtures/ten-pack-b4b-2.scans.json keeps four of each
    /// card's nudged crops rather than all of them, so it reads with less to go on than the app has.
    ///
    /// It found three things the first opening did not. A screenshot with one heading and no other
    /// gap to compare it with (1391) -- so a heading is judged against the card's height, not the
    /// picture's other gaps. A top row of two pale cards the mask did not see at all (1391 again) --
    /// so a row is guessed above the first as well as below the last. And a screenshot whose cards
    /// all masked a few pixels small (1388), which read half of them -- so the list offers larger
    /// crops too.
    /// </summary>
    private static readonly string[][] SecondTruth =
    [
        ["Pikachu", "Charmeleon", "Charmeleon", "Melmetal ex"],
        ["Metapod", "Sylveon", "Yamper", "Milotic ex"],
        ["Riolu", "Frigibax", "Magneton", "Mega Lucario ex"],
        ["Zorua", "Peculiar Plaza", "Carvanha", "Magnezone ex"],
        ["Meloetta", "Skeledirge", "Skrelp", "Rotom ex"],
        ["Charmeleon", "Lucky Ice Pop", "Growlithe", "Magnezone ex"],
        ["Dragonair", "Onix", "Darkrai", "Teal Mask Ogerpon ex"],
        ["Galarian Perrserker", "Korrina", "Ponyta", "Mega Mawile ex"],
        ["Hiking Trail", "Delcatty", "Sprigatito", "Greninja ex"],
        ["Butterfree", "Ivysaur", "Gastly", "Rotom ex"],
    ];

    private static IReadOnlyList<ShotScan> SecondScans() =>
        JsonSerializer.Deserialize<Dictionary<string, ShotScan>>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "ten-pack-b4b-2.scans.json")),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Value)
            .ToArray();

    [Fact]
    public void Five_pictures_of_two_packs_each_make_the_ten_that_were_opened()
    {
        var opening = OpeningStitcher.Stitch(Read(SecondScans()), _ => Deluxe);

        Assert.Equal(SecondTruth, opening.Packs.Select(p => p.Matches.Select(m => m.Card.Name).ToArray()));
        Assert.All(opening.Packs, p => Assert.Empty(p.UnreadSlots));
    }

    [Fact]
    public void One_heading_and_nothing_to_compare_it_with_still_splits_two_packs()
    {
        // 1391 shows pack 9's second row, a heading, and pack 10. Its one gap is all it has.
        var reading = Read(SecondScans().TakeLast(1)).Single();

        Assert.Equal(CardScreen.PackList, reading.Screen);
        Assert.Contains(reading.Rows, r => r.StartsPack == true);
    }

    // ---- a third opening, photographed twice ---------------------------------------------------

    /// <summary>
    /// Ten more B4b packs, photographed two ways: five pictures of two packs each (1404-1408), then
    /// ten scrolling down a pack at a time (1409-1418). fixtures/ten-pack-b4b-3.scans.json keeps
    /// every nudged crop, as the app does; with eight of each, Mega Camerupt ex in pack 4 is in no
    /// picture of the second set that reads.
    ///
    /// Both ways read nine packs before. Pack 8 opens with two pale cards, Meltan and Aegislash,
    /// and in 1407 nothing of pack 8 masked, and of pack 7 only its two coloured cards on the right:
    /// the card size came out of two regions and was a card's worth small, and two rows assembled
    /// from pieces straddled the gaps between real ones. In 1410 the rows were laid out as a grid,
    /// which on this list is a heading's worth wrong by the third row.
    /// </summary>
    private static readonly string[][] ThirdTruth =
    [
        ["Drizzile", "Mareep", "Budew", "Miraidon ex"],
        ["Ivysaur", "Magneton", "Honedge", "Mega Sableye ex"],
        ["Eevee", "Deceptive Needle", "Onix", "Typhlosion ex"],
        ["Haunter", "Slowpoke", "Haxorus", "Mega Camerupt ex"],
        ["Hiking Trail", "Sobble", "Lilligant", "Bellibolt ex"],
        ["Mareep", "Calem", "Eevee", "Mega Gengar ex"],
        ["Delcatty", "Fuecoco", "Fragrant Forest", "Mega Gyarados ex"],
        ["Meltan", "Aegislash", "Dragonair", "Mega Gallade ex"],
        ["Ivysaur", "Eevee", "Axew", "Terapagos ex"],
        ["Charmeleon", "Copycat", "Alolan Grimer", "Mega Camerupt ex"],
    ];

    private static IReadOnlyDictionary<string, ShotScan> ThirdScans() =>
        JsonSerializer.Deserialize<Dictionary<string, ShotScan>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "ten-pack-b4b-3.scans.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static IReadOnlyList<ShotScan> Pictures(int from, int to) =>
        ThirdScans().Where(kv => int.Parse(kv.Key[4..]) is var n && n >= from && n <= to)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Value)
            .ToArray();

    [Fact]
    public void Five_pictures_of_two_packs_each_make_the_ten_when_one_pack_masked_nothing()
    {
        var opening = OpeningStitcher.Stitch(Read(Pictures(1404, 1408)), _ => Deluxe);

        Assert.Equal(ThirdTruth, opening.Packs.Select(p => p.Matches.Select(m => m.Card.Name).ToArray()));
        Assert.Empty(opening.Notes);
    }

    [Fact]
    public void Ten_pictures_a_pack_apart_make_the_same_ten()
    {
        var opening = OpeningStitcher.Stitch(Read(Pictures(1409, 1418)), _ => Deluxe);

        Assert.Equal(ThirdTruth, opening.Packs.Select(p => p.Matches.Select(m => m.Card.Name).ToArray()));
        Assert.Empty(opening.Notes);
    }

    [Fact]
    public void A_heading_only_a_guessed_row_shows_still_makes_it_the_list()
    {
        // 1407's two masked rows are one pack's. The heading is between the second of them and
        // pack 8's first row, which is there only because it was guessed and then recognised.
        var reading = Read(Pictures(1407, 1407)).Single();

        Assert.Equal(CardScreen.PackList, reading.Screen);
        Assert.Equal(["Delcatty", "Fuecoco", "Fragrant Forest", "Mega Gyarados ex",
                      "Meltan", "Aegislash", "Dragonair", "Mega Gallade ex"],
            reading.Matches.Select(m => m.Card.Name));
        Assert.Empty(reading.UnreadSlots);
    }

    [Fact]
    public void Guesses_nothing_was_recognised_in_are_not_rows()
    {
        // Every picture is guessed around at both spacings; most of those land on headings and the
        // title bar. None of them may be offered for naming or counted as a row.
        foreach (var reading in Read(ThirdScans().Values))
        {
            var named = reading.Matches.Select(m => m.Row).ToHashSet();
            Assert.All(reading.Rows, r => Assert.Contains(r.Row, named));
        }
    }
}
