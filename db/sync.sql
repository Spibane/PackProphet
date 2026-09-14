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
    held_written timestamptz;
    next_version bigint;

    -- THE CALLER'S ADDRESS IS DELIBERATELY NOT READ HERE.
    --
    -- It can be. The database sits behind the API gateway, so inet_client_addr() is PostgREST's
    -- own loopback address and says nothing, but PostgREST passes the request's headers through
    -- as a setting, and for one night this function read them:
    --
    --     coalesce(nullif(current_setting('request.headers', true), '')::json ->> 'x-forwarded-for', '?')
    --     coalesce(nullif(current_setting('request.headers', true), '')::json ->> 'user-agent', '?')
    --
    -- appended to the refusal below. It is what found a browser on a university network that had
    -- been writing to one document 180 times a second for ten days, after two days of looking in
    -- the wrong places -- so it earns being written down rather than forgotten.
    --
    -- It is out because this app is built so the host cannot read what it stores, and logging a
    -- stranger's IP address is collecting the one thing about them it otherwise never sees. With
    -- a single user that cost nothing. With anyone else on it, it is a promise quietly broken in a
    -- log nobody reads.
    --
    -- Put it back for a session if a document misbehaves again, then take it out. The writer id
    -- below is enough to tell devices apart and is a random per-install string that means nothing
    -- off this server.

    -- How many documents may exist at once.
    --
    -- The key the app talks to this database with is published inside the app, because that is
    -- the only way a page with no sign-in can reach anything. So creating a document needs no
    -- pairing code: anyone may call this with an id nobody has used and get a row. The floor
    -- above does not apply, since there is nothing yet to have been written recently.
    --
    -- A ceiling is the cheap half of the answer. Storage is the failure that is painful to undo --
    -- a full database goes read-only and has to be emptied by hand -- and at 2 MB a document this
    -- bounds the table to a size the free tier does not notice. It does nothing about request
    -- volume, which only the platform in front of the database can refuse.
    --
    -- Fifty, against one real user and a few hundred KB per collection. Raise it if this is ever
    -- shared with more than a household.
    max_docs     constant integer := 50;

    -- The floor between two accepted writes to ONE document.
    --
    -- Every throttle the app has lives in the browser -- a six-second debounce, one sync at a
    -- time, three attempts -- and none of it is enforceable: the anon key ships in the published
    -- page, so anything holding a pairing code can call this in a loop and none of that applies.
    -- This is the only place a limit can be made to stick.
    --
    -- One second, because the legitimate ceiling is far below it. A device pushes at most once
    -- every six seconds and a person has a handful of devices, so even three of them syncing hard
    -- is a write every two seconds. A rejection here is not a lost edit either: the client waits
    -- and comes back with the same merge.
    write_floor  constant interval := interval '1 second';
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

    select d.auth, d.version, d.updated_at into held_auth, held_version, held_written
    from public.sync_docs d
    where d.id = doc_id
    for update;                                 -- serialises two devices racing on one document

    if held_auth is null then
        if expected_version is not null then
            -- The client expected a document that is gone. Almost certainly abandoned-document
            -- cleanup ran; it has to be told rather than silently starting a new one.
            raise exception 'document no longer exists' using errcode = 'P0002';
        end if;

        if (select count(*) from public.sync_docs) >= max_docs then
            raise exception 'the host is holding as many collections as it will'
                using errcode = '53400';
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

    -- After the auth check, so the rate of writes to a document is not something a caller without
    -- the pairing code can measure, and before the version compare, so a caller that is simply
    -- going too fast is told that rather than being told it lost a race it never entered. The
    -- client waits out the floor and retries; 40001 asks it to pull and merge first, which would
    -- be a wasted round trip here.
    if now() - held_written < write_floor then
        raise exception 'too many writes to this document' using errcode = '53400';
    end if;

    -- Two different things, and they used to share one SQLSTATE.
    --
    -- A null expected_version means "I pulled and found nothing here". Reaching this line proves
    -- that was wrong, and it is not a race: no other device has to have written for it to happen,
    -- and the client is told "another device wrote first" about a device that does not exist. It
    -- then retries, which cannot help -- its next pull returns nothing again -- and every attempt
    -- is another rejected write in the log under a name that sends anyone reading it looking for a
    -- second device.
    --
    -- Told apart so the log says which one happened. 55000 is object_not_in_prerequisite_state:
    -- the document is not in the state the caller believed it was.
    -- The values go in the message, not just the code.
    --
    -- Postgres logs the parameters as $1, so a refusal says which rule fired and nothing about
    -- what fired it -- and these two are only meaningful against the numbers involved. A caller
    -- sending the same stale version on every call looks identical in the log to two devices
    -- genuinely racing, until you can see that the expected version never moves.
    --
    -- Safe to write down: the writer is a random per-install string and the versions are counters.
    -- Neither says anything about the collection, which the database cannot read in any case.
    if expected_version is null then
        raise exception 'document exists (version %) but writer % expected none',
            held_version, doc_writer using errcode = '55000';
    end if;

    if held_version <> expected_version then
        raise exception 'writer % expected version %, stored is %',
            doc_writer, expected_version, held_version using errcode = '40001';
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

create or replace function public.sync_sweep(
    older_than interval default '180 days',
    -- A document nobody has written to twice. Junk from a burst looks like this, and so does a
    -- pairing made and then not used -- but only PUSHING moves the version, and pulling does not,
    -- so a perfectly real pairing sits at version 1 for as long as nobody edits anything. Set up
    -- on a Friday and left over a weekend is version 1 on Monday.
    --
    -- A week, therefore. The ceiling is what stops a burst; this only recycles the slots
    -- afterwards, so it can afford to be slow and wrong in the safe direction. Sweeping one of
    -- these costs nothing anyway: the device re-creates it under the same code on its next sync,
    -- without the user being asked. It is the second device joining inside that window that gets
    -- the messy path, and a week makes that vanishingly rare.
    never_used   interval default '7 days')
returns integer
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    removed integer;
begin
    delete from public.sync_docs
    where updated_at < now() - older_than
       or (version = 1 and created_at < now() - never_used);

    get diagnostics removed = row_count;
    return removed;
end;
$$;

-- Not granted to anon: this one is for you, not for the app.
-- select cron.schedule('packprophet-sync-sweep', '0 4 * * 0', $$select public.sync_sweep()$$);
