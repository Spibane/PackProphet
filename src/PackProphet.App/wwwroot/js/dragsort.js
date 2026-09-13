// Drag one item of a shelf to a new place in it.
//
// Pointer events rather than HTML5 drag-and-drop, for one reason: HTML5 dragging does not exist on
// touch. The shelves this reorders are read on a phone as often as at a desk, and a reorder that
// works only with a mouse is a reorder half the app cannot do.
//
// It also means the same code path on both, rather than a mouse story and a finger story that
// drift. The cost is doing the work a browser would otherwise do: hit-testing, the moved element's
// position, and the guard below about what counts as a drag rather than a scroll.
//
// Nothing here re-renders Blazor. The lifted item is moved in the DOM as the pointer travels, and
// .NET is told once, on release, where it ended up -- so dragging across a shelf of forty costs no
// renders and the component re-renders once from its own state afterwards.

let host = null, dotnet = null;

// The item being dragged, where it started, and the pointer that owns it.
let item = null, startIndex = -1, pointerId = null;

// Where the finger was when it went down, so a scroll can be told from a drag. Below this many
// pixels nothing has happened yet and the page keeps the gesture.
let startX = 0, startY = 0, armed = false;
const SLOP = 6;

function items() {
    return [...host.querySelectorAll('[data-drag-id]')];
}

function indexOf(el) {
    return items().indexOf(el);
}

// The item under the pointer, if it is one of ours and not the one being carried.
function targetAt(x, y) {
    const el = document.elementFromPoint(x, y);
    const row = el && el.closest ? el.closest('[data-drag-id]') : null;
    return row && row !== item && host.contains(row) ? row : null;
}

function onDown(e) {
    // Only from the handle. A shelf item is a card with links and buttons all over it, and making
    // the whole thing draggable means every press on it is a maybe-drag.
    const handle = e.target.closest ? e.target.closest('[data-drag-handle]') : null;
    if (!handle || pointerId !== null) return;

    const row = handle.closest('[data-drag-id]');
    if (!row || !host.contains(row)) return;

    item = row;
    pointerId = e.pointerId;
    startIndex = indexOf(row);
    startX = e.clientX; startY = e.clientY;
    armed = false;
}

function onMove(e) {
    if (pointerId !== e.pointerId || !item) return;

    if (!armed) {
        if (Math.abs(e.clientX - startX) < SLOP && Math.abs(e.clientY - startY) < SLOP) return;
        armed = true;
        item.classList.add('dragging');
        host.classList.add('dragging-in');
        // Claimed only once the gesture is definitely a drag, so a scroll that starts on a handle
        // is still a scroll.
        handleCapture(e);
    }

    // The drag now owns the gesture, so the page must not also scroll with it.
    e.preventDefault();

    const over = targetAt(e.clientX, e.clientY);
    if (!over) return;

    // Insert before or after depending on which half was entered, so passing the midpoint of a
    // neighbour swaps with it and passing back swaps back -- rather than the item jittering
    // between two places while the pointer sits on a boundary.
    const box = over.getBoundingClientRect();
    const past = host.classList.contains('dragsort-rows')
        ? e.clientY > box.top + box.height / 2
        : e.clientX > box.left + box.width / 2;

    over.parentNode.insertBefore(item, past ? over.nextSibling : over);
}

function handleCapture(e) {
    try { e.target.setPointerCapture(e.pointerId); } catch { /* already gone */ }
}

function onUp() {
    if (!item) return;

    const moved = armed;
    const id = item.dataset.dragId;
    const to = indexOf(item);

    item.classList.remove('dragging');
    host.classList.remove('dragging-in');
    item = null; pointerId = null; armed = false;

    // Only when it actually landed somewhere else. A press on the handle that goes nowhere is not
    // an edit, and reporting it would put a no-op on the undo stack.
    if (moved && to >= 0 && to !== startIndex) dotnet.invokeMethodAsync('Dropped', id, to);
}

export function attach(element, dotnetRef) {
    host = element; dotnet = dotnetRef;
    host.addEventListener('pointerdown', onDown);
    host.addEventListener('pointermove', onMove, { passive: false });
    window.addEventListener('pointerup', onUp);
    window.addEventListener('pointercancel', onUp);
}

export function dispose() {
    if (!host) return;
    host.removeEventListener('pointerdown', onDown);
    host.removeEventListener('pointermove', onMove);
    window.removeEventListener('pointerup', onUp);
    window.removeEventListener('pointercancel', onUp);
    host = null; dotnet = null; item = null; pointerId = null;
}
