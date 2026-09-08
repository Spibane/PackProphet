// Finds the card slots in a screenshot of the game and fingerprints each one.
//
// This module does pixels and nothing else. It does not know what a card is, which set it belongs
// to, or whether the player owns it — it finds a repeating grid of card-shaped regions, measures
// each region, and hands the numbers to C#, where every judgement is made and unit-tested. The
// wire format is PackProphet.Vision.ShotScan; keep the two in step.
//
// Nothing leaves the device. Like js/qrscan.js, the image is drawn to an off-screen canvas and the
// pixels are read locally, so a screenshot import works offline and uploads nothing. Unlike
// qrscan.js there is no library to vendor: the whole method is a fingerprint comparison, and the
// 3,761 fingerprints it compares against ship as a 150 KB text file.
//
// Why a grid detector rather than object detection. The screens worth importing — a set's card
// list, a pack's five cards, a Wonder Pick's line-up — are all the same shape to a computer: cards
// of one size, at one spacing, in rows. That regularity is far easier to find, and far harder to
// get wrong, than a card is to detect in isolation. It also means one detector serves all three
// screens, since a row of five is a grid with one row.

/// The working resolution's longest side. Every measurement below is a fixed fraction of the image,
/// so the result is scale-independent; the cap is there because a modern phone screenshot is 12
/// megapixels and none of that detail survives the 9x9 sampling the fingerprint uses anyway.
const WORK_MAX = 1400;

/// Sampling grid, matching ArtHash.Grid.
const ArtHashGrid = 9;

/// The part of a card a thumbnail keeps: the name and the illustration, and none of the half below
/// them.
///
/// A thumbnail of the whole card is mostly attack text, a stats bar and the copy-count banner, all
/// of it far too small to read at this size and all of it crowding out the one thing worth looking
/// at. Cropping to the picture roughly doubles the picture.
///
/// The band is measured to hold both card layouts, which do not put their illustration in the same
/// place: a Pokémon's runs from 0.07 to 0.47 of the card's height with its name above it at 0.02,
/// and a Trainer's from 0.14 to 0.51 with its kind and its name above that. One window covering both
/// keeps the name either way, which matters because the name is what gets typed into the box beside
/// it.
const ART = { left: 0.04, right: 0.96, top: 0.02, bottom: 0.50 };

/// The longest side of a slot's thumbnail, and how hard it is compressed. Sized so the zoomed view
/// is about life size rather than an enlargement of too few pixels — the crop is under half the
/// card's area, so this is a smaller picture than the number suggests. The fingerprint is computed
/// from the full-resolution box and never from this.
const THUMB_MAX = 176;
const THUMB_QUALITY = 0.7;

/// How many slots of one picture get a thumbnail.
///
/// A real screen holds five to twenty-five, so this never binds on a screenshot of the game; it is
/// here because MAX_CELLS allows 160 and a lattice found in something that is not a card list could
/// reach it. Measured at 2 to 7 KB a slot — a hand of five large cards is dearer per slot than a
/// page of twenty-five small ones — so a whole picture runs 34 to 56 KB and the cap holds the
/// pathological one to a few hundred, against 5 to 12 MB for the screenshot itself.
const THUMB_CELLS = 60;

/// A Pocket card's width over its height. The one fixed fact about the shapes being looked for, and
/// the thing that separates a card from every other rectangle on screen.
const CARD_ASPECT = 0.717;
const ASPECT_MIN = 0.55;
const ASPECT_MAX = 0.95;

/// How much taller than its width implies a region may be and still be measured as a card.
///
/// The margin is for the WIDTH's under-reach, not the height's: a region an honest two percent
/// narrow implies a height two percent short, and the region is then two percent over it while
/// being a perfectly good card. Anything further over is furniture the mask joined to the card —
/// see the note in findCards.
const TALLEST_HONEST = 1.02;

/// Smallest card, as a fraction of the image width. The tightest real layout is the five-across card
/// list, where a card is about a sixth of the screen; anything much smaller is an icon or a chip.
const MIN_CARD_WIDTH = 0.10;

/// The mask is built on a grid of blocks this many pixels square. Four is small enough to keep the
/// four-pixel gutters between cards unmasked — which is what stops a row of cards merging into one
/// region — and large enough that a whole screenshot is a few thousand blocks rather than a million.
const BLOCK = 4;

/// A pixel counts as card-coloured above this saturation, and a block counts as card if this fraction
/// of it does. The page behind the cards is a pale, almost grey blue: on all four reference
/// screenshots between half and three quarters of the image sits under 0.10 saturation.
const SATURATION = 0.20;
const SATURATED_FRACTION = 0.35;

/// A block also counts as card if it holds this much local gradient. Cards whose bodies are white
/// carry text where they carry no colour, and this is what catches them.
const BLOCK_EDGE = 10;

/// How far a card may sit from its row or column before it is taken to be in a different one.
const LINE_TOLERANCE = 0.3;

/// Blank slots are only worth filling in on a real grid: the five-across card list draws the cards
/// you do not own, and a hand of five has nothing to fill. Below this many columns it is a hand.
const GRID_COLUMNS = 4;

/// The copy-count badge, as fractions of the card: a dark ribbon across the bottom-left corner. The
/// search region is generous because only its rough position matters — the badge's own dark pixels
/// are what pin it down.
const BADGE_TOP = 0.80;
const BADGE_RIGHT = 0.62;

/// Luma below which a pixel belongs to the badge rather than the card.
const BADGE_DARK = 115;

/// The glyph grid, matching CountReader.GlyphWidth and GlyphHeight in PackProphet.Core.
const GLYPH_W = 8;
const GLYPH_H = 14;

/// A digit's width over its height in the game's badge font. Used to cut a run of touching digits
/// into the right number of pieces — the font is fixed pitch, so equal pieces is exactly right.
const DIGIT_PITCH = 0.69;

/// How much of the badge's height a digit fills, at the least. Measured at 0.57 to 0.60 on the
/// reference screenshots; the corner and slanted-edge artefacts that share the ribbon with it come
/// in at 0.11 to 0.15, so there is a wide gap to sit in.
const MIN_DIGIT_HEIGHT = 0.4;

/// How far the card box is nudged when offering the matcher alternative crops, in pixels at the
/// working scale. The detector lands within a few pixels of a card's true edge and the fingerprint
/// is unforgiving about the difference — three pixels out is worth ten bits or more — so the cell
/// carries a small spread of crops and C# keeps whichever it can identify most confidently.
const NUDGE = 3;

/// Cells past this are dropped. A real screen holds a few dozen; a number far above that means the
/// detector locked onto texture rather than cards, and the payload should not follow it.
const MAX_CELLS = 160;

/// How far apart two pieces of one card may sit, as a fraction of the card height their shared width
/// implies. The white band between a pale card's illustration and its attack text measures 0.34 of
/// the card on the reference screenshots.
const STACK_GAP = 0.45;

