# sync-proxy

The front door for cloud sync, so the app does not have to carry a key.

## Why

Sync is sound where it matters: the collection is encrypted on the device, the table has
row-level security with no policies, and the only reachable surface is three functions that each
demand a token derived from the pairing code. None of that stops someone **calling** those
functions, because a page with no sign-in needs a credential to reach a database at all — so the
Supabase anon key shipped inside the app, where anyone could read it.

A call costs the project's quota whether or not it succeeds, and nothing in the database can
change that. By the time Postgres runs, the request has crossed the edge, gone through PostgREST
and taken a connection. Refusing there makes a call cheap; it never makes it absent.

This worker moves the key out of the page and the refusal in front of Supabase.

## What it does

- holds the Supabase URL and anon key as secrets
- accepts `sync_pull`, `sync_push` and `sync_forget`, and nothing else
- accepts them only from the app's own origin
- rate-limits per address **before** forwarding
- drops a body over 3 MB
- forwards everything else byte for byte, including the error codes sync depends on reading

It reads nothing. The payload is ciphertext it could not open if it tried.

It is **not** a secret itself. Anyone can call it — the app is public. What changes is that every
call is counted and capped, and there is no way round it, because the key that would let you go
direct is no longer in the page.

## Deploying

```bash
cd workers/sync-proxy
npx wrangler secret put SUPABASE_URL        # https://<project>.supabase.co
npx wrangler secret put SUPABASE_ANON_KEY
npx wrangler deploy
```

Set `ALLOWED_ORIGIN` in `wrangler.toml` to wherever the app is served from.

Then point the app at it, in the repository variable the deploy reads:

```
PACKPROPHET_SYNC_PROXY = https://packprophet-sync.<you>.workers.dev
```

Leave `PACKPROPHET_SYNC_KEY` unset. The deploy refuses to build an app that ships both, since one
would hand a reader the means to skip the other.

## Check it worked

```bash
# the app's own origin, a real call: expect 200 and []
curl -i -X POST https://packprophet-sync.<you>.workers.dev/sync_pull \
  -H 'content-type: application/json' -H 'Origin: https://packprophet.spibane.com' \
  -d '{"doc_id":"00000000000000000000000000000000","doc_auth":"0000000000000000000000000000000000000000000000000000000000000000"}'

# anything else: expect 404
curl -i -X POST https://packprophet-sync.<you>.workers.dev/sync_docs \
  -H 'Origin: https://packprophet.spibane.com' -d '{}'

# another site driving a visitor's browser: expect 403
curl -i -X POST https://packprophet-sync.<you>.workers.dev/sync_pull \
  -H 'Origin: https://example.com' -d '{}'
```

**Confirm the rate limiter is actually bound.** The worker treats it as optional so that a missing
binding does not take sync down, which means a misconfigured deploy looks healthy and enforces
nothing. Fire seventy requests in a minute and check the last few come back `429`.

## After it is live

Rotate the Supabase anon key. The old one is in every build published before this and in anyone's
cache; the worker is the only thing that needs the new one.
