// Scroll resets.
//
// The card grid has its own scroller, and on a narrow screen the page's <main> scrolls as well —
// so returning to the top of a new list means resetting whichever of them is actually scrolled.
// Doing only the grid left the page itself parked halfway down its own bars.
window.ppScroll = {
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
