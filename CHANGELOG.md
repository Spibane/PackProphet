# Changelog

Notable changes to PackProphet. Dates are ISO. Versions follow
[semantic versioning](https://semver.org) once there is a release to be compatible with;
until then the minor number tracks the roadmap phase.

## 0.4.1 — 2026-08-26

### Fixed

- **The hover tooltip landed on the card below the one you were pointing at.** It was placed off
  the target's bottom edge, which is right for the 32px thumbnail in list view and wrong for a tile
  three hundred pixels tall: the label opened a whole card-height under the cursor, at the corner of
  the card in the next row. On the last row that fits on screen there was nowhere for it to go — the
  only space below is the sliver of the next row, so it sat jammed against the bottom edge or
  flipped up and covered the row before. It anchors to the pointer within the target now, so it
  opens just under the cursor, over the card it names. A small target is unaffected: the offset is
  less than a thumbnail, so the list view's preview still hangs off the row as it did.

- **The whole grid's art blinked out for a frame whenever you scrolled quickly.** Not the tiles
  coming into view — the ones already on screen, art loaded, going blank together and coming back.
  The tiles carried a `@key` and the rows holding them did not, and Blazor matches keys only among
  siblings: with the key one level too deep, the row elements were matched by position, so a scroll
  of a single row made every tile in every rendered row a new key. Destroy, rebuild, all of them, on
  every window change. The `<img>` elements went with their tiles, and their `src` is set by
  `js/imgloader.js` rather than by the renderer, so each one came back with nothing to show while
  the loader worked through them again. Keyed at the row, a row still on screen is matched by
  identity and moved, its loaded images intact.

- **Art the browser already had still queued behind the loader's six slots.** The cap exists to keep
  a fast scroll from opening hundreds of streams on one HTTP/2 connection and having the CDN drop it;
  a cache hit opens none, so making one wait its turn bought nothing and cost a tile its picture.
  Urls that have loaded once are now shown straight away, and where the browser can answer in the
  same tick — the memory-cache case, which is most of them — the tile is never painted as pending at
  all. A url that turns out to have been evicted falls back to the managed queue.

## 0.4.0 — 2026-08-25

A pass over the collection grid and the screens around it, started by comparing the app against
two other trackers — [PTCGP Tracker](https://ptcgp-tracker.com) and
[TCG Pocket Collection Tracker](https://tcgpocketcollectiontracker.com) — and then reworked over
several rounds of review. Every figure below was measured in the running app at 375x812 unless
another width is named.

### The grid's own bar

- **List mode's switch moved onto the always-on bar.** It is the view that carries the set and
  number, type, rarity, a count you can type into and the printed text — half of what a tile grid
  cannot do — and behind a summary reading "filters and layout" it was findable only by someone who
  already suspected it existed. The density toggle stays inside: it means nothing until you are in
  list mode, and that row has to survive a phone.

- **"Missing only" is a button on that bar too.** It is what you reach for after opening a pack, and
  it was option two of a select inside a closed disclosure. The select still offers all six filters;
  this is a shortcut to the one used every session.

- **Rarity and type are chips, several at a time.** Rarity was a dropdown that could hold exactly
  one rung and named each one in words the game draws as symbols; it is now a row of toggle chips
  carrying the same glyphs the list view draws, because "the stars and the crown, never mind the
  diamonds" is the question people actually ask. A **type filter** did not exist at all and now
  does, as the fourteen pips. Nothing selected means everything, so an untouched row hides nothing.
  Type comes from the printed detail, which downloads after the card list, so those chips are
  disabled until it lands.

- **Each chip row is a labelled group** — `RARITY`, `TYPE`, `PACK` — separated by the divider the bar
  already uses. Side by side on a wide screen they read as one row of twenty-four unexplained
  buttons; stacked on a narrow one, two rows of them. The visible label is also the group's
  accessible name, so the two cannot drift.

- **One control height per bar.** Every bar mixed 31px Bootstrap controls, 40px set tabs and 44px
  filter chips, which reads as a row that was assembled rather than designed. The height is a token
  now: 36px on a mouse, 44px on a coarse pointer, so every control in a bar grows together rather
  than singling out the chips. The page header is deliberately exempt — its 3rem is a contract the
  sticky toolbar below it depends on, and at 44px the log screen's header squeezed the pack's name
  down to "R.".

- **Choose a pack by its wrapper.** The pack filter was a select listing "Charizard", "Mewtwo",
  "Pikachu"; it is now the three boosters, which is how the game asks the same question. One at a
  time — a card lists every pack it comes from, so "either of these two" is nearly the whole set,
  while "what is still missing from the pack I am about to open" is the question the filter exists
  for. Tapping the chosen pack again clears it. The set tabs get a wrapper beside the code for the
  same reason: B2a and B2b are the same three characters in a different order, and their wrappers
  are not.

- **A back-to-top button.** A set is a couple of hundred cards and "every set" is three and a half
  thousand, and every control that acts on the list is at the top of the page and nowhere else. It
  appears once a viewport has been scrolled past, sits clear of the tab bar, and jumps rather than
  animating: a virtualised list of thousands smooth-scrolled renders every row in between and queues
  every image in between for nothing.

- **The pack log had no way to type.** A pack holds up to 233 cards and you are looking for the five
  you pulled, so finding them meant scanning the grid five times. It gets the search box the
  collection grid already had, with the picked strip left unfiltered so narrowing never hides what
  you have already logged.

### Which set you are looking at

- **A sticky strip names the set you are scrolling through**, with its logo beside the name. The
  collection can hold every card ever printed, in set order, and a screen into it nothing said which
  set was under your thumb — the set picker names the *filter*, which in that view is "every set". A
  header per set is impossible inside a virtualised list without breaking the uniform row height its
  scrollbar depends on, so one strip sits under the toolbar and a small script keeps it in step with
  the topmost visible row. Only where the list spans more than one set.

  The logo is 36px tall and the strip about 42px, because a 256x113 wordmark scaled to the cap height
  of the label beside it is a smudge. The set tabs keep their booster wrapper instead: a tab holds
  its picture beside a code and a percentage, and the sliver that leaves is the wrong shape for
  lettering. Same set, two pictures, because the two places have opposite shapes to fill.

### The card tile

- **The tile is the art again.** The count, the info button and the heart were all badges over the
  artwork, covering three corners of the only thing the grid layout exists to show. At desk widths
  they sit in a caption strip under the picture — heart, **card name**, count. The name replaces the
  word "info": below the art those six characters label a thing that already has a label, and the
  printed name on a 200px tile is small, stylised and sometimes behind an ex badge. It is styled as a
  name rather than a button, which is how the list view already does it.

  The frame moved from the art onto the whole tile, so the two read as one object. Without it a
  tile's count sat four pixels from the next card's heart with nothing between them, and the eye
  grouped across the gutter instead of down the tile.

- **At mobile widths the grid is unchanged**: no caption, the count back to a badge exactly where it
  was, and the tile the height of its art, so a screenful holds as many cards as before. That switch
  is on the same 1056px breakpoint as the tab bar rather than on `hover`, because a desktop window
  dragged narrow keeps its mouse — it was keeping the caption while everything else on the page had
  already become the phone layout. The corner info button comes back in that band, over the art and
  revealed on hover, since a long press is a touch gesture.

- **The count badge is smaller wherever it sits on the art.** It was 27% of the tile wide with a
  2.25rem height floor, which on a six-column phone tile of 58px came out 30 by 36 — half the card's
  width and nearly two thirds of its height, for one or two digits. Now 28px square.

- **A spinner while art loads, instead of a broken-image icon.** The loader holds an image's `src`
  back until one of its six slots is free, and an `<img>` with an alt and no src is drawn by the
  browser as its broken-image marker — so a fast scroll showed a fault on every tile it had not
  reached yet. "Not loaded" and "will never load" looked identical, and the constant one looked like
  the fault. Pending and in-flight images are hidden with a spinner in their place; the failed state
  is untouched, since the broken marker and the card's name beside it are the most useful thing a
  tile with no art can show — and it is now the only thing that looks like a fault.

### Wanting a card

- **A heart on the tile and on each list row.** Wanting a card was reachable only from that card's
  own page, so recording it meant leaving the set you were looking at, marking it, and coming back.
  One tap, on or off, one copy — wanting four of something is a wishlist-page question, and a heart
  that cycled through counts would give no way to see what it landed on.

- **The hearts have a list of their own**, "Want it", made by the first heart. Filling whichever
  wishlist happened to be first would quietly rewrite the one list you built deliberately. It is
  marked as the hearts' list wherever wishlists are shown, and deleting it is safe: the next heart
  makes another.

- **In list view the heart has its own column, at the front.** Beside the −/+ buttons it was a third
  small square button in a row of them, doing something completely different, next to the number it
  was most likely to be confused with.

- **Not on the tile grid on a touch screen.** A permanent button in the corner of every tile is a
  permanent hazard in the one layout whose whole job is being tapped, and a miss costs a copy of the
  wrong card. The list view keeps its column, and the card's own page has the same one-tap heart —
  present whether or not the list exists yet, which is what makes it a complete route on a phone.

### Numbers that were already computed and never shown

- **Where in a pack the chance is.** "Chance of a hit" answers whether a pack helps and hides which
  card in it does the helping: the first three cards come from the common pool, so a collection that
  has finished the commons has all of its chance in the last two, and the same percentage means
  different things in two sets. An expanded ranking row now breaks the chance down by card position,
  merging adjacent positions whose odds are equal — which is what produces the "1st-3rd card"
  reading. Merged from the numbers rather than hardcoded: a set that broke the three-commons pattern
  would come out unmerged rather than wrongly merged.

- **The deck page's pack table** showed a bare "chance of a hit" with no way to read it as packs. It
  gets the "packs per new card" column the Which-pack table has, from a figure the ranker already
  returned.

- **A wishlist as pasteable text.** The share link is the better artefact and the wrong shape for
  where these trades happen: a Discord thread takes a few lines, nobody opens a stranger's link, and
  a reader with the game open on the same phone cannot follow one and come back. Only what is still
  short, set and number first, rarity on every line — the game's trade rules turn on it.

### Accessibility

- **`aria-pressed` was silently absent on every unpressed toggle, everywhere.** Blazor omits an
  attribute whose value is `false` — right for HTML's own booleans, since `disabled="false"` would
  disable a control, and wrong for every ARIA state, where the two values are the *words* "true" and
  "false" and the absence of the attribute says the element has no such state at all. So a toggle
  that was off announced itself as an ordinary button: the one state that needed saying was the only
  one that said nothing, and the markup looked correct — the attribute was written, it just never
  arrived.

  Found by a test that clicked `button[aria-pressed]` and hit "add", because the unpressed button did
  not match the selector. Fixed at all 26 sites across eleven files through one `Aria.Flag` helper.
  The worst of them was the rarity plan on the Which-pack page, where six of the ten chips that
  configure every estimate in the app were announced as plain buttons.

  Guarded by a test that reads the `.razor` sources rather than the DOM, because from the DOM side a
  dropped attribute is invisible: what you see is an element with no `aria-pressed`, which is exactly
  what a plain button looks like. Checked against a deliberately reintroduced violation.

- **The filled heart failed the non-text contrast floor in dark.** It is the only thing on a tile
  saying "this is on your want list", so WCAG 1.4.11 asks 3:1 of it; raw `--bs-danger` measured
  **2.94:1** against the caption — the same failure the outline buttons had, and the same pair of
  mixed reds fixes it, now shared as a token by all three places that draw a heart. The empty heart
  was already fine at 4.45 light / 5.15 dark, which is asserted too because its alpha is the product
  of a translucent colour and an element opacity.

- **The heart was reachable only with a mouse.** Everything in a tile is out of the tab order — a
  focus stop per card would put thousands of them between the grid and the rest of the page — so the
  grid's contract is that the cursor plus a letter does whatever a tile's furniture does. There was a
  key for the count, for the detail page and for a bulk range, and none for wanting a card. **`w`**
  now toggles it, and the key legend says so.

- **The want column's header in list view had no accessible name.** Blank on screen is right; a
  column header announced as nothing is not.

- **White on the count badge sits at exactly 4.50:1**, which passes and could not pass by less, so it
  is now pinned by a test rather than by luck.

### Fixed

- **The sticky bars were hiding what sticks under them.** The set strip and the list view's column
  headers were both pinned at `top: 0` beneath a toolbar that is also pinned there and paints over
  them — so both vanished on the first scroll, which is the state they exist for. CSS cannot express
  "under my previous sibling" when that sibling's height depends on how many rows it wrapped into, so
  the script that fills the strip measures the bars and publishes the offset for the stylesheet.

- **A gap under the pinned header on the log screen**, with the card grid scrolling through it. The
  toolbar below it was pinned at a constant `3rem + 1px`, which border-box rounding left a sub-pixel
  short of the header. Measured now — and it was wrong in the other direction too: once the controls
  grew on a phone the header became 56px and the same constant would have pinned the toolbar nine
  pixels *underneath* it.

- **"How to get it" rendered 192px wide on a tablet.** Two columns at those widths put it on a second
  row — in column one, the art column. It spans the full width instead.

- **The promo set tab drew a blank grey box where a wrapper should be**, and the two tabs without a
  picture — the promo set and the "all of series" aggregate — were laid out as narrow columns among a
  row of wider tabs. The blank came from asking for pack art through the helper that never returns
  nothing: where the catalogue has no mapping it guesses a URL from the pack's name, which is right
  for a real booster and wrong for a promo set, whose "packs" are the Vol. 1, Vol. 2 groupings
  recording how a promo was given away. The tab asks the catalogue directly now, and every tab keeps
  the same layout with or without a picture.

- **The info button and the heart no longer disappear before the touch layout arrives.** Both were
  hidden below a 200px tile, which with six columns happens at about 1200px of viewport — while the
  long press that replaces them does not arrive until 1057px. Between the two there was a mouse, no
  corner button and no way to open a card except the keyboard. In the caption there is nothing to
  collide with, so neither rule is needed.

- **Wishlists took Wonder Pick's place in both nav bars.** The four primary destinations are the ones
  you visit while you are handling cards; a wishlist is consulted every time you open a pack or weigh
  a trade, and Wonder Pick is a once-a-day errand with its own screen. It moves into More, which is
  where the rest of the occasional destinations already live. Both navs read from the same list, so
  the swap happened once.

- **Backing out of the More sheet no longer taps the page underneath.** On a phone the sheet is
  anchored to the bottom edge, over the tab bar that opened it, so the More button cannot be tapped a
  second time to close it — and the only "outside" left is the collection grid. A popover's
  `::backdrop` is painted but not hit-testable, so that dismissing tap went straight through to
  whichever card was under it. The sheet now has a **Close** button at the bottom, and a transparent
  sibling of the popover catches the tap: it is outside the popover, so light dismiss still fires, and
  it is a real element, so nothing behind it is clicked.

### Earlier

Work that had landed before this pass and has not been released either.

- **Contrast, measured rather than eyeballed.** Every text colour on every page was read with
  getComputedStyle in both themes and checked against the surface it actually sits on. Two things
  were under WCAG AA's 4.5:1, both from Bootstrap's defaults rather than from this project:

  - **Outline buttons**, in *both* themes and worse in dark. Against the raised panel they
    measured secondary 4.10 light / 2.84 dark, danger 3.96 / 2.94, primary 3.94 / 2.96 — six
    values, none of them passing, the dark ones close to half. Each hue is now mixed toward its
    theme's far end until it reaches 4.8:1, which leaves room for a later change to the panel fill
    without dropping back under. Border as well as text, since the outline is the control's
    boundary and carries its own 3:1 requirement.
  - **`text-body-tertiary`**, the quietest copy in the app — the line under a destructive button,
    the trademark notice. Bootstrap ships it at 50% alpha, which composites to 3.02:1 in light and
    3.78:1 in dark. The hue is unchanged and only the alpha moved, to the first value clearing
    4.8:1 in both themes.

  The rules are written flat rather than nested: nothing else in the stylesheet nests, and native
  CSS nesting is unsupported before iOS 17.2, where a nested block is not degraded but dropped
  whole — which would have restored the failing colours with nothing to show for it.
- **Contrast is now a test.** The colours are read back out of the stylesheet and the ratios
  recomputed, so editing one re-runs the arithmetic instead of quietly invalidating a comment.
  Selectors are matched as whole comma-separated entries, not as substrings — matching by
  substring finds `[data-bs-theme="dark"]` inside the light rule's own
  `:root:not([data-bs-theme="dark"])` and reads the light colour as the dark one, which is how
  this test first "failed", reporting a ratio that existed nowhere in the app.
- **Three controls were named only by their placeholder** — the deck screenshot picker, the
  share-code box beside it, and the new-collection field in Settings. A placeholder disappears the
  moment anyone types into it, which is also the moment they most need to be told what the field
  was. All three have real labels now.
- **The packs-per-day chart says what it shows.** It was `role="img"` labelled "Packs opened per
  day", which names the picture and says nothing about it — and `role="img"` hides an element's
  children, so the thirty bars inside it did not exist for a screen reader. The label now carries
  the range, the total and the busiest day, and the figures themselves sit under it as a real
  table behind a disclosure. Collapsed rather than visually hidden: thirty rows read out before
  the rest of the page is a worse answer than a control that says what it opens, and it is also
  the only way a sighted reader gets an exact figure off a bar.
- **Accessibility rules that hold everywhere are now swept over every page**: form controls have
  names, buttons and links have names, images have alternatives, and heading levels never skip.
  Driven from the same reflection list as the render tests, so a page added later is covered
  without anyone remembering. Heading order was already clean; the sweep is what keeps it so.
- **Import a collection from another tracker.** A CSV or `.xlsx` export, read entirely in the
  browser and never uploaded. The figures come first — cards, copies, rows read, and any row that
  did not match, named with its line — and nothing is written until a destination is chosen. The
  default is a new collection, which cannot lose what is already there; merge and replace sit
  beside it, and only those two advertise an undo, because switching collections clears the undo
  history and the third would be promising something it cannot do.
- **One import pipeline, no per-tracker code.** Every tracker surveyed names a card by the same
  two facts, set code and number, and none of them agree on the shape:
  tcgpocketcollectiontracker.com writes a single `A1-1`, PTCGP Tracker writes a `set_id` of `A1`
  beside a `card_id` of `1` — the same column name the first gives to the whole identifier. A
  header cannot say which it is, so the identifier is reassembled per row: the whole id is tried
  first and the set column consulted only if that fails. Composing first would ask for `A1-A1-1`
  on every row of tcgpocketcollectiontracker's file, which carries both.
- **Upper-casing a set code loses eight of the twenty-two sets.** `A1a` becomes `A1A`, which
  matches nothing, so the whole of A1a, A2a, A2b, A3a, A3b, A4a, A4b and their B-series
  equivalents import as unknown rows while every other set succeeds — no exception, no malformed
  row, just a third of a collection quietly missing. A4b is also where all 214 re-listings live,
  so the same bug broke the reprint merge. Set codes are matched against the card database rather
  than normalised by rule. Found by a test that imports all 3,761 entries at once.
- **An import collapses and an export expands.** This app keys ownership by artwork; the trackers
  key it by set entry. So 3,761 rows fold into 3,546 cards on the way in — two rows can name one
  artwork — and 3,546 cards are written back as 3,761 rows on the way out. Counts merge by the
  larger of the two rather than the sum: two rows about one card are two opinions about one
  number, and summing would double a Deluxe-set owner's whole collection.
- **An `.xlsx` reader**, because PTCGP Tracker exports a workbook and a file that has been opened
  in Excel comes back as one whichever way it started. About 200 lines and no dependency: the App
  ships to a browser, and a spreadsheet library is payload every user downloads to read a file
  most of them never have. It follows the workbook relationships to find the first sheet rather
  than assuming `sheet1.xml`, since sheet order is not file order, and fills sparse cells from
  their `A1`-style references, since reading cells in document order shifts every value left of a
  gap. The kind of file is decided by the zip signature rather than the extension.
- **Export the collection as CSV**, aimed at no site in particular: `set`, `number`, `card_id`,
  `name`, `rarity`, `quantity`. The identifier is written twice, in both shapes the trackers use,
  so a reader looking for either finds it — one column of redundancy in place of a guess about
  what some particular site wants. Unowned cards are written as zeros rather than left out,
  because a reader cannot tell "none of these" from "no opinion" and at least one tracker's import
  deletes what a file reports as zero. No byte order mark: Excel would prefer one for the names
  carrying ♀ and ♂, but a reader that does not expect it takes it as part of the first column's
  name and then recognises no column at all.
- **Writing for one particular tracker was tried and abandoned.**
  tcgpocketcollectiontracker.com's importer checks that a human-readable `Id` column is present
  and then ignores it, writing by its own `internal_id`. Those are not derivable — the gaps are
  irregular — so a file it accepts would mean vendoring 3,546 of another project's database keys,
  and a stale copy would write correct-looking counts against the wrong cards, in the one column
  a person could not check.
- Probing that format turned up something worth recording: their 3,761 entries carry only
  **3,546 distinct `internal_id` values**, and `A4b-1` shares one with `A1-1`. Their internal id
  is an ownership key. Two projects reached the same conclusion independently — that a card
  re-listed in a later set is one card — from the same published data.
- The import fixtures are **real exports**, kept byte for byte as the tracker wrote them, in both
  CSV and `.xlsx`. A fixture written by this project would only prove the reader can read its own
  output. Both forms of the same collection are asserted to import identically, card by card.
- **A CDN that hung, rather than failed, left every page on "Loading card data…" for good.** The
  fallback to the vendored snapshot only runs when a CDN attempt returns, so a request that is
  neither answered nor refused — a network that drops packets to jsdelivr rather than rejecting
  them — never reached it. Each request that leaves the origin now carries a deadline: five
  seconds on the boot path, thirty on the card detail that loads afterwards, and a hang is
  treated exactly like a failure. Verified by reverting the deadline and watching the new test go
  red.
- **Dates on the history chart printed on top of each other.** The axis marked both ends, every
  month start and every Monday, with no rule against two marks landing in the same place — and a
  label is about three days wide. 24 August is a Monday one day before the 25th, so the last two
  dates were drawn over each other; 1 August beside the Monday after it did the same. A mark is
  now skipped when it cannot clear the labels already placed, ends first, then months, then weeks.
- The grid's image loader could not honour a request to re-check its images: `refresh()` read a
  variable that was never assigned, so every call threw and was swallowed.

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

- **Render tests for the App project** (bUnit), which previously had none. Pages are discovered
  by reflection, so a page added later is covered automatically; each is rendered on a
  brand-new profile and on a populated one. Discovery deduplicates by page type rather than by
  route, because Collection answers both `/` and `/collection` and a repeated theory argument is
  dropped by the runner, which removed the app's main screen from every page-driven test. Every
  regression test was verified by reverting the fix and confirming the suite went red.
- **CI runs the tests before deploying.** It previously did not.
- The deck QR was **verified against the live game in both directions**. Until then every codec
  test ran this project's encoder against its own decoder.

## 0.3.0 — 2026-08-25

Phase 3, and a layout pass over all seventeen routes. Every figure below was measured in the
running app at 375x812 unless another width is named.

### New

- **Shareable read-only wishlists.** A list travels whole inside the link and is decoded in the
  recipient's browser; nothing is uploaded and no account exists. The payload sits after the `#`
  so it never reaches a server as a query string, which also fixed a 414 on a real list.
- **An in-game name per collection**, carried into the share link. A want-list told the reader
  exactly what to send and gave them no way to find you: the only name in the payload was the
  collection's, which is what you called a save slot.
- **Split a pack budget across packs** — how many of each to buy for a fixed number of packs,
  rather than sinking all of them into whichever pack currently ranks first.
- **Pin a pack to the front of the log picker.** A star on each booster; pinned packs lead the
  grid whatever the series dropdown says, which is the trip the pin exists to save. Persisted
  per collection, and drawn as pinned wherever it appears, so a pack from another series never
  reads as part of the one below it.

### Layout

- **Every page was clipping 160px below the fold.** An unstyled wrapper plus a vendored
  `height: 100vh` from a package nothing used. Removing the package alone made it worse.
- **A bottom tab bar below 1057px**, and above it a top nav cut to the same four destinations in
  the same order, from one shared menu component. Twelve links inline needed 1242px of a 1265px
  row, so the bar wrapped at every width — well above the point the tab bar takes over.
- **One page shell with three named measures**, replacing fifteen ad hoc `max-width` values
  across twenty-six declarations. Body blocks now clear a display cutout; they were padding with
  a flat `1rem`, so in landscape on a notched phone they sat under it while the bars above did
  not.
- **Column priority is a container query, not a media query.** A table's width comes from its
  container: the wishlist editor is two columns above 1000px, so at a 1280px window its table had
  614px and needed 680, and every row wrapped — worse the wider the screen.
- **The set row, the rarity plan and the board's tuning knobs fold away**, each summarised by its
  own state. Collection opens on the newest openable set rather than the first one printed.
- Collection chrome 499 → 156px. Which pack's tools three rows → one at 1280px. Which pack 855 →
  341px, Trades 559 → 288px, the in-game list 911 → 501px. Breakpoints 11 → 9.
- **Which pack's breakdown buttons wrap into an even grid on a phone.** Four labels of four
  different lengths in a flex row wrapped three-and-an-orphan at 414px and two-and-two at 375px,
  with neither row's edges lining up. Two equal columns below 600px, and the flex row kept above
  it, where all four fit on one line.
- **The ranked pack table lines up with the page again.** Bleeding it to the screen edge on a
  phone put the "pack" heading hard against the bezel and the log button a few pixels off the
  other edge, out of line with every control above. The row tint and the rules still run edge to
  edge; only the text is inset. A floor on the numeric headings keeps the two insets out of those
  columns, so the header comes out at 36px rather than the 50 it was, or the 64 the inset alone
  would have cost.

### Accessibility

- **The card grid is operable from the keyboard.** It was pointer-only: a tile was a div with a
  click handler. Arrows move, digits set a count outright, Enter adds, `-` removes, `i` opens
  card detail. One tab stop with `aria-activedescendant`, a visible **keyboard** button naming
  the keys, a skip link, and a command-palette action.
- **The -/+ buttons meet WCAG 2.5.8.** Two of their four spellings measured 26.67 x 23.00 px,
  under the 24 x 24 floor, in a 50px row with the buttons centred in it. One class now, sized by
  one variable, guarded by a test on the variable.
- **The command palette is a real `<dialog>`.** The focus trap, Escape, the inert background, the
  backdrop and focus restoration are the platform's; a hand-rolled trap and an inert wrapper
  around the whole layout went with it.
- Tables carry real headers, scroll inside their own wrapper rather than sliding the page, and
  are reachable by keyboard. Eighteen ad hoc alerts became one `Notice` component that also picks
  whether a message interrupts a screen reader or waits its turn.
- Two table cells had been given a flex display, which takes a cell out of its row: they measured
  25.8px and 34.6px in a 46.6px row, so the rule sat high in those two columns and level in the
  other seven.

### Copy

- **Paragraphs over 40 words: 45 → 15.** Kept what tells you about the game, your data, or what
  to do; dropped what a badge already says, what justifies the design, and the clause that
  restates the previous one.
  
### Fixed

- **Leaving a page while its JavaScript modules were still loading crashed the render loop.** The
  card grid imports three modules on first render, and leaving /collection inside that window
  disposed the component while the imports were in flight. The continuation went on to hand the
  disposed object reference to the next call, which threw as it was serialised and was logged as
  an unhandled exception; it also registered the dead grid as the target the command palette jumps
  focus to. The command palette and the undo accelerator had the same shape and left a document
  listener pointing at a component that had gone.
- **The card grid no longer flickers while scrolling.** `<Virtualize>` was given a row height
  computed from an assumed 1,000px-wide grid; a row is as tall as a tile is wide, so on a 375px
  phone at 6 columns the figure was about 2.5x the truth. It rendered a fraction of the rows the
  screen had room for, saw the gap, and rendered again — on every scroll event. The height is now
  measured from the laid-out row and re-measured whenever the grid changes width.
- **Log a pack keeps its header on screen.** "Add N to collection" sat above a grid of several
  hundred cards, so tapping the last card of a pack meant scrolling back past every card you had
  just tapped to reach it. The bar that did stay counted the cards in the pack — a fixed number
  nobody is tracking — and now reports **picked 3 of 5** instead, which is the figure that moves
  and the one that says when a pack is fully logged.

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
