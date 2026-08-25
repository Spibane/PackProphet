// The real height of one virtualised row.
//
// <Virtualize> is told an ItemSize in pixels and trusts it: it renders however many rows that
// figure says will fill the viewport, then compares what it guessed against what the browser
// actually laid out and renders again if they disagree. A row here is a strip of card tiles whose
// height comes from the tile's aspect ratio against whatever width the grid happens to have, so
// the only honest way to know it is to measure it.
//
// C# guessed instead, from a formula that assumed a 1000px-wide grid. On a phone that claimed a
// row was two and a half times its real height, so Virtualize rendered a couple of rows for a
// screenful, saw the gap, rendered more, and the correction landed on every scroll event — which
// is the flicker.
//
// Measured rather than recomputed from the CSS, so nothing here has to be kept in step with the
// tile ratio, the gap or the row padding.

const watched = new WeakMap();

/// The laid-out height of the first row matching `selector`, and the grid's own width.
///
/// Both, because there is a case with no row to measure: Virtualize renders nothing at all until
/// it believes something is visible, and it can sit in that state for a while — a background tab
/// is enough. The width still lets C# work the height out to within a pixel or two, which is the
/// difference between a good estimate and one that never improves.
export function metrics(el, selector) {
    if (!el) return { row: 0, width: 0 };

    const row = el.querySelector(selector);
    const height = row ? row.getBoundingClientRect().height : 0;

    return { row: height > 0 ? height : 0, width: el.clientWidth };
}

/// Tell .NET to measure again whenever the grid changes width. Rotating a phone, opening the
/// toolbar disclosure and dragging a desktop window all change the row height without changing
/// anything C# knows about.
export function watch(el, dotnet) {
    if (!el || watched.has(el) || typeof ResizeObserver === 'undefined') return;

    let width = el.clientWidth;
    // Coalesced to a frame: a drag-resize fires this continuously, and each call is a round trip
    // into .NET followed by a re-render of the grid.
    let pending = 0;

    const ro = new ResizeObserver(() => {
        if (el.clientWidth === width) return;   // height changes are our own doing
        width = el.clientWidth;
        if (pending) return;
        pending = requestAnimationFrame(() => {
            pending = 0;
            dotnet.invokeMethodAsync('RemeasureRows').catch(() => { /* torn down */ });
        });
    });

    ro.observe(el);
    watched.set(el, ro);
}

export function dispose(el) {
    const ro = watched.get(el);
    if (ro) { ro.disconnect(); watched.delete(el); }
}
