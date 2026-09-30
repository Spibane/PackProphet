# Changelog

Notable changes to PackProphet. Dates are ISO. Versions follow
[semantic versioning](https://semver.org) once there is a release to be compatible with;
until then the minor number tracks the roadmap phase.

### v0.8.0 - 2026-09-17

- **Every unfinished set on one page, at `/progress`.** The app could already answer "how do I
  finish A2?" and had nowhere that answered "which of my sets are unfinished?" — scope was
  one-at-a-time on the four Answer pages, and the Collection page's set strip carried a code and a
  percentage as a way to jump between sets, not as an overview. One panel per set now leads with
  how far short of target it is, what would finish it in calendar time, what kind of cards are left,
  and the state of all three routes into it. Expanding a panel gives the rarity ladder with the
  rungs you collect none of drawn as gaps rather than zeroes, and the outstanding cards, each
  linking to its own page where its five routes are already priced
- **No total, and no date for finishing everything.** Cards short is additive and the page bar
  states it; expected packs is not. Packs of one set are not packs of another and the daily
  allowance is shared, so 412 + 61 is not a number — the same reason `PackAllocator` exists rather
  than the ranking simply adding its sets up. For the same reason the panels cannot be ordered by
  packs: release order and closest-to-done are each one quantity measured the same way in every set,
  and expected packs is not, so ordering by it would rank quantities that are not comparable. The
  rail says so where the control is
- **Three routes, no winner.** Each panel names packs, that set's pack points and how many of the
  missing cards are tradeable, side by side and with no cheapest tinted among them. The engine
  already refused that conversion — `RouteCost` prices only pulls and pack points in packs, and
  leaves shinedust and stamina in their own currencies, because the game has no exchange rate
  between them — and a page that picked a winner would be inventing one in the one place nothing
  else does
- **Every panel is the same eight bands at the same heights.** Four of them are pinned rather than
  sized to their contents, and the contents are clamped to fit: the sentence to three lines, the
  rarity line to one, the caveats to two. A band that grows to its content puts one panel out of
  step with the one beside it, and then the figure, the bar and the buttons all sit at a different
  height in each — which is the difference between twenty-one panels and one panel shown twenty-one
  times. An expanded panel is no longer allowed to stretch the panels beside it either; a shut panel
  is the same height whatever its neighbours are doing
- **A set whose odds nobody has published says so, rather than "not sold in packs".** The two are
  not the same statement: one is a gap in the data that the pack ranking can close by borrowing
  another set's distribution, the other is a fact about the game that nothing closes. The engine
  lumps them together, having nothing to price in either case, and the page now separates them and
  names the fix
- **`/packs` no longer says "Open this next" on a scoped ranking.** That is a claim about every pack
  in the game and it is only true when everything was ranked. Against one set it is the best pack
  *for that set*, and the best pack overall may be in a set the ranking never looked at. A narrowed
  scope reads "Best for this target", deferring to the page bar above it for what the target is
- **Progress takes the second slot in the main bar and Chase Lists moves behind More.** Progress is
  where you arrive with "what am I missing", and every panel on it links out to the pack ranking for
  one set — so it comes before the page it feeds, not after. A chase list is something you build by
  hand for cards outside your plan, reached occasionally; it is still in More, in the command
  palette, and in the scope picker on four pages
- **Every destination has a mark, in all three places destinations appear.** The tab bar had a
  glyph apiece and the desktop row had none, so the two navs agreed about what they contained and
  disagreed about how it looked — and the mark is the part you learn. The More menu had none at all.
  All of them come from one component now, for the reason the booster pack already did: path data
  copied into two files disagrees with itself after the first tweak
- **The nine marks in the More menu were drawn against each other, not one at a time.** Six had an
  obvious drawing and three did not, and a menu where six items carry a mark and three do not reads
  as one that failed to load — so the three had to be solved before any could be used. Each was
  rendered at its real size beside the marks already in use and kept or thrown out on that, which
  cost a gear (indistinguishable from the theme button's sun), a three-card fan for Wonder Pick (a
  blob at 15px), two bars for Compare (a count away from the progress marks), a Venn for the same
  (one shape rather than an overlap), and the wishlist's own twenty slots (a filled grey rectangle).
  What each mark is, and what was drawn first and discarded, is recorded beside it
- **A rarity rung has a compact form, and it is coloured wherever it appears.** Ten rungs across a
  panel is about three rem a box, where "◆◆◆◆" does not fit and was being cut in half; the ladder now
  reads `4◆` and wraps rather than being squeezed onto one row. The four families take their usual
  colours, which on a row of ten is half of what tells them apart
- **Fixed: the app's own root URL lit nothing up in the nav.** The collection is served at two
  addresses — the root and `/collection` — because the root has to show something and the collection
  is what it shows. A nav link matches one address, so the entry pointing at `/collection` was not
  active at the root: the page was right and the nav looked like it had no home, on the one URL
  every first visit arrives at and the one the site is bookmarked as. That entry's state is now
  computed from the URL, in one place both navs read, since it is the only route in the app with
  two of them
- **The wordmark reads as the name again, now the destinations have marks of their own.** Giving
  every button a glyph made the brand the same construction as its neighbours — a mark, then a word,
  at the same size in the same ink — so it looked like a sixth button that had lost its border. It
  is the largest type and the largest drawing in the row now, separated from the buttons by a rule
  rather than by a gap that read as the gap between two of them, and its pack is drawn **solid**
  where every other mark in the app is drawn in line: a different kind of mark rather than a larger
  one of the same kind. The pack also appears under the Slate skin, which had never had it
- **The wordmark on the desktop bar goes home.** It was text, and clicking it did nothing — the one
  thing every visitor tries. It leads to the collection, which is what the app's root shows and what
  everything else on the bar is an answer about. A plain link rather than a nav entry: a wordmark
  that filled in like a pressed button whenever you were on the collection would read as a sixth
  destination in the row
- **Fixed: a rail's prose notes were being laid out as grid rows.** Two different structures shared
  the `rail-note` class and the grid rules meant for one of them won for both, so any note with an
  inline element in it broke into a line per element. The footnote structure has its own name now
- **Fixed: the set strip was sitting on the top row of the collection.** It overlays the grid
  rather than taking a bar of its own, and it parks at the top — so with more than one set in scope
  the first row of cards was underneath it and nothing brought it out, since at rest there is
  nothing to scroll. Worst on a search that matches one row, where the row you went looking for is
  the covered one. The grid starts below the strip now, by the height the script already measures
- **Fixed: "12 pack hourglasss".** `Fmt.S` added an *s* to everything; English adds *es* after a
  sibilant. Every other word the app counts is unaffected
- **Pack points move in fives, because the game has no other kind.** Packs pay five at a time and
  every shop price is a multiple of five, so the balance fields step in fives and round a typed
  figure down to one — down rather than to the nearest, since a balance reported high recommends a
  card the shop will refuse to sell. Both places that take a balance now agree, and the box shows
  what was stored rather than what was typed
- **Logged packs past the day's free ones can spend pack hourglasses.** Off by default, with the
  switch under **Hourglasses** in Settings — it draws down a stored balance uninvited, and a player
  hoarding hourglasses for a release would not thank it. On, the allowance is the account's own (two
  a day, three with premium), whole packs only at twelve hourglasses each, and the log says what it
  spent
