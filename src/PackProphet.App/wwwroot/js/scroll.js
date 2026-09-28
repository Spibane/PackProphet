// Scroll resets.
//
// The card grid has its own scroller, and on a narrow screen the page's <main> scrolls as well —
// so returning to the top of a new list means resetting whichever of them is actually scrolled.
// Doing only the grid left the page itself parked halfway down its own bars.
//
// Where each list was left, by URL, so coming back from a card lands where you were.
//
// Recorded as you scroll rather than on the way out. A card is opened by a link, a long press or a
// key, and by the time any of those has navigated the URL has changed and the grid is going; the
// last scroll event is the one moment all three share. Keyed by the full URL, so the collection's
// set (which is in its query) picks which place is returned to, and the card page's own scrolling
// is filed under its own URL rather than over the list's.
//
// Only while a grid is on screen, and only for the thing that scrolls it -- not the rail beside it,
// which scrolls on its own.
//
// In a closure because this is a classic script, and its helpers would otherwise be globals.
(() => {
    const left = new Map();
    let restoring = false;
    let queued = 0;

    function scrollerOf(el) {
        let node = el;
        while (node && node !== document.body) {
            const style = getComputedStyle(node);
            if (/(auto|scroll|overlay)/.test(style.overflowY) && node.scrollHeight > node.clientHeight + 1)
                return node;
            node = node.parentElement;
        }
        return window;
    }

    const topOf = scroller => scroller === window ? window.scrollY : scroller.scrollTop;
    const viewTop = scroller => scroller === window ? 0 : scroller.getBoundingClientRect().top;

    function setTop(scroller, top) {
        if (scroller === window) {
            try { window.scrollTo({ top, left: 0, behavior: 'instant' }); }
            catch { window.scrollTo(0, top); }
        } else {
            scroller.scrollTop = top;
        }
    }

    // The first tile whose top is on screen. A card rather than a pixel offset, because the grid is
    // virtualised: it lays itself out from an estimated row height until it has measured one, and
    // the same offset on the two layouts is a different card -- measured on a phone, 2500px was a
    // row and a half short of the card you had opened.
    function anchorIn(grid, scroller) {
        const edge = viewTop(scroller);
        for (const tile of grid.querySelectorAll('[data-idx]')) {
            const top = tile.getBoundingClientRect().top;
            if (top >= edge) return { idx: tile.getAttribute('data-idx'), at: top - edge };
        }
        return null;
    }

    // Where a tile that is not rendered would be on screen, extrapolated from the first and last
    // tiles that are. Linear, because rows are uniform -- which is what lets the grid virtualise.
    function estimate(grid, idx) {
        const tiles = grid.querySelectorAll('[data-idx]');
        if (tiles.length < 2) return null;

        const first = tiles[0], last = tiles[tiles.length - 1];
        const i0 = Number(first.getAttribute('data-idx')), i1 = Number(last.getAttribute('data-idx'));
        if (i1 <= i0) return null;

        const y0 = first.getBoundingClientRect().top, y1 = last.getBoundingClientRect().top;
        return y0 + (idx - i0) * (y1 - y0) / (i1 - i0);
    }

    function record(grid, scroller) {
        const top = topOf(scroller);
        left.set(location.href, top > 0 ? { top, anchor: anchorIn(grid, scroller) } : null);
    }

    document.addEventListener('scroll', e => {
        if (restoring || queued) return;
        queued = requestAnimationFrame(() => {
            queued = 0;
            const grid = document.querySelector('.grid-wrap[role="grid"]');
            if (!grid) return;

            const scroller = scrollerOf(grid);
            const own = scroller === window
                ? e.target === document || e.target === document.documentElement
                : e.target === scroller;
            if (own) record(grid, scroller);
        });
    }, { capture: true, passive: true });

    // And again at the tap or key that opens a card. A frame after the last scroll event the
    // virtualised grid can still be showing the rows it had before it caught up, so the anchor read
    // then may be a card that has already left; by the time anything in the grid is pressed, what
    // is on screen is what the reader is looking at.
    for (const type of ['pointerdown', 'keydown']) {
        document.addEventListener(type, e => {
            const grid = e.target instanceof Element && e.target.closest('.grid-wrap[role="grid"]');
            if (grid && !restoring) record(grid, scrollerOf(grid));
        }, { capture: true, passive: true });
    }

    window.ppScroll = {
        // Back to where this URL's list was left, if it was.
        //
        // The saved offset first, which is near enough to have the grid render the rows around
        // the anchor, then the anchor itself once it exists, put back where it was on screen.
        // Repeated for a few frames because the grid measures its rows after rendering and moves
        // under the first attempt; abandoned the moment the reader scrolls for themselves.
        recall(grid) {
            if (!grid) return;

            // Nowhere to return to: the top, which is where a list opened fresh always went.
            const saved = left.get(location.href);
            if (!saved) { window.ppScroll.toTop(grid); return; }

            const events = ['wheel', 'touchstart', 'keydown', 'pointerdown'];
            const until = performance.now() + 1500;
            const cancel = () => {
                restoring = false;
                for (const type of events) window.removeEventListener(type, cancel, true);
            };
            for (const type of events) window.addEventListener(type, cancel, { capture: true, passive: true });

            restoring = true;
            let settled = 0;
            const step = () => {
                if (!restoring) return;
                const scroller = scrollerOf(grid);
                const tile = saved.anchor && grid.querySelector(`[data-idx="${saved.anchor.idx}"]`);

                let want = saved.top;
                if (tile) {
                    const off = tile.getBoundingClientRect().top - viewTop(scroller);
                    want = topOf(scroller) + off - saved.anchor.at;
                } else if (saved.anchor) {
                    // Not rendered: the window is somewhere else. Aim at where the tiles that are
                    // rendered say it must be, and the next frames render it.
                    const at = estimate(grid, Number(saved.anchor.idx));
                    if (at !== null) want = topOf(scroller) + at - viewTop(scroller) - saved.anchor.at;
                }

                if (Math.abs(topOf(scroller) - want) > 1) { setTop(scroller, want); settled = 0; }
                else if (tile) settled++;

                // Done once the anchor has held still for a few frames, or out of time.
                if (settled < 4 && performance.now() < until) requestAnimationFrame(step);
                else cancel();
            };
            step();
        },

        toTop(el) {
            if (el && typeof el.scrollTop === 'number') el.scrollTop = 0;

            // Walk up for any other scrolled ancestor. Cheap, and it survives layout changes without
            // this file needing to know which element is the scroller today.
            let node = el && el.parentElement;
            while (node) {
                if (node.scrollTop > 0) node.scrollTop = 0;
                node = node.parentElement;
            }

            if (window.scrollY > 0) window.scrollTo(0, 0);
        },

        // The top of a page that has just replaced another one.
        //
        // Blazor routes in place and nothing resets the scroll, so the window keeps whatever offset
        // the previous page was at. Going from a long page to a short one -- a scrolled card grid to
        // one card -- the browser then clamps that offset to the new document's maximum, and the
        // detail opens at its own bottom. Measured at 375px: the grid at 1400 of 6315, the card page
        // 1475 tall, and it opened at 663, which is exactly 1475 minus the 812 viewport.
        //
        // Only bites where the WINDOW is the scroller, which above the desk breakpoint it is not --
        // the grid scrolls itself there and the window never moved. So this is a phone bug, and the
        // fix is cheap enough not to need gating on width.
        //
        // 'instant' rather than the default: Bootstrap's reboot sets scroll-behavior: smooth on
        // :root under prefers-reduced-motion: no-preference, which turns this into an animation --
        // and an animation racing a document that is shrinking under it lands wherever the clamp
        // leaves it, which is the bug rather than the fix.
        toPageTop() {
            try { window.scrollTo({ top: 0, left: 0, behavior: 'instant' }); }
            catch { window.scrollTo(0, 0); }   // older engines reject an unknown behavior
        }
    };
})();
