// Visible error surface. There is no console on a phone, and Blazor's default
// #blazor-error-ui only says "An unhandled error has occurred" with no detail.
// This renders the actual message on the page so a failure is diagnosable from
// the device it happened on.
(function () {
    let box = null, n = 0;

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
            show('js', 'Script error (no detail available)',
                 'The browser withheld the source. This is what it reports for a throw it treats\n' +
                 'as cross-origin, including some it raises itself -- an extension, or the OS share\n' +
                 'sheet. Nothing in this app is loaded from another origin, so it is very likely\n' +
                 'not ours. Page: ' + location.pathname + ' | standalone: ' +
                 (window.matchMedia('(display-mode: standalone)').matches ? 'yes' : 'no'));
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