- **A Wonder Pick you took spends hourglasses when the stamina was not there.** Taking one already
  spent stamina; it took it off the figure as typed, so a pool entered empty two days ago was
  charged from empty and the regeneration since was thrown away. It now spends the projected
  balance and covers the rest from hourglasses, twelve to a stamina, which is what the game made
  you do to take the pick in the first place
- **A button for the day's hourglasses, on `/resources`.** One tap credits four pack and
  two Wonder hourglasses, and refuses a second go the same day: a double tap is otherwise
  invisible, and two balances slightly too high move every timeline on the page
- **A finished set with points left says what they can buy.** Points are spendable only in the set
  that earned them, so a plan finished with 800 in hand is 800 stranded — and the two columns that
  answer for it said *Set Complete* and nothing. They now name the rarest card from that set you do
  not own, whatever rung it sits on, marked **Outside Your Plan** so it cannot be read as something
  you were short of
- **Fixed: a showcase card drew its face over its own name.** The face is as tall as the card and
  takes its width from that height, which a grid track sized `auto` cannot resolve — it sized the
  track from the face's minimum and then painted the face at its real width. A chase list with
  three badges in it was enough: a 150px face in a 125px track, over the name beside it and 11px
  past the panel. The card is a flex row now, where the height is settled before the width is asked
  for
- **Fixed: two hairlines where "What's Left" opens on `/progress`.** The rung strip draws its own
  top rule, for the pages where it follows a paragraph; here it follows a rule the panel had just
  drawn
- **Fixed: the rail ran underneath the tab bar.** Between 900 and 1056px both are on screen, and
  the rail is pinned to the window rather than to the page — so its last 57px, and the end of its
  scroll, sat behind a fixed bar. On a short window that was most of what the rail holds
