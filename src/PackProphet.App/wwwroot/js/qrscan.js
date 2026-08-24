// Reads a deck share code out of an image the user picked or pasted.
//
// This is the only place in the app that needs raw pixel access, and the reason the project
// vendors one JavaScript library: the alternative was ZXing.Net plus SkiaSharp, several megabytes
// of extra WASM paid for by every visitor.
//
// Nothing leaves the device. The image is drawn to an off-screen canvas and the pixels are read
// locally, so the module works offline.
export async function decode(dataUrl) {
    const jsQR = await load();
    if (!jsQR) return { ok: false, error: 'The QR reader could not be loaded.' };

    let bitmap;
    try {
        bitmap = await imageFrom(dataUrl);
    } catch {
        return { ok: false, error: 'That file could not be read as an image.' };
    }

    // Tried at a few sizes. An in-game screenshot is usually far larger than the code itself
    // and jsQR's locator does better on a downscaled copy, but downscaling can also destroy a
    // code that was already small — so try the original first, then progressively smaller.
    for (const scale of [1, 0.6, 0.35, 1.6]) {
        const found = scan(bitmap, scale);
        if (found) return { ok: true, code: found };
    }
    return { ok: false, error: 'No code found in that image. Crop closer to the code and retry.' };
}

function scan(bitmap, scale) {
    const w = Math.max(1, Math.round(bitmap.width * scale));
    const h = Math.max(1, Math.round(bitmap.height * scale));

    // A very large canvas can fail to allocate on a phone; treat that as "this scale did not
    // work" rather than as a fatal error, since another scale may well succeed.
    let data;
    try {
        const canvas = document.createElement('canvas');
        canvas.width = w;
        canvas.height = h;
        const ctx = canvas.getContext('2d', { willReadFrequently: true });
        ctx.drawImage(bitmap, 0, 0, w, h);
        data = ctx.getImageData(0, 0, w, h);
    } catch {
        return null;
    }

    // "attemptBoth" also tries the inverted image. The in-game code is light-on-dark, which
    // the default (dark-on-light only) mode misses entirely.
    const result = window.jsQR(data.data, w, h, { inversionAttempts: 'attemptBoth' });
    return result && result.data ? result.data : null;
}

function imageFrom(src) {
    return new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => resolve(img);
        img.onerror = reject;
        img.src = src;
    });
}

let loading = null;

// Injected as a plain script because the published bundle is UMD and assigns window.jsQR;
// there is no ES module build. Memoised so a second attempt after a failed scan does not
// re-download a quarter of a megabyte.
function load() {
    if (window.jsQR) return Promise.resolve(window.jsQR);
    loading ??= new Promise(resolve => {
        const tag = document.createElement('script');
        tag.src = 'js/vendor-jsQR.js';
        tag.onload = () => resolve(window.jsQR ?? null);
        tag.onerror = () => { loading = null; resolve(null); };
        document.head.appendChild(tag);
    });
    return loading;
}
