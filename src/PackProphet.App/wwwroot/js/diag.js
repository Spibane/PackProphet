// Visible error surface. There is no console on a phone, and Blazor's default
// #blazor-error-ui only says "An unhandled error has occurred" with no detail.
// This renders the actual message on the page so a failure is diagnosable from
// the device it happened on.
(function () {
    // Bumped whenever this file changes what a report contains. Printed in every report, because
    // a phone reading a cached copy produces an OLD report that looks like a current one, and two
    // rounds of this investigation were spent comparing readings taken from different versions of
    // this file without knowing it.
    const VERSION = 'diag/6';

    let box = null, n = 0;

    // Captured before anything is patched, and declared up here because they are used during
    // load -- by the capture listener below -- as well as by the wrappers installed at the end.
    // Left where the wrappers were, `const` would put these in the temporal dead zone for every
    // line above them.
    const nativeAdd = EventTarget.prototype.addEventListener;
    const nativeRemove = EventTarget.prototype.removeEventListener;
    const nativeTimeout = window.setTimeout, nativeInterval = window.setInterval;
    const nativeFrame = window.requestAnimationFrame;

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

    // Copying, on the origin this box actually gets read on.
    //
    // navigator.clipboard does not exist outside a secure context, and the case the error box is
    // FOR is a phone on plain HTTP to a LAN address. `navigator.clipboard?.writeText(...)` there
    // is optional chaining onto undefined: the button did nothing at all, silently, which is
    // worse than having no button. So: the real API where it exists, and a selection the OS can
    // copy from where it does not.
    let copyBtn = null;

    function flash(text) {
        if (!copyBtn) return;
        copyBtn.textContent = text;
        nativeTimeout.call(window, () => { if (copyBtn) copyBtn.textContent = 'copy'; }, 1600);
    }

    function copy(text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(() => flash('copied'), () => select(text));
            return;
        }
        select(text);
    }

    /// Put the report in a field and select it. execCommand('copy') is deprecated and still the
    /// only thing that works here; where even that is refused the text is left selected, so the
    /// iOS Copy menu can finish the job by hand.
    function select(text) {
        const field = document.createElement('textarea');
        field.className = 'diag-copy';
        field.value = text;
        box.appendChild(field);
        field.focus();
        // iOS ignores select() on a field it considers read-only, and ignores it entirely unless
        // a range is set explicitly.
        field.setSelectionRange(0, text.length);

        let copied = false;
        try { copied = document.execCommand('copy'); } catch { copied = false; }
        if (copied) { field.remove(); flash('copied'); return; }
        flash('select & copy');
    }

    /// Which browser, and specifically whether it is one that injects scripts of its own.
    ///
    /// Every browser on iOS is WebKit, so "it only happens on iOS" reads as a WebKit problem. The
    /// third-party ones are WebKit inside an app that injects its own code into every page --
    /// content blocking, autofill, translation, reader mode. That code is not a page subresource,
    /// so `document.scripts` and resource timing never see it, and a throw from it is cross-origin
    /// and therefore muted. Naming the browser is the difference between an unexplained error and
    /// one that is not this page's to explain.
    function browser() {
        const ua = navigator.userAgent || '';
        const wrappers = [
            ['CriOS', 'Chrome for iOS'], ['FxiOS', 'Firefox for iOS'],
            ['EdgiOS', 'Edge for iOS'], ['OPT/', 'Opera Touch'], ['OPiOS', 'Opera Mini'],
            ['Brave', 'Brave'], ['DuckDuckGo', 'DuckDuckGo'], ['YaBrowser', 'Yandex'],
        ];
        const hit = wrappers.find(([token]) => ua.includes(token));
        const which = hit
            ? hit[1] + ' -- a WebKit wrapper that injects its own scripts into every page'
            : (/iPhone|iPad|iPod/.test(ua) ? 'iOS, no third-party wrapper detected' : 'not iOS');
        return which + '\n  ' + ua.slice(0, 180);
    }

    /// Where the HTML parser had got to. For an error at +0.0s this is the whole question: the
    /// parser adds each <script> element as it reaches it, so the count says which of the page's
    /// ten scripts were in play, and readyState says whether parsing had finished at all.
    function lastScript() {
        const all = document.scripts;
        const last = all[all.length - 1];
        if (!last) return 'none';
        return last.src ? last.src.split('/').pop() : (last.type || 'inline');
    }

    /// The import map, as the browser was handed it. A map the browser REJECTS is reported by
    /// spec as an exception with no script behind it -- no filename, no Error object, fired from
    /// a queued task -- which is the exact shape of the muted report, so it has to be ruled in or
    /// out rather than assumed. The .NET SDK writes this map, and its `integrity` key is a 2024
    /// addition that older WebKit does not implement.
    function importMap() {
        const el = document.querySelector('script[type="importmap"]');
        if (!el) return 'none on the page';
        const text = el.textContent || '';
        try {
            const keys = Object.keys(JSON.parse(text));
            return text.length + ' chars, valid JSON, keys: ' + keys.join(', ');
        } catch (err) {
            return text.length + ' chars, DOES NOT PARSE: ' + (err && err.message);
        }
    }

    // Muted errors that are provably not this page's: counted rather than shown.
    //
    // A muted report means the throw was cross-origin; no wrapper having caught anything means it
    // was not a callback this app registered; and no cross-origin subresource means there is no
    // foreign code on the page to have thrown it. What is left is a script the BROWSER injected,
    // which is what KNOWN-ISSUES.md now records: every third-party iOS browser does it, Safari
    // does not, and a probe page whose only script is this file still reports it.
    //
    // Opening a red panel on a visitor's phone for another program's bug is noise, so it is kept
    // rather than displayed. Nothing is lost: a real throw from this app's own code now arrives
    // through a wrapper with a full stack, .NET exceptions still come through console.error, and
    // if anything real does fire, the count and the last report are shown alongside it.
    let mutedSuppressed = 0, lastMutedReport = null;

    function show(kind, msg, extra) {
        // Flush first, so a genuine error never appears without the context that preceded it.
        if (mutedSuppressed > 0) {
            const count = mutedSuppressed, report = lastMutedReport;
            mutedSuppressed = 0; lastMutedReport = null;
            addRow('browser', count + ' muted error' + (count === 1 ? '' : 's') +
                   ' before this, from the browser rather than from this page',
                   'The last of them:\n' + report);
        }
        addRow(kind, msg, extra);
    }

    function addRow(kind, msg, extra) {
        n++;
        if (!box) {
            box = document.createElement('div');
            box.id = 'diag';
            const bar = document.createElement('div');
            bar.className = 'diag-bar';
            bar.innerHTML = '<strong>errors</strong>';
            const btn = document.createElement('button');
            btn.textContent = 'copy';
            btn.onclick = () => copy(box.innerText);
            copyBtn = btn;
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

    // ---- Unmuting ------------------------------------------------------------------------
    //
    // Safari reports a throw from a CROSS-ORIGIN script as a bare "Script error." with no
    // message, no file, no line and no Error object, and will not be talked round. That is the
    // whole of the iOS report in KNOWN-ISSUES.md.
    //
    // The muting rule has a useful half, though. An error is muted only when the script that
    // threw is cross-origin, and every line of JavaScript this page loads is served from this
    // origin -- the classic scripts in index.html, the ES modules under js/, the vendored
    // Bootstrap bundle, the .NET runtime. Nothing from the CDN is code: it is card data and
    // card art, fetched and decoded, never executed. So a muted error should be impossible
    // here, and one arrives anyway.
    //
    // This layer closes the gap between those two facts. Every asynchronous entry point the
    // app's own JavaScript uses -- timers, animation frames, the three observers, and event
    // listeners -- is wrapped in a try/catch that reports the caught Error with its stack. Two
    // outcomes, and each is worth having:
    //
    //   a wrapper catches it   the message, the stack and the entry point, read off the phone,
    //                          with no cable and no Web Inspector.
    //   nothing catches it     the throw came from no callback this app registered, which is
    //                          the fact the muted report cannot state on its own.
    //
    // The wrappers SWALLOW what they catch. Every one of these callbacks is already
    // fire-and-forget -- an observer, a frame, a scroll handler -- so the exception was going
    // unhandled either way; containing it keeps each outcome to exactly one row instead of two.
    let caught = 0;

    // A throw inside a per-frame callback repeats every frame, so identical reports collapse
    // onto one row with a count rather than filling the screen in a second.
    const guardedRows = new Map();

    function guard(fn, where) {
        if (typeof fn !== 'function') return fn;   // setTimeout(string), or a handleEvent object
        return function (...args) {
            try {
                return fn.apply(this, args);
            } catch (err) {
                caught++;
                const at = typeof where === 'function' ? where(args) : where;
                const msg = (err && err.message) || String(err);
                const stack = err && err.stack
                    ? err.stack.split('\n').slice(0, 6).join('\n')
                    : 'no stack on the thrown value';
                const key = at + '\u0000' + msg + '\u0000' + stack;

                const seen = guardedRows.get(key);
                if (seen) {
                    seen.count++;
                    seen.row.textContent = seen.text + `\n(${seen.count} times)`;
                    return;
                }
                show('js', msg, `thrown in ${at}\n${stack}`);
                const row = box.lastElementChild;
                guardedRows.set(key, { row, text: row.textContent, count: 1 });
            }
        };
    }

    // Resource failures fire AT THE ELEMENT and do not bubble, so the listener below never saw
    // one -- and its IMG guard, which reads as though it did, had never fired once. Capture is
    // the only phase that reaches them from window. Worth having on its own terms: a blocked or
    // failed subresource is named rather than muted, so it never has to be guessed at.
    nativeAdd.call(window, 'error', e => {
        const t = e.target;
        if (!t || t === window || !t.tagName) return;   // a script threw; the next listener has it
        if (t.tagName === 'IMG') return;                // broken art is expected, not an error

        show('resource', t.tagName.toLowerCase() + ' failed to load',
             String(t.src || t.href || '(no src)').slice(0, 200) +
             (t.integrity ? '\nintegrity: ' + t.integrity : '') +
             (t.crossOrigin ? '\ncrossorigin: ' + t.crossOrigin : '') +
             '\n' + timing());
    }, true);

    window.addEventListener('error', e => {
        // Elements are the capture listener's business, above. This one is for throws.
        if (e.target && e.target !== window && e.target.tagName) return;

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
            //   caught      how many of the app's OWN callbacks have thrown this session, from
            //               the wrappers above. Zero, alongside a muted error, says the throw
            //               came from no timer, frame, observer or listener this app registered
            //               -- which is the one thing the muted event itself cannot tell us.
            //   context     whether this origin is a secure context, and so whether a service
            //               worker exists at all. A worker is a separate script origin and its
            //               unhandled errors reach the page muted, so it is a candidate only
            //               where one can be registered -- not over plain HTTP to a LAN address.
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

            const report =
                 'target: ' + targetDesc +
                 '\nError object: ' + (e.error ? (e.error.stack || e.error.message || String(e.error)).split('\n').slice(0,3).join('\n') : 'none (so the throw was cross-origin)') +
                 '\nforeign code loaded (elements, modules, fetches): ' + (foreignSummary.length ? foreignSummary.join(', ') : 'none') +
                 '\nthis app\u2019s own callbacks that have thrown: ' + caught +
                 (caught ? ' (reported above)' : ' (so not a timer, frame, observer or listener of ours)') +
                 '\nOrigin: ' + location.origin + ' | secure context: ' +
                 (window.isSecureContext ? 'yes' : 'no') + ' | service worker: ' +
                 (!navigator.serviceWorker ? 'unavailable'
                    : navigator.serviceWorker.controller ? 'controlling this page' : 'registrable, not controlling') +
                 '\nBrowser: ' + browser() +
                 '\nParser: readyState=' + document.readyState + ', ' + document.scripts.length +
                 ' script elements reached (last: ' + lastScript() + ')' +
                 '\nImport map: ' + importMap() +
                 '\nPage: ' + location.pathname + ' | ' + VERSION + ' | standalone: ' +
                 (window.matchMedia('(display-mode: standalone)').matches ? 'yes' : 'no') +
                 '\n' + timing();

            // Not this page's code -- see the note on mutedSuppressed above.
            if (caught === 0 && foreignSummary.length === 0) {
                mutedSuppressed++;
                lastMutedReport = report;
                return;
            }

            show('js', 'Script error (no detail available)', report);
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

    // Installed last, so diag's own listeners above stay outside the wrappers and a throw while
    // reporting cannot recurse into the reporter. Still ahead of every other script on the page:
    // this file is the first one in <body>, and the app's modules are imported later still by
    // .NET. theme.js runs earlier, in <head>, and registers nothing.
    window.setTimeout = function (fn, ...rest) {
        return nativeTimeout.call(window, guard(fn, 'a setTimeout callback'), ...rest);
    };
    window.setInterval = function (fn, ...rest) {
        return nativeInterval.call(window, guard(fn, 'a setInterval callback'), ...rest);
    };

    if (nativeFrame) {
        window.requestAnimationFrame = function (fn) {
            return nativeFrame.call(window, guard(fn, 'a requestAnimationFrame callback'));
        };
    }

    // gridspy, gridmetrics and imgloader are built out of these three, and between them they are
    // everything that runs when the grid first renders -- which is when the iOS error fires.
    for (const name of ['IntersectionObserver', 'MutationObserver', 'ResizeObserver']) {
        const Native = window[name];
        if (typeof Native !== 'function') continue;
        // Returns the real observer, so instanceof and every method keep working; the prototype is
        // shared rather than copied for the same reason.
        const Guarded = function (callback, ...rest) {
            return new Native(guard(callback, 'a ' + name + ' callback'), ...rest);
        };
        Guarded.prototype = Native.prototype;
        window[name] = Guarded;
    }

    // Listeners need the wrapper to be findable again: every dispose() in this app removes its
    // listener by the function it added, and that function is no longer the one the browser holds.
    // One wrapper per listener, remembered weakly, and removeEventListener translates through it.
    const listenerWrappers = new WeakMap();

    EventTarget.prototype.addEventListener = function (type, listener, options) {
        if (typeof listener !== 'function') return nativeAdd.call(this, type, listener, options);
        let wrapped = listenerWrappers.get(listener);
        if (!wrapped) {
            // Labelled from the event rather than from `type`: one function is often registered
            // for several events, and there is only ever one wrapper for it.
            wrapped = guard(listener, args => 'a "' + (args[0] && args[0].type) + '" listener');
            listenerWrappers.set(listener, wrapped);
        }
        return nativeAdd.call(this, type, wrapped, options);
    };
    EventTarget.prototype.removeEventListener = function (type, listener, options) {
        const wrapped = typeof listener === 'function' ? listenerWrappers.get(listener) : null;
        return nativeRemove.call(this, type, wrapped || listener, options);
    };

    window.diagShow = show; // so .NET can push messages here too

    /// The muted errors that were attributed to the browser and not shown. For asking on purpose,
    /// from a console or a bookmarklet, when a report is wanted without waiting for a real error.
    window.diagMuted = () => ({ count: mutedSuppressed, last: lastMutedReport });
})();
