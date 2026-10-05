#!/usr/bin/env python3
"""Vendor the card art that upstream has not published yet.

Card DATA and card ART come from the same author in two different repositories on two different
cadences. The data ships within days of a set going live; the art is a manual commit. B4a's data
was published 2026-08-27 and its art was still absent a week later, so every card in the newest
set drew as a placeholder -- which looks like a broken app rather than an upstream gap.

The art does exist by then, just not on a per-file CDN: the database repository attaches it to each
release as one zip. So this reads that zip, takes only the sets Limitless is missing, and writes
them into the published output. Nothing is committed -- see the note on --out below -- so the repository
never grows and a fresh clone in ten years is the same size as one today.

The zip is ~400 MB and this downloads about 6 MB of it. A zip's index lives at the END of the file,
so the tail gives the name, offset and size of every member without reading any of them, and each
member is then one ranged request. That is the whole trick, and it is why this can run on every
deploy rather than being a thing somebody remembers to do.

Two callers, for two different reasons:

    tools/vendor-gap-art.py --out build/wwwroot/art     # the deploy, so the app can serve the art
    tools/vendor-gap-art.py --out /tmp/gap-art          # the fingerprint refresh, so it can read it

The second is the same problem from the other end. tools/CardHashGen downloads from the art CDN to
fingerprint a card and read its type badge, and the only set it has work to do for is the newest --
which is, for the reason above, precisely the set the CDN does not have. So it saw 0 of B4a's 110
cards while this script was already extracting all 110 of them for the deploy. Both jobs wanted the
same bytes; now the refresh runs this first and passes the directory as --art-dir.

The deploy's --out is written straight into the PUBLISHED output, after `dotnet publish`, on purpose:

  * files added before publish are fingerprinted by the static-asset pipeline, which would rename
    them out from under the URLs ArtSource builds;
  * files added before publish also land in service-worker-assets.js, and overwriting anything in
    there after publish fails the deploy's own integrity check;
  * and art under the published tree but outside data/ is not precached, so a first visit does not
    download a set nobody has scrolled to.

Exit code is 0 when there was nothing to do. A failure to reach either source is also 0 with a
warning: a deploy must not be blocked because a CDN was briefly unavailable, since the app still
works -- it falls back to the remote chain, which is where it looked before this existed.
"""

from __future__ import annotations

import argparse
import io
import json
import os
import struct
import sys
import urllib.error
import urllib.request
import zlib

DATA_CDN = "https://cdn.jsdelivr.net/npm/pokemon-tcg-pocket-database/dist"
LIMITLESS = "https://limitlesstcg.nyc3.cdn.digitaloceanspaces.com/pocket"
TCGDEX_SERIES = "https://api.tcgdex.net/v2/en/series/tcgp"
DB_REPO = "flibustier/pokemon-tcg-pocket-database"
UA = "PackProphet-deploy (+https://packprophet.spibane.com)"


def log(msg: str) -> None:
    print(msg, flush=True)


def warn(msg: str) -> None:
    # GitHub Actions renders this as an annotation; harmless anywhere else.
    print(f"::warning::{msg}", flush=True)


def get(url: str, *, headers: dict[str, str] | None = None, timeout: int = 60) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": UA, **(headers or {})})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read()


def get_json(url: str, *, headers: dict[str, str] | None = None):
    return json.loads(get(url, headers=headers))


def gh_headers() -> dict[str, str]:
    """Authorise against the API when a token is available, purely for the rate limit."""
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    h = {"Accept": "application/vnd.github+json"}
    if token:
        h["Authorization"] = f"Bearer {token}"
    return h


def get_range(url: str, start: int, end: int) -> bytes:
    """Bytes [start, end] inclusive, the way HTTP ranges are counted."""
    data = get(url, headers={"Range": f"bytes={start}-{end}"})
    want = end - start + 1
    if len(data) != want:
        # A server that ignored the header and sent the whole file would otherwise be parsed as
        # though it were the slice that was asked for.
        raise RuntimeError(f"range request returned {len(data)} bytes, expected {want}")
    return data


