namespace PackProphet.App.Tests;

/// <summary>Proves the harness itself works before anything is asserted through it.</summary>
public class HostSanityTests : AppHost
{
    [Fact]
    public async Task The_snapshot_loads_through_the_fake_http_client()
    {
        await ReadyAsync();

        Assert.Equal(PackProphet.Services.DataSource.VendoredSnapshot, Session.Data!.Source);
        Assert.True(Session.Index.All.Count > 3000, $"only {Session.Index.All.Count} cards");
        Assert.NotNull(Session.Odds);
    }
}
