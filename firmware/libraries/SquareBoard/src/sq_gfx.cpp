#if defined(__GNUC__) && !defined(__clang__)
#pragma GCC optimize("O2")  // o Arduino compila com -Os; o desenho é o gargalo
#endif
#include "sq_gfx.h"
#include <math.h>
#include <string.h>

uint16_t blend565(uint16_t bg, uint16_t fg, uint8_t a) {
  if (a == 0) return bg;
  if (a >= 255) return fg;
  uint32_t inv = 255 - a;
  uint32_t r = (((fg >> 11) & 31) * a + ((bg >> 11) & 31) * inv) / 255;
  uint32_t g = (((fg >> 5) & 63) * a + ((bg >> 5) & 63) * inv) / 255;
  uint32_t b = ((fg & 31) * a + (bg & 31) * inv) / 255;
  return (uint16_t)((r << 11) | (g << 5) | b);
}

uint16_t dim565(uint16_t c, uint8_t keep) { return blend565(0, c, keep); }

uint32_t utf8Next(const char*& s) {
  uint8_t c = (uint8_t)*s++;
  if (c < 0x80) return c;
  int extra = (c >= 0xF0) ? 3 : (c >= 0xE0) ? 2 : 1;
  uint32_t cp = c & (0x3F >> extra);
  while (extra-- > 0 && (*s & 0xC0) == 0x80) cp = (cp << 6) | (*s++ & 0x3F);
  return cp;
}

void Canvas::plot(int lx, int ly, uint16_t c, uint8_t a) {
  if (lx < 0 || ly < 0 || lx >= view.w || ly >= view.h || a == 0) return;
  int sx, sy;
  view.toScreen(lx, ly, sx, sy);
  uint16_t& p = fb[sy * SCREEN_W + sx];
  p = blend565(p, c, a);
}

void Canvas::clear(uint16_t c) {
  for (int i = 0; i < SCREEN_W * SCREEN_H; i++) fb[i] = c;
}

void Canvas::fillRect(int x, int y, int w, int h, uint16_t c) {
  if (x < 0) { w += x; x = 0; }
  if (y < 0) { h += y; y = 0; }
  if (x + w > view.w) w = view.w - x;
  if (y + h > view.h) h = view.h - y;
  if (w <= 0 || h <= 0) return;
  // um retângulo continua retângulo após girar 180°
  int sx0, sy0, sx1, sy1;
  view.toScreen(x, y, sx0, sy0);
  view.toScreen(x + w - 1, y + h - 1, sx1, sy1);
  if (sx0 > sx1) { int t = sx0; sx0 = sx1; sx1 = t; }
  if (sy0 > sy1) { int t = sy0; sy0 = sy1; sy1 = t; }
  for (int yy = sy0; yy <= sy1; yy++) {
    uint16_t* row = fb + yy * SCREEN_W;
    for (int xx = sx0; xx <= sx1; xx++) row[xx] = c;
  }
}

void Canvas::frameRect(int x, int y, int w, int h, int t, uint16_t c) {
  fillRect(x, y, w, t, c);
  fillRect(x, y + h - t, w, t, c);
  fillRect(x, y + t, t, h - 2 * t, c);
  fillRect(x + w - t, y + t, t, h - 2 * t, c);
}

void Canvas::dimRect(int x, int y, int w, int h, uint8_t keep) {
  for (int yy = y; yy < y + h; yy++)
    for (int xx = x; xx < x + w; xx++) {
      if (xx < 0 || yy < 0 || xx >= view.w || yy >= view.h) continue;
      int sx, sy;
      view.toScreen(xx, yy, sx, sy);
      uint16_t& p = fb[sy * SCREEN_W + sx];
      p = dim565(p, keep);
    }
}

void Canvas::tintRect(int x, int y, int w, int h, uint16_t c, uint8_t alpha) {
  for (int yy = y; yy < y + h; yy++)
    for (int xx = x; xx < x + w; xx++) plot(xx, yy, c, alpha);
}

