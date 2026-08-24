# Changelog

Notable changes to PackProphet. Dates are ISO. Versions follow
[semantic versioning](https://semver.org) once there is a release to be compatible with;
until then the minor number tracks the roadmap phase.

## Unreleased

- **Licence and source are offered inside the app.** Settings now carries a "Licence and
  source" section linking the licence text, the repository, every upstream data source and the
  trademark disclaimer. AGPL section 13 applies to network interaction, so the offer has to
  reach the running app rather than only the repository.
- **The service worker no longer precaches about 663 KB that no page loads** — roughly a sixth
  of a 4.3 MB first visit. Removed: Blazor.Bootstrap's pdf.js worker and sortable list, and
  every vendored Bootstrap variant other than the two files `index.html` links (unminified
  copies, right-to-left builds, ESM builds, and the grid/reboot/utilities subsets). The
  precache rules are now tested by reading the regexes out of the worker itself, since that
  file runs neither in development nor in CI.

- **The card grid is operable from the keyboard.** It was previously pointer-only: a tile was a
  div with a click handler. Arrows move, digits set a count outright, Enter adds, `-` removes,
  `i` opens card detail, Home and End jump. The grid is one tab stop with
  `aria-activedescendant` naming the cursor, rather than a roving tabindex that Blazor would
  have to re-render to move. The key legend appears only while the grid has focus, and every
  tile has an accessible name reading name, set, rarity and copies held. Keyboard entry can be
  started three ways: a visible **keyboard** button in the grid toolbar whose tooltip names the
  keys, a skip link one tab from page load, and a command-palette action. Pressing `i` opens a
  card and Escape returns, with the cursor restored to the card you were reading about.

- **Render tests for the App project** (bUnit), which previously had none. Pages are discovered
  by reflection, so a page added later is covered automatically; each is rendered on a
  brand-new profile and on a populated one. Discovery deduplicates by page type rather than by
  route, because Collection answers both `/` and `/collection` and a repeated theory argument is
  dropped by the runner, which removed the app's main screen from every page-driven test. Every
  regression test was verified by reverting the fix and confirming the suite went red.
- **CI runs the tests before deploying.** It previously did not.
- The deck QR was **verified against the live game in both directions**. Until then every codec
  test ran this project's encoder against its own decoder.

## 0.2.0 — 2026-08-23

Phase 2: daily-use depth. The same odds engine applied to the decisions between packs.

### New screens

- **Wonder Pick** — tap the five cards on offer for a take-or-skip verdict. Cost is set by the
  highest rarity *in the offer* rather than by what the offer is worth to you, so offers priced
  above their value to you are flagged.
- **Resources** — the three currency systems side by side, never summed, because nothing
  converts between them. Each leads with waste: a full pool has *stopped* regenerating, while a
  pool at two of five has 36 hours of slack.
- **Trades** — which single trade deserves your next stamina. Stamina caps trading at ~2/day
  while dust accumulates, so trades rather than currency are treated as the scarce resource.
  Grouped by pack, set or rarity, since the best trade in each is rarely near the top of a
  global ranking.
- **In-game wishlist** — the game's own 20-slot board, filled by cost-to-get-otherwise, with a
  minority of slots reserved for widely-held cards. Output is a transcription list in set order
  with swap-level diffs, since the board is retyped by hand.
- **Compare collections** — self-trades between profiles, with full profile management.

### Additions to existing screens

- **Lifetime totals** — enter the game's own counters and the odds check scales to them,
  splitting a lifetime pack count across sets from what the collection implies. Logged and
  imported figures are never mixed, and per-set figures stay logged-only.
- **Evolution gaps** on Collection and Card detail, with a grid filter for the printings that
  would close a chain.
- **Rarity plan chips** are available in Settings as well as on Packs.
- **Pack points** panel gained a "packs until you can afford the rarest card you still need"
  column, and now respects the rarity plan.
- Deep link from the pack ranking straight to logging that pack.

### Shares

Shares were added to the game during this phase. A Share is one-way: a friend sends a 1–4
diamond card and receives nothing back, capped at one received per day per account. It costs no
shinedust, no stamina and no card given up. Diamonds are therefore removed from the self-trade
pairing rather than listed under both routes, and the trade queue flags them so a 4-diamond is
not bought for 5,000 dust.

### Bugs found by probing real data

- **Fossils are Trainers, and a Trainer can close an evolution gap.** Omanyte evolves from Helix
  Fossil. Excluding trainers from the name tables reported eleven missing fossils to a player
  who owned every card in the game.
- **Obtainability now outranks rarity** when recommending which printing to chase: the promo
  Charmeleon is a 1-diamond while every openable one is a 2-diamond, so ranking by rarity
  recommended the one card that cannot be obtained.
- **The points ledger ignored the rarity plan**, so it advised saving for rarities the user had
  excluded — and the Packs page was applying one set's plan to every set.
- **Hourglass balances were rounded on save.** The field was seeded from a floored division, so
  1,751 came back as 1,740.
- **A stamina pool was blamed for time it spent filling**, reporting waste to someone who had
  never been at the cap.

### Notes

- The evolution-gap bar is dismissible and stays dismissed. Early on nearly every chain is
  missing a stage, so it reports a standing fact rather than a problem.
- **Binder view was cut.** The Collection page already lists every card of a set in set-then-
  number order, owned and unowned, with a columns picker; the binder's remaining delta was
  row-width parity with a number that varies by device.

## 0.1.0 — 2026-08-23

First working version: the whole core loop, from an empty collection to "open this pack".

### The engine

- **Pull-rate odds** per pack variant and slot, with expected copies per card. Slot
  distributions and variant appearance rates are both normalised — a slot always yields exactly
  one card, so upstream's 99.996% totals would otherwise leak a chance of an empty slot into
  every card's rate.
- **Expected packs to finish a target** as a Poissonised integral, so a demand for two copies is
  the same formula as a demand for one rather than a special case.
- **Targets** are an interface: rarity plans, decks, wishlists and arbitrary composites all feed
  the same ranking. Demand carries a *quantity*, which lets a deck ask for two of a card and
  stops the ranking chasing a card you already have enough of.
- **Cheapest route per card** across pulling, pack points, trading and Wonder Pick, consulting a
  routing matrix so no route is offered that does not exist at that rarity.
- **Pack points** ranked by packs saved per pack's worth of points, not by price.
- **Deck codec** — a C# port of the reverse-engineered share format, round-tripped in tests.
- **Deck linter** with a distinct *unverified* severity, so a deck with missing stage data is
  not reported as legal.

### Screens

- **Collection** — virtualised grid and list views, counts, filters, per-set progress, bulk
  drag-select, undo/redo.
- **Which pack** — the ranking, cost-to-finish in packs and calendar time, target advisor, pack
  points panel, and per-card cheapest routes.
- **Log a pack** — pick a pack, tap what came out, points accrue and the ranking updates.
- **Decks** — screenshot import of the in-game code, a builder with live legality checks and
  search over rules text, QR export, closest-to-buildable ordering, list and showcase views.
- **Wishlists** — arbitrary wanted cards, priced by the same engine, list and showcase views.
- **History** — packs over time, predicted against actual, and a showcase of your best hits.
- **Card detail**, **Settings** with JSON export/import, and a **command palette** (Ctrl/⌘K).

### Data findings that shaped the design

Each of these was an assumption that turned out to be wrong, and each would have produced
silently wrong numbers rather than an error. All are now pinned by tests.

- Slot rarity codes name rarity **rungs**, not exact codes. Upstream never names `SAR`
  anywhere; counting exact matches priced all 113 SAR cards as unobtainable.
- Pack variants are not just Regular and Rare — sets have 2, 3 or 4, holding **4, 5 or 6**
  cards, and one variant numbers its slots from 0 while every other starts at 1.
- Ownership is per **card**, not per set entry. 3,761 entries are only 3,546 ownable cards; A4b
  re-lists 214 of its 379. Keying by set entry would have priced "complete A4b" at nearly double
  its real cost.
- **40% of card names map to more than one card identity** — Eevee has twelve. Text-decklist
  import was dropped as a result, since the ambiguity cannot be resolved from a name alone.
- Pack points are a **byproduct** of opening rather than an alternative to it, so the ranking
  uses the ratio of pull cost to point cost. By that measure Immersives are among the worst
  purchases in the game despite looking cheap.
- Deluxe packs are limited-time, which re-prices *other* sets when they leave rotation, and can
  make a completion estimate **fall** when the situation gets worse. Estimates therefore always
  state how many cards they cover.

### Notes

- Local-only storage, with a schema version and a migration from two earlier target shapes.
- Installable as a PWA with full offline support after first visit.
- Light and dark themes, applied before first paint so a dark-mode user never sees a flash.
- Light mode's structural lines are drawn at 2.9:1 contrast against the body. Bootstrap's
  default `#dee2e6` on white manages 1.30:1.
- Sets that are released but have no published pull rates borrow another set's slot shape,
  labelled as an assumption everywhere a number derived from it appears.
