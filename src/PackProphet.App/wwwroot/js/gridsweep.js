// Drag-to-sweep selection over the card grid.
//
// Two problems this solves, both specific to touch:
//   1. Touch has implicit pointer capture — once a finger goes down on a tile, every later
//      pointermove/pointerenter targets that same tile, so per-tile @onpointerenter never sees
//      the tiles you drag over. This hit-tests with elementFromPoint instead.
//   2. A vertical drag is a scroll gesture unless touch-action says otherwise, so touch sweeping
//      only arms while the caller enables sweep mode.
//
// Highlighting is done here by toggling a CSS class rather than round-tripping to Blazor, so a
// sweep across hundreds of tiles costs zero re-renders. .NET is called once, on release.

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

// Controls inside a row keep their own behaviour. This function preventDefaults every pointerdown
// to stop the browser dragging card art, and focusing a field is a default action, so without this
// guard the typed count field highlighted on hover and could never be clicked into. Buttons and
// links survived only because their click still fired.
const CONTROLS = 'input, textarea, select, button, a, [contenteditable="true"]';

// List rows are full of selectable text, so a mouse drag there means "select this text" more often
// than "sweep these rows". Hijacking it destroyed the selection and range-selected rows nobody
// asked for, with the sweep toggle reading "off". Tiles have no text and no scroll gesture to lose,
// so an unarmed mouse drag over the grid stays a selection.
function listing() {
    return !!host && host.classList.contains('listing');
}

function onDown(e) {
    // Touch always needs the toggle, or the page cannot be scrolled. The mouse needs it too
    // in list mode, for the reason above.
    if (!sweepEnabled && (e.pointerType === 'touch' || listing())) return;
    if (e.pointerType === 'mouse' && e.button !== 0) return;
    if (e.target && e.target.closest && e.target.closest(CONTROLS)) return;
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

// Whether this device has a coarse pointer AT ALL — a capability, not the primary input, so
// a touchscreen laptop still answers true. Grid sweeping is only ever *needed* for touch,
// since an unarmed mouse drag already range-selects tiles, so a device with no touch input
// has no use for the toggle in grid mode. Deliberately any-pointer rather than pointer:
// keying it to the primary input would hide the toggle on a hybrid and leave a finger with
// no way to sweep at all.
export function hasCoarsePointer() {
    return window.matchMedia('(any-pointer: coarse)').matches;
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
