// NEXLauncher — lançador de apps do NEXDECK. Fica gravado na partição app0 (pela USB); toda vez
// que a placa liga abre a tela de boas-vindas e depois mostra os arquivos .bin da pasta /apps
// do cartão SD. Tocar num app grava ele na partição app1
// (só na primeira vez ou se o arquivo mudou) e reinicia nele. O app volta para cá com
// sqReturnToLauncher() (SquareLauncher.h).
//
// Pela USB (921600 baud) dá para mandar arquivos para o cartão sem tirá-lo da placa:
//   PUT <caminho> <bytes>  → responde OK, recebe blocos de 4096 bytes (K após cada um), DONE no fim
//   LS                     → lista /apps
//   RUN <nome>             → instala (se precisar) e abre o app, como um toque
//   RM <caminho>           → apaga um arquivo do cartão (OK ou ERR)
//   LSF <pasta> <.ext>     → "nome bytes crc32" por linha, END no fim (o HUB acha módulos .dll e ícones .icn)
//   GET <caminho>          → OK <bytes>, blocos placa→PC de 2048 + soma (o PC responde K ou R), DONE
// O script tools/push.ps1 faz isso do PC.
#include <Arduino.h>
#include <Preferences.h>
#include <SD.h>
#include <Update.h>
#include <esp_ota_ops.h>
#include <math.h>
#include <SquareBoard.h>
#include <SquareLauncher.h>
#include <SquareLink.h>