/// How much of the narrower piece's width two stacked pieces must share before they can be the same
/// card.
const STACK_OVERLAP = 0.7;

/// Every failure a picture can cause is a failure of the picture, not of the app, so each one comes
/// back as a sentence the page can show rather than an exception.
const fail = error => ({ ok: false, error, width: 0, height: 0, lattice: null, cells: [] });

export async function scan(dataUrl) {
    let bitmap;
    try {
        bitmap = await imageFrom(dataUrl);
    } catch {
        return fail('That file could not be read as an image.');
    }

    let pixels;
    try {
        pixels = draw(bitmap);
    } catch {
        // A canvas that will not allocate on a phone. Fatal here, unlike in qrscan.js where a
        // smaller scale is worth trying: this one already downscaled to WORK_MAX.
        return fail('That image was too large for this device to read.');
    }

    const gray = grayscale(pixels);
    const found = findCards(gray, pixels);

    if (!found) {
        return { ok: true, width: bitmap.width, height: bitmap.height, lattice: null, cells: [] };
    }

    const cells = slots(gray, pixels, found);

    return {
        ok: true,
        width: bitmap.width,
        height: bitmap.height,
        // originX/originY and the card box on each cell are surplus to what
        // PackProphet.Vision.ShotScan reads — the deserialiser ignores them. They are here because
        // every bug in this file that produced confident wrong answers was a misplaced card box, and
        // a wire format that cannot show where it cut cannot be debugged from the outside.
        lattice: {
            rows: found.rows.length,
            cols: Math.max(...found.rows.map(r => r.columns.length)),
            cellWidth: found.cardW,
            cellHeight: found.cardH,
            originX: found.origin,
            originY: found.rows[0].top,
            confidence: Math.min(1, found.cards.length / Math.max(1, cells.length)),
            relativeCellWidth: found.cardW / gray.w,
        },
        cells,
    };
}

// ---------------------------------------------------------------------------------------------
// Getting at the pixels

function imageFrom(src) {
    return new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => resolve(img);
        img.onerror = reject;
        img.src = src;
    });
}

function draw(bitmap) {
    const scale = Math.min(1, WORK_MAX / Math.max(bitmap.width, bitmap.height));
    const w = Math.max(1, Math.round(bitmap.width * scale));
    const h = Math.max(1, Math.round(bitmap.height * scale));

    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    ctx.drawImage(bitmap, 0, 0, w, h);

    // The canvas travels with the pixels because the slots are cut back out of it — see
    // thumbnail(). Redrawing the image a second time for that would decode it again, and the
    // measurements are all in this canvas's coordinates anyway.
    return { w, h, canvas, rgba: ctx.getImageData(0, 0, w, h).data };
}

/// A small picture of one slot, for a card the reader could not name.
///
/// The point is a person looking at it. A slot that comes back unread is a slot where every
/// automatic answer has already been tried, so what is left is to show someone the card and let
/// them say what it is — and a row of card names with no pictures beside them is not something
/// anyone can check against a screenshot they took ten minutes ago.
///
/// Cut here rather than in the page because this is where the image is: the page hands the scanner
/// a data URL and lets it go, and keeping several megabytes of base64 per picture alive so the
/// review can crop it later would cost far more than the pictures do. Every slot of a whole
/// screenshot comes to a few tens of KB, against 5-12 MB for the screenshot it came from.
function thumbnail({ canvas }, box) {
    const x = box.x + box.w * ART.left;
    const y = box.y + box.h * ART.top;
    const w = box.w * (ART.right - ART.left);
    const h = box.h * (ART.bottom - ART.top);

    const scale = Math.min(1, THUMB_MAX / Math.max(w, h));
    const thumb = document.createElement('canvas');
    thumb.width = Math.max(1, Math.round(w * scale));
    thumb.height = Math.max(1, Math.round(h * scale));

    thumb.getContext('2d').drawImage(canvas, x, y, w, h, 0, 0, thumb.width, thumb.height);

    return thumb.toDataURL('image/jpeg', THUMB_QUALITY);
}

/// Luma per pixel, in a flat array. Rec. 601 weights: the exact coefficients do not matter to a hash
/// built from the signs of differences, only that they match the generator's.
function grayscale({ w, h, rgba }) {
    const g = new Uint8Array(w * h);
    for (let i = 0, p = 0; i < g.length; i++, p += 4)
        g[i] = (rgba[p] * 77 + rgba[p + 1] * 150 + rgba[p + 2] * 29) >> 8;
    return { w, h, g };
}

// ---------------------------------------------------------------------------------------------
// Finding the cards
//
// This is the third approach in this file's history and the first that survives contact with a real
// screenshot. The two before it are worth recording, because each failed for a reason that was not
// obvious until real input proved it.
//
// The first autocorrelated the edge-energy profile of the whole image, looking for the period of the
// card grid. It reports a slot pitch of 42 pixels on a screenshot whose cards are 192 apart, because
// a real screenshot is mostly TEXT, and text carries far more edge energy than the thin gutters
// between cards.
//
// The second found cards as connected regions and trusted each region's own extent as the card's
// box. That located cards well and framed them badly: a region is the extent of the MASK, not of the
// card, and it varied by eight pixels between cards that are identical on screen. Eight pixels is
// fatal — the fingerprint needs the card to within about two.
//
// What works is to use regions to find the cards and then throw their individual extents away. Every
// card on one screen is the same size and sits on the same pitch, so the AGREEMENT across all of
// them is far better evidence than any one of them.

