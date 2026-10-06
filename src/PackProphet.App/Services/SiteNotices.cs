using PackProphet.Data;

namespace PackProphet.Services;

/// <summary>
/// Decides what the bar above every page says, if anything.
///
/// ONE BAR, ONE NOTICE
/// ==================================================================================
/// Two things can produce a notice — the app working out that its own data is behind
/// (<see cref="DataLag"/>) and a human posting one (<see cref="NoticeFeed"/>) — and both of them
/// can be true at once, on release day of a set during an outage. Exactly one is shown.
///
/// That is the design, not a simplification. The bar sits above every page in the app, so a second
/// row is a second row on every page; and a user who is told two things at once acts on neither.
/// The one shown is the most severe, and an authored notice outranks a derived one at equal
/// severity — somebody wrote it on purpose, and the derived ones will still be true tomorrow.
///
/// WHAT IS NOT SHOWN, AND WHEN
/// ==================================================================================
/// Derived notices wait for a collection. A first visit is already a wall of onboarding, and
/// "Genetic Apex has no pull rates yet" means nothing to somebody who has not entered a card —
/// it is an answer to a question they have not asked. Authored notices do NOT wait: an outage is
/// as true for a new visitor as for anyone else, and is the more confusing of the two to meet
/// without explanation.
/// </summary>
public sealed class SiteNotices
{
    private readonly AppSession _session;
    private readonly NoticeFeed _feed;

    /// <summary>
    /// Notices dismissed for this page load only — the ones whose condition is re-tested on every
    /// boot. See <see cref="SiteNotice.Persist"/>.
    /// </summary>
    private readonly HashSet<string> _forNow = new(StringComparer.Ordinal);

    public SiteNotices(AppSession session, NoticeFeed feed)
    {
        _session = session;
        _feed = feed;
    }

    /// <summary>Raised when the feed lands, so the layout can render a bar it did not have.</summary>
    public event Action? Changed;

    /// <summary>
    /// Fetch the authored feed. Called once, after first render, and not awaited into anything:
    /// the bar appears when it answers.
    /// </summary>
    public async Task InitAsync()
    {
        await _feed.LoadAsync();
        if (_feed.Notices.Count > 0) Changed?.Invoke();
    }

    private object? _signature;
    private SiteNotice? _current;

    /// <summary>
    /// The notice to show, or null.
    ///
    /// Memoised, because this is read on every render of the layout and therefore on every render
    /// of the app. <see cref="AppSession.SetsAwaitingRates"/> walks every openable set and hits the
    /// rate table for each — cheap on its own, and not something to do sixty times a second behind
    /// a bar that changes about once a month.
    /// </summary>
    public SiteNotice? Current
    {
        get
        {
            var now = Signature();
            if (!Equals(now, _signature))
            {
                _signature = now;
                _current = Pick();
            }
            return _current;
        }
    }

    /// <summary>
    /// Everything <see cref="Pick"/> reads, in a form that compares. Data and the feed's list are
    /// compared by reference: both are replaced wholesale when they change and never mutated.
    ///
    /// The dismissals are here rather than a revision this class bumps itself, because dismissing
    /// is not the only thing that changes them: the settings page can clear the persisted ones,
    /// and a counter incremented in <see cref="Dismiss"/> would not notice — the bar would stay
    /// hidden after being switched back on, until something unrelated moved.
    ///
    /// The persisted list by reference, not by count. A dismissal can replace a key rather than
    /// add one — a new "waiting:" notice prunes the last, a full list drops its oldest — and the
    /// count holding still kept a just-dismissed notice on screen. The session-only set only ever
    /// grows, so its count is enough.
    /// </summary>
    private object Signature() => (
        _session.Loaded,
        _session.Data,
        _feed.Notices,
        _session.DismissedNotices,
        _forNow.Count);

    private SiteNotice? Pick()
    {
        // Rank, then notice. Feed entries carry the lower rank number so they win a tie at equal
        // severity — see the class comment.
        var candidates = new List<(int Rank, SiteNotice Notice)>();

        foreach (var notice in _feed.Notices) candidates.Add((0, notice));
        foreach (var notice in Derived()) candidates.Add((1, notice));

        return candidates
            .Where(c => !Dismissed(c.Notice))
            .OrderByDescending(c => c.Notice.Level)
            .ThenBy(c => c.Rank)
            .Select(c => c.Notice)
            .FirstOrDefault();
    }

    private IEnumerable<SiteNotice> Derived()
    {
        // Not before the saved state has been read back. Shown with or without a collection: a
        // first visit on release day is looking at the set the bar is about, and an empty grid of
        // placeholders reads as broken without it.
        if (!_session.Loaded || _session.Data is not { } data) yield break;

        if (data.Source == DataSource.VendoredSnapshot)
        {
            yield return DataLag.Offline(data.Version);

            // Nothing else, and not as a simplification. Against a bundled snapshot every set
            // released since the deploy is missing outright, so "this set has no art or detail"
            // would be a reading of a table that is simply old — and the notice above already
            // says so, better and once.
            yield break;
        }

        // Only once the detail table has finished loading. It arrives in the background after the
        // UI is usable, and before it lands EVERY set is legitimately uncovered — so asking early
        // reports the whole game as missing for the second or two it takes to parse.
        //
        // FactsReady is also false when the table could not be fetched at all, and the same gate
        // is right for that: "every set is missing detail" would be a reading of a failed request
        // rather than of what upstream has published.
        var detail = _session.FactsReady
            ? DataLag.DetailShortfalls(data.Index, data.Facts)
            : [];

        if (DataLag.Waiting(DataLag.ArtShortfalls(data.ArtGaps), detail, _session.Sets) is { } lag)
            yield return lag;
    }

    private bool Dismissed(SiteNotice notice) =>
        notice.Persist ? _session.NoticeDismissed(notice.Key) : _forNow.Contains(notice.Key);

    /// <summary>
    /// Hide a notice. Remembered across reloads or only for this one, as the notice itself says.
    /// </summary>
    public void Dismiss(SiteNotice notice)
    {
        // A persisted dismissal arrives back as a Session change, which the bar is subscribed to.
        // A session-only one changes nothing the Session knows about, so it is announced here.
        if (notice.Persist) _session.DismissNotice(notice.Key);
        else { _forNow.Add(notice.Key); Changed?.Invoke(); }
    }
}
