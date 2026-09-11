// Which set you are looking at, while you scroll.
//
// The collection grid can hold every card ever printed — 3,546 of them across twenty-odd sets, in
// set order — and once you are a screen into it there is nothing on screen that says which set the
// cards under your thumb belong to. The set picker names the FILTER, which in that view is "every
// set", so it is no help at all.
//
// A header per set, sticky, is the obvious shape and it cannot be done here: <Virtualize> renders a
// window of uniformly-sized rows into a spacer, so a group header would either break the uniform
// ItemSize the scrollbar depends on or be positioned absolutely inside a container that scrolls
// past it. So the label stays outside the virtualised region — one sticky strip under the toolbar —
// and this file keeps it in step with what is actually visible.
//
// Written straight into the DOM rather than through .NET, for the same reason gridsweep paints its
// selection here: a scroll event that re-rendered a Blazor component would cost a render per frame
// of every scroll, and this changes one text node.

const watched = new WeakMap();

/// The element that actually scrolls. The grid is not always its own scroller — below the desk
/// breakpoint <main> scrolls, above it the grid does — so rather than encode today's layout, walk
/// up for the first ancestor that can scroll and fall back to the window.
function scrollerOf(el) {
    let node = el;
    while (node && node !== document.body) {
        const style = getComputedStyle(node);
        const scrolls = /(auto|scroll|overlay)/.test(style.overflowY);
        if (scrolls && node.scrollHeight > node.clientHeight + 1) return node;
        node = node.parentElement;
    }
    return window;
}

/// The group of the topmost row not yet scrolled under the label.
///
/// Rows carry data-group and sit in DOM order, and only the rendered window exists — a couple of
/// dozen elements — so this is a short linear scan rather than anything that needs an observer per
/// row. `edge` is the bottom of the label itself: the row a reader considers "current" is the first
/// one whose bottom is still below the strip covering it.
function currentGroup(grid, edge) {
    const rows = grid.querySelectorAll('[data-group]');
    for (const row of rows) {
        const box = row.getBoundingClientRect();
        if (box.bottom > edge) return row;
    }
    // Everything rendered is above the edge: mid-scroll between two windows, or the very end of
    // the list. The last row is the honest answer, and it stops the label flickering to empty.
    return rows[rows.length - 1] || null;
}

/// Where the sticky bars above the grid end, in viewport pixels.
///
/// Two things below them want to stick just under, not at zero: this strip and the list view's
/// column headers. Both were sitting at top: 0 with a lower z-index than the toolbar, which is to
/// say both were invisible the moment you scrolled — the toolbar parked on top of them. CSS cannot
/// express "under my previous sibling", because that sibling's height depends on how many rows its
/// contents wrapped into.
///
/// The toolbar's own sticky offset is included: on the log-a-pack screen it sits under a pinned
/// header rather than at the top of the viewport.
///
/// A railed page has no toolbar at all -- its controls are in a column beside the grid rather than
/// on a bar above it -- so there the only thing above the grid is the page's own bar, and that is
/// what the strip has to park under. Measured the same way and from the same place, because the
/// answer this returns is "how far down the viewport is the grid's first visible pixel" and which
/// element is responsible for that is the shell's business rather than this function's.
///
/// Falls through to 0 when neither is there, which is the unmeasured case and the old behaviour.
function barsBottom(label) {
    const bar = label.parentElement?.querySelector('.grid-toolbar')
        ?? stickyBarAbove(label);
    if (!bar) return 0;

    const own = parseFloat(getComputedStyle(bar).top) || 0;
    return own + bar.getBoundingClientRect().height;
}

/// The page's own pinned bar, for a shell that has no toolbar between it and the grid.
///
/// Scoped to the document rather than to the strip's parent: the page head is a sibling of the
/// page body the grid lives in, not a child of it, so the query barsBottom uses cannot see it.
/// Only a PINNED one counts -- a bar that scrolls away stops covering anything, and parking the
/// strip below where it used to be would leave a gap the height of a bar that is no longer there.
function stickyBarAbove(label) {
    if (!label.closest('.railed')) return null;

    const head = document.querySelector('.page-head');
    if (!head) return null;

    return getComputedStyle(head).position === 'sticky' ? head : null;
}

/// The pinned page header's height, for the toolbar below it to sit under.
///
/// This was a constant -- `3rem + 1px`, the header's min-height plus its border -- and it was wrong
/// in both directions. Border-box rounding put the real height a fraction under that at some zoom
/// levels, leaving a sub-pixel strip of scrolling card grid between the two pinned bars; and once
/// the controls in the header grew on a coarse pointer the header became 56px, so the same constant
/// pinned the toolbar nine pixels UNDER the header instead. Measured, it is right at every size.
function headHeight(label) {
    const head = label.parentElement?.querySelector('.page-head.sticky-head');
    return head ? head.getBoundingClientRect().height : 0;
}

/// How far the list has been scrolled, from whichever thing is doing the scrolling.
///
/// Not window.scrollY alone: above the desk breakpoint the grid has its own scroller and the window
/// never moves, so a button keyed to the window would never appear on a desktop.
function scrolledBy(grid) {
    const scroller = scrollerOf(grid);
    return scroller === window
        ? window.scrollY || document.documentElement.scrollTop || 0
        : scroller.scrollTop;
}

