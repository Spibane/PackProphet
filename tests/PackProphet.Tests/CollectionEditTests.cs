namespace PackProphet.Tests;

/// <summary>
/// Removal semantics. The UI had no way to decrement at all for a while, so the underlying
/// behaviour is worth pinning: counts must floor at zero and drop the entry entirely.
/// </summary>
public class CollectionEditTests
{
    private const string Key = "cPK_10_000010_00_FUSHIGIDANE_C.webp";

    [Fact]
    public void RemovingTheLastCopy_DropsTheEntryRatherThanStoringZero()
    {
        // Absence means "none". A stored zero would inflate DistinctOwned and bloat the save.
        var owned = new Collection().With(Key, 1).With(Key, 0);

        Assert.Equal(0, owned[Key]);
        Assert.Equal(0, owned.DistinctOwned);
        Assert.DoesNotContain(Key, owned.Raw.Keys);
    }

    [Fact]
    public void CountsNeverGoNegative()
    {
        var owned = new Collection().With(Key, 1);
        // The app clamps before calling With; this asserts the floor holds regardless.
        var cleared = owned.With(Key, -5);

        Assert.Equal(0, cleared[Key]);
        Assert.Empty(cleared.Raw);
    }

    [Fact]
    public void RemovingOneOfTwo_LeavesOne()
    {
        var owned = new Collection().With(Key, 2).With(Key, 1);
        Assert.Equal(1, owned[Key]);
    }

    [Fact]
    public void EditsDoNotMutateTheOriginal()
    {
        // AppSession detects collection changes by reference identity, so every edit must
        // produce a new instance rather than mutating in place.
        var before = new Collection().With(Key, 1);
        var after = before.With(Key, 0);

        Assert.Equal(1, before[Key]);
        Assert.Equal(0, after[Key]);
        Assert.NotSame(before.Raw, after.Raw);
    }

    [Fact]
    public void RemovingAReprint_RemovesItFromEverySetAtOnce()
    {
        // Ownership is per card, not per set entry, so clearing it clears every listing.
        var ix = Snapshot.Index();
        var shared = ix.ByOwnershipKey.First(kv => kv.Value.Select(c => c.Set).Distinct().Count() > 1);

        var owned = new Collection().With(shared.Key, 1);
        Assert.All(shared.Value, e => Assert.Equal(1, owned.Of(e)));

        var cleared = owned.With(shared.Key, 0);
        Assert.All(shared.Value, e => Assert.Equal(0, cleared.Of(e)));
    }
}
