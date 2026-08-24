// Keyboard operation of the card grid.
//
// Entering a collection is the app's primary interaction, and before this file it could only be
// done with a pointer: a tile was a div with a click handler, no tabindex and no key handling.
//
// This lives in JS rather than a Blazor @onkeydown on the container because only here can
// preventDefault be applied selectively. The container is the scroller, so arrow keys must be
// stopped from scrolling it as well as moving the cursor — but the list layout has a count field in
// every row, and countentry.js listens for keys on the document, so blanket-preventing or
// blanket-stopping would break typing counts down a column. The rule is: if the key is one we act
// on, and focus is not in a field, take it.
//
// Focus stays on the container throughout, with aria-activedescendant naming the current tile. For
// a virtualised grid a roving tabindex would need Blazor to re-render on every keypress to move the
// tab stop, and would leave a tab stop pointing at an element Virtualize had since destroyed. One
// container, one tab stop, no renders to move the cursor.
export function attach(host, owner, columns) {
    if (!host) return;

    const state = { host, owner, columns: Math.max(1, columns | 0), idx: -1 };
    host.__ppKeys = state;

    host.setAttribute('tabindex', '0');
    host.addEventListener('keydown', onKeyDown);
    host.addEventListener('focus', onFocus);
    host.addEventListener('blur', onBlur);
    // A pointer tap should move the cursor too, or the two interactions disagree about where you
    // are and the first arrow press jumps somewhere unrelated.
    host.addEventListener('pointerdown', onPointerDown, true);
}

/// Whether to show the keyboard affordances: the key legend, and the ring on the cursor.
///
/// Tracked as an explicit mode rather than inferred from focus. Clicking a tile focuses the
/// container, because the container holds the tab stop, so :focus-within lit the legend up on every
/// mouse click. :focus-visible does not work either — focus moved programmatically after a mouse
/// click on the toolbar button does not match it, so the deliberate route to the keyboard would
/// have shown nothing.
///
/// The rule is intent rather than focus: a key was pressed, or the keyboard was asked for.
function keyMode(state, on) {
    state.host.classList.toggle('keys', !!on);
}

export function setColumns(host, columns) {
    const state = host && host.__ppKeys;
    if (state) state.columns = Math.max(1, columns | 0);
}

export function dispose(host) {
    if (!host || !host.__ppKeys) return;
    host.removeEventListener('keydown', onKeyDown);
    host.removeEventListener('focus', onFocus);
    host.removeEventListener('blur', onBlur);
    host.removeEventListener('pointerdown', onPointerDown, true);
    delete host.__ppKeys;
}

function stateOf(e) {
    const host = e.currentTarget;
    return host && host.__ppKeys;
}

/// A field, a link or a button: their own keys win. This is what lets the count column keep
/// behaving like a spreadsheet while the grid around it answers to arrows.
function inControl(el) {
    // A keydown target is normally an element, but guard anyway: throwing here would swallow the
    // key and leave the grid feeling dead rather than reporting anything.
    return !!el && typeof el.closest === 'function'
        && !!el.closest('input, textarea, select, button, a[href]');
}

/// Two frames, not one. Virtualize renders in response to the scroll event, so a single frame can
/// land before the new window exists — which showed up as Home doing nothing from far down a list.
function afterRender(fn) {
    requestAnimationFrame(() => requestAnimationFrame(fn));
}

/// Scrolls to one end and puts the cursor there. Home and End name an index that is almost never
/// rendered, so they cannot go through move(), which walks a step at a time by design.
function jump(state, toEnd) {
    state.host.scrollTop = toEnd ? state.host.scrollHeight : 0;

    afterRender(() => {
        const all = tiles(state.host);
        if (all.length === 0) return;

        const indexes = all.map(x => Number(x.getAttribute('data-idx')));
        mark(state, toEnd ? Math.max(...indexes) : Math.min(...indexes), true);
    });
}

function tiles(host) {
    return Array.from(host.querySelectorAll('[data-idx]'));
}

function tileAt(host, idx) {
    return host.querySelector(`[data-idx="${idx}"]`);
}

function onPointerDown(e) {
    const state = stateOf(e);
    if (!state) return;

    // Out of keyboard mode: this is a pointer interaction, so nothing keyboard-shaped should
    // appear. The cursor still MOVES — silently — so that arrows continue from where you clicked.
    keyMode(state, false);

    const tile = e.target && e.target.closest ? e.target.closest('[data-idx]') : null;
    if (tile) mark(state, Number(tile.getAttribute('data-idx')));
}

function onBlur(e) {
    const state = stateOf(e);
    // Leaving the grid ends the mode, or the legend sits there after focus has gone elsewhere.
    if (state) keyMode(state, false);
}

function onFocus(e) {
    const state = stateOf(e);
    if (!state) return;

    // Land somewhere sensible rather than nowhere: the first tile on screen, so tabbing into the
    // grid does not silently start at an item scrolled far above.
    if (state.idx < 0) {
        const first = tiles(state.host)[0];
        if (first) mark(state, Number(first.getAttribute('data-idx')));
    }
}

