# Known issues

Open problems with what has already been ruled out, so a second attempt starts where the
first one stopped rather than repeating it.

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
