// Pull down at the top of the page to reload it.
//
// WHY THE APP HAS TO DO THIS
// ==================================================================================
// Pull to refresh is a browser gesture, and an iOS web app added to the Home Screen is not in a
// browser: there is no address bar to pull, and the system provides no replacement. So on the one
// device this app is mostly used on, installed the way it is meant to be installed, the gesture
// every other app on the phone has simply does nothing.
//
// Only installed, deliberately. In a browser tab Safari and Chrome both already have this, and a
// second implementation on top of theirs means one pull firing two reloads -- or worse, a page
// that fights the browser's own rubber band.
//
// WHAT REFRESH MEANS HERE
// ----------------------------------------------------------------------------------
// A reload. The collection lives in this device's own storage and does not need fetching, so the
// things a refresh can actually change are the two the app cannot notice on its own: a new card
// set appearing in the community data, and a new build of the app itself. Both arrive with the
// document.
//
// TOUCH EVENTS, NOT POINTER EVENTS, AND THE REASON IS NOT STYLE
// ----------------------------------------------------------------------------------
// Every other gesture in this app is read from pointer events -- the grid sweep, the shelf drag,
// the tile's long press -- and this one cannot be. A pointer belongs to the page only until the
// browser decides the gesture is a scroll; at that moment it sends pointercancel and stops. This
// gesture IS a downward drag at the top of a scrollable page, which is the exact shape of a
// scroll, so WebKit claims it on the first move: pointerdown arrives, and pointermove never does.
// Measured on iOS 27 -- pointerdown fired, pointermove did not, once.
//
// touch-action would hand it back, and the price is the page. The property has to be set on the
// element the gesture starts on, the gesture starts anywhere, and anything strong enough to keep
// the pointer would stop that element scrolling at all.
//
// Touch events have no such rule: touchmove keeps arriving through a scroll, and is cancelable
// until the scroll is committed -- which is also how the page is kept still while the indicator is
// out. So this is the one gesture in the app read from touches.

(() => {

const PULL = 88;          // how far to pull before letting go means reload
const MAX = 120;          // how far the indicator will travel, however hard you pull
const RESIST = .55;       // pull travels slower than the finger, as a rubber band does
const SLOP = 12;          // below this it is a tap with a shake in it

let sheet = null;         // the indicator
let startY = 0, pulling = false, armed = false, distance = 0;

function installed() {
    return window.matchMedia('(display-mode: standalone)').matches
        || window.navigator.standalone === true;
}

function indicator() {
    if (sheet) return sheet;

    sheet = document.createElement('div');
    sheet.className = 'pulldown';
    sheet.setAttribute('aria-hidden', 'true');
    sheet.innerHTML = '<span class="ring"></span>';
    document.body.appendChild(sheet);
    return sheet;
}

function draw(at) {
    const el = indicator();
    el.style.setProperty('--pull', at.toFixed(1) + 'px');
    el.classList.toggle('ready', at >= PULL);
    el.classList.add('on');
}

function reset() {
    pulling = false; armed = false; distance = 0;
    if (!sheet) return;
    sheet.classList.remove('on', 'ready', 'going');
    sheet.style.setProperty('--pull', '0px');
}

// Whether this gesture belongs to something else. A sheet with its own scroller, a shelf item
// being dragged, a field being edited -- all of them own a vertical drag that starts on them, and
// none of them wants the page reloaded underneath it.
function taken(target) {
    if (!target || !target.closest) return false;
    if (target.closest('[data-drag-handle], .dragsort .dragging')) return true;
    if (target.closest('input, textarea, select')) return true;

    // A sweep across the grid is a drag that means "add a copy of each of these", and it starts on
    // a tile at whatever scroll position the reader is at -- including the top.
    if (target.closest('.grid-wrap.sweeping')) return true;

    for (let node = target; node && node !== document.body; node = node.parentElement) {
        if (node.matches && node.matches(':popover-open')) return true;
        if (node.scrollHeight > node.clientHeight + 1 && node.scrollTop > 0) return true;
    }

    return false;
}

function onStart(e) {
    reset();

    // One finger, at the top, on nothing that owns a drag of its own.
    if (e.touches.length !== 1 || window.scrollY > 0 || taken(e.target)) return;

    startY = e.touches[0].clientY;
    pulling = true;
}

function onMove(e) {
    if (!pulling) return;

    const travelled = e.touches[0].clientY - startY;

    // Upward, or the page moved under us: this is a scroll and it was never ours.
    if (travelled <= 0 || window.scrollY > 0) { reset(); return; }
    if (!armed && travelled < SLOP) return;

    armed = true;

    // The page must not rubber-band while the indicator is out, or both move at once. Only
    // possible while the browser still considers the gesture open, which is why the pull is
    // claimed within a few pixels rather than after a long look at it.
    if (e.cancelable) e.preventDefault();

    distance = Math.min(MAX, travelled * RESIST);
    draw(distance);
}

function onEnd() {
    if (armed && distance >= PULL) {
        // Held out while the document goes, so the last thing seen is the gesture completing
        // rather than the indicator snapping back and then the screen blanking.
        indicator().classList.add('going');
        pulling = false;
        window.location.reload();
        return;
    }

    reset();
}

// A plain script rather than a module, like the rest of the app's document-level behaviour: it
// binds four listeners and holds no state anything else needs to reach.

if (installed()) {
    document.addEventListener('touchstart', onStart, { passive: true });
    document.addEventListener('touchmove', onMove, { passive: false });
    document.addEventListener('touchend', onEnd, { passive: true });
    document.addEventListener('touchcancel', onEnd, { passive: true });
}

})();