- **Fixed: four diamonds overlapped the bar beside them in the rail's target rows.** 49px of
  glyphs in a 40px track. Nothing clipped them, because a clipped rarity reads as a lower one, so
  the track fits the widest rung instead
- **Deleting a logged pack gives back the hourglasses it spent**, alongside the points it earned.
  What a pack cost is recorded on the logged row, so the refund is exact and a free pack refunds
  nothing; whether *this* pack was the third one that Tuesday cannot be worked out from a log with
  a row taken out of it
- **A target lasts the visit, and is never saved.** The four Answer pages shared one stored scope,
  on the argument that "given what I am collecting, what should I do next" is one question. It is —
  but the answer does not keep. Stored, it outlived the visit that set it: `/trades` and `/wonder`
  opened ranked against a set chosen once days earlier on another screen, with a picker nobody
  remembered touching as the only clue. A link could write it too, so "Which Pack" on one Progress
  panel decided what Wonder Pick thought an offer was worth. Every page starts at **everything**
  now and holds a choice for as long as you are on it; a redirect — `?scope=`, `?chase=` — is the
  one thing that can say what a page opens on. A scope left in an older save is dropped on read
- **Copy: less explaining.** The page-wide notes defending a design decision are gone — why
  `/progress` names three routes instead of picking one, why "by packs" is not an ordering, why a
  batch cannot beat ten singles, what the batch figures do not model — along with the reasoning
  clauses trailing six switch labels and footnotes. The instruction or the fact stays; the argument
  for it lives in the code
- **Fixed: the same strip was squeezing the grid on a narrow window.** Below the fold width it is
  meant to be a bar in the flow, and the rule that undid its overlay placement was outranked by the
  rule that set it, so it stayed in a column that layout does not have. Grid invented an implicit
  one the width of a wordmark and charged it to the cards
- **A bar above every page for what the site is still waiting on.** The app's own data ages at
  three rates by design — card data is fetched live, card art is a manual commit in a second
  repository, and card detail is a separate 4.4 MB table topped up per set — so for days after a
  release it knows a card exists, cannot draw it, and cannot say what it does. Every page behaved as
  though that were normal, which reads as a broken app rather than a waiting one. It now names the
  set and what has not arrived, once, where you land. A card database that could not be reached at
  all says that instead, because the visible symptom of falling back to the bundled copy is that the
  newest set is simply absent
- **Not pull rates, though they lag the same way.** An unpriced set is already solvable in the app:
  `/packs` offers to borrow the newest measured set's distributions and every figure follows. A bar
  for something you can already fix is a bar about a setting, and this one is about data nobody has
  published. Screenshot recognition is off it too, for a different reason — deciding whether it
  applies means parsing the 150 KB fingerprint table, which is the one piece of card data
  deliberately kept off the boot path, and the import page already says so above its own file picker
- **The deploy now writes down what it could not find, not only what it fixed.** Art coverage is the
  one gap the app cannot compute: it cannot see a missing image without requesting it, and probing
  3,879 of them to decide whether to show one sentence is absurd. `tools/vendor-gap-art.py` already
  asks the art repository which sets it has and fills the gaps from a release archive, so
  `art/index.json` gained a per-set count of what is actually drawable. It asks for a set's
  *directory*, so a set upstream has started and not finished still looks complete — this
  under-reports and never over-reports, which is the right direction for a claim made on every page
- **And a channel for the things the app cannot work out for itself.** An outage, a feature that has
  started failing, or a pack that launched before the card database published it — which is the one
  release-day case nothing derived can see, since the app cannot know about a set it has never
  heard of. Posted as a gist and read at boot, so saying something does not mean a deploy; not a
  file in this repo, because the service worker precaches every `.json` in the published output and
  then serves it cache-first, so a committed notice would be frozen at whichever build the visitor
  installed. The feed is the only text in the app that neither the build nor the user wrote, so it
  is treated as such: an entry with no id, an unreadable `until` date, text past 300 characters, or
  a link that is not an absolute `https` URL or a plain in-app path is refused rather than guessed
  at. Blank in `appsettings.json` and nothing is requested
