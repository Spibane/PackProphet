// Offline support for the published app.
//
// This gets used on a phone, beside the game, sometimes on bad hotel wifi — and the .NET
// runtime is a multi-megabyte download, so a cold start with no cache is the app's worst
// moment. Everything in the published output is precached on install, and the whole app is
// then served from that cache.

self.importScripts('./service-worker-assets.js');

self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;

// Deep links are served index.html by the Pages 404 fallback, so navigation requests must be
// answered from the cached index rather than by URL — /packs is not a file that exists.
// The last pattern is not redundant with the extension list: the vendored data snapshot
// includes an extensionless VERSION file, and the app reads it on every start. Matched by
// extension alone it would be the one uncached request keeping the app from working offline.
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/, /\.svg$/, /^data\//];
// Precaching is not free: the worker downloads every match on the first visit, so anything the
// app never references is a straight cost to a phone on hotel wifi. Measured against the live
// deploy, these came to about 663 KB of a 4.3 MB first visit — roughly a sixth of it — for files
// no page ever loads.
//
// The .map files are already absent, but only by accident: they end in .map, so no include pattern
// matches them. Worth knowing, because renaming an include pattern could quietly pull 700 KB of
// source maps into the cache.
const offlineAssetsExclude = [
    /^service-worker\.js$/,

    // Blazor.Bootstrap bundles a PDF viewer and a sortable list. Neither component is used here,
    // and the pdf.js worker alone is the single largest asset in the deploy.
    /pdfjs-/,
    /blazor\.bootstrap\.sortable-list\./,

    // Bootstrap is vendored whole, and index.html links exactly two files out of it. The rest —
    // unminified copies, right-to-left variants, ESM builds, and the grid/reboot/utilities
    // subsets — is never requested by anything.
    /^lib\/bootstrap\/(?!dist\/css\/bootstrap\.min\.css$|dist\/js\/bootstrap\.bundle\.min\.js$)/,
];

// The base path, taken from the worker's own URL: the app is hosted under /<repo>/ on GitHub
// Pages and at / in development, and hardcoding either one breaks the other.
const base = self.registration.scope;
const baseUrl = new URL(base);

async function onInstall() {
    // Skip waiting so an update takes effect on the next load rather than requiring every tab
    // to be closed — a lone bookmarked tab on a phone otherwise never updates.
    self.skipWaiting();

    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));

    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate() {
    await self.clients.claim();

    // Drop caches from older versions, or the browser's storage quota fills with every deploy
    // ever made and eviction starts taking out the current one.
    const keys = await caches.keys();
    await Promise.all(keys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    if (event.request.method !== 'GET') return fetch(event.request);

    // Card art comes from a CDN and is not in the manifest. It must never be forced through the
    // cache-first path here: the browser's own HTTP cache already handles it, and precaching
    // 3,700 images would be gigabytes.
    const url = new URL(event.request.url);
    if (url.origin !== baseUrl.origin) return fetch(event.request);

    const shouldServeIndexHtml = event.request.mode === 'navigate';
    const request = shouldServeIndexHtml ? 'index.html' : event.request;

    const cache = await caches.open(cacheName);
    const cached = await cache.match(request);
    if (cached) return cached;

    // Not precached — the vendored data snapshot is fetched at runtime, for instance. Fall
    // through to the network rather than failing.
    try {
        return await fetch(event.request);
    } catch (err) {
        // Offline and uncached. A navigation still gets the app shell, which can then say what
        // it cannot load, instead of the browser's own error page.
        if (shouldServeIndexHtml) {
            const shell = await cache.match('index.html');
            if (shell) return shell;
        }
        throw err;
    }
}
