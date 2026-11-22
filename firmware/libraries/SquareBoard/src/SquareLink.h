#pragma once
// Conversa com o NEXDECK HUB (app do PC) pela USB, comum aos apps do NEXDECK que falam com o PC.
// Linhas "<campos separados por tab>\t~<soma16 em hexa>"; resposta K (ok) ou E (soma errada).
// Blocos de dados (imagens, arquivos): até 2048 bytes + 2 bytes de soma, resposta K ou R (reenviar).
// Comandos de cartão que todo app atende (o HUB usa para listar e instalar apps):
//   LS → EV APPS nome,bytes;...   FPUT\t<caminho>\t<bytes> → OK, blocos, DONE   RM\t<caminho>
//   OPENAPP\t<nome> → volta ao NEXLauncher, que abre o app na hora
#include <Arduino.h>
#include <Preferences.h>
#include <SD.h>
#include <stdlib.h>
#include <string.h>
#include "SquareLauncher.h"

namespace sqlink {

constexpr int CHUNK = 2048;

// confere e tira o "\t~xxxx" do fim da linha
inline bool checkLine(char* line) {
  char* t = strrchr(line, '\t');
  if (!t || t[1] != '~') return false;
  unsigned want = (unsigned)strtoul(t + 2, nullptr, 16);
  *t = 0;
  unsigned sum = 0;
  for (const char* p = line; *p; p++) sum = (sum + (uint8_t)*p) & 0xFFFF;
  return sum == want;
}

// lê exatamente n bytes (desiste após ms sem nada chegar)
inline bool readExact(uint8_t* dst, size_t n, uint32_t ms = 400) {
  size_t have = 0;
  uint32_t t0 = millis();
  while (have < n && millis() - t0 < ms) {
    int k = Serial.read(dst + have, n - have);
    if (k > 0) { have += k; t0 = millis(); }
  }
  return have == n;
}

inline void drain() {
  for (uint32_t t = millis(); millis() - t < 120;)
    if (Serial.available()) { Serial.read(); t = millis(); }
}

// recebe len bytes em blocos com soma; cada bloco certo vai para sink(dados, n) (false = parar)
template <typename F>
bool receiveChunks(uint32_t len, F sink) {
  static uint8_t chunk[CHUNK + 2];
  uint32_t got = 0;
  int fails = 0;
  while (got < len) {
    size_t n = len - got < (uint32_t)CHUNK ? len - got : CHUNK;
    bool ok = readExact(chunk, n + 2);
    if (ok) {
      uint16_t sum = 0;
      for (size_t i = 0; i < n; i++) sum += chunk[i];
      ok = sum == (uint16_t)(chunk[n] | (chunk[n + 1] << 8));
    }
    if (!ok) {
      drain();
      if (++fails > 8) { Serial.println("ERR dados"); return false; }
      Serial.println("R");
      continue;
    }
    if (!sink(chunk, n)) { Serial.println("ERR gravar"); return false; }
    got += n;
    Serial.println("K");
  }
  return true;
}

inline void listApps() {
  String out = "EV\tAPPS\t";
  File dir = SD.open("/apps");
  bool first = true;
  if (dir && dir.isDirectory())
    for (File f = dir.openNextFile(); f; f = dir.openNextFile()) {
      String n = f.name();
      if (!f.isDirectory() && n[0] != '.' && n.length() > 4 && n.endsWith(".bin")) {
        if (!first) out += ";";
        out += n.substring(0, n.length() - 4) + "," + String((unsigned)f.size());
        first = false;
      }
      f.close();
    }
  Serial.println(out);
}

inline void receiveFile(const char* path, uint32_t len) {
  if (path[0] != '/' || strstr(path, "..")) { Serial.println("ERR caminho"); return; }
  {  // cria a pasta do arquivo (um nível), ex.: /apps/x.bin → /apps
    char dir[64];
    snprintf(dir, sizeof dir, "%s", path);
    char* slash = strrchr(dir, '/');
    if (slash && slash != dir) { *slash = 0; SD.mkdir(dir); }
  }
  String tmp = String(path) + ".tmp";
  File f = SD.open(tmp, FILE_WRITE);
  if (!f) { Serial.println("ERR abrir"); return; }
  Serial.println("OK");
  bool ok = receiveChunks(len, [&](const uint8_t* d, size_t n) { return f.write(d, n) == n; });
  f.close();
  if (!ok) { SD.remove(tmp); return; }
  SD.remove(path);
  SD.rename(tmp, path);
  Serial.println("DONE");
}

// LS, FPUT, RM e OPENAPP; devolve true se a linha era um deles (já respondida).
// beforeLeave: o app apaga a tela/fecha o cartão antes de voltar ao NEXLauncher
template <typename F>
bool common(char* s, bool sdOk, F beforeLeave) {
  if (!strcmp(s, "LS")) { if (sdOk) listApps(); Serial.println("K"); return true; }
  if (!strncmp(s, "FPUT\t", 5)) {
    char path[64];
    unsigned len = 0;
    if (sdOk && sscanf(s + 5, "%63[^\t]\t%u", path, &len) == 2 && len > 0) receiveFile(path, len);
    else Serial.println("ERR");
    return true;
  }
  if (!strncmp(s, "RM\t", 3)) {
    bool ok = sdOk && s[3] == '/' && !strstr(s + 3, "..") && SD.remove(s + 3);
    Serial.println(ok ? "K" : "ERR");
    return true;
  }
  if (!strncmp(s, "OPENAPP\t", 8)) {
    Serial.println("K");
    Serial.flush();
    if (!sqLauncherAvailable()) return true;
    Preferences p;
    p.begin("launcher", false);
    p.putString("next", s + 8);
    p.end();
    beforeLeave();
    sqReturnToLauncher();
    return true;
  }
  return false;
}

}  // namespace sqlink
