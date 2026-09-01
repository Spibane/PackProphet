// Everything the pairing code turns into, and the encryption it does.
//
// Kept in JavaScript rather than C# because WebCrypto is the only key derivation in the browser
// that is both constant-time and fast enough to run 300,000 PBKDF2 iterations without freezing the
// page -- the same reason jsQR and the image loader live here. The derived key never crosses into
// .NET: the module holds it, and C# asks this file to encrypt or decrypt.
//
// The code is the whole credential. From it come three independent values, and which of them
// leaves the device matters:
//
//   id    identifies the document.        SENT. The server files the blob under it.
//   auth  proves you know the code.       SENT. Stops anyone who guesses an id from overwriting it.
//   key   decrypts the blob.              NEVER SENT. Not derivable from the other two.
//
// They are three separate HKDF expansions of one master, so handing the server the first two tells
// it nothing whatsoever about the third. The host stores bytes it cannot read.

const SALT = 'packprophet.sync.v1';

// 300k iterations of PBKDF2-SHA256. The cost is paid twice in the life of a pairing -- once on the
// device that made the code, once on the device that typed it -- so a delay that would be
// intolerable per-request is free here, and it is the only thing standing between a 55-bit code
// and someone working through the space offline if they ever obtained a blob.
const ITERATIONS = 300000;

let active = null;      // { id, key } for the code currently paired on this device

const utf8 = new TextEncoder();

async function expand(master, label, bytes) {
    const key = await crypto.subtle.importKey('raw', master, 'HKDF', false, ['deriveBits']);
    return new Uint8Array(await crypto.subtle.deriveBits(
        { name: 'HKDF', hash: 'SHA-256', salt: new Uint8Array(0), info: utf8.encode(label) },
        key, bytes * 8));
}

function hex(bytes) {
    return Array.from(bytes).map(b => b.toString(16).padStart(2, '0')).join('');
}

function base64(bytes) {
    let s = '';
    for (const b of bytes) s += String.fromCharCode(b);
    return btoa(s);
}

function unbase64(text) {
    const s = atob(text);
    const bytes = new Uint8Array(s.length);
    for (let i = 0; i < s.length; i++) bytes[i] = s.charCodeAt(i);
    return bytes;
}

/// Turn a pairing code into the id and proof the server sees, and hold the encryption key here.
/// Returns { id, auth } -- deliberately not the key, which has no reason to exist in .NET.
export async function derive(code) {
    const material = await crypto.subtle.importKey(
        'raw', utf8.encode(code), 'PBKDF2', false, ['deriveBits']);

    const master = await crypto.subtle.deriveBits(
        { name: 'PBKDF2', salt: utf8.encode(SALT), iterations: ITERATIONS, hash: 'SHA-256' },
        material, 256);

    const id = hex(await expand(master, 'id', 16));
    const auth = hex(await expand(master, 'auth', 32));
    const raw = await expand(master, 'enc', 32);

    const key = await crypto.subtle.importKey(
        'raw', raw, { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']);

    active = { id, key };
    return { id, auth };
}

/// Forget the key. Unpairing has to leave nothing behind that could decrypt a blob.
export function forget() {
    active = null;
}

export async function encrypt(json) {
    if (!active) throw new Error('not paired');

    // A fresh nonce per write, never reused: AES-GCM leaks plaintext relationships if one is.
    const nonce = crypto.getRandomValues(new Uint8Array(12));
    const sealed = await crypto.subtle.encrypt(
        { name: 'AES-GCM', iv: nonce }, active.key, utf8.encode(json));

    return { payload: base64(new Uint8Array(sealed)), nonce: base64(nonce) };
}

/// Null rather than a throw when the blob will not open. The honest readings are a code that does
/// not belong to this document, or a payload written by a future format -- neither is an error the
/// user can act on differently, and both have to leave the local collection untouched.
export async function decrypt(payload, nonce) {
    if (!active) return null;

    try {
        const opened = await crypto.subtle.decrypt(
            { name: 'AES-GCM', iv: unbase64(nonce) }, active.key, unbase64(payload));
        return new TextDecoder().decode(opened);
    } catch {
        return null;
    }
}
