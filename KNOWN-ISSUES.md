# Known issues

Open problems with what has already been ruled out, so a second attempt starts where the
first one stopped rather than repeating it.

---

## Screenshot import: one screenshot still reads nothing

**Status:** open for one fixture of five, and narrowly. Every screen the import targets now reads end
to end on a real screenshot — both card lists with copy counts, a pack's reveal, and a Wonder Pick
line-up. What still fails is a pack reveal in which **every** card is white-bodied.

### The fixtures

`tests/PackProphet.Tests/fixtures/IMG_115*.jpeg|PNG` are six real 1320x2868 iPhone screenshots.
Develop against these: every bug in this feature was invisible until they were run through the whole
chain, and several passed every synthetic test first.

| File | Screen | Result |
|---|---|---|
| IMG_1152 | My Cards, three across | **6 of 6** cards, **6 of 6** counts (9, 11, 1, 8, 14, 20) |
| IMG_1154 | My Cards, three across, scrolled | **9 of 9** cards, **7 of 9** counts |
| IMG_1153 | My Cards, five across | **6 of 6** drawn cards, 9 blank slots placed |
| IMG_1157 | Opening Results (Wisdom of Sea and Sky) | **5 of 5** cards, pack named as Lugia |
| IMG_1151 | Wonder Pick (Shining Revelry) | **5 of 5** cards |
| IMG_1150 | Opening Results (Team Rocket's Ambition) | **0 of 5** |

IMG_1150 and IMG_1153 are of *Team Rocket's Ambition*, which the vendored snapshot does not have —
its newest set is B4 — so those two can only be used for geometry, not recognition.

### Open: a picture in which every card is pale

The mask that finds cards is "coloured **or** locally detailed", block-wise, and it does not see a
card with a small art panel over a large white body carrying sparse black text. On IMG_1150 every
card splits into an art panel and an attack-text strip, and neither is card-shaped:

```
[36,452 172x140] ar=1.23     [244,472 160x120] ar=1.33   [424,452 172x140] ar=1.23
[36,676 168x40]  ar=4.20     [232,676 164x40]  ar=4.10   [424,676 168x40]  ar=4.20
```

A pale card is now read **as long as something else in its row is found**: the row's own cards fix
the phase, the column pitch says where the remaining slots must be, and the fingerprint decides
whether a card is there. That is how Dunsparce (IMG_1157) and Raticate (IMG_1151) are recovered, and
neither is found as a region at all.

IMG_1150 defeats that because there is nothing to extend *from* — all five of its cards are Team
Rocket's cards, so no row has a seed. Fixing it needs the mask itself to see a white card, and the
obvious ways do not work. It is also the one fixture whose recognition cannot be verified, since its
set is not in the card data; the weekly workflow will change that, and it should be re-tried then.

Things already ruled out:

| Tried | Result |
|---|---|
| Adding "luma differs from the page background" to the mask | Worse everywhere. The three-across gutters are 4px, so blocks straddling them fire and every card in a row merges into one region; at the threshold where IMG_1150 gains anything it finds 2 of 5 and IMG_1152 collapses to a single blob. |
| Snapping each box edge to the strongest nearby gradient | Unreliable. Cards have strong internal edges — the art panel's border — and the snap jumps to them, moving edges up to 10px the wrong way. |
| The same snap aggregated over every card at once | Also wrong. It moved rows that were already correct by 7px, so the strongest aggregate edge is not the card's outer one either. |
| Splitting a wide region into k cards by aspect ratio | Needs the card size pinned first. Without it, a row of toolbar text is explained as six tiny cards. |
| Autocorrelating the edge profile to find the grid's period | Reports a 42px pitch on cards 192px apart. A real screenshot is mostly text, and text carries far more edge energy than the gutters. |

It produces no wrong answers in the meantime: a card not found is a card not applied.

### Open: two of nine copy counts

On IMG_1154 the badge segmentation returns a sliver rather than a digit for the last two cards
(Exeggutor ex and Tangela), so their counts are reported as unknown. Both are cards with a holo
treatment, which is the likely reason — the badge sits over a brighter, busier corner and the
relative ink threshold picks the wrong level. Everything else on both three-across fixtures reads
correctly, and an unreadable digit costs the whole count rather than producing a wrong one.

### What it took, so it is not undone

Four findings that each cost a full round of measurement, and each of which had passed every
synthetic test beforehand:

- **Only the card's window is fingerprinted**, never the whole card, because the game draws gold
  flair over a card held ten times or more and a copy-count badge across its bottom. Whole-card
  sampling recognised one card in nine. A *tighter* window than the current one is worse, not safer:
  the illustration panel alone collapses foil printings onto their plain twins.
- **The card size comes from a high quantile of the regions**, not their median. The mask can fall
  short of a card's edge but never past it, so the largest regions are the honest ones.
- **Rows are anchored from the bottom edge**, and only by cards the screen shows whole.
- **Columns are worked out per row.** A hand of five is laid out three then two, and the second row
  sits half a pitch across; one shared set of columns never looks at its slots.
- **Each cell offers nine crops**, the centre and eight nudged three pixels. The detector cannot
  place a box closer than two to four pixels and the fingerprint is worth ten bits per pixel of
  error. Every crop still passes the same threshold and margin on its own; among those that pass, the
  nearest wins — choosing the widest margin instead is a trap that picks a crop where the card is 40
  bits away and everything else is 43.

---

## Opaque "Script error" on iOS Safari, a few seconds after load

**Status:** open, non-fatal. Everything on the page works; the app's own error surface catches
it and shows a row.

**Symptom.** On an iPhone, on the collection page, the on-page error box shows:

```
[1] js: Script error (no detail available)
target: window (a script threw)
Error object: none (so the throw was cross-origin)
foreign code loaded (elements, modules, fetches): fetch https://cdn.jsdelivr.net
Page: / | standalone: no
at +5.8s after load, page has never been backgrounded this session
```

It fires 3.5–6s after load, which is roughly when the card snapshot finishes and the grid
first renders on a phone. That timing is suggestive, not established.

**Ruled out, and how.** Each of these was a working hypothesis that the evidence killed:

| Hypothesis | Killed by |
|---|---|
| The app's own share feature | Fires on `/`, not on a wishlist. That code is C# with no JavaScript in it. |
| The iOS share sheet raising it over the page | The report says the page has never been backgrounded. iOS backgrounds a page while the sheet is open. |
| A browser extension or content blocker injecting a script | Reproduces in a Private tab. |
| A foreign `<script>` element on the page | None — and the check now covers dynamically imported ES modules and fetches too, which `document.scripts` misses. |
| The CDN card-data fetch failing | `CardDataLoader` catches a CDN failure and falls back to the vendored snapshot by design. |
| An unsupported browser API | The modules that load on `/` use only `IntersectionObserver` and `MutationObserver`, both long-supported on iOS. |

**One row of that table has since been narrowed.** "The CDN card-data fetch failing" was killed
on the grounds that `CardDataLoader` falls back to the snapshot — true of a fetch that *fails*,
but until the CDN requests were given a deadline it was not true of one that *hangs*. That does
not revive the hypothesis here: on the phone the page loads and everything on it works, so the
loader plainly returned. It only means the fallback covers less than the row assumed, and the
row's reasoning now holds for both cases rather than one.

**Does not reproduce** in a desktop browser at the same LAN origin, so it is not simply a
consequence of the app being served over plain HTTP from an IP address.

### The test to run next

Safari withholds the message, file and line from the page itself, so no amount of in-page
instrumentation will produce them. Web Inspector is not subject to that.

1. iPhone: **Settings → Apps → Safari → Advanced → Web Inspector**, on.
2. Mac: **Safari → Settings → Advanced → Show features for web developers**.
3. Connect the phone by cable, open the app on it, then on the Mac:
   **Develop → \<the iPhone\> → the PackProphet tab**.
4. Reload the page with the console open and read the real error.

What to look for: the file and line, and whether the frame below it is one of this project's
modules, the .NET runtime (`dotnet.js` / `blazor.webassembly.js`), or neither. That single
fact decides whether this is the app's bug or the runtime's.

### If Web Inspector is not available

Force the vendored snapshot instead of the live CDN and see whether the error survives. That
exonerates or implicates the only cross-origin loader on the page, which is the one thing the
in-page report can still see but not explain. It is a weaker test than the inspector: it
narrows rather than answers.
