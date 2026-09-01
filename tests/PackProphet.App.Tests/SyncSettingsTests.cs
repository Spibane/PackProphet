namespace PackProphet.App.Tests;

using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using PackProphet.Services;
using PackProphet.Sync;

/// <summary>
/// Answers a canned response to every RPC, so the outcome mapping can be exercised without a
/// database. The mapping is the part of sync most worth covering: every branch turns on what a
/// Postgres error code means, and getting one wrong turns a retryable race into a dead end.
/// </summary>
internal sealed class RpcHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public string? LastPath { get; private set; }
    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        LastPath = request.RequestUri!.AbsolutePath;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}

public class SyncTransportTests
{
    private static readonly SyncOptions Configured = new()
    {
        Url = "https://example.supabase.co",
        AnonKey = "test-anon-key",
    };

    private static readonly SyncKeys Keys = new("doc-id", "auth-token");
    private static readonly SealedState Blob = new("Y2lwaGVy", "bm9uY2U=");

    private static SyncTransport Transport(HttpStatusCode status, string body) =>
        new(Configured, new RpcHandler(status, body));

    [Fact]
    public void An_unconfigured_build_reports_itself_unavailable()
    {
        Assert.False(new SyncOptions().Configured);
        Assert.False(new SyncOptions { Url = "https://YOUR-PROJECT.supabase.co", AnonKey = "k" }.Configured);
        Assert.True(Configured.Configured);
    }

    [Fact]
    public async Task An_empty_result_means_nothing_is_stored_yet_rather_than_an_error()
    {
        // A table-returning function answers with an array, so "no document" arrives as 200 with
        // [] -- not a 404. Reading that as a failure would stop a first pair from ever working.
        var transport = Transport(HttpStatusCode.OK, "[]");

        Assert.Null(await transport.PullAsync(Keys));
    }

    [Fact]
    public async Task A_stored_document_comes_back_whole()
    {
        var transport = Transport(HttpStatusCode.OK, """
            [{"payload":"Y2lwaGVy","nonce":"bm9uY2U=","writer":"abc123",
              "version":7,"updated_at":"2026-03-01T12:00:00+00:00"}]
            """);

        var doc = await transport.PullAsync(Keys);

        Assert.NotNull(doc);
        Assert.Equal("Y2lwaGVy", doc!.Payload);
        Assert.Equal("abc123", doc.Writer);
        Assert.Equal(7, doc.Version);
    }

    [Fact]
    public async Task A_successful_push_reports_the_new_version()
    {
        var transport = Transport(HttpStatusCode.OK, "12");

        var result = await transport.PushAsync(Keys, Blob, "device", 11);

        Assert.Equal(PushOutcome.Stored, result.Outcome);
        Assert.Equal(12, result.Version);
    }

