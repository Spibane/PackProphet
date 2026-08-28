# PackProphet

**[spibane.github.io/PackProphet](https://spibane.github.io/PackProphet/)**

A collection tracker for **Pokémon TCG Pocket** built around one question:

> **Which pack should I open next?**

Record what you own, say what you want, see which pack gets you there fastest, log what you
opened, repeat.

It runs entirely in your browser. There is no account, no server, and nothing is uploaded.

One odds engine drives every screen. The same maths prices a set of rarities, a deck, a whole
series, or a hand-picked wishlist.

---

## What it does

### Which pack
Every pack ranked against whatever you are collecting, using published pull rates.

- **Chance a pack gives you something you need**, and expected packs until it does.
- **Where in the pack that chance is** — per card position, since the first three cards come from
  the common pool: a collection that has finished the commons has all of its chance in the last two,
  and the same headline percentage means different things in two sets.
- **Expected packs to finish**, converted into calendar time at your own pack rate — for
  example "≈2.6 years, or 1.7 with premium".
- **Target advisor** — every rarity priced side by side, at one copy and at two.
- **Pack points panel** — per-set balances, cap warnings, and a ranking of what points buy.
  Ranked by *packs saved per pack's worth of points*: a Crown returns about 2.5, an Immersive
  0.3.
- Scope it to everything, one set, a whole series, a deck, one wishlist, or all wishlists.

### Collection
Every card in a virtualised grid or a compact list, with counts, per-set progress against your
target, bulk drag-select and undo/redo.

- **Filters that hold more than one thing at a time** — rarity as the game's own symbols, type as
  the fourteen energy and trainer pips, ownership, and the pack a card comes from, picked by its
  wrapper rather than by name. Whatever is applied is shown as a chip on the bar, because a count
  read without knowing it is filtered is a different answer to the one it appears to be.
- **A heart on every card** puts it on your **Want it** list in one tap — its own list, so a
  passing heart never rewrites a wishlist you built deliberately.
- **Which set you are scrolling through**, on a sticky strip with the set's logo, once the list
  spans more than one.
- **A tile shows the art and nothing over it.** The count, the card's name and the heart sit in a
  caption under the picture; on a phone the grid keeps the count as a badge and stays as dense as it
  was.
- **Keyboard throughout**: arrow keys move a cursor, digits set a count outright, `enter` and `-`
  adjust, `shift`+move selects a range, `w` wants a card, `i` opens its page.

Ownership is keyed by **artwork**, not by set entry, matching how the game treats it — a card
reprinted in a later set is one card you own.

### Import from a screenshot
Screenshot the game and the cards are read off the picture. Nothing is uploaded and it works
offline: every card's artwork was reduced to 128 bits offline and the fingerprints ship with the
app, so recognising a card is a distance comparison against a 150 KB table rather than a model or a
server.

Four in-game screens can be read, and the import lives **where the answer is useful** rather than
in one place that then asks what you meant:

| Screen | Where | What it does |
| --- | --- | --- |
| Opening Results | [Log a pack](#log-a-pack) | Works out **which pack** from the cards, and fills the log in |
| Wonder Pick | [Wonder Pick](#wonder-pick) | Feeds the five cards straight into the appraisal |
| My Cards, five across | Collection | Whole set, blanks for what you are missing &mdash; reads both ways |
| My Cards, three across | Collection | Only what you own, **with how many copies** |

- **The pack identifies itself.** A card lists the packs it can come from, so the packs that could
  have produced a whole hand are the intersection of five short lists. In Genetic Apex, where each
  pack has about 80 exclusive cards against 46 shared, one exclusive card settles it. When the cards
  genuinely do not narrow it &mdash; a God Pack holds only the rarities every pack shares &mdash; the
  shortlist is offered rather than a guess made.
- **The two card lists are read differently, and that is a safety rule.** The five-across list draws
  unowned cards as blank slots, so a blank is a card known to be missing and the numbering around it
  gives its name. The three-across list leaves unowned cards out instead, so a gap there means
  nothing at all &mdash; positional reasoning is switched off on it, and it can never report a card
  missing.
- **It stops rather than guesses.** Two cards whose artwork is too alike to choose between, a slot
  matching nothing, a list not in number order: each comes back as a slot that was not recognised
  and is left out.
- **Nothing is applied without being shown.** Marking cards as *not* owned is off until asked for,
  and the whole import is one undo step.

Fingerprints are the one piece of card data that cannot be newer than the deploy, since generating
one means downloading the art. Card lists come live from the CDN, so a set can be browsable and
unrecognisable at the same time &mdash; the page says which sets those are and when the table was
built. A weekly workflow closes the gap; see [Refreshing the fingerprints](#refreshing-the-fingerprints).

Only a card's **window** is fingerprinted — everything but the outer frame and the bottom sixth —
because a card on screen is not its artwork file. The game draws a gold flair border over any card
held ten times or more and prints a copy-count badge across the bottom. Measured on a real
screenshot, this recognises six of six whole cards at 4 to 12 bits of 128, flair and all; sampling
the whole card managed one in nine.

Cards are found by their colour and texture against the page, and then every box is rebuilt from
what all the cards on screen **agree** about — the median size, the median row and column — because
one card's own outline is measured eight pixels differently from its neighbour's, and eight pixels is
enough to change its fingerprint completely.

The three-across list also gives up **how many copies** you hold, read off the badge the game prints
on each card. Ten shapes in a fixed-pitch font is a far easier problem than card art, and a far less
forgiving one — 1 recorded where the badge said 14 is silent — so a count is read completely or
reported as unknown, never in part.

A card's box is only ever located to within a few pixels, and the fingerprint has no tolerance for
that — three pixels is worth ten bits or more. So each card is offered to the matcher as nine crops,
nudged three pixels each way, and the most confidently identified one wins. Every crop still has to
clear the same threshold and margin on its own, so more crops cannot manufacture a confident answer.

A card is not always found by looking at it. A white-bodied card has no colour to catch and only
sparse text, so the mask misses it entirely — it is read because the other cards in its row fix the
row's phase and the column pitch says where the remaining slots must be. The fingerprint then decides
whether a card is there, rather than the mask.

> **All four screens read on real screenshots**: both card lists with copy counts, a pack's reveal
> with the pack named from the cards, and a Wonder Pick line-up — five of five on each. One fixture
> still reads nothing, a pack reveal in which *every* card is white-bodied, so no row has anything to
> extend from. The six real fixtures, the measurements, and everything already ruled out are in
> [KNOWN-ISSUES.md](KNOWN-ISSUES.md).

### Decks
Import a deck by **screenshotting the in-game share code**, decoded in the browser. Or build
one by hand, with live legality checking and a search that covers rules text and card type as
well as names ("what puts something to Sleep", "show me the Supporters"). Export back to a
scannable code the game accepts. Saved decks are ranked closest-to-buildable first.

### Wishlists
Any set of cards you want — chase cards, a binder page, the alternate arts of one Pokémon.
Priced by the same engine as a rarity target. Wishlists want a particular **printing**, unlike
decks: wanting the alternate art is not satisfied by owning the plain one.

Share one as a link — nothing is uploaded, the list travels entirely inside the link. It shows
only what is wanted, never your own collection, and if the person opening it has their own
PackProphet collection loaded, it highlights which of it they already own a copy of.

Or **copy it as a few lines of text**, for the chat threads these trades actually happen in: what
is still short, set and number first, with the rarity on every line — the game's trade rules turn
on it.

### Log a pack
Pick the pack, search it by name or tap the cards that came out of it, done. Accrues pack
points, warns at the cap, and updates every recommendation immediately. Pin the packs you open
daily and they lead the picker whatever series you are looking at.

### History
Packs over time, what you pulled against what the model predicted, and a showcase of your best
hits — with the threshold for a "hit" set by you. If you played before finding this app, enter
the game's own lifetime counters and the odds check will scale to them; logged and imported
figures are kept apart.

### Wonder Pick
Tap the five cards on offer and get a take-or-skip verdict. The cost is set by the highest
rarity *in the offer* rather than by what the offer is worth to you, so an offer can be priced
at four stamina because of a 2-star you already own. Those offers are flagged.

### Resources
The three currency systems side by side — Pack, Wonder and Trade. They are never summed,
because nothing converts between them. Each panel leads with waste: a full pool has *stopped*
regenerating, so every 12 hours it sits there is a unit lost. A pool at two of five has 36
hours of slack, and the panel says so.

### Trades
Which single trade deserves your next stamina. Stamina caps trading at about two a day while
shinedust accumulates, so the ranking treats *trades* as the scarce resource rather than
currency. A **share?** column marks cards to skip trading for: 1–4 diamond cards can be Shared,
which costs no dust, no stamina and no card given back.

### In-game wishlist
The game's own 20-slot public trade board, filled with the eligible cards that cost the most to
get any other way. A listing costs nothing but a slot and slots do not expire, so
low-probability high-value requests are worth listing; a minority of slots is reserved for
widely-held cards that someone is likely to offer. Recommendations come as **swaps** against
what is already listed, since the board is retyped by hand.

### Multiple collections
Track alt accounts and compare two collections. A trade is a *swap*: both sides hand over a
card and the rarities must match, so the return card is as much a constraint as the wanted one.
Diamonds are reported separately, because a **Share** is one-way and free — shares are listed
first, and trades hold only what a share cannot carry.

### Evolution gaps
Evolutions you own but cannot play, because you do not own what they evolve from. Reported per
missing card, with the cheapest printing that would fill it. It walks the whole chain, so a
Stage 2 with neither lower stage reports two missing cards.

---

## Privacy

- Everything lives in your browser's `localStorage`. Export a JSON backup from Settings.
- Card data comes from public community datasets over a CDN, with a bundled snapshot as a
  fallback so the app never boots empty.
- Screenshot import is decoded locally. **No image is ever uploaded.**
- The app does not connect to in-game accounts and has no support for doing so.

---

## Tests

Two suites:

- `tests/PackProphet.Tests` — the engine. No UI, no browser, runs in about a second.
- `tests/PackProphet.App.Tests` — the pages, rendered with bUnit against the vendored data
  snapshot served over a fake `HttpClient`. Every routable page is discovered by **reflection**
  and rendered twice: on a brand-new profile and on a populated one. The empty-profile case
  covers pages that index a list with nothing in it, which compiles cleanly and passes every
  engine test.

`dotnet test` runs both, and CI runs them before it will deploy.

Each regression test was checked against the defect it describes: the fix reverted, the suite
confirmed red, the fix restored.

## Running it

Needs the **.NET 10 SDK** and the `wasm-tools` workload. On Fedora (including a distrobox
container) you also need `libatomic`, which emscripten's bundled node links against.

```bash
dotnet workload install wasm-tools

dotnet run --project src/PackProphet.App          # http://localhost:5000
dotnet test                                        # 946 tests
```

`InvariantGlobalization` is on in Debug as well as Release. It changes string comparison and
casing, and card names are sorted and searched, so a Debug-only difference would mean search
behaving differently in production. The cost is a native relink on every build.

> The dev server captures its static-asset manifest at startup, so **restart it after a
> rebuild** — otherwise the newly fingerprinted `_framework` files 404.

### Deploying

`.github/workflows/deploy-pages.yml` publishes to GitHub Pages on a push to `main`. Enable
Pages for the repository with **GitHub Actions** as the source; nothing else is needed.

The workflow handles three Blazor-on-Pages traps, each of which fails silently otherwise:
`.nojekyll` (Jekyll drops `_framework`), `404.html` (no SPA rewrite, so deep links 404), and
rewriting `<base href>` **before** publish rather than after — the service worker pins
`index.html` by SHA-256, so a later edit makes its install fail and offline support never
happens. A build step re-hashes every precached asset and fails the deploy on a mismatch.

### Refreshing the fingerprints

`.github/workflows/card-hashes.yml` runs weekly, looks for cards the fingerprint table has never
seen, downloads only those, and opens a pull request if it found any. The pull request body lists
every set the table still does not cover completely, with counts — a set far short of its card count
is artwork upstream has not published yet, and a later run picks it up.

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
```

A run merges rather than replaces: art that 404s today must not remove a card the app can currently
recognise. A run that fetches less than two thirds of what the table already held is treated as an
outage and writes nothing.

---

## How it is built

| | |
| --- | --- |
| **PackProphet.Core** | Pure C#, no UI. Data layer, game rules, odds engine, targets, deck codec and linter. Warnings are errors. |
| **PackProphet.App** | Blazor WebAssembly + Blazor Bootstrap. |
| **PackProphet.Tests** | xUnit, run against the same vendored data snapshot the app ships. |

The maths: a card's per-pack arrival rate is summed across pack variants and slots, then
expected packs to finish a target is a Poissonised integral,
`E = ∫₀^∞ (1 − Π_u P(Poisson(λ_u·t) ≥ k_u)) dt`, numerically integrated. For single-copy demand
it collapses to the familiar `1 − e^{−λt}` form; multi-copy demand is the same integral rather
than a separate algorithm, which is what lets a deck ask for two Pikachu ex.

JavaScript covers what Blazor cannot reach: capped-concurrency image loading (native
`loading="lazy"` queued thousands of requests on a fast scroll and the CDN dropped the
connection), touch drag-select (touch's implicit pointer capture makes per-tile `pointerenter`
useless), grid keyboard navigation (selective `preventDefault` on the scroller), theme
application before first paint, document-level hotkeys, tooltips, a vendored jsQR loaded only when
a deck code is scanned, and finding the card slots in a screenshot.

That last one is the sharpest example of where the line falls. `js/cardshot.js` locates a repeating
grid of card-shaped regions and measures each one; it does not know what a card is. Which card a
slot holds, whether it is owned, which in-game screen this is and what to tell the user are all
decided in `PackProphet.Core.Vision`, where they are asserted from hand-written measurements with no
browser and no image. The fingerprint itself is computed on both sides — in Core for the generated
table, in JavaScript for the screenshot — and the two are pinned to a shared golden vector, because
a hash computed a different way is not a near miss, it is a different card.

---

## Known issues

Open problems, each with what has already been ruled out, live in
[KNOWN-ISSUES.md](KNOWN-ISSUES.md) so a second attempt at one starts where the first stopped.

## Roadmap

Phases 1 and 2 are complete. See [CHANGELOG.md](CHANGELOG.md).

- **Phase 3** — localisation remains. Done: an accessibility pass (contrast measured in both
  themes, names on every control, a text alternative for the chart), importing a collection from
  another tracker and exporting it again (Settings &rarr; import/export, or `/collection/import`),
  budget allocation across packs ("Which pack" &rarr; splitting a budget), and shareable
  read-only wishlist links (a wishlist's own page &rarr; share this list) — the recipient sees
  what is wanted and, if they have their own collection loaded, which of it they already own.
- **Phase 4** — under way ahead of Phase 3's remaining localisation, since it is the larger
  feature. Done: screenshot recognition of the card list, a pack's five cards and a Wonder Pick
  line-up (Collection &rarr; import from a screenshot, or `/collection/screenshot`), with the
  fingerprint table refreshed weekly by a workflow. The match thresholds are set from the shape of
  the problem and from synthetic art rather than from a corpus of real screenshots, so they are the
  first thing to revisit once there is one. Cloud sync last, since it is the first thing needing a
  backend.

Out of scope: meta tier lists, matchup data, tournament results, a battle simulator, a trade
marketplace, accounts and social features, collection value scores.

---

## Licence and data

**AGPL-3.0-or-later** — see [LICENSE](LICENSE).

The licence follows the data: the only community dataset carrying attack text, ability text and
evolution stage for every set is AGPL.

Data sources, the reverse-engineered deck format, and bundled third-party code are credited in
[NOTICE.md](NOTICE.md). Pull rates come from
[flibustier/pokemon-tcg-pocket-database](https://github.com/flibustier/pokemon-tcg-pocket-database)
and are published nowhere else; card detail from
[chase-mew/pokemon-tcg-pocket-cards](https://github.com/chase-mew/pokemon-tcg-pocket-cards).

Pokémon and Pokémon TCG Pocket are trademarks of Nintendo, Creatures Inc. and GAME FREAK inc.
This is an unofficial fan tool, unaffiliated with and unendorsed by any of them.
