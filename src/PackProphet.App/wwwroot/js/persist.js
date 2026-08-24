// Flush pending state to storage when the page is going away.
//
// Saves are debounced by 400ms, because tap-to-increment fires faster than the whole state can be
// serialised. That debounce has a cost nobody sees until it bites: tap a card and close the tab
// inside those 400ms and the edit is gone. It is not a rare shape of use either — this app is
// opened beside the game, five cards are tapped, and it is dismissed immediately.
//
// AppSession.DisposeAsync cannot cover it. In WebAssembly nothing disposes the DI container when a
// tab closes, so that path only ever runs in tests.
//
// visibilitychange is the reliable hook and pagehide is the backstop, per the page-lifecycle rules
// browsers actually implement: unload is not fired on mobile at all, and beforeunload is
// unreliable there too. Neither guarantees an async continuation completes, so this shortens the
// window rather than closing it — the save is started at the last moment the page is still able to
// start one.
window.ppPersist = (() => {
    let owner = null;

    function flush() {
        if (!owner) return;
        // Errors are swallowed on purpose: the page is on its way out, and an exception here would
        // surface as an unhandled rejection in the console of a page nobody is looking at.
        try { owner.invokeMethodAsync('FlushNow'); } catch { /* already torn down */ }
    }

    function onVisibility() {
        if (document.visibilityState === 'hidden') flush();
    }

    return {
        register(dotnetRef) {
            owner = dotnetRef;
            document.addEventListener('visibilitychange', onVisibility);
            window.addEventListener('pagehide', flush);
        },
        unregister() {
            document.removeEventListener('visibilitychange', onVisibility);
            window.removeEventListener('pagehide', flush);
            owner = null;
        }
    };
})();