    [Theory]
    [InlineData("40001", PushOutcome.Superseded)]
    [InlineData("P0002", PushOutcome.Gone)]
    [InlineData("42501", PushOutcome.Refused)]
    [InlineData("22001", PushOutcome.TooLarge)]
    public async Task Each_refusal_from_the_database_is_told_apart(string sqlState, PushOutcome expected)
    {
        // Matched on SQLSTATE, never on the message: the wording in db/sync.sql is for a human
        // reading the logs, and a client that parsed it would break the first time it was reworded.
        var transport = Transport(HttpStatusCode.BadRequest,
            $"{{\"code\":\"{sqlState}\",\"message\":\"whatever this happens to say\"}}");

        var result = await transport.PushAsync(Keys, Blob, "device", 1);

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task A_refusal_with_no_code_at_all_is_not_read_as_success()
    {
        var transport = Transport(HttpStatusCode.InternalServerError, "<html>gateway error</html>");

        var result = await transport.PushAsync(Keys, Blob, "device", 1);

        Assert.NotEqual(PushOutcome.Stored, result.Outcome);
    }

    [Fact]
    public async Task A_first_push_says_it_expects_no_document()
    {
        // Null rather than a version, and it matters: the function inserts only when the client
        // says it believes nothing is there, so a stale client cannot resurrect a deleted document.
        var handler = new RpcHandler(HttpStatusCode.OK, "1");
        var transport = new SyncTransport(Configured, handler);

        await transport.PushAsync(Keys, Blob, "device", null);

        Assert.Contains("\"expected_version\":null", handler.LastBody);
        Assert.EndsWith("/rest/v1/rpc/sync_push", handler.LastPath);
    }

    [Fact]
    public async Task The_encryption_key_is_never_in_anything_sent()
    {
        // The contract the whole design rests on: the host receives an id, a proof token and
        // ciphertext. If a key ever appeared in a request body, this is where it would show up.
        var handler = new RpcHandler(HttpStatusCode.OK, "1");
        var transport = new SyncTransport(Configured, handler);

        await transport.PushAsync(Keys, Blob, "device", null);

        Assert.Contains("doc_id", handler.LastBody);
        Assert.Contains("doc_auth", handler.LastBody);
        Assert.DoesNotContain("key", handler.LastBody);
    }
}

/// <summary>The settings page, with and without a project to sync to.</summary>
public class SyncSettingsPageTests : AppHost
{
    [Fact]
    public async Task A_build_with_nowhere_to_sync_does_not_mention_sync()
    {
        // The state this repository builds in, and the state a fork stays in. Offering a control
        // that cannot work is worse than not offering one.
        await ReadyAsync();
        var page = RenderComponent<PackProphet.Pages.Settings>();

        Assert.DoesNotContain("Sync Across Devices", page.Markup);
        Assert.DoesNotContain("Set Up Sync", page.Markup);
    }
}

public class SyncOfferedPageTests : AppHost
{
    protected override SyncOptions SyncSettings() => new()
    {
        Url = "https://example.supabase.co",
        AnonKey = "test-anon-key",
    };

    [Fact]
    public async Task A_configured_build_offers_pairing_and_says_the_code_cannot_be_recovered()
    {
        await ReadyAsync();
        var page = RenderComponent<PackProphet.Pages.Settings>();

        Assert.Contains("Sync Across Devices", page.Markup);
        Assert.Contains("Set Up Sync", page.Markup);
        Assert.Contains("I Have A Code", page.Markup);

        // The one thing a user must be told before they rely on it: there is no account behind
        // this, so nobody can send them the code again.
        Assert.Contains("cannot be recovered", page.Markup);
    }

    [Fact]
    public async Task The_page_says_there_is_no_account_and_that_the_server_cannot_read_it()
    {
        // Both halves of the promise this feature makes, and the two things the README's privacy
        // section has to keep agreeing with.
        await ReadyAsync();
        var page = RenderComponent<PackProphet.Pages.Settings>();

        Assert.Contains("No account", page.Markup);
        Assert.Contains("what it cannot read", page.Markup);
    }

    [Fact]
    public async Task The_code_entry_field_appears_only_once_asked_for()
    {
        await ReadyAsync();
        var page = RenderComponent<PackProphet.Pages.Settings>();

        Assert.DoesNotContain("XXXX-XXXX-XXXX", page.Markup);

        page.FindAll("button").First(b => b.TextContent.Contains("I Have A Code")).Click();

        Assert.Contains("XXXX-XXXX-XXXX", page.Markup);
    }

