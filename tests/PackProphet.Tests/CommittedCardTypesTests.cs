namespace PackProphet.Tests;

using PackProphet.Vision;

/// <summary>
/// The committed card type table, checked against the types the card detail table already knows.
///
/// This is the test that justified generating the table offline rather than reading the badge in
/// the browser. The detail table carries the type for every card released so far, so a generated
/// table can be compared against a couple of thousand real answers — where a runtime reader could
/// only ever be checked against whatever fixtures someone thought to save.
///
/// It is also the test that found two defects the calibration run did not. The reader was given a
/// type for 69 Trainers, which have no energy badge at all and were reading whatever their frame
/// happened to be; and 1,430 Pokémon had no row, because only a card's base printing has the plain
/// header the reader can read.
/// </summary>
public class CommittedCardTypesTests
{
    private static TypeBadgeTable Table { get; } =
        TypeBadgeTable.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "card-types.txt")));

    /// <summary>Card key to the type the detail table gives it, for Pokémon only.</summary>
    private static Dictionary<string, string> KnownTypes()
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fact in Snapshot.Facts().All)
        {
            if (!fact.IsPokemon) continue;
            if (fact.CardKey is not { Length: > 0 } key) continue;
            if (fact.Subtype is not { Length: > 0 } subtype) continue;
            known[key] = subtype;
        }

        return known;
    }

    [Fact]
    public void The_table_was_committed_and_is_readable()
    {
        Assert.True(Table.Count > 3000, $"only {Table.Count} rows");
        Assert.False(string.IsNullOrWhiteSpace(Table.Generated), "no generated stamp");
    }

    [Fact]
    public void Every_type_it_claims_agrees_with_the_card_detail()
    {
        // The whole claim. Read off the artwork, checked against the published answer, on every
        // card where both exist -- 2,029 of them at the time of writing, and zero disagreements.
        // A regression here is the reader having drifted, not the table being incomplete: coverage
        // is asserted separately and is allowed to be partial.
        var known = KnownTypes();
        var wrong = new List<string>();
        var checked_ = 0;

        foreach (var (key, expected) in known)
        {
            var read = Table.For(key);
            if (read.Count == 0) continue;      // not covered; that is coverage, not correctness

            checked_++;
            if (read.Count == 1 && read[0].Equals(expected, StringComparison.OrdinalIgnoreCase))
                continue;

            wrong.Add($"{key}: detail says {expected}, badge says {string.Join('/', read)}");
        }

        Assert.True(checked_ > 1500, $"only {checked_} cards could be cross-checked");
        Assert.Empty(wrong);
    }

    [Fact]
    public void No_Trainer_is_given_an_energy_type()
    {
        // A Trainer's header carries a kind label where a Pokémon's carries HP and a badge, so
        // reading the badge position on one reads the frame. 69 of them were given a type before
        // the generator learned to tell the two apart from the artwork filename.
        var trainers = Snapshot.Facts().All
            .Where(f => !f.IsPokemon && f.CardKey is { Length: > 0 })
            .Select(f => f.CardKey!)
            .ToList();

        // A floor on the guard itself, not a fact about the game: the detail table is indexed by
        // deck-builder number and many Trainers carry none, so this sees 157 of them rather than
        // all ~1,500. Enough to be a real check.
        Assert.True(trainers.Count > 100, $"only {trainers.Count} Trainers in the detail table");

        var given = trainers.Where(k => Table.For(k).Count > 0).ToList();
        Assert.Empty(given);
    }

    [Fact]
    public void Every_type_it_names_is_one_the_reader_knows()
    {
        // A row naming something outside the palette would reach the UI as a pip with no glyph.
        var named = KnownTypes().Keys
            .SelectMany(k => Table.For(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.NotEmpty(named);
        Assert.All(named, t => Assert.Contains(t, TypeBadge.KnownTypes));
    }

    [Fact]
    public void Coverage_is_partial_and_that_is_the_documented_bargain()
    {
        // Deliberately a floor rather than a target. Only a card's base printing has the plain
        // header the reader can read: a full-art, an immersive or a crown draws its own frame, and
        // those are simply not covered. AppSession resolves an uncovered printing through the
        // printings that share its identity, which is where the coverage actually lands.
        var known = KnownTypes();
        var covered = known.Keys.Count(k => Table.For(k).Count > 0);

        Assert.True(covered > known.Count / 2,
            $"only {covered} of {known.Count} known Pokémon printings are covered");
    }

    [Fact]
    public void Every_committed_row_is_listed_including_cards_past_400()
    {
        // The generator merges a partial run into this list. It used to rebuild it by asking for
        // numbers 1 to 400 of each set, and B4b's 401 to 429 fell off the end.
        var entries = Table.Entries.ToArray();

        Assert.Equal(Table.Count, entries.Length);
        Assert.Contains(entries, kv => kv.Key == "B4b-429");
    }
}