namespace {

constexpr int MAX_APPS = 9;
// paleta ardósia do NEXDECK (#0F172A #334155 #64748B #94A3B8 #E2E8F0 #F8FAFC)
constexpr uint16_t PANEL = rgb(15, 23, 42), TILE = rgb(30, 41, 59), LINE = rgb(51, 65, 85);
constexpr uint16_t GREY = rgb(148, 163, 184), FAINT = rgb(100, 116, 139);
constexpr uint16_t SOFT = rgb(226, 232, 240), WHITE = rgb(248, 250, 252);
constexpr uint16_t BAD = rgb(248, 113, 113), GOOD = rgb(74, 222, 128);

struct AppFile {
  char name[40];   // nome do arquivo sem ".bin"
  char label[40];  // "my_app" → "My App"
  uint32_t size;
  uint8_t* icon;   // /apps/<nome>.icn: RGB565 + alfa por pixel (ICON_PX × ICON_PX), ou nullptr
};

constexpr int ICON_PX = 88;

AppFile apps[MAX_APPS];
int appCount = 0;
bool sdOk = false;
char installed[40] = "";
uint32_t installedSize = 0;
int pressed = -1;
SqTouch touch;
Preferences prefs;

// cores dos ícones sem .icn: tons médios
uint16_t tileColor(const char* s) {
  static const uint16_t C[] = {rgb(214, 96, 77), rgb(213, 140, 73), rgb(205, 178, 69), rgb(130, 190, 80),
                               rgb(70, 170, 110), rgb(64, 168, 160), rgb(66, 160, 205), rgb(71, 85, 105)};
  uint32_t h = 2166136261u;
  for (; *s; s++) h = (h ^ (uint8_t)*s) * 16777619u;
  return C[h % (sizeof C / sizeof C[0])];
}

void makeLabel(const char* name, char* out, size_t n) {
  size_t j = 0;
  bool up = true;
  for (const char* p = name; *p && j + 1 < n; p++) {
    char ch = *p;
    if (ch == '_' || ch == '-') { out[j++] = ' '; up = true; continue; }
    if (up && ch >= 'a' && ch <= 'z') ch -= 32;
    up = false;
    out[j++] = ch;
  }
  out[j] = 0;
}

// ícone opcional ao lado do .bin (gerado por tools/make_icons.py)
uint8_t* loadIcon(const char* name) {
  char path[64];
  snprintf(path, sizeof path, "/apps/%s.icn", name);
  File f = SD.open(path, FILE_READ);
  if (!f) return nullptr;
  uint8_t hdr[6];
  uint8_t* px = nullptr;
  const size_t n = ICON_PX * ICON_PX * 3;
  if (f.read(hdr, 6) == 6 && memcmp(hdr, "ICN1", 4) == 0 && hdr[4] == ICON_PX && hdr[5] == ICON_PX) {
    px = (uint8_t*)ps_malloc(n);
    if (px && f.read(px, n) != (int)n) { free(px); px = nullptr; }
  }
  f.close();
  return px;
}

void drawIcon(const uint8_t* px, int x0, int y0) {
  uint16_t* fb = sq::backBuffer();
  for (int y = 0; y < ICON_PX; y++)
    for (int x = 0; x < ICON_PX; x++, px += 3) {
      uint8_t a = px[2];
      if (!a) continue;
      uint16_t& d = fb[(y0 + y) * SCREEN_W + x0 + x];
      d = blend565(d, px[0] | (px[1] << 8), a);
    }
}

void scanApps() {
  for (int i = 0; i < appCount; i++) free(apps[i].icon);
  appCount = 0;
  if (!sdOk) return;
  File dir = SD.open("/apps");
  if (!dir || !dir.isDirectory()) return;
  for (File f = dir.openNextFile(); f && appCount < MAX_APPS; f = dir.openNextFile()) {
    const char* n = f.name();
    size_t len = strlen(n);
    if (!f.isDirectory() && n[0] != '.' && len > 4 && len < 44 && strcasecmp(n + len - 4, ".bin") == 0) {
      AppFile& a = apps[appCount++];
      snprintf(a.name, sizeof a.name, "%.*s", (int)(len - 4), n);
      makeLabel(a.name, a.label, sizeof a.label);
      a.size = f.size();
      a.icon = nullptr;
    }
    f.close();
  }
  // ordem alfabética
  for (int i = 0; i < appCount; i++)
    for (int j = i + 1; j < appCount; j++)
      if (strcasecmp(apps[j].label, apps[i].label) < 0) { AppFile t = apps[i]; apps[i] = apps[j]; apps[j] = t; }
  for (int i = 0; i < appCount; i++) apps[i].icon = loadIcon(apps[i].name);
}

// grade 3 × 3: ícone de 88 px flutuando (sombra) com o nome embaixo; a área de toque é a célula
Rect tileRect(int i) { return {(int16_t)(14 + (i % 3) * 152), (int16_t)(84 + (i / 3) * 128), 148, 124}; }

// sombra suave embaixo do ícone (quadrado arredondado desfocado)
void softShadow(int x, int y, int s, int r, int blur, uint8_t maxA) {
  uint16_t* fb = sq::backBuffer();
  float c = s / 2.f, inner = c - r;
  for (int yy = y - blur; yy < y + s + blur; yy++) {
    if (yy < 0 || yy >= SCREEN_H) continue;
    for (int xx = x - blur; xx < x + s + blur; xx++) {
      if (xx < 0 || xx >= SCREEN_W) continue;
      float qx = fabsf(xx + 0.5f - x - c) - inner, qy = fabsf(yy + 0.5f - y - c) - inner;
      float ox = qx > 0 ? qx : 0, oy = qy > 0 ? qy : 0;
      float d = sqrtf(ox * ox + oy * oy) + fminf(fmaxf(qx, qy), 0.f) - r;  // < 0 dentro
      float t = d <= 0 ? 1 : 1 - d / blur;
      if (t <= 0) continue;
      uint16_t& px = fb[yy * SCREEN_W + xx];
      px = blend565(px, 0, (uint8_t)(maxA * t * t));
    }
  }
}

// marca do NEXDECK: três cartas empilhadas (s = largura da carta da frente)
void deckMark(Canvas& c, int cx, int cy, int s) {
  int h = s * 2 / 3, r = s / 7, d = s / 6;
  c.fillRound(cx - s / 2 + 2 * d, cy - h / 2 - 2 * d, s, h, r, LINE);
  c.fillRound(cx - s / 2 + d, cy - h / 2 - d, s, h, r, FAINT);
  c.fillRound(cx - s / 2, cy - h / 2, s, h, r, SOFT);
  c.fillRound(cx - s / 2 + s / 7, cy - h / 2 + h / 4, s * 2 / 5, h / 6, h / 12, PANEL);
  c.fillRound(cx - s / 2 + s / 7, cy - h / 2 + h / 2, s * 3 / 5, h / 8, h / 16, FAINT);
}

bool welcome = true;      // tela de boas-vindas (só ao ligar a placa; voltar de um app vem direto para cá)
uint32_t welcomeAt = 0;
constexpr uint32_t WELCOME_MS = 3500;

void drawWelcome() {
  Canvas& c = sq::canvas();
  c.setView({0, 0, SCREEN_W, SCREEN_H, false});
  c.clear(PANEL);
  deckMark(c, 228, 170, 120);
  c.text(FONT_BIG, 240, 262, "NEXDECK", WHITE, CENTER);
  c.text(FONT_SMALL, 240, 336, "Bem-vindo", SOFT, CENTER);
  c.text(FONT_SMALL, 240, 438, "Toque para começar", FAINT, CENTER);
  c.fillRound(216, 380, 48, 4, 2, LINE);
  sq::flushAll();
}

bool isInstalled(const AppFile& a) {
  if (strcmp(a.name, installed) || a.size != installedSize) return false;
  const esp_partition_t* p = esp_partition_find_first(ESP_PARTITION_TYPE_APP, ESP_PARTITION_SUBTYPE_APP_OTA_1, nullptr);
  esp_app_desc_t d;
  return p && esp_ota_get_partition_description(p, &d) == ESP_OK;
}

void drawMenu() {
  welcome = false;
  Canvas& c = sq::canvas();
  c.setView({0, 0, SCREEN_W, SCREEN_H, false});
  c.clear(PANEL);
  deckMark(c, 40, 37, 28);
  c.text(FONT_MED, 64, 18, "NEXLauncher", WHITE);
  char count[16];
  snprintf(count, sizeof count, appCount == 1 ? "1 app" : "%d apps", appCount);
  c.text(FONT_SMALL, 456, 24, count, FAINT, RIGHT);
  c.fillRect(24, 68, SCREEN_W - 48, 2, LINE);
  if (!sdOk) {
    c.textCentered(FONT_MED, 240, 220, "Sem cartão SD", SOFT);
    c.text(FONT_SMALL, 240, 256, "Coloque o cartão e reinicie a placa.", GREY, CENTER);
  } else if (!appCount) {
    c.textCentered(FONT_MED, 240, 220, "Nenhum app no cartão", SOFT);
    c.text(FONT_SMALL, 240, 256, "Copie arquivos .bin para a pasta /apps.", GREY, CENTER);
  }
  for (int i = 0; i < appCount; i++) {
    Rect r = tileRect(i);
    bool down = i == pressed;  // apertado: o ícone "encosta" (desce e a sombra diminui)
    int ix = r.x + (r.w - ICON_PX) / 2, iy = r.y + 6 + (down ? 3 : 0);
    softShadow(ix, iy + (down ? 3 : 8), ICON_PX, 22, down ? 8 : 16, down ? 120 : 160);
    if (apps[i].icon) {
      drawIcon(apps[i].icon, ix, iy);
    } else {  // sem .icn: quadrado arredondado colorido com as iniciais
      c.fillRound(ix, iy, ICON_PX, ICON_PX, 22, tileColor(apps[i].name));
      char ini[3] = {apps[i].label[0], 0, 0};
      const char* sp = strchr(apps[i].label, ' ');
      if (sp && sp[1]) ini[1] = sp[1];
      c.textCentered(FONT_MED, ix + ICON_PX / 2, iy + ICON_PX / 2, ini, WHITE);
    }
    if (down) c.dimRect(ix, iy, ICON_PX, ICON_PX, 200);
    c.text(FONT_SMALL, r.x + r.w / 2, r.y + 100, apps[i].label, SOFT, CENTER);
  }
  sq::flushAll();
}

void drawProgress(const char* title, const char* line, int pct, uint16_t lineCol = SOFT) {
  Canvas& c = sq::canvas();
  c.setView({0, 0, SCREEN_W, SCREEN_H, false});
  c.clear(PANEL);
  c.textCentered(FONT_MED, 240, 180, title, WHITE);
  c.text(FONT_SMALL, 240, 210, line, lineCol, CENTER);
  if (pct >= 0) {
    c.frameRect(60, 260, 360, 28, 2, LINE);
    c.fillRect(64, 264, 352 * pct / 100, 20, SOFT);
  }
  sq::flushAll();
}

void bootApp1() {
  const esp_partition_t* p = esp_partition_find_first(ESP_PARTITION_TYPE_APP, ESP_PARTITION_SUBTYPE_APP_OTA_1, nullptr);
  if (p && esp_ota_set_boot_partition(p) == ESP_OK) {
    sq::fade(255, 0, 200);
    sqHoldBacklightOff();
    esp_restart();
  }
}

// Grava o .bin na partição app1 e reinicia nele.
void launch(const AppFile& a) {
  Serial.printf("abrindo %s\n", a.name);
  if (isInstalled(a)) bootApp1();
  char path[64];
  snprintf(path, sizeof path, "/apps/%s.bin", a.name);
  File f = SD.open(path, FILE_READ);
  char title[64];
  snprintf(title, sizeof title, "Instalando %s", a.label);
  if (!f || !Update.begin(f.size())) {
    drawProgress(title, "Não deu para começar a instalação.", -1, BAD);
    delay(2500);
    drawMenu();
    return;
  }
  static uint8_t buf[4096];
  size_t done = 0, total = f.size();
  int lastPct = -1;
  while (done < total) {
    int n = f.read(buf, sizeof buf);
    if (n <= 0 || Update.write(buf, n) != (size_t)n) break;
    done += n;
    int pct = done * 100 / total;
    if (pct / 5 != lastPct / 5) { drawProgress(title, "Não desligue a placa.", pct); lastPct = pct; }
  }
  f.close();
  if (done != total || !Update.end(true)) {
    Serial.printf("falha na instalacao: %s\n", Update.errorString());
    Update.abort();
    drawProgress(title, "Arquivo inválido ou corrompido.", -1, BAD);
    delay(2500);
    drawMenu();
    return;
  }
  prefs.begin("launcher", false);
  prefs.putString("app", a.name);
  prefs.putUInt("size", a.size);
  prefs.end();
  drawProgress(title, "Pronto. Abrindo...", 100);
  bootApp1();
}

// ---------- recebimento de arquivos pela USB ----------
char cmd[96];
size_t cmdLen = 0;

void receiveFile(const char* path, uint32_t size) {
  char tmp[72];
  snprintf(tmp, sizeof tmp, "%s.tmp", path);
  if (strncmp(path, "/apps/", 6) == 0) SD.mkdir("/apps");
  {  // cria a pasta do arquivo (um nível), ex.: /fotos/a.jpg → /fotos
    char dir[64];
    snprintf(dir, sizeof dir, "%s", path);
    char* slash = strrchr(dir, '/');
    if (slash && slash != dir) { *slash = 0; SD.mkdir(dir); }
  }
  File f = SD.open(tmp, FILE_WRITE);
  if (!f) { Serial.println("ERR abrir"); return; }
  Serial.println("OK");
  char title[80];
  snprintf(title, sizeof title, "Recebendo %s", path);
  static uint8_t buf[4096];
  uint32_t got = 0;
  int lastPct = -1;
  while (got < size) {
    size_t want = size - got < sizeof buf ? size - got : sizeof buf, have = 0;
    uint32_t t0 = millis();
    while (have < want && millis() - t0 < 5000) {
      int n = Serial.read(buf + have, want - have);
      if (n > 0) { have += n; t0 = millis(); }
    }
    if (have < want || f.write(buf, have) != have) {
      f.close();
      SD.remove(tmp);
      // descarta o resto do bloco que ainda estiver chegando, para não virar "comando"
      for (uint32_t t = millis(); millis() - t < 300;) if (Serial.available()) { Serial.read(); t = millis(); }
      Serial.println("ERR dados");
      drawMenu();
      return;
    }
    got += have;
    // desenha antes de pedir o próximo bloco: copiar a tela inteira segura a CPU por alguns ms
    // e a UART perderia bytes que chegassem nesse meio tempo
    int pct = (int)((uint64_t)got * 100 / size);
    if (pct / 10 != lastPct / 10) { drawProgress(title, "pela USB", pct); lastPct = pct; }
    Serial.println("K");
  }
  f.close();
  SD.remove(path);
  SD.rename(tmp, path);
  Serial.printf("DONE %u\n", (unsigned)got);
  scanApps();
  drawMenu();
}

void handleCommand(char* line) {
  if (strncmp(line, "PUT ", 4) == 0) {
    char path[64];
    unsigned size = 0;
    if (sscanf(line + 4, "%63s %u", path, &size) == 2 && path[0] == '/' && sdOk) receiveFile(path, size);
    else Serial.println("ERR uso: PUT /apps/nome.bin bytes");
  } else if (strcmp(line, "LS") == 0) {
    for (int i = 0; i < appCount; i++)
      Serial.printf("%s %u%s\n", apps[i].name, (unsigned)apps[i].size, isInstalled(apps[i]) ? " (instalado)" : "");
    Serial.println("END");
  } else if (strncmp(line, "RM ", 3) == 0) {
    bool ok = sdOk && line[3] == '/' && SD.remove(line + 3);
    Serial.println(ok ? "OK" : "ERR nao apagou");
    if (ok) { scanApps(); if (!welcome) drawMenu(); }
  } else if (strncmp(line, "LSF ", 4) == 0) {
    char dir[48], ext[12] = "";
    if (sdOk && sscanf(line + 4, "%47s %11s", dir, ext) >= 1 && sqlink::safePath(dir)) {
      String out;
      sqlink::listFiles(dir, ext, out);
      for (int a = 0; a < (int)out.length();) {  // "n,b,c;n,b,c" → uma linha "n b c" por arquivo
        int e = out.indexOf(';', a);
        if (e < 0) e = out.length();
        String item = out.substring(a, e);
        item.replace(',', ' ');
        Serial.println(item);
        a = e + 1;
      }
    }
    Serial.println("END");
  } else if (strncmp(line, "GET ", 4) == 0) {
    if (sdOk) sqlink::sendFile(line + 4, "OK ");
    else Serial.println("ERR sem cartao");
  } else if (strncmp(line, "RUN ", 4) == 0) {
    for (int i = 0; i < appCount; i++)
      if (strcmp(apps[i].name, line + 4) == 0) { launch(apps[i]); return; }
    Serial.println("ERR app nao encontrado");
  }
}

void pollSerial() {
  while (Serial.available()) {
    char ch = Serial.read();
    if (ch == '\r') continue;
    if (ch == '\n') {
      cmd[cmdLen] = 0;
      if (cmdLen) handleCommand(cmd);
      cmdLen = 0;
    } else if (cmdLen + 1 < sizeof cmd) {
      cmd[cmdLen++] = ch;
    }
  }
}

int hit(int x, int y) {
  for (int i = 0; i < appCount; i++)
    if (tileRect(i).contains(x, y)) return i;
  return -1;
}

}  // namespace

