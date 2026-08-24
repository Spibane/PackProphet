// Command palette hotkey.
//
// This has to be a document-level listener rather than Blazor's @onkeydown: the shortcut must
// work no matter what has focus, and Blazor can only attach a handler to an element it renders.
// Capture phase, because Ctrl+K is claimed by the browser itself (Firefox focuses the address
// bar's search) — the default has to be prevented before it reaches the chrome.
window.ppPalette = (() => {
    let owner = null;
    let previouslyFocused = null;

    // Every element a user should be able to reach with Tab while the palette is open, in DOM
    // order. The backdrop button comes first in the markup, so trapping through it rather than
    // skipping it keeps the ring closed without needing a wrapper element to query against.
    function focusables() {
        return Array.from(document.querySelectorAll('.palette-back, .palette-input, .palette-item'));
    }

    function onKeyDown(e) {
        if (!owner) return;
        if ((e.ctrlKey || e.metaKey) && !e.altKey && (e.key || '').toLowerCase() === 'k') {
            e.preventDefault();
            e.stopPropagation();
            owner.invokeMethodAsync('ToggleFromHotkey');
            return;
        }

        if (e.key !== 'Tab') return;

        // An empty list means the palette is closed — nothing here to trap into, and letting Tab
        // through as normal keeps every other page working exactly as before.
        const items = focusables();
        if (items.length === 0) return;

        const i = items.indexOf(document.activeElement);
        if (i === -1) return;

        e.preventDefault();
        const next = e.shiftKey ? (i - 1 + items.length) % items.length : (i + 1) % items.length;
        items[next].focus();
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
        // delay before typing did anything. Capturing the outgoing focus here, right before it
        // moves, is what lets restoreFocus() put it back on close.
        focus(el) {
            previouslyFocused = document.activeElement;
            if (!el) return;
            el.focus();
            el.select && el.select();
        },
        // Without this, closing the palette leaves focus on whatever the mouse or Tab trap last
        // landed on inside it — usually gone from the DOM a moment later — dropping a keyboard
        // user back at the top of the document instead of where they opened the palette from.
        //
        // Deferred a frame: this fires from the same event that flips the layout's `inert` back
        // off, and that attribute is cleared by a SEPARATE component's render, queued rather than
        // applied synchronously. Calling focus() before that render reaches the DOM targets an
        // element still marked inert, which silently refuses it and leaves focus on <body>.
        restoreFocus() {
            const el = previouslyFocused;
            previouslyFocused = null;
            requestAnimationFrame(() => {
                if (el && document.contains(el) && typeof el.focus === 'function') el.focus();
            });
        }
    };
})();
