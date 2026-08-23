// Light/dark theming. Bootstrap 5.3 reads `data-bs-theme` off the root element and every
// --bs-* variable follows, so this file only has to decide what that attribute should say —
// no palette lives here, and app.css needs no dark-specific rules.
//
// Three settings, not two: "auto" tracks the operating system and is the default, because a
// user who has already told their OS they want dark should not have to tell us as well.
// "light" and "dark" are explicit overrides that ignore the OS.
(function () {
    const KEY = 'packprophet.state.v1';   // must match LocalStorageStateStore.Key
    const DARK = '(prefers-color-scheme: dark)';

    let pref = 'auto';

    function resolve(p) {
        if (p === 'light' || p === 'dark') return p;
        return window.matchMedia && window.matchMedia(DARK).matches ? 'dark' : 'light';
    }

    function apply() {
        document.documentElement.setAttribute('data-bs-theme', resolve(pref));
    }

    // Applied BEFORE Blazor boots, by reading the saved state directly. Waiting for the app
    // to load would show a white flash on every cold start, which on a phone at night is the
    // most visible defect in the whole app. A failure here is silent and simply leaves the
    // OS default: the app re-applies the real preference once it has loaded.
    function readSaved() {
        try {
            const raw = localStorage.getItem(KEY);
            if (!raw) return 'auto';
            const t = JSON.parse(raw)?.prefs?.theme;
            return t === 'light' || t === 'dark' ? t : 'auto';
        } catch { return 'auto'; }
    }

    pref = readSaved();
    apply();

    // Only meaningful under "auto", but the listener stays registered either way: the OS can
    // flip at sunset while the tab is open, and re-resolving is free.
    if (window.matchMedia) {
        try { window.matchMedia(DARK).addEventListener('change', apply); } catch { /* older browser */ }
    }

    window.ppTheme = {
        set: function (p) { pref = (p === 'light' || p === 'dark') ? p : 'auto'; apply(); },
        resolved: function () { return resolve(pref); }
    };
})();