/// Finds the cards and the grid they sit on, or null when the picture holds no cards.
function findCards(gray, pixels) {
    const blocks = components(mask(gray, pixels));
    const regions = cardShaped(blocks, gray.w);

    // Assembling cards out of their pieces fills the gaps the mask left; it does not replace what
    // the mask found. Where a card masked as one shape, that shape is the better evidence and the
    // assembly is not consulted for it.
    //
    // This used to run only when NOTHING card-shaped was found anywhere, on the reasoning that a
    // pale card among solid ones is recovered by extending its row. That holds for a pale card, and
    // fails for a pale ROW: extending a row needs one card in it to fix the phase, and a pack whose
    // second row is two white-bodied cards has none. A mixed pack reveal is the common case, not
    // the edge — the newest set is Team Rocket's, half its cards have white bodies, and a pack of
    // three coloured and two white read as three cards with the other two never looked at.
    //
    // What an assembly is held to, where the mask found any cards at all to hold it to. A join is a
    // guess about where a broken card's pieces end, and it is loose enough that the game's own
    // furniture chains into something card-shaped now and then: a reveal's "Next" button and the
    // banner above it assemble into a passable card below the real ones. One invented region is
    // enough to drag the consensus size off every genuine card and hang a row of empty slots under
    // them, so two cheap conditions keep them out, and neither applies when the mask found nothing
    // — there is then no size and no place to check against, and the assembly stands on its own,
    // which is the all-white reveal joined() was written for.
    const masked = regions.length > 0 ? quantile(regions.map(r => r.w), 0.9) : 0;
    const near = masked / CARD_ASPECT * 0.25;
    const top = Math.min(...regions.map(r => r.y));
    const bottom = Math.max(...regions.map(r => r.y + r.h));

    const rebuilt = cardShaped(joined(blocks, gray.w), gray.w)
        // Size. Furniture cannot come out the size of the cards already on screen: measured across
        // these screenshots, every genuine assembly lands between 0.96 and 1.05 of the masked
        // width, and the invented one lands at 1.13.
        .filter(r => masked === 0 || (r.w >= masked * 0.90 && r.w <= masked * 1.10))
        // Place. Rows of cards butt up against one another, so the row below a row of cards starts
        // where that one ended, give or take a gutter. An assembly starting well clear of
        // everything the mask found is not the next row of anything: a genuine recovered row starts
        // a ninth of a card below the last masked one, and the two invented ones start a third and
        // a half of a card below.
        .filter(r => masked === 0 || (r.y - bottom <= near && top - (r.y + r.h) <= near))
        // Not a card the mask already has. joined() will not use a piece that is already
        // card-shaped, but a card can still break into a shaped panel plus scraps that chain into a
        // second region over the same card, and two boxes on one card is two readings of it.
        .filter(r => !regions.some(o => overlap(r, o) > Math.min(r.w * r.h, o.w * o.h) * 0.25));

    const found = [...regions, ...rebuilt];
    const assembled = rebuilt.length > 0;
    if (found.length === 0) return null;

    // The card's size from a HIGH QUANTILE of the regions, not their middle. The mask can fall short
    // of a card's edge — it does, by four to eight pixels, depending on what the card has near its
    // border — but it cannot reach past one, so the largest regions are the honest ones. Measured
    // across two screenshots of the same screen: the median gives 192x268 on one and 184x260 on the
    // other, where the truth is 192x268 both times, and eight pixels is enough to make every card
    // unrecognisable. The quantile gives 192x268 for both.
    const cardW = quantile(found.map(r => r.w), 0.9);

    // The height needs the same high quantile and cannot have it, because the reasoning that
    // justifies one for the width — the mask can fall short of a card's edge but never reach past
    // it — is false vertically. The game draws the "NEW" flash ABOVE a card's top edge and the
    // copy-count banner BELOW its bottom one, so a region that swallows either is taller than the
    // card it came from, and a quantile chosen to pick the tallest regions picks exactly those.
    //
    // Measured on 23 pack screenshots: a clean region is 244 tall where the card is 245, and a
    // badged one is 264 — eight percent, where four pixels is enough to lose the card. It shows up
    // as a whole screenful failing at once, because the flash is drawn on every card in a pack the
    // player has none of, which is the newest set, which is the one being opened.
    //
    // So the tall ones are dropped before the quantile rather than clamped after it, and what
    // remains is measured as before. Regions are called too tall against the height this shot's own
    // card WIDTH implies, allowing a little for that width's own under-reach: the contaminated ones
    // sit 7.5% over and the honest ones within 2.8%, so the two do not overlap. Dropping too many
    // is safe and dropping too few is not — with nothing left the aspect-derived height stands,
    // and that is right to within a pixel or two either way.
    const derived = Math.round(cardW / CARD_ASPECT);
    const upright = found.filter(r => r.h <= derived * TALLEST_HONEST);
    const cardH = upright.length > 0
        ? Math.max(quantile(upright.map(r => r.h), 0.9), derived)
        : derived;

    // Regions well off that size are furniture that happened to be card-shaped — a booster pack
    // thumbnail, a button. Dropped before they can drag the rows and columns around.
    const cards = found.filter(r => r.w >= cardW * 0.7 && r.w <= cardW * 1.3);
    if (cards.length === 0) return null;

    // Columns from the leftmost edge seen in each group, rows from the bottom edge. Each is the side
    // that under-reaches least: a card's left edge is found reliably, its top is not, and anchoring
    // the top as "bottom minus the card's height" lands within a pixel or two where taking the
    // region's own top lands six out.
    const columns = lines(cards.map(r => r.x), cardW * LINE_TOLERANCE, Math.min);
    const rowTops = lines(cards.map(r => r.y), cardH * LINE_TOLERANCE, Math.min)
        .map(top => {
            const inRow = cards.filter(c => Math.abs(c.y - top) <= cardH * LINE_TOLERANCE);

            // Only cards the screen shows whole may anchor a row. A card cut off at the bottom of
            // the screen has a bottom edge that is the screen's, not the card's, so anchoring from
            // it puts the row wherever the cut happened to fall — and one such row is enough to
            // throw off the pitch every other row is then placed by.
            const whole = inRow.filter(c => c.h >= cardH * 0.9);
            return whole.length > 0 ? quantile(whole.map(c => c.y + c.h), 0.9) - cardH : top;
        })
        .filter(top => top >= 0);

    if (rowTops.length === 0) return null;

    const pitch = columnPitch(cards, columns, cardW, cardH);
    const origin = Math.min(...columns);
    const fullWidth = columns.length >= GRID_COLUMNS;
    const rows = interpolate(rowTops).map(top => ({
        top,
        columns: slotsInRow(cards, top, cardH, pitch, origin, cardW, gray.w, fullWidth),
    }));

    return { cards, cardW, cardH, rows, origin, pitch, assembled };
}

