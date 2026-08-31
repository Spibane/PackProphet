// Appearance, which is two independent settings rather than one.
//
// BRIGHTNESS is Bootstrap's: 5.3 reads `data-bs-theme` off the root element and every --bs-*
// variable follows. Three values rather than two -- "auto" tracks the operating system and is the
// default, so a user whose OS is set to dark gets dark without saying so again, while "light" and
// "dark" are explicit overrides that ignore the OS.
//
// SKIN is this app's, stamped as `data-pp-skin`, and app.css owns what each one means: "paper" is
// warm neutral surfaces with one accent and the default, "slate" is the greys and blues the app
// shipped with. Kept a separate attribute rather than folded into the theme value because the two
// genuinely multiply -- a skin has a light and a dark form, and choosing one must not silently
// pick the other.
//
// No palette lives in this file. It decides what the two attributes should say and nothing else.
(function () {
    const KEY = 'packprophet.state.v1';   // must match LocalStorageStateStore.Key
    const DARK = '(prefers-color-scheme: dark)';

    let pref = 'auto';
    let skin = 'paper';

    // Colours the browser chrome around the page, and matches it to the palette inside. One value
    // per combination, since a meta tag holds a single colour and cannot carry a media query; the
    // static value in index.html is the paper light one, so the first frame of a cold start is
    // already right for the default and this only has work to do for the other three.
    //
    // The manifest carries the same paper light value in its theme_color, which cannot follow a
    // preference at all -- it is read at install time -- so it tracks the DEFAULT skin, and a slate
    // user sees one warm frame before their own palette takes over. Its background_color is the
    // icon's indigo instead, so the launch screen matches the icon; index.html has the detail.
    const chrome = document.querySelector('meta[name="theme-color"]');
    const CHROME = {
        paper: { light: '#fbfaf8', dark: '#17161a' },
        slate: { light: '#4338ca', dark: '#212529' }
    };

    function resolve(p) {
        if (p === 'light' || p === 'dark') return p;
        return window.matchMedia && window.matchMedia(DARK).matches ? 'dark' : 'light';
    }

    function apply() {
        const mode = resolve(pref);
        document.documentElement.setAttribute('data-bs-theme', mode);
        document.documentElement.setAttribute('data-pp-skin', skin);

        if (chrome) chrome.setAttribute('content', CHROME[skin][mode]);
    }

    // Applied before Blazor boots, by reading the saved state directly. Waiting for the app to load
    // would show a white flash on every cold start -- and with a skin in play it would also show
    // the wrong palette, which is worse than a flash because it looks like a rendering bug. A
    // failure here is silent and leaves the defaults: the app re-applies the real preference once
    // it has loaded.
    function readSaved() {
        try {
            const prefs = JSON.parse(localStorage.getItem(KEY) || 'null')?.prefs;
            if (!prefs) return;
            if (prefs.theme === 'light' || prefs.theme === 'dark') pref = prefs.theme;
            if (prefs.skin === 'slate') skin = 'slate';
        } catch { /* defaults stand */ }
    }

    readSaved();
    apply();

    // Only meaningful under "auto", but the listener stays registered either way: the OS can
    // flip at sunset while the tab is open, and re-resolving is free.
    if (window.matchMedia) {
        try { window.matchMedia(DARK).addEventListener('change', apply); } catch { /* older browser */ }
    }

    window.ppTheme = {
        // One entry point for both, called with both values, so a change to either cannot leave
        // the attributes describing two different moments.
        set: function (p, s) {
            pref = (p === 'light' || p === 'dark') ? p : 'auto';
            skin = s === 'slate' ? 'slate' : 'paper';
            apply();
        },
        resolved: function () { return resolve(pref); }
    };
})();
