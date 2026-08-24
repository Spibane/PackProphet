namespace PackProphet.Services;

/// <summary>
/// Shows the user that work is happening during a slow synchronous computation.
///
/// WebAssembly runs on a single thread, so a long calculation blocks rendering completely and the
/// page looks broken rather than busy. The work cannot be moved off-thread, so a "working" state is
/// painted before starting and the browser is given a frame to draw it, which is what
/// <see cref="RunAsync"/> sequences.
///
/// Every path slow enough to be noticed goes through here rather than reimplementing the yield
/// sequence.
/// </summary>
public sealed class UiBusy
{
    public string? Label { get; private set; }

    public bool IsBusy => Label is not null;

    public event Action? Changed;

    /// <summary>
    /// Announce <paramref name="label"/>, let the browser paint it, then run
    /// <paramref name="work"/>. The label clears even if the work throws.
    /// </summary>
    public async Task RunAsync(string label, Action work)
    {
        Label = label;
        Changed?.Invoke();

        // Both are needed: Yield hands control back to the Blazor renderer, and the delay
        // gives the browser an actual frame in which to paint. Without the delay the label
        // is queued but never drawn before the thread is seized again.
        await Task.Yield();
        await Task.Delay(1);

        try
        {
            work();
        }
        finally
        {
            Label = null;
            Changed?.Invoke();
        }
    }

    /// <summary>Same, for work that is already asynchronous.</summary>
    public async Task RunAsync(string label, Func<Task> work)
    {
        Label = label;
        Changed?.Invoke();
        await Task.Yield();
        await Task.Delay(1);

        try
        {
            await work();
        }
        finally
        {
            Label = null;
            Changed?.Invoke();
        }
    }
}