/// The spacing between card columns, taken from the row that shows the most of them, or 0 when the
/// picture does not say. One row is better evidence than all of them pooled: on a hand of five, laid
/// out three then two, the second row sits half a pitch across from the first, and pooling the two
/// gives a spacing that is neither.
function columnPitch(cards, columns, cardW, cardH) {
    const byRow = new Map();
    for (const card of cards) {
        const key = [...byRow.keys()].find(y => Math.abs(y - card.y) <= cardH * LINE_TOLERANCE) ?? card.y;
        byRow.set(key, [...(byRow.get(key) ?? []), card.x]);
    }

    const widest = [...byRow.values()].sort((a, b) => b.length - a.length)[0] ?? [];
    const from = widest.length >= 2 ? widest : columns;
    if (from.length < 2) return 0;

    const sorted = [...from].sort((a, b) => a - b);
    const gap = median(...sorted.slice(1).map((at, i) => at - sorted[i]));

    // What the gap between two found cards says about the spacing, which is not the same number.
    //
    // Cards do not overlap, so a slot is at least a card wide. A gap of two card widths is
    // therefore not a two-card-wide slot, it is two slots with nothing found in the middle one —
    // the pale cards the mask misses, on the very screens where they cluster. Taken at face value
    // that gap tiles the row at double pitch and the skipped card is never even looked at.
    //
    // Dividing errs towards more slots than there are cards, which is the right way to be wrong: a
    // slot with nothing in it fingerprints to nothing and is reported unread, where a slot that was
    // never emitted loses a card the picture plainly shows.
    const step = gap / Math.max(1, Math.floor(gap / cardW));

    // Then that spacing re-measured across the whole row rather than believed from one gap.
    //
    // A gap is the distance between two found cards, and either card being a few pixels out of
    // place is worth the whole error. That is not hypothetical: an iOS volume overlay sitting over
    // the first card of a row is masked as part of it, and the card's left edge is then reported
    // twenty pixels early. The row's two gaps come out 212 and 192 where both are truly 202, and
    // median() of an even count takes the upper one -- so the contaminated gap becomes the pitch
    // outright.
    //
    // The span from the first card to the last carries the same error over every slot it crosses
    // instead of one, which for a row of three halves it and for a five-across list quarters it.
    // How many slots that is comes from the gap estimate, which is accurate enough to count with
    // long before it is accurate enough to measure with.
    //
    // A pitch estimated long is not a rounding detail. It is what put both cards of IMG_1234's
    // bottom row on column 1, and a column is an identity -- see slots().
    const span = sorted[sorted.length - 1] - sorted[0];
    const pitch = span > 0 ? span / Math.max(1, Math.round(span / step)) : step;

    // Below a card's width the number cannot be a spacing at all. It is what the fallback above
    // produces on a hand of five when only one card per row was found: the two rows are offset by
    // half a slot, so the "columns" it measures across them are half-pitch apart and describe a row
    // that does not exist. Zero says so, and the row is left with the cards actually found.
    return pitch >= cardW ? pitch : 0;
}

/// Where the cards in one row sit, including the places a card should be and was not found.
///
/// Per row rather than once for the whole picture, because a hand of five is laid out three then two
/// and the second row is offset by half a column. Sharing one set of columns across both rows means
/// the second row's slots are never looked at — which is exactly how a pale card in that row went
/// missing, since it is not found by its own colour either.
///
/// The row's own cards fix the phase and the pitch extends it. On a full-width card list that fills
/// the row, which is what lets the five-across view report the cards you do not own. On a hand it
/// adds a slot or two, and a slot holding nothing fingerprints to nothing.
function slotsInRow(cards, top, cardH, pitch, origin, cardW, imageWidth, fullWidth) {
    const here = cards
        .filter(c => Math.abs(c.y - top) <= cardH * LINE_TOLERANCE)
        .map(c => c.x)
        .sort((a, b) => a - b);

    const seed = here.length > 0 ? here : [origin];
    if (pitch <= 0) return seed;

    const leftmost = fullWidth ? 0 : Math.min(...seed) - pitch;
    const rightmost = fullWidth ? imageWidth : Math.max(...seed) + pitch;

    // Where a card was actually found beats where the pitch says one should be, and the order these
    // go in is the whole of that rule. Both are offered, two slots half a pitch apart are the same
    // slot, and whichever was offered first is the one kept — so seeding with the found cards is
    // what stops a tiled guess from standing in for a card whose position is known.
    //
    // It is not a small difference. A row of a pack's reveal is tiled from its leftmost card, and
    // the game's own spacing is not exactly uniform, so by the far end of the row the tiling is six
    // pixels off the card sitting there. Six pixels is not a near miss at this sampling rate: the
    // card that reads at 10 bits from its own box reads at 27 from the tiled one, which is over the
    // threshold and lost. Sorting first and deduplicating afterwards, as this did, kept whichever
    // came first left-to-right — the tiled one, every time.
    const at = [];
    const offer = x => { if (!at.some(y => Math.abs(y - x) <= pitch * 0.5)) at.push(x); };

    seed.forEach(offer);
    for (let x = seed[0] - pitch; x >= leftmost; x -= pitch) offer(Math.round(x));
    for (let x = seed[0] + pitch; x <= rightmost; x += pitch) offer(Math.round(x));

    return at
        .filter(x => x >= 0 && x + cardW <= imageWidth)
        .sort((a, b) => a - b);
}

/// Blocks that might be part of a card: coloured, or carrying text. Either alone misses a screen —
/// colour misses the white-bodied cards, and text alone would take the game's own text everywhere.
function mask({ w, h, g }, pixels) {
    const bw = Math.ceil(w / BLOCK), bh = Math.ceil(h / BLOCK);
    const blocks = new Uint8Array(bw * bh);

    for (let by = 0; by < bh; by++) {
        for (let bx = 0; bx < bw; bx++) {
            let coloured = 0, counted = 0, gradient = 0, pairs = 0;

            for (let y = by * BLOCK; y < Math.min(h, (by + 1) * BLOCK); y++) {
                for (let x = bx * BLOCK; x < Math.min(w, (bx + 1) * BLOCK); x++) {
                    const i = y * w + x, p = i * 4;
                    const r = pixels.rgba[p], gg = pixels.rgba[p + 1], b = pixels.rgba[p + 2];
                    const max = Math.max(r, gg, b), min = Math.min(r, gg, b);

                    if (max > 0 && (max - min) / max >= SATURATION) coloured++;
                    counted++;

                    if (x + 1 < w) { gradient += Math.abs(g[i + 1] - g[i]); pairs++; }
                    if (y + 1 < h) { gradient += Math.abs(g[i + w] - g[i]); pairs++; }
                }
            }

            const byColour = counted > 0 && coloured / counted >= SATURATED_FRACTION;
            const byDetail = pairs > 0 && gradient / pairs >= BLOCK_EDGE;
            blocks[by * bw + bx] = byColour || byDetail ? 1 : 0;
        }
    }

    return { blocks, bw, bh };
}

/// Bounding boxes of the mask's 8-connected regions, in image pixels.
function components({ blocks, bw, bh }) {
    const seen = new Uint8Array(bw * bh);
    const found = [];
    const stack = [];

    for (let start = 0; start < bw * bh; start++) {
        if (!blocks[start] || seen[start]) continue;

        stack.length = 0;
        stack.push(start);
        seen[start] = 1;
        let x0 = bw, y0 = bh, x1 = -1, y1 = -1, filled = 0;

        while (stack.length > 0) {
            const at = stack.pop();
            const ax = at % bw, ay = (at - ax) / bw;
            filled++;
            if (ax < x0) x0 = ax;
            if (ax > x1) x1 = ax;
            if (ay < y0) y0 = ay;
            if (ay > y1) y1 = ay;

            for (let dy = -1; dy <= 1; dy++) {
                for (let dx = -1; dx <= 1; dx++) {
                    const nx = ax + dx, ny = ay + dy;
                    if (nx < 0 || ny < 0 || nx >= bw || ny >= bh) continue;
                    const next = ny * bw + nx;
                    if (blocks[next] && !seen[next]) { seen[next] = 1; stack.push(next); }
                }
            }
        }

        found.push({
            x: x0 * BLOCK, y: y0 * BLOCK,
            w: (x1 - x0 + 1) * BLOCK, h: (y1 - y0 + 1) * BLOCK,
            fill: filled / ((x1 - x0 + 1) * (y1 - y0 + 1)),
        });
    }

    return found;
}

