// Undo/redo hotkeys.
//
// A document-level listener, like the command palette's: the shortcut has to work wherever you
// are, and Blazor can only bind a handler to an element it renders. The undo stack itself lives
// in AppSession — this file only decides when to ask for it.
window.ppUndo = (() => {
    let owner = null;

    // A text field has its own undo history and the browser's is the right one there. Typing a
    // deck name and pressing Ctrl+Z must fix the typo, not roll back the collection.
    function inTextField(el) {
        if (!el) return false;
        if (el.isContentEditable) return true;
        const tag = (el.tagName || '').toUpperCase();
        return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
    }

    function onKeyDown(e) {
        if (!owner) return;
        if (!(e.ctrlKey || e.metaKey) || e.altKey) return;

        const key = (e.key || '').toLowerCase();
        if (key !== 'z' && key !== 'y') return;
        if (inTextField(e.target)) return;

        // Ctrl+Y and Ctrl+Shift+Z both redo: Windows and Linux apps use the former, macOS the
        // latter, and there is no cost to accepting both.
        const redo = key === 'y' || e.shiftKey;

        e.preventDefault();
        owner.invokeMethodAsync(redo ? 'Redo' : 'Undo');
    }

    return {
        register(dotnetRef) {
            owner = dotnetRef;
            document.addEventListener('keydown', onKeyDown, true);
        },
        unregister() {
            document.removeEventListener('keydown', onKeyDown, true);
            owner = null;
        }
    };
})();
