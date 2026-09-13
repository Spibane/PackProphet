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
//
// THE ITEM FOLLOWS THE FINGER
// ==================================================================================
// It used to only be re-inserted: you pressed the grip, moved, and the card appeared somewhere
// else. Nothing was ever seen to travel, so the shelf read as a control that teleports rather than
// as a thing being carried -- and with nothing under the finger there was no way to aim, which is
// the whole reason a drag is a drag.
//
// So the carried item is translated to the pointer every move, and its neighbours FLIP into the
// space it opens: their positions are measured before the re-insert, transformed back to where
// they were, and then released to travel to the new ones. Both are transforms, so neither costs a
// layout pass per frame and neither disturbs the order the DOM is actually in -- which is still
// the thing read back on release.

let host = null, dotnet = null;

// The item being dragged, where it started, and the pointer that owns it.
let item = null, startIndex = -1, pointerId = null;

// Where the finger was when it went down, so a scroll can be told from a drag. Below this many
// pixels nothing has happened yet and the page keeps the gesture.
let startX = 0, startY = 0, armed = false;
const SLOP = 6;

// Where inside the item the finger took hold, so the card stays under the same point of itself for
// the whole drag rather than jumping its own width at the first move.
let grabX = 0, grabY = 0;

// A vertical shelf -- table rows -- where sideways travel means nothing and drawing it only makes
// the row look detached from its table.
let rows = false;

// How long a displaced neighbour takes to reach its new place, and the carried item to settle into
// the slot it was dropped in. Short: this is feedback about something you did, not an animation.
const GLIDE = 150;

function items() {
    return [...host.querySelectorAll('[data-drag-id]')];
}

function indexOf(el) {
    return items().indexOf(el);
}

// The item under the pointer, if it is one of ours and not the one being carried.
//
// elementsFromPoint rather than elementFromPoint: the carried item is drawn under the finger and
// above its neighbours, so the topmost element at the pointer is always itself.
function targetAt(x, y) {
    for (const el of document.elementsFromPoint(x, y)) {
        const row = el.closest ? el.closest('[data-drag-id]') : null;
        if (row && row !== item && host.contains(row)) return row;
    }
    return null;
}

// Draw the item where the finger is holding it. Measured with the transform off, because the
// offset has to be from where the item LIES -- which changes every time it is re-inserted.
function place(x, y) {
    item.style.transform = 'none';
    const box = item.getBoundingClientRect();
    const dx = rows ? 0 : (x - grabX) - box.left;
    const dy = (y - grabY) - box.top;
    item.style.transform = `translate(${dx}px, ${dy}px)`;
}

function snapshot() {
    const was = new Map();
    for (const el of items()) if (el !== item) was.set(el, el.getBoundingClientRect());
    return was;
}

// Move the neighbours from where they were to where they now are. They are already in their new
// places by the time this runs; the transform puts them back, and dropping it lets them travel.
function glide(was) {
    const moved = [];

    for (const [el, before] of was) {
        const now = el.getBoundingClientRect();
        const dx = before.left - now.left, dy = before.top - now.top;
        if (!dx && !dy) continue;

        el.style.transition = 'none';
        el.style.transform = `translate(${dx}px, ${dy}px)`;
        moved.push(el);
    }

    if (moved.length === 0) return;

    requestAnimationFrame(() => {
        for (const el of moved) {
            el.style.transition = `transform ${GLIDE}ms ease`;
            el.style.transform = '';
        }
    });
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

        rows = host.classList.contains('dragsort-rows');
        const box = item.getBoundingClientRect();
        grabX = startX - box.left;
        grabY = startY - box.top;

        item.classList.add('dragging');
        host.classList.add('dragging-in');
        // Claimed only once the gesture is definitely a drag, so a scroll that starts on a handle
        // is still a scroll.
        handleCapture(e);
    }

    // The drag now owns the gesture, so the page must not also scroll with it.
    e.preventDefault();

    place(e.clientX, e.clientY);

    const over = targetAt(e.clientX, e.clientY);
    if (!over) return;

    // Insert before or after depending on which half was entered, so passing the midpoint of a
    // neighbour swaps with it and passing back swaps back -- rather than the item jittering
    // between two places while the pointer sits on a boundary.
    const box = over.getBoundingClientRect();
    const past = rows
        ? e.clientY > box.top + box.height / 2
        : e.clientX > box.left + box.width / 2;

    const next = past ? over.nextSibling : over;
    if (next === item || (next === null && item === over.parentNode.lastChild)) return;

    const was = snapshot();
    over.parentNode.insertBefore(item, next);
    glide(was);
    place(e.clientX, e.clientY);
}

function handleCapture(e) {
    try { e.target.setPointerCapture(e.pointerId); } catch { /* already gone */ }
}

// Everything this drag wrote on the DOM, off. The carried item keeps its transition for one beat
// so it travels the last few pixels into the slot rather than snapping there.
function settle(carried) {
    for (const el of items()) {
        if (el === carried) continue;
        el.style.transition = '';
        el.style.transform = '';
    }

    if (!carried) return;

    carried.style.transition = `transform ${GLIDE}ms ease`;
    carried.style.transform = 'none';
    setTimeout(() => { carried.style.transition = ''; carried.style.transform = ''; }, GLIDE + 40);
}

function onUp() {
    if (!item) return;

    const moved = armed;
    const carried = item;
    const id = carried.dataset.dragId;
    const to = indexOf(carried);

    carried.classList.remove('dragging');
    host.classList.remove('dragging-in');
    settle(moved ? carried : null);

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
    host = null; dotnet = null; item = null; pointerId = null; armed = false;
}