/// The area two regions share, in pixels. Zero when they do not meet.
function overlap(a, b) {
    const w = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x);
    const h = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y);
    return w > 0 && h > 0 ? w * h : 0;
}

/// Pieces of one pale card, joined into a single region.
///
/// A card with a small illustration panel over a large white body does not mask as one shape. The
/// panel is coloured and the attack text below it is detailed, but the plain body between and around
/// them is neither, so the card arrives as two or three regions stacked in the same columns with
/// unmasked white bands between them. Every card on a Team Rocket's Ambition pack reveal is like
/// that, which is why that screen found nothing at all: with no card-shaped region anywhere there is
/// no size to pin the grid to and no seed for a row to extend from.
///
/// Joining is deliberately timid, and two rules do the work:
///
///   - A piece that is ALREADY card-shaped is never touched, as a head or as a member. Where the
///     mask found the card, it found the card; this only ever assembles what it broke.
///   - A join has to produce the shape of a card. That is what keeps a column of cards on the
///     three-across list from fusing into one stripe — those sit 8 pixels apart and share their
///     columns exactly, and the aspect of the union is the only thing that tells them from the
///     halves of one card.
function joined(regions, imageWidth) {
    const shaped = r => r.w >= imageWidth * MIN_CARD_WIDTH && r.h >= 30
                     && r.w / r.h >= ASPECT_MIN && r.w / r.h <= ASPECT_MAX;

    // Solid, wide enough to be part of a card, and not a card already.
    const pieces = regions
        .map((r, at) => ({ r, at }))
        .filter(({ r }) => r.fill >= 0.55 && r.w >= imageWidth * MIN_CARD_WIDTH && !shaped(r))
        .sort((a, b) => a.r.y - b.r.y);

    const taken = new Set();
    const chains = [];

    for (const head of pieces) {
        if (taken.has(head.at)) continue;

        let chain = { ...head.r };
        const members = [head.at];

        for (const { r: next, at } of pieces) {
            if (taken.has(at) || members.includes(at) || next.y < chain.y) continue;

            const shared = Math.min(chain.x + chain.w, next.x + next.w) - Math.max(chain.x, next.x);
            if (shared < Math.min(chain.w, next.w) * STACK_OVERLAP) continue;

            const x = Math.min(chain.x, next.x), y = Math.min(chain.y, next.y);
            const w = Math.max(chain.x + chain.w, next.x + next.w) - x;
            const h = Math.max(chain.y + chain.h, next.y + next.h) - y;

            if (next.y - (chain.y + chain.h) > (w / CARD_ASPECT) * STACK_GAP) continue;
            if (w / h < ASPECT_MIN || w / h > ASPECT_MAX) continue;

            // The fill of the least solid piece, not of the union: the white body between them is
            // unmasked by nature, and averaging it in would reject every card this exists for.
            chain = { x, y, w, h, fill: Math.min(chain.fill, next.fill) };
            members.push(at);
        }

        if (members.length > 1) {
            for (const at of members) taken.add(at);

            // Anchored on the bottom edge and sized by the card aspect, rather than reported as the
            // extent of its own pieces. A joined region's top is the least trustworthy thing about
            // it: the game draws a NEW flash that hangs 20 pixels above the card it belongs to, and
            // the flash masks as part of the illustration panel. The bottom is where the card's own
            // content ends and nothing protrudes past it.
            const h = Math.round(chain.w / CARD_ASPECT);
            chains.push({ x: chain.x, y: chain.y + chain.h - h, w: chain.w, h, fill: chain.fill });
        }
    }

    return [...regions.filter((_, at) => !taken.has(at)), ...chains];
}

/// Regions the size and shape of a card, in reading order.
function cardShaped(regions, imageWidth) {
    return regions
        .filter(r => r.w >= imageWidth * MIN_CARD_WIDTH && r.h >= 30 && r.fill >= 0.55)
        .filter(r => r.w / r.h >= ASPECT_MIN && r.w / r.h <= ASPECT_MAX)
        .sort((a, b) => a.y - b.y || a.x - b.x);
}

/// Positions grouped into lines, each line reported by `pick` over its members. Which statistic is
/// right depends on the direction the mask errs in — see findCards.
function lines(positions, tolerance, pick = median) {
    const sorted = [...positions].sort((a, b) => a - b);
    const groups = [];

    for (const at of sorted) {
        const last = groups[groups.length - 1];
        if (last && at - last[last.length - 1] <= tolerance) last.push(at);
        else groups.push([at]);
    }

    return groups.map(g => pick(...g));
}

/// Fills gaps between the first and last row using the closest spacing seen. Interpolation only: a
/// screenshot's card area is bounded by the game's own bars, and extending past the last row would
/// invent slots in the navigation.
function interpolate(rows) {
    if (rows.length < 2) return rows;

    // The median gap, not the smallest. A row anchored badly — the last one on screen, half cut off
    // — shortens one gap, and taking the smallest would then re-space every row by it.
    const pitch = median(...rows.slice(1).map((at, i) => at - rows[i]));
    if (pitch <= 0) return rows;

    const out = [];
    for (let at = rows[0]; at <= rows[rows.length - 1] + pitch * 0.4; at += pitch) {
        out.push(Math.round(at));
        if (out.length > 40) break;
    }

    return out;
}

const median = (...values) => {
    const sorted = [...values].sort((a, b) => a - b);
    return sorted[sorted.length >> 1];
};

/// The value at a given position through the sorted list. Used at 0.9 rather than 0.5 wherever the
/// measurement is known to under-reach.
const quantile = (values, at) => {
    const sorted = [...values].sort((a, b) => a - b);
    return sorted[Math.min(sorted.length - 1, Math.floor(at * (sorted.length - 1)))];
};

