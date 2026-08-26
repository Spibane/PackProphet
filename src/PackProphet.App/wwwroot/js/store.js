// localStorage bridge. The collection is ~50 KB and a few thousand logged packs ~300 KB,
// comfortably inside the ~5 MB budget, so IndexedDB interop buys nothing here.
export function load(key) {
    try { return localStorage.getItem(key); }
    catch { return null; }          // private mode, or site data blocked
}

export function save(key, value) {
    try { localStorage.setItem(key, value); return true; }
    catch (e) {
        // Quota exceeded is the one failure the user must hear about, since it means their
        // edits are NOT being kept.
        if (window.diagShow) window.diagShow('storage', e && e.message ? e.message : String(e));
        return false;
    }
}

export function remove(key) {
    try { localStorage.removeItem(key); } catch { /* nothing to do */ }
}

/// Hand the user a file — a state backup, or a collection as CSV. The type is passed in
/// rather than assumed, since those two are not the same. Nothing here ever leaves the device.
export function download(filename, text, mime) {
    const blob = new Blob([text], { type: mime || 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = filename;
    document.body.appendChild(a); a.click();
    document.body.removeChild(a);
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
