# Hardware: Guition ESP32-4848S040 (JC4848W540C)

| Part | Details |
|---|---|
| MCU | ESP32-S3-WROOM-1 **N16R8** — 16 MB flash, 8 MB PSRAM (OPI) |
| Display | 4.0" IPS 480×480, ST7701S, 16-bit parallel RGB |
| Touch | GT911 capacitive, I2C address 0x5D (or 0x14), up to 5 points |
| USB | USB-C → CH340C (UART0); DTR/RTS wired to auto-reset/boot |
| Storage | microSD slot (SPI) |
| Extras | battery connector MX1.25 2P (IP5306 charger, no I2C), speaker MX1.25 2P (NS4168 I2S amp), MX1.25 4P (5V/TX/RX/GND) |

## Pins

| Function | GPIO |
|---|---|
| RGB data / sync | 18 17 16 21 11 12 13 14 0 8 20 3 46 9 10 4 5 6 7 15 (see `SquareBoard.cpp`) |
| ST7701 init SPI | CS 39, SCK 48, SDA 47 |
| Backlight (PWM) | 38 |
| Touch GT911 | SDA 19, SCL 45 (INT/RST not connected) |
| microSD | CS 42, MISO 41, MOSI 47, SCK 48 (shares SCK/MOSI with the display init SPI: call `sq::sdBegin()` after `sq::begin()`) |
| Relay / I2S audio | GPIO 1, 2, 40 (move the 0 Ω resistors R25–R27 to R21–R23 to use audio) |

## Quirks worth knowing

- **Opening the serial port can reset the board** (DTR/RTS). Open it with DTR and RTS low (the HUB and
  `push.ps1` do). A short RTS pulse with DTR low is a clean reset.
- **The CH340 drops bytes at 921600** under load: the launcher protocol retries; app traffic uses 460800 with a
  checksum per line and per block.
- **Flash/NVS writes stall the RGB panel** for 9–35 ms (the Arduino core is built without
  `CONFIG_SPIRAM_XIP_FROM_PSRAM` / `CONFIG_LCD_RGB_ISR_IRAM_SAFE`): prefer the SD card for frequent saves and
  save when nothing animates.
- The panel uses a bounce buffer (`SCREEN_W * 10` px) so DMA doesn't fight the CPU for PSRAM; keep it.
- Raw GT911 coordinates already match screen rotation 0.
- Battery: the IP5306 cuts the output under ~45 mA after ~32 s; a deep-sleeping board on battery may switch off.
- The touch controller has no interrupt pin wired: poll it every ~15 ms (`sq::touchPoll`).
