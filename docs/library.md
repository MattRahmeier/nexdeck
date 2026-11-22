# SquareBoard library reference

`firmware/libraries/SquareBoard/src` — pass `--libraries firmware/libraries` to arduino-cli (done by `build.sh`).

| Header | Contents |
|---|---|
| `SquareBoard.h` | board bring-up: display, back buffer, flush, backlight, touch, SD |
| `sq_gfx.h` | `Canvas` drawing (portable C++, also compiles on a PC) |
| `sq_fonts.h` | bitmap fonts (`FONT_*`) |
| `SquareLauncher.h` | NEXDECK launcher helpers (boot partition, return to launcher, backlight hold) |
| `SquareLink.h` | PC protocol helpers (`sqlink::`): checksum, blocks, SD commands |

## `sq::` (SquareBoard.h)

```cpp
bool sq::begin();                 // display on (backlight 0), back buffer in PSRAM, touch I2C
Canvas& sq::canvas();             // draw here...
uint16_t* sq::backBuffer();       // ...or write RGB565 pixels directly (480×480, row-major)
void sq::flush(const Rect& r);    // copy a rectangle of the back buffer to the screen
void sq::flushAll();              // whole screen (~8 ms)
void sq::backlight(uint8_t level);        // 0..255
void sq::fade(int from, int to, int ms);  // backlight fade (blocking)
bool sq::touchPoll(SqTouch& t);   // true when the GT911 had a new frame; t.n points in t.p[] (id, x, y)
bool sq::sdBegin();               // mount the microSD (20→1 MHz); call after begin()
```

Typical loop: poll touch every ~15 ms, update state, redraw only what changed, `flush` those rectangles.

## `Canvas` (sq_gfx.h)

All coordinates are local to the current `View` (`setView({x, y, w, h, rot180})`); `rot180` draws upside down
(useful for players sitting across a table). Colors are RGB565: `rgb(r, g, b)` is `constexpr`.

```cpp
void clear(uint16_t c);
void fillRect(int x, int y, int w, int h, uint16_t c);
void frameRect(int x, int y, int w, int h, int t, uint16_t c);
void dimRect(int x, int y, int w, int h, uint8_t keep);                 // darken (keep 0..255)
void tintRect(int x, int y, int w, int h, uint16_t c, uint8_t alpha);   // translucent veil
void fillGradient(int x, int y, int w, int h, uint16_t c0, uint16_t c1); // diagonal (prefer flat colors)
void fillRound(int x, int y, int w, int h, int r, uint16_t c);          // rounded rect, anti-aliased
void fillRound(int x, int y, int w, int h, int r, uint16_t c0, uint16_t c1, bool diag = false);
void roundBox(float x, float y, float w, float h, float r, float thick, uint16_t c);  // outline
void fillCircle(float cx, float cy, float r, uint16_t c);
void ring(float cx, float cy, float r, float thick, uint16_t c);
void arc(float cx, float cy, float r, float thick, float a0, float a1, uint16_t c);   // radians, 0 = right, clockwise
void line(float x0, float y0, float x1, float y1, float thick, uint16_t c);           // round caps
void triangle(float x0, float y0, float x1, float y1, float x2, float y2, uint16_t c);
void blit(int x, int y, int w, int h, const uint16_t* px, const uint8_t* alpha = nullptr);  // RGB565 image (+ alpha)
int  textWidth(const Font& f, const char* s) const;                       // UTF-8
int  text(const Font& f, int x, int y, const char* s, uint16_t c, Align a = LEFT);  // y = top of line
void textCentered(const Font& f, int cx, int cy, const char* s, uint16_t c);         // centered on the glyphs' ink
void setSlant(float s);   // italic drawn at runtime for any font: 0 = upright, 0.2 = typical italic (saves flash)
void glyphCentered(const Font& f, uint32_t cp, int cx, int cy, uint16_t c);
uint16_t* fb;                                                             // the framebuffer itself
```

