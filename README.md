# PackProphet

**[spibane.github.io/PackProphet](https://spibane.github.io/PackProphet/)**

A collection tracker for **Pokémon TCG Pocket** built around one question:

> **Which pack should I open next?**

Everything else follows from that loop — record what you own, say what you want, get told
which pack gets you there fastest, log what you opened, repeat.

It runs entirely in your browser. There is no account, no server, and nothing is uploaded.

**Simple but versatile** is the design constraint, and the versatility comes from one engine
pointed at different targets rather than from more screens. The same odds maths prices a set of
rarities, a deck, a whole series, or a hand-picked wishlist.

---

## What it does

### Which pack
Every pack ranked against whatever you are collecting, with real probabilities from published
pull rates — not heuristics.

- **Chance a pack gives you something you need**, and expected packs until it does.
- **Expected packs to finish**, converted into calendar time at your own pack rate. "1,900
  packs" is abstract; "≈2.6 years, or 1.7 with premium" changes decisions.
- **Target advisor** — every rarity priced side by side, at one copy and at two. People set
  impossible targets because nobody ever priced them.
- **Pack points panel** — per-set balances, cap warnings, and where points are actually worth
  spending. Ranked by *packs saved per pack's worth of points*, not by price: a Crown returns
  about 2.5, an Immersive 0.3, so ranking by price alone recommends the worst purchases.
- Scope it to everything, one set, a whole series, a deck, one wishlist, or all wishlists.

### Collection
Every card in a virtualised grid or a compact list, with counts, filters, search over names,
per-set progress against your target, bulk drag-select and undo/redo.

Ownership is keyed by **artwork**, not by set entry, because that is how the game treats it —
a card reprinted in a later set is one card you own, not two you have to chase.

### Decks
Import a deck by **screenshotting the in-game share code** — decoded in the browser, no upload.
Or build one by hand, with live legality checking and a search that covers rules text and card
type, not just names ("what puts something to Sleep", "show me the Supporters"). Export back to
a scannable code the game accepts. Saved decks are ranked closest-to-buildable first.

### Wishlists
Any set of cards you want, for any reason — chase cards, a binder page, the alternate arts of
one Pokémon. Priced by exactly the same engine as a rarity target. Wishlists want a particular
**printing**, unlike decks: wanting the alternate art is not satisfied by owning the plain one.

### Log a pack
The daily interaction. Pick the pack, tap the cards that came out of it, done. Accrues pack
points, warns at the cap, and updates every recommendation immediately.

### History
Packs over time, what you pulled against what the model predicted, and a showcase of your best
hits — with what counts as a "hit" being your call, because a 1-star is a good day to one
player and noise to someone opening thirty packs a week. If you played before finding this app,
enter the game's own lifetime counters and the odds check will scale to them, with logged and
imported figures always kept apart.

### Wonder Pick
Tap the five cards on offer and get a take-or-skip verdict. The cost is set by the highest
rarity *in the offer*, not by what it is worth to you, so an offer priced at four stamina
because of a 2-star you already own is expensive for reasons that do not benefit you. That is
the flag worth having, and it is sharpest at the top of the price range.

### Resources
The three currency systems side by side — Pack, Wonder and Trade — and never summed, because
nothing converts between them. The number that changes behaviour is the waste: a full pool has
*stopped* regenerating, so every 12 hours it sits there is a unit nobody gets back. A pool at
two of five, by contrast, has 36 hours of slack, and the app says so rather than nagging.

### Trades
Which single trade deserves your next stamina. Stamina caps trading at about two a day while
shinedust merely accumulates, so the scarce thing is *trades*, not currency — and that inverts
the usual question from "can I afford this?" to "which one is worth the slot?". A **share?**
column says when to skip the trade entirely: 1–4 diamond cards can be Shared, which costs no
dust, no stamina and no card given back.

### In-game wishlist
The game's own 20-slot public trade board, filled with the eligible cards that cost the most to
get any other way. A listing costs nothing but a slot and slots do not expire, so a
low-probability high-value request is a free lottery ticket — but a board where *nothing* will
ever be offered converts nothing, so a minority of slots is reserved for widely-held cards.
Recommendations come as **swaps** against what is already listed, since the board is retyped by
hand.

### Multiple collections
Alt accounts are ordinary, and you can trade with yourself. Comparing two collections is not a
set difference, because a trade is a *swap*: both sides hand over a card and the rarities must
match, so the return card is as much a constraint as the wanted one. Diamonds skip that
entirely — a **Share** is one-way and free — so the two mechanics are reported separately, with
shares first and trades left holding only what a share cannot carry.

### Evolution gaps
Evolutions you own but cannot play, because you do not own what they evolve from. The grid
cannot show this: a Charizard sits there looking like an asset until you try to build a deck.
Reported per missing card, with the cheapest printing that would fill it — and it walks the
whole chain, so a Stage 2 with neither lower stage says you are two cards away, not one.

---

## Privacy

- Everything lives in your browser's `localStorage`. Export a JSON backup from Settings.
- Card data comes from public community datasets over a CDN, with a bundled snapshot as a
  fallback so the app never boots empty.
- Screenshot import is decoded locally. **No image is ever uploaded.**

**Screenshot-based import will never become account linking.** At least one competing tracker
offers "no manual input" by connecting to your in-game data, which means handing a third-party
server your credentials or a device token — against the game's terms, a ban vector, and a
breach waiting to happen. Reading a screenshot reaches the same result with no credentials and
no network round trip. That is a feature, not a limitation.

---

## Tests

Two suites, and the split is deliberate.

- `tests/PackProphet.Tests` — the engine. No UI, no browser, runs in about a second.
- `tests/PackProphet.App.Tests` — the pages, rendered with bUnit against the vendored data
  snapshot served over a fake `HttpClient`. Every routable page is discovered by **reflection**
  and rendered twice: on a brand-new profile and on a populated one. The empty case is the
  important half — it is the state every user starts in, and the one where a page indexes a list
  that has nothing in it, which compiles cleanly and passes every engine test.

`dotnet test` runs both, and CI runs them before it will deploy.

Each regression test here was checked against the defect it describes: the fix reverted, the suite
confirmed red, the fix restored. A test that passes on broken code is worse than no test, because
it gets counted as coverage.

## Running it

Needs the **.NET 10 SDK** and the `wasm-tools` workload. On Fedora (including a distrobox
container) you also need `libatomic`, which emscripten's bundled node links against.

```bash
dotnet workload install wasm-tools

dotnet run --project src/PackProphet.App          # http://localhost:5000
dotnet test                                        # 281 tests
```

`InvariantGlobalization` is on in Debug as well as Release, deliberately: it changes string
comparison and casing, and card names are sorted and searched, so a Debug-only difference would
mean search behaving differently in production. The cost is a native relink on every build.

> The dev server captures its static-asset manifest at startup, so **restart it after a
> rebuild** — otherwise the newly fingerprinted `_framework` files 404.

### Deploying

`.github/workflows/deploy-pages.yml` publishes to GitHub Pages on a push to `main`. Enable
Pages for the repository with **GitHub Actions** as the source, and it needs nothing else.

Three Blazor-on-Pages traps are handled there, each of which fails silently otherwise:
`.nojekyll` (Jekyll drops `_framework`), `404.html` (no SPA rewrite, so deep links 404), and
rewriting `<base href>` **before** publish rather than after — the service worker pins
`index.html` by SHA-256, so a later edit makes its install fail and offline support quietly
never happens. A build step re-hashes every precached asset and fails the deploy on a mismatch.

---

## How it is built

| | |
| --- | --- |
| **PackProphet.Core** | Pure C#, no UI. Data layer, game rules, odds engine, targets, deck codec and linter. Warnings are errors. |
| **PackProphet.App** | Blazor WebAssembly + Blazor Bootstrap. |
| **PackProphet.Tests** | xUnit, run against the same vendored data snapshot the app ships. |

The maths, for the curious: a card's per-pack arrival rate is summed across pack variants and
slots, then expected packs to finish a target is a Poissonised integral,
`E = ∫₀^∞ (1 − Π_u P(Poisson(λ_u·t) ≥ k_u)) dt`, numerically integrated. For single-copy demand
it collapses to the `1 − e^{−λt}` form every other tracker uses; multi-copy demand is the same
integral rather than a different algorithm, which is what lets a deck ask for two Pikachu ex.

JavaScript is kept to five small files, each earning its place: capped-concurrency image
loading (native `loading="lazy"` queued thousands of requests on a fast scroll and the CDN
dropped the connection), touch drag-select (touch's implicit pointer capture makes per-tile
`pointerenter` useless), theme application before first paint, tooltips, and a vendored jsQR
loaded only when a screenshot import is attempted.

---

## Roadmap

Phases 1 and 2 are complete. See [CHANGELOG.md](CHANGELOG.md).

- **Phase 3** — budget allocation across packs, shareable read-only snapshots, importing from
  other trackers, localisation, accessibility pass.
- **Phase 4** — screenshot recognition of the in-game card list, to remove the onboarding
  barrier entirely. Cloud sync last, since it is the first thing needing a backend.

Deliberately **out of scope**: meta tier lists, matchup data, tournament results, a battle
simulator, a trade marketplace, accounts and social features, collection value scores. Each is
a vector for the catalogue drift this app exists to avoid.

---

## Licence and data

**AGPL-3.0-or-later** — see [LICENSE](LICENSE).

That is forced rather than chosen: the only community dataset carrying attack text, ability
text and evolution stage for every set is AGPL, and the alternatives were a dataset missing all
of that, one with no licence at all, or scraping the origin ourselves.

Data sources, the reverse-engineered deck format, and bundled third-party code are credited in
[NOTICE.md](NOTICE.md). Pull rates come from
[flibustier/pokemon-tcg-pocket-database](https://github.com/flibustier/pokemon-tcg-pocket-database)
and are published nowhere else; card detail from
[chase-mew/pokemon-tcg-pocket-cards](https://github.com/chase-mew/pokemon-tcg-pocket-cards).

Pokémon and Pokémon TCG Pocket are trademarks of Nintendo, Creatures Inc. and GAME FREAK inc.
This is an unofficial fan tool, unaffiliated with and unendorsed by any of them.
