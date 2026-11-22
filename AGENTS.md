# AGENTS.md — building NEXDECK apps with an AI coding agent

You are helping someone build an app for **NEXDECK**: Guition ESP32-4848S040 board (ESP32-S3, 480×480 RGB
touch screen, microSD) + **NEXDECK HUB** on Windows. Read this file first, then the doc for the task.

## What to read

| Task | Read |
|---|---|
| set up, build, flash, install | `docs/getting-started.md` |
| how the pieces fit (launcher, partitions, boot, HUB) | `docs/architecture.md` |
| drawing, touch, fonts, SD, launcher helpers | `docs/library.md` + `firmware/libraries/SquareBoard/src/*.h` |
| talking to the PC | `docs/protocol.md` + `firmware/templates/hello_pc` |
| the PC side (settings page, data, events) | `docs/hub-modules.md` + `hub/NexDeckHub.Sample.cs` |
| packaging/installing | `docs/app-package.md` |
| look and feel | `docs/design-guide.md` |
| pins and hardware quirks | `docs/hardware.md` |

## Rules that save time

- **Start from a template** (`firmware/templates/hello_app` or `hello_pc`). Folder name = `.ino` name = app id
  (lowercase, `[a-z0-9_]`). Every app: `sq::begin()`, `sqBootLauncherNext()` in `setup()`, hold 1 s → `sqReturnToLauncher()`.
- **Keep the UI portable**: put state + drawing in `src/*.cpp` using only `sq_gfx.h` (no Arduino calls), so it
  compiles on a PC (any C++17 compiler + `sq_gfx.cpp` + `sq_fonts.cpp`). Render frames to `.ppm`/`.png` and look at
  them before flashing; add checks for touch flows.
- **Redraw only what changed** and `sq::flush(rect)` those rectangles. Big buffers go in PSRAM (`ps_malloc`).
- **Text** must use characters the fonts have (ASCII + Latin accents); no emoji. From the PC, pass text through
  `Txt.Clean`.
- **PC link**: 460800 baud, every PC line ends with `\t~<sum16 hex>` and gets `K`/`E`; the board's events are
  `EV\t...` lines. Use `sqlink::checkLine`, `sqlink::common` (LS/FPUT/RM/OPENAPP) and answer `HI` with
  `EV\tHELLO\t<app id>`. Always answer `K`, even to unknown lines.
- **Data that must work without the PC** goes to the SD card (`FPUT` from the HUB, then your own reload command).
- **HUB code is C# 5** (csc of .NET Framework 4): no string interpolation (`$""`), no `?.`, no `nameof`, no
  expression-bodied members, no `out var`. WinForms only. UI work on the window thread; board I/O in
  `OnConnect`/`Loop`/`OnEvent` (board thread). Throw (`Worker.Must`) on I/O failure; the core reconnects.
- **Design**: slate palette on the board (`#0F172A` background, `#1E293B` tiles), flat colors, 88×88 flat icons,
  skeleton-with-shine for loading, no instructional text on screen. See `docs/design-guide.md`.
- **Don't** flash apps directly over USB for normal use (that replaces the launcher in `app0`); flash only the
  launcher, install apps through the SD card (HUB or `tools/push.ps1`).
- **Windows shell notes**: run `tools/push.ps1` from PowerShell (Git Bash rewrites `/apps/...` paths); the HUB
  holds the COM port while running (tray → *Pausar* to free it).

## Build commands

```sh
sh firmware/build.sh launcher COM3                 # flash the launcher (once)
sh firmware/build.sh path/to/my_app                # build an app → firmware/build/my_app/my_app.ino.bin
python -I tools/make_icon.py my_app 334155 MA path/to/my_app
python -I tools/make_nexapp.py path/to/my_app my_app "My App" 1.0   # → dist/my_app.nexapp
```
```powershell
powershell -ExecutionPolicy Bypass -File hub\build.ps1 [-Apps <modules dir>] [-Icons <png dir>] [-Resources <files>] [-Out <exe>]
powershell -ExecutionPolicy Bypass -File hub\build-module.ps1 -Src <module .cs> -Out dist\my_app.dll   # PC side as a .dll
python -I tools/make_nexapp.py path/to/my_app my_app "My App" 1.0 --pc dist/my_app.dll   # HUBs load it from the SD card
hub\NEXDECKHub.exe --shots C:\temp\shots           # render the HUB pages to PNG
# installer: the same exe named "Instalar NEXDECK HUB.exe" installs itself (no admin) and opens the HUB
```

## Definition of done for a new app

1. Builds with `firmware/build.sh`; the UI renders correctly in a PC simulation (screenshots reviewed).
2. Touch: every control has a pressed state; hold 1 s returns to the launcher.
3. Works offline (clear state when the PC is missing) and, if it talks to the PC, has a HUB module with a
   settings page built from `AppPage` components.
4. Has an 88×88 icon and a `.nexapp` package that installs from the HUB.
