namespace PackProphet.Services;

/// <summary>
/// Lets anything ask for the command palette without owning it.
///
/// The palette is rendered once, by the layout, while the control that opens it sits in the nav —
/// and on a phone there is no Ctrl+K, so a tappable button is needed. A one-line event avoids
/// duplicating the palette per caller or threading a cascading parameter through every page.
/// </summary>
public sealed class PaletteSwitch
{
    public event Action? Requested;

    public void Toggle() => Requested?.Invoke();

    /// <summary>
    /// Whether the palette is currently open, so the layout can hide the rest of the page from
    /// the accessibility tree while it is up — a real modal, not just something drawn on top.
    /// </summary>
    public bool IsOpen { get; private set; }

    public event Action? OpenedChanged;

    public void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;
        OpenedChanged?.Invoke();
    }
}
