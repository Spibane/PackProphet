#!/usr/bin/env bash
#
# Refresh the vendored card snapshot from the community datasets.
#
# WHAT THE SNAPSHOT IS FOR
# ==================================================================================
# The app loads card data from the CDN and falls back to these files, so a cold start on a slow
# connection shows cards rather than an empty grid. That makes the snapshot a floor, not a cache:
# it is allowed to be behind, and it is the version every test renders against.
#
# It drifts anyway, and the drift has a cost the tests will find. The fingerprint table is
# generated against these files (see .github/workflows/card-data.yml, which also runs this script when a new release appears), so a snapshot months
# behind means new cards have no fingerprint and screenshot import cannot recognise them.
#
# AFTER RUNNING THIS
# ----------------------------------------------------------------------------------
#   1. dotnet run --project tools/CardHashGen/CardHashGen.csproj -c Release -- \
#          --cards src/PackProphet.App/wwwroot/data/snapshot/cards.min.json \
#          --out src/PackProphet.App/wwwroot/data/card-hashes.txt --only-missing
#   2. dotnet test  -- the counts in CommittedArtHashesTests are written down on purpose and a
#      refresh is exactly when they are meant to be reconsidered rather than followed.
#
# curl rather than python: this machine's python has no CA bundle without SSL_CERT_FILE set, and
# a refresh that fails on certificates once a month is a refresh nobody runs.

set -euo pipefail

DEST="${1:-src/PackProphet.App/wwwroot/data/snapshot}"

# VERSION is not a file the CDN serves -- dist/VERSION is a 404. It is a marker this script
# writes, holding the package version the rest of the files came from, and CardDataLoader reads it
# back to say which snapshot the app fell back to.
#
# Resolved first and then pinned into every URL below, so the files cannot come from two different
# releases: fetching "latest" seven times across a publish would vendor half of one version and
# half of the next, and the marker would name only one of them.
VERSION="$(curl -fsSL https://registry.npmjs.org/pokemon-tcg-pocket-database/latest \
    | python3 -c 'import json,sys; print(json.load(sys.stdin)["version"])')"

DB="https://cdn.jsdelivr.net/npm/pokemon-tcg-pocket-database@${VERSION}/dist"
FACTS="https://cdn.jsdelivr.net/npm/pokemon-tcg-pocket-cards@5/data/v5"
REPO="https://cdn.jsdelivr.net/gh/chase-mew/pokemon-tcg-pocket-cards@main/data/v5"

# Each line is: local name <- url. The pairing is not obvious and is not ours to choose -- it
# mirrors CardDataLoader, which reads expansions from the git repo and the facts file from a
# different package than the card list. A snapshot assembled from different sources than the app
# reads live is a snapshot that disagrees with the CDN it is standing in for.
#
# Retried, because a cold jsDelivr edge can answer a large file it has not cached with a 503. The
# 4.7 MB facts file did that to two scheduled runs on 2026-10-05, and served fine 37 seconds into
# a later ask.
fetch() {
    local name="$1" url="$2"
    printf '  %-18s <- %s\n' "$name" "$url"
    curl -fsSL --retry 4 --retry-delay 15 --retry-all-errors "$url" -o "${TMP}/${name}"
}

TMP="$(mktemp -d)"
trap 'rm -rf "${TMP}"' EXIT

echo "fetching:"
fetch cards.min.json  "${DB}/cards.min.json"
fetch rarities.json   "${DB}/rarities.json"
fetch pullRates.json  "${DB}/pullRates.json"
fetch sets.json       "${DB}/sets.json"
fetch expansions.json "${REPO}/expansions.json"
fetch cards.v5.json   "${FACTS}/cards.min.json"

# Every file parses and none is empty, checked before anything is overwritten. A CDN that answers
# 200 with an error page would otherwise replace a working snapshot with rubbish, and the app
# treats these as the thing to fall back TO.
for f in "${TMP}"/*.json; do
    python3 -c "import json,sys; d=json.load(open(sys.argv[1])); sys.exit(0 if d else 1)" "$f" \
        || { echo "error: $(basename "$f") is empty or not JSON" >&2; exit 1; }
done

printf '%s\n' "${VERSION}" > "${TMP}/VERSION"

echo
echo "was $(cat "${DEST}/VERSION" 2>/dev/null || echo '?') -> now ${VERSION}"
before=$(python3 -c "import json;print(len(json.load(open('${DEST}/cards.min.json'))))" 2>/dev/null || echo 0)
after=$(python3 -c "import json;print(len(json.load(open('${TMP}/cards.min.json'))))")
echo "cards $before -> $after"

cp "${TMP}"/* "${DEST}/"
echo
echo "written to ${DEST}"
