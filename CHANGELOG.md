# Changelog

Notable changes to PackProphet. Dates are ISO. Versions follow
[semantic versioning](https://semver.org) once there is a release to be compatible with;
until then the minor number tracks the roadmap phase.

### Unreleased

- **A set with no logo yet says its name where the logo would be.** The log screen draws each pack
  under its set's wordmark, and a set is loggable the day it goes live — days before the community
  CDN publishes its logo, which left a blank strip above the booster on a tile whose neighbours are
  all wordmarks. The name now stands in, at the size and in the place the logo would have taken. It
  is drawn by the image's own pseudo-element, which a browser renders only while the image has
  nothing to show: no error handler, no class to write, and no state to get stuck in — the failure
  mode an `onerror` on this screen had the first time round. It covers the wait as well as the
  absence, since the box's height is fixed either way and nothing moves when the logo lands

- **Art that never arrives is drawn, not left blank.** Only the card grid could tell a failed load
  from a pending one: everywhere else the art is a background image, which reports nothing, so a
  missing scan left an empty grey box — every thumbnail, the detail art, the deck and chase faces,
  the hit strip, the Wonder Pick slots. They now paint a stand-in underneath the art, which the art
  covers when it loads: the outline of a card (a booster where the box is a pack) on a faint weave.
  The grid tile takes the same outline behind the set and number it already showed, plus the card's
  name above them wherever the tile is at least 130px wide — which is where a wrapped name is read
  rather than deciphered, and which on a phone is the only place the name is drawn at all. The 32px
  list thumbnail, which showed nothing at all, takes the outline alone. One drawn language, whether
  the CDN is a day behind on a new set or a single scan is missing

- **A muted error that is provably not this page's is counted rather than shown.** Cross-origin
  throw, no wrapper caught anything, no cross-origin subresource on the page: that is the signature
  of a script the browser injected, and every third-party iOS browser injects one. A red panel on a
  visitor's phone for another program's bug is noise. Nothing is lost — a genuine throw from this
  app's own code now arrives through a wrapper with a full stack, .NET exceptions still come through
  `console.error`, and if anything real does fire, the suppressed count and the last full report are
  shown as the row above it, so the context of a real error is never hidden. `window.diagMuted()`
  returns them on demand
- **The opaque iOS "Script error" is not this app's bug, and the reports now say so.** It is a
  third-party iOS browser injecting a script that throws. Safari on the same phone, on the same URL,
  is clean; Chrome, Firefox, Edge, Brave and DuckDuckGo all show it. Every browser on iOS is WebKit,
  which is why "only on iOS" read for four rounds as a WebKit problem — but the third-party ones are
  WebKit inside an app that injects its own code into every page, and that code is not a page
  subresource (so `document.scripts` and resource timing never see it), is not same-origin (so the
  message, file and line are stripped), and runs at document start (so it throws at `+0.0s`). The
  clinching measurement was a probe page whose entire script content is the error surface itself,
  with no import map, no runtime, no CDN and no app code: it still threw. `KNOWN-ISSUES.md` keeps
  the eleven hypotheses that were eliminated to get there, including the two that were mine and
  wrong, and the one row of the original table that had killed the right hypothesis with the wrong
  evidence — a Safari private tab suppresses *extensions*, never the code the browser app itself
  injects
- **Every report names the browser, and whether it is one that injects scripts.** Checked against
  the real user-agent strings of Chrome, Firefox, Edge, Brave and DuckDuckGo on iOS, and of Safari,
  which it correctly reports as carrying no wrapper. That one line is what turns this error from
  unexplained into not-ours, so it is the first thing any future report should be read for
- **Every error report names the version of `diag.js` that produced it.** A phone reading a cached
  copy produces an old report that looks like a current one, and two readings taken from different
  versions of the file were compared without anyone realising. The stamp makes that unmistakable
- **`tools/DiagProbe/probe.py` settles the import-map question in three page loads.** Three pages,
  identical except for the import map — the real one, the same one without its `integrity` key, and
  none at all — with no runtime, no CDN and no app code on any of them, so whichever shows the
  error names the cause. It serves `diag.js` out of `wwwroot` rather than keeping a copy, because a
  probe testing its own stale duplicate of the error surface is worse than no probe
- **The copy button on the error box works on a phone, which is the only place it was needed.**
  `navigator.clipboard` does not exist outside a secure context, and the case that box exists for is
  a phone reading a LAN address over plain HTTP. Written as `navigator.clipboard?.writeText(...)` it
  was optional chaining onto `undefined`: the button did nothing at all, silently, which is worse
  than having no button. The real API where there is one, and otherwise the report goes into a field
  with its contents selected, so the OS copy menu can finish the job
- **Failed subresources are reported instead of vanishing.** `window.addEventListener('error', …)`
  without `capture` never receives a resource failure — those fire at the element and do not bubble
  — so the `e.target.tagName === 'IMG'` guard sitting inside that listener, which reads as though it
  did, had never fired once. Every blocked or failed script, stylesheet and icon on the page was
  silently unreported. Now listened for in the capture phase and named, with the `integrity` and
  `crossorigin` attributes when it carries them, since those are what make a same-origin file fail
- **The muted report says where the HTML parser had got to, and what the import map is.** The
  instrumented report from an iPhone put the throw at `+0.0s` with no cross-origin code loaded at
  all — so it happens while the page's own scripts are still being parsed, not when the grid
  renders, which is what `KNOWN-ISSUES.md` had assumed for four rounds. `readyState` plus the count
  of script elements reached places it among them, and the import map is printed because a map the
  browser objects to is reported with no script behind it, which is that report's exact shape
- **The error box can now read a cross-origin throw, which Safari refuses to describe.** The
  opaque `Script error` on iOS in `KNOWN-ISSUES.md` had one line under it saying no in-page
  instrumentation could ever produce the message, file and line, and that was wrong. Safari mutes
  the *report* it hands `window.onerror`, not the `Error` itself: a `try`/`catch` inside a
  same-origin script sees the whole thing, whatever origin the code came from. So `diag.js` wraps
  every asynchronous entry point the app's own JavaScript uses — `setTimeout`, `setInterval`,
  `requestAnimationFrame`, the three observers, and every event listener — and reports what it
  catches with the stack and the kind of callback it came from. Checked against a script served
  from a second origin: routed through a wrapped timer it reported its real message, file and line;
  raised synchronously, bypassing every wrapper, it still arrived bare. Wrapping listeners means
  the function the browser holds is no longer the one the caller added, so `removeEventListener`
  translates through a weak map of wrappers — without that every `dispose()` in the app would
  silently stop removing anything. Identical throws collapse onto one row with a count, because a
  callback that throws on every animation frame would otherwise fill the screen in a second
- **The muted report says the two things that are left to say.** How many of the app's own
  callbacks have thrown — zero, alongside a muted error, means the throw came from no timer, frame,
  observer or listener this app registered — and whether the origin is a secure context, since a
  worker is a separate script origin whose unhandled errors also reach the page muted, and over
  plain HTTP to a LAN address no worker can be registered at all. Between them the next report from
  a phone answers the question either way instead of restating it
- **A test keeps the net complete in both directions.** Every entry point the app's JavaScript uses
  is wrapped, and a *new* kind of callback — a `requestIdleCallback`, a `PerformanceObserver` —
  fails the build rather than quietly escaping the wrappers and costing another round of this
  months later, when the only symptom would be one more report with nothing in it
- **The app has a palette of its own, and it is the default.** *Paper* is warm neutral surfaces
  and ONE accent. Three stacked warm greys carry the depth, the ink is a near-black rather than a
  tinted one, no trim or divider carries a hue, and Pokéball red appears only where something is
  chosen or wants care — which leaves card art as the only saturated thing on a page of cards.
  *Slate* is the greys and blues the app shipped with, unchanged, for anyone who preferred them
- **The pack from the app icon sits beside the wordmark in the top bar**, redrawn rather than
  scaled. The icon's version is a filled shape — a white pack on an indigo tile — and neither half
  of that survives at this size: on a light bar a white pack is invisible, and a mark carrying its
  own two colours was the one thing on the page the palette had not chosen. It is an outline in
  `currentColor` instead, so it is the ink colour in both themes and inverts with them for free,
  which is what every other glyph in that bar already does. Redrawn also means simplified, on the
  grounds `favicon.svg` gives for being a different drawing from `icon.svg`: at 18px the tear
  strip's dashes and the diamond's outline turn to mush, so the strip is one line and the diamond a
  solid. The 8-degree tilt is kept — it is what stops the shape reading as a plain rectangle
- **The same pack replaces the `+` on the phone's centre tab.** A plus said "this one writes
  something" without saying what, and the label under it already reads Log; the pack says what is
  being logged. It is one component drawn once — a 24 viewBox and a 1.8 stroke, matching the other
  tab glyphs exactly — because path data copied into two files is path data that disagrees with
  itself after the first tweak, and a test asserts both homes use it rather than their own copy. The
  glyph needs nothing said about colour in either place: it is near-black ink on a light bar,
  near-white on a dark one, and white inside the filled red tab, all from `currentColor`
- **That replaced a greyscale filter over `favicon.svg`, which had a failure mode `currentColor`
  does not.** Greyscale mapped the icon's indigo tile to `#454545`: 8.6:1 against the light bar and
  1.7:1 against the dark one, so it needed a second rule inverting it in dark to stay visible at
  all. One colour that is already the body colour cannot be under-contrasted against a surface the
  body text is readable on. The test that guarded the filter is replaced by the invariant with teeth
  — the mark names no colour of its own — asserted over the markup, since a hardcoded fill is a
  property of how the SVG is written. Hidden under slate, which shipped without a mark, and the
  browser tab keeps the icon in full colour
- **The mark was called `.mark`, and Bootstrap owns that name.** `.mark, mark` is the highlight
  element: `padding: .1875em`, a colour, and a background of `--bs-highlight-bg`. So the glyph came
  out in a pale yellow box in light and an olive one in dark, padded, with its ink overridden — and
  it read as a broken asset rather than as a name collision, which is how the stylesheet's existing
  note on why `.fold-hint` is not called `.hint` was arrived at as well. Renamed to `.pack-mark`,
  and there is now a test that takes the classes this app coined for its own components and fails
  any that Bootstrap styles on the bare class. It is a named list rather than a scan of the markup:
  the app uses Bootstrap's classes on purpose everywhere and overrides plenty of them, so colliding
  is only a defect for names the app invented, and no scanner can tell which those are. It has its
  own guard — the check must fire on `mark`, `badge` and `btn`
- **The mark is 1.3rem against the wordmark's 1rem**, so it reads as slightly the larger of the two
  without outgrowing a bar whose controls are 2rem, and in rem so it tracks a reader's type scale
- **It is a SECOND setting rather than two more values on light/dark**, because every palette has a
  light and a dark form: picking a look does not pick a brightness with it, and someone whose phone
  flips at sunset keeps the palette they chose. Chosen on **Settings › Appearance** or from the
  command palette; the top bar keeps its one cycling button for light and dark, since it is already
  the widest thing in a row that has to survive a phone
- **The accent does two jobs, and they are separated by value rather than by hue.**
  `--bs-primary` is "chosen" — selection, active, focus, and the 6–22% tints that mark an owned card
  or one already in a deck — and `--bs-danger` is "careful". Both are red, two-to-one apart in
  luminance. That is deliberate: a second saturated hue is what made the first attempt at this skin
  read as a logo painted onto a layout. The cost is honest — this skin tells "save" from "delete"
  by weight where slate had blue against red — so a test now pins the gap at 1.8:1 and asserts
  which of the two is the deeper one, in both brightnesses, because the roles invert between them:
  on a near-black page a deep burgundy cannot also be the border of the storage alarm
- **Corners moved too, since geometry dates a layout as much as colour does.** The file's radii were
  sixty scattered numbers; the three that mattered are now tokens carrying exactly the old values,
  which is what lets a skin raise them as a set — a page where the panels got rounder and the chips
  did not looks broken rather than rounder. Bootstrap's own radius family is raised alongside them
- **Depth instead of lines, in the two places lines were doing a third job.** The top bar and the
  tab bar already separate themselves with a fill, so their hard 3.4:1 rule drops to a hairline and
  a two-layer shadow does the lifting. The border stays 1px and only changes colour: the tab bar's
  height is reserved by `main` as `--tab-bar-h + 1px` and the desktop More menu positions itself
  under the top bar, so a border that changed WIDTH would hide a strip of the last row of content
  behind the bar on every phone
- **Both palettes are measured, not eyeballed.** The contrast test covers the new skin the same way
  it covers the old one, and reads the surfaces out of the stylesheet rather than remembering them:
  every outline button, every filled button's white label, the quietest copy, the want heart, the
  count badge, the structural border, and the accent in its ink form. Twenty-two new cases. Two are
  worth naming — the paper dark border is a deliberate 3.3:1 where Bootstrap's own dark border
  manages 1.9:1, because a near-black page flattens a faint edge more than a light one does; and
  the dark accent needs a second value for its six text uses, since fill-safe under white text
  measures 3.4:1 as text on the raised surface
- **The skin is stamped on the root element before the first paint**, by the same script that
  stamps the theme, so a cold start never shows the wrong palette for a frame — and the browser
  chrome and the install splash follow it too
- **An older save adopts the new default.** The skin is unset until someone chooses one, and an
  unset field is not written at all, so nothing needed a schema bump and nobody is pinned to the
  look their browser happened to save
- **`background_color` is the icon's indigo rather than the page colour**, so the launch screen and
  the app icon agree. `theme_color` still tracks the palette, since that one colours the chrome
  around the page.

  Recorded next to it, because it looks like a stale value and is not: iOS insets the app icon
  inside a white tile in the Add to Home Screen PREVIEW sheet, and none of this affects it. A
  control carrying the icon declarations from before any of these changes and a candidate declaring
  `purpose: "maskable"` icons were added side by side on an iPhone: both previewed inset, and both
  landed on the home screen correctly and full-bleed. So the preview is iOS's own presentation, it
  is seen once, and there was no regression to fix. No icon file changed
- **The command palette's selected row is legible in every skin.** Its two quiet columns — the
  kind on the left, the subtitle on the right — were white at 80% alpha on the highlight fill,
  which composited to 3.42:1 against the slate skin's primary and 3.60:1 against the paper skin's
  dark one, both under the 4.5:1 floor for text that size. This predated the skins and nothing
  covered it. Raising the alpha was not an option: no value below full opacity clears the floor on
  any of the three fills that row is drawn on, since Bootstrap's own #0d6efd only reaches 4.50:1
  under solid white. So the alpha is gone, and the hierarchy rests on what was already carrying it
  — .74rem uppercase against a 600-weight label, with the subtitle right-aligned. The check reads
  the colour out of the stylesheet and composites it onto each skin's primary, so a reintroduced
  alpha fails it; two gaps in the test's own plumbing came out with it, a property match that would
  read `background-color` when asked for `color`, and a hex parser that threw on `#fff`

- **The app has its own domain: [packprophet.spibane.com](https://packprophet.spibane.com/).** The
  deploy used to rewrite the base href to `/PackProphet/`, which is what project-page hosting needs
  and what a custom domain must not have — every asset would be fetched from a path the host does
  not have, and the site would come up blank on every route while the deploy reported success. The
  href committed in `index.html` is already the domain root, so nothing rewrites it now and the
  workflow fails if that ever stops being true. The domain travels in the published output as a
  `CNAME` file rather than living only in the repository settings
- **The site's own lists are now "chase lists", and "wishlist" means the game's 20-slot board.**
  The app had a Wishlists page and an "In-game wishlist" page, which is one word doing two jobs —
  and the more-menu called the second one "In-game list", which named nothing at all. The game
  calls its own board a wishlist, so that is the word it keeps; the app's own hand-built lists are
  chase lists, which is what the README already called the cards on them. `/wishlists` is now
  `/chase`, and the board page is simply **Wishlist**
- **Saved chase lists survive the rename.** The stored spelling changed with the name, so a save
  written before this carries `wishlists` where one written after carries `chaseLists`. Schema v4
  moves them across on read, along with the list/grid layout toggle. These are lists built by hand
  whose only copy is in the browser: reading an older save as "no chase lists" would not look like
  a migration that was skipped, it would look like the app had lost the lot. The old field is
  cleared as it is read, so a save never carries both spellings at once
- **Share links are unaffected.** Only the C# type behind a shared list was renamed, not the fields
  it writes, so a link made before the rename decodes exactly as it did
- **Housekeeping found while reviewing the above.** A tap that chose no readable pictures — a
  cancelled picker, or a mis-tap on a whole camera roll — cleared the results already on screen
  before it worked out there was nothing to read, so a mis-tap cost someone their place in a batch
  of eight. The progress notice opened on "picture 0 of 3", because the first render happens at the
  handler's first await and the counter had not moved yet. The rule for cycling parallel-foil copies
  was written out in three places that could disagree; there is now one, and the helper that always
  existed for it is the one being called. `ShotImport.Clear()` and `CountReader.ExemplarCount` were
  public, documented, and called from nowhere
- **The screenshot import takes several pictures at once.** A sitting is several pictures: packs
  get opened in a run, Wonder Picks come in a batch, and the My Cards list runs past one screenful
  so shooting it in a few goes is the normal case rather than the exception. Up to twenty per go,
  each read on its own and reported under its own filename, with a failure naming the file it came
  from rather than saying "that file" beside four others. Choosing a different card layout still
  re-reads the whole batch for free, because the pixels were measured once and only their meaning
  changes
- **What the pages do with a batch differs, because the pictures mean different things.** A pack
  and a Wonder Pick are separate events, so those two screens offer each reading its own button and
  are worked through one at a time. The collection is one thing, so its import merges the readings
  and applies them as a single edit with a single undo step — not one apply and one undo per
  picture
- **Merging is also the only way to get overlapping screenshots right.** Two shots of the same card
  list overlap, and a card can be a blank slot in the screenful taken before you scrolled and a
  recognised card in the one taken after. Read on its own, that first picture says to delete it.
  Marking cards as not owned is the one destructive half of this import, so a card found in any
  picture is never marked missing on the strength of another
- **A whole row of copy counts could go unread, and the reason was one pixel.** The count badge is
  a dark ribbon with bright artwork at both ends — a slanted right edge and a rounded bottom-left
  corner. A column span's height was measured from its topmost ink to its bottommost, so when a
  stray bright pixel on the badge's top row happened to land in the same columns as that bottom
  corner, an empty span measured as tall as the badge itself. It then became the tallest thing on
  the card, and the filter that separates digits from the marks beside them threw away every real
  digit for being shorter than it. Three cards in a row came back with no count at all while the
  rest of the same screenshot read perfectly, which is what made it look like a property of those
  cards. A span is now measured by its tallest unbroken stack of inked rows — every digit has ink
  in every row of its own box, and two specks 27 rows apart cannot fake that — and a digit must
  fill at least 40% of the ribbon's height, which the corner and edge artefacts never do. Two cards
  on an older fixture whose counts had always been reported as unreadable turn out to read as 5 and
  4

### v0.5.1 - 2026-08-28

- **Housekeeping before the first public test** — a lighter first visit, a smaller repository, and
  one fix for what a brand-new set looks like before its art exists
- **A card with no art shows its set and number instead of a broken-image marker.** A set is
  playable in-game days before the community CDN has scanned its cards, so the app has the card
  data and none of the pictures — and every tile in the set failed at once, which read as the app
  being broken rather than the set being new. The tile now stands the set and number in the art's
  place, the same pair the game prints in the card's own corner. Past ten columns the set line
  drops and the number stays, since the grid is usually filtered to one set anyway. The list
  layout gets no stand-in on purpose: that row already prints the same id in its own cell
- **[AI-DECLARATION.md](AI-DECLARATION.md)**, stating which parts of this project were written with
  an AI assistant and which were not
- **164 KB less to download on a first visit**, measured over the service worker's precache:
  - **System.Text.RegularExpressions is no longer shipped** (111 KB gzipped, now a 8 KB stub).
    Five patterns needed it — a set-code parse, two damage-phrase matchers and two artwork-filename
    shapes — and all five were simple enough to scan by hand. Two of them described the same
    filename format in two places, so they are now one parser rather than two descriptions that
    could disagree. Also faster: the variant index is read inside the odds engine's per-card loop,
    and it was a regex match per card
  - **cards.extra.json is gone** (57 KB gzipped, 787 KB on disk). It had been replaced because its
    stats were wrong, and nothing had loaded it since — but it still shipped and was still
    precached
- **The deploy artifact is 11.6 MB rather than 19.7 MB.** The SDK writes a `.gz` and a `.br` beside
  every published asset for a host that serves precompressed files. GitHub Pages does not; it
  compresses on the fly, and nothing in the published output refers to the sidecars. Users receive
  identical bytes
- **8.25 MB of vendored Bootstrap.** The whole `dist` was committed while `index.html` links two
  files out of it: the unminified copies, right-to-left variants, ESM builds, the grid/reboot/
  utilities subsets and 900 KB of source maps are gone. The service worker had been carrying a
  lookahead rule to keep them out of the precache, which no longer has anything to match; a test
  now fails if the whole dist is ever re-vendored
- **Seven pages had their own copy of the same rarity lookup**, written two different ways, and a
  shared helper for it already existed — doing a linear scan when the ladder has a dictionary. One
  lookup now, and the shared one got faster
- **Four pages each had their own scope picker and their own scope reader**, and they had drifted:
  two spelled a set `set:A1` and two spelled it `A1`, and only one knew about whole-series and
  all-wishlist scopes. One `ScopePicker` component and one `AppSession.TargetForScope`, which reads
  every spelling any of them ever produced

### v0.5.0 - 2026-08-28

- **Phase 4's headline feature**, taken ahead of Phase 3's remaining localisation because it is the
  larger of the two. Reading cards off a screenshot of the game: a set's card list, the five cards
  from a pack, or a Wonder Pick line-up
- **Cards can be read out of a screenshot.** Every card's artwork was reduced to 128 bits offline
  and the 3,761 fingerprints ship with the app, so recognising a card is a Hamming distance against a
  150 KB text file. No model, no service, nothing uploaded, and it works offline — the fingerprint
  table and the reader are both precached, which is asserted rather than assumed
- **Each import sits where its answer is useful**, rather than in one place that then asks what you
  meant. The same five cards mean different things in different places, and a choice offered in the
  wrong place is a wrong answer waiting to happen:
  | Screen | Where the import is | What it does |
  | --- | --- | --- |
  | Opening Results | Log a pack | Works out which pack from the cards, and fills the log in |
  | Wonder Pick | Wonder Pick | Feeds the five cards into the appraisal; changes nothing |
  | My Cards, five across | Collection | Whole set, blanks for what is missing — reads both ways |
  | My Cards, three across | Collection | Only what you own, with copy counts |
- **A pack identifies itself from the cards that came out of it.** A card lists the packs it can come
  from, so the packs that could have produced a whole hand are the intersection of five short lists.
  Genetic Apex gives each pack about 80 exclusive cards against 46 shared, so one exclusive card in a
  hand settles it — and a hand of five nearly always has one. Where the cards genuinely do not
  narrow it, which is what a God Pack looks like because it holds only the rarities every pack
  shares, the shortlist is offered as buttons rather than a one-in-three guess made. The log screen
  then sits exactly where it would after picking the pack and tapping five cards, and no further:
  the same button commits it, against the same grid, so a card read wrongly is visible first
- **The two card lists are read differently, and the difference is a safety rule.** The five-across
  list draws unowned cards as blank slots, so a blank is a card known to be missing and the numbering
  around it supplies the name. The three-across list leaves unowned cards out instead — so a gap in
  it means "not shown", which may be unowned, or on the next page, or filtered. Positional reasoning
  is therefore switched off on that list rather than merely allowed to fail, and it can never report
  a card missing. A coincidental arithmetic run is all it would take to propose deleting a card
  someone owns
- **The ownership list is anchored per row** rather than once for the whole grid. The slot detector
  tiles the entire screenshot, and a real one has a status bar above the list, navigation below it,
  and a set heading part-way down interrupting the grid — one screen-wide offset would be thrown off
  by any of those, where a row of furniture recognises nothing and so anchors nothing
- **It declines to guess.** Two cards whose artwork is too alike to choose between, a slot matching
  nothing, a list sorted by rarity rather than by number: each comes back as a slot that was not
  recognised and is left out of what gets applied. A slot with art in it that could not be named is
  counted, never reported as missing — the reason it was unreadable might be a foil or a crop, and
  calling it missing would delete a card the user owns
- **Marking cards as not owned is off until asked for.** It is the only half of an import that can
  destroy something; everything else adds. The whole import is one undo step either way
- **The page says which sets it cannot recognise yet, and when the table was built.** Card lists come
  live from the community CDN, so a new set is browsable within days; fingerprints cannot work that
  way, because generating one means downloading the art. Between those two moments a set is fully
  browsable and completely unrecognisable, and a screenshot of it reading as "no cards found" would
  look like a broken feature rather than a dated table
- **`.github/workflows/card-hashes.yml` closes that gap weekly.** It looks for cards the table has
  never seen, downloads only those, and opens a pull request if it found any — listing every set
  still short of its card count, since a set far short of it is artwork upstream has not published
  yet and a later run will pick it up. The stamp in the file header is ignored when deciding whether
  anything changed, so a quiet Monday does not produce a pull request to close
- **`tools/CardHashGen`** generates the table. Deliberately absent from `PackProphet.slnx`: it needs a
  native WebP decoder, and `dotnet test` resolves the solution, so including it would put SkiaSharp on
  the deploy path for no reason. A run merges rather than replaces, and a run that fetches less than
  two thirds of what the table already held is treated as an outage and writes nothing
- **The artwork independently confirms the ownership model.** `PocketCard.OwnershipKey` has claimed
  since 0.1.0 that the artwork filename is a card's identity, and `CardIndex` counts 3,546 ownable
  cards among 3,761 entries on the strength of that claim. Fingerprinting every card's art arrives at
  the same number from the pixels: **3,546 distinct fingerprints**, with 214 of them shared by 429
  entries, because a reprint is the same picture. Two independent routes to one answer, and now a
  test
- **Only a card's window is fingerprinted, not the whole card**, and this is the change that made
  recognition work on a real screenshot at all. A card on screen is not its artwork file: the game
  draws a gold flair border over any card held ten times or more, prints a copy-count badge across
  the bottom, and clips the last row at the screen edge. Sampling the whole card scored **0 bits**
  against screenshots built from the artwork files and recognised **one real card in nine** — the
  flair alone was worth 19 to 26 bits of error. Sampling everything but the frame and the bottom
  sixth recognises **six of six** whole cards on the fixture, at 4 to 12 bits of 128, with no other
  card within the threshold.
  Two things fell out of measuring it that reasoning had got backwards. **A tighter window is not a
  safer one**: the illustration panel alone matched about as well and collapsed 58 cards into
  indistinguishable pairs, because a foil printing differs from its plain twin mostly *outside* the
  panel. The wider window separates those by 18 to 25 bits, and the table is back to 3,546 distinct
  fingerprints — the ownable-card count exactly. And **insetting buys nothing against a badly located
  card**: shifting the box 8px on a 192px card costs about 40 bits whichever window is used, because
  a shift moves the sampling grid wherever its edges are
- **The ambiguity rule is keyed on the ownable card, not on the fingerprint.** It used to wave
  through any rival carrying the winner's exact fingerprint, on the grounds that reprints share
  artwork. Once the card's frame was outside the window that reasoning broke: a foil printing differs
  from its plain twin in nothing but the frame, so it sits at distance 0 from a *different* ownable
  card and was being treated as the same answer — which would have recorded the wrong printing
  silently. `ArtHashTable` now returns ranked candidates and `ScreenshotReader` groups them by
  `OwnershipKey`, because it is the only side that knows which card an entry belongs to
- **Columns are worked out per row, which is what made the pack reveal and Wonder Pick screens
  work.** A hand of five is laid out three then two, and the second row sits half a column across
  from the first — so one set of columns shared by both rows describes neither, and the second row's
  slots were never looked at. That is where the missing cards were: a **white-bodied card has no
  colour for the mask to catch and only sparse text**, so it is not found by looking at it at all.
  It is found because the other card in its row fixes the phase, the column pitch says where the
  second slot must be, and the fingerprint decides whether a card is there.
  Dunsparce on a Wisdom of Sea and Sky pack reveal and Raticate on a Shining Revelry Wonder Pick are
  both recovered this way, taking both screens from four of five to **five of five**. The pack is
  then named from the cards — three of those five are exclusive to Lugia — so logging a pack from a
  screenshot needs nothing said about which pack it was
- **A card is offered to the matcher as nine crops, not one**, and this is what made the Wonder Pick
  screen work. The detector puts a box within two to four pixels of a card's true edges and cannot
  reliably do better — the box comes from a mask whose extent depends on what the card has near its
  border — while the fingerprint has no tolerance for it: on a real line-up the centre crops score
  21, 22, 21 and 12 bits against the right cards, and crops three pixels over score 5, 11, 12 and 2.
  One card recognised became four.
  Every crop still has to pass the same test on its own — close enough, and clear of the next
  different card by the full margin — so nine crops cannot turn a doubtful reading into a confident
  one. Among those that pass, the **nearest** wins. Taking the one with the widest margin was tried
  and is a trap: margin alone ignores distance, so a crop where the card is 40 bits away and
  everything else is 43 beats one where it is 4 bits away and clear by 20. The centre crop is tried
  first and short-circuits when it already identifies the card comfortably, because nine passes over
  3,761 fingerprints per cell is seconds of work on a phone
- **The three-across card list gives up how many copies you hold**, read off the badge the game
  prints across each card's bottom-left corner. Ten shapes in the game's own fixed-pitch font, matched
  against glyphs cut from the reference screenshots — real digits, not a font this project drew. On
  the two three-across fixtures that is 6 of 6 counts and 7 of 9, with none wrong.
  Two things it does rather than guess. A count is read **completely or not at all**: dropping an
  unreadable leading digit turns 14 into 4, so a partial read is not a smaller answer but a different
  number. And an unread count is **null, never zero** — null means "the screen did not say", where
  zero would mean "you own none" and erase a card. The page shows what it read, says "not read" where
  it could not, and counts those out loud.
  Getting the digits apart needed two things that a bitmap comparison alone did not give. The glyphs
  are compared as **grey ink coverage** rather than black and white, because a digit is about ten
  pixels across and how much of a cell a stroke covers is most of what separates an 8 from a 3.
  And the glyph's **width-to-height ratio is kept separately**, because normalising every digit into
  one box throws it away and it is what tells a 1 from everything else outright — 0.38 against 0.63
  and up. Binary comparison alone misclassified six of twenty-one samples; with both, leave-one-out
  over every digit that has a second sample is 16 of 16
- **Cards are found as regions and framed by consensus**, which is the third detector and the first
  that works on a real screenshot. Autocorrelating the edge profile to find the grid's period reports
  a 42px pitch on cards 192px apart, because a real screenshot is mostly text and text carries far
  more edge energy than the gutters between cards. Finding cards as connected coloured-or-textured
  regions locates them well, and framing each one by its own extent was still eight pixels out —
  a region is the extent of the mask, not of the card, and with the right box a card sits 6 bits from
  its entry against 30 with that one.
  So the regions are used to find the cards and their individual extents are then thrown away: the
  card size and the rows and columns are rebuilt from what all the cards agree on, and every box comes
  from that. Which statistic to agree by took measuring: the **median** of the region sizes is right
  on one fixture and eight pixels small on another, because the mask can fall short of a card's edge
  but never reach past one — so a **high quantile** is the honest one, and it gives the same 192x268
  for both. Rows are anchored from the **bottom** edge, which under-reaches least, and only from
  cards the screen shows whole: one row cut off at the bottom of the screen, used as an anchor, moved
  every other row by 40 bits' worth.
  Together that recognises **6 of 6** whole cards on one fixture and **9 of 9** on the other. Blank
  slots are placed across a full-width list, which is what lets the five-across view say what is
  missing
- **The card box is measured once per screenshot, not once per slot.** This was a real bug, found
  only by testing against real artwork, and it is worth recording because everything synthetic
  passed. The first version cut each card out of its slot by eating uniform lines inward from the
  slot's edges — but "uniform" is a property of the card, not of the gutter. A card with a bright
  border stopped the trim dead; a card whose border blended into the dark background had the trim eat
  into its artwork until it hit its own safety cap. The same thirteen cards that fingerprint **0 bits**
  from the table when read from their art files came back **4 to 43 bits** away when read out of a
  screenshot built from those same files, and five of thirteen were recognised.
  The gutter is now measured from the whole image at once, by folding the edge-energy profile over one
  slot pitch: the card's two outer borders are the only feature every card shares in the same place,
  so they dominate the fold while a bright line inside one card's art blurs away. The card is the
  longer of the two arcs between those peaks. The box now lands **within one pixel** of where the
  cards were drawn, and **13 of 13** are recognised at 2 to 8 bits, against a nearest wrong card at 19
  or more. The measurement is pinned as a regression test
- **The match threshold is 14 bits of 128, not 22.** 22 was the median distance between two different
  cards in the table, which is exactly the wrong place to put a cutoff: a threshold as wide as the
  typical gap between cards will match a card that is not in the table at all — and a set released
  since the last workflow run is precisely a screenful of those. 14 is several times the drift that
  rescaling introduces and comfortably inside the typical gap
- **The pack picker no longer runs off the side of a phone, and the bottom bar stays put.** At six
  columns on a 375px screen the last pack was cut off and the tab bar could only be reached by
  scrolling. One cause, and not the one it looked like: the grid asked for `repeat(n, 1fr)`, which
  means `minmax(AUTO, 1fr)`, and an auto minimum is the track's min-content width — so a track never
  shrank below the longest word in a pack name. Six tiles wanted about 460px, the grid overflowed by
  a hundred, and the browser widened its layout viewport to fit the page, which is what put the
  fixed bottom bar below the bottom of the screen. `minmax(0, 1fr)` is the same layout wherever
  there is room and the only one that degrades. The tile's own labels are told their width too: a
  centred flex column leaves a child as wide as its content, so a name wider than its tile spilled
  over its neighbours instead of ellipsising.
  Checked by a test rather than remembered, in both the stylesheet and the markup, because the
  failure is invisible at a desk — which is where the column count gets changed
- **An import's confirmation is clear of the button that produced it, and offers a button to undo.**
  The "Recorded N cards" banner sat flush against "apply", so it read as part of the control rather
  than as the answer to pressing it. It also said "undoable with Ctrl+Z" — on the device this page
  exists for, that is not an instruction. Both imports now put an **undo that** button in the
  confirmation, and the CSV import's up-front promise is worded without the keystroke
- **The three-across card list no longer explains what it cannot see.** It carried a warning that
  the list shows only cards you own, so nothing in it can say what is missing, and pointed at the
  five-across view instead. The section is an import: it adds the cards it read. The note answered a
  question the screen never raised, and sat on top of the reading, which is the thing there to read
- **Less prose across the screenshot import.** The intro, the layout hints, the checkbox
  explanations, the failure advice, the coverage warning and the how-it-works disclosure were each
  saying in three clauses what one says. Trimmed to the instruction or the fact, with the reasoning
  left in the code where it belongs
- **The log header keeps the pack name on a phone.** With "change pack" spelled out and "picked N"
  beside a button that already reads "add N to collection", a 375px header left the title about ten
  pixels and "Mega Altaria" rendered as "M." — the one thing on the bar that says which pack you are
  logging. The button's word and the duplicated count are dropped below 600px; the arrow keeps its
  full tap target and its label goes to the accessible name

### v0.4.1 - 2026-08-26

- **The card total now sits with the set name and the percentage**, and the grid's own bar stops
  wrapping into one full row and one broken one on a phone. Six things wanted that bar — search, a
  promoted missing-only toggle, the add/remove mode, a chip per active filter, the count and
  grid/list — and at 375px there are 351px to spend. Worse, source order put the count first onto
  the second line and an auto margin pushes it right, so that line opened with 200px of nothing
  while the search box above was squeezed to its 8rem minimum: an auto margin takes all of a line's
  free space before flex-grow sees any of it.
  Three changes, and the bar is two full rows at 375px in every state but one:
  - **The total moved to the set bar**, where it belongs with the figures it is read beside — how
    far through the set you are, and which set that is — and where the row is one line at every
    width. Failed art went with it: it is the caveat that stops the total being the number of cards
    you can actually see, reported out of the grid rather than counted a second time. With four
    things on it the set's own name now shortens with an ellipsis rather than wrapping the row, the
    same contract `.page-head` keeps; the set code leads the label, so what survives identifies the
    set, and the full name is on the title. Wrapping had to be turned off for the name to shorten at
    all — flex breaks a line from each item's base size, so a full-length name broke the row before
    anything was given the chance to shrink
  - **The missing-only toggle is back to being one control**, the ownership select in the
    disclosure with the other five. On the bar it was also the same state said twice: a pressed
    button, and a chip beside it reading "missing only". The chip stays, because it is what says a
    filter is on while the control that set it is folded away, and it is how you clear it
  - **What is left wraps on purpose.** Row one is what a card looks like and what a tap does —
    search, taking the slack the auto margin used to hold, then the mode, then grid/list. Row two is
    what is being shown — a chip per active filter, and the mode warning where there is one. Chips
    come last of all because they are the part there can be any number of, so a third row, when one
    happens, is chips rather than a stranded control, which is what grid/list became when it sat
    after them. The separator goes below 600px: it divides filters from layout only while the two
    share a line.
  Nothing above 600px changed, apart from the count no longer being said twice
- **The filters panel is rows on a phone, not a wrap.** Eleven controls of nine different widths
  broke wherever each one happened to end: the ownership select alone on the first line with half
  the width unused, a pack wrapper sharing a line with "keyboard", the column count between "sweep
  off" and "fill target", every line ragged down the right. Two sizes now, and every line comes out
  flush: full width for the ownership filter and for each labelled chip group — rarity, type, pack —
  which have a heading of their own and an unpredictable number of chips, and half width for
  everything that is one control with one word on it, at a basis that puts two on a row at 375px and
  lets the last row's grow to fill it rather than leaving a hole. One divider survives, the one
  between the filters and the layout controls, turned on its side: a 1px column between two rows is
  a tick with nothing either side of it, and a 1px row between them is the line the panel was
  missing. The others separated groups that no longer share a line. The three labels also share a
  column now — each was as wide as its own word, RARITY 45px against TYPE's 32, so the first chip
  of each row stepped in and out by 13px down the panel and three rows of the same thing read as
  three unrelated ones. Nothing above 600px changed
- **Every control you can type into or open is 16px on a phone.** Bootstrap's small controls are
  .875rem, which is right in a dense bar on a laptop and reads as fine print on a phone — 14px of
  grey label in a 44px-tall box, under body text set at 16. It has a second cost that only shows on
  a real device: iOS Safari zooms the page in when a focused input or select is smaller than 16px,
  and does not zoom back out, so choosing a filter left the layout magnified and scrolled somewhere
  else. Buttons keep .875rem — they cannot take a keyboard, so they cannot trigger the zoom, and a
  one-word label in a bordered pill was not the thing that was hard to read. Control heights still
  come from `--ctl-h`, so nothing that lined up stopped lining up
- **The hover tooltip landed on the card below the one you were pointing at.** It was placed off
  the target's bottom edge, which is right for the 32px thumbnail in list view and wrong for a tile
  three hundred pixels tall: the label opened a whole card-height under the cursor, at the corner of
  the card in the next row. On the last row that fits on screen there was nowhere for it to go — the
  only space below is the sliver of the next row, so it sat jammed against the bottom edge or
  flipped up and covered the row before. It anchors to the pointer within the target now, so it
  opens just under the cursor, over the card it names. A small target is unaffected: the offset is
  less than a thumbnail, so the list view's preview still hangs off the row as it did
- **The whole grid's art blinked out for a frame whenever you scrolled quickly.** Not the tiles
  coming into view — the ones already on screen, art loaded, going blank together and coming back.
  The tiles carried a `@key` and the rows holding them did not, and Blazor matches keys only among
  siblings: with the key one level too deep, the row elements were matched by position, so a scroll
  of a single row made every tile in every rendered row a new key. Destroy, rebuild, all of them, on
  every window change. The `<img>` elements went with their tiles, and their `src` is set by
  `js/imgloader.js` rather than by the renderer, so each one came back with nothing to show while
  the loader worked through them again. Keyed at the row, a row still on screen is matched by
  identity and moved, its loaded images intact
- **Art the browser already had still queued behind the loader's six slots.** The cap exists to keep
  a fast scroll from opening hundreds of streams on one HTTP/2 connection and having the CDN drop it;
  a cache hit opens none, so making one wait its turn bought nothing and cost a tile its picture.
  Urls that have loaded once are now shown straight away, and where the browser can answer in the
  same tick — the memory-cache case, which is most of them — the tile is never painted as pending at
  all. A url that turns out to have been evicted falls back to the managed queue

### v0.4.0 - 2026-08-25

- **A pass over the collection grid and the screens around it**, started by comparing the app
  against two other trackers — [PTCGP Tracker](https://ptcgp-tracker.com) and
  [TCG Pocket Collection Tracker](https://tcgpocketcollectiontracker.com) — and then reworked over
  several rounds of review. Every figure below was measured in the running app at 375x812 unless
  another width is named
- **List mode's switch moved onto the always-on bar.** It is the view that carries the set and
  number, type, rarity, a count you can type into and the printed text — half of what a tile grid
  cannot do — and behind a summary reading "filters and layout" it was findable only by someone who
  already suspected it existed. The density toggle stays inside: it means nothing until you are in
  list mode, and that row has to survive a phone
- **"Missing only" is a button on that bar too.** It is what you reach for after opening a pack, and
  it was option two of a select inside a closed disclosure. The select still offers all six filters;
  this is a shortcut to the one used every session
- **Rarity and type are chips, several at a time.** Rarity was a dropdown that could hold exactly
  one rung and named each one in words the game draws as symbols; it is now a row of toggle chips
  carrying the same glyphs the list view draws, because "the stars and the crown, never mind the
  diamonds" is the question people actually ask. A **type filter** did not exist at all and now
  does, as the fourteen pips. Nothing selected means everything, so an untouched row hides nothing.
  Type comes from the printed detail, which downloads after the card list, so those chips are
  disabled until it lands
- **Each chip row is a labelled group** — `RARITY`, `TYPE`, `PACK` — separated by the divider the bar
  already uses. Side by side on a wide screen they read as one row of twenty-four unexplained
  buttons; stacked on a narrow one, two rows of them. The visible label is also the group's
  accessible name, so the two cannot drift
- **One control height per bar.** Every bar mixed 31px Bootstrap controls, 40px set tabs and 44px
  filter chips, which reads as a row that was assembled rather than designed. The height is a token
  now: 36px on a mouse, 44px on a coarse pointer, so every control in a bar grows together rather
  than singling out the chips. The page header is deliberately exempt — its 3rem is a contract the
  sticky toolbar below it depends on, and at 44px the log screen's header squeezed the pack's name
  down to "R."
- **Choose a pack by its wrapper.** The pack filter was a select listing "Charizard", "Mewtwo",
  "Pikachu"; it is now the three boosters, which is how the game asks the same question. One at a
  time — a card lists every pack it comes from, so "either of these two" is nearly the whole set,
  while "what is still missing from the pack I am about to open" is the question the filter exists
  for. Tapping the chosen pack again clears it. The set tabs get a wrapper beside the code for the
  same reason: B2a and B2b are the same three characters in a different order, and their wrappers
  are not
- **A back-to-top button.** A set is a couple of hundred cards and "every set" is three and a half
  thousand, and every control that acts on the list is at the top of the page and nowhere else. It
  appears once a viewport has been scrolled past, sits clear of the tab bar, and jumps rather than
  animating: a virtualised list of thousands smooth-scrolled renders every row in between and queues
  every image in between for nothing
- **The pack log had no way to type.** A pack holds up to 233 cards and you are looking for the five
  you pulled, so finding them meant scanning the grid five times. It gets the search box the
  collection grid already had, with the picked strip left unfiltered so narrowing never hides what
  you have already logged
- **A sticky strip names the set you are scrolling through**, with its logo beside the name. The
  collection can hold every card ever printed, in set order, and a screen into it nothing said which
  set was under your thumb — the set picker names the *filter*, which in that view is "every set". A
  header per set is impossible inside a virtualised list without breaking the uniform row height its
  scrollbar depends on, so one strip sits under the toolbar and a small script keeps it in step with
  the topmost visible row. Only where the list spans more than one set.
  The logo is 36px tall and the strip about 42px, because a 256x113 wordmark scaled to the cap height
  of the label beside it is a smudge. The set tabs keep their booster wrapper instead: a tab holds
  its picture beside a code and a percentage, and the sliver that leaves is the wrong shape for
  lettering. Same set, two pictures, because the two places have opposite shapes to fill
- **The tile is the art again.** The count, the info button and the heart were all badges over the
  artwork, covering three corners of the only thing the grid layout exists to show. At desk widths
  they sit in a caption strip under the picture — heart, **card name**, count. The name replaces the
  word "info": below the art those six characters label a thing that already has a label, and the
  printed name on a 200px tile is small, stylised and sometimes behind an ex badge. It is styled as a
  name rather than a button, which is how the list view already does it.
  The frame moved from the art onto the whole tile, so the two read as one object. Without it a
  tile's count sat four pixels from the next card's heart with nothing between them, and the eye
  grouped across the gutter instead of down the tile
- **At mobile widths the grid is unchanged**: no caption, the count back to a badge exactly where it
  was, and the tile the height of its art, so a screenful holds as many cards as before. That switch
  is on the same 1056px breakpoint as the tab bar rather than on `hover`, because a desktop window
  dragged narrow keeps its mouse — it was keeping the caption while everything else on the page had
  already become the phone layout. The corner info button comes back in that band, over the art and
  revealed on hover, since a long press is a touch gesture
- **The count badge is smaller wherever it sits on the art.** It was 27% of the tile wide with a
  2.25rem height floor, which on a six-column phone tile of 58px came out 30 by 36 — half the card's
  width and nearly two thirds of its height, for one or two digits. Now 28px square
- **A spinner while art loads, instead of a broken-image icon.** The loader holds an image's `src`
  back until one of its six slots is free, and an `<img>` with an alt and no src is drawn by the
  browser as its broken-image marker — so a fast scroll showed a fault on every tile it had not
  reached yet. "Not loaded" and "will never load" looked identical, and the constant one looked like
  the fault. Pending and in-flight images are hidden with a spinner in their place; the failed state
  is untouched, since the broken marker and the card's name beside it are the most useful thing a
  tile with no art can show — and it is now the only thing that looks like a fault
- **A heart on the tile and on each list row.** Wanting a card was reachable only from that card's
  own page, so recording it meant leaving the set you were looking at, marking it, and coming back.
  One tap, on or off, one copy — wanting four of something is a wishlist-page question, and a heart
  that cycled through counts would give no way to see what it landed on
- **The hearts have a list of their own**, "Want it", made by the first heart. Filling whichever
  wishlist happened to be first would quietly rewrite the one list you built deliberately. It is
  marked as the hearts' list wherever wishlists are shown, and deleting it is safe: the next heart
  makes another
- **In list view the heart has its own column, at the front.** Beside the −/+ buttons it was a third
  small square button in a row of them, doing something completely different, next to the number it
  was most likely to be confused with
- **Not on the tile grid on a touch screen.** A permanent button in the corner of every tile is a
  permanent hazard in the one layout whose whole job is being tapped, and a miss costs a copy of the
  wrong card. The list view keeps its column, and the card's own page has the same one-tap heart —
  present whether or not the list exists yet, which is what makes it a complete route on a phone
- **Where in a pack the chance is.** "Chance of a hit" answers whether a pack helps and hides which
  card in it does the helping: the first three cards come from the common pool, so a collection that
  has finished the commons has all of its chance in the last two, and the same percentage means
  different things in two sets. An expanded ranking row now breaks the chance down by card position,
  merging adjacent positions whose odds are equal — which is what produces the "1st-3rd card"
  reading. Merged from the numbers rather than hardcoded: a set that broke the three-commons pattern
  would come out unmerged rather than wrongly merged
- **The deck page's pack table** showed a bare "chance of a hit" with no way to read it as packs. It
  gets the "packs per new card" column the Which-pack table has, from a figure the ranker already
  returned
- **A wishlist as pasteable text.** The share link is the better artefact and the wrong shape for
  where these trades happen: a Discord thread takes a few lines, nobody opens a stranger's link, and
  a reader with the game open on the same phone cannot follow one and come back. Only what is still
  short, set and number first, rarity on every line — the game's trade rules turn on it
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
  what a plain button looks like. Checked against a deliberately reintroduced violation
- **The filled heart failed the non-text contrast floor in dark.** It is the only thing on a tile
  saying "this is on your want list", so WCAG 1.4.11 asks 3:1 of it; raw `--bs-danger` measured
  **2.94:1** against the caption — the same failure the outline buttons had, and the same pair of
  mixed reds fixes it, now shared as a token by all three places that draw a heart. The empty heart
  was already fine at 4.45 light / 5.15 dark, which is asserted too because its alpha is the product
  of a translucent colour and an element opacity
- **The heart was reachable only with a mouse.** Everything in a tile is out of the tab order — a
  focus stop per card would put thousands of them between the grid and the rest of the page — so the
  grid's contract is that the cursor plus a letter does whatever a tile's furniture does. There was a
  key for the count, for the detail page and for a bulk range, and none for wanting a card. **`w`**
  now toggles it, and the key legend says so
- **The want column's header in list view had no accessible name.** Blank on screen is right; a
  column header announced as nothing is not
- **White on the count badge sits at exactly 4.50:1**, which passes and could not pass by less, so it
  is now pinned by a test rather than by luck
- **The sticky bars were hiding what sticks under them.** The set strip and the list view's column
  headers were both pinned at `top: 0` beneath a toolbar that is also pinned there and paints over
  them — so both vanished on the first scroll, which is the state they exist for. CSS cannot express
  "under my previous sibling" when that sibling's height depends on how many rows it wrapped into, so
  the script that fills the strip measures the bars and publishes the offset for the stylesheet
- **A gap under the pinned header on the log screen**, with the card grid scrolling through it. The
  toolbar below it was pinned at a constant `3rem + 1px`, which border-box rounding left a sub-pixel
  short of the header. Measured now — and it was wrong in the other direction too: once the controls
  grew on a phone the header became 56px and the same constant would have pinned the toolbar nine
  pixels *underneath* it
- **"How to get it" rendered 192px wide on a tablet.** Two columns at those widths put it on a second
  row — in column one, the art column. It spans the full width instead
- **The promo set tab drew a blank grey box where a wrapper should be**, and the two tabs without a
  picture — the promo set and the "all of series" aggregate — were laid out as narrow columns among a
  row of wider tabs. The blank came from asking for pack art through the helper that never returns
  nothing: where the catalogue has no mapping it guesses a URL from the pack's name, which is right
  for a real booster and wrong for a promo set, whose "packs" are the Vol. 1, Vol. 2 groupings
  recording how a promo was given away. The tab asks the catalogue directly now, and every tab keeps
  the same layout with or without a picture
- **The info button and the heart no longer disappear before the touch layout arrives.** Both were
  hidden below a 200px tile, which with six columns happens at about 1200px of viewport — while the
  long press that replaces them does not arrive until 1057px. Between the two there was a mouse, no
  corner button and no way to open a card except the keyboard. In the caption there is nothing to
  collide with, so neither rule is needed
- **Wishlists took Wonder Pick's place in both nav bars.** The four primary destinations are the ones
  you visit while you are handling cards; a wishlist is consulted every time you open a pack or weigh
  a trade, and Wonder Pick is a once-a-day errand with its own screen. It moves into More, which is
  where the rest of the occasional destinations already live. Both navs read from the same list, so
  the swap happened once
- **Backing out of the More sheet no longer taps the page underneath.** On a phone the sheet is
  anchored to the bottom edge, over the tab bar that opened it, so the More button cannot be tapped a
  second time to close it — and the only "outside" left is the collection grid. A popover's
  `::backdrop` is painted but not hit-testable, so that dismissing tap went straight through to
  whichever card was under it. The sheet now has a **Close** button at the bottom, and a transparent
  sibling of the popover catches the tap: it is outside the popover, so light dismiss still fires, and
  it is a real element, so nothing behind it is clicked
- **Contrast, measured rather than eyeballed** — the first of several things that landed before
  this pass and had not been released either. Every text colour on every page was read with
  getComputedStyle in both themes and checked against the surface it actually sits on. Two things
  were under WCAG AA's 4.5:1, both from Bootstrap's defaults rather than from this project:
  - **Outline buttons**, in *both* themes and worse in dark. Against the raised panel they
    measured secondary 4.10 light / 2.84 dark, danger 3.96 / 2.94, primary 3.94 / 2.96 — six
    values, none of them passing, the dark ones close to half. Each hue is now mixed toward its
    theme's far end until it reaches 4.8:1, which leaves room for a later change to the panel fill
    without dropping back under. Border as well as text, since the outline is the control's
    boundary and carries its own 3:1 requirement
  - **`text-body-tertiary`**, the quietest copy in the app — the line under a destructive button,
    the trademark notice. Bootstrap ships it at 50% alpha, which composites to 3.02:1 in light and
    3.78:1 in dark. The hue is unchanged and only the alpha moved, to the first value clearing
    4.8:1 in both themes.
  The rules are written flat rather than nested: nothing else in the stylesheet nests, and native
  CSS nesting is unsupported before iOS 17.2, where a nested block is not degraded but dropped
  whole — which would have restored the failing colours with nothing to show for it
- **Contrast is now a test.** The colours are read back out of the stylesheet and the ratios
  recomputed, so editing one re-runs the arithmetic instead of quietly invalidating a comment.
  Selectors are matched as whole comma-separated entries, not as substrings — matching by
  substring finds `[data-bs-theme="dark"]` inside the light rule's own
  `:root:not([data-bs-theme="dark"])` and reads the light colour as the dark one, which is how
  this test first "failed", reporting a ratio that existed nowhere in the app
- **Three controls were named only by their placeholder** — the deck screenshot picker, the
  share-code box beside it, and the new-collection field in Settings. A placeholder disappears the
  moment anyone types into it, which is also the moment they most need to be told what the field
  was. All three have real labels now
- **The packs-per-day chart says what it shows.** It was `role="img"` labelled "Packs opened per
  day", which names the picture and says nothing about it — and `role="img"` hides an element's
  children, so the thirty bars inside it did not exist for a screen reader. The label now carries
  the range, the total and the busiest day, and the figures themselves sit under it as a real
  table behind a disclosure. Collapsed rather than visually hidden: thirty rows read out before
  the rest of the page is a worse answer than a control that says what it opens, and it is also
  the only way a sighted reader gets an exact figure off a bar
- **Accessibility rules that hold everywhere are now swept over every page**: form controls have
  names, buttons and links have names, images have alternatives, and heading levels never skip.
  Driven from the same reflection list as the render tests, so a page added later is covered
  without anyone remembering. Heading order was already clean; the sweep is what keeps it so
- **Import a collection from another tracker.** A CSV or `.xlsx` export, read entirely in the
  browser and never uploaded. The figures come first — cards, copies, rows read, and any row that
  did not match, named with its line — and nothing is written until a destination is chosen. The
  default is a new collection, which cannot lose what is already there; merge and replace sit
  beside it, and only those two advertise an undo, because switching collections clears the undo
  history and the third would be promising something it cannot do
- **One import pipeline, no per-tracker code.** Every tracker surveyed names a card by the same
  two facts, set code and number, and none of them agree on the shape:
  tcgpocketcollectiontracker.com writes a single `A1-1`, PTCGP Tracker writes a `set_id` of `A1`
  beside a `card_id` of `1` — the same column name the first gives to the whole identifier. A
  header cannot say which it is, so the identifier is reassembled per row: the whole id is tried
  first and the set column consulted only if that fails. Composing first would ask for `A1-A1-1`
  on every row of tcgpocketcollectiontracker's file, which carries both
- **Upper-casing a set code loses eight of the twenty-two sets.** `A1a` becomes `A1A`, which
  matches nothing, so the whole of A1a, A2a, A2b, A3a, A3b, A4a, A4b and their B-series
  equivalents import as unknown rows while every other set succeeds — no exception, no malformed
  row, just a third of a collection quietly missing. A4b is also where all 214 re-listings live,
  so the same bug broke the reprint merge. Set codes are matched against the card database rather
  than normalised by rule. Found by a test that imports all 3,761 entries at once
- **An import collapses and an export expands.** This app keys ownership by artwork; the trackers
  key it by set entry. So 3,761 rows fold into 3,546 cards on the way in — two rows can name one
  artwork — and 3,546 cards are written back as 3,761 rows on the way out. Counts merge by the
  larger of the two rather than the sum: two rows about one card are two opinions about one
  number, and summing would double a Deluxe-set owner's whole collection
- **An `.xlsx` reader**, because PTCGP Tracker exports a workbook and a file that has been opened
  in Excel comes back as one whichever way it started. About 200 lines and no dependency: the App
  ships to a browser, and a spreadsheet library is payload every user downloads to read a file
  most of them never have. It follows the workbook relationships to find the first sheet rather
  than assuming `sheet1.xml`, since sheet order is not file order, and fills sparse cells from
  their `A1`-style references, since reading cells in document order shifts every value left of a
  gap. The kind of file is decided by the zip signature rather than the extension
- **Export the collection as CSV**, aimed at no site in particular: `set`, `number`, `card_id`,
  `name`, `rarity`, `quantity`. The identifier is written twice, in both shapes the trackers use,
  so a reader looking for either finds it — one column of redundancy in place of a guess about
  what some particular site wants. Unowned cards are written as zeros rather than left out,
  because a reader cannot tell "none of these" from "no opinion" and at least one tracker's import
  deletes what a file reports as zero. No byte order mark: Excel would prefer one for the names
  carrying ♀ and ♂, but a reader that does not expect it takes it as part of the first column's
  name and then recognises no column at all
- **Writing for one particular tracker was tried and abandoned.**
  tcgpocketcollectiontracker.com's importer checks that a human-readable `Id` column is present
  and then ignores it, writing by its own `internal_id`. Those are not derivable — the gaps are
  irregular — so a file it accepts would mean vendoring 3,546 of another project's database keys,
  and a stale copy would write correct-looking counts against the wrong cards, in the one column
  a person could not check
- Probing that format turned up something worth recording: their 3,761 entries carry only
  **3,546 distinct `internal_id` values**, and `A4b-1` shares one with `A1-1`. Their internal id
  is an ownership key. Two projects reached the same conclusion independently — that a card
  re-listed in a later set is one card — from the same published data
- The import fixtures are **real exports**, kept byte for byte as the tracker wrote them, in both
  CSV and `.xlsx`. A fixture written by this project would only prove the reader can read its own
  output. Both forms of the same collection are asserted to import identically, card by card
- **A CDN that hung, rather than failed, left every page on "Loading card data…" for good.** The
  fallback to the vendored snapshot only runs when a CDN attempt returns, so a request that is
  neither answered nor refused — a network that drops packets to jsdelivr rather than rejecting
  them — never reached it. Each request that leaves the origin now carries a deadline: five
  seconds on the boot path, thirty on the card detail that loads afterwards, and a hang is
  treated exactly like a failure. Verified by reverting the deadline and watching the new test go
  red
- **Dates on the history chart printed on top of each other.** The axis marked both ends, every
  month start and every Monday, with no rule against two marks landing in the same place — and a
  label is about three days wide. 24 August is a Monday one day before the 25th, so the last two
  dates were drawn over each other; 1 August beside the Monday after it did the same. A mark is
  now skipped when it cannot clear the labels already placed, ends first, then months, then weeks
- The grid's image loader could not honour a request to re-check its images: `refresh()` read a
  variable that was never assigned, so every call threw and was swallowed
- **Licence and source are offered inside the app.** Settings now carries a "Licence and
  source" section linking the licence text, the repository, every upstream data source and the
  trademark disclaimer. AGPL section 13 applies to network interaction, so the offer has to
  reach the running app rather than only the repository
- **The service worker no longer precaches about 663 KB that no page loads** — roughly a sixth
  of a 4.3 MB first visit. Removed: Blazor.Bootstrap's pdf.js worker and sortable list, and
  every vendored Bootstrap variant other than the two files `index.html` links (unminified
  copies, right-to-left builds, ESM builds, and the grid/reboot/utilities subsets). The
  precache rules are now tested by reading the regexes out of the worker itself, since that
  file runs neither in development nor in CI
- **Render tests for the App project** (bUnit), which previously had none. Pages are discovered
  by reflection, so a page added later is covered automatically; each is rendered on a
  brand-new profile and on a populated one. Discovery deduplicates by page type rather than by
  route, because Collection answers both `/` and `/collection` and a repeated theory argument is
  dropped by the runner, which removed the app's main screen from every page-driven test. Every
  regression test was verified by reverting the fix and confirming the suite went red
- **CI runs the tests before deploying.** It previously did not
- The deck QR was **verified against the live game in both directions**. Until then every codec
  test ran this project's encoder against its own decoder

### v0.3.0 - 2026-08-25

- **Phase 3, and a layout pass over all seventeen routes.** Every figure below was measured in the
  running app at 375x812 unless another width is named
- **Shareable read-only wishlists.** A list travels whole inside the link and is decoded in the
  recipient's browser; nothing is uploaded and no account exists. The payload sits after the `#`
  so it never reaches a server as a query string, which also fixed a 414 on a real list
- **An in-game name per collection**, carried into the share link. A want-list told the reader
  exactly what to send and gave them no way to find you: the only name in the payload was the
  collection's, which is what you called a save slot
- **Split a pack budget across packs** — how many of each to buy for a fixed number of packs,
  rather than sinking all of them into whichever pack currently ranks first
- **Pin a pack to the front of the log picker.** A star on each booster; pinned packs lead the
  grid whatever the series dropdown says, which is the trip the pin exists to save. Persisted
  per collection, and drawn as pinned wherever it appears, so a pack from another series never
  reads as part of the one below it
- **Every page was clipping 160px below the fold.** An unstyled wrapper plus a vendored
  `height: 100vh` from a package nothing used. Removing the package alone made it worse
- **A bottom tab bar below 1057px**, and above it a top nav cut to the same four destinations in
  the same order, from one shared menu component. Twelve links inline needed 1242px of a 1265px
  row, so the bar wrapped at every width — well above the point the tab bar takes over
- **One page shell with three named measures**, replacing fifteen ad hoc `max-width` values
  across twenty-six declarations. Body blocks now clear a display cutout; they were padding with
  a flat `1rem`, so in landscape on a notched phone they sat under it while the bars above did
  not
- **Column priority is a container query, not a media query.** A table's width comes from its
  container: the wishlist editor is two columns above 1000px, so at a 1280px window its table had
  614px and needed 680, and every row wrapped — worse the wider the screen
- **The set row, the rarity plan and the board's tuning knobs fold away**, each summarised by its
  own state. Collection opens on the newest openable set rather than the first one printed
- Collection chrome 499 → 156px. Which pack's tools three rows → one at 1280px. Which pack 855 →
  341px, Trades 559 → 288px, the in-game list 911 → 501px. Breakpoints 11 → 9
- **Which pack's breakdown buttons wrap into an even grid on a phone.** Four labels of four
  different lengths in a flex row wrapped three-and-an-orphan at 414px and two-and-two at 375px,
  with neither row's edges lining up. Two equal columns below 600px, and the flex row kept above
  it, where all four fit on one line
- **The ranked pack table lines up with the page again.** Bleeding it to the screen edge on a
  phone put the "pack" heading hard against the bezel and the log button a few pixels off the
  other edge, out of line with every control above. The row tint and the rules still run edge to
  edge; only the text is inset. A floor on the numeric headings keeps the two insets out of those
  columns, so the header comes out at 36px rather than the 50 it was, or the 64 the inset alone
  would have cost
- **The card grid is operable from the keyboard.** It was pointer-only: a tile was a div with a
  click handler. Arrows move, digits set a count outright, Enter adds, `-` removes, `i` opens
  card detail. One tab stop with `aria-activedescendant`, a visible **keyboard** button naming
  the keys, a skip link, and a command-palette action
- **The -/+ buttons meet WCAG 2.5.8.** Two of their four spellings measured 26.67 x 23.00 px,
  under the 24 x 24 floor, in a 50px row with the buttons centred in it. One class now, sized by
  one variable, guarded by a test on the variable
- **The command palette is a real `<dialog>`.** The focus trap, Escape, the inert background, the
  backdrop and focus restoration are the platform's; a hand-rolled trap and an inert wrapper
  around the whole layout went with it
- Tables carry real headers, scroll inside their own wrapper rather than sliding the page, and
  are reachable by keyboard. Eighteen ad hoc alerts became one `Notice` component that also picks
  whether a message interrupts a screen reader or waits its turn
- Two table cells had been given a flex display, which takes a cell out of its row: they measured
  25.8px and 34.6px in a 46.6px row, so the rule sat high in those two columns and level in the
  other seven
- **Paragraphs over 40 words: 45 → 15.** Kept what tells you about the game, your data, or what
  to do; dropped what a badge already says, what justifies the design, and the clause that
  restates the previous one.
  
- **Leaving a page while its JavaScript modules were still loading crashed the render loop.** The
  card grid imports three modules on first render, and leaving /collection inside that window
  disposed the component while the imports were in flight. The continuation went on to hand the
  disposed object reference to the next call, which threw as it was serialised and was logged as
  an unhandled exception; it also registered the dead grid as the target the command palette jumps
  focus to. The command palette and the undo accelerator had the same shape and left a document
  listener pointing at a component that had gone
- **The card grid no longer flickers while scrolling.** `<Virtualize>` was given a row height
  computed from an assumed 1,000px-wide grid; a row is as tall as a tile is wide, so on a 375px
  phone at 6 columns the figure was about 2.5x the truth. It rendered a fraction of the rows the
  screen had room for, saw the gap, and rendered again — on every scroll event. The height is now
  measured from the laid-out row and re-measured whenever the grid changes width
- **Log a pack keeps its header on screen.** "Add N to collection" sat above a grid of several
  hundred cards, so tapping the last card of a pack meant scrolling back past every card you had
  just tapped to reach it. The bar that did stay counted the cards in the pack — a fixed number
  nobody is tracking — and now reports **picked 3 of 5** instead, which is the figure that moves
  and the one that says when a pack is fully logged

### v0.2.0 - 2026-08-23

- **Phase 2: daily-use depth.** The same odds engine applied to the decisions between packs
- **Wonder Pick** — tap the five cards on offer for a take-or-skip verdict. Cost is set by the
  highest rarity *in the offer* rather than by what the offer is worth to you, so offers priced
  above their value to you are flagged
- **Resources** — the three currency systems side by side, never summed, because nothing
  converts between them. Each leads with waste: a full pool has *stopped* regenerating, while a
  pool at two of five has 36 hours of slack
- **Trades** — which single trade deserves your next stamina. Stamina caps trading at ~2/day
  while dust accumulates, so trades rather than currency are treated as the scarce resource.
  Grouped by pack, set or rarity, since the best trade in each is rarely near the top of a
  global ranking
- **In-game wishlist** — the game's own 20-slot board, filled by cost-to-get-otherwise, with a
  minority of slots reserved for widely-held cards. Output is a transcription list in set order
  with swap-level diffs, since the board is retyped by hand
- **Compare collections** — self-trades between profiles, with full profile management
- **Lifetime totals** — enter the game's own counters and the odds check scales to them,
  splitting a lifetime pack count across sets from what the collection implies. Logged and
  imported figures are never mixed, and per-set figures stay logged-only
- **Evolution gaps** on Collection and Card detail, with a grid filter for the printings that
  would close a chain
- **Rarity plan chips** are available in Settings as well as on Packs
- **Pack points** panel gained a "packs until you can afford the rarest card you still need"
  column, and now respects the rarity plan
- Deep link from the pack ranking straight to logging that pack
- **Shares were added to the game during this phase.** A Share is one-way: a friend sends a 1–4
  diamond card and receives nothing back, capped at one received per day per account. It costs no
  shinedust, no stamina and no card given up. Diamonds are therefore removed from the self-trade
  pairing rather than listed under both routes, and the trade queue flags them so a 4-diamond is
  not bought for 5,000 dust
- **Fossils are Trainers, and a Trainer can close an evolution gap.** Omanyte evolves from Helix
  Fossil. Excluding trainers from the name tables reported eleven missing fossils to a player
  who owned every card in the game
- **Obtainability now outranks rarity** when recommending which printing to chase: the promo
  Charmeleon is a 1-diamond while every openable one is a 2-diamond, so ranking by rarity
  recommended the one card that cannot be obtained
- **The points ledger ignored the rarity plan**, so it advised saving for rarities the user had
  excluded — and the Packs page was applying one set's plan to every set
- **Hourglass balances were rounded on save.** The field was seeded from a floored division, so
  1,751 came back as 1,740
- **A stamina pool was blamed for time it spent filling**, reporting waste to someone who had
  never been at the cap
- The evolution-gap bar is dismissible and stays dismissed. Early on nearly every chain is
  missing a stage, so it reports a standing fact rather than a problem
- **Binder view was cut.** The Collection page already lists every card of a set in set-then-
  number order, owned and unowned, with a columns picker; the binder's remaining delta was
  row-width parity with a number that varies by device

### v0.1.0 - 2026-08-23

- **First working version:** the whole core loop, from an empty collection to "open this pack"
- **Pull-rate odds** per pack variant and slot, with expected copies per card. Slot
  distributions and variant appearance rates are both normalised — a slot always yields exactly
  one card, so upstream's 99.996% totals would otherwise leak a chance of an empty slot into
  every card's rate
- **Expected packs to finish a target** as a Poissonised integral, so a demand for two copies is
  the same formula as a demand for one rather than a special case
- **Targets** are an interface: rarity plans, decks, wishlists and arbitrary composites all feed
  the same ranking. Demand carries a *quantity*, which lets a deck ask for two of a card and
  stops the ranking chasing a card you already have enough of
- **Cheapest route per card** across pulling, pack points, trading and Wonder Pick, consulting a
  routing matrix so no route is offered that does not exist at that rarity
- **Pack points** ranked by packs saved per pack's worth of points, not by price
- **Deck codec** — a C# port of the reverse-engineered share format, round-tripped in tests
- **Deck linter** with a distinct *unverified* severity, so a deck with missing stage data is
  not reported as legal
- **Collection** — virtualised grid and list views, counts, filters, per-set progress, bulk
  drag-select, undo/redo
- **Which pack** — the ranking, cost-to-finish in packs and calendar time, target advisor, pack
  points panel, and per-card cheapest routes
- **Log a pack** — pick a pack, tap what came out, points accrue and the ranking updates
- **Decks** — screenshot import of the in-game code, a builder with live legality checks and
  search over rules text, QR export, closest-to-buildable ordering, list and showcase views
- **Wishlists** — arbitrary wanted cards, priced by the same engine, list and showcase views
- **History** — packs over time, predicted against actual, and a showcase of your best hits
- **Card detail**, **Settings** with JSON export/import, and a **command palette** (Ctrl/⌘K)
- **Four assumptions that turned out to be wrong**, each of which would have produced silently
  wrong numbers rather than an error. All are now pinned by tests. Slot rarity codes name rarity
  **rungs**, not exact codes: upstream never names `SAR` anywhere, and counting exact matches
  priced all 113 SAR cards as unobtainable
- Pack variants are not just Regular and Rare — sets have 2, 3 or 4, holding **4, 5 or 6**
  cards, and one variant numbers its slots from 0 while every other starts at 1
- Ownership is per **card**, not per set entry. 3,761 entries are only 3,546 ownable cards; A4b
  re-lists 214 of its 379. Keying by set entry would have priced "complete A4b" at nearly double
  its real cost
- **40% of card names map to more than one card identity** — Eevee has twelve. Text-decklist
  import was dropped as a result, since the ambiguity cannot be resolved from a name alone
- Pack points are a **byproduct** of opening rather than an alternative to it, so the ranking
  uses the ratio of pull cost to point cost. By that measure Immersives are among the worst
  purchases in the game despite looking cheap
- Deluxe packs are limited-time, which re-prices *other* sets when they leave rotation, and can
  make a completion estimate **fall** when the situation gets worse. Estimates therefore always
  state how many cards they cover
- Local-only storage, with a schema version and a migration from two earlier target shapes
- Installable as a PWA with full offline support after first visit
- Light and dark themes, applied before first paint so a dark-mode user never sees a flash
- Light mode's structural lines are drawn at 2.9:1 contrast against the body. Bootstrap's
  default `#dee2e6` on white manages 1.30:1
- Sets that are released but have no published pull rates borrow another set's slot shape,
  labelled as an assumption everywhere a number derived from it appears