Helpers: `blend565(bg, fg, alpha)`, `dim565(c, keep)`, `utf8Next(const char*&)`, `Rect::contains(x, y)`,
`SCREEN_W`/`SCREEN_H` (480).

Shapes are drawn with signed-distance functions, so edges are smooth at any size. Everything is software
rendering into PSRAM: a full-screen fill is ~2 ms, a complex screen 10–20 ms; cache static backgrounds in a
buffer (`memcpy`) when something redraws at high frequency (e.g. a dial dragged with the finger).

## Fonts (sq_fonts.h)

Barlow, 4-bit alpha, with ASCII + Latin accents + typographic punctuation (digit fonts: digits only).

| Font | Style / size | Use |
|---|---|---|
| `FONT_BIG` | condensed 64 | big titles |
| `FONT_TITLE` | semibold 36 | screen titles, rich text titles |
| `FONT_MED` | semibold 30 | page titles, buttons |
| `FONT_COND` | condensed 28 | numbers, short labels |
| `FONT_TXT` / `FONT_TXT_B` / `FONT_TXT_I` | 24 regular / bold / italic | rich text (italic is synthesized) |
| `FONT_SMALL` | medium 22 | secondary text |
| `FONT_HEAD` | semibold 21 | headlines |
| `FONT_BODY` | medium 16 | body text |
| `FONT_CAP` | semibold 13 | captions |
| `FONT_MINI` | semibold 11 | tiny labels |
| `FONT_CLOCK`, `FONT_WATCH`, `FONT_TEMP`, `FONT_LIFE`, `FONT_HUGE`, `FONT_TINY` | condensed digits 70/150/50/108/210/22 | clocks, counters |

Text from the PC must only use characters the fonts have: NEXDECK HUB's `Txt.Clean(s, maxBytes)` maps accents
it can and drops the rest.

### Custom fonts

Fonts are generated by `tools/gen_fonts.py`. For app-specific fonts (icons, other typefaces) write your own
script that imports it, sets `FONTS` to your TTF folder and calls
`emit([render(spec), ...], "path/my_fonts.cpp", "my_fonts.h")`; declare the fonts in your header
(`extern const Font FONT_X;`). Spec: `(C name, ttf file, size px, characters[, "italic"])`, or
`("NAME", file, ("fit", px), chars)` to scale each glyph to the same box (icon fonts).

## `SquareLauncher.h`

```cpp
bool sqLauncherAvailable();   // running from app1 (installed by the launcher)? false when flashed directly over USB
void sqBootLauncherNext();    // call in setup(): next power-on opens the NEXDECK welcome screen
void sqStayInApp();           // before a deep sleep that must wake up in this app
void sqHoldBacklightOff();    // keep the backlight off through a restart (sq::begin releases it)
bool sqReturnToLauncher();    // back to the launcher (does not return on success)
```

## `SquareLink.h` (namespace `sqlink`)

```cpp
bool checkLine(char* line);   // verifies and strips "\t~xxxx" (sum16 of the bytes, hex)
bool readExact(uint8_t* dst, size_t n, uint32_t ms = 400);
void drain();
template <F> bool receiveChunks(uint32_t len, F sink);  // 2048-byte blocks + sum, answers K/R; sink(data, n) → bool
void listApps();                                         // prints EV APPS name,bytes;...
void receiveFile(const char* path, uint32_t len);        // FPUT: OK, blocks, DONE (writes path.tmp then renames)
template <F> bool common(char* s, bool sdOk, F beforeLeave);  // handles LS, FPUT, RM, OPENAPP; true if handled
```

Minimal line handler (see `templates/hello_pc`):

```cpp
void handleLine(char* s) {
  if (!sqlink::checkLine(s)) { Serial.println("E"); return; }
  if (sqlink::common(s, sdOk, [] { sq::fade(255, 0, 200); SD.end(); })) return;
  if (!strcmp(s, "HI")) { Serial.println("EV\tHELLO\tmy_app"); Serial.println("K"); return; }
  // ... your commands ...
  Serial.println("K");
}
```
