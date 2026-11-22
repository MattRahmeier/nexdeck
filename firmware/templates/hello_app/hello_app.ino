// hello_app — minimal NEXDECK app template (no PC needed).
// Shows the essentials: start the board, draw with Canvas, read touch, return to the NEXDECK launcher.
// Copy this folder, rename it (folder name = .ino name = app id), build with firmware/build.sh and
// install it with tools/make_nexapp.py + NEXDECK HUB (or tools/push.ps1). See docs/getting-started.md.
#include <Arduino.h>
#include <SquareBoard.h>     // display, touch, SD, Canvas drawing, fonts
#include <SquareLauncher.h>  // NEXDECK launcher helpers

namespace {

// NEXDECK slate palette (see docs/design-guide.md)
constexpr uint16_t BG = rgb(15, 23, 42), TILE = rgb(30, 41, 59), RAISE = rgb(51, 65, 85);
constexpr uint16_t MUTED = rgb(148, 163, 184), TEXT = rgb(248, 250, 252), ACCENT = rgb(226, 232, 240);

SqTouch touch;
int taps = 0;
bool pressed = false;
constexpr Rect BUTTON{90, 300, 300, 72};

void draw() {
  Canvas& c = sq::canvas();
  c.setView({0, 0, SCREEN_W, SCREEN_H, false});
  c.clear(BG);
  c.text(FONT_TITLE, 240, 70, "Hello, NEXDECK", TEXT, CENTER);
  c.text(FONT_BODY, 240, 124, "Toque no botão. Segure a tela para voltar.", MUTED, CENTER);
  c.fillRound(140, 170, 200, 100, 22, TILE);
  char buf[16];
  snprintf(buf, sizeof buf, "%d", taps);
  c.textCentered(FONT_CLOCK, 240, 220, buf, TEXT);
  c.fillRound(BUTTON.x, BUTTON.y, BUTTON.w, BUTTON.h, 20, pressed ? RAISE : ACCENT);
  c.textCentered(FONT_MED, 240, BUTTON.y + BUTTON.h / 2, "Tocar", pressed ? TEXT : BG);
  sq::flushAll();  // copy the back buffer to the screen (or sq::flush(rect) for a part of it)
}

}  // namespace

void setup() {
  Serial.begin(115200);
  sq::begin();             // display + touch (also releases the backlight held by the launcher)
  sqBootLauncherNext();    // next power-on opens the NEXDECK welcome screen
  draw();
  sq::fade(0, 255, 250);   // fade the backlight in
}

void loop() {
  static bool down = false;
  static uint32_t downAt = 0;
  static int16_t x = 0, y = 0;
  if (sq::touchPoll(touch)) {  // true when the touch controller has new data
    if (touch.n && !down) {
      down = true;
      downAt = millis();
      x = touch.p[0].x;
      y = touch.p[0].y;
      pressed = BUTTON.contains(x, y);
      if (pressed) draw();
    } else if (!touch.n && down) {
      down = false;
      if (pressed && BUTTON.contains(x, y)) taps++;
      pressed = false;
      draw();
    } else if (touch.n) {
      x = touch.p[0].x;
      y = touch.p[0].y;
    }
  }
  // hold for 1 s anywhere: back to the launcher
  if (down && millis() - downAt > 1000 && sqLauncherAvailable()) {
    sq::fade(255, 0, 200);
    sqReturnToLauncher();
  }
  delay(15);
}
