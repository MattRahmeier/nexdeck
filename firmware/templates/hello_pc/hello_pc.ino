// hello_pc — NEXDECK app template that talks to the PC (NEXDECK HUB).
// PC side: hub/NexDeckHub.Sample.cs (HelloPcApp). Protocol (docs/protocol.md):
//   board → PC:  EV\tHELLO\thello_pc   on start and when the PC sends HI
//                EV\tTAP\t<count>      when the screen is touched
//   PC → board:  MSG\t<text>  TIME\t<HH:mm:ss>  P (ping)   — every line ends with \t~<sum16 hex>
//   The board answers K (ok) or E (bad checksum) to every line.
// SquareLink also answers the SD commands every app should support (LS, FPUT, RM, OPENAPP), so the
// HUB can list and install apps while this one is open.
#include <Arduino.h>
#include <SD.h>
#include <SquareBoard.h>
#include <SquareLauncher.h>
#include <SquareLink.h>

namespace {

constexpr uint16_t BG = rgb(15, 23, 42), TILE = rgb(30, 41, 59), MUTED = rgb(148, 163, 184), TEXT = rgb(248, 250, 252), GOOD = rgb(74, 222, 128);

SqTouch touch;
bool sdOk = false, dirty = true;
char message[128] = "Abra o NEXDECK HUB no PC";
char clockText[16] = "--:--:--";
int taps = 0;
uint32_t pcAt = 0;  // last line from the PC

void draw() {
  Canvas& c = sq::canvas();
  c.setView({0, 0, SCREEN_W, SCREEN_H, false});
  c.clear(BG);
  bool online = pcAt && millis() - pcAt < 15000;
  c.fillCircle(440, 40, 6, online ? GOOD : MUTED);
  c.text(FONT_CAP, 424, 33, online ? "PC conectado" : "sem PC", MUTED, RIGHT);
  c.fillRound(40, 110, 400, 150, 24, TILE);
  c.textCentered(FONT_CLOCK, 240, 185, clockText, TEXT);
  c.text(FONT_MED, 240, 300, message, TEXT, CENTER);
  char buf[32];
  snprintf(buf, sizeof buf, "Toques: %d", taps);
  c.text(FONT_BODY, 240, 360, buf, MUTED, CENTER);
  sq::flushAll();
  dirty = false;
}

void hello() { Serial.println("EV\tHELLO\thello_pc"); }

char line[512];
size_t lineLen = 0;

void handleLine(char* s) {
  if (!sqlink::checkLine(s)) { Serial.println("E"); return; }  // removes the "\t~xxxx"
  pcAt = millis();
  if (sqlink::common(s, sdOk, [] { sq::fade(255, 0, 200); SD.end(); })) return;  // LS, FPUT, RM, OPENAPP
  if (!strcmp(s, "HI")) { hello(); Serial.println("K"); return; }
  if (!strncmp(s, "MSG\t", 4)) { snprintf(message, sizeof message, "%s", s + 4); dirty = true; }
  else if (!strncmp(s, "TIME\t", 5)) { snprintf(clockText, sizeof clockText, "%s", s + 5); dirty = true; }
  Serial.println("K");  // always answer, also to lines this app does not use (P = ping)
}

void pollSerial() {
  while (Serial.available()) {
    char ch = Serial.read();
    if (ch == '\r') continue;
    if (ch == '\n') {
      line[lineLen] = 0;
      if (lineLen) handleLine(line);
      lineLen = 0;
    } else if (lineLen + 1 < sizeof line) {
      line[lineLen++] = ch;
    }
  }
}

}  // namespace

void setup() {
  Serial.setRxBufferSize(16384);
  Serial.begin(460800);  // apps that talk to the HUB use 460800 baud
  sq::begin();
  sqBootLauncherNext();
  sdOk = sq::sdBegin();
  draw();
  sq::fade(0, 255, 250);
  hello();
}

void loop() {
  static bool down = false;
  static uint32_t downAt = 0, lastDraw = 0;
  pollSerial();
  if (sq::touchPoll(touch)) {
    if (touch.n && !down) { down = true; downAt = millis(); }
    else if (!touch.n && down) {
      down = false;
      taps++;
      Serial.printf("EV\tTAP\t%d\n", taps);  // events are plain lines (no checksum)
      dirty = true;
    }
  }
  if (down && millis() - downAt > 1000 && sqLauncherAvailable()) {
    sq::fade(255, 0, 200);
    SD.end();
    sqReturnToLauncher();
  }
  if (dirty || millis() - lastDraw > 5000) { lastDraw = millis(); draw(); }  // also refreshes "PC conectado"
  delay(5);
}
