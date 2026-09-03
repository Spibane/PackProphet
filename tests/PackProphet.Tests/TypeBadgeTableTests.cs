namespace PackProphet.Tests;

using PackProphet.Vision;

/// <summary>
/// The committed type table's format. Small, but it is the contract between a tool that writes the
/// file and an app that reads it, and the two are in different processes on different schedules.
/// </summary>
public class TypeBadgeTableTests
{
    [Fact]
    public void A_written_table_reads_back_as_what_went_in()
    {
        var table = new TypeBadgeTable(
        [
            new("A1-1", ["Grass"]),
            new("B4a-7", ["Psychic", "Fighting"]),
            new("PROMO-A-12", ["Colorless"]),
        ], null);

        var round = TypeBadgeTable.Parse(table.Serialize("2026-09-03"));

        Assert.Equal(3, round.Count);
        Assert.Equal(["Grass"], round.For("A1-1"));
        Assert.Equal(["Psychic", "Fighting"], round.For("B4a-7"));
        // A set code with a hyphen in it, which is the one that a naive split breaks.
        Assert.Equal(["Colorless"], round.For("PROMO-A-12"));
        Assert.Equal("2026-09-03", round.Generated);
    }

    [Fact]
    public void A_card_it_has_never_seen_is_empty_rather_than_a_guess()
    {
        // The ordinary answer for a set released since the last workflow run.
        var table = TypeBadgeTable.Parse("# generated 2026-09-03\nA1 1 Grass\n");

        Assert.Empty(table.For("B9-1"));
        Assert.Empty(table.For(""));
    }

    [Fact]
    public void Set_codes_are_matched_without_regard_to_case()
    {
        var table = TypeBadgeTable.Parse("A1 1 Grass\n");
        Assert.Equal(["Grass"], table.For("a1-1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("# nothing but a comment\n")]
    [InlineData("A1\n")]                       // too few fields
    [InlineData("A1 notanumber Grass\n")]
    [InlineData("A1 1\n")]                     // no type
    [InlineData("A1 1 \n")]
    public void A_line_it_cannot_read_costs_that_line_and_nothing_else(string text)
    {
        // Tolerant for the same reason the fingerprint table is: one bad row must not take the
        // other three thousand with it and blank the column for every card.
        var table = TypeBadgeTable.Parse(text + "A2 5 Water\n");

        Assert.Equal(["Water"], table.For("A2-5"));
    }

    [Fact]
    public void Rows_are_written_in_set_and_number_order()
    {
        // So a regenerated table diffs as the cards that changed rather than as a reshuffle of all
        // of them -- which is the difference between a reviewable pull request and an unreadable one.
        var table = new TypeBadgeTable(
        [
            new("A2-10", ["Fire"]),
            new("A1-2", ["Grass"]),
            new("A2-2", ["Water"]),
            new("A1-10", ["Metal"]),
        ], null);

        var lines = table.Serialize("x").Split('\n')
            .Where(l => l.Length > 0 && l[0] != '#').ToArray();

        Assert.Equal(["A1 2 Grass", "A1 10 Metal", "A2 2 Water", "A2 10 Fire"], lines);
    }

    [Fact]
    public void The_sets_it_covers_can_be_listed()
    {
        var table = TypeBadgeTable.Parse("A1 1 Grass\nA1 2 Fire\nPROMO-A 3 Colorless\n");

        Assert.Equal(["A1", "PROMO-A"], table.Sets.OrderBy(s => s, StringComparer.Ordinal));
    }
}
