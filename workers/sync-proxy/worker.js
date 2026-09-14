// The front door for cloud sync.
//
// WHY THIS EXISTS
// ==================================================================================
// A page with no sign-in needs some credential to reach a database, so the Supabase anon key
// shipped inside the app where anyone could read it. Everything else about sync is sound -- the
// payload is encrypted on the device, the table has no policies, the only surface is three
// functions that each demand a token derived from the pairing code -- but a published key means
// anyone can CALL those functions, as fast as they like, and a call costs the project's quota
// whether or not it succeeds.
//
// Nothing in the database can fix that. By the time Postgres runs, the request has crossed the
// edge, gone through PostgREST and taken a connection: refusing there makes the call cheap, never
// absent. The refusal has to happen before Supabase is involved at all.
//
// So the key lives here as a secret, the app talks to this instead, and Cloudflare turns away
// anything over the rate before it reaches anyone's database. The app is left with no credential
// worth stealing.
//
// WHAT THIS IS NOT
// ----------------------------------------------------------------------------------
// A secret. This endpoint is public -- it has to be, the app is public -- and anyone may call it.
// What changes is that every call is now counted and capped, and there is no way to go round it,
// because the key that would let you is not in the page any more.
//
// It also reads nothing. The body is forwarded byte for byte; the payload inside it is ciphertext
// this worker could not open if it wanted to.

const CALLS = new Set(['sync_pull', 'sync_push', 'sync_forget']);

// A collection is a few hundred KB and the database refuses a payload over 2 MB. Three is that
// with room for the JSON around it, and it is checked here so an oversized body is dropped at the
// edge rather than forwarded for Postgres to reject.
const MAX_BODY = 3 * 1024 * 1024;

export default {
    async fetch(request, env) {
        const cors = allow(request.headers.get('Origin'), env);

        // The browser asks before it sends: the app posts JSON, which is not a simple request.
        if (request.method === 'OPTIONS') return new Response(null, { status: 204, headers: cors });
        if (request.method !== 'POST') return refuse(405, 'post only', cors);
        if (!cors['Access-Control-Allow-Origin']) return refuse(403, 'origin not allowed', cors);

        // One of three names, nothing else. The rest of the project's data API is not reachable
        // through here at all, which is most of the point of a proxy over a shared key.
        const call = new URL(request.url).pathname.replace(/^\/+/, '');
        if (!CALLS.has(call)) return refuse(404, 'no such call', cors);

        // Per address, before anything is forwarded. Optional so the worker still runs with the
        // binding unconfigured -- a deploy that silently had no limiter would be worse than one
        // that fails loudly, so the readme says to check it.
        if (env.RATE_LIMIT) {
            const who = request.headers.get('CF-Connecting-IP') ?? 'unknown';
            const { success } = await env.RATE_LIMIT.limit({ key: who });
            if (!success) return refuse(429, 'too many requests', cors);
        }

        const body = await request.text();
        if (body.length > MAX_BODY) return refuse(413, 'too large', cors);

        let upstream;
        try {
            upstream = await fetch(`${env.SUPABASE_URL}/rest/v1/rpc/${call}`, {
                method: 'POST',
                headers: {
                    'content-type': 'application/json',
                    apikey: env.SUPABASE_ANON_KEY,
                    authorization: `Bearer ${env.SUPABASE_ANON_KEY}`,
                },
                body,
            });
        } catch {
            // The app reads a failure to reach the host as "offline, nothing synced, your
            // collection here is unaffected", which is the honest reading of this too.
            return refuse(502, 'sync host unreachable', cors);
        }

        // Passed through as-is. The app tells a lost race from a refusal by the SQLSTATE in the
        // body, so rewriting either the status or the JSON would break the part of sync that
        // depends on knowing exactly which rule said no.
        const headers = new Headers(cors);
        headers.set('content-type', upstream.headers.get('content-type') ?? 'application/json');

        return new Response(upstream.body, { status: upstream.status, headers });
    },
};

/// One origin, named in the config, echoed back only when it matches. A wildcard would let any
/// page on the internet drive this with a visitor's browser.
function allow(origin, env) {
    const permitted = (env.ALLOWED_ORIGIN ?? '')
        .split(',')
        .map(o => o.trim())
        .filter(Boolean);

    const headers = {
        'Access-Control-Allow-Methods': 'POST, OPTIONS',
        'Access-Control-Allow-Headers': 'content-type',
        'Access-Control-Max-Age': '86400',
        Vary: 'Origin',
    };

    if (origin && permitted.includes(origin)) headers['Access-Control-Allow-Origin'] = origin;

    return headers;
}

function refuse(status, message, cors) {
    const headers = new Headers(cors);
    headers.set('content-type', 'application/json');

    // Shaped like PostgREST's own errors, so the app's one error reader handles both.
    return new Response(JSON.stringify({ message }), { status, headers });
}
