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
/// This was silent: js/store.js already returned false on a refused write, and the caller threw
/// the answer away — so past the quota every edit was lost while the app looked completely normal.
/// A tracker that appears to have saved is the worst failure it has, so the alarm is tested rather
/// than trusted.
/// </summary>
public class StorageAlarmTests : TestContext
{
    private AppSession Host(IStateStore store)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(new HttpClient(new SnapshotHandler())
        {
            BaseAddress = new Uri("https://test.local/")
        });
        Services.AddSingleton<CardDataLoader>();
        Services.AddSingleton<LocalStorageStateStore>();
        Services.AddSingleton(store);
        Services.AddSingleton<AppSession>();
        Services.AddSingleton<UiBusy>();
        Services.AddSingleton<NavHistory>();
        Services.AddSingleton<PaletteSwitch>();
        Services.AddBlazorBootstrap();

        return Services.GetRequiredService<AppSession>();
    }

    [Fact]
    public async Task Nothing_is_shown_while_saving_works()
    {
        var session = Host(new InMemoryStateStore());
        await session.InitAsync();

        var layout = RenderComponent<MainLayout>();

        Assert.False(session.SaveFailed);
        Assert.DoesNotContain("not being saved", layout.Markup);
    }

    [Fact]
    public async Task A_refused_write_is_announced_on_every_page()
    {
        var session = Host(new FullStore());
        await session.InitAsync();

        var layout = RenderComponent<MainLayout>();

        // Force the write rather than waiting out the debounce.
        await session.FlushAsync();

        Assert.True(session.SaveFailed);

        layout.WaitForAssertion(() =>
            Assert.Contains("not being saved", layout.Markup), TimeSpan.FromSeconds(2));

        // It has to say what to DO. "Something went wrong" would leave the user with a full
        // storage quota and no idea their collection is one reload from gone.
        Assert.Contains("Export a backup", layout.Markup);
    }

    [Fact]
    public async Task The_alarm_carries_an_alert_role_so_it_is_not_only_visual()
    {
        var session = Host(new FullStore());
        await session.InitAsync();

        var layout = RenderComponent<MainLayout>();
        await session.FlushAsync();

        layout.WaitForAssertion(() =>
            Assert.NotEmpty(layout.FindAll("[role=alert]")), TimeSpan.FromSeconds(2));
    }
}
