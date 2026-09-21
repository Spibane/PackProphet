using PackProphet.Engine;
using PackProphet.State;

namespace PackProphet.Tests;

/// <summary>
/// The check that catches the same screenshot imported twice.
///
/// Its whole job is a comparison, and the interesting part is which differences it is allowed to
/// ignore. Order may be ignored, because the reader walks slots and a re-crop can shift a row.
/// Nothing else may be: a different pack, a different card, or one copy against two are all real
/// differences between two openings.
/// </summary>
public class RepeatOpeningsTests
{
    private static PackOpenEvent Opened(
        string set, string pack, DateTimeOffset at, params string[] keys) =>
        new(at, set, pack, "unknown", [.. keys]);

    private static readonly DateTimeOffset March = new(2025, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheSameCardsInTheSamePackAreTheSameOpening()
    {
        var log = new[] { Opened("A1", "Mewtwo", March, "a", "b", "c", "d", "e") };

        Assert.NotNull(RepeatOpenings.Match(log, "A1:Mewtwo", ["a", "b", "c", "d", "e"]));
    }

    [Fact]
    public void OrderIsNotEvidence()
    {
        // A crop that shifts a row reorders the slots without changing what came out of the pack.
        var log = new[] { Opened("A1", "Mewtwo", March, "a", "b", "c", "d", "e") };

        Assert.NotNull(RepeatOpenings.Match(log, "A1:Mewtwo", ["e", "d", "c", "b", "a"]));
    }

    [Fact]
    public void CopiesAre()
    {
        // Two of a card is not one of it, and a pack really can hold two.
        var log = new[] { Opened("A1", "Mewtwo", March, "a", "a", "b") };

        Assert.Null(RepeatOpenings.Match(log, "A1:Mewtwo", ["a", "b"]));
        Assert.NotNull(RepeatOpenings.Match(log, "A1:Mewtwo", ["a", "a", "b"]));
    }

    [Fact]
    public void TheSameCardsOutOfADifferentPackAreADifferentOpening()
    {
        var log = new[] { Opened("A1", "Mewtwo", March, "a", "b", "c") };

        Assert.Null(RepeatOpenings.Match(log, "A1:Charizard", ["a", "b", "c"]));
        Assert.Null(RepeatOpenings.Match(log, "A2:Mewtwo", ["a", "b", "c"]));
    }

    [Fact]
    public void OneCardDifferentIsNotARepeat()
    {
        var log = new[] { Opened("A1", "Mewtwo", March, "a", "b", "c", "d", "e") };

        Assert.Null(RepeatOpenings.Match(log, "A1:Mewtwo", ["a", "b", "c", "d", "f"]));
    }

    [Fact]
    public void TheMatchReportedIsTheMostRecentOne()
    {
        // The message names a date, and the useful date is the nearest: "an hour ago" is the
        // sentence that identifies the import being repeated.
        var log = new[]
        {
            Opened("A1", "Mewtwo", March, "a", "b"),
            Opened("A1", "Mewtwo", March.AddMonths(2), "a", "b"),
            Opened("A1", "Mewtwo", March.AddMonths(1), "a", "b"),
        };

        var found = RepeatOpenings.Match(log, "A1:Mewtwo", ["a", "b"]);

        Assert.Equal(March.AddMonths(2), found?.At);
    }

    [Fact]
    public void AnEmptyLogMatchesNothing()
    {
        Assert.Null(RepeatOpenings.Match([], "A1:Mewtwo", ["a", "b"]));
    }

    [Fact]
    public void AMalformedPackKeyIsNotAMatchRatherThanAThrow()
    {
        // Callers pass a key the identifier produced, so this should not arise -- and a lookup
        // that throws would take down a batch of twelve readings over one of them.
        var log = new[] { Opened("A1", "Mewtwo", March, "a") };

        Assert.Null(RepeatOpenings.Match(log, "Mewtwo", ["a"]));
        Assert.Null(RepeatOpenings.Match(log, "A1:Mewtwo:extra", ["a"]));
    }
}