/// One cell per slot of the grid, each at the consensus card size.
///
/// A slot with a card in it is measured. A slot with nothing in it is still reported, with its
/// statistics left at zero, because on the five-across card list an empty slot is a card the player
/// does not own — the most useful thing on that screen. On a hand of five there are no empty slots
/// to report, which is why the columns are not tiled across the image in that case.
function slots(gray, pixels, found) {
    const { cards, cardW, cardH, rows, origin, pitch, assembled } = found;
    const out = [];

    for (let row = 0; row < rows.length && out.length < MAX_CELLS; row++) {
        // The last column number given out in this row. Rows are independent: the same number in
        // two rows is two different slots and always was.
        let previous = -1;

        for (const x of rows[row].columns) {
            if (out.length >= MAX_CELLS) break;

            const box = { x, y: rows[row].top, w: cardW, h: cardH };
            if (box.x < 0 || box.y < 0 || box.x + box.w > gray.w || box.y + box.h > gray.h) continue;

            // The column index counts from the grid's own origin, so a card keeps the same number
            // whichever row it is in. On a card list that number IS its place in the set's ordering,
            // which is what lets a recognised card name the blanks beside it.
            //
            // And because it is an identity, it has to be unique within its row. Row and column are
            // the whole of a slot's name on the other side of the wire: PackProphet.Vision keys both
            // its recognition and its unread list by them, so two slots answering to one name are
            // one slot, and the second card is not merely misread -- it never reaches the reader at
            // all. It cannot be recognised, and it cannot be offered to be named by hand either,
            // since that list is built from the slots the reader saw and gave up on.
            //
            // Nothing above guarantees uniqueness, because the number is a position divided by an
            // estimated pitch and rounded. Estimate the pitch five percent long and two neighbours
            // round to the same number, which is what IMG_1234 does -- a hand of five whose bottom
            // two cards both came out as column 1, so the fifth card of the pack simply vanished.
            //
            // A row's slots arrive sorted and no two sit within half a pitch of one another, so
            // their numbers must strictly increase; where the arithmetic says otherwise it is the
            // arithmetic that is wrong. Nudging the number keeps the card, and a card at a number
            // one too high is visible and correctable in a way a deleted card is not.
            const col = Math.max(previous + 1, pitch > 0 ? Math.round((x - origin) / pitch) : 0);
            previous = col;

            const here = cards.find(c => Math.abs(c.x - box.x) <= cardW * LINE_TOLERANCE
                                      && Math.abs(c.y - box.y) <= cardH * LINE_TOLERANCE);

            // A card the screen cut off cannot be fingerprinted — the picture holds part of it. Its
            // region is much shorter than the consensus, which is how it is told from a whole one,
            // and it is reported as a slot that could not be read rather than measured wrongly.
            // A card the screen cut off still gets its picture. It cannot be fingerprinted and it
            // is perfectly legible to a person, which is exactly the case the thumbnail is for.
            if (here && here.h < cardH * 0.9) {
                out.push({
                    row, col, box: [box.x, box.y, box.w, box.h],
                    hash: '', luma: 0, saturation: 0, detail: 1,
                    thumb: out.length < THUMB_CELLS ? thumbnail(pixels, box) : '',
                });
                continue;
            }

            // Slots with no region behind them are still measured. On the five-across list that is a
            // card the player does not own, and on a hand it is where a pale card the mask could not
            // see might be — either way the fingerprint decides, not the mask.
            out.push({
                row, col,
                box: [box.x, box.y, box.w, box.h],
                ...measure(gray, pixels, box),
                nearby: nudged(gray, pixels, box, assembled),
                digits: readBadge(gray, box),
                thumb: out.length < THUMB_CELLS ? thumbnail(pixels, box) : '',
            });
        }
    }

    return out;
}

// ---------------------------------------------------------------------------------------------
// The copy-count badge
//
// The three-across card list prints how many copies of each card you hold, on a dark ribbon across
// the card's bottom-left corner. Reading it is a far easier problem than recognising card art — ten
// shapes in the game's own fixed-pitch font, always the same size relative to the card — and the
// only reason it needs care is that a misread digit is a silent wrong answer: 1 where the truth is
// 14 looks exactly like a card you own one of.
//
// This finds the badge and cuts its digits out. Which digit each one is gets decided by
// PackProphet.Vision.CountReader, against glyphs cut from the reference screenshots.

/// The digits on a card's badge, left to right. Empty when the card has no badge, which is every
/// screen but the three-across list.
function readBadge(gray, card) {
    const badge = findBadge(gray, card);
    if (!badge) return [];

    const level = inkLevel(gray, badge);
    const runs = digitRuns(gray, badge, level);

    return runs.map(run => glyphOf(gray, badge, run, level)).filter(Boolean);
}

/// The badge itself: the dark block in the card's bottom-left corner. Found from the rows and columns
/// that are mostly dark, rather than by connected pixels, because the digits sit inside it and would
/// otherwise split it in two.
function findBadge({ w, h, g }, card) {
    const x0 = card.x;
    const y0 = card.y + Math.round(card.h * BADGE_TOP);
    const x1 = Math.min(w, card.x + Math.round(card.w * BADGE_RIGHT));
    const y1 = Math.min(h, card.y + card.h + 1);
    if (x1 - x0 < 20 || y1 - y0 < 10) return null;

    const perRow = new Map(), perCol = new Map();
    for (let y = y0; y < y1; y++) {
        for (let x = x0; x < x1; x++) {
            if (g[y * w + x] >= BADGE_DARK) continue;
            perRow.set(y, (perRow.get(y) ?? 0) + 1);
            perCol.set(x, (perCol.get(x) ?? 0) + 1);
        }
    }

    const rows = [...perRow].filter(([, n]) => n >= (x1 - x0) * 0.35).map(([y]) => y).sort((a, b) => a - b);
    const cols = [...perCol].filter(([, n]) => n >= (y1 - y0) * 0.25).map(([x]) => x).sort((a, b) => a - b);
    if (rows.length < 6 || cols.length < 10) return null;

    return {
        x: cols[0], y: rows[0],
        w: cols[cols.length - 1] - cols[0] + 1,
        h: rows[rows.length - 1] - rows[0] + 1,
    };
}

/// Where the ink starts, half way between the badge's own darkness and its brightest stroke. Relative
/// rather than fixed, because a badge over a bright card is lighter than one over a dark card and a
/// fixed cutoff loses the thin strokes on the lighter ones.
function inkLevel({ w, g }, badge) {
    const values = [];
    for (let y = badge.y; y < badge.y + badge.h; y++)
        for (let x = badge.x; x < badge.x + badge.w; x++) values.push(g[y * w + x]);

    values.sort((a, b) => a - b);
    const dark = values[Math.floor(values.length * 0.30)];
    const bright = Math.max(values[Math.floor(values.length * 0.97)], dark + 30);
    return { dark, bright, ink: Math.max(dark + 18, Math.round((dark + bright) / 2)) };
}

