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
        if (box.bottom > edge) return row.dataset.group || '';
    }
    // Everything rendered is above the edge: mid-scroll between two windows, or the very end of
    // the list. The last row is the honest answer, and it stops the label flickering to empty.
    const last = rows[rows.length - 1];
    return last ? last.dataset.group || '' : '';
}

export function watch(grid, label) {
    if (!grid || !label || watched.has(grid)) return;

    let pending = 0;

    const update = () => {
        pending = 0;

        // Sticky, so its own box is where the strip currently sits. Measured each time rather than
        // cached: the toolbars above it wrap at different widths, and the disclosure opening moves
        // it several rem.
        const edge = label.getBoundingClientRect().bottom;
        const group = currentGroup(grid, edge);

        // Only one group in scope — a single set selected — and there is nothing to report. Empty
        // rather than hidden: the stylesheet drops an empty strip, so there is no attribute for
        // Blazor's diff and this file to disagree about.
        if (label.textContent !== group) label.textContent = group;
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

    watched.set(grid, { scroller, onScroll, mo });
    update();
}

/// Called after the rendered window changes as well as on teardown's behalf, because a new set of
/// rows is a new answer and no scroll event follows a re-render.
export function refresh(grid, label) {
    if (!grid || !label) return;
    const edge = label.getBoundingClientRect().bottom;
    const group = currentGroup(grid, edge);
    if (label.textContent !== group) label.textContent = group;
}

export function dispose(grid) {
    const entry = watched.get(grid);
    if (!entry) return;

    entry.scroller.removeEventListener('scroll', entry.onScroll);
    if (entry.scroller !== window) window.removeEventListener('scroll', entry.onScroll);
    if (entry.mo) entry.mo.disconnect();
    watched.delete(grid);
}
