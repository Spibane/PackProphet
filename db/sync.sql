-- PackProphet cloud sync: the whole server side.
--
-- Run this once in the Supabase SQL editor. It creates one table that nothing outside the database
-- can read, and two functions that are the only way in.
--
-- What the host can see: an opaque id, a proof token, and a blob of AES-GCM ciphertext. The key
-- that opens the blob is derived from the user's pairing code and never leaves their device
-- (see wwwroot/js/sync.js), so this schema stores collections it cannot read.

create table if not exists public.sync_docs (
    id          text        primary key,
    -- Proves the writer knows the pairing code. Independently derived from the encryption key, so
    -- holding this column reveals nothing about the plaintext. Without it, anyone who guessed an
    -- id could overwrite a stranger's collection with garbage -- they still could not read it, but
    -- destroying it would be enough.
    auth        text        not null,
    payload     text        not null,
    nonce       text        not null,
    -- Which device wrote last, so the app can say "synced from your phone" rather than just a
    -- time. A random per-install string, not an identifier for a person.
    writer      text        not null,
    -- The concurrency token. A counter rather than a timestamp: two devices pushing inside the
    -- same clock tick would read the same timestamp, and the compare-and-set below would let the
    -- second one through, which is the exact update it exists to catch.
    version     bigint      not null default 1,
    updated_at  timestamptz not null default now(),
    created_at  timestamptz not null default now()
);

-- Row-level security with NO policies, which is the point: PostgREST cannot select, insert, update
-- or delete this table at all, whatever the anon key in the published app allows. The two
-- functions below run as owner and are the entire surface.
alter table public.sync_docs enable row level security;
revoke all on public.sync_docs from anon, authenticated;

-- ---------------------------------------------------------------------------------------------
-- Shape constraints
--
-- Every one of these columns is a fixed-width value the client derives, and `text` in Postgres is
-- unbounded. Capping only `payload` left the other four open: the anon key is published in the app,
-- so anyone could call sync_push with a half-gigabyte `writer` and fill the database, with the
-- 2 MB payload check giving a false impression that inserts were bounded. These pin each column to
-- the shape it actually has, which also turns a malformed client into a clean refusal.
--
-- Written to be re-runnable: this file is meant to be pasted over an existing project.

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'sync_docs_id_shape') then
        alter table public.sync_docs add constraint sync_docs_id_shape
            check (id ~ '^[0-9a-f]{32}$');                    -- HKDF 'id', 16 bytes as hex
    end if;

    if not exists (select 1 from pg_constraint where conname = 'sync_docs_auth_shape') then
        alter table public.sync_docs add constraint sync_docs_auth_shape
            check (auth ~ '^[0-9a-f]{64}$');                  -- HKDF 'auth', 32 bytes as hex
    end if;

    if not exists (select 1 from pg_constraint where conname = 'sync_docs_nonce_shape') then
        alter table public.sync_docs add constraint sync_docs_nonce_shape
            check (nonce ~ '^[A-Za-z0-9+/=]{1,32}$');         -- 12 random bytes, base64
    end if;

    if not exists (select 1 from pg_constraint where conname = 'sync_docs_writer_shape') then
        alter table public.sync_docs add constraint sync_docs_writer_shape
            check (writer ~ '^[0-9a-f]{0,32}$');              -- install id, 6 bytes as hex
    end if;

    if not exists (select 1 from pg_constraint where conname = 'sync_docs_payload_size') then
        alter table public.sync_docs add constraint sync_docs_payload_size
            check (length(payload) <= 2000000);
    end if;
end
$$;

-- ---------------------------------------------------------------------------------------------
-- Pull

create or replace function public.sync_pull(doc_id text, doc_auth text)
returns table (payload text, nonce text, writer text, version bigint, updated_at timestamptz)
language sql
security definer
set search_path = pg_catalog, public
as $$
    -- The auth check is here as well as on push. Reading is harmless -- the blob is ciphertext --
    -- but there is no reason to hand it out to someone who cannot open it.
    select d.payload, d.nonce, d.writer, d.version, d.updated_at
    from public.sync_docs d
    where d.id = doc_id and d.auth = doc_auth;
$$;

