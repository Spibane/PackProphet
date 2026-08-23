// Managed image loader for the card grid.
//
// WHY THIS EXISTS: native loading="lazy" on a 3,761-tile grid lets a fast scroll
// queue thousands of concurrent requests. Over HTTP/2 those all multiplex onto one
// connection, and once the server's max-concurrent-stream limit is exceeded it sends
// GOAWAY — which the browser reports as ERR_CONNECTION_CLOSED. Observed against
// jsDelivr with hundreds of art fetches failing at once. The files were all fine;
// the request pattern was the problem.
//
// Fixes it three ways:
//   1. Hard cap on in-flight requests (MAX_INFLIGHT).
//   2. IntersectionObserver, so only tiles actually near the viewport are queued.
//   3. Tiles scrolled past are dropped from the queue before they ever start, so a
//      fast flick does not spend the budget on cards you already went by.

const MAX_INFLIGHT = 6;

// How far beyond the viewport to start fetching. 300px was barely one row ahead, so art
// visibly popped in as you scrolled; ~1200px keeps two or three rows warm. Safe to raise
// because MAX_INFLIGHT still caps concurrency and anything scrolled past is dequeued before
// it ever starts — so a bigger window costs queue depth, not requests.
const PRELOAD_MARGIN = '1200px 0px';

let io = null, mo = null;
let queue = [], inflight = 0;
let loaded = 0, failed = 0;

// Warm the browser cache for cards not yet on screen, so scrolling through a set does not
// wait on the network at all. These are detached Image objects rather than DOM elements:
// Virtualize has not rendered the far rows, so there is nothing to attach a loader to.
//
// Strictly LOWER priority than visible tiles — it shares the same in-flight budget and is
// only drained once the visible queue is empty. That is what keeps this from recreating the
// connection-drop problem that made a managed loader necessary in the first place.
let prefetchQueue = [];
const attempted = new Set();      // urls fetched or in flight, so nothing is requested twice
let prefetched = 0;

function enqueue(img) {
    const want = img.dataset.src;
    if (!want) return;

    // A recycled element may carry state from a DIFFERENT card. Virtualize reuses row DOM
    // and simply rewrites data-src, so "already done" is only true if what finished loading
    // is what this element now wants — otherwise the old art stays on screen forever.
    if (img.dataset.state && img.dataset.loadedSrc === want) return;
    if (img.dataset.state === 'loading') { img.dataset.stale = '1'; return; }
    if (img.dataset.state === 'queued') return;

    img.dataset.state = 'queued';
    queue.push(img);
    pump();
}

/// data-src changed on an element we are already tracking: re-fetch for the new card.
function onSrcChanged(img) {
    const want = img.dataset.src;
    if (!want || img.dataset.loadedSrc === want) return;

    if (img.dataset.state === 'loading') {
        img.dataset.stale = '1';   // let the in-flight load finish, then redo it
        return;
    }
    delete img.dataset.state;
    img.classList.remove('img-failed');
    enqueue(img);
}

function drop(img) {
    if (img.dataset.state !== 'queued') return; // never cancel one already in flight
    delete img.dataset.state;
    const i = queue.indexOf(img);
    if (i >= 0) queue.splice(i, 1);
}

function pump() {
    pumpVisible();
    pumpPrefetch();
}

function pumpVisible() {
    while (inflight < MAX_INFLIGHT && queue.length) {
        const img = queue.shift();
        // Virtualize may have removed the row while it sat in the queue.
        if (!img.isConnected || !img.dataset.src) { delete img.dataset.state; continue; }
        inflight++;
        img.dataset.state = 'loading';
        const requested = img.dataset.src;
        const done = ok => {
            inflight--;
            img.dataset.state = ok ? 'done' : 'error';
            if (ok) { loaded++; img.dataset.loadedSrc = requested; }
            else { failed++; img.classList.add('img-failed'); }
            img.onload = img.onerror = null;

            // The card under this element changed while we were fetching, so what just
            // arrived is the wrong art. Go round again for the current data-src.
            if (img.dataset.stale) {
                delete img.dataset.stale;
                delete img.dataset.state;
                enqueue(img);
            }
            pump();
        };
        img.onload = () => done(true);
        img.onerror = () => done(false);
        attempted.add(requested);
        img.src = img.dataset.src;
    }
}

function pumpPrefetch() {
    while (inflight < MAX_INFLIGHT && queue.length === 0 && prefetchQueue.length) {
        const url = prefetchQueue.shift();
        if (!url || attempted.has(url)) continue;

        attempted.add(url);
        inflight++;

        const probe = new Image();
        probe.onload = probe.onerror = () => {
            inflight--;
            prefetched++;
            probe.onload = probe.onerror = null;
            pump();
        };
        probe.src = url;
    }
}

function scan(node, add) {
    if (node.nodeType !== 1) return;
    const imgs = node.matches?.('img[data-src]') ? [node] : node.querySelectorAll?.('img[data-src]');
    if (!imgs) return;
    for (const img of imgs) add ? io.observe(img) : (io.unobserve(img), drop(img));
}

export function init(root) {
    io = new IntersectionObserver(entries => {
        for (const e of entries) e.isIntersecting ? enqueue(e.target) : drop(e.target);
        pump();
    }, { root, rootMargin: PRELOAD_MARGIN });

    // Virtualize swaps rows in and out constantly, so watch the subtree rather than
    // asking each tile to register itself.
    mo = new MutationObserver(muts => {
        for (const m of muts) {
            if (m.type === 'attributes') { onSrcChanged(m.target); continue; }
            m.addedNodes.forEach(n => scan(n, true));
            m.removedNodes.forEach(n => scan(n, false));
        }
    });
    // attributeFilter matters as much as childList: in list layout the rows are recycled
    // rather than replaced, so a set change arrives ONLY as a data-src attribute change.
    mo.observe(root, {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: ['data-src'],
    });
    scan(root, true);
}

export function stats() {
    return { loaded, failed, queued: queue.length, inflight, prefetched,
             prefetchPending: prefetchQueue.length };
}

/// Queue every given url for background fetching, replacing any previous prefetch. Called
/// when the visible card list changes, so switching set abandons the old set's queue rather
/// than competing with it.
export function prefetch(urls) {
    prefetchQueue = (urls || []).filter(u => u && !attempted.has(u));
    pump();
}

export function cancelPrefetch() { prefetchQueue = []; }

/// Force a re-check of every tracked image. Called when the underlying card list changes
/// wholesale, as a belt-and-braces companion to the attribute observer.
export function refresh() {
    if (!host) return;
    for (const img of host.querySelectorAll('img[data-src]')) onSrcChanged(img);
    pump();
}

export function dispose() {
    io?.disconnect(); mo?.disconnect();
    io = mo = null; queue = []; prefetchQueue = []; inflight = 0;
}
