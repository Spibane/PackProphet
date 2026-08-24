# Changelog

Notable changes to PackProphet. Dates are ISO. Versions follow
[semantic versioning](https://semver.org) once there is a release to be compatible with;
until then the minor number tracks the roadmap phase.

## Unreleased

- **The card grid is operable from the keyboard.** It was pointer-only: a tile was a div with a
  click handler, so entering a collection — the app's primary interaction — was impossible without
  a mouse or a touchscreen. Arrows move, digits set a count outright, Enter adds, `-` removes, `i`
  opens card detail, Home and End jump. One tab stop for the whole grid with `aria-activedescendant`
  naming the cursor, rather than a roving tabindex that Blazor would have to re-render to move. The
  key legend appears only while the grid has focus, and every tile now has an accessible name
  reading name, set, rarity and copies held. Starting keyboard entry needed its own answer,
  since tabbing to the grid means passing twenty-six controls: there is a visible **keyboard**
  button in the grid toolbar whose tooltip names the keys, a skip link one tab from page load, and
  a command-palette action. Pressing `i` opens a card and Escape comes straight back, with the
  cursor restored to the card you were reading about — without that, every look at a card cost you
  your place in the list.

- **Render tests for the App project** (bUnit). It had none, while being the larger half of the
  codebase — and nearly every defect found in review lived there, because a page that indexes an
  empty list compiles cleanly and passes every engine test. Pages are discovered by reflection, so
  one added later is covered without anyone remembering; each is rendered on a brand-new profile
  and on a populated one. Every regression test was verified by reverting the fix and confirming
  the suite went red.
- **CI now runs the tests before deploying.** It did not, which made a green suite optional.
- The deck QR was **verified against the live game in both directions** — until then every codec
  test was our encoder against our decoder.

## 0.2.0 — 2026-08-23

Phase 2: daily-use depth. The same odds engine pointed at the decisions you make between packs.

### New screens

- **Wonder Pick** — tap the five cards on offer for a take-or-skip verdict. Cost is set by the
  highest rarity *in the offer* rather than by what it is worth to you, so the overpriced-offer
  flag falls straight out of the pricing rule and is sharpest at the top of the range.
- **Resources** — the three currency systems side by side, never summed, because nothing
  converts between them. Leads with the waste: a full pool has *stopped* regenerating, and that
  loss is computable, while a pool at two of five has 36 hours of slack and needs no attention.
- **Trades** — which single trade deserves your next stamina. Stamina caps trading at ~2/day
  while dust accumulates, so the scarce thing is trades, not currency. Grouped by pack, set or
  rarity, since the best trade in each is rarely near the top of a global ranking.
- **In-game wishlist** — the game's own 20-slot board, filled by cost-to-get-otherwise, with a
  minority of slots reserved for cards someone will actually offer. Output is a transcription
  list in set order with swap-level diffs, because the board is retyped by hand.
- **Compare collections** — self-trades between profiles, with full profile management.

### Additions to existing screens

- **Lifetime totals** — enter the game's own counters and the odds check scales to them,
  splitting a lifetime pack count across sets from what the collection implies. Logged and
  imported figures are never mixed, and per-set figures stay logged-only.
- **Evolution gaps** on Collection and Card detail, with a grid filter for the printings that
  would close a chain.
- **Rarity plan chips** moved to Settings as well as Packs, since they drive every page.
- **Pack points** panel gained a "packs until you can afford the rarest card you still need"
  column, and now respects the rarity plan — it had been recommending cards the plan excluded.
- Deep link from the pack ranking straight to logging that pack.

### Shares

A mechanic added to the game mid-phase, and it is not a variety of trade. A Share is one-way:
a friend sends a 1–4 diamond card and receives **nothing** back, capped at one received per day
per account. So it costs no shinedust, no stamina and no card given up — which means wherever it
applies, trading is *strictly worse*, and any surface offering both routes for one card is
recommending the bad one. Diamonds are therefore removed from the self-trade pairing altogether
rather than listed twice, and the trade queue flags them so a 4-diamond is never bought for
5,000 dust when someone would hand it over.

### Bugs found by probing real data

- **Fossils are Trainers, and a Trainer can close an evolution gap.** Omanyte evolves from Helix
  Fossil. Excluding trainers from the name tables reported eleven missing fossils to a player
  who owned every card in the game.
- **Obtainability must outrank rarity** when recommending which printing to chase: the promo
  Charmeleon is a 1-diamond while every openable one is a 2-diamond, so ranking by rarity
  recommended the one card that cannot be got.
- **The points ledger ignored the rarity plan**, so it advised saving for rarities the user had
  explicitly excluded — and the Packs page was applying one set's plan to every set.
- **Hourglass balances were rounded on save.** The field was seeded from a floored division, so
  1,751 came back as 1,740 and eleven hourglasses vanished on every save.
- **A stamina pool was blamed for time it spent filling**, reporting waste to someone who had
  never been at the cap.

### Notes

- The evolution-gap bar is dismissible and stays dismissed. Early on nearly every chain is
  missing a stage, so it is a standing fact rather than a problem — and a notice that cannot be
  closed is a notice that gets ignored.
- **Binder view was cut.** The Collection page already lists every card of a set in set-then-
  number order, owned and unowned, with a columns picker; the binder's remaining delta was row-
  width parity with a number that varies by device. What was left was decoration, which is the
  category this project refuses.

## 0.1.0 — 2026-08-23

First working version: the whole core loop, from an empty collection to "open this pack".

### The engine

- **Pull-rate odds** per pack variant and slot, with expected copies per card. Slot
  distributions and variant appearance rates are both normalised — a slot always yields
  exactly one card, so upstream's 99.996% totals would otherwise leak a chance of an empty
  slot into every card's rate.
- **Expected packs to finish a target** as a Poissonised integral, so a demand for two copies
  is the same formula as a demand for one rather than a special case.
- **Targets** are an interface, not a setting: rarity plans, decks, wishlists and arbitrary
  composites all feed the same ranking. Demand carries a *quantity*, which is what lets a deck
  ask for two of a card and stops the ranking chasing a card you already have enough of.
- **Cheapest route per card** across pulling, pack points, trading and Wonder Pick, consulting
  a routing matrix so no route is ever offered that does not exist at that rarity.
- **Pack points** ranked by packs saved per pack's worth of points, not by price.
- **Deck codec** — a C# port of the reverse-engineered share format, round-tripped in tests.
- **Deck linter** with a distinct *unverified* severity, because reporting a deck as legal when
  its stage data is missing is how you hand someone a deck that cannot start.

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
- Ownership is per **card**, not per set entry. 3,761 entries are only 3,546 ownable cards;
  A4b re-lists 214 of its 379. Keying by set entry would have priced "complete A4b" at nearly
  double its real cost.
- **40% of card names map to more than one card identity** — Eevee has twelve. This killed
  text-decklist import outright: ambiguity is not an edge case at 40%, and it gets worse with
  every set that reprints a species.
- Pack points are a **byproduct** of opening, not an alternative to it, so the only meaningful
  ranking is the ratio of pull cost to point cost — and by that measure Immersives are among
  the worst purchases in the game despite looking cheap.
- Deluxe packs are limited-time, which re-prices *other* sets when they leave rotation, and
  makes a completion estimate **fall** when the situation gets worse. Estimates therefore never
  appear without saying how many cards they cover.

### Notes

- Local-only storage, with a schema version and a real migration from two earlier target
  shapes.
- Installable as a PWA with full offline support after first visit.
- Light and dark themes, applied before first paint so a dark-mode user never sees a flash.
- Light mode's structural lines were rebuilt at 2.9:1 contrast against the body; Bootstrap's
  default `#dee2e6` on white manages 1.30:1, which is invisible on a phone outdoors.
- Sets that are released but have no published pull rates can borrow another set's slot shape,
  labelled as an assumption everywhere a number derived from it appears.