-- ---------------------------------------------------------------------------------------------
-- Push
--
-- Compare-and-set on `version`. Without it sync loses data in the ordinary case: two devices both
-- pull, both merge, and the second push overwrites a merge the first one had already completed.
-- Rejecting the stale write sends the client back to pull, merge again, and retry -- which is
-- correct, because by then it has seen the other device's work.

create or replace function public.sync_push(
    doc_id           text,
    doc_auth         text,
    doc_payload      text,
    doc_nonce        text,
    doc_writer       text,
    expected_version bigint default null      -- null means "I believe this document does not exist"
)
returns bigint
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    held_auth    text;
    held_version bigint;
    next_version bigint;
begin
    -- A whole collection with several thousand logged packs is a few hundred KB. Two megabytes is
    -- far above any real save and far below anything that could be used to fill the database.
    if length(doc_payload) > 2000000 then
        raise exception 'payload too large' using errcode = '22001';
    end if;

    -- Checked here as well as by the constraints above, so a malformed call is refused with a
    -- stated reason rather than surfacing as a constraint violation the client cannot classify.
    if doc_id !~ '^[0-9a-f]{32}$'
        or doc_auth !~ '^[0-9a-f]{64}$'
        or doc_nonce !~ '^[A-Za-z0-9+/=]{1,32}$'
        or doc_writer !~ '^[0-9a-f]{0,32}$' then
        raise exception 'malformed request' using errcode = '22023';
    end if;

    select d.auth, d.version into held_auth, held_version
    from public.sync_docs d
    where d.id = doc_id
    for update;                                 -- serialises two devices racing on one document

    if held_auth is null then
        if expected_version is not null then
            -- The client expected a document that is gone. Almost certainly abandoned-document
            -- cleanup ran; it has to be told rather than silently starting a new one.
            raise exception 'document no longer exists' using errcode = 'P0002';
        end if;

        insert into public.sync_docs (id, auth, payload, nonce, writer)
        values (doc_id, doc_auth, doc_payload, doc_nonce, doc_writer)
        returning version into next_version;

        return next_version;
    end if;

    -- Not a constant-time comparison, and deliberately not treated as one. The token is 256 bits
    -- of HKDF output, so distinguishing a first-byte mismatch from a full match over a network --
    -- against Postgres, through PostgREST -- is not a route to guessing it. Noted rather than
    -- papered over with a hand-rolled comparison that would be slower and no more honest.
    if held_auth <> doc_auth then
        raise exception 'wrong pairing code for this document' using errcode = '42501';
    end if;

    if expected_version is null or held_version <> expected_version then
        raise exception 'another device wrote first' using errcode = '40001';
    end if;

    update public.sync_docs
    set payload    = doc_payload,
        nonce      = doc_nonce,
        writer     = doc_writer,
        version    = version + 1,
        updated_at = now()
    where id = doc_id
    returning version into next_version;

    return next_version;
end;
$$;

-- ---------------------------------------------------------------------------------------------
-- Unpair, for a user who wants the remote copy gone now rather than in ninety days.

create or replace function public.sync_forget(doc_id text, doc_auth text)
returns boolean
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    removed integer;
begin
    delete from public.sync_docs where id = doc_id and auth = doc_auth;
    get diagnostics removed = row_count;
    return removed > 0;
end;
$$;

-- The functions are the API; the table is not.
grant execute on function public.sync_pull(text, text) to anon;
grant execute on function public.sync_push(text, text, text, text, text, bigint) to anon;
grant execute on function public.sync_forget(text, text) to anon;

-- ---------------------------------------------------------------------------------------------
-- Abandoned documents
--
-- Someone pairs, tries it, and never comes back. Nothing here can tell that apart from a user on
-- holiday, so the window is long. Enable pg_cron and schedule this, or run it by hand
-- occasionally; either way the app warns that an unused pairing expires.

create or replace function public.sync_sweep(older_than interval default '180 days')
returns integer
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    removed integer;
begin
    delete from public.sync_docs where updated_at < now() - older_than;
    get diagnostics removed = row_count;
    return removed;
end;
$$;

-- Not granted to anon: this one is for you, not for the app.
-- select cron.schedule('packprophet-sync-sweep', '0 4 * * 0', $$select public.sync_sweep()$$);
