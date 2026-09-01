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

## Opaque "Script error" on iOS: the browser app's own injected script

**Status: not this app's bug, and settled by measurement.** It is a third-party iOS browser
injecting a script that throws. PackProphet is not involved, and there is nothing in the page to
fix. Kept because six hypotheses were eliminated to get here and because the evidence is what
makes the conclusion safe to rely on.

**What decides it.** Safari shows no error. Chrome, Firefox, Edge, Brave and DuckDuckGo on the
same phone, on the same URL, all show it.

Every browser on iOS is WebKit, which is why "only on iOS" read for four rounds as a WebKit
problem. But the third-party ones are WebKit inside an app that injects its own code into every
page — content blocking, autofill, translation, reader mode. That code is not a page subresource,
so `document.scripts` and resource timing never see it; it is not same-origin, so Safari's muting
rule strips the message, file and line; and it runs at document start, so it throws before the page
has done anything. Every field of the report follows from that, and none of it is about this app.

**The probe that proved the page is not involved.** `tools/DiagProbe/probe.py` serves three pages
that differ only in their import map, with no runtime, no CDN and no app code on any of them. All
three threw:

| Probe | Import map | Result |
|---|---|---|
| A | the real one, `integrity` and all | `Script error` |
| B | same, `integrity` removed | `Script error` |
| C | **none at all** | `Script error` |

C is the one that matters. `Parser: readyState=complete, 1 script elements reached (last: diag.js)`
— a page whose entire script content is the error surface itself, with no import map, still throws.
So it is nothing the app loads, writes or runs.

That also retires the import map, which had been the leading candidate and fitted every measurement
up to this point: the SDK writes a top-level `integrity` key that older WebKit does not implement,
and a rejected map is reported by spec as an exception with no script behind it — the report's exact
shape. It was a good hypothesis and it was wrong. B and C killed it in two page loads.

### Where the reasoning went wrong

One row of the original table killed the right hypothesis with the wrong evidence:

> | A browser extension or content blocker injecting a script | Reproduces in a Private tab. |

A private tab suppresses *Safari extensions*. It does not suppress the code a third-party browser
**app** injects — that is part of the app, and it runs in private tabs too. So "reproduces in a
Private tab" never ruled out injection; it ruled out one narrow kind of it, and the row was then
treated as closing the whole question.

The timing was the other wrong turn. The first report said 3.5–6s, which is about when the card
snapshot finishes and the grid first renders, and four rounds of hypotheses were built on that
coincidence. The instrumented report put it at `+0.0s`, before the runtime boots and before any
CDN request is made.

### What was eliminated on the way, and how

Each of these was a working hypothesis that evidence killed. They are worth keeping only as a
record of what not to re-run:

| Hypothesis | Killed by |
|---|---|
| The app's share feature | Fires on `/`, not on a chase list. That code is C# with no JavaScript in it. |
| The iOS share sheet raising it over the page | The report says the page has never been backgrounded; iOS backgrounds a page while the sheet is open. |
| A foreign `<script>` element on the page | None, including dynamically imported ES modules and fetches, which `document.scripts` misses. |
| The CDN card-data fetch, in any form | `foreign code loaded: none` at the moment of the throw — nothing cross-origin had been requested yet. |
| An unsupported browser API | The modules that load on `/` use only `IntersectionObserver` and `MutationObserver`. |
| Any callback the app registered | `callbacks that have thrown: 0`. Every timer, frame, observer and listener is wrapped in a `catch` that would have reported a real Error with its stack. |
| A worker — also a separate script origin whose errors arrive muted | `secure context: no`, `service worker: unavailable`. No worker can be registered over plain HTTP to a LAN address, and the app creates no other. |
| Anything that runs when the grid renders | `+0.0s`. Neither the grid nor the runtime exists yet. |
| The dev server, the LAN origin, the non-secure context | The same error, at the same moment, on the published HTTPS site with a service worker. |
| The import map the SDK writes | Probes B and C above. |
| The app's own JavaScript at all | Probe C: one script on the page, and it is the error surface. |

### What the error surface can now do

Safari withholds the message, file and line from `window.onerror`. It does not withhold them from a
`try`/`catch` inside a same-origin script — the muting is a property of the *report*, not of the
`Error`. So `diag.js` wraps every asynchronous entry point the app's JavaScript uses — timers,
animation frames, the three observers, and event listeners — and reports what it catches with the
stack and the kind of callback it came from.

Verified against a script served from a second origin: routed through a wrapped `setTimeout` it
reported its real message, file and line; raised synchronously, bypassing every wrapper, it still
arrived bare. So the wrappers defeat the muting for anything that passes through them — and a
*muted* report now carries real information, because it means no callback was involved at all.

The report names the things that turned out to matter: the browser and whether it is a wrapper that
injects scripts, how many of the app's own callbacks have thrown, where the HTML parser had got to,
the import map, the secure context, and **its own version** (`diag/6`, on the `Page:` line) — a
phone reading a cached copy produces an old report that looks like a current one, and two readings
from different versions of the file were compared before that stamp existed.

**A muted error with that signature is now counted rather than shown.** Cross-origin throw, no
wrapper caught anything, and no cross-origin subresource on the page: that is a script the browser
injected, and opening a red panel on a visitor's phone for another program's bug is noise. Nothing
is lost — a genuine throw from this app's own code arrives through a wrapper with a full stack,
.NET exceptions still come through `console.error`, and if anything real does fire, the suppressed
count and the last full report are shown as the row above it. `window.diagMuted()` returns them on
demand. Verified: three muted errors produced no box at all, and a real throw afterwards opened it
with `3 muted errors before this, from the browser rather than from this page` and the report
attached.

Two bugs in the error surface were found on the way, both of which had been hiding evidence:

- **Resource failures were invisible.** `window.addEventListener('error', ...)` without `capture`
  never receives them — they fire at the element and do not bubble — so the `e.target.tagName ===
  'IMG'` guard, which reads as though it did, had never fired once. Every blocked or failed script,
  stylesheet and icon on the page was silently unreported. Now listened for in the capture phase.
- **The copy button did nothing on a phone.** `navigator.clipboard` does not exist outside a secure
  context, which is every phone reading this box over plain HTTP to a LAN address — the case it
  exists for. `navigator.clipboard?.writeText(...)` there is optional chaining onto `undefined`.
  It now falls back to a field containing the report, selected, so the OS can copy it.

`tests/PackProphet.App.Tests/DiagUnmutingTests.cs` holds all of it in place: every entry point the
app's JavaScript uses is wrapped, a *new* kind of callback fails the build rather than quietly
escaping, resource errors stay in the capture phase, and copying stays independent of a secure
context.

### If it needs re-opening

It should not, but the discriminator is cheap: **load the page in Safari.** If Safari is clean and
another iOS browser is not, it is that browser's injected script and not this app. `tools/DiagProbe`
narrows it further — a probe page whose only script is the error surface reproducing the error is
proof the page is not involved.