- **One bar, one notice, and a dismissal that names its subject.** Two rows above every page is two
  rows on every page, and a reader told two things at once acts on neither, so the most severe wins
  and an authored notice breaks a tie. Missing art and missing detail are said in one sentence for
  the same reason: as two notices the bar would show one, and dismissing it would reveal the other.
  Dismissing is per subject rather than per feature — the two dismissible strips before this were
  each a single flag, which for a bar that announces whichever thing is behind today would mean
  hiding one release's notice hides the next one's — and a set that gains its art while still short
  of detail is the same subject half answered, so it is not re-announced. Only a `problem`
  interrupts a screen reader; a `warning` is assertive beside the form it is about and not when it
  is read on arrival at twelve pages. Nothing derived appears until there are cards to be talking
  about, and **Settings → Show Hidden Notices Again** undoes a dismissal, as the other two strips
  already allowed
- **The screenshot readers on `/log` and `/wonder` are buttons now, not disclosures.** Each was a
  `<summary>` in secondary text, which is the typography of a footnote — something the page
  mentions in case you want it. Both are the fast way to do the thing their page is for: on the
  log screen the game has just shown you the five cards and the cards themselves say which pack
  they came out of, and on Wonder Pick entering the offer by hand is five searches. So both are
  drawn as the offer they are, and as the same control, since it is the same offer in the same
  place in the same job. Still folded by default, because the by-hand route always works and this
  does not: a set released since the last fingerprint refresh cannot be recognised. Folded means
  hidden rather than unmounted — a batch of twenty readings is a minute of work, and a mis-tap on
  the button must not be able to throw it away
- **The day's hourglasses can be credited from the More menu.** One idempotent act with no
  arguments and nothing to read afterwards, which used to live only on Resources, a page load away
  from wherever you were. It sits across both columns at the foot of the sheet and outside the
  `<nav>`: a landmark listing destinations should not have a button in it, and full width is what
  separates an action from the tiles above it. Resources keeps its own copy — that is the page that
  explains what the two currencies are and where the balances are watched moving — and both press
  the same guard, so the day is spent whichever was used and the other says so. Alone among the
  sheet's controls it does not dismiss the sheet, because its answer is its own label
- **The same screenshot imported twice is caught before it is logged.** A folder chosen again, or
  yesterday's shots handed over with today's, used to add five cards and a pack-log row recording
  an opening that never happened — invisible afterwards, since nothing in the log says the picture
  was the same picture. A reading carries a pack and its cards and so does a logged opening, so the
  two are simply compared, against the log and against the rest of the batch. It warns and does not
  refuse: three common slots draw from a pool in the low tens, so over a few hundred packs of one
  booster a genuinely identical hand is percentage points rather than nothing, and refusing it
  would deny an event the user watched happen with no way to overrule it. The date of the earlier
  one is named instead, which is the part that settles it. Card identity is the evidence rather
  than the pixels: a hash would catch a re-upload of the same file and miss a re-crop, a
  re-compression or a second screenshot of the same still screen, and would need a store that has
  to be synced, migrated and pruned

- **Promos can be logged from `/log`, without the scroll.** A promo arrives one at a time — a
  Wonder Pick event, a mission, a campaign — so there is no pack to log and a screenshot saves
  nothing: the finding *is* the work, and it used to mean the collection, filtered to the promo
  set, scrolled past ninety cards you already have, for one tap. A button beside the screenshot
  one opens the ten newest in the promo set currently being filled, newest first, one tap per
  copy. Not tiles in the pack picker: that grid is packs you decide to open, and a promo is not
  one of those
- **Newest, which is exact — not live, which is not knowable.** Neither community dataset carries
  an event calendar: every promo card has a null release date and the promo sets have none either,
  and the only event listings are editorial web pages. What the data does carry is order, because
  promo numbers are issued as cards are released — the volumes in both promo sets are strictly
  monotonic and never overlap, PROMO-B running Vol. 1 at 2–6 through Vol. 12 at 88–92, with the
  promos belonging to no volume sitting at the numbers matching when they landed. So the panel
  says what it is showing and never claims those are the ones still on offer
- **Cards you already own stay on the list, and the volumes are ignored.** A promo can be earned
  more than once — a repeatable mission, an event run twice — so a row that vanished on the first
  tap would vanish exactly when the second was needed; the count sits on the row and a minus takes
  a mis-tap back without a keyboard. Volumes are not used to group it although the data would
  allow it, because a third of the promos are in none: 34 of PROMO-B's 94 are the Wonder Pick and
  mission cards, which is precisely what an event hands out
- **Logging a promo touches the collection and nothing else.** No pack-log row, no pack points, no
  hourglass. A promo is handed over rather than opened, so a row in the log would be a pack that
  was never bought, and it would reach the odds check on History as a pack the model has no rates
  for — the same reason `PromoScopeTests` keeps promos out of the pack ranking

