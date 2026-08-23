// Drag-to-sweep selection over the card grid.
//
// Two problems this solves, both specific to touch:
//   1. Touch has IMPLICIT POINTER CAPTURE — once a finger goes down on a tile,
//      every later pointermove/pointerenter targets that same tile. So the naive
//      "@onpointerenter on each tile" approach can never see the tiles you drag
//      over. We hit-test with elementFromPoint instead.
//   2. A vertical drag is a scroll gesture unless touch-action says otherwise, so
//      touch sweeping only arms while the caller enables sweep mode.
//
// Highlighting is done here by toggling a CSS class rather than round-tripping to
// Blazor, so a sweep across hundreds of tiles costs zero re-renders. .NET is
// called exactly once, on release.

let host = null, dotnet = null;
let sweepEnabled = false;
let active = false, fromIdx = -1, toIdx = -1, activePointer = null;

function idxAt(x, y) {
    const el = document.elementFromPoint(x, y);
    const tile = el && el.closest ? el.closest('[data-idx]') : null;
    return tile ? parseInt(tile.dataset.idx, 10) : -1;
}

function paint() {
    const lo = Math.min(fromIdx, toIdx), hi = Math.max(fromIdx, toIdx);
    for (const t of host.querySelectorAll('[data-idx]')) {
        const i = parseInt(t.dataset.idx, 10);
        t.classList.toggle('sel', active && i >= lo && i <= hi);
    }
}

function clearPaint() {
    for (const t of host.querySelectorAll('[data-idx].sel')) t.classList.remove('sel');
}

function onDown(e) {
    if (e.pointerType === 'touch' && !sweepEnabled) return; // let the page scroll
    if (e.pointerType === 'mouse' && e.button !== 0) return;
    const i = idxAt(e.clientX, e.clientY);
    if (i < 0) return;

    // Stop the browser starting a native image drag. Without this a mouse sweep just picks
    // the card art up and drags a ghost of it around, and the pointer stream is hijacked so
    // no selection ever happens. Needs a non-passive listener to be allowed.
    e.preventDefault();
    // Drop implicit capture so we keep getting moves as the pointer leaves this tile.
    if (e.target.releasePointerCapture) {
        try { e.target.releasePointerCapture(e.pointerId); } catch { /* not captured */ }
    }
    active = true; activePointer = e.pointerId; fromIdx = toIdx = i;
}

function onDragStart(e) {
    e.preventDefault();
    return false;
}

function onMove(e) {
    if (!active || e.pointerId !== activePointer) return;
    const i = idxAt(e.clientX, e.clientY);
    if (i < 0 || i === toIdx) return;
    toIdx = i;
    paint();
    if (sweepEnabled) e.preventDefault();
}

function onUp(e) {
    if (!active || e.pointerId !== activePointer) return;
    const lo = Math.min(fromIdx, toIdx), hi = Math.max(fromIdx, toIdx);
    active = false; activePointer = null;
    clearPaint();
    if (hi > lo) {
        // Real sweep. Swallow the click that follows so the tap handler in Blazor
        // does not also increment the tile the finger happened to lift over.
        window.addEventListener('click', ev => { ev.stopPropagation(); ev.preventDefault(); },
            { capture: true, once: true });
        dotnet.invokeMethodAsync('ApplySweep', lo, hi);
    }
    fromIdx = toIdx = -1;
}

export function attach(element, dotnetRef) {
    host = element; dotnet = dotnetRef;
    host.addEventListener('pointerdown', onDown, { passive: false });
    // Belt and braces: even with preventDefault above, a drag can begin from an image that
    // was already under the cursor, so refuse dragstart outright inside the grid.
    host.addEventListener('dragstart', onDragStart);
    host.addEventListener('pointermove', onMove, { passive: false });
    // Listen on window for up/cancel: the finger often lifts outside the grid.
    window.addEventListener('pointerup', onUp);
    window.addEventListener('pointercancel', onUp);
}

export function setSweep(on) {
    sweepEnabled = !!on;
    if (host) host.classList.toggle('sweeping', sweepEnabled);
}

export function dispose() {
    if (!host) return;
    host.removeEventListener('pointerdown', onDown);
    host.removeEventListener('dragstart', onDragStart);
    host.removeEventListener('pointermove', onMove);
    window.removeEventListener('pointerup', onUp);
    window.removeEventListener('pointercancel', onUp);
    host = null; dotnet = null;
}
