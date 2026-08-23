# Changelog

Notable changes to PackProphet. Dates are ISO. Versions follow
[semantic versioning](https://semver.org) once there is a release to be compatible with;
until then the minor number tracks the roadmap phase.

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
