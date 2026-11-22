#pragma once
#include <stdint.h>
#include "sq_fonts.h"

// Desenho portátil num framebuffer RGB565 (mesmo código no ESP32 e no simulador do PC).

constexpr int SCREEN_W = 480;
constexpr int SCREEN_H = 480;

constexpr uint16_t rgb(uint8_t r, uint8_t g, uint8_t b) {
  return (uint16_t)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
}
uint16_t blend565(uint16_t bg, uint16_t fg, uint8_t alpha);  // alpha 0..255
uint16_t dim565(uint16_t c, uint8_t keep);                    // keep 0..255

struct Rect {
  int16_t x, y, w, h;
  bool contains(int px, int py) const { return px >= x && py >= y && px < x + w && py < y + h; }
};

// Uma área retangular da tela com sistema de coordenadas local.
// rot180 = conteúdo virado para quem senta do outro lado da mesa.
struct View {
  int16_t x, y, w, h;
  bool rot180;
  void toScreen(int lx, int ly, int& sx, int& sy) const {
    if (rot180) { sx = x + w - 1 - lx; sy = y + h - 1 - ly; }
    else { sx = x + lx; sy = y + ly; }
  }
  void toLocal(int sx, int sy, int& lx, int& ly) const {
    if (rot180) { lx = x + w - 1 - sx; ly = y + h - 1 - sy; }
    else { lx = sx - x; ly = sy - y; }
  }
  bool containsScreen(int sx, int sy) const { return sx >= x && sy >= y && sx < x + w && sy < y + h; }
};

enum Align : uint8_t { LEFT, CENTER, RIGHT };

class Canvas {
 public:
  explicit Canvas(uint16_t* buf) : fb(buf) {}
  uint16_t* fb;

  void setView(const View& v) { view = v; }
  // text slant (italic drawn at runtime, any font): 0 = upright, 0.2 = typical italic
  void setSlant(float s) { slant = s; }
  const View& currentView() const { return view; }

  void clear(uint16_t c);
  void fillRect(int x, int y, int w, int h, uint16_t c);         // coordenadas locais
  void frameRect(int x, int y, int w, int h, int t, uint16_t c);
  void dimRect(int x, int y, int w, int h, uint8_t keep);
  void tintRect(int x, int y, int w, int h, uint16_t c, uint8_t alpha);  // véu translúcido
  void fillGradient(int x, int y, int w, int h, uint16_t c0, uint16_t c1);  // diagonal, c0 no canto superior esquerdo
  void glyphCentered(const Font& f, uint32_t cp, int cx, int cy, uint16_t c);
  int glyphHeight(const Font& f, uint32_t cp) const { const Glyph* g = find(f, cp); return g ? g->h : 0; }
  void fillCircle(float cx, float cy, float r, uint16_t c);
  void ring(float cx, float cy, float r, float thick, uint16_t c);
  // arco de anel entre a0 e a1 (radianos, 0 = direita, sentido horário na tela)
  void arc(float cx, float cy, float r, float thick, float a0, float a1, uint16_t c);
  void line(float x0, float y0, float x1, float y1, float thick, uint16_t c);
  void triangle(float x0, float y0, float x1, float y1, float x2, float y2, uint16_t c);
  void roundBox(float x, float y, float w, float h, float r, float thick, uint16_t c);  // contorno
  // retângulo arredondado preenchido; c0→c1 em degradê (vertical, ou diagonal se diag), bordas suaves
  void fillRound(int x, int y, int w, int h, int r, uint16_t c0, uint16_t c1, bool diag = false);
  void fillRound(int x, int y, int w, int h, int r, uint16_t c) { fillRound(x, y, w, h, r, c, c); }
  // imagem RGB565 (+ alfa opcional, 1 byte por pixel) em coordenadas locais, sem girar
  void blit(int x, int y, int w, int h, const uint16_t* px, const uint8_t* alpha = nullptr);

  int textWidth(const Font& f, const char* s) const;
  // y = topo da linha (ascent); devolve a largura desenhada
  int text(const Font& f, int x, int y, const char* s, uint16_t c, Align a = LEFT);
  // centraliza verticalmente pelo tamanho real dos glifos (bom para números)
  void textCentered(const Font& f, int cx, int cy, const char* s, uint16_t c);

 private:
  View view{0, 0, SCREEN_W, SCREEN_H, false};
  float slant = 0;
  void plot(int lx, int ly, uint16_t c, uint8_t a);
  template <typename F>
  void shade(float x0, float y0, float x1, float y1, uint16_t c, F sdf);
  const Glyph* find(const Font& f, uint32_t cp) const;
};

// decodifica um codepoint UTF-8 e avança o ponteiro
uint32_t utf8Next(const char*& s);
