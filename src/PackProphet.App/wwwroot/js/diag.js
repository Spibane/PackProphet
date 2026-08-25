// Visible error surface. There is no console on a phone, and Blazor's default
// #blazor-error-ui only says "An unhandled error has occurred" with no detail.
// This renders the actual message on the page so a failure is diagnosable from
// the device it happened on.
(function () {
    let box = null, n = 0;

    // When the page was last put in the background, and last brought back. iOS backgrounds the
    // page while the share sheet is open, so "was hidden a moment ago" is the difference between
    // an error this app caused and one the OS raised over the top of it. Without this the two are
    // indistinguishable, because the browser withholds the source for both.
    const startedAt = Date.now();
    let lastHiddenAt = 0, lastShownAt = 0;
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) lastHiddenAt = Date.now();
        else lastShownAt = Date.now();
    });

    function timing() {
        const now = Date.now();
        const parts = ['at +' + ((now - startedAt) / 1000).toFixed(1) + 's after load'];
        if (lastHiddenAt) {
            parts.push('page was backgrounded ' + ((now - lastHiddenAt) / 1000).toFixed(1) + 's ago');
            if (lastShownAt > lastHiddenAt)
                parts.push('and came back ' + ((now - lastShownAt) / 1000).toFixed(1) + 's ago');
        } else {
            parts.push('page has never been backgrounded this session');
        }
        return parts.join(', ');
    }

    function show(kind, msg, extra) {
        n++;
        if (!box) {
            box = document.createElement('div');
            box.id = 'diag';
            const bar = document.createElement('div');
            bar.className = 'diag-bar';
            bar.innerHTML = '<strong>errors</strong>';
            const btn = document.createElement('button');
            btn.textContent = 'copy';
            btn.onclick = () => navigator.clipboard?.writeText(box.innerText);
            const x = document.createElement('button');
            x.textContent = 'hide';
            x.onclick = () => box.remove();
            bar.append(btn, x);
            box.append(bar);
            document.body.appendChild(box);
        }
        const row = document.createElement('div');
        row.className = 'diag-row';
        row.textContent = `[${n}] ${kind}: ${msg}` + (extra ? `\n${extra}` : '');
        box.appendChild(row);
    }

    window.addEventListener('error', e => {
        if (e.target && e.target.tagName === 'IMG') return; // broken art is expected, not an error

        // A bare "Script error" with no file and no line is what a browser reports when it will
        // not say more: the throw came from a script it considers cross-origin, or from outside
        // the page entirely. Reported as-is it is unactionable -- it says only that something,
        // somewhere, went wrong. So say what is knowable and name the reason the rest is missing,
        // rather than printing a message with no content in it.
        const opaque = !e.filename && (!e.message || /^script error/i.test(e.message));
        if (opaque) {
            // The browser hides the source, but not everything about the event. Three things
            // separate the possibilities, and none of them needs the source:
            //
            //   target      window means a script threw. An element means a RESOURCE failed to
            //               load -- and then the element says which one.
            //   error       an Error object is present for same-origin throws and absent for
            //               cross-origin ones. Absent plus target=window is a genuine foreign
            //               script, which this app does not have unless something injected one.
            //   foreign     any script tag on the page whose src is not from here. An extension
            //               injecting one is the remaining way this app sees an error it did not
            //               cause.
            const t = e.target;
            const targetDesc = !t || t === window ? 'window (a script threw)'
                : (t.tagName || '?') + (t.src || t.href ? ' src=' + String(t.src || t.href).slice(0, 120)
                                                        : ' (no src)') + ' -- a resource failed to load';
            // document.scripts lists <script> ELEMENTS only, and this app loads almost all of its
            // JavaScript as dynamically imported ES modules, which create no element. Resource
            // timing sees those, so ask it instead -- and include fetches, since a cross-origin
            // fetch is the other way code from elsewhere reaches this page.
            const sameOrigin = u => { try { return new URL(u, location.href).origin === location.origin; } catch { return false; } };
            const foreign = [...document.scripts].map(x => x.src).filter(Boolean).filter(u => !sameOrigin(u));
            const foreignLoads = performance.getEntriesByType('resource')
                .filter(r => ['script', 'fetch', 'xmlhttprequest', 'other'].includes(r.initiatorType))
                .filter(r => !sameOrigin(r.name))
                .map(r => r.initiatorType + ' ' + new URL(r.name).origin);
            const foreignSummary = [...new Set(foreign.concat(foreignLoads))];

            show('js', 'Script error (no detail available)',
                 'target: ' + targetDesc +
                 '\nError object: ' + (e.error ? (e.error.stack || e.error.message || String(e.error)).split('\n').slice(0,3).join('\n') : 'none (so the throw was cross-origin)') +
                 '\nforeign code loaded (elements, modules, fetches): ' + (foreignSummary.length ? foreignSummary.join(', ') : 'none') +
                 '\nPage: ' + location.pathname + ' | standalone: ' +
                 (window.matchMedia('(display-mode: standalone)').matches ? 'yes' : 'no') +
                 '\n' + timing());
            return;
        }

        show('js', e.message,
             (e.filename ? `${e.filename}:${e.lineno}:${e.colno}` : '') +
             (e.error && e.error.stack ? `\n${e.error.stack.split('\n').slice(0, 4).join('\n')}` : ''));
    });
    window.addEventListener('unhandledrejection', e => {
        const r = e.reason;
        show('promise', (r && (r.message || r)) + '', r && r.stack ? r.stack.split('\n').slice(0, 4).join('\n') : '');
    });

    // Blazor reports unhandled .NET exceptions through console.error and shows only a generic
    // "An unhandled error has occurred" in its own banner, discarding the detail. Mirroring
    // console.error here is the only way to see the real exception on a device with no devtools.
    const originalError = console.error;
    console.error = function (...args) {
        try {
            const text = args.map(a => {
                if (a instanceof Error) return (a.stack || a.message || String(a));
                if (typeof a === 'object') { try { return JSON.stringify(a); } catch { return String(a); } }
                return String(a);
            }).join(' ');
            show('dotnet', text.slice(0, 4000));
        } catch { /* never let logging break the page */ }
        originalError.apply(console, args);
    };

    window.diagShow = show; // so .NET can push messages here too
})();
