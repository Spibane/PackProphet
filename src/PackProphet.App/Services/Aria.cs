namespace PackProphet.Services;

/// <summary>
/// Boolean ARIA state values, rendered as words.
///
/// Blazor omits an attribute whose value is <c>false</c>. That is right for HTML's own boolean
/// attributes — <c>disabled="false"</c> would disable the control — and wrong for every ARIA state,
/// where the two values are the strings "true" and "false" and the absence of the attribute is a
/// third, different thing: it says the element has no such state at all.
///
/// So <c>aria-pressed="@Pressed"</c> announced a toggle that was OFF as an ordinary button. The one
/// state that needed saying was the state that said nothing, and the failure was invisible in the
/// markup — the attribute was written, it just never arrived. Every toggle in this app therefore
/// renders the word through here.
///
/// Found by a test that clicked <c>button[aria-pressed]</c> and hit the wrong button, because the
/// unpressed ones did not match the selector.
/// </summary>
public static class Aria
{
    /// <summary>An ARIA boolean: the word, always, whichever way it is set.</summary>
    public static string Flag(bool on) => on ? "true" : "false";
}
