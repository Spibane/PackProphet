// Typing counts down a list.
//
// Entering a collection that already exists is the worst moment in this app: hundreds of cards,
// each needing a number. Tapping + eleven times is not entry, it is punishment. This makes the
// count fields behave like a spreadsheet column — type, Enter, type, Enter — which is the only
// way that job is bearable.
//
// Delegated from the document rather than bound per row: the list is virtualised, so rows come
// and go constantly and per-element handlers would be attached and torn down on every scroll.
(function () {
    const SELECTOR = 'input[data-count-input]';

    function fields() {
        return Array.from(document.querySelectorAll(SELECTOR));
    }

    function move(from, delta) {
        const all = fields();
        const at = all.indexOf(from);
        if (at < 0) return false;

        const next = all[at + delta];
        // No next field means the end of what is rendered. Blurring commits the value and stops
        // the keystroke reading as "nothing happened".
        if (!next) { from.blur(); return true; }

        // Focus first, then scroll: focusing already nudges the row into view, and asking for
        // 'nearest' afterwards avoids the jump to centre that 'center' would cause on every row.
        next.focus();
        next.select();
        next.scrollIntoView({ block: 'nearest' });
        return true;
    }

    document.addEventListener('keydown', e => {
        const el = e.target;
        if (!el || !el.matches || !el.matches(SELECTOR)) return;

        if (e.key === 'Enter') {
            e.preventDefault();
            // Moving focus fires the field's change event, which is what commits the value —
            // so the count is saved by the same act that advances the cursor.
            move(el, e.shiftKey ? -1 : 1);
            return;
        }

        // The field is text, not number, so the arrows do nothing in it and are free to mean
        // "next row" — matching every spreadsheet anyone has used.
        if (e.key === 'ArrowDown') { e.preventDefault(); move(el, 1); return; }
        if (e.key === 'ArrowUp') { e.preventDefault(); move(el, -1); return; }

        if (e.key === 'Escape') { el.blur(); }
    }, true);

    // Select on focus, so typing replaces the existing count instead of appending to it. Landing
    // on a field showing 3 and typing 12 must not produce 312.
    document.addEventListener('focusin', e => {
        const el = e.target;
        if (el && el.matches && el.matches(SELECTOR)) el.select();
    });
})();