- **A batch with one duplicate in it can be logged without the duplicate.** The usual shape of the
  problem is nine packs opened in a sitting and one picture from an earlier sitting still in the
  folder. The choice was to log the repeat along with the nine or to throw all ten away and pick
  the files again — logging clears the readings either way, so "cancel and remove the odd one" was
  never on offer. The confirm now leads with **Log 9, Skipping 1 Repeat**, and the account
  afterwards says how many were left out, because a summary reading "logged 9 packs" over a folder
  of ten is what makes someone count them by hand. Only when dropping the repeats still leaves
  something to log, and the *log everything anyway* button stays beside it — a match is not proof
- **A card opens at the top of itself.** Blazor routes in place and nothing reset the scroll, so a
  card page inherited whatever offset the page before it was at; coming from a scrolled grid — far
  taller than one card — the browser clamped that offset to the card page's own maximum and opened
  it at the bottom. Measured at 375px: the grid at 1400 of 6315, the card page 1475 tall, opening
  at 663, which is 1475 less the 812 viewport. A phone problem only, since above the desk
  breakpoint the grid scrolls itself and the window never moved. The reset is keyed to the card
  rather than to first render, because this page navigates to itself through the reprints and the
  evolution line
- **`/progress` in release order now runs newest to oldest.** The reverse of the Collection page,
  and right here for the reason that page's order is right there: the Collection is a catalogue
  and reads forwards, while this is a list of work outstanding. The work is nearly always in the
  sets that just came out — an old set is either finished or has been sitting unfinished for a
  year — so oldest-first put the two sets you are actually opening at the bottom of twenty panels
- **Ready for a second Deluxe set.** B4b, *Deluxe Pack: Mega*, is due 2026-09-29, and three
  things about Deluxe packs were tied to the code `A4b` or to the pack's name alone:
  - A Deluxe set with no published rates was offered **B3b's** rates, a five-card pack with no
    guaranteed 4◆ and no foil codes, which turned its parallel foils into plain cards. It now
    borrows from the newest measured Deluxe set, or is offered nothing
  - **Guaranteed 4◆** was drawn on A4b's row only. It now follows the pack, since every Deluxe
    pack holds four cards and guarantees one
  - Booster art fell back to a file named by pack alone, and `Deluxe.webp` is one file for two
    sets, so B4b's tiles would have shown A4b's booster. A name shared by two sets now draws the
    placeholder until the expansions index has the new set's own art, and the deploy's gap-art
    step no longer vendors a shared name
- **One On Sale switch for every Deluxe pack.** The chips are one per pack name rather than one per
  pack, so A4b and B4b do not show two identical "Deluxe" chips. Still stored per pack, so they can
  be split if re-releases stop coinciding
- A test now fails if a Deluxe set reprints an existing 1-3◆ alternate art — B1's Furfrou, B2a's
  Iono or Penny — because the foil rule would call that card a foil in its original set too
- **A Deluxe Wonder Pick is four cards and 2 Pack Hourglasses.** A Deluxe pack holds four cards,
  so its offer's fifth slot is the hourglasses, and `/wonder` asked for a fifth card that does not
  exist. An offer of Deluxe printings now completes at four, draws the hourglasses as the fifth
  box, and counts them in its expected value: a fifth of two hourglasses, at twelve to a pack.
  They do not lift an offer out of *nothing here you need* — they are in every Deluxe offer.
  Receiving them is a choice when logging a take, and credits them to Pack Hourglasses. Logged
  offers now record whether they were Deluxe; older ones are read by shape, four cards all sold in
  a Deluxe pack
- **Each limited-time pack has its own On Sale switch.** B4b's Deluxe pack went on sale without
  A4b's, so the shared "Deluxe" switch could not say so. One chip per pack now, under the name the
  game uses (*Deluxe Pack: Mega*, *Deluxe Pack: ex*), newest first. The setting was always stored
  per pack, so nothing was lost. A pack with no rates has no chip until its rates are estimated
- **Deluxe screenshots are read as Deluxe packs.** Every card in a B4b pack can be a reprint, and a
  reprint's art is its original's, so one pack was read as four cards from four sets. Where exactly
  one set prints every card in a pack or Wonder Pick, each is named as that set's printing. The
  four-card results screen, two by two with a count on each card, was read as your own card list;
  it is read as a pack
