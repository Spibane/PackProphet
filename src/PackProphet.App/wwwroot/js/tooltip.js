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

    function show(target) {
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

        // Measure, then place: prefer below-right of the element, but flip or clamp rather
        // than letting it run off screen.
        const r = target.getBoundingClientRect();
        const t = el.getBoundingClientRect();
        const margin = 8;

        let left = r.left;
        if (left + t.width > window.innerWidth - margin) left = window.innerWidth - t.width - margin;
        if (left < margin) left = margin;

        let top = r.bottom + 6;
        if (top + t.height > window.innerHeight - margin) top = r.top - t.height - 6;
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

    // Long enough to be a deliberate hover rather than a consequence of crossing the grid. A
    // shorter delay is what made this feel like it fired the instant the mouse moved: in a grid
    // of tiles the pointer is ALWAYS over something, so a tooltip on a short timer is effectively
    // permanent.
    const DELAY = 650;

    // The pointer must also settle. Without this, a slow sweep across the grid still opens a
    // tooltip every time the pointer lingers on one tile for longer than the delay, which is
    // exactly what a slow sweep does.
    const STILL = 6;

    function cancel() {
        if (timer) { clearTimeout(timer); timer = null; }
        pending = null;
    }

    function schedule(target) {
        if (timer) clearTimeout(timer);
        pending = target;
        timer = setTimeout(() => { timer = null; pending = null; show(target); }, DELAY);
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
        schedule(target);
    });

    // Restarts the countdown while the pointer is still travelling, so the tooltip waits for the
    // mouse to come to rest. Passive and deliberately trivial: this fires constantly.
    document.addEventListener('pointermove', e => {
        if (!pending || e.pointerType !== 'mouse') return;

        if (Math.abs(e.clientX - lastX) > STILL || Math.abs(e.clientY - lastY) > STILL) {
            lastX = e.clientX;
            lastY = e.clientY;
            schedule(pending);
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
