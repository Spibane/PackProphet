namespace PackProphet.Services;

using Microsoft.JSInterop;

/// <summary>What the server is allowed to know, derived from a pairing code.</summary>
/// <param name="Id">The document id it files the blob under.</param>
/// <param name="Auth">Proof of knowing the code. Independent of the encryption key.</param>
public sealed record SyncKeys(string Id, string Auth);

/// <summary>A sealed payload, ready to hand over.</summary>
public sealed record SealedState(string Payload, string Nonce);

/// <summary>
/// Thin wrapper over js/sync.js. The encryption key deliberately never reaches .NET -- the module
/// holds it and this asks it to work -- so there is no property here that could leak one.
/// </summary>
public sealed class SyncCrypto : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public SyncCrypto(IJSRuntime js) => _js = js;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/sync.js");

    /// <summary>
    /// Stretch the code into an id and a proof token, and arm the module for encrypt/decrypt.
    /// Takes a noticeable moment on a phone -- 300,000 PBKDF2 iterations -- so callers show a
    /// spinner rather than letting the page look wedged.
    /// </summary>
    public async Task<SyncKeys> DeriveAsync(string code) =>
        await (await ModuleAsync()).InvokeAsync<SyncKeys>("derive", code)
        ?? throw Silent(nameof(DeriveAsync));

    public async Task<SealedState> SealAsync(string json) =>
        await (await ModuleAsync()).InvokeAsync<SealedState>("encrypt", json)
        ?? throw Silent(nameof(SealAsync));

    /// <summary>
    /// Interop answered with nothing where a value was required.
    ///
    /// Guarded here rather than trusted, because a null does not stay a null: it travels one more
    /// call and surfaces as a NullReferenceException from inside the transport, which reads as a
    /// bug in the wrong file. A JSException is what this actually is -- the JavaScript side did not
    /// produce a value -- and callers already treat that as a sync that could not run.
    /// </summary>
    private static JSException Silent(string call) =>
        new($"js/sync.js returned nothing from {call}.");

    /// <summary>Null when the blob will not open, which must never disturb the local collection.</summary>
    public async Task<string?> OpenAsync(string payload, string nonce) =>
        await (await ModuleAsync()).InvokeAsync<string?>("decrypt", payload, nonce);

    public async Task ForgetAsync()
    {
        if (_module is null) return;
        try { await _module.InvokeVoidAsync("forget"); }
        catch (JSDisconnectedException) { /* page is going away; the key goes with it */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try { await _module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }
}
