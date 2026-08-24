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
    }
};
