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
| [chase-mew/pokemon-tcg-pocket-cards](https://github.com/chase-mew/pokemon-tcg-pocket-cards) v5 | Attacks, abilities, evolution stage, element, HP, retreat, weakness, evolves-from | AGPL-3.0-or-later |
| [flibustier/pokemon-tcg-pocket-database](https://github.com/flibustier/pokemon-tcg-pocket-database) | **Pull rates** (published nowhere else), pack membership, rarity economics (pack points, shinedust), set/series grouping, artwork filenames | MIT, © Jon (flibustier) |
| [flibustier/pokemon-tcg-exchange](https://github.com/flibustier/pokemon-tcg-exchange) | Card and booster artwork | MIT, © Jon (flibustier) |

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
