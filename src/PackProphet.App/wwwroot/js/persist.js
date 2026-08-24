// Flush pending state to storage when the page is going away.
//
// Saves are debounced by 400ms, because tap-to-increment fires faster than the whole state can be
// serialised. Tapping a card and closing the tab inside those 400ms loses the edit, and that is an
// ordinary way to use this app: opened beside the game, five cards tapped, dismissed.
//
// AppSession.DisposeAsync cannot cover it. In WebAssembly nothing disposes the DI container when a
// tab closes, so that path only runs in tests.
//
// visibilitychange is the reliable hook and pagehide is the backstop, per the page-lifecycle rules
// browsers implement: unload is not fired on mobile at all, and beforeunload is unreliable there
// too. Neither guarantees an async continuation completes, so this shortens the window rather than
// closing it.
window.ppPersist = (() => {
    let owner = null;

    function flush() {
        if (!owner) return;
        // Errors are swallowed: the page is on its way out, and an exception here would surface
        // as an unhandled rejection in the console of a page nobody is looking at.
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
