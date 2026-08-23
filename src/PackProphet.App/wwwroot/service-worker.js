// Development no-op, deliberately.
//
// A caching service worker in dev is actively harmful: it would serve yesterday's
// _framework files after a rebuild, which looks exactly like a broken build. The published
// build swaps in service-worker.published.js instead — see the csproj.
self.addEventListener('fetch', () => { });
