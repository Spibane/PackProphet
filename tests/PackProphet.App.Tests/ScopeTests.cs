namespace PackProphet.App.Tests;

/// <summary>
/// Scoping a page to one chase list.
///
/// Every ranking page offers it — which pack to open for this list, which trade, which slots on
/// the board — and it silently did nothing. "chase:" is six characters and the id was being taken
/// from index five, so every lookup got ":abc" instead of "abc", found no list, and fell back to
/// ranking against everything you collect. The control worked, the picker showed the choice, and
/// the answer was the same one it gives with no scope at all.
/// </summary>
public class ScopeTests : AppHost
{
    [Fact]
    public async Task A_chase_list_scope_survives_being_normalised()
    {
        // NormalizeScope exists to turn a scope naming something deleted back into "everything".
        // It was doing that to every chase list, deleted or not.
        await ReadyAsync();

        var id = Session.CreateChaseList("Crowns I want");

        Assert.Equal($"chase:{id}", Session.NormalizeScope($"chase:{id}"));
    }

    [Fact]
    public async Task A_chase_list_scope_resolves_to_that_list()
    {
        await ReadyAsync();

        var id = Session.CreateChaseList("Crowns I want");
        var card = Session.Index.All.First(c => !c.IsPromo);
        Session.AddToChaseList(id, [card.OwnershipKey]);

        var target = Session.TargetForScope($"chase:{id}");
        var everything = Session.TargetForScope("everything");

        // The list wants one card; everything you collect wants rather more. Compared by what they
        // ask for rather than by type, since both are composites.
        Assert.NotEqual(
            everything.Outstanding(Session.Index, Session.Owned).Count,
            target.Outstanding(Session.Index, Session.Owned).Count);
    }

    [Fact]
    public async Task A_deleted_chase_list_still_falls_back()
    {
        // The behaviour the off-by-one was masking, and the reason NormalizeScope exists: a picker
        // showing a deleted list keeps offering it, so the scope has to resolve to something.
        await ReadyAsync();

        var id = Session.CreateChaseList("Temporary");
        Session.DeleteChaseList(id);

        Assert.Equal("everything", Session.NormalizeScope($"chase:{id}"));
    }
}