void Canvas::fillGradient(int x, int y, int w, int h, uint16_t c0, uint16_t c1) {
  // 32 faixas diagonais bastam para um degradê suave em RGB565
  uint16_t lut[33];
  for (int i = 0; i <= 32; i++) lut[i] = blend565(c0, c1, (uint8_t)(i * 255 / 32));
  int span = w + h - 2 > 0 ? w + h - 2 : 1;
  for (int yy = 0; yy < h; yy++)
    for (int xx = 0; xx < w; xx++) {
      int lx = x + xx, ly = y + yy;
      if (lx < 0 || ly < 0 || lx >= view.w || ly >= view.h) continue;
      int sx, sy;
      view.toScreen(lx, ly, sx, sy);
      fb[sy * SCREEN_W + sx] = lut[(xx + yy) * 32 / span];
    }
}

void Canvas::glyphCentered(const Font& f, uint32_t cp, int cx, int cy, uint16_t c) {
  const Glyph* g = find(f, cp);
  if (!g || !g->w) return;
  int x0 = cx - g->w / 2, y0 = cy - g->h / 2;
  const uint8_t* bits = f.bits + g->offset;
  int stride = (g->w + 1) / 2;
  for (int yy = 0; yy < g->h; yy++)
    for (int xx = 0; xx < g->w; xx++) {
      uint8_t b = bits[yy * stride + xx / 2];
      uint8_t v = (xx & 1) ? (b & 15) : (b >> 4);
      if (v) plot(x0 + xx, y0 + yy, c, v * 17);
    }
}

// Pinta pixels de uma caixa com cobertura derivada de uma função de distância
// assinada (negativa = dentro). Dá bordas suaves sem supersampling.
template <typename F>
void Canvas::shade(float x0, float y0, float x1, float y1, uint16_t c, F sdf) {
  int ix0 = (int)floorf(x0) - 1, iy0 = (int)floorf(y0) - 1;
  int ix1 = (int)ceilf(x1) + 1, iy1 = (int)ceilf(y1) + 1;
  for (int y = iy0; y <= iy1; y++)
    for (int x = ix0; x <= ix1; x++) {
      float d = sdf(x + 0.5f, y + 0.5f);
      float cov = 0.5f - d;
      if (cov <= 0) continue;
      plot(x, y, c, cov >= 1 ? 255 : (uint8_t)(cov * 255));
    }
}

