// Hover detail for attacks and abilities.
//
// Native title="" is unusable here: a ~500ms delay, no styling, and it collapses the
// multi-line effect text that is the whole point of showing it.
//
// ONE element, delegated from the document, rather than a tooltip per row. The list is
// virtualised — thousands of rows recycling through a few dozen DOM nodes — so per-element
// tooltip instances would be attached and torn down constantly. Appended to <body> so the
// row's own `overflow: hidden` cannot clip it.
(function () {
    let tip = null;
    let current = null;

    function element() {
        if (!tip) {
            tip = document.createElement('div');
            tip.id = 'tip';
            tip.setAttribute('role', 'tooltip');
            document.body.appendChild(tip);
        }
        return tip;
    }

    function show(target, px, py) {
        const text = target.getAttribute('data-tip');
        const art = target.getAttribute('data-tip-img');
        if (!text && !art) return;

        current = target;
        const el = element();

        // Built with DOM calls rather than innerHTML: the content is ours, but a card name is
        // still data and does not belong in a markup string.
        el.replaceChildren();
        if (art) {
            const img = document.createElement('img');
            img.src = art;
            img.alt = '';
            // Hidden on failure so a missing image leaves the caption rather than a broken icon.
            img.onerror = () => img.remove();
            el.appendChild(img);
        }
        if (text) {
            const body = document.createElement('div');
            body.className = 'tip-text';
            body.textContent = text;
            el.appendChild(body);
        }

        el.style.visibility = 'hidden';
        el.style.display = 'block';

        // Measure, then place: prefer below-right, but flip or clamp rather than letting it run
        // off screen.
        //
        // Anchored to the POINTER inside the target, not to the target's box.
        //
        // The box is right for a small target and wrong for a big one, and this tooltip serves
        // both: a 32px list thumbnail and a card tile that is three hundred pixels tall. Off the
        // tile's bottom edge the label landed a whole card-height below the cursor -- which is to
        // say on top of the card in the NEXT row, at its corner, describing a card the reader was
        // not pointing at. On the last row that fits on screen it went further: the only space
        // left below is the sliver of the next row, so the label sat jammed against the bottom
        // edge, or flipped above and covered the row before instead. Never over the card it names.
        //
        // Clamping the anchor to the pointer's neighbourhood covers both cases with one rule. The
        // gap is smaller than a thumbnail, so a small target still anchors to its own edges to
        // within a few pixels and behaves as it always did; a tile anchors just under the cursor,
        // which is over the card being described.
        const r = target.getBoundingClientRect();
        const t = el.getBoundingClientRect();
        const margin = 8;
        const gap = 6;

        // A pointer position is not always available -- a focus or a programmatic show -- so fall
        // back to the box, which is the old behaviour exactly.
        const x = typeof px === 'number' ? px : r.left;
        const y = typeof py === 'number' ? py : r.bottom;

        // The pointer's own offset. Enough to clear the cursor graphic, no more: this is also how
        // far the label sits from the thing it belongs to.
        const reach = 18;

        let left = Math.min(Math.max(x, r.left), r.right);
        if (left + t.width > window.innerWidth - margin) left = window.innerWidth - t.width - margin;
        if (left < margin) left = margin;

        // Below the pointer, or below the target where the target is the smaller of the two.
        let top = Math.min(r.bottom, y + reach) + gap;
        if (top + t.height > window.innerHeight - margin) {
            // No room: go above, by the same rule mirrored, so a tall tile still puts the label
            // just over the cursor rather than at the top of the card.
            top = Math.max(r.top, y - reach) - t.height - gap;
        }
        if (top < margin) top = margin;

        el.style.left = `${left}px`;
        el.style.top = `${top}px`;
        el.style.visibility = 'visible';
    }

    function hide() {
        current = null;
        if (tip) tip.style.display = 'none';
    }

    let timer = null;
    let pending = null;
    let lastX = 0;
    let lastY = 0;

    // Long enough to be a deliberate hover rather than a consequence of crossing the grid. In a
    // grid of tiles the pointer is always over something, so a tooltip on a short timer is
    // effectively permanent.
    const DELAY = 650;

    // The pointer must also settle. Without this, a slow sweep across the grid still opens a
    // tooltip every time the pointer lingers on one tile for longer than the delay, which is
    // exactly what a slow sweep does.
    const STILL = 6;

    function cancel() {
        if (timer) { clearTimeout(timer); timer = null; }
        pending = null;
    }

    // The pointer position is captured per scheduling rather than read at fire time, so the label
    // opens where the pointer came to rest -- which is the position the delay was waiting for.
    function schedule(target, x, y) {
        if (timer) clearTimeout(timer);
        pending = target;
        timer = setTimeout(() => { timer = null; pending = null; show(target, x, y); }, DELAY);
    }

    const SELECTOR = '[data-tip],[data-tip-img]';

    document.addEventListener('pointerover', e => {
        // Only a real mouse. A long press on iOS synthesises a pointerover for the element
        // under the finger, which is how the tooltip was appearing next to the detail page it
        // had just opened.
        if (e.pointerType !== 'mouse') return;

        const target = e.target.closest?.(SELECTOR);
        if (!target) { cancel(); if (current) hide(); return; }
        if (target === current) return;

        lastX = e.clientX;
        lastY = e.clientY;
        schedule(target, lastX, lastY);
    });

    // Restarts the countdown while the pointer is still travelling, so the tooltip waits for the
    // mouse to come to rest. Passive and deliberately trivial: this fires constantly.
    document.addEventListener('pointermove', e => {
        if (!pending || e.pointerType !== 'mouse') return;

        if (Math.abs(e.clientX - lastX) > STILL || Math.abs(e.clientY - lastY) > STILL) {
            lastX = e.clientX;
            lastY = e.clientY;
            schedule(pending, lastX, lastY);
        }
    }, { passive: true });

    document.addEventListener('pointerdown', e => {
        cancel();

        // Touch NEVER opens a tooltip. There is no hover to hover, so the only gestures left to
        // trigger one are a tap — which already means something on every surface that has tips —
        // and a long press, which opens card detail. A tooltip riding along with either is
        // something the user did not ask for, sitting on top of what they did.
        //
        // Nothing is lost: the roomy list layout shows attack and ability text inline, and the
        // card detail page shows all of it.
        hide();
    }, { passive: true });

    // Any scroll invalidates the position, and re-measuring on every frame is not worth it.
    window.addEventListener('scroll', () => { cancel(); hide(); }, true);
    window.addEventListener('resize', () => { cancel(); hide(); });
})();