/// Moves the cursor, updating the container's aria-activedescendant and the visible ring.
function mark(state, idx, scroll) {
    const tile = tileAt(state.host, idx);
    if (!tile) return false;

    const previous = state.idx >= 0 ? tileAt(state.host, state.idx) : null;
    if (previous) previous.classList.remove('kb');

    state.idx = idx;
    tile.classList.add('kb');

    if (!tile.id) tile.id = `${state.host.id || 'cg'}-tile-${idx}`;
    state.host.setAttribute('aria-activedescendant', tile.id);

    if (scroll) tile.scrollIntoView({ block: 'nearest', inline: 'nearest' });

    announce(state, tile);
    return true;
}

/// Reads the cursor's card out to a screen reader. The tile already carries the whole sentence as
/// its label, so this is a copy rather than a second description that could disagree with it.
function announce(state, tile) {
    const live = state.host.querySelector('[data-grid-live]');
    if (live) live.textContent = tile.getAttribute('aria-label') || '';
}

function move(state, delta) {
    const all = tiles(state.host);
    if (all.length === 0) return;

    const indexes = all.map(t => Number(t.getAttribute('data-idx')));
    const target = state.idx + delta;

    if (mark(state, target, true)) return;

    // Off the rendered window. Scroll toward it and try again next frame: Virtualize renders an
    // overscan either side, so one step at a time always lands, which is why movement is by steps
    // rather than by jumping to an arbitrary index.
    const edge = delta < 0 ? Math.min(...indexes) : Math.max(...indexes);
    const at = tileAt(state.host, edge);
    if (at) at.scrollIntoView({ block: 'nearest' });

    afterRender(() => {
        if (!mark(state, target, true)) mark(state, edge, true);
    });
}

function onKeyDown(e) {
    const state = stateOf(e);
    if (!state || inControl(e.target)) return;

    const cols = state.columns;
    let handled = true;

    switch (e.key) {
        case 'ArrowRight': move(state, 1); break;
        case 'ArrowLeft': move(state, -1); break;
        case 'ArrowDown': move(state, cols); break;
        case 'ArrowUp': move(state, -cols); break;
        case 'PageDown': move(state, cols * 5); break;
        case 'PageUp': move(state, -cols * 5); break;
        case 'Home': jump(state, false); break;
        case 'End': jump(state, true); break;

        case 'Enter':
        case ' ':
        case '+':
        case '=':
            adjust(state, 1);
            break;

        case '-':
        case 'Backspace':
        case 'Delete':
            adjust(state, -1);
            break;

        case 'i':
        case 'I':
            if (state.idx >= 0) state.owner.invokeMethodAsync('KeyOpen', state.idx);
            break;

        default:
            // A digit sets the count outright: 0-9 in place of nine presses of +.
            if (e.key >= '0' && e.key <= '9' && state.idx >= 0) {
                state.owner.invokeMethodAsync('KeySet', state.idx, Number(e.key));
            } else {
                handled = false;
            }
    }

    if (!handled) return;

    // A key was pressed and acted on, so the affordances are wanted now.
    keyMode(state, true);

    // Only now, and only for keys we acted on. Arrows would otherwise scroll the container as well
    // as moving the cursor, and space would scroll the page.
    e.preventDefault();
    e.stopPropagation();
}

function adjust(state, delta) {
    if (state.idx < 0) return;
    state.owner.invokeMethodAsync('KeyAdjust', state.idx, delta);

    // The label changes with the count, so re-read it once Blazor has re-rendered the tile.
    afterRender(() => {
        const tile = tileAt(state.host, state.idx);
        if (tile) announce(state, tile);
    });
}

/// Hands the container focus and places the cursor, for the skip button and the command palette.
/// Both exist because tabbing here means passing dozens of controls first.
export function focusGrid(host, idx, total) {
    if (!host) return;
    host.focus();

    const state = host.__ppKeys;
    if (!state) return;

    // Asked for explicitly — the toolbar button or the palette — so show the keys even though the
    // request may have arrived by mouse. This is the case :focus-visible gets wrong.
    keyMode(state, true);

    if (typeof idx === 'number' && idx >= 0) {
        restore(state, idx, total);
        return;
    }

    const first = tiles(host)[0];
    if (state.idx < 0 && first) mark(state, Number(first.getAttribute('data-idx')), true);
    else if (state.idx >= 0) mark(state, state.idx, true);
}

/// Puts the cursor on an arbitrary index, which move() cannot do.
///
/// Returning from a card's detail page lands on a freshly mounted grid: scrolled to the top, with
/// only the first screenful rendered, so the card you were reading about usually does not exist in
/// the DOM yet. Stepping there would take hundreds of frames, so the position is estimated from the
/// scroller — Virtualize sizes it to the whole list — and the cursor is placed once the window has
/// caught up.
function restore(state, idx, total) {
    if (mark(state, idx, true)) return;

    if (total > 0) {
        const fraction = idx / total;
        state.host.scrollTop = Math.max(
            0, (fraction * state.host.scrollHeight) - (state.host.clientHeight / 2));
    }

    afterRender(() => {
        if (mark(state, idx, true)) return;

        // Landed near it but not on it: settle for the nearest rendered tile rather than leaving
        // the cursor nowhere.
        const first = tiles(state.host)[0];
        if (first) mark(state, Number(first.getAttribute('data-idx')), true);
    });
}
