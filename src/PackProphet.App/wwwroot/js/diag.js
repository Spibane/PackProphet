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
        show('js', e.message, e.filename ? `${e.filename}:${e.lineno}` : '');
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