void Canvas::fillCircle(float cx, float cy, float r, uint16_t c) {
  shade(cx - r, cy - r, cx + r, cy + r, c,
        [=](float x, float y) { return sqrtf((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r; });
}

void Canvas::ring(float cx, float cy, float r, float t, uint16_t c) {
  shade(cx - r - t, cy - r - t, cx + r + t, cy + r + t, c, [=](float x, float y) {
    return fabsf(sqrtf((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r) - t / 2;
  });
}

static float segDist(float px, float py, float ax, float ay, float bx, float by) {
  float dx = bx - ax, dy = by - ay;
  float l2 = dx * dx + dy * dy;
  float t = l2 > 0 ? ((px - ax) * dx + (py - ay) * dy) / l2 : 0;
  t = t < 0 ? 0 : (t > 1 ? 1 : t);
  float qx = ax + t * dx - px, qy = ay + t * dy - py;
  return sqrtf(qx * qx + qy * qy);
}

void Canvas::arc(float cx, float cy, float r, float t, float a0, float a1, uint16_t c) {
  const float TAU = 6.2831853f;
  float ex0 = cx + r * cosf(a0), ey0 = cy + r * sinf(a0);
  float ex1 = cx + r * cosf(a1), ey1 = cy + r * sinf(a1);
  const float span = fmodf(a1 - a0 + 2 * TAU, TAU);
  shade(cx - r - t, cy - r - t, cx + r + t, cy + r + t, c, [=](float x, float y) {
    // longe do anel (onde ficam também as pontas): nem calcula o ângulo, que é o caro
    float ring = fabsf(sqrtf((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r) - t / 2;
    if (ring > 1.f) return ring;
    float a = atan2f(y - cy, x - cx);
    float rel = fmodf(a - a0 + 2 * TAU, TAU);
    if (rel <= span) return ring;
    // fora do intervalo: pontas arredondadas
    float d0 = sqrtf((x - ex0) * (x - ex0) + (y - ey0) * (y - ey0));
    float d1 = sqrtf((x - ex1) * (x - ex1) + (y - ey1) * (y - ey1));
    return (d0 < d1 ? d0 : d1) - t / 2;
  });
}

void Canvas::line(float x0, float y0, float x1, float y1, float t, uint16_t c) {
  float minx = (x0 < x1 ? x0 : x1) - t, maxx = (x0 > x1 ? x0 : x1) + t;
  float miny = (y0 < y1 ? y0 : y1) - t, maxy = (y0 > y1 ? y0 : y1) + t;
  shade(minx, miny, maxx, maxy, c, [=](float x, float y) { return segDist(x, y, x0, y0, x1, y1) - t / 2; });
}

void Canvas::triangle(float x0, float y0, float x1, float y1, float x2, float y2, uint16_t c) {
  float minx = fminf(x0, fminf(x1, x2)), maxx = fmaxf(x0, fmaxf(x1, x2));
  float miny = fminf(y0, fminf(y1, y2)), maxy = fmaxf(y0, fmaxf(y1, y2));
  float area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
  float s = area < 0 ? -1.f : 1.f;
  shade(minx, miny, maxx, maxy, c, [=](float x, float y) {
    // distância assinada aproximada: máximo das distâncias às três arestas
    float d = -1e9f;
    const float px[3] = {x0, x1, x2}, py[3] = {y0, y1, y2};
    for (int i = 0; i < 3; i++) {
      float ax = px[i], ay = py[i], bx = px[(i + 1) % 3], by = py[(i + 1) % 3];
      float ex = bx - ax, ey = by - ay, len = sqrtf(ex * ex + ey * ey);
      float e = s * ((x - ax) * ey - (y - ay) * ex) / len;
      if (e > d) d = e;
    }
    return d;
  });
}

void Canvas::fillRound(int x, int y, int w, int h, int r, uint16_t c0, uint16_t c1, bool diag) {
  if (w <= 0 || h <= 0) return;
  if (r * 2 > w) r = w / 2;
  if (r * 2 > h) r = h / 2;
  uint16_t lut[33];
  for (int i = 0; i <= 32; i++) lut[i] = blend565(c0, c1, (uint8_t)(i * 255 / 32));
  int span = diag ? (w + h - 2 > 0 ? w + h - 2 : 1) : (h - 1 > 0 ? h - 1 : 1);
  for (int yy = 0; yy < h; yy++) {
    int ly = y + yy;
    if (ly < 0 || ly >= view.h) continue;
    float dy = yy < r ? r - (yy + 0.5f) : (yy >= h - r ? (yy + 0.5f) - (h - r) : -1.f);
    for (int xx = 0; xx < w; xx++) {
      int lx = x + xx;
      if (lx < 0 || lx >= view.w) continue;
      uint16_t c = lut[(diag ? xx + yy : yy) * 32 / span];
      uint8_t a = 255;
      if (dy >= 0) {
        float dx = xx < r ? r - (xx + 0.5f) : (xx >= w - r ? (xx + 0.5f) - (w - r) : -1.f);
        if (dx >= 0) {
          float cov = r + 0.5f - sqrtf(dx * dx + dy * dy);
          if (cov <= 0) continue;
          if (cov < 1) a = (uint8_t)(cov * 255);
        }
      }
      int sx, sy;
      view.toScreen(lx, ly, sx, sy);
      uint16_t& p = fb[sy * SCREEN_W + sx];
      p = a == 255 ? c : blend565(p, c, a);
    }
  }
}

void Canvas::blit(int x, int y, int w, int h, const uint16_t* px, const uint8_t* alpha) {
  for (int yy = 0; yy < h; yy++) {
    int ly = y + yy;
    if (ly < 0 || ly >= view.h) continue;
    for (int xx = 0; xx < w; xx++) {
      int lx = x + xx;
      if (lx < 0 || lx >= view.w) continue;
      uint8_t a = alpha ? alpha[yy * w + xx] : 255;
      if (!a) continue;
      int sx, sy;
      view.toScreen(lx, ly, sx, sy);
      uint16_t& p = fb[sy * SCREEN_W + sx];
      p = blend565(p, px[yy * w + xx], a);
    }
  }
}

void Canvas::roundBox(float x, float y, float w, float h, float r, float t, uint16_t c) {
  float cx = x + w / 2, cy = y + h / 2, hx = w / 2 - r, hy = h / 2 - r;
  shade(x - t, y - t, x + w + t, y + h + t, c, [=](float px, float py) {
    float qx = fabsf(px - cx) - hx, qy = fabsf(py - cy) - hy;
    float ox = qx > 0 ? qx : 0, oy = qy > 0 ? qy : 0;
    float d = sqrtf(ox * ox + oy * oy) + fminf(fmaxf(qx, qy), 0.f) - r;
    return fabsf(d) - t / 2;
  });
}

const Glyph* Canvas::find(const Font& f, uint32_t cp) const {
  int lo = 0, hi = f.count - 1;
  while (lo <= hi) {
    int mid = (lo + hi) / 2;
    if (f.glyphs[mid].cp == cp) return &f.glyphs[mid];
    if (f.glyphs[mid].cp < cp) lo = mid + 1; else hi = mid - 1;
  }
  return nullptr;
}

int Canvas::textWidth(const Font& f, const char* s) const {
  int w = 0;
  while (*s) {
    const Glyph* g = find(f, utf8Next(s));
    if (g) w += g->adv;
  }
  return w;
}

int Canvas::text(const Font& f, int x, int y, const char* s, uint16_t c, Align a) {
  int w = textWidth(f, s);
  if (a == CENTER) x -= w / 2;
  else if (a == RIGHT) x -= w;
  while (*s) {
    const Glyph* g = find(f, utf8Next(s));
    if (!g) continue;
    const uint8_t* bits = f.bits + g->offset;
    int stride = (g->w + 1) / 2;
    for (int yy = 0; yy < g->h; yy++) {
      // itálico: cada linha anda para a direita conforme a altura acima da linha de base
      float sx = slant * (f.ascent - (g->yoff + yy));
      int ix = (int)floorf(sx);
      uint32_t frac = (uint32_t)((sx - ix) * 256);
      for (int xx = 0; xx < g->w; xx++) {
        uint8_t b = bits[yy * stride + xx / 2];
        uint8_t v = (xx & 1) ? (b & 15) : (b >> 4);
        if (!v) continue;
        int px = x + g->xoff + xx + ix, py = y + g->yoff + yy;
        if (!frac) { plot(px, py, c, v * 17); continue; }
        uint32_t a = v * 17;
        plot(px, py, c, (uint8_t)(a * (256 - frac) >> 8));
        plot(px + 1, py, c, (uint8_t)(a * frac >> 8));
      }
    }
    x += g->adv;
  }
  return w;
}

void Canvas::textCentered(const Font& f, int cx, int cy, const char* s, uint16_t c) {
  int top = 10000, bottom = -10000;
  for (const char* p = s; *p;) {
    const Glyph* g = find(f, utf8Next(p));
    if (!g || g->h == 0) continue;
    if (g->yoff < top) top = g->yoff;
    if (g->yoff + g->h > bottom) bottom = g->yoff + g->h;
  }
  if (top > bottom) return;
  text(f, cx, cy - (top + bottom) / 2, s, c, CENTER);
}