- **A Deluxe Wonder Pick can be five cards.** An event Wonder Pick from a Deluxe pack shows five
  cards and no hourglasses, and `/wonder` cut such an offer to four. A Deluxe offer of four cards
  still draws the 2 Pack Hourglasses as its fifth box; naming a fifth card makes it the event offer,
  and the hourglasses leave the box, the expected value and the choices when logging a take. Where
  the game puts the hourglasses among the five does not matter to the verdict
- **A read card can be switched to its foil.** A Deluxe foil and its plain card look nearly alike
  in a screenshot, so the reader can name either. Every recognised card that has one gets a
  **Foil** button, on the Collection import, Log a Pack and Wonder Pick alike. Foils are paired from
  the card list, so a Deluxe set with no rates yet has the button too
- **The notice bar names a promo set missing a few cards' detail.** A numbered set is published
  whole or not at all, so a tenth of slack costs nothing there; a promo set grows a few cards at a
  time, and Promo B's nine newest read as complete. Any gap in a promo set is now reported, and a set
  nearly all there is said with its count: *Promo B is still missing attack and ability detail for
  9 cards*
- **A new set without art says so, and looks it at once.** Only the deploy knew which sets lacked
  art, so a set released since the last deploy was found out tile by tile: twenty seconds and more
  of spinners per screen before the placeholder, and nothing on the notice bar. After the app opens
  it now asks both art sources about one card of each recent set, and about the newest cards of
  each promo set, which go missing a few at a time. A set or promo with no art draws the placeholder
  straight away and is named on the notice bar, and a reprint in it is drawn from the set it was
  first printed in, since the artwork file is the same: 236 of B4b's 429 cards. Only two answers
  saying "not there" count, so a slow or throttled source changes nothing
- **The notice bar shows on a first visit too.** It waited for a collection, but a first visit on
  release day is looking at exactly the set the bar is about
- **A Deluxe Wonder Pick's hourglasses are not offered for naming.** The screenshot reader left
  their slot unread, so it was asked about as a card it could not name. The slot is the brightest in
  the picture by a distance, a small figure on the page's own background, and on a Wonder Pick from
  a Deluxe set that is taken as the hourglasses
- **A card with no art is outlined in its type's colour.** The placeholder's card outline takes the
  colour of the card's energy pip, on the Collection grid and on the card's own page, so a set
  whose art has not been drawn yet still reads by type. A card whose type is not known yet keeps the
  grey one
- **The site redeploys itself when new card data is released.** A new set's art reaches the site
  only through a deploy, and nothing deployed on a release: B4b's art was in the release archive
  the day it came out and the site drew placeholders until a push happened to go out.
  `card-data.yml` checks every three hours and deploys when the live site was built against an
  older release. It replaces `card-hashes.yml`: the snapshot refresh and fingerprints now follow a
  new release too, as one pull request, instead of waiting for a snapshot refreshed by hand and the
  next Monday
- **Art the site serves itself draws everywhere, not only in the grid.** Its addresses were relative,
  and the pages that draw art as a background pass it through a CSS variable read in `css/`, so the
  browser asked for `css/art/…` and got nothing: no booster for B4b on Which Pack, and no card art on
  a B4b card's own page. They are absolute now, under the app's own base
- **Off means off on the Collection grid.** A right-click or shift-click went on removing a copy
  with tapping set to Off, and putting it back meant switching to Add under a switch that said Off.
  In Off they now do nothing; in Add and Remove they still do the opposite of the mode
- **Wonder Pick's search takes a Deluxe offer whatever printing it starts from.** After the first
  card the picker kept to that printing's set, so a Frigibax named from B2a shut out Mega Manectric,
  though B4b's Deluxe pack holds both. It now narrows by every printing of the cards named, and once
  one pack is left each card becomes its printing there, which makes the offer a Deluxe one
- **Ten packs opened at once can be logged from screenshots.** The results list is longer than a
  screen and arrives as a run of overlapping screenshots, and each pack sits under a "Pack no. N"
  heading. Chosen together on Log a Pack, they are read back into the packs they show, whatever
  size those packs are, and logged as a batch. The row the Next button covers is read from its
  picture, and a card no screenshot held is left as a slot to name by hand
- **A deploy reaches a browser that already has the site.** The offline copy updates itself by
  fetching a new worker and that worker's list of files, and the list came from the browser's
  cache, four hours old, so every new file failed against it and the old version stayed however
  often the page was refreshed. The list is now always fetched fresh

### v0.7.0 - 2026-09-01

