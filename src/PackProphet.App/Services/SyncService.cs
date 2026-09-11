namespace PackProphet.Services;

using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.JSInterop;

using PackProphet.State;
using PackProphet.Sync;

/// <summary>Where a sync got to, as the settings page has to describe it.</summary>
public enum SyncStatus
{
    /// <summary>This build has nowhere to sync to. The feature is not offered.</summary>
    Unavailable,

    /// <summary>Configured, but this device has not been paired.</summary>
    Unpaired,

    Working,
    Synced,

    /// <summary>Tried and failed. <see cref="SyncService.Message"/> says how.</summary>
    Failed,
}

/// <summary>
/// Cloud sync: pull, merge, push, on a pairing code and nothing else.
///
/// Three rules shape all of it.
///
/// The local collection is the source of truth. Every failure path here leaves it exactly as it
/// was; nothing is ever cleared, emptied or replaced because a network call went wrong. The worst
/// outcome of a broken sync is that the app is the local-only app it was before.
///
/// A merge needs a common ancestor, so this keeps one: the state that was last known to match the
/// remote copy. It is what turns "these two differ" into "this side changed it", and without it
/// the second device to push erases the first one's work. See <see cref="StateMerge"/>.
///
/// A push is conditional on the version it was based on. Two devices that pull at the same moment
/// will both try to push; the loser is told, pulls again, merges what it now knows, and retries.
/// </summary>
public sealed class SyncService : IAsyncDisposable
{
    private const string CodeKey = "packprophet.sync.code";
    private const string BaseKey = "packprophet.sync.base";
    private const string VersionKey = "packprophet.sync.version";
    private const string DeviceKey = "packprophet.sync.device";

    /// <summary>
    /// How long after the last edit a push goes out. Much longer than the 400 ms local save: a
    /// local write is free and a round trip is not, and nobody is waiting on it.
    /// </summary>
    private static readonly TimeSpan PushDelay = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Attempts before giving up on a contended document. Each one costs a pull, a merge and a
    /// push, and losing three in a row means a genuinely busy document rather than a race.
    /// </summary>
    private const int Attempts = 3;

    private readonly SyncOptions _options;
    private readonly AppSession _session;
    private readonly SyncCrypto _crypto;
    private readonly SyncTransport _transport;
    private readonly BrowserStore _store;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _pendingPush;

    /// <summary>
    /// True while this service is the one changing the state, so its own write does not look like
    /// a user edit and schedule another sync.
    /// </summary>
    private bool _applying;

    private SyncKeys? _keys;
    private long? _version;
    private AppState? _ancestor;

    public SyncService(
        SyncOptions options,
        AppSession session,
        SyncCrypto crypto,
        SyncTransport transport,
        BrowserStore store)
    {
        _options = options;
        _session = session;
        _crypto = crypto;
        _transport = transport;
        _store = store;
    }

    public SyncStatus Status { get; private set; } = SyncStatus.Unpaired;

    /// <summary>What happened, in the words the settings page shows. Null when there is nothing to say.</summary>
    public string? Message { get; private set; }

    /// <summary>The pairing code, to show the user so they can type it on the other device.</summary>
    public string? Code { get; private set; }

    public bool Paired => Code is not null;
    public bool Available => _options.Configured;

    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>True when the last sync pulled work in from somewhere else.</summary>
    public bool LastCameFromElsewhere { get; private set; }

    /// <summary>Conflicts the last merge had to resolve, for the page to explain.</summary>
    public MergeReport LastMerge { get; private set; } = MergeReport.None;

    public event Action? Changed;

    private string _device = "";

    /// <summary>
    /// Pick up an existing pairing and sync once. Safe to call from more than one page.
    /// </summary>
    private Task? _init;

    public Task InitAsync() => _init ??= InitCoreAsync();

