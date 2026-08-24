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
- **Expected packs to finish**, converted into calendar time at your own pack rate — for
  example "≈2.6 years, or 1.7 with premium".
- **Target advisor** — every rarity priced side by side, at one copy and at two.
- **Pack points panel** — per-set balances, cap warnings, and a ranking of what points buy.
  Ranked by *packs saved per pack's worth of points*: a Crown returns about 2.5, an Immersive
  0.3.
- Scope it to everything, one set, a whole series, a deck, one wishlist, or all wishlists.

### Collection
Every card in a virtualised grid or a compact list, with counts, filters, search over names,
per-set progress against your target, bulk drag-select and undo/redo.

Ownership is keyed by **artwork**, not by set entry, matching how the game treats it — a card
reprinted in a later set is one card you own.

### Decks
Import a deck by **screenshotting the in-game share code**, decoded in the browser. Or build
one by hand, with live legality checking and a search that covers rules text and card type as
well as names ("what puts something to Sleep", "show me the Supporters"). Export back to a
scannable code the game accepts. Saved decks are ranked closest-to-buildable first.

### Wishlists
Any set of cards you want — chase cards, a binder page, the alternate arts of one Pokémon.
Priced by the same engine as a rarity target. Wishlists want a particular **printing**, unlike
decks: wanting the alternate art is not satisfied by owning the plain one.

### Log a pack
Pick the pack, tap the cards that came out of it, done. Accrues pack points, warns at the cap,
and updates every recommendation immediately.

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
dotnet test                                        # 568 tests
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
application before first paint, document-level hotkeys, tooltips, and a vendored jsQR loaded
only when a screenshot import is attempted.

---

## Roadmap

Phases 1 and 2 are complete. See [CHANGELOG.md](CHANGELOG.md).

- **Phase 3** — shareable read-only snapshots, importing from other trackers, localisation,
  accessibility pass. Budget allocation across packs is done: see "Which pack" &rarr; splitting
  a budget.
- **Phase 4** — screenshot recognition of the in-game card list. Cloud sync last, since it is
  the first thing needing a backend.

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