void setup() {
  Serial.setRxBufferSize(8192);
  Serial.begin(921600);
  if (!sq::begin()) Serial.println("falha ao iniciar a tela");
  sdOk = sq::sdBegin();
  prefs.begin("launcher", true);
  prefs.getString("app", installed, sizeof installed);
  installedSize = prefs.getUInt("size", 0);
  char next[40] = "";
  prefs.getString("next", next, sizeof next);
  prefs.end();
  if (next[0]) { prefs.begin("launcher", false); prefs.remove("next"); prefs.end(); }
  scanApps();
  esp_reset_reason_t why = esp_reset_reason();
  welcome = why == ESP_RST_POWERON || why == ESP_RST_EXT || why == ESP_RST_BROWNOUT || why == ESP_RST_UNKNOWN;
  Serial.printf("lancador: cartao %s, %d apps, reset %d%s\n", sdOk ? "ok" : "ausente", appCount, (int)why, welcome ? " (boas-vindas)" : "");
  for (int i = 0; next[0] && i < appCount; i++)
    if (!strcmp(apps[i].name, next)) {
      drawProgress(apps[i].label, "Abrindo...", -1);
      sq::fade(0, 255, 150);
      launch(apps[i]);
    }
  if (welcome) drawWelcome();
  else drawMenu();
  welcomeAt = millis();
  sq::fade(0, 255, 250);
}

void loop() {
  static uint32_t lastPoll = 0;
  static int16_t lx = 0, ly = 0;
  static bool down = false;
  pollSerial();
  if (welcome && millis() - welcomeAt >= WELCOME_MS) { welcome = false; drawMenu(); }
  if (millis() - lastPoll >= 15) {
    lastPoll = millis();
    if (welcome) {  // boas-vindas: um toque pula direto para os apps
      if (sq::touchPoll(touch) && touch.n) {
        welcome = false;
        drawMenu();
        down = true;  // não abre o app que estiver embaixo do dedo
        pressed = -1;
      }
    } else if (sq::touchPoll(touch)) {
      if (touch.n && !down) {  // encostou
        down = true;
        lx = touch.p[0].x;
        ly = touch.p[0].y;
        pressed = hit(lx, ly);
        if (pressed >= 0) drawMenu();
      } else if (touch.n) {
        lx = touch.p[0].x;
        ly = touch.p[0].y;
      } else if (down) {  // soltou: abre se ainda estiver no mesmo app
        down = false;
        int was = pressed;
        pressed = -1;
        if (was >= 0) {
          drawMenu();
          if (hit(lx, ly) == was) launch(apps[was]);
        }
      }
    }
  }
  delay(1);
}
