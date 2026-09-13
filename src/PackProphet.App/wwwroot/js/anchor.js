// Keep the thing you are working in where it is when something above it changes size.
//
// THE PROBLEM
// ==================================================================================
// Two screens grow a summary above the control you are using. Logging a pack fills a slot with the
// card's art the first time you tap one, and the strip goes from a row of empty boxes to a row of
// pictures; Wonder Pick replaces two paragraphs of explanation with the five boxes. Either way a
// block ABOVE the grid becomes a few hundred pixels taller, so everything under it slides down --
// and what is under it is the card you were about to tap next.
//
// The platform has an answer to this and it is not available here. CSS scroll anchoring does
// exactly this job, automatically, and Safari does not implement it -- which is the one browser
// the phone this is used on has.
//
// HOW
// ----------------------------------------------------------------------------------
// Remember where the anchor sits in the DOCUMENT, not on the screen: a top measured against the
// page survives the reader scrolling between renders, and the difference between two of them is
// exactly how much the content above grew. Take that back out of the scroll position and the
// anchor has not moved.
//
// Called from Blazor's after-render rather than from an observer, because Blazor already knows the
// precise moment the DOM changed, and getBoundingClientRect at that moment reads the new layout.
//
// The TOKEN is which screen the measurement belongs to. Both pages swap their whole contents at
// some point -- choosing a different pack, finishing an offer -- and a remembered position from
// the screen before is not a change in height, it is a different page. A new token re-marks
// instead of compensating.

const at = new Map();

// How many frames to keep trying for. See below: the scroll the compensation needs may not exist
// yet at the moment the DOM changes.
const FRAMES = 3;

// Scroll by as much of `by` as the document will take, and report what is left over.
function shift(by) {
    const before = window.scrollY;
    window.scrollBy(0, by);
    return by - (window.scrollY - before);
}

export function keep(selector, token) {
    const el = document.querySelector(selector);

    // Gone: the screen no longer has this on it, so there is nothing to hold still and nothing
    // worth remembering about where it used to be.
    if (!el) { at.delete(selector); return; }

    const top = el.getBoundingClientRect().top + window.scrollY;
    const was = at.get(selector);
    at.set(selector, { token, top });

    if (!was || was.token !== token || top === was.top) return;

    // Scrolling does not move the document, so the top just recorded is still true afterwards.
    let left = shift(top - was.top);
    if (Math.abs(left) < 1) return;

    // THE SCROLL MAY NOT EXIST YET.
    // ==============================================================================
    // What is being taken out of the scroll position has to be there to take: a page that has
    // grown at the top but not yet at the bottom -- a picker whose list is briefly empty while it
    // refilters, a grid mid-chunk -- has nowhere to put the difference, and the browser silently
    // clamps it. The reader then sees exactly the jump this exists to prevent.
    //
    // So the remainder is carried for a few frames. Each one takes what it can; when the page
    // reaches its full height the rest goes and the anchor lands where it should.
    let frames = FRAMES;
    const again = () => {
        left = shift(left);
        if (Math.abs(left) >= 1 && --frames > 0) requestAnimationFrame(again);
    };
    requestAnimationFrame(again);
}

export function forget(selector) {
    at.delete(selector);
}