    private async Task InitCoreAsync()
    {
        if (!_options.Configured)
        {
            Status = SyncStatus.Unavailable;
            return;
        }

        // BEFORE the pairing code is read, and before anything subscribes to changes: a merge
        // reads _session.State, and until this returns that is AppState.Fresh() -- an empty
        // collection, which every rule in StateMerge reads as "the user deleted all of it".
        // MainLayout starts this on first render, long before a page's InitAsync has finished
        // loading state, so without this await the whole feature is a race whose losing side is
        // silent, total and pushed to every other device.
        await _session.InitAsync();

        _device = await DeviceIdAsync();
        _session.Changed += OnLocalChange;

        var saved = PairingCode.TryParse(await _store.GetAsync(CodeKey));
        if (saved is null) return;

        Code = saved;
        _ancestor = StateSerializer.Deserialize(await _store.GetAsync(BaseKey));
        _version = long.TryParse(await _store.GetAsync(VersionKey), out var v) ? v : null;

        await SyncAsync();
    }

    // ---- Pairing -------------------------------------------------------------------------

    /// <summary>
    /// Start syncing this device's collection, and hand back the code for the next one.
    /// </summary>
    public async Task<string?> PairNewAsync()
    {
        if (!_options.Configured) return null;

        await _gate.WaitAsync();
        try
        {
            if (Blocked() is { } why) { Fail(why); return null; }

            Working("Setting up…");

            // A fresh code is a fresh document, so nothing is pulled and nothing is merged: this
            // device's collection simply becomes the stored one.
            var code = PairingCode.New();
            var keys = await _crypto.DeriveAsync(code);

            var state = _session.State;
            var result = await _transport.PushAsync(
                keys, await _crypto.SealAsync(StateSerializer.Serialize(state)), _device, null);

            if (result.Outcome != PushOutcome.Stored)
            {
                Fail(Explain(result.Outcome));
                return null;
            }

            _keys = keys;
            Code = code;
            await RememberAsync(state, result.Version);

            Done(fromElsewhere: false, MergeReport.None);
            return code;
        }
        catch (Exception ex) when (Recoverable(ex))
        {
            Fail(CryptoUnavailable);
            return null;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Join an existing pairing. True when it worked; on failure <see cref="Message"/> says why.
    ///
    /// Every exit reports through <see cref="Fail"/> rather than returning a string, because a
    /// returned string is a second error channel and the page was only rendering the first: a
    /// mistyped code produced a perfectly good sentence that nothing displayed, so pressing Join
    /// appeared to do nothing at all.
    /// </summary>
    public async Task<bool> JoinAsync(string typed)
    {
        if (!_options.Configured)
        {
            Fail("Sync is not available in this build.");
            return false;
        }

        var code = PairingCode.TryParse(typed);
        if (code is null)
        {
            Fail(string.IsNullOrWhiteSpace(typed)
                ? "Type the code from your other device first."
                : $"“{typed.Trim()}” is not a valid code. It is twelve characters, like "
                + "PACK-7K3M-92QX. Check it against the other device.");
            return false;
        }

        await _gate.WaitAsync();
        try
        {
            if (Blocked() is { } why) { Fail(why); return false; }

            Working("Looking for your collection…");

            var keys = await _crypto.DeriveAsync(code);

            RemoteDoc? remote;
            try
            {
                remote = await _transport.PullAsync(keys);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Fail("Could not reach the server. Your collection here is untouched.");
                return false;
            }

            if (remote is null)
            {
                // The code parsed, so it is not a typo -- it is a code for a document that does not
                // exist. Worth separating, because the useful next step is different.
                Fail("Nothing is stored under that code. Check it on the other device, or set sync "
                   + "up here and type this device's code over there instead.");
                return false;
            }

            var opened = await OpenAsync(remote);
            if (opened.State is null)
            {
                Fail(opened.Problem!);
                return false;
            }

            // No ancestor: these two collections have never agreed on anything, so nothing can be
            // read as a deletion and the result is the union of both. Joining two devices should
            // never be the moment something disappears.
            var result = StateMerge.FirstPair(_session.State, opened.State);

            _keys = keys;
            Code = code;

            await AdoptAsync(result.State);
            var pushed = await PushAsync(result.State, remote.Version);

            if (pushed != PushOutcome.Stored)
            {
                // The merge is already applied locally and the code is saved, so the next sync
                // will carry it up. Worth saying, not worth undoing -- and this device IS now
                // paired, so it reports as a sync failure rather than a failed join.
                Fail(Explain(pushed));
                return true;
            }

            Done(fromElsewhere: true, result.Report);
            return true;
        }
        catch (Exception ex) when (Recoverable(ex))
        {
            Fail(CryptoUnavailable);
            return false;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Stop syncing this device. Optionally delete the stored copy, which affects every device on
    /// the pairing and is not reversible.
    /// </summary>
    public async Task UnpairAsync(bool deleteRemote)
    {
        await _gate.WaitAsync();
        try
        {
            if (deleteRemote && _keys is not null) await _transport.ForgetAsync(_keys);

            await _crypto.ForgetAsync();
            await _store.RemoveAsync(CodeKey);
            await _store.RemoveAsync(BaseKey);
            await _store.RemoveAsync(VersionKey);

            _keys = null;
            _ancestor = null;
            _version = null;
            Code = null;
            LastSyncedAt = null;
            LastMerge = MergeReport.None;
            Status = SyncStatus.Unpaired;
            Message = deleteRemote
                ? "Unpaired, and the stored copy is deleted. Your collection is still here."
                : "Unpaired. Your collection is still here, and the stored copy is untouched.";

            Notify();
        }
        finally { _gate.Release(); }
    }

    // ---- The sync itself -----------------------------------------------------------------

    /// <summary>
    /// Why this device must not sync right now, or null when it may.
    ///
    /// All three are the same fault: the state in hand is empty for a reason that is not the user
    /// emptying it, and a merge cannot tell those apart. Refusing is free -- the collection is
    /// local-first and the next launch syncs -- while proceeding writes the emptiness to every
    /// paired device.
    /// </summary>
    private string? Blocked()
    {
        if (!_session.Loaded)
            return "Waiting for your collection to load before syncing.";

        if (_session.LoadFailed)
            return "The collection saved here could not be read, so nothing was synced -- syncing "
                 + "now would copy that loss to your other device. Restore from a backup, or open "
                 + "this on the device that still has your cards.";

        if (_session.StorageUnavailable)
            return "This browser is giving the app no storage, so there is nothing here to sync "
                 + "from. Your other device is untouched.";

        return null;
    }

    /// <summary>Pull, merge, push. The whole of it.</summary>
    public async Task SyncAsync()
    {
        if (!Paired || !_options.Configured) return;

        if (Blocked() is { } why) { Fail(why); return; }

        await _gate.WaitAsync();
        try
        {
            await SyncCoreAsync();
        }
        catch (Exception ex) when (Recoverable(ex))
        {
            // The backstop. Status is what disables every control on the settings page, so an
            // escaping exception would not merely fail a sync -- it would leave the feature looking
            // permanently mid-flight, with no way out but a reload.
            Fail("Sync did not finish. Your collection here is unaffected.");
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// The honest reading when interop fails outright: WebCrypto is unavailable, which in practice
    /// means the page is not on a secure origin. Naming that is more use than "something failed".
    /// </summary>
    private const string CryptoUnavailable =
        "This browser would not derive a key from the code. Sync needs WebCrypto, which needs a "
        + "secure connection. Your collection here is unaffected.";

    /// <summary>
    /// Failures worth reporting rather than crashing on: interop, network, and the JSON of a
    /// response. Deliberately not a bare catch -- a bug in the merge should surface as one.
    /// </summary>
    private static bool Recoverable(Exception ex) =>
        ex is JSException
           or JSDisconnectedException
           or HttpRequestException
           or TaskCanceledException
           or JsonException;

    private async Task SyncCoreAsync()
    {
        Working("Syncing…");

        _keys ??= await _crypto.DeriveAsync(Code!);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            RemoteDoc? remote;
            try
            {
                remote = await _transport.PullAsync(_keys);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Offline is the common case, not a fault: the app works without this, and the
                // next launch will sync. Nothing local changes.
                Fail("Offline. Nothing was synced. Your collection here is unaffected.");
                return;
            }

            if (remote is null)
            {
                // Swept as abandoned, or deleted from another device. Re-establishing it under the
                // same code is right: the user still has the code and still expects it to work.
                var restored = await PushAsync(_session.State, null);
                if (restored == PushOutcome.Stored)
                {
                    Done(fromElsewhere: false, MergeReport.None);
                    return;
                }
                if (restored == PushOutcome.Superseded) continue;   // it came back under us

                Fail(Explain(restored));
                return;
            }

            var opened = await OpenAsync(remote);
            if (opened.State is null)
            {
                Fail(opened.Problem!);
                return;
            }

            // Last line of defence, and the one that does not depend on getting the ordering
            // right somewhere else. Nothing local, something in the ancestor: the merge would
            // read every card, deck and list as deleted here, honour it, and push that up. A user
            // who really did clear everything leaves the deletions recorded behind them, which an
            // unloaded state cannot have, so this cannot be reached by resetting a collection --
            // only by state that was never loaded.
            if (_ancestor is not null
                && StateMerge.NothingRecorded(_session.State)
                && !StateMerge.NothingRecorded(_ancestor))
            {
                Fail("This device came up with an empty collection where it should have one, so "
                   + "nothing was synced. Your stored copy is untouched. Reload the page, and "
                   + "restore from a backup if it is still empty.");
                return;
            }

            var result = _ancestor is null
                ? StateMerge.FirstPair(_session.State, opened.State)
                : StateMerge.Merge(_session.State, opened.State, _ancestor);

            var merged = result.State;
            var cameFromElsewhere = !StateMerge.SameState(_session.State, merged);
            await AdoptAsync(merged);

            // Nothing on either side moved. Saving the round trip matters: this is the path every
            // launch takes for a user with one device.
            if (!cameFromElsewhere && StateMerge.SameState(merged, opened.State))
            {
                await RememberAsync(merged, remote.Version);
                Done(fromElsewhere: false, result.Report);
                return;
            }

            var pushed = await PushAsync(merged, remote.Version);

            if (pushed == PushOutcome.Stored)
            {
                Done(cameFromElsewhere, result.Report);
                return;
            }

            // Another device got there first. Its work is now in the stored copy, so going round
            // again merges it in rather than overwriting it.
            if (pushed == PushOutcome.Superseded) continue;

            Fail(Explain(pushed));
            return;
        }

        Fail("Another device kept writing while this one tried to. Try again in a moment.");
    }

    /// <summary>
    /// Seal and store, recording the result as the new ancestor when it lands.
    /// </summary>
    private async Task<PushOutcome> PushAsync(AppState state, long? expectedVersion)
    {
        var sealedState = await _crypto.SealAsync(StateSerializer.Serialize(state));
        var result = await _transport.PushAsync(_keys!, sealedState, _device, expectedVersion);

        if (result.Outcome == PushOutcome.Stored) await RememberAsync(state, result.Version);

        return result.Outcome;
    }

    private sealed record Opened(AppState? State, string? Problem);

    /// <summary>
    /// Decrypt and parse a stored document, distinguishing the two failures that mean different
    /// things to the user.
    /// </summary>
    private async Task<Opened> OpenAsync(RemoteDoc remote)
    {
        var json = await _crypto.OpenAsync(remote.Payload, remote.Nonce);
        if (json is null)
            return new Opened(null, "The stored copy would not open with this code. Nothing here "
                                  + "was changed.");

        var state = StateSerializer.Deserialize(json);
        if (state is not null) return new Opened(state, null);

        // Deserialize refuses a schema newer than this build understands, on purpose: reading it
        // would drop the fields it does not know and the next push would store that loss. After a
        // release that is the likely reading, and it needs its own sentence.
        return new Opened(null, NewerSchema(json)
            ? "The stored copy was written by a newer version of PackProphet. Reload this page to "
            + "update, then sync again."
            : "The stored copy could not be read. Nothing here was changed.");
    }

    private static bool NewerSchema(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("schemaVersion", out var v)
                && v.TryGetInt32(out var version)
                && version > AppState.CurrentSchemaVersion;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Put a merged state into the app, and only when it differs. Going through ReplaceState means
    /// a sync that pulled something unwelcome is undoable with Ctrl+Z like anything else.
    /// </summary>
    private async Task AdoptAsync(AppState merged)
    {
        if (StateMerge.SameState(_session.State, merged)) return;

        _applying = true;
        try
        {
            _session.ReplaceState(merged);
            await _session.FlushAsync();     // do not leave a merge sitting in a debounce
        }
        finally { _applying = false; }
    }

    private async Task RememberAsync(AppState state, long version)
    {
        _ancestor = state;
        _version = version;

        // Best effort. A quota refusal here costs the ancestor, and the next sync falls back to
        // the union rule -- worse, but not lossy, which is the right way round to fail.
        await _store.SetAsync(BaseKey, StateSerializer.Serialize(state));
        await _store.SetAsync(VersionKey, version.ToString());
        if (Code is not null) await _store.SetAsync(CodeKey, Code);
    }

    // ---- Push after edits ----------------------------------------------------------------

    private void OnLocalChange()
    {
        if (_applying || !Paired) return;

        _pendingPush?.Cancel();
        _pendingPush = new CancellationTokenSource();
        var ct = _pendingPush.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(PushDelay, ct);
                if (!ct.IsCancellationRequested) await SyncAsync();
            }
            catch (OperationCanceledException) { /* superseded by a later edit */ }
        }, ct);
    }

    // ---- Bookkeeping ---------------------------------------------------------------------

    private async Task<string> DeviceIdAsync()
    {
        var existing = await _store.GetAsync(DeviceKey);
        if (!string.IsNullOrWhiteSpace(existing)) return existing;

        // Identifies an install so the app can say which device wrote last. Random, stored only
        // here, and sent alongside ciphertext -- it says nothing about who the user is.
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        await _store.SetAsync(DeviceKey, id);
        return id;
    }

    private static string Explain(PushOutcome outcome) => outcome switch
    {
        PushOutcome.Unreachable =>
            "Could not reach the server. Your collection here is untouched.",
        PushOutcome.TooLarge =>
            "This collection is too large to sync. Export a backup instead.",
        PushOutcome.Gone =>
            "The stored copy is gone: an unused pairing expires. Set sync up again to make a new code.",
        PushOutcome.Refused =>
            "The server refused that code.",
        PushOutcome.Superseded =>
            "Another device is writing right now. Try again in a moment.",
        _ => "Sync did not finish.",
    };

    private void Working(string message)
    {
        Status = SyncStatus.Working;
        Message = message;
        Notify();
    }

    private void Done(bool fromElsewhere, MergeReport report)
    {
        Status = SyncStatus.Synced;
        Message = null;
        LastSyncedAt = DateTimeOffset.Now;
        LastCameFromElsewhere = fromElsewhere;
        LastMerge = report;
        Notify();
    }

    private void Fail(string message)
    {
        Status = SyncStatus.Failed;
        Message = message;
        Notify();
    }

    private void Notify() => Changed?.Invoke();

    public async ValueTask DisposeAsync()
    {
        _session.Changed -= OnLocalChange;
        _pendingPush?.Cancel();
        _gate.Dispose();
        await _crypto.DisposeAsync();
    }
}