export function watch(grid, label) {
    if (!grid || !label || watched.has(grid)) return;

    let pending = 0;

    const update = () => {
        pending = 0;

        // Published for the stylesheet rather than applied here, so one measurement serves both
        // the strip and the list's column headers and neither needs to know about the other.
        const host = label.parentElement;

        // The header first: the toolbar's own sticky offset is set from it, and barsBottom reads
        // that offset back. Setting them the other way round would measure the toolbar against the
        // previous frame's header.
        const head = headHeight(label);
        if (head > 0) host?.style.setProperty('--head-h', `${head}px`);

        const bars = barsBottom(label);
        host?.style.setProperty('--bars-h', `${bars}px`);

        // The strip's own height, so the list view's column headers can sit under it rather than
        // behind it. Zero while it has nothing to say: the stylesheet drops an empty strip, and a
        // header offset by a strip that is not there would float a row's height below the bars.
        host?.style.setProperty('--spy-h', `${label.getBoundingClientRect().height}px`);

        // The line under which a row is covered: the bottom of the bars, plus the strip itself once
        // it has something in it.
        //
        // Derived rather than read off the strip's own box, because the strip is hidden until it has
        // something to say -- so measuring it would give zero on the very pass that decides what to
        // put in it. offsetHeight is 0 in exactly that state, which is the right answer for it.
        const edge = bars + label.offsetHeight;
        const row = currentGroup(grid, edge);

        const name = row?.dataset.group || '';
        const art = row?.dataset.groupArt || '';

        // The set's name, and a wrapper from it. Two nodes written separately -- textContent on one,
        // a background-image on the other -- so nothing here ever assembles markup out of data.
        const text = label.querySelector('[data-spy-text]');
        const picture = label.querySelector('[data-spy-art]');

        if (text && text.textContent !== name) text.textContent = name;
        if (picture) {
            const want = art ? `url("${art}")` : '';
            if (picture.style.backgroundImage !== want) picture.style.backgroundImage = want;
            // A promo set has no wrapper published, so the picture collapses and the name stands
            // alone rather than leaving a hole where one should be.
            picture.classList.toggle('has-art', !!art);
        }

        // Hidden by a class the script owns rather than by :empty, now that the strip has children
        // of its own. Safe against Blazor: the class attribute it renders here is a constant, so the
        // diff never has a new value to write and never puts this back.
        label.classList.toggle('on', !!name);

        // The back-to-top button, on the same pass and for the same reason: this is the one place
        // that already knows how far down the page is, without a second scroll listener.
        //
        // One viewport as the threshold. Less and the button appears while the toolbar it returns
        // you to is still on screen; more and you are hunting for it by the time it is worth having.
        //
        // querySelectorAll, not querySelector: there are two of these now -- one floating over
        // the corner and one on the toolbar for wide screens -- and a single lookup would have
        // driven whichever came first in the DOM and left the other one permanently hidden.
        const far = scrolledBy(grid) > window.innerHeight;
        for (const up of host?.querySelectorAll('[data-to-top]') || [])
            up.classList.toggle('on', far);
    };

    // Coalesced to a frame. A scroll fires this dozens of times a second and the work is a handful
    // of getBoundingClientRect calls, but there is no point doing it twice for one paint.
    const onScroll = () => {
        if (pending) return;
        pending = requestAnimationFrame(update);
    };

    const scroller = scrollerOf(grid);
    scroller.addEventListener('scroll', onScroll, { passive: true });
    // The window as well as the scroller: on a phone the page itself scrolls the bars away first,
    // and the grid's own scroller never fires while that happens.
    if (scroller !== window) window.addEventListener('scroll', onScroll, { passive: true });

    // The rows are the other half of the answer, and they change without a scroll.
    //
    // <Virtualize> swaps its window of rows in a render of its own, which does not run the parent
    // component's OnAfterRender — so C# cannot reliably tell us. Worse, a scroll and the re-render
    // it causes are two separate frames: the last scroll of a flick schedules this before the new
    // rows exist, so reading on the scroll alone leaves the label one window behind whenever
    // scrolling stops. Watching the DOM catches both cases and needs nothing from .NET.
    const mo = typeof MutationObserver === 'undefined' ? null : new MutationObserver(onScroll);
    if (mo) mo.observe(grid, { childList: true, subtree: true });

    // The bars above change height without a scroll and without touching the grid: a filter chip
    // appears, the window narrows and the row wraps, the disclosure opens. Watching the toolbar
    // keeps --bars-h honest through all three.
    const ro = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(onScroll);
    for (const sel of ['.grid-toolbar', '.page-head.sticky-head']) {
        const el = label.parentElement?.querySelector(sel);
        if (ro && el) ro.observe(el);
    }

    watched.set(grid, { scroller, onScroll, mo, ro });
    update();
}

/// Called after the rendered window changes as well as on teardown's behalf, because a new set of
/// rows is a new answer and no scroll event follows a re-render.
export function refresh(grid, label) {
    if (!grid || !label) return;

    const entry = watched.get(grid);
    if (entry) entry.onScroll();
}

export function dispose(grid) {
    const entry = watched.get(grid);
    if (!entry) return;

    entry.scroller.removeEventListener('scroll', entry.onScroll);
    if (entry.scroller !== window) window.removeEventListener('scroll', entry.onScroll);
    if (entry.mo) entry.mo.disconnect();
    if (entry.ro) entry.ro.disconnect();
    watched.delete(grid);
}
