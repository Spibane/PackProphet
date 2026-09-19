<h1 align="center">PackProphet</h1>

<h2 align="center"><strong>Collection tracker for Pokémon TCG Pocket — record what you own, say what you want, and see which pack gets you there fastest.</strong></h2>

<div align="center">

[![Version](https://img.shields.io/badge/version-0.8.0-black?style=flat-square)](./CHANGELOG.md)
[![Status](https://img.shields.io/badge/status-Public_test-green?style=flat-square)](https://packprophet.spibane.com/)
[![Changelog](https://img.shields.io/badge/changelog-blue?style=flat-square)](./CHANGELOG.md)

[![.NET](https://img.shields.io/badge/.NET-10.x-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-WebAssembly-512BD4?style=flat-square&logo=blazor)](https://blazor.net/)
[![Storage](https://img.shields.io/badge/localStorage-no_account-003B57?style=flat-square)](#privacy)
[![Sync](https://img.shields.io/badge/sync-end--to--end_encrypted-003B57?style=flat-square)](#cloud-sync)
[![Hosting](https://img.shields.io/badge/GitHub_Pages-static-222222?style=flat-square&logo=github)](https://packprophet.spibane.com/)
[![Licence](https://img.shields.io/badge/AGPL--3.0--or--later-A42E2B?style=flat-square&logo=gnu)](LICENSE)
[![AI](https://img.shields.io/badge/AI-pair-blue?style=flat-square)](AI-DECLARATION.md)

**[packprophet.spibane.com](https://packprophet.spibane.com/)**

</div>

## Architecture

```
Browser
   │
   ▼
Blazor WebAssembly  (.NET 10 — static files, no account, no sign-in)
   │
   ├── /                        Collection (grid or list, counts, filters, undo)
   ├── /progress                Every unfinished set, and what each one still needs
   ├── /packs                   Which pack to open next, against any target
   ├── /log                     Log a pack; accrues pack points
   ├── /chase                   Chase lists, shared entirely inside a link
   ├── /decks                   Deck import, building, legality, QR share codes
   ├── /trades  ·  /board       Trade ranking, and the in-game 20-slot wishlist
   ├── /wonder  ·  /resources   Wonder Pick verdicts; the three currency systems
   └── /collection/screenshot   Read cards off a screenshot of the game
   │
   ├── PackProphet.Core   (pure C#, no UI reference — the compiler enforces it)
   │       ├── Engine     odds, targets, ranking, allocation, trades, wonder picks
   │       ├── Vision     fingerprint matching, screen classification, count reading
   │       ├── Deck       share-code codec, legality linter, QR render
   │       ├── Data       card index, pull rates, rarity ladder, set catalogue
   │       └── State      profiles, undo history, chase-list codec, serialisation
   │
   ├── JS interop  (only what Blazor cannot reach)
   │       └── capped-concurrency image loading, touch drag-select, grid keys,
   │           theme and palette before first paint, tooltips, jsQR, screenshot slotting
   │
   ├── localStorage   collections, chase lists, decks, pack log, settings
   │
   ├── Cloud sync     optional. A 12-character pairing code derives the document id and the
   │       │          AES-GCM key; only the id and a proof token ever leave the browser
   │       └── Supabase   one table, no policies, reachable only through three functions
   │
   └── Card data
           ├── cdn.jsdelivr.net    live card lists and card detail
           └── wwwroot/data/       vendored snapshot + fingerprint table,
                                   so a cold start never boots empty
```

One odds engine drives every screen. The same maths prices a set of rarities, a deck, a whole
series, or a hand-picked chase list.

## Features

- **Every unfinished set on one page** — how far short of your target each one is, what would finish
  it, and the state of all three routes into it: packs, that set's pack points, and how many of the
  missing cards are tradeable. One panel per set, each leading with its own figure, because the
  figures do not add up: a set can be three cards short and four hundred packs away, if those three
  are Crowns. There is deliberately **no total and no finish-everything date** — cards are cards and
  the page counts them, but packs of one set are not packs of another, and the daily allowance is
  shared. Expand a set to see what is left by rarity rung, with the rungs you collect none of drawn
  as gaps rather than zeroes
- **Which pack to open next** — every pack ranked against whatever you are collecting, using
  published pull rates. The chance a pack gives you something you need and the expected packs until
  it does; **where in the pack that chance is**, per card position, since the first three cards come
  from the common pool — a collection that has finished the commons has all of its chance in the
  last two, and the same headline percentage means different things in two sets. Expected packs to
  finish converts into calendar time at your own pack rate ("≈2.6 years, or 1.7 with premium").
  Scope it to everything, one set, a whole series, a deck, one chase list, or all chase lists
- **Target advisor and pack points** — every rarity priced side by side, at one copy and at two;
  per-set point balances, cap warnings, and a ranking of what points buy. Ranked by *packs saved per
  pack's worth of points*: a Crown returns about 2.5, an Immersive 0.3
- **Collection in a virtualised grid or a compact list** — counts, per-set progress against your
  target, bulk drag-select, and undo/redo. Ownership is keyed by **artwork** rather than by set
  entry, matching how the game treats it, so a card reprinted in a later set is one card you own
- **Filters that hold more than one thing at a time** — rarity as the game's own symbols, type as
  the fourteen energy and trainer pips, ownership, and the pack a card comes from, picked by its
  wrapper rather than by name. Whatever is applied shows as a chip on the bar, because a count read
  without knowing it is filtered is a different answer to the one it appears to be
- **A heart on every card** puts it on your **Want it** list in one tap — its own list, so a passing
  heart never rewrites a chase list you built deliberately. A sticky strip names which set you are
  scrolling through once the list spans more than one
- **A tile shows the art and nothing over it** — the count, the card's name and the heart sit in a
  caption under the picture; on a phone the grid keeps the count as a badge and stays as dense as it
  was. A card whose art the CDN has not published yet shows its set and number in the art's place
  rather than a broken-image marker
- **Keyboard throughout** — arrow keys move a cursor, digits set a count outright, `enter` and `-`
  adjust, `shift`+move selects a range, `w` wants a card, `i` opens its page, and `/` jumps to search
- **Import from a screenshot** — screenshot the game and the cards are read off the picture. Nothing
  is uploaded and it works offline: every card's artwork was reduced to 128 bits ahead of time and
  the fingerprints ship with the app, so recognising a card is a distance comparison against a 150 KB
  table rather than a model or a server. See [Reading a screenshot](#reading-a-screenshot) below
- **Decks** — import one by screenshotting the in-game share code, decoded in the browser; or build
  one by hand with live legality checking and a search covering rules text and card type as well as
  names ("what puts something to Sleep", "show me the Supporters"). Export back to a scannable code
  the game accepts. Saved decks are ranked closest-to-buildable first
- **Chase lists** — any set of cards you want: chase cards, a binder page, the alternate arts of one
  Pokémon, priced by the same engine as a rarity target. Chase lists want a particular **printing**,
  unlike decks: wanting the alternate art is not satisfied by owning the plain one
- **Chase lists share as a link**, with nothing uploaded — the list travels entirely inside the link.
  It shows only what is wanted, never your own collection, and if the person opening it has their own
  PackProphet collection loaded it highlights which of it they already own. Or **copy it as a few
  lines of text** for the chat threads these trades actually happen in: what is still short, set and
  number first, with the rarity on every line, since the game's trade rules turn on it
- **Log a pack** — pick the pack, search it by name or tap the cards that came out of it, done. It
  accrues pack points, warns at the cap, and updates every recommendation immediately. Pin the packs
  you open daily and they lead the picker whatever series you are looking at
- **History** — packs over time, what you pulled against what the model predicted, and a showcase of
  your best hits, with the threshold for a "hit" set by you. If you played before finding this app,
  enter the game's own lifetime counters and the odds check scales to them; logged and imported
  figures are kept apart
- **Wonder Pick** — tap the five cards on offer and get a take-or-skip verdict. The cost is set by
  the highest rarity *in the offer* rather than by what the offer is worth to you, so an offer can be
  priced at four stamina because of a 2-star you already own. Those offers are flagged
- **Resources** — the three currency systems side by side: Pack, Wonder and Trade. They are never
  summed, because nothing converts between them. Each panel leads with waste: a full pool has
  *stopped* regenerating, so every 12 hours it sits there is a unit lost, and a pool at two of five
  has 36 hours of slack the panel names
- **Trades** — which single trade deserves your next stamina. Stamina caps trading at about two a day
  while shinedust accumulates, so the ranking treats *trades* as the scarce resource rather than
  currency. A **share?** column marks cards to skip trading for: 1–4 diamond cards can be Shared,
  which costs no dust, no stamina and no card given back
- **Wishlist** — the game's own 20-slot public trade board, filled with the eligible cards
  that cost the most to get any other way. A listing costs nothing but a slot and slots do not
  expire, so low-probability high-value requests are worth listing; a minority of slots is reserved
  for widely-held cards someone is likely to offer. Recommendations come as **swaps** against what is
  already listed, since the board is retyped by hand
- **Multiple collections** — track alt accounts and compare two of them. A trade is a *swap*: both
  sides hand over a card and the rarities must match, so the return card is as much a constraint as
  the wanted one. Diamonds are reported separately, because a **Share** is one-way and free — shares
  are listed first, and trades hold only what a share cannot carry
- **Evolution gaps** — evolutions you own but cannot play, because you do not own what they evolve
  from. Reported per missing card, with the cheapest printing that would fill it. It walks the whole
  chain, so a Stage 2 with neither lower stage reports two missing cards
- **Appearance** — two independent settings. Light, dark or follow-the-OS, and a palette: **Paper**,
  warm neutral surfaces with one accent, or **Slate**, the greys and blues the app shipped with.
  Every skin has a light and a dark form, so choosing one is not also choosing the other
- **Accessibility** — contrast measured live in every skin and theme, names on every control, keyboard
  operation throughout, and a text alternative for the history chart

### Reading a screenshot

Four in-game screens can be read, and the import lives **where the answer is useful** rather than in
one place that then asks what you meant:

| Screen | Where | What it does |
| --- | --- | --- |
| Opening Results | Log a pack | Works out **which pack** from the cards, and fills the log in |
| Wonder Pick | Wonder Pick | Feeds the five cards straight into the appraisal |
| My Cards, five across | Collection | Whole set, blanks for what you are missing &mdash; reads both ways |
| My Cards, three across | Collection | Only what you own, **with how many copies** |

- **The pack identifies itself.** A card lists the packs it can come from, so the packs that could
  have produced a whole hand are the intersection of five short lists. In Genetic Apex, where each
  pack has about 80 exclusive cards against 46 shared, one exclusive card settles it. When the cards
  genuinely do not narrow it &mdash; a God Pack holds only the rarities every pack shares &mdash; the
  shortlist is offered rather than a guess made
- **The two card lists are read differently, and that is a safety rule.** The five-across list draws
  unowned cards as blank slots, so a blank is a card known to be missing and the numbering around it
  gives its name. The three-across list leaves unowned cards out instead, so a gap there means
  nothing at all &mdash; positional reasoning is switched off on it, and it can never report a card
  missing
- **It stops rather than guesses.** Two cards whose artwork is too alike to choose between, a slot
  matching nothing, a list not in number order: each comes back as a slot that was not recognised and
  is left out. Nothing is applied without being shown, marking cards as *not* owned is off until
  asked for, and the whole import is one undo step
- **Only a card's window is fingerprinted** — everything but the outer frame and the bottom sixth —
  because a card on screen is not its artwork file. The game draws a gold flair border over any card
  held ten times or more and prints a copy-count badge across the bottom. Measured on a real
  screenshot, this recognises six of six whole cards at 4 to 12 bits of 128, flair and all; sampling
  the whole card managed one in nine
- **Every box is rebuilt from what all the cards on screen agree about** — the median size, the median
  row and column — because one card's own outline is measured eight pixels differently from its
  neighbour's, and eight pixels is enough to change its fingerprint completely. Cards are found by
  their colour and texture against the page in the first place
- **A card is not always found by looking at it.** A white-bodied card has no colour to catch and only
  sparse text, so the mask misses it entirely — it is read because the other cards in its row fix the
  row's phase and the column pitch says where the remaining slots must be. The fingerprint then
  decides whether a card is there, rather than the mask
- **Nine crops per card, and the most confident one wins.** A card's box is only ever located to
  within a few pixels and the fingerprint has no tolerance for that — three pixels is worth ten bits
  or more — so each card is offered to the matcher nudged three pixels each way. Every crop still has
  to clear the same threshold and margin on its own, so more crops cannot manufacture a confident
  answer
- **Copy counts are read completely or not at all.** The three-across list gives up how many copies
  you hold, read off the badge the game prints on each card. Ten shapes in a fixed-pitch font is a far
  easier problem than card art, and a far less forgiving one — 1 recorded where the badge said 14 is
  silent — so a count is never read in part
- **Fingerprints are the one piece of card data that cannot be newer than the deploy**, since
  generating one means downloading the art. Card lists come live from the CDN, so a set can be
  browsable and unrecognisable at the same time; the page says which sets those are and when the table
  was built. A weekly workflow closes the gap — see
  [Refreshing the fingerprints](#refreshing-the-fingerprints)

> **All four screens read on real screenshots**: both card lists with copy counts, a pack's reveal
> with the pack named from the cards, and a Wonder Pick line-up — five of five on each. One fixture
> still reads nothing, a pack reveal in which *every* card is white-bodied, so no row has anything to
> extend from. The six real fixtures, the measurements, and everything already ruled out are in
> [KNOWN-ISSUES.md](KNOWN-ISSUES.md).

## Privacy

- Everything lives in your browser's `localStorage`. Export a JSON backup from Settings
- Card data comes from public community datasets over a CDN, with a bundled snapshot as a fallback so
  the app never boots empty
- Screenshot import is decoded locally. **No image is ever uploaded**
- The app does not connect to in-game accounts and has no support for doing so
- Cloud sync is **off until you turn it on**, and encrypted on this device when you do. See below

## Cloud Sync

Optional, and off by default. There is no account and no sign-in: one device makes a twelve-character
pairing code, the other types it, and that code is the whole credential.

The code never leaves the browser. It is stretched with PBKDF2 (300,000 iterations, SHA-256) into a
master secret, and three independent HKDF expansions of that master do three different jobs:

| Derived value | Job | Leaves the device? |
| --- | --- | --- |
| `id` | Names the stored document | **Yes** |
| `auth` | Proves you know the code, so an id-guesser cannot overwrite you | **Yes** |
| `key` | AES-256-GCM, encrypts the collection | **Never** |

The host stores ciphertext it has no way to read. That is also the trade: **lose the code and the
stored copy is unrecoverable.** The collection on each paired device is untouched either way, and a
JSON export is still the backup that survives everything.

### Merging, not overwriting

Two devices edited apart have to be reconciled, not ranked. Sync keeps the last state both devices
agreed on and does a three-way merge against it, so "changed here" is distinguishable from "changed
there" — logging packs on a phone and ticking cards on a laptop loses neither.

| | Rule |
| --- | --- |
| Card counts | Whoever changed it wins. Both changed it → the higher count |
| Pack and Wonder logs | Union. Append-only, so an absence is never a deletion |
| Decks, chase lists | Per id. Both edited → this device wins, and says so |
| Resource pools | The more recently read balance, by its own timestamp |
| Everything else | Whoever changed it wins. Both changed it → this device, and it says which |
| Theme, columns, active profile | Never synced — they belong to the device you are holding |

On a **first pair** there is no ancestor, so nothing can be read as a deletion and nothing a device
has never set can outvote a device that has: a blank window joining an established collection adopts
its settings rather than blanking them.

Conflicts are reported in Settings rather than resolved silently, and a merge arrives through the
same path as any other change, so <kbd>Ctrl</kbd>+<kbd>Z</kbd> undoes one.

### Turning it on for your own fork

Sync is disabled unless the build names a Supabase project, which is why a clone of this repository
is local-only out of the box. To enable it:

1. Create a Supabase project and run [`db/sync.sql`](db/sync.sql) in its SQL editor. It creates one
   table with row-level security and **no policies** — PostgREST cannot touch it — plus the three
   `security definer` functions that are the entire API.
2. Set two **repository variables** (Settings → Secrets and variables → Actions → Variables):
   `PACKPROPHET_SYNC_URL` and `PACKPROPHET_SYNC_KEY`. The deploy workflow substitutes both into
   `appsettings.json` and into the `connect-src` in `index.html`, and fails the build if only one of
   them lands. Leave them unset and the deploy ships with sync off.
3. For local `dotnet run`, put the same two values in
   `src/PackProphet.App/wwwroot/appsettings.Development.json`, which is gitignored. Blazor layers it
   over `appsettings.json`.

Variables rather than secrets, deliberately. The browser has to send the anon key to Supabase on
every request, so it is public the moment the site is deployed — it identifies the project and
grants nothing without a pairing code. Filing it as a secret would dress that up as something it is
not. What keeping it out of the tree does buy is a rotation that costs a settings change rather than
a commit, and a git history that never carried it.

The CSP names one exact host rather than `*.supabase.co`, so a tampered card dataset has nowhere to
post to.

## Notices

One bar above every page, for the two cases where the site knows something the pages do not.

**Derived.** The app's own data ages at three rates by design: card data is fetched live, card art
is a manual commit in a second repository, and card detail is a separate 4.4 MB table topped up per
set. So for days after a release the app knows a card exists, cannot draw it, and cannot say what it
does. The bar names the set and which of the two is missing — as one sentence, because a new set is
usually missing both and two notices would mean dismissing one to reveal the other.

If the live database could not be reached at all it says that instead: the visible symptom of
falling back to the bundled snapshot is that the newest set is simply absent, so it is worth stating
rather than leaving to be discovered.

Pull rates deliberately are **not** on this bar, although they lag the same way. An unpriced set is
already solvable in the app — `/packs` offers to borrow the newest measured set's distributions and
every figure follows — so it is a setting, not something anyone is waiting on.

Screenshot recognition is not on it either, for a different reason: deciding whether it applies
means parsing the 150 KB fingerprint table, which is the one piece of card data deliberately kept
off the boot path, and the import page already says so above its own file picker.

Detail coverage is computed on the spot. Art coverage cannot be — the app cannot see a missing image
without requesting it, and probing 3,879 of them to decide whether to show one sentence is absurd —
so `tools/vendor-gap-art.py` writes what it found into `art/index.json` during the deploy, which is
the one place that already knows. That listing asks the art repository for a set's *directory*, so a
set it has started and not finished looks complete: this under-reports and never over-reports, which
is the right direction for a claim made on every page.

**Authored, for the rest.** An outage, a feature that has started failing, or a pack that launched
before the card database published it — the last of which nothing derived can see, since the app
cannot know about a set it has never heard of.

Set the repository variable `PACKPROPHET_NOTICE_GIST` to a gist's **raw URL without the revision
SHA** (`https://gist.githubusercontent.com/<you>/<id>/raw/notice.json`). The deploy workflow
substitutes it into `appsettings.json` and refuses a URL carrying a SHA, which would serve one
revision forever. Unset, no request is made. For local `dotnet run`, put it in the gitignored
`appsettings.Development.json`.

A gist rather than a file in this repository because the service worker precaches every `.json` in
the published output and then serves it cache-first: a committed notice would be frozen at whichever
build the visitor installed and could never announce anything.

The gist holds a list, even for one notice:

```json
[
  {
    "id": "2026-09-18-b5",
    "level": "info",
    "text": "B5 launches today. The card database has not published it yet.",
    "until": "2026-09-25",
    "actionLabel": "Read More",
    "actionHref": "https://example.com/notes"
  }
]
```

`id` is required and is what a dismissal is remembered against — reuse it to edit a notice, change
it to post a new one. `level` is `info`, `warning` or `problem`; anything unrecognised reads as
`info`, so a typo there still shows the notice. `until` is an inclusive `yyyy-MM-dd` and optional,
but a date that cannot be parsed **drops** the notice: that field is the only thing that makes one
stop on its own. The action is both halves or neither, and the href must be an absolute `https` URL
or a plain in-app path.

This feed is the only text in the app that neither the build nor the user wrote, so it is treated
that way. Every check is in `NoticeFeedReader` with an assertion against it, and every ambiguity
resolves to showing nothing.

**One notice at a time.** Two rows above every page is two rows on every page, and a reader told
two things at once acts on neither, so the most severe wins and an authored notice breaks a tie.
Dismissing is per subject rather than per feature — hiding one release's notice leaves the next
one's free to appear — and only a `problem` interrupts a screen reader.

## Package Structure

```
PackProphet/
├── src/
│   ├── PackProphet.Core/          # Engine and domain. No UI reference; warnings are errors
│   │   ├── Data/                  # Card index, facts, pull rates, rarity ladder, sets, notices
│   │   ├── Deck/                  # Share-code codec, legality linter, QR render
│   │   ├── Domain/                # Cards, rarities, pack variants, game rules
│   │   ├── Engine/                # Odds, targets, ranking, allocation, trades, wonder picks
│   │   ├── Import/                # CSV/XLSX readers, tracker import and export
│   │   ├── State/                 # Profiles, undo history, chase-list codec, serialisation
│   │   ├── Sync/                  # Pairing code, three-way state merge
│   │   └── Vision/                # Fingerprints, screen classification, count reading
│   └── PackProphet.App/           # Blazor WebAssembly
│       ├── Components/            # Card grid, tile, picker, palette, shared controls
│       ├── Pages/                 # One per route (see URLs below)
│       ├── Services/              # Session, data loader, storage, sync, notices, scanning, focus
│       └── wwwroot/
│           ├── js/                # Only what Blazor cannot reach (see Architecture)
│           └── data/              # Vendored snapshot + card-hashes.txt fingerprint table
├── tests/
│   ├── PackProphet.Tests/         # Engine suite, no browser — runs in about a second
│   └── PackProphet.App.Tests/     # Page renders under bUnit, against the real snapshot
├── tools/CardHashGen/             # Fingerprint generator (deliberately outside the solution)
├── .github/workflows/             # deploy-pages.yml, card-hashes.yml
└── PackProphet.slnx
```

## Run

Requires the **.NET 10 SDK** and the `wasm-tools` workload. On Fedora (including a distrobox
container) you also need `libatomic`, which emscripten's bundled node links against.

```bash
dotnet workload install wasm-tools

dotnet run --project src/PackProphet.App          # http://localhost:5000
dotnet test                                        # 961 tests
```

`InvariantGlobalization` is on in Debug as well as Release. It changes string comparison and casing,
and card names are sorted and searched, so a Debug-only difference would mean search behaving
differently in production. The cost is a native relink on every build.

> The dev server captures its static-asset manifest at startup, so **restart it after a rebuild** —
> otherwise the newly fingerprinted `_framework` files 404.

## URLs

| Path | Description |
|------|-------------|
| `/` , `/collection` | Collection grid or list, with filters, counts and undo |
| `/collection/screenshot` | Read cards off a screenshot of the game |
| `/collection/import`, `/collection/export` | Import from another tracker; export a backup |
| `/card/{key}` | One card: printings, cheapest route, packs that can give it |
| `/progress` | Every unfinished set: what it still needs, and where it comes from |
| `/packs` | Which pack to open next, with the target advisor and points panel |
| `/log` | Log a pack |
| `/history` | Packs over time, predicted against actual, and your best hits |
| `/chase`, `/chase/{id}` | Chase lists, and one chase list's own page |
| `/share` | A chase list opened from a share link (read-only) |
| `/decks`, `/decks/new`, `/decks/{id}` | Saved decks, and the builder |
| `/decks/import` | Import a deck from an in-game share code |
| `/trades` | Which trade deserves your next stamina |
| `/board` | The in-game 20-slot wishlist (the public trade board) |
| `/wonder` | Wonder Pick take-or-skip verdicts |
| `/resources` | Pack, Wonder and Trade currencies side by side |
| `/compare` | Compare two collections; shares and swaps between them |
| `/settings` | Targets, profiles, backup and restore |

## Tests

Two suites, and `dotnet test` runs both. CI runs them before it will deploy.

- `tests/PackProphet.Tests` — the engine. No UI, no browser, runs in about a second
- `tests/PackProphet.App.Tests` — the pages, rendered with bUnit against the vendored data snapshot
  served over a fake `HttpClient`. Every routable page is discovered by **reflection** and rendered
  twice: on a brand-new profile and on a populated one. The empty-profile case covers pages that
  index a list with nothing in it, which compiles cleanly and passes every engine test

Each regression test was checked against the defect it describes: the fix reverted, the suite
confirmed red, the fix restored.

## Deployment (GitHub Pages)

`.github/workflows/deploy-pages.yml` publishes on a push to `main`. Enable Pages for the repository
with **GitHub Actions** as the source; nothing else is needed.

The workflow handles three Blazor-on-Pages traps, each of which fails silently otherwise:
`.nojekyll` (Jekyll drops `_framework`), `404.html` (no SPA rewrite, so deep links 404), and
rewriting `<base href>` **before** publish rather than after — the service worker pins `index.html`
by SHA-256, so a later edit makes its install fail and offline support never happens. A build step
re-hashes every precached asset and fails the deploy on a mismatch.

### Refreshing the fingerprints

`.github/workflows/card-hashes.yml` runs weekly, looks for cards the fingerprint table has never
seen, downloads only those, and opens a pull request if it found any. The pull request body lists
every set the table still does not cover completely, with counts — a set far short of its card count
is artwork upstream has not published yet, and a later run picks it up.

It runs `tools/vendor-gap-art.py` first and hands the generator the directory, because the only set
the job has work to do for is the newest one and that is reliably the set the art CDN has **not**
published — card data ships in days, art is a manual commit weeks later. Without it B4a sat at 0 of
110 fingerprinted and 0 of 110 typed while every other set was complete, with its art available the
whole time in the release archive the deploy already reads.

`tools/CardHashGen` is the generator behind it, and is deliberately **absent from
`PackProphet.slnx`**: it needs a native WebP decoder, and `dotnet test` resolves the solution, so
including it would put SkiaSharp on the deploy path for no reason. Run it by path.

```bash
# everything, from scratch — about 3,700 downloads
dotnet run --project tools/CardHashGen

# only cards with no fingerprint yet, which is what CI does
dotnet run --project tools/CardHashGen -- --only-missing

# one set, to a scratch file
dotnet run --project tools/CardHashGen -- --set A1a --out /tmp/hashes.txt

# read art from a directory instead of the CDN, for a set the CDN does not have
tools/vendor-gap-art.py --out /tmp/gap-art
dotnet run --project tools/CardHashGen -- --art-dir /tmp/gap-art --only-missing
```

A run merges rather than replaces: art that 404s today must not remove a card the app can currently
recognise. A run that fetches less than two thirds of what the table already held is treated as an
outage and writes nothing.

### Other notes

- **The maths** — a card's per-pack arrival rate is summed across pack variants and slots, then
  expected packs to finish a target is a Poissonised integral,
  `E = ∫₀^∞ (1 − Π_u P(Poisson(λ_u·t) ≥ k_u)) dt`, numerically integrated. For single-copy demand it
  collapses to the familiar `1 − e^{−λt}` form; multi-copy demand is the same integral rather than a
  separate algorithm, which is what lets a deck ask for two Pikachu ex
- **Where the JavaScript line falls** — `js/cardshot.js` locates a repeating grid of card-shaped
  regions and measures each one; it does not know what a card is. Which card a slot holds, whether it
  is owned, which in-game screen this is and what to tell the user are all decided in
  `PackProphet.Core.Vision`, where they are asserted from hand-written measurements with no browser
  and no image. The fingerprint itself is computed on both sides — in Core for the generated table, in
  JavaScript for the screenshot — and the two are pinned to a shared golden vector, because a hash
  computed a different way is not a near miss, it is a different card
- **Bootstrap is vendored as exactly the two files `index.html` links.** The Blazor.Bootstrap
  component package was dropped and nothing replaced it

## Known issues

Open problems, each with what has already been ruled out, live in
[KNOWN-ISSUES.md](KNOWN-ISSUES.md) so a second attempt at one starts where the first stopped.

## Roadmap

Phases 1 and 2 are complete. See [CHANGELOG.md](CHANGELOG.md).

- **Phase 3** — localisation remains. Done: an accessibility pass (contrast measured in both themes,
  names on every control, a text alternative for the chart), importing a collection from another
  tracker and exporting it again (Settings &rarr; import/export, or `/collection/import`), budget
  allocation across packs ("Which pack" &rarr; splitting a budget), and shareable read-only chase-list
  links (a chase list's own page &rarr; share this list) — the recipient sees what is wanted and, if
  they have their own collection loaded, which of it they already own
- **Phase 4** — under way ahead of Phase 3's remaining localisation, since it is the larger feature.
  Done: screenshot recognition of the card list, a pack's five cards and a Wonder Pick line-up
  (Collection &rarr; import from a screenshot, or `/collection/screenshot`), with the fingerprint
  table refreshed weekly by a workflow. The match thresholds are set from the shape of the problem and
  from synthetic art rather than from a corpus of real screenshots, so they are the first thing to
  revisit once there is one. Cloud sync last, since it is the first thing needing a backend

Out of scope: meta tier lists, matchup data, tournament results, a battle simulator, a trade
marketplace, accounts and social features, collection value scores.

## Licence and data

**AGPL-3.0-or-later** — see [LICENSE](LICENSE).

The licence follows the data: the only community dataset carrying attack text, ability text and
evolution stage for every set is AGPL.

Data sources, the reverse-engineered deck format, and bundled third-party code are credited in
[NOTICE.md](NOTICE.md). Pull rates come from
[flibustier/pokemon-tcg-pocket-database](https://github.com/flibustier/pokemon-tcg-pocket-database)
and are published nowhere else; card detail from
[chase-mew/pokemon-tcg-pocket-cards](https://github.com/chase-mew/pokemon-tcg-pocket-cards).

How much of this was written with an AI assistant, and which parts were not, is stated in
[AI-DECLARATION.md](AI-DECLARATION.md).

Pokémon and Pokémon TCG Pocket are trademarks of Nintendo, Creatures Inc. and GAME FREAK inc.
This is an unofficial fan tool, unaffiliated with and unendorsed by any of them.