# --------------------------------------------------------------------------- what is missing


def card_data() -> tuple[dict[str, list[int]], dict[str, set[str]]]:
    """
    Every set the app knows about: the card numbers in it, and the packs it is sold in.

    Both come off one fetch of the same file the app itself boots from, so this vendors art for
    exactly the cards the app will ask for -- not for whatever the archive happens to contain.
    """
    cards = get_json(f"{DATA_CDN}/cards.min.json")
    numbers: dict[str, list[int]] = {}
    packs: dict[str, set[str]] = {}
    for c in cards:
        code = c["set"]
        numbers.setdefault(code, []).append(int(c["number"]))
        packs.setdefault(code, set()).update(c.get("packs") or [])
    return {k: sorted(v) for k, v in numbers.items()}, packs


def published_code(code: str) -> str:
    """How Limitless and TCGdex spell a set code. Mirrors ArtSource.PublishedSetCode."""
    return {"PROMO-A": "P-A", "PROMO-B": "P-B"}.get(code.upper(), code)


def sets_on_limitless(known: dict[str, list[int]]) -> set[str]:
    """
    Sets Limitless has art for, the app's first source that has every set.

    Asked one card per set, and the HIGHEST-numbered one: a promo set is never published whole,
    so its newest card is the one that goes missing, and for any other set the last card is the
    last to be drawn. Limitless answers a missing file with 403, since its bucket does not let a
    stranger list it, and with 404 as well; either one is "not there". Anything else -- a timeout,
    a 5xx -- is counted as present, because vendoring a set costs megabytes of deploy and a set
    wrongly left out costs nothing the remote chain does not already cover.
    """
    have = set()
    for code, numbers in known.items():
        pub = published_code(code)
        url = f"{LIMITLESS}/{pub}/{pub}_{max(numbers):03d}_EN.webp"
        req = urllib.request.Request(url, method="HEAD", headers={"User-Agent": UA})
        try:
            with urllib.request.urlopen(req, timeout=30):
                have.add(code)
        except urllib.error.HTTPError as e:
            if e.code not in (403, 404):
                warn(f"Limitless answered {e.code} for {code}; counting it as present")
                have.add(code)
        except (urllib.error.URLError, TimeoutError) as e:
            warn(f"could not ask Limitless about {code} ({e}); counting it as present")
            have.add(code)
    return have


def sets_on_tcgdex(known: dict[str, list[int]]) -> list[str]:
    """
    Sets TCGdex lists, in the app's spelling, for the manifest. The app tries TCGdex first for
    these and skips it for the rest, so one request here saves a 404 per card of every set
    TCGdex has not reached yet. Empty, with a warning, when it cannot be asked: the chain then
    starts at Limitless, which is complete.
    """
    try:
        listed = {s["id"] for s in get_json(TCGDEX_SERIES)["sets"]}
    except (urllib.error.URLError, urllib.error.HTTPError, KeyError, TypeError, ValueError) as e:
        warn(f"could not read TCGdex's set list ({e}); the app will start at Limitless")
        return []
    return sorted(code for code in known if published_code(code) in listed)


# --------------------------------------------------------------------------- reading the zip


def release_zip_url() -> str:
    """The newest release's zip asset. Named dist.zip today and release.zip before 2.10.0."""
    rel = get_json(f"https://api.github.com/repos/{DB_REPO}/releases/latest", headers=gh_headers())
    zips = [a for a in rel.get("assets", []) if a["name"].endswith(".zip")]
    if not zips:
        raise RuntimeError(f"release {rel.get('tag_name')} has no zip asset")
    # Largest, in case a release ever carries both the images and something smaller.
    asset = max(zips, key=lambda a: a.get("size", 0))
    log(f"release {rel.get('tag_name')}: {asset['name']} ({asset['size'] / 1e6:.0f} MB)")
    return asset["browser_download_url"]


