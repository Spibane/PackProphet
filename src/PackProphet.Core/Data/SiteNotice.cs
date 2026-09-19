namespace PackProphet.Data;

/// <summary>
/// How much of the reader's attention a notice is entitled to.
///
/// Three rather than <see cref="PackProphet.Data"/>'s five-level inline notice, because this is
/// the bar above every page: "success" and "unverified" are answers about something on screen,
/// and there is nothing on screen here to be an answer about.
/// </summary>
public enum NoticeLevel
{
    /// <summary>A fact. The data is behind, and it will catch up on its own.</summary>
    Info,

    /// <summary>Something is degraded and will stay that way until it is fixed.</summary>
    Warning,

    /// <summary>Something is broken.</summary>
    Problem,
}

/// <summary>
/// One message for the bar above every page.
///
/// A notice is identified by its <paramref name="Key"/> rather than by its text, because that is
/// what a dismissal is recorded against. The distinction is the whole reason this type exists: the
/// app already had two dismissible strips and both were a single boolean, so dismissing one was
/// dismissing the feature. A bar that announces whichever thing is currently behind cannot work
/// that way — silencing "B4a has no rates yet" has to leave "B5 has no rates yet" free to appear.
///
/// So a key names the SUBJECT, not the wording. Rewording a notice must not bring it back, and a
/// new subject must not inherit an old dismissal.
/// </summary>
/// <param name="Persist">
/// Whether dismissing it is remembered across reloads.
///
/// True for a standing fact: an unpriced set stays unpriced for days, so a dismissal that lasted
/// one page load would be no dismissal at all. False for a condition that is re-tested on every
/// boot — the CDN being unreachable is the case — where a remembered dismissal would silence a
/// real fault months later on the strength of one bad afternoon.
/// </param>
public sealed record SiteNotice(
    string Key,
    NoticeLevel Level,
    string Text,
    string? ActionLabel = null,
    string? ActionHref = null,
    bool Persist = true)
{
    /// <summary>True when there is a link to offer, i.e. both halves of one are present.</summary>
    public bool HasAction => !string.IsNullOrWhiteSpace(ActionLabel)
                          && !string.IsNullOrWhiteSpace(ActionHref);
}
