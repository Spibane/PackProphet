// Managed image loader for the card grid.
//
// Native loading="lazy" on a 3,761-tile grid lets a fast scroll queue thousands of concurrent
// requests. Over HTTP/2 those all multiplex onto one connection, and once the server's
// max-concurrent-stream limit is exceeded it sends GOAWAY, which the browser reports as
// ERR_CONNECTION_CLOSED. Observed against jsDelivr with hundreds of art fetches failing at once.
//
// Three mitigations:
//   1. Hard cap on in-flight requests (MAX_INFLIGHT).
//   2. IntersectionObserver, so only tiles near the viewport are queued.
//   3. Tiles scrolled past are dropped from the queue before they start, so a fast flick does not
//      spend the budget on cards already passed.
//
// Art already in the browser's cache skips all three: see `ready` and showCached below. The cap
// exists to protect one HTTP/2 connection, and a cache hit never touches it.

const MAX_INFLIGHT = 6;

// How far beyond the viewport to start fetching. 300px was barely one row ahead, so art
// visibly popped in as you scrolled; ~1200px keeps two or three rows warm. Safe to raise
// because MAX_INFLIGHT still caps concurrency and anything scrolled past is dequeued before
// it ever starts — so a bigger window costs queue depth, not requests.
const PRELOAD_MARGIN = '1200px 0px';

let io = null, mo = null, host = null;
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

// Urls that have loaded successfully at least once, and so are in the browser's cache.
//
// A tile scrolling into view gets a fresh <img> with no state at all -- hidden, spinner up -- and
// then waits for a slot, even when what it wants has been sitting in memory since the first time
// you scrolled past it. Scroll back over ground you have already covered and every tile you pass
// does that again: blank, spinner, art.
//
// So a url in here bypasses the queue and the cap entirely. Nothing is risked by it: the cap is
// there to keep a fast scroll from opening hundreds of streams on one connection, and a cache hit
// opens none. (The other half of that flash was CardGrid rendering unkeyed rows, which threw away
// every visible tile's DOM on each window change. See the note on @key there.)
const ready = new Set();
let cached = 0;

function enqueue(img) {
    const want = img.dataset.src;
    if (!want) return;

    // A recycled element may carry state from a DIFFERENT card. Virtualize reuses row DOM
    // and simply rewrites data-src, so "already done" is only true if what finished loading
    // is what this element now wants — otherwise the old art stays on screen forever.
    if (img.dataset.state && img.dataset.loadedSrc === want) return;
    if (img.dataset.state === 'loading') { img.dataset.stale = '1'; return; }
    if (img.dataset.state === 'queued') return;

    // Already in the cache: show it now rather than making it wait behind five other tiles for a
    // slot it does not need.
    if (ready.has(want)) { showCached(img, want); return; }

    img.dataset.state = 'queued';
    queue.push(img);
    pump();
}

/// Point a tile at art the browser already has. Takes no in-flight slot.
function showCached(img, url) {
    img.onload = img.onerror = null;
    img.classList.remove('img-failed');
    img.dataset.state = 'loading';
    img.src = url;

    // The memory-cache case, and the one that matters: `complete` is already true in this same
    // tick, so the tile goes straight to done and is never once painted as pending. No blank
    // frame, no spinner, nothing to flash.
    if (img.complete && img.naturalWidth > 0) { cachedDone(img, url, true); return; }

    // Disk cache, or a decode still in progress. Still off the queue -- it is not going to the
    // network -- but it does get the ordinary pending look for however long it takes.
    img.onload = () => cachedDone(img, url, true);
    img.onerror = () => cachedDone(img, url, false);
}

function cachedDone(img, url, ok) {
    img.onload = img.onerror = null;

    if (ok) {
        loaded++; cached++;
        img.dataset.state = 'done';
        img.dataset.loadedSrc = url;
    } else {
        // Evicted since we last saw it, so this went to the network after all and failed there.
        // Forget the url and let the managed queue retry it under the cap, where a failure is
        // handled properly.
        ready.delete(url);
        attempted.delete(url);
    }

    // Same stale check the queued path makes: the tile may have been recycled onto a different
    // card while this was resolving.
    if (!ok || img.dataset.stale) {
        delete img.dataset.stale;
        delete img.dataset.state;
        enqueue(img);
    }
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
            if (ok) { loaded++; img.dataset.loadedSrc = requested; ready.add(requested); }
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
        const settle = ok => {
            inflight--;
            prefetched++;
            // Only a success means the cache holds it. Marking a failure ready would send tiles
            // down the fast path to fetch it again, one per tile, outside the cap.
            if (ok) ready.add(url); else attempted.delete(url);
            probe.onload = probe.onerror = null;
            pump();
        };
        probe.onload = () => settle(true);
        probe.onerror = () => settle(false);
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
    host = root;
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
    return { loaded, failed, cached, queued: queue.length, inflight, prefetched,
             prefetchPending: prefetchQueue.length, ready: ready.size };
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
    io = mo = host = null; queue = []; prefetchQueue = []; inflight = 0;
}