def resolved_length(url: str) -> tuple[str, int]:
    """Follow the redirect once and report the final URL and its length."""
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=60) as r:
        if r.headers.get("Accept-Ranges") != "bytes":
            raise RuntimeError("asset host does not accept range requests")
        return r.geturl(), int(r.headers["Content-Length"])


def central_directory(url: str, size: int) -> list[tuple[str, int, int, int]]:
    """
    Every member as (name, compressed size, uncompressed size, local header offset).

    Read from the end of the file: the End Of Central Directory record is last, and it says where
    the index starts.
    """
    tail_len = min(size, 70_000)
    tail = get_range(url, size - tail_len, size - 1)

    eocd = tail.rfind(b"PK\x05\x06")
    if eocd < 0:
        raise RuntimeError("no end-of-central-directory record in the last 70 KB")

    cd_size, cd_off = struct.unpack_from("<II", tail, eocd + 12)

    # Zip64, for a release that outgrows the 4 GB or 65,535-entry fields above. Refused rather
    # than guessed at: a truncated offset would read the index from the wrong place and the
    # failure would look like a corrupt archive.
    if cd_size == 0xFFFFFFFF or cd_off == 0xFFFFFFFF:
        z64 = tail.rfind(b"PK\x06\x06")
        if z64 < 0:
            raise RuntimeError("zip64 sizes with no zip64 end-of-central-directory record")
        cd_size = struct.unpack_from("<Q", tail, z64 + 40)[0]
        cd_off = struct.unpack_from("<Q", tail, z64 + 48)[0]

    cd = get_range(url, cd_off, cd_off + cd_size - 1)

    entries: list[tuple[str, int, int, int]] = []
    at = 0
    while at < len(cd) - 4 and cd[at:at + 4] == b"PK\x01\x02":
        csize, usize = struct.unpack_from("<II", cd, at + 20)
        nlen, elen, clen = struct.unpack_from("<HHH", cd, at + 28)
        lho = struct.unpack_from("<I", cd, at + 42)[0]
        name = cd[at + 46:at + 46 + nlen].decode("utf-8", "replace")
        entries.append((name, csize, usize, lho))
        at += 46 + nlen + elen + clen

    log(f"index: {len(entries)} members")
    return entries


def member_bytes(url: str, csize: int, usize: int, lho: int) -> bytes:
    """One member, by ranged read of its local header and payload."""
    # The local header repeats the name and extra field, and its extra field length can differ
    # from the one in the central directory -- so it has to be read rather than assumed. 64 KB
    # covers any name and extra this archive uses.
    head = get_range(url, lho, min(lho + 65_535, lho + 30 + 65_535))
    if head[:4] != b"PK\x03\x04":
        raise RuntimeError("member does not start with a local file header")

    method = struct.unpack_from("<H", head, 8)[0]
    nlen, elen = struct.unpack_from("<HH", head, 26)
    start = lho + 30 + nlen + elen

    payload = get_range(url, start, start + csize - 1)
    if method == 0:
        raw = payload
    elif method == 8:
        raw = zlib.decompress(payload, -15)
    else:
        raise RuntimeError(f"unsupported compression method {method}")

    if len(raw) != usize:
        raise RuntimeError(f"member inflated to {len(raw)} bytes, expected {usize}")
    return raw


