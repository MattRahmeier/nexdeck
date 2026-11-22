# Architecture

```
 ┌───────────────────────── board (ESP32-S3) ─────────────────────────┐        ┌──────────── PC (Windows) ────────────┐
 │ flash app0: NEXDECK launcher   flash app1: the app being used        │  USB   │ NEXDECK HUB (tray)                    │
 │ microSD: /apps/<id>.bin + <id>.icn, app data (/<anything>)           │◀──────▶│  core: find board, list/install/open │
 │ SquareBoard library: display, touch, SD, Canvas, fonts, link        │ serial │  modules: PC side of each app        │
 └──────────────────────────────────────────────────────────────────────┘        └──────────────────────────────────────┘
```

## Partitions and boot flow

Partition scheme `app3M_fat9M_16MB`: two 3 MB app slots (`app0`, `app1`) and a FAT area.

- **`app0` = NEXDECK launcher**, flashed once over USB. It lists `/apps/*.bin` on the SD card.
- **`app1` = the installed app.** Tapping an icon copies that `.bin` into `app1` with the ESP-IDF `Update`
  API (only when the file changed: name + size are remembered in NVS), sets `app1` as the boot partition and
  restarts.
- Every app calls **`sqBootLauncherNext()`** in `setup()`: it sets `app0` as the boot partition again, so the next
  power-on (or reset button) lands in the launcher. The restart *into* the app still goes to `app1` because the
  launcher set it just before restarting.
- **`sqReturnToLauncher()`** sets `app0` and restarts (used for "hold to go back").
- Apps that deep-sleep and must wake up in themselves call **`sqStayInApp()`** before sleeping.
- **Welcome screen** only on power-on/external reset (`esp_reset_reason()`); coming back from an app goes
  straight to the grid.
- **Open an app by name:** an app can store `next=<id>` in NVS namespace `launcher` and return to the launcher;
  the launcher opens that app immediately (`OPENAPP` in the protocol does exactly this).
- **No flicker between apps:** before restarting, the backlight pin (GPIO 38) is held low with `gpio_hold_en`;
  `sq::begin()` releases it after the new app has drawn its first frame.

## Apps

An app is a normal Arduino sketch that includes `SquareBoard.h`. Recommended structure:

```
my_app/
  my_app.ino        hardware glue: serial, SD, JPEG decoding, touch polling → calls into src/
  src/my_ui.h/.cpp  portable C++: state, protocol parsing, drawing on a Canvas (also runs in a PC simulator)
  my_app.icn        launcher icon (tools/make_icon.py)
```

Draw into the back buffer (`sq::canvas()`), then copy only what changed (`sq::flush(rect)`) — full-screen
copies take ~8 ms. Keep big buffers in PSRAM (`ps_malloc`).

## The PC side: NEXDECK HUB

The HUB is a Windows tray app. Its **core** (`hub/NexDeckHub.Core.cs`):

1. finds the board (CH340 / Espressif USB ids) and tries `HI` at **460800 baud**; an app answers
   `EV HELLO <name> ...`. If nothing answers it tries the launcher at **921600 baud** (`LS`).
2. lists the apps on the card (`LS` → `EV APPS` in apps, `LS` lines in the launcher), installs packages
   (`FPUT`/`PUT`), opens apps (`OPENAPP`/`RUN`);
3. hands the connection to the **module** whose `Hello` matches (`HubApp.OnConnect/Loop/OnEvent`), pings every 5 s
   and sends queued blocks;
4. shows a window (`hub/NexDeckHub.Ui.cs`): apps home, HUB settings, and one `AppPage` per module with settings.

A **watcher** process (`NEXDECKHub.exe --guard`) restarts the HUB if it crashes and, after the user quits, keeps
listening for `EV WAKE` from the board to reopen it.

Files: config and log in `%APPDATA%\NEXDECK HUB` (`config.json`, `log.txt`); installed package icons in
`%APPDATA%\NEXDECK HUB\icones`.

## Data that must survive without the PC

Put it on the SD card (the HUB can write any path with `FPUT`, then send your own "reload" line). Example: a
notice-board app keeps `/myapp/items.txt` + images on the card, so it keeps working when the PC is off.
