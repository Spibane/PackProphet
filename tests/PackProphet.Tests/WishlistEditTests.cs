using PackProphet.State;

namespace PackProphet.Tests;

public class WishlistEditTests
{
    private static Wishlist Empty() => new("w1", "cool", new());

    [Fact]
    public void Bump_CountsUpWithoutWrappingRound()
    {
        var list = Empty();
        for (var i = 1; i <= 6; i++)
        {
            list = WishlistEdit.Bump(list, "a.webp", 1);
            Assert.Equal(i, list.Wanted["a.webp"]);
        }
    }

    [Fact]
    public void Bump_DownToZeroRemovesTheCard()
    {
        var list = WishlistEdit.SetWanted(Empty(), "a.webp", 1);
        Assert.Empty(WishlistEdit.Bump(list, "a.webp", -1).Wanted);
    }

    [Fact]
    public void Bump_StopsAtZeroRatherThanGoingNegative()
    {
        Assert.Empty(WishlistEdit.Bump(Empty(), "a.webp", -1).Wanted);
    }

    [Fact]
    public void SetWanted_AllowsFarMoreThanADeckCouldHold()
    {
        // The deck limit of two must NOT leak in here: extra copies are wanted as trade fodder
        // and as flair material, which is the most ordinary reason to want a fourth.
        var list = WishlistEdit.SetWanted(Empty(), "a.webp", 12);
        Assert.Equal(12, list.Wanted["a.webp"]);
    }

    [Fact]
    public void SetWanted_ClampsAtTheTypoCeiling()
    {
        var list = WishlistEdit.SetWanted(Empty(), "a.webp", 5000);
        Assert.Equal(WishlistEdit.MaxCopies, list.Wanted["a.webp"]);
    }

    [Fact]
    public void SetWanted_ZeroRemovesRatherThanStoringAZero()
    {
        // A stored zero would count as an entry everywhere the list is measured, so "wanted 0"
        // and "not on the list" must be the same state.
        var list = WishlistEdit.SetWanted(Empty(), "a.webp", 2);
        list = WishlistEdit.SetWanted(list, "a.webp", 0);

        Assert.Empty(list.Wanted);
    }

    [Fact]
    public void SetWanted_NegativeIsTreatedAsRemoval()
    {
        var list = WishlistEdit.SetWanted(Empty(), "a.webp", 1);
        Assert.Empty(WishlistEdit.SetWanted(list, "a.webp", -3).Wanted);
    }

    [Fact]
    public void SetWanted_DoesNotMutateTheOriginal()
    {
        // Every edit goes through Mutate, which compares the old and new profile — so an
        // in-place edit would be invisible to undo.
        var before = WishlistEdit.SetWanted(Empty(), "a.webp", 1);
        WishlistEdit.SetWanted(before, "b.webp", 1);

        Assert.Single(before.Wanted);
    }

    [Fact]
    public void FreshName_AvoidsCollisions()
    {
        var lists = new[]
        {
            new Wishlist("1", "Wishlist", new()),
            new Wishlist("2", "Wishlist 2", new()),
        };

        Assert.Equal("Wishlist 3", WishlistEdit.FreshName(lists));
        Assert.Equal("Wishlist", WishlistEdit.FreshName([]));
    }

    [Fact]
    public void FreshName_IgnoresCase()
    {
        // Two lists whose names differ only in case are indistinguishable in a dropdown.
        Assert.Equal("Wishlist 2", WishlistEdit.FreshName([new Wishlist("1", "wishlist", new())]));
    }
}