# --------------------------------------------------------------------------- the job


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--out", required=True, help="directory to write, inside the published output")
    ap.add_argument("--set", action="append", default=[],
                    help="vendor this set whatever the CDN has (repeatable; for testing)")
    ap.add_argument("--dry-run", action="store_true", help="report what would be written")
    ap.add_argument("--release", help="read the card list from this database version, not latest")
    args = ap.parse_args()

    # Pinned when the caller knows the version, as the deploy does: jsDelivr resolves "latest" per
    # file and caches each answer for a week, so the unpinned card list can be a release behind
    # the one the deploy is recording, and art would be vendored for the older one.
    if args.release:
        global DATA_CDN
        DATA_CDN = f"https://cdn.jsdelivr.net/npm/pokemon-tcg-pocket-database@{args.release}/dist"

    try:
        known, packs_by_set = card_data()
        # --set is the testing path and says "vendor this whatever upstream has", so it skips the
        # listing rather than being filtered by it.
        have = set() if args.set else sets_on_limitless(known)
    except (urllib.error.URLError, urllib.error.HTTPError, RuntimeError, ValueError, KeyError) as e:
        warn(f"could not work out which sets are missing ({e}); deploying with no vendored art")
        return 0

    tcgdex = sets_on_tcgdex(known)

    unknown = [s for s in args.set if s not in known]
    if unknown:
        log(f"::error::no such set in the card data: {', '.join(unknown)}")
        return 1

    missing = sorted(args.set) if args.set else sorted(s for s in known if s not in have)

    if not missing:
        log(f"every one of the {len(known)} sets is on Limitless; nothing to vendor")
        # Still written, and written empty: it is the difference between "this deploy vendored
        # nothing" and a manifest that failed to appear, and only one of those is worth a warning
        # in the browser console later.
        write_manifest(args.out, [], [], {}, tcgdex, args.dry_run)
        return 0

    cards = sum(len(known[s]) for s in missing)
    log(f"missing from Limitless: {', '.join(missing)} ({cards} cards)")

    try:
        url, size = resolved_length(release_zip_url())
        index = central_directory(url, size)
    except (urllib.error.URLError, urllib.error.HTTPError, RuntimeError, KeyError, ValueError) as e:
        warn(f"could not read the release archive ({e}); deploying with no vendored art")
        return 0

    # Matched by suffix rather than by full path so a release that moves its root -- dist/ today,
    # something else tomorrow -- still resolves.
    by_suffix = {}
    for name, csize, usize, lho in index:
        if name.endswith("/"):
            continue
        for marker in ("images/cards-by-set/", "images/packs/", "images/sets/"):
            if marker in name:
                by_suffix[name[name.index(marker):]] = (csize, usize, lho)
                break

    wanted: list[tuple[str, str]] = []      # (suffix in zip, path under --out)
    for s in missing:
        for n in known[s]:
            wanted.append((f"images/cards-by-set/{s}/{n}.webp", f"{s}/{n}.webp"))
        wanted.append((f"images/sets/LOGO_expansion_{s}_en_US.webp",
                       f"sets/LOGO_expansion_{s}_en_US.webp"))

    # Pack art is named by pack, not by set, so the packs of a missing set come from the card data
    # rather than from the set code.
    #
    # A name more than one set uses is skipped: "Deluxe" is A4b's pack and B4b's, one file in the
    # archive, and the manifest is keyed by name, so vendoring it for B4b would redraw A4b too.
    # The app shows a placeholder for a shared name until the expansions index has the set.
    uses = {}
    for s, names in packs_by_set.items():
        for p in names:
            uses[p] = uses.get(p, 0) + 1
    shared = sorted({p for s in missing for p in packs_by_set.get(s, set()) if uses[p] > 1})
    if shared:
        log(f"not vendoring booster art named by more than one set: {', '.join(shared)}")
    packs = sorted({p for s in missing for p in packs_by_set.get(s, set()) if uses[p] == 1})
    for p in packs:
        wanted.append((f"images/packs/{p}.webp", f"packs/{p}.webp"))

    if args.dry_run:
        found = [w for w in wanted if w[0] in by_suffix]
        log(f"would write {len(found)} of {len(wanted)} files "
            f"({sum(by_suffix[f[0]][1] for f in found) / 1e6:.1f} MB)")
        for suffix, _ in wanted:
            if suffix not in by_suffix:
                log(f"  not in the archive either: {suffix}")

        # Counted off the archive rather than off the filesystem, because a dry run writes
        # nothing -- so this is "what the app would be told", which is the whole point of
        # previewing a deploy. The real path counts extracted files instead, and the two differ
        # only where an extraction fails, which is logged where it happens.
        coverage = {
            s: {"have": sum(1 for n in known[s]
                            if f"images/cards-by-set/{s}/{n}.webp" in by_suffix),
                "of": len(known[s])}
            for s in missing
        }
        write_manifest(args.out, sorted(s for s in missing if coverage[s]["have"] > 0),
                       [p for p in packs if f"images/packs/{p}.webp" in by_suffix],
                       coverage, tcgdex, dry=True)
        return 0

    written, absent, bytes_out = 0, [], 0
    for suffix, rel in wanted:
        entry = by_suffix.get(suffix)
        if entry is None:
            absent.append(suffix)
            continue
        try:
            raw = member_bytes(url, *entry)
        except (urllib.error.URLError, urllib.error.HTTPError, RuntimeError) as e:
            warn(f"could not extract {suffix} ({e})")
            absent.append(suffix)
            continue

        dest = os.path.join(args.out, rel)
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        with open(dest, "wb") as f:
            f.write(raw)
        written += 1
        bytes_out += len(raw)

    log(f"vendored {written} file(s), {bytes_out / 1e6:.1f} MB, for {', '.join(missing)}")
    if absent:
        # Expected, not a failure: a set can reach the data before all of its own art exists, and
        # those cards keep falling back to the remote chain and then to the placeholder.
        log(f"{len(absent)} file(s) are not in the archive yet")

    # How much of each missing set this deploy can actually draw.
    #
    # Written for the app's notice bar rather than for anything here, and this is the only place
    # that can know it: the app cannot discover a missing image without requesting it, and probing
    # 3,879 of them to decide whether to show one sentence is absurd. The work is already done
    # above -- which sets the art repository lacks, and which files the archive turned out to hold
    # -- so all that was missing was writing the answer down instead of only the fix.
    #
    # Every missing set is recorded, including the ones nothing was written for. A set with have=0
    # is the one most worth saying out loud, and it is exactly the one `sets` below must omit.
    coverage = {
        s: {
            "have": sum(1 for n in known[s]
                        if os.path.exists(os.path.join(args.out, s, f"{n}.webp"))),
            "of": len(known[s]),
        }
        for s in missing
    }

    # Only the sets something was actually written for. Claiming a set the app then cannot serve
    # would spend a request per card on this app's own origin discovering that.
    served = sorted(s for s in missing if coverage[s]["have"] > 0)
    served_packs = [p for p in packs if os.path.exists(os.path.join(args.out, "packs", f"{p}.webp"))]

    for s in missing:
        c = coverage[s]
        if c["have"] < c["of"]:
            log(f"{s}: art for {c['have']} of {c['of']} cards")

    write_manifest(args.out, served, served_packs, coverage, tcgdex, args.dry_run)
    return 0


def write_manifest(out: str, sets: list[str], packs: list[str],
                   coverage: dict[str, dict[str, int]], tcgdex: list[str], dry: bool) -> None:
    if dry:
        log(f"would write index.json: sets={sets} packs={packs} art={coverage} tcgdex={tcgdex}")
        return
    os.makedirs(out, exist_ok=True)
    path = os.path.join(out, "index.json")
    with io.open(path, "w", encoding="utf-8") as f:
        # "art" and "tcgdex" are additive: a deployment written by an older copy of this script has
        # neither, and the app reads that as "nothing known" rather than as an error.
        json.dump({"sets": sets, "packs": packs, "art": coverage, "tcgdex": tcgdex}, f)
        f.write("\n")
    log(f"wrote {path}: sets={sets or 'none'} packs={packs or 'none'} tcgdex={len(tcgdex)} set(s)")


if __name__ == "__main__":
    sys.exit(main())
