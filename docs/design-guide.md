# Design guide

NEXDECK apps share one calm, flat look so they feel like one system.

## Board (480×480)

**Palette (slate)** — background and tiles from the palette, flat colors (gradients glare on this panel):

| Token | Hex | RGB565 helper | Use |
|---|---|---|---|
| BG | `#0F172A` | `rgb(15, 23, 42)` | screen background |
| TILE | `#1E293B` | `rgb(30, 41, 59)` | cards / tiles |
| RAISE | `#334155` | `rgb(51, 65, 85)` | pressed, selected, skeleton base |
| FAINT | `#64748B` | `rgb(100, 116, 139)` | disabled, hints |
| MUTED | `#94A3B8` | `rgb(148, 163, 184)` | secondary text |
| SOFT | `#E2E8F0` | `rgb(226, 232, 240)` | primary buttons (dark text on them), highlights |
| TEXT | `#F8FAFC` | `rgb(248, 250, 252)` | text |

Semantic colors (sparingly): good `#4ADE80`, bad `#F87171`, sun/warning `#FBBF24`.

**Layout:** 12 px outer margin and gaps, tile radius 16, card padding 12–20. Full-screen content uses 36 px side
margins. Titles in `FONT_TITLE`/`FONT_MED`, text in `FONT_TXT`/`FONT_BODY`, captions in `FONT_CAP`.

**Touch:** targets ≥ 48 px; tap = act, swipe = next/previous, **hold 1 s anywhere = back to the launcher** (every
app). Give instant feedback (pressed state) and redraw only what changed.

**Loading:** show skeleton bars with a moving shine in the place of data that is coming (not "Loading..." text),
keep the last known data, and show a clear offline state when the PC is gone.

**Images:** full-bleed, cover-cropped (never letterboxed); decode JPEG with JPEGDEC at the largest scale that
still covers the area, upscale smaller images. Baseline JPEG only (no progressive).

## Launcher icons

88×88, same rounded square for every app (radius ≈ 24%), **one flat background color**, one simple white symbol,
symmetric. The launcher draws them floating with a soft drop shadow and the name below. Generate with
`tools/make_icon.py` or draw your own and convert with `write_icn()`.

## NEXDECK HUB (Windows)

Concrete grays (`Theme`): side `#1B1B1A`, back `#242423`, card `#2E2E2C`, border `#3C3C3A`, text `#ECEBE8`,
muted `#A3A19C`, accent (primary button) `#E2E0DA` with dark text. Rounded cards (12), 700 px content column,
section titles 19 pt semibold.

Write little: no tutorials in the main flow. Put reference text ("where does this come from", "how to")
in a collapsed `Expander` with the info icon. Prefer controls that show state (toggles, segmented choices) over
explanations.
