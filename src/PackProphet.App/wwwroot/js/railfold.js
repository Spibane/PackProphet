// A rail is never open as a popover above the fold width.
//
// THE TRAP
// ==================================================================================
// The rail is one element with two presentations: a column beside the content above 900px, a
// bottom sheet over it below. Which one it gets is decided by the stylesheet, and the sheet half
// is keyed to the popover being OPEN rather than to a width — `.sheet:popover-open`, which at two
// class selectors outweighs the `.page-rail` rule that makes it a column.
//
// That held only because of a coincidence: the button that opens it is `display: none` above the
// fold, so above the fold it was never open. Nothing enforced it.
//
// Which a window makes a lie of. Open the filters on a narrow window and widen it — drag the
// corner, rotate a tablet, undock a laptop from an external display — and the popover is still
// open, so the rail paints as a 1280px-wide bottom sheet covering the page, the grid has no rail
// column, and the floating button you would reach for to close it has gone with the breakpoint.
// Escape and the sheet's own Done still work, so it is recoverable rather than fatal; it is still
// the app rearranging itself into a state nobody chose.
//
// CSS cannot fix this. It has no way to close a popover, and forcing the column rules onto an open
// one only trades a sheet for a top-layer element with `position: sticky` and no scroll container
// to stick inside. The invariant is about whether it is OPEN, so it is enforced where open and
// closed are decided.
//
// WHERE THE COLUMN STARTS, AND HOW FAR DOWN IT RUNS
// ==================================================================================
// The second job here, and the same kind of invariant: something about the rail that CSS cannot
// work out on its own, enforced where it can be.
//
// The rail pins under the page's bars, so it needs to know where they end -- which is the nav's
// height plus the page header's, and the header's height is a title, an optional sentence and
// whatever controls the page put on it. Not a number CSS has. It used to read --bars-h, which the
// card grid's gridspy.js publishes, and that was right on the pages that HAVE a grid and simply
// absent on the four that do not: the shelves, the wishlist and the board fell through to the 0
// fallback, so the rail pinned at the top of the window with its first control group -- the trade
// tier, the sort -- parked behind 95px of bars you could not scroll out of the way.
//
// The height is the same measurement and the bug the user sees. A rail is as tall as its contents,
// which on a page with two groups is about 400px, so its panel and the hairline down its edge
// stopped a third of the way down the window and the page's background took over -- a column that
// looks like it failed to finish rather than one that ran out of controls. Given where the top is,
// the bottom is the window's, and the stylesheet can say so.
//
// Published as --rail-top rather than written into `top` and `block-size` here, so the geometry
// stays in the stylesheet and this file supplies only the one number it cannot have. A property of
// its own rather than gridspy's --bars-h: that one is for the grid's own strip and column headers,
// on a different element, and two scripts writing one property is the pair that agrees right up
// until it does not.
//
// Classic script rather than a module: it is one listener for the life of the document, it belongs
// to no page, and there is nothing for .NET to call.
(function () {
    // The same 900px the stylesheet folds at. Written once here and once there, which is one more
    // time than anybody wants -- but a media query is not readable from CSS custom properties, and
    // the alternative is publishing the breakpoint into the DOM to read it back out.
    const wide = window.matchMedia('(width > 900px)');

    function close() {
        if (!wide.matches) return;

        // Every rail, not the first: a page has one today, and a page that grows a second must not
        // leave it stranded because this assumed otherwise.
        for (const rail of document.querySelectorAll('.page-rail')) {
            // matches() rather than a try/catch: hidePopover() on a closed popover throws, and a
            // guard that throws on the ordinary case is a guard that fills the console.
            if (rail.matches(':popover-open')) rail.hidePopover();
        }
    }

    wide.addEventListener('change', close);

    // The bottom of the pinned bars above the rail, in viewport pixels.
    //
    // Its own sticky offset plus its height, because above 1056px the header parks under the nav
    // and below it parks at the top of the window -- so the offset is where the shell put it
    // rather than a constant this has to know. A header that is not pinned covers nothing once you
    // have scrolled, and parking the rail below where a bar used to be would leave a gap the
    // height of a bar that is no longer there.
    //
    // Measured rather than derived from the header's min-height, which is a constant that was
    // tried in gridspy.js and was wrong in both directions: border-box rounding put the real
    // height a fraction under it at some zoom levels, and a coarse pointer grew the controls on it
    // to 56px.
    function barsBottom() {
        const head = document.querySelector('main > .page-head');
        if (!head || getComputedStyle(head).position !== 'sticky') return 0;

        return (parseFloat(getComputedStyle(head).top) || 0)
            + head.getBoundingClientRect().height;
    }

    let published = null;

    function pin() {
        const rails = document.querySelectorAll('.page-rail');
        if (rails.length === 0) return;

        const top = barsBottom();

        // Unchanged is the common case, and writing the same value back would invalidate style for
        // nothing.
        if (top === published) return;
        published = top;

        for (const rail of rails) rail.style.setProperty('--rail-top', `${top}px`);
    }

    // TWO TRIGGERS, ONE MEASUREMENT
    //
    // A RESIZE is every geometric way the answer moves without the page changing: the 1056px
    // breakpoint where the header stops parking under the nav and parks at the top of the window
    // instead, and any width where the header's own sentence wraps to a second line.
    //
    // A RENDER is the other one, and the rail asks for that itself -- PageRail calls pin() after
    // it renders, which is when it and the header it measures against are both in the DOM. This
    // file cannot see that moment: it runs before Blazor has rendered anything at all, when there
    // is no <main>, no header and no rail to publish to.
    //
    // Neither is an observer, deliberately. A ResizeObserver on the header is the tighter
    // instrument and its callbacks are delivered with a frame, so a tab that is not rendering
    // never gets them; a MutationObserver has to watch the app root's whole subtree to see a page
    // swap at all, which means firing for every row the virtualised grid renders while you scroll,
    // in a callback that reads computed style and so forces layout.
    window.addEventListener('resize', pin);

    // Named the way ppScroll is, and for the same reason: one function, called from the component
    // that knows when it is worth calling, rather than a module for .NET to import and hold.
    // Whether the rail is folded away on a desktop. Stamped on the root element, like the theme, so
    // it is in place before Blazor renders anything and a cold start does not flash the column.
    // Storage failures leave it shown.
    const FOLD_KEY = 'packprophet.rail.hidden';

    function isHidden() {
        return document.documentElement.dataset.rail === 'hidden';
    }

    function setHidden(hidden) {
        if (hidden) document.documentElement.dataset.rail = 'hidden';
        else delete document.documentElement.dataset.rail;
        try { localStorage.setItem(FOLD_KEY, hidden ? '1' : '0'); } catch { /* shown next load */ }
    }

    function toggle() {
        setHidden(!isHidden());
        return isHidden();
    }

    try {
        if (localStorage.getItem(FOLD_KEY) === '1') document.documentElement.dataset.rail = 'hidden';
    } catch { /* shown */ }

    window.ppRail = { pin, isHidden, toggle };

    // And once on load, for the window that was already wide when the page arrived -- a reload
    // while the sheet was open restores neither the popover nor this listener's chance to have
    // seen it change.
    close();
    pin();
})();
