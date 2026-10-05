# Data sources and licensing

PackProphet is licensed under the **GNU Affero General Public License v3.0 or later**
(see [LICENSE](LICENSE)).

The licence follows the card data. `chase-mew/pokemon-tcg-pocket-cards` v5 is the only
community dataset carrying attack text, ability text and evolution stage for **every** set, and
it is AGPL-3.0-or-later. The alternatives considered were an MIT dataset without that
information, a dataset with no licence at all, and scraping the origin directly.

## Card data

| Source | Used for | Licence |
| --- | --- | --- |
| [chase-mew/pokemon-tcg-pocket-cards](https://github.com/chase-mew/pokemon-tcg-pocket-cards) v5 | Attacks, abilities, evolution stage, element, HP, retreat, weakness, evolves-from; expansion logos; **card artwork, as the last source in the chain** | AGPL-3.0-or-later |
| [flibustier/pokemon-tcg-pocket-database](https://github.com/flibustier/pokemon-tcg-pocket-database) | **Pull rates** (published nowhere else), pack membership, rarity economics (pack points, shinedust), set/series grouping, artwork filenames; booster artwork and expansion logos | MIT, © Jon (flibustier) |
| [TCGdex](https://tcgdex.dev) | Card artwork, as the first source in the chain, for the sets it has | MIT |
| [Limitless TCG](https://pocket.limitlesstcg.com/cards) | Card artwork, as the second source in the chain, and the first for sets TCGdex has not reached | No published terms |

### Why artwork has more than one source

Card data and card artwork are published from two different repositories on two different
cadences. The data ships within days of a set going live; the artwork is a manual commit that
lands when it lands. B4a's data was published on 2026-08-27 and its artwork was still absent a
week later, which drew every card in the newest set as a placeholder.

So artwork is a chain rather than a single URL — see `PackProphet.Core/Domain/ArtSource.cs`. Until
2026-10-05 it began at flibustier/pokemon-tcg-exchange, which that day replaced its card and
booster directories with links to a checkout outside the repository. TCGdex and Limitless TCG took
its place: the only two hosts found with the whole catalogue between them.

The licences above cover each project's code and data. The artwork itself is © The Pokémon
Company, Nintendo, Creatures Inc. and GAME FREAK inc., and none of these sources can license it.

Artwork for a set Limitless has not published yet is taken from the
[pokemon-tcg-pocket-database release archive](https://github.com/flibustier/pokemon-tcg-pocket-database/releases)
at deploy time — the same MIT source as the second row above, and the same artwork, distributed by
its author as a release asset rather than per file. It is written into the published site and never committed;
see `tools/vendor-gap-art.py`.

Card data ultimately originates from **[Limitless TCG](https://pocket.limitlesstcg.com/cards)**,
from which the datasets above are compiled.

## Reverse-engineered deck share format

The Pokémon TCG Pocket deck-share code format was reverse-engineered by
**[Nirostar](https://github.com/Nirostar/ptcgp-deck-qr)** and published under the MIT License.
`PackProphet.Core/Deck/DeckCodec.cs` is an independent C# port of that format description.

## Bundled third-party code

| Component | Used for | Licence |
| --- | --- | --- |
| [jsQR](https://github.com/cozmo/jsQR) 1.4.0, vendored as `wwwroot/js/vendor-jsQR.js` | Reading a deck share code out of an uploaded screenshot | Apache-2.0, © Cosmo Wolfe |
| [QRCoder](https://github.com/codebude/QRCoder) | Rendering a deck's share code as a scannable image | MIT, © Raffael Herrmann |

jsQR is vendored rather than fetched from a CDN so the app works offline, and it is loaded only
when a screenshot import is attempted. It is larger than the rest of the app's JavaScript
combined.

Decoding happens entirely in the browser: the screenshot is drawn to a canvas and the pixels are
read locally. **No image is ever uploaded anywhere.**

## Trademarks

Pokémon and Pokémon TCG Pocket are trademarks of Nintendo, Creatures Inc. and GAME FREAK inc.
This project is an unofficial fan tool, unaffiliated with and unendorsed by any of them.

The **Paper** palette is an homage drawn by eye, in colours picked for contrast rather than matched
to a specification. No official logo, wordmark, typeface or other brand asset is bundled or
reproduced, and the **Slate** palette carries none of it.
