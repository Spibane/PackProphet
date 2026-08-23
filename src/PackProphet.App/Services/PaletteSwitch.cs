namespace PackProphet.Services;

/// <summary>
/// Lets anything ask for the command palette without owning it.
///
/// It exists because the palette is rendered once, by the layout, while the thing that opens
/// it sits in the nav — and on a phone there is no Ctrl+K at all, so a tappable button is not
/// optional. A one-line event beats either duplicating the palette per caller or threading a
/// cascading parameter through every page.
/// </summary>
public sealed class PaletteSwitch
{
    public event Action? Requested;

    public void Toggle() => Requested?.Invoke();
}