/// Column spans holding a digit. Three things have to be got right here and each one was a wrong
/// answer first:
///
///   - The badge's right end is cut on a slant, so its edge reads as a bright run. Runs touching the
///     last columns are the badge, not a digit.
///   - A Wonder Pick badge carries a small gold dot beside the count, marking a card held ten times
///     or more. It is shorter than a digit, which is how it is told apart.
///   - Digits touch. "20" is one unbroken run, and is cut in two by the font's pitch.
function digitRuns({ w, g }, badge, { ink }) {
    const inked = [];
    for (let x = badge.x; x < badge.x + badge.w; x++) {
        let n = 0;
        for (let y = badge.y; y < badge.y + badge.h; y++) if (g[y * w + x] >= ink) n++;
        inked.push(n > 0);
    }

    // A thin stroke can drop out of a single column. Healing that is safe: no two digits are one
    // column apart.
    for (let i = 1; i < inked.length - 1; i++)
        if (!inked[i] && inked[i - 1] && inked[i + 1]) inked[i] = true;

    const spans = [];
    let from = -1;
    for (let i = 0; i < inked.length; i++) {
        if (inked[i] && from < 0) from = i;
        else if (!inked[i] && from >= 0) { if (i - from >= 2) spans.push([from, i - 1]); from = -1; }
    }
    if (from >= 0 && inked.length - from >= 2) spans.push([from, inked.length - 1]);

    const inside = spans.filter(([, to]) => to < inked.length - 2);
    if (inside.length === 0) return [];

    // Two filters, because either alone leaves a hole. The relative one separates digits from the
    // shorter marks beside them, and cannot help when the only span found is a mark. The absolute
    // one knows what a digit is: it fills well over half the ribbon's height, and the corner and
    // edge artefacts fill a seventh of it.
    const measured = inside.map(span => ({ span, ...verticalExtent({ w, g }, badge, span, ink) }))
                           .filter(m => m.h >= badge.h * MIN_DIGIT_HEIGHT);
    if (measured.length === 0) return [];

    const tallest = Math.max(...measured.map(m => m.h));
    const digits = measured.filter(m => m.h >= tallest * 0.75);

    const out = [];
    for (const { span: [from2, to], top, h } of digits) {
        const width = to - from2 + 1;
        const count = Math.max(1, Math.round(width / (DIGIT_PITCH * h)));
        const step = width / count;

        for (let i = 0; i < count; i++) {
            const a = from2 + Math.round(i * step);
            const z = from2 + Math.round((i + 1) * step) - 1;
            if (z - a + 1 >= 2) out.push({ from: a, to: z, top, h });
        }
    }

    return out;
}

/// A span's tallest unbroken stack of inked rows, rather than the distance between its topmost and
/// bottommost ink.
///
/// The difference is the whole of a bug that lost three counts in a row. The badge has bright
/// artwork at both ends — a slanted right edge and a rounded bottom-left corner — and the corner
/// puts a few pixels in the same columns that a stray bright pixel on the badge's top row can also
/// land in. Measured end to end, those two specks 27 rows apart make a span as tall as the badge
/// itself. Nothing is there, but it becomes the tallest span, and the relative filter below then
/// throws away every real digit for being shorter than a thing that is not a digit.
///
/// Every digit from 0 to 9 has ink in every row of its own box, so the longest run is the right
/// measure of one and is exactly what a pair of specks cannot fake.
function verticalExtent({ w, g }, badge, [from, to], ink) {
    let best = 0, bestTop = badge.y, run = 0;

    for (let y = badge.y; y < badge.y + badge.h; y++) {
        let lit = false;
        for (let x = badge.x + from; x <= badge.x + to && !lit; x++) lit = g[y * w + x] >= ink;

        run = lit ? run + 1 : 0;
        if (run > best) { best = run; bestTop = y - run + 1; }
    }

    return { top: bestTop, h: best };
}

/// One digit, as ink coverage on a fixed grid plus its aspect. Grey rather than black and white
/// because a glyph is about ten pixels across and how much of a cell a stroke covers is most of what
/// distinguishes an 8 from a 3; measured both ways, and binary gets digits wrong. The aspect goes
/// separately because normalising into a fixed grid discards it, and it is what tells a 1 from
/// everything else outright.
function glyphOf({ w, g }, badge, { from, to, top, h }, { dark, bright }) {
    const x0 = badge.x + from;
    const width = to - from + 1;
    if (width < 2 || h < 5) return null;

    let grey = '';
    for (let j = 0; j < GLYPH_H; j++) {
        const ya = top + Math.floor(j * h / GLYPH_H);
        const yb = top + Math.max(ya + 1 - top, Math.floor((j + 1) * h / GLYPH_H));

        for (let i = 0; i < GLYPH_W; i++) {
            const xa = x0 + Math.floor(i * width / GLYPH_W);
            const xb = x0 + Math.max(xa + 1 - x0, Math.floor((i + 1) * width / GLYPH_W));

            let sum = 0, n = 0;
            for (let y = ya; y < yb; y++) {
                for (let x = xa; x < xb; x++) {
                    sum += Math.min(1, Math.max(0, (g[y * w + x] - dark) / (bright - dark)));
                    n++;
                }
            }
            grey += Math.round((n > 0 ? sum / n : 0) * 15).toString(16);
        }
    }

    return { grey, aspect: Math.round(width / h * 100) / 100 };
}

/// The window, as fractions of a card's width and height: everything but the outer frame and the
/// bottom strip. Must match ArtSampler.WindowLeft and friends in PackProphet.Core exactly, which
/// carries the measurements behind these numbers.
///
/// The card is not fingerprinted edge to edge, because a card on screen is not the artwork file. The
/// game draws a gold flair border over any card held ten times or more and prints a copy-count badge
/// across the bottom of it — both on the parts left outside. Sampling the whole card recognised one
/// real card in nine; see KNOWN-ISSUES.md.
const WINDOW = { left: 0.06, right: 0.94, top: 0.06, bottom: 0.85 };

/// The fingerprint and the three statistics C# reasons about, for one card's box. Samples are box
/// averages over a 9x9 grid rather than point reads, so the result does not depend on how much the
/// screenshot was scaled — the same card at two resolutions has to produce the same 128 bits.
///
/// Note which measurements use which region. The fingerprint comes from the art window, because that
/// is the part a screenshot does not alter. Saturation and detail are measured over the WHOLE card,
/// because their job is to tell a drawn card from the blank slot the game puts in its place, and a
/// blank slot is blank everywhere.
function measure(gray, pixels, box) {
    // The window, in the same arithmetic ArtSampler.Window uses so the two agree pixel for pixel.
    const wx = box.x + Math.round(box.w * WINDOW.left);
    const wy = box.y + Math.round(box.h * WINDOW.top);
    const ww = Math.max(1, Math.round(box.w * WINDOW.right) - Math.round(box.w * WINDOW.left));
    const wh = Math.max(1, Math.round(box.h * WINDOW.bottom) - Math.round(box.h * WINDOW.top));

    const window = grid(gray, wx, wy, ww, wh);
    const whole = grid(gray, box.x, box.y, box.w, box.h);
    const colour = colours(gray, pixels, box);

    return {
        hash: hash(window),
        luma: colour.luma,
        saturation: colour.saturation,
        // Over the whole card, not the window: this number's job is to tell a drawn card from the
        // blank slot the game puts in place of one, and a blank slot is blank all over. Measuring it
        // inside the window would also put it at the mercy of the card number the game prints in
        // the middle of a blank, which sits close to the window's lower edge.
        detail: energy(whole),
    };
}

