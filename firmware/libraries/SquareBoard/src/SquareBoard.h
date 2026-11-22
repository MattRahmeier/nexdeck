#pragma once
// Placa Guition ESP32-4848S040: tela 480×480 (ST7701 RGB), toque GT911, microSD e luz de fundo.
// Usado pelo NEXDECK (lançador) e pelos apps. API: docs/library.md.
#include <Arduino.h>
#include "sq_gfx.h"

struct SqPoint { uint8_t id; int16_t x, y; };
struct SqTouch { uint8_t n = 0; SqPoint p[5]; };

namespace sq {

// Liga a tela (apagada), reserva o buffer de desenho na PSRAM e inicia o toque.
bool begin();
// Buffer de desenho: desenhe nele e chame flush() para mostrar.
Canvas& canvas();
uint16_t* backBuffer();
void flush(const Rect& r);
void flushAll();

void backlight(uint8_t level);
void fade(int from, int to, int ms);

// Lê o GT911; devolve true quando havia um quadro novo (touch é atualizado).
bool touchPoll(SqTouch& touch);

// Monta o cartão (tenta 20, 10, 4 e 1 MHz). Chamar depois de begin(): divide SCK/MOSI com a tela.
bool sdBegin();

}  // namespace sq
