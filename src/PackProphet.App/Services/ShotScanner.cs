using Microsoft.JSInterop;
using PackProphet.Vision;

namespace PackProphet.Services;

/// <summary>
/// The C# side of wwwroot/js/cardshot.js: hands it an image and gets back the card slots it found.
///
/// Thin on purpose. Everything this class could be tempted to decide — what the cards are, whether
/// they are owned, which screen this is — belongs to <see cref="ScreenshotReader"/> in Core, where
/// it is testable without a browser. What is left here is the module import, one call, and the
/// disposal that a page navigated away from mid-scan needs.
/// </summary>
public sealed class ShotScanner : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public ShotScanner(IJSRuntime js) => _js = js;

    /// <summary>
    /// Scans one image, given as a data URL. Never throws for a bad image: a failure comes back as
    /// a <see cref="ShotScan"/> carrying a sentence for the user, because every way this can fail
    /// is a way the user's file can be wrong rather than a way the app is broken.
    /// </summary>
    public async Task<ShotScan> ScanAsync(string dataUrl)
    {
        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/cardshot.js");
            return await _module.InvokeAsync<ShotScan>("scan", dataUrl)
                   ?? ShotScan.Failed("The image reader returned nothing.");
        }
        catch (JSException e)
        {
            return ShotScan.Failed($"The image reader failed ({e.Message.Split('\n')[0]}).");
        }
        catch (Exception e) when (e is TaskCanceledException or ObjectDisposedException)
        {
            // The page was navigated away from while the scan was in flight. Nothing is waiting on
            // this result, so it needs a value rather than an exception.
            return ShotScan.Failed("The scan was interrupted.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;

        try { await _module.DisposeAsync(); }
        catch (JSDisconnectedException) { /* the circuit is already gone */ }
        catch (ObjectDisposedException) { }

        _module = null;
    }
}
