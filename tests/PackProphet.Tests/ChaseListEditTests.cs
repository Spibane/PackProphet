using PackProphet.State;

namespace PackProphet.Tests;

public class ChaseListEditTests
{
    private static ChaseList Empty() => new("w1", "cool", new());

    [Fact]
    public void Bump_CountsUpWithoutWrappingRound()
    {
        var list = Empty();
        for (var i = 1; i <= 6; i++)
        {
            list = ChaseListEdit.Bump(list, "a.webp", 1);
            Assert.Equal(i, list.Wanted["a.webp"]);
        }
    }

    [Fact]
    public void Bump_DownToZeroRemovesTheCard()
    {
        var list = ChaseListEdit.SetWanted(Empty(), "a.webp", 1);
        Assert.Empty(ChaseListEdit.Bump(list, "a.webp", -1).Wanted);
    }

    [Fact]
    public void Bump_StopsAtZeroRatherThanGoingNegative()
    {
        Assert.Empty(ChaseListEdit.Bump(Empty(), "a.webp", -1).Wanted);
    }

    [Fact]
    public void SetWanted_AllowsFarMoreThanADeckCouldHold()
    {
        // The deck limit of two must NOT leak in here: extra copies are wanted as trade fodder
        // and as flair material, which is the most ordinary reason to want a fourth.
        var list = ChaseListEdit.SetWanted(Empty(), "a.webp", 12);
        Assert.Equal(12, list.Wanted["a.webp"]);
    }

    [Fact]
    public void SetWanted_ClampsAtTheTypoCeiling()
    {
        var list = ChaseListEdit.SetWanted(Empty(), "a.webp", 5000);
        Assert.Equal(ChaseListEdit.MaxCopies, list.Wanted["a.webp"]);
    }

    [Fact]
    public void SetWanted_ZeroRemovesRatherThanStoringAZero()
    {
        // A stored zero would count as an entry everywhere the list is measured, so "wanted 0"
        // and "not on the list" must be the same state.
        var list = ChaseListEdit.SetWanted(Empty(), "a.webp", 2);
        list = ChaseListEdit.SetWanted(list, "a.webp", 0);

        Assert.Empty(list.Wanted);
    }

    [Fact]
    public void SetWanted_NegativeIsTreatedAsRemoval()
    {
        var list = ChaseListEdit.SetWanted(Empty(), "a.webp", 1);
        Assert.Empty(ChaseListEdit.SetWanted(list, "a.webp", -3).Wanted);
    }

    [Fact]
    public void SetWanted_DoesNotMutateTheOriginal()
    {
        // Every edit goes through Mutate, which compares the old and new profile — so an
        // in-place edit would be invisible to undo.
        var before = ChaseListEdit.SetWanted(Empty(), "a.webp", 1);
        ChaseListEdit.SetWanted(before, "b.webp", 1);

        Assert.Single(before.Wanted);
    }

    [Fact]
    public void FreshName_AvoidsCollisions()
    {
        var lists = new[]
        {
            new ChaseList("1", "Chase list", new()),
            new ChaseList("2", "Chase list 2", new()),
        };

        Assert.Equal("Chase list 3", ChaseListEdit.FreshName(lists));
        Assert.Equal("Chase list", ChaseListEdit.FreshName([]));
    }

    [Fact]
    public void FreshName_IgnoresCase()
    {
        // Two lists whose names differ only in case are indistinguishable in a dropdown.
        Assert.Equal("Chase list 2", ChaseListEdit.FreshName([new ChaseList("1", "chase list", new())]));
    }
}
