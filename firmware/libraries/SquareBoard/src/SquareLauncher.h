#pragma once
// NEXLauncher (o lançador de apps do NEXDECK): o menu fica fixo na partição "app0" e o app escolhido
// é instalado do cartão SD na "app1". Um app volta ao menu chamando sqReturnToLauncher().
// Toda vez que a placa liga, abre a tela de boas-vindas do NEXDECK (no NEXLauncher): o lançador marca a app1 só
// para o reinício que abre o app, e o app, ao começar, chama sqBootLauncherNext() para que o
// próximo boot (desligar e ligar, botão reset) volte ao lançador.
// Só depende do ESP-IDF, então qualquer sketch pode incluir este arquivo.
#include <esp_ota_ops.h>
#include <esp_system.h>
#include <driver/gpio.h>
#include <string.h>

// O app está rodando como app instalado pelo lançador (partição app1)?
// Gravado direto pela USB ele roda na app0, no lugar do lançador, e não há menu para onde voltar.
inline bool sqLauncherAvailable() {
  const esp_partition_t* run = esp_ota_get_running_partition();
  return run && strcmp(run->label, "app1") == 0;
}

// grava a partição de boot só se ela mudar (cada gravação apaga um setor da flash)
inline bool sqSetBoot(esp_partition_subtype_t sub) {
  const esp_partition_t* p = esp_partition_find_first(ESP_PARTITION_TYPE_APP, sub, nullptr);
  if (!p) return false;
  const esp_partition_t* cur = esp_ota_get_boot_partition();
  if (cur && cur->address == p->address) return true;
  return esp_ota_set_boot_partition(p) == ESP_OK;
}

// Chamar no começo do app: o próximo boot abre o NEXLauncher.
inline void sqBootLauncherNext() {
  if (sqLauncherAvailable()) sqSetBoot(ESP_PARTITION_SUBTYPE_APP_OTA_0);
}

// Antes de um sono profundo que deve acordar no próprio app (o despertar é um boot).
inline void sqStayInApp() {
  if (sqLauncherAvailable()) sqSetBoot(ESP_PARTITION_SUBTYPE_APP_OTA_1);
}

// Prende a luz da tela apagada (pino 38 em 0) durante o reinício para outro app: sem isso o pino
// fica solto no boot e a tela pisca com lixo. O próximo app solta no sq::begin() (gpio_hold_dis).
// Chamar depois de apagar a luz (fade até 0).
inline void sqHoldBacklightOff() {
  gpio_set_level(GPIO_NUM_38, 0);
  gpio_hold_en(GPIO_NUM_38);
}

// Marca o lançador para o próximo boot e reinicia. Não retorna se der certo.
inline bool sqReturnToLauncher() {
  if (!sqSetBoot(ESP_PARTITION_SUBTYPE_APP_OTA_0)) return false;
  sqHoldBacklightOff();
  esp_restart();
  return true;
}