- **Cloud sync, with no account and nothing readable on the server.** One device makes a
  twelve-character pairing code, the other types it, and that is the entire sign-up. The code is
  stretched with PBKDF2 into a master secret, and three independent HKDF expansions of it do three
  jobs: a document id and a proof token, both of which the host sees, and an AES-256-GCM key, which
  never leaves the browser. Supabase stores a blob it cannot open. The trade is stated wherever the
  code is shown — there is no account to recover it from, so losing the code loses the stored copy,
  though never the collection on any paired device
- **Two devices are merged against what they last agreed on, not ranked by who wrote last.**
  Last-writer-wins is not sync: log a pack on a phone, tick two cards on a laptop, and whichever
  pushed second erases the other's afternoon. Sync keeps the last state both devices shared and does
  a three-way merge against it, so "changed here" is distinguishable from "changed there". Card
  counts go to whoever moved them, and to the higher figure when both did — a tracker that forgets a
  card you own is worse than one showing a card you sold, because the second is visible and the
  first is not. The pack and Wonder logs are append-only, so a row one side lacks is a row it has
  not seen rather than a deletion, and they union. Decks and chase lists resolve per id
- **A first pair lets an unset field yield instead of win.** With no common ancestor there is no
  way to tell which device changed a single-value field, and the rule was to keep this device's --
  which on the device doing the joining is the empty one. A blank private window adopting a real
  collection was therefore discarding its trade board, its shinedust, its hourglasses, its rarity
  plan and its name, and the established device then pulled those blanks back on its next sync. With
  no ancestor there is still no way to tell who *changed* a field, but there is a way to tell who
  never set one, and an absence now yields to a figure. Two devices that have both set something
  differently is still a conflict, and still reported
- **Pressing Join with a bad code no longer looks like a dead button.** The join reported failure
  by returning a sentence, and the settings page rendered failures from `Sync.Message` -- two error
  channels, and three of the exits only wrote to the one nothing was reading. A mistyped code, an
  empty field and a code for a document that does not exist each produced a perfectly good
  explanation that was computed and dropped, so the click did nothing observable whatsoever. Every
  exit now reports through the one channel the page renders, an empty field is told to type the code
  rather than that what it typed was invalid, and the field stays up afterwards so the code can be
  corrected instead of retyped
- **The code field updates as it is typed rather than when it loses focus.** With `onchange` the
  value reached the app only on blur, so submitting by pointer depended on the blur firing before
  the click
- **A conflict cannot be counted without being described.** The count and the notes were a `ref int`
  and a separate list, and nine of the resolution sites bumped the first without writing to the
  second, so a merge that resolved a changed rarity plan or a changed trade board reported conflicts
  it had nothing to say about -- and the settings page rendered "Both devices had changed the same
  things:" above an empty list. Recording a conflict now requires the sentence describing it, so the
  two cannot come apart, and every field names itself rather than being totalled anonymously
- **Appearance and the active profile deliberately do not sync.** A phone and a desktop want
  different column counts, and a theme belongs to the screen being looked at. A difference in those
  is not a conflict and is not reported as one
- **A push is conditional on the version it was based on.** Two devices that pull at the same moment
  both try to push; the database refuses the stale one, and that device pulls again, merges what it
  now knows, and retries. Without the check the second push would overwrite a merge the first had
  already completed — the ordinary case, not a rare race. The token is a counter rather than a
  timestamp, because two writes inside one clock tick would read the same time and slip through
- **Every failure leaves the local collection exactly as it was.** Offline, an unreadable blob, a
  wrong code, a swept document: each is reported and none of them clears, empties or replaces
  anything. A merge that does arrive comes through the same path as any other change, so
  <kbd>Ctrl</kbd>+<kbd>Z</kbd> undoes it
- **A stored copy written by a newer build is refused rather than read.** Deserialisation already
  rejected a schema it does not understand, which matters far more with sync than without: reading
  it would drop the fields this build has never heard of, and the next push would store that loss
  for every device. It now says so specifically, since after a release that is the likely reading
- **Every client-supplied column is bounded, not just the payload.** `sync_push` capped the blob at
  two megabytes and left `id`, `auth`, `nonce` and `writer` as unbounded `text`. The anon key is
  published in the app by design, so anyone could have called the function with a half-gigabyte
  writer string and filled the database, with the payload check giving a false impression that
  inserts were bounded. Each column is now pinned to the shape it actually has -- hex of a known
  length, base64 of a known length -- as re-runnable constraints and as a stated refusal inside the
  function, so a malformed client gets an error it can classify rather than an opaque failure
