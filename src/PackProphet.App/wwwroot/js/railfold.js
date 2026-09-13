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

    // And once on load, for the window that was already wide when the page arrived -- a reload
    // while the sheet was open restores neither the popover nor this listener's chance to have
    // seen it change.
    close();
})();
