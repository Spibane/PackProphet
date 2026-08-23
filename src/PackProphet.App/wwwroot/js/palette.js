// Command palette hotkey.
//
// This has to be a document-level listener rather than Blazor's @onkeydown: the shortcut must
// work no matter what has focus, and Blazor can only attach a handler to an element it renders.
// Capture phase, because Ctrl+K is claimed by the browser itself (Firefox focuses the address
// bar's search) — the default has to be prevented before it reaches the chrome.
window.ppPalette = (() => {
    let owner = null;

    function onKeyDown(e) {
        if (!owner) return;
        if (!(e.ctrlKey || e.metaKey) || e.altKey) return;
        if ((e.key || '').toLowerCase() !== 'k') return;

        e.preventDefault();
        e.stopPropagation();
        owner.invokeMethodAsync('ToggleFromHotkey');
    }

    return {
        register(dotnetRef) {
            owner = dotnetRef;
            document.addEventListener('keydown', onKeyDown, true);
        },
        unregister() {
            document.removeEventListener('keydown', onKeyDown, true);
            owner = null;
        },
        // Focus is set from JS rather than Blazor's FocusAsync so the caret lands in the box in
        // the same frame the overlay appears; a round-trip through .NET showed up as a visible
        // delay before typing did anything.
        focus(el) {
            if (!el) return;
            el.focus();
            el.select && el.select();
        }
    };
})();