/// Fingerprints of the same card shifted a few pixels each way — eight of them, around the centre
/// crop the cell already carries, and three more at a larger scale when the box was assembled.
///
/// The detector puts a card's box within two to four pixels of its true edges, and cannot reliably
/// do better: the box comes from a mask whose extent depends on what the card has near its border.
/// The fingerprint has no tolerance for that — three pixels is worth ten bits or more — so rather
/// than demand a perfect box, the cell offers a spread and the matcher keeps the crop it can
/// identify most confidently. Measured on a Wonder Pick line-up, where the boxes are three pixels
/// out: the centre crop scores 21 to 22 bits against the right cards and a nudged one scores 0 to 3.
///
/// Every crop still has to pass the same test on its own — close enough, and clear of the next
/// card by the full margin — so offering more of them cannot turn a doubtful reading into a
/// confident one. See ScreenshotReader.Identify.
function nudged(gray, pixels, box, assembled = false) {
    const out = [];

    // A box that was assembled out of a card's interior pieces is not merely misplaced, it is small:
    // the pieces stop at the illustration and the attack text, and the card's plain border is not in
    // the mask at all. Translating such a box cannot fix it, because the error is in its scale.
    //
    // The mask cannot reach past a card but can fall short of it by up to one block, so the box
    // grown by a block on each side is the other end of what the card can be. Offered at three
    // horizontal anchors, since which side the block was lost on is not known.
    //
    // Measured, by putting exactly this error on the pack reveal whose cards ARE in the fingerprint
    // table: a box 4 pixels narrow and 5 short reads 3 of its 5 cards, and the same box with these
    // crops added reads 4, at 3 to 9 bits where the true box scores 4 to 11. Growing by half a block
    // instead recovers nothing, so the whole block is what does the work.
    if (assembled) {
        const w = box.w + BLOCK * 2;
        const h = Math.round(w / CARD_ASPECT);

        for (const dx of [0, -BLOCK, -BLOCK * 2]) {
            const grown = { x: box.x + dx, y: box.y + box.h - h, w, h };
            if (grown.x < 0 || grown.y < 0) continue;
            if (grown.x + grown.w > gray.w || grown.y + grown.h > gray.h) continue;

            out.push(measure(gray, pixels, grown).hash);
        }
    }

    for (const dx of [-NUDGE, 0, NUDGE]) {
        for (const dy of [-NUDGE, 0, NUDGE]) {
            if (dx === 0 && dy === 0) continue;

            const moved = { x: box.x + dx, y: box.y + dy, w: box.w, h: box.h };
            if (moved.x < 0 || moved.y < 0) continue;
            if (moved.x + moved.w > gray.w || moved.y + moved.h > gray.h) continue;

            out.push(measure(gray, pixels, moved).hash);
        }
    }

    return out;
}

/// Box-averaged luma over a 9x9 grid covering the given region.
function grid(gray, x0, y0, w, h) {
    const N = ArtHashGrid;
    const out = new Float32Array(N * N);

    for (let j = 0; j < N; j++) {
        const ya = y0 + Math.floor(j * h / N);
        const yb = y0 + Math.max(ya + 1 - y0, Math.floor((j + 1) * h / N));
        for (let i = 0; i < N; i++) {
            const xa = x0 + Math.floor(i * w / N);
            const xb = x0 + Math.max(xa + 1 - x0, Math.floor((i + 1) * w / N));

            let sum = 0, n = 0;
            for (let y = ya; y < yb && y < gray.h; y++) {
                for (let x = xa; x < xb && x < gray.w; x++) { sum += gray.g[y * gray.w + x]; n++; }
            }
            out[j * N + i] = n ? sum / n : 0;
        }
    }

    return out;
}

/// Mean brightness and mean saturation over the whole card, both 0 to 1.
function colours(gray, pixels, box) {
    let l = 0, s = 0, n = 0;

    // Every fourth pixel in each direction. These two numbers decide "is there a card here", which
    // does not need every pixel, and a card can be a quarter of a megapixel.
    for (let y = box.y; y < box.y + box.h && y < gray.h; y += 4) {
        for (let x = box.x; x < box.x + box.w && x < gray.w; x += 4) {
            const p = (y * gray.w + x) * 4;
            const r = pixels.rgba[p], g = pixels.rgba[p + 1], b = pixels.rgba[p + 2];
            const max = Math.max(r, g, b), min = Math.min(r, g, b);
            l += gray.g[y * gray.w + x];
            s += max === 0 ? 0 : (max - min) / max;
            n++;
        }
    }

    return { luma: n ? l / n / 255 : 0, saturation: n ? s / n : 0 };
}

/// How much large-scale structure a grid holds, 0 to 1. Near zero for a flat region.
function energy(values) {
    const N = ArtHashGrid;
    let sum = 0, n = 0;

    for (let j = 0; j < N - 1; j++) {
        for (let i = 0; i < N - 1; i++) {
            sum += Math.abs(values[j * N + i] - values[j * N + i + 1])
                 + Math.abs(values[j * N + i] - values[(j + 1) * N + i]);
            n += 2;
        }
    }

    return n ? sum / n / 255 : 0;
}

/// 128 bits: 64 signs of the left-to-right gradient, 64 of the top-to-bottom one. Two axes because
/// the near-duplicates in this particular haystack — the same Pokémon drawn twice, a full art beside
/// its plain printing — are too close together for one.
///
/// PARITY: this is the second implementation of one algorithm. The fingerprints it is compared
/// against are generated offline by ArtHash.From in PackProphet.Core, and the two have to agree bit
/// for bit — a hash computed a different way is not a near miss, it is a different card. The shared
/// golden vector, asserted on the C# side by ArtHashTests.TheGoldenVectorMatchesTheBrowserImplementation:
///
///     grid[j * 9 + i] = (i * 29 + j * 53) % 251
///     -> 1040000208104000186080030c106080
///
/// Change either side and re-check both.
function hash(values) {
    const N = ArtHashGrid;
    let rows = 0n, cols = 0n;

    for (let j = 0; j < N - 1; j++) {
        for (let i = 0; i < N - 1; i++) {
            const here = values[j * N + i];
            const bit = BigInt(j * (N - 1) + i);

            if (here > values[j * N + i + 1]) rows |= 1n << bit;
            if (here > values[(j + 1) * N + i]) cols |= 1n << bit;
        }
    }

    return rows.toString(16).padStart(16, '0') + cols.toString(16).padStart(16, '0');
}
