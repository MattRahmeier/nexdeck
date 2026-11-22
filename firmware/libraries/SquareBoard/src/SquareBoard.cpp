#include "SquareBoard.h"
#include <Arduino_GFX_Library.h>
#include <SD.h>
#include <SPI.h>
#include <Wire.h>
#include <esp_cache.h>
#include <driver/gpio.h>

#define PIN_BACKLIGHT 38
#define PIN_TOUCH_SDA 19
#define PIN_TOUCH_SCL 45
#define PIN_SD_CS 42
#define PIN_SD_MISO 41
#define PIN_SD_MOSI 47
#define PIN_SD_SCK 48

namespace sq {
namespace {

// criados em begin(), não na inicialização estática: há sketches que incluem a biblioteca
// (pelo desenho) sem usar esta parte
Arduino_DataBus* panelBus = nullptr;
Arduino_ESP32RGBPanel* rgbPanel = nullptr;
Arduino_RGB_Display* display = nullptr;
uint16_t* back = nullptr;
Canvas* cv = nullptr;
uint8_t gtAddr = 0x5D;
SPIClass sdSpi(FSPI);

bool gtRead(uint16_t reg, uint8_t* buf, size_t n) {
  Wire.beginTransmission(gtAddr);
  Wire.write(reg >> 8);
  Wire.write(reg & 0xFF);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(gtAddr, (uint8_t)n) != n) return false;
  for (size_t i = 0; i < n; i++) buf[i] = Wire.read();
  return true;
}

void gtWrite(uint16_t reg, uint8_t v) {
  Wire.beginTransmission(gtAddr);
  Wire.write(reg >> 8);
  Wire.write(reg & 0xFF);
  Wire.write(v);
  Wire.endTransmission();
}

}  // namespace

bool begin() {
  ledcAttach(PIN_BACKLIGHT, 5000, 8);
  backlight(0);
  gpio_hold_dis((gpio_num_t)PIN_BACKLIGHT);  // o app anterior prendeu a luz apagada durante o reinício
  panelBus = new Arduino_SWSPI(GFX_NOT_DEFINED, 39, 48, 47, GFX_NOT_DEFINED);
  rgbPanel = new Arduino_ESP32RGBPanel(18, 17, 16, 21, 11, 12, 13, 14, 0, 8, 20, 3, 46, 9, 10, 4, 5, 6, 7, 15,
                                       1, 10, 8, 50, 1, 10, 8, 20, 0, 12000000, false, 0, 0, SCREEN_W * 10);
  display = new Arduino_RGB_Display(SCREEN_W, SCREEN_H, rgbPanel, 0, true, panelBus, GFX_NOT_DEFINED,
                                    st7701_type9_init_operations, sizeof(st7701_type9_init_operations));
  if (!display->begin()) return false;
  back = (uint16_t*)ps_malloc(SCREEN_W * SCREEN_H * sizeof(uint16_t));
  if (!back) return false;
  cv = new Canvas(back);
  Wire.begin(PIN_TOUCH_SDA, PIN_TOUCH_SCL, 400000);
  uint8_t id[4];
  for (uint8_t a : {0x5D, 0x14}) {
    gtAddr = a;
    if (gtRead(0x8140, id, 4)) break;
  }
  return true;
}

Canvas& canvas() { return *cv; }
uint16_t* backBuffer() { return back; }

void flush(const Rect& r) {
  uint16_t* fb = display->getFramebuffer();
  for (int y = r.y; y < r.y + r.h; y++)
    memcpy(fb + y * SCREEN_W + r.x, back + y * SCREEN_W + r.x, r.w * sizeof(uint16_t));
  uint16_t* start = fb + r.y * SCREEN_W + r.x;
  size_t bytes = ((r.h - 1) * SCREEN_W + r.w) * sizeof(uint16_t);
  esp_cache_msync(start, bytes, ESP_CACHE_MSYNC_FLAG_DIR_C2M | ESP_CACHE_MSYNC_FLAG_UNALIGNED);
}

void flushAll() { flush({0, 0, SCREEN_W, SCREEN_H}); }

void backlight(uint8_t level) { ledcWrite(PIN_BACKLIGHT, level); }

void fade(int from, int to, int ms) {
  const int steps = 20;
  for (int i = 1; i <= steps; i++) {
    backlight((uint8_t)(from + (to - from) * i / steps));
    delay(ms / steps);
  }
}

bool touchPoll(SqTouch& touch) {
  uint8_t status;
  if (!gtRead(0x814E, &status, 1) || !(status & 0x80)) return false;
  uint8_t n = status & 0x0F;
  if (n > 5) n = 5;
  uint8_t raw[5 * 8];
  touch.n = 0;
  if (n && gtRead(0x814F, raw, n * 8)) {
    for (uint8_t i = 0; i < n; i++) {
      const uint8_t* p = raw + i * 8;
      int x = p[1] | (p[2] << 8), y = p[3] | (p[4] << 8);
      if (x < 0 || y < 0 || x >= SCREEN_W || y >= SCREEN_H) continue;
      touch.p[touch.n++] = {p[0], (int16_t)x, (int16_t)y};
    }
  }
  gtWrite(0x814E, 0);
  return true;
}

bool sdBegin() {
  sdSpi.begin(PIN_SD_SCK, PIN_SD_MISO, PIN_SD_MOSI, PIN_SD_CS);
  static const uint32_t SPEEDS[] = {20000000, 10000000, 4000000, 1000000};
  for (uint32_t hz : SPEEDS) {
    if (SD.begin(PIN_SD_CS, sdSpi, hz)) return true;
    SD.end();
  }
  return false;
}

}  // namespace sq