    [Fact]
    public async Task A_mistyped_code_is_refused_before_anything_is_sent()
    {
        // No JS interop and no server in this host, so if the code were not rejected locally this
        // would fail trying to derive a key -- which is the assertion.
        await ReadyAsync();
        var sync = Services.GetRequiredService<SyncService>();

        Assert.False(await sync.JoinAsync("nope"));
        Assert.False(sync.Paired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nope")]
    [InlineData("PACK-7K3M-92QX-TOOLONG")]
    public async Task Every_refused_join_leaves_a_message_the_page_will_render(string typed)
    {
        // The bug that made pressing Join look like a dead button: the failure was described in a
        // returned string, the page rendered Sync.Message, and those were different channels. Any
        // exit that does not set BOTH of these shows the user nothing at all.
        await ReadyAsync();
        var sync = Services.GetRequiredService<SyncService>();

        Assert.False(await sync.JoinAsync(typed));
        Assert.Equal(SyncStatus.Failed, sync.Status);
        Assert.False(string.IsNullOrWhiteSpace(sync.Message));
    }

    [Fact]
    public async Task An_empty_field_is_told_to_type_the_code_rather_than_that_it_is_invalid()
    {
        // "That is not a valid code" is wrong and slightly insulting for a field nobody filled in.
        await ReadyAsync();
        var sync = Services.GetRequiredService<SyncService>();

        await sync.JoinAsync("");

        Assert.Contains("Type the code", sync.Message);
    }

    [Fact]
    public async Task A_refused_join_shows_the_reason_on_the_page_and_keeps_the_field_up()
    {
        await ReadyAsync();
        var page = RenderComponent<PackProphet.Pages.Settings>();

        page.FindAll("button").First(b => b.TextContent.Contains("I Have A Code")).Click();
        page.Find("input[placeholder='XXXX-XXXX-XXXX']").Input("nope");
        page.FindAll("button").First(b => b.TextContent.Trim() == "Join").Click();

        Assert.Contains("not a valid code", page.Markup);

        // Still there to correct, rather than collapsed so the code has to be retyped from scratch.
        Assert.Contains("XXXX-XXXX-XXXX", page.Markup);
    }

    [Fact]
    public async Task A_failed_join_leaves_the_collection_exactly_as_it_was()
    {
        await ReadyAsync();
        Session.SetCount("a.webp", 3);

        var sync = Services.GetRequiredService<SyncService>();
        await sync.JoinAsync(PairingCode.Format(PairingCode.New()) + "X");   // now the wrong length

        Assert.Equal(3, Session.Profile.Collection["a.webp"]);
    }
}

/// <summary>
/// What happens when the browser cannot do the crypto — an insecure origin, or a WebCrypto
/// implementation that refuses. JS interop is loose in this host, so calls return defaults rather
/// than throwing; these cover the state machine's own exits instead.
/// </summary>
public class SyncFailureTests : AppHost
{
    protected override SyncOptions SyncSettings() => new()
    {
        Url = "https://example.supabase.co",
        AnonKey = "test-anon-key",
    };

    [Fact]
    public async Task A_sync_that_cannot_finish_does_not_leave_the_feature_stuck_mid_flight()
    {
        // Status is what disables every button on the settings page. Whatever goes wrong, it has to
        // come to rest somewhere the user can act from -- not on Working, which has no way out but
        // a page reload.
        await ReadyAsync();
        var sync = Services.GetRequiredService<SyncService>();

        await sync.SyncAsync();                     // not paired: a no-op, and must stay one
        Assert.NotEqual(SyncStatus.Working, sync.Status);

        await sync.PairNewAsync();
        Assert.NotEqual(SyncStatus.Working, sync.Status);

        await sync.JoinAsync("PACK-7K3M-92QX");
        Assert.NotEqual(SyncStatus.Working, sync.Status);
    }

    [Fact]
    public async Task Unpairing_leaves_the_collection_and_reports_that_it_did()
    {
        await ReadyAsync();
        Session.SetCount("a.webp", 4);

        var sync = Services.GetRequiredService<SyncService>();
        await sync.UnpairAsync(deleteRemote: false);

        Assert.Equal(4, Session.Profile.Collection["a.webp"]);
        Assert.False(sync.Paired);
        Assert.Equal(SyncStatus.Unpaired, sync.Status);
        Assert.Contains("still here", sync.Message);
    }

    [Fact]
    public async Task Syncing_while_unpaired_does_nothing_at_all()
    {
        await ReadyAsync();
        Session.SetCount("a.webp", 2);

        var sync = Services.GetRequiredService<SyncService>();
        await sync.SyncAsync();

        Assert.Equal(2, Session.Profile.Collection["a.webp"]);
        Assert.Null(sync.LastSyncedAt);
    }
}
