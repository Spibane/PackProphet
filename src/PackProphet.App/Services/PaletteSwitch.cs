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
}
