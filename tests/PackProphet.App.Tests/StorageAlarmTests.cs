namespace PackProphet.App.Tests;

using Microsoft.Extensions.DependencyInjection;
using PackProphet.Layout;
using PackProphet.Services;

/// <summary>A store that refuses to write, the way localStorage does past its quota.</summary>
internal sealed class FullStore : IStateStore
{
    private AppState _state = AppState.Fresh();

    public Task<AppState> LoadAsync(CancellationToken ct = default) => Task.FromResult(_state);

    public Task<bool> SaveAsync(AppState state, CancellationToken ct = default)
    {
        _state = state;
        return Task.FromResult(false);
    }
}

/// <summary>
/// The warning that has to appear when writes are failing.
///
/// This was silent: js/store.js already returned false on a refused write, and the caller threw the
/// answer away, so past the quota every edit was lost while the app looked normal.
///
/// Inherits the host rather than wiring its own services: a second copy of Program.cs's
/// registrations drifts, and it already had — adding one service to the app broke this file and
/// nothing else.
/// </summary>
public class StorageAlarmTests : AppHost
{
    private bool _full;

    protected override IStateStore Store() => _full ? new FullStore() : new InMemoryStateStore();

    [Fact]
    public async Task Nothing_is_shown_while_saving_works()
    {
        await ReadyAsync();

        var layout = RenderComponent<MainLayout>();

        Assert.False(Session.SaveFailed);
        Assert.DoesNotContain("not being saved", layout.Markup);
    }

    [Fact]
    public async Task A_refused_write_is_announced_on_every_page()
    {
        _full = true;
        await ReadyAsync();

        var layout = RenderComponent<MainLayout>();

        // Forced rather than waiting out the debounce.
        await Session.FlushAsync();

        Assert.True(Session.SaveFailed);

        layout.WaitForAssertion(() =>
            Assert.Contains("not being saved", layout.Markup), TimeSpan.FromSeconds(2));

        // It has to say what to DO. "Something went wrong" would leave someone with a full quota
        // and no idea their collection is one reload from gone.
        Assert.Contains("Export a backup", layout.Markup);
    }

    [Fact]
    public async Task The_alarm_carries_an_alert_role_so_it_is_not_only_visual()
    {
        _full = true;
        await ReadyAsync();

        var layout = RenderComponent<MainLayout>();
        await Session.FlushAsync();

        layout.WaitForAssertion(() =>
            Assert.NotEmpty(layout.FindAll("[role=alert]")), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task The_layout_offers_a_skip_straight_to_the_cards_when_a_grid_is_mounted()
    {
        // One tab from page load, versus roughly fifteen to reach the grid through the page's own
        // toolbar. Absent when there is no grid, so it never focuses nothing.
        await ReadyAsync();
        var focus = Services.GetRequiredService<PackProphet.Services.GridFocus>();

        var layout = RenderComponent<MainLayout>();
        Assert.DoesNotContain("Skip to the cards", layout.Markup);

        focus.Register(this, () => Task.CompletedTask);
        layout.Render();

        Assert.Contains("Skip to the cards", layout.Markup);

        // Bootstrap's own class rather than a hand-rolled off-screen rule, which depended on which
        // ancestor was positioned and which clipped.
        Assert.NotEmpty(layout.FindAll(".skip-link.visually-hidden-focusable"));
    }
}
