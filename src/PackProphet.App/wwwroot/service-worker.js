// Development no-op.
//
// A caching service worker in dev would serve yesterday's _framework files after a rebuild, which
// looks like a broken build. The published build swaps in service-worker.published.js instead —
// see the csproj.
self.addEventListener('fetch', () => { });