- **Untrusted state cannot reach a throw on a render path.** `DeckCodec.Create` refuses a
  non-positive card identity or more than three energy types, correctly, since neither can be
  encoded -- and it runs during render to build a deck's share code. Deserialisation null-guarded
  those lists without clamping their contents, so a payload holding `"deckBuilderNrs": [0]` opened
  as a deck page that threw. That was a narrow concern when state came only from this browser or a
  file the user chose; sync makes a remote document an ordinary input on every launch, so the parse
  now clamps rather than merely null-guards. Both are dropped, and the rest of the collection loads
- **`security definer` functions pin `search_path` to `pg_catalog, public`** rather than `public`
- **The project's own URL and key come from repository variables rather than the tree.** Variables
  and not secrets, on purpose: the browser sends the anon key to Supabase on every request, so it is
  public from the moment the site deploys, and filing it as a secret would dress that up as
  something it is not. Keeping it out of the tree buys a rotation that costs a settings change
  instead of a commit, and a history that never carried it. The deploy substitutes both files and
  refuses to ship if only one of them landed -- a half-substituted deploy would report success and
  serve an app whose sync silently never works, since the key is useless while the CSP still names
  the placeholder host
- **Sync is off unless the build names a Supabase project.** A clone of this repository is the
  local-only app it always was, rather than one with a settings section that cannot work. Turning it
  on means running `db/sync.sql`, filling in `appsettings.json`, and naming the same host in the
  CSP — one host, not `*.supabase.co`, so a tampered card dataset still has nowhere to post to
- **The settings page stops claiming nothing is uploaded once something is.** The line was true for
  every previous build and is not true of a device that is syncing

### v0.6.0 - 2026-09-01

- **A pack reveal whose cards are all white-bodied is found at last, though not yet framed
  exactly.** A card with a small illustration panel over a large white body does not mask as one
  shape: the panel is coloured, the attack text is detailed, and the plain body between and around
  them is neither, so the card arrives as two pieces with an unmasked white band between them and
  neither piece is card-shaped. On every other screen such a card is still recovered, because a card
  that *was* found fixes its row's phase and the column pitch says where the rest must be; on a Team
  Rocket's Ambition reveal all five are pale, so there was no seed anywhere and the screen read
  nothing at all. The pieces are now joined back into a card — but only when the mask found nothing
  card-shaped in the whole picture, which is the case this exists for and the only one with nothing
  to lose. Eight of the nine reference screenshots come out byte-identical; the ninth goes from 0 of
  5 to 5 of 5 found
- **A joined card is measured from its bottom edge and the card aspect, not from its own pieces.**
  The game draws a NEW flash that hangs about twenty pixels above the card it belongs to and masks
  as part of the illustration panel, so the top of an assembled region is the least trustworthy
  thing about it. Taking the pieces' own extent put every row nineteen pixels high and nineteen too
  tall; the bottom edge is where the card's content ends and nothing protrudes past it
- **An assembled box is offered at a larger scale as well as nudged.** Such a box is not merely
  misplaced, it is small — its pieces stop at the illustration and the attack text, and the card's
  plain border is not in the mask at all — so translating it cannot fix it. It now also offers the
  box grown by one mask block on each side, at three horizontal anchors, since the mask can fall
  short of a card by up to a block but never reach past one. Measured by putting exactly that error
  on the one pack reveal whose cards *are* in the fingerprint table: 3 of 5 read without the grown
  crops and 4 of 5 with them, at 3 to 9 bits where the true box scores 4 to 11. Growing by half a
  block recovers nothing, so the whole block is what does the work
- **What is still open is written down rather than rounded up.** The assembled lattice reads 172x240
  where the same screen measures 176x245, because nothing in a picture of only pale cards ever
  reaches a card's border. No wrong answers come of it — the nearest fingerprint to any of that
  screenshot's sixty crops is 21 bits away against a threshold of 18, so all five stay unread rather
  than being pushed onto the closest thing in the table. Its set is newer than the committed
  fingerprints, so the reading itself cannot be verified until the weekly workflow catches up, and
  `KNOWN-ISSUES.md` says to re-measure then
- **The known-issues list no longer describes a bug that was already fixed.** It still had IMG_1154
  reading 7 of 9 copy counts, with Exeggutor ex and Tangela called unreadable; running the real
  `scan()` over the fixture reads 9 of 9, and the two cards it named are 5 and 4. It also had no
  entry for IMG_1188, IMG_1189 or IMG_1190, which are committed fixtures. The three-across
  screenshots overlap on purpose and the table now says so — IMG_1189's clipped top row is
  IMG_1188's second row, and IMG_1190 is the same nine cards as IMG_1154

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
