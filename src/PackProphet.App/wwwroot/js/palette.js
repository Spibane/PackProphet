// Command palette hotkey, and the two calls a <dialog> needs that Blazor cannot make itself.
//
// The hotkey has to be a document-level listener rather than Blazor's @onkeydown: the shortcut
// must work no matter what has focus, and Blazor can only attach a handler to an element it
// renders. Capture phase, because Ctrl+K is claimed by the browser itself (Firefox focuses the
// address bar's search) — the default has to be prevented before it reaches the chrome.
//
// There is deliberately no focus trap here, and no focus restoration. showModal() puts the dialog
// in the top layer, makes the rest of the document inert, closes the tab ring, handles Escape and
// puts focus back where it came from when the dialog closes. All of that used to be hand-rolled,
// including a requestAnimationFrame deferral to work around focus being restored to an element
// that was still inert — a race that cannot happen now, because nothing here clears inertness.
window.ppPalette = (() => {
    let owner = null;

    function onKeyDown(e) {
        if (!owner) return;
        if ((e.ctrlKey || e.metaKey) && !e.altKey && (e.key || '').toLowerCase() === 'k') {
            e.preventDefault();
            e.stopPropagation();
            owner.invokeMethodAsync('ToggleFromHotkey');
        }
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
        // Opened from JS rather than Blazor so the dialog is shown and the caret lands in the box
        // in the same frame; a round-trip through .NET showed up as a visible delay before typing
        // did anything.
        open(dialog, input) {
            if (!dialog || dialog.open) return;
            dialog.showModal();
            if (input) { input.focus(); input.select && input.select(); }
            // A modal dialog does not dismiss on a backdrop click by itself. The backdrop is
            // painted by the dialog element, so a click out there arrives with the dialog as its
            // own target, which is what distinguishes it from a click on the palette's contents.
            // This replaces the button the backdrop used to be — the button existed so the
            // keyboard could reach it, and Escape now covers that.
            dialog.addEventListener('click', e => {
                if (e.target === dialog) dialog.close();
            }, { once: false });
        },
        close(dialog) {
            if (dialog && dialog.open) dialog.close();
        }
    };
})();
