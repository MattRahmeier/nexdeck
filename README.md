# NEXDECK

**An app platform for the Guition ESP32-4848S040** — the 4" square 480×480 touch board — **plus NEXDECK HUB,
its Windows companion app.**

- **NEXDECK launcher** lives in the board's flash and installs apps from the microSD card (`/apps/*.bin`),
  with a welcome screen, floating icons and fast switching between apps.
- **SquareBoard library**: display, touch, SD, an anti-aliased drawing canvas, bitmap fonts (Barlow), launcher
  helpers and the serial protocol to talk to the PC.
- **NEXDECK HUB** (Windows tray app, C# 5 / .NET Framework 4, no installer): shows the apps on the board's card,
  installs `.nexapp` packages, opens apps on the board and hosts the **PC side of each app** (modules with their
  own settings page, data feeds and events).
- **Templates and docs** to build new apps, written to be read by people *and* by AI coding agents.

> This repository is the platform. Apps built on it by the authors are private and are not included.

## Quick start

1. Install [arduino-cli](https://arduino.github.io/arduino-cli/), the ESP32 core 3.3.x and the libraries
   *GFX Library for Arduino* and *JPEGDEC*. ([details](docs/getting-started.md))
2. Flash the launcher once over USB:
   ```sh
   sh firmware/build.sh launcher COM3
   ```
3. Build an app and package it:
   ```sh
   sh firmware/build.sh templates/hello_pc
   python -I tools/make_nexapp.py templates/hello_pc hello_pc "Hello PC" 1.0
   ```
4. Build and open NEXDECK HUB, then **Instalar novo app** → `dist/hello_pc.nexapp`:
   ```powershell
   powershell -ExecutionPolicy Bypass -File hub\build.ps1
   hub\NEXDECKHub.exe
   ```

## Repository map

| Path | What it is |
|---|---|
| `firmware/libraries/SquareBoard` | Core Arduino library (`SquareBoard.h`, `sq_gfx.h`, `sq_fonts.h`, `SquareLauncher.h`, `SquareLink.h`) |
| `firmware/launcher` | NEXDECK launcher (flashed to partition `app0`) |
| `firmware/templates/hello_app` | Smallest standalone app: draw, touch, back to the launcher |
| `firmware/templates/hello_pc` | App that talks to the PC (pairs with `hub/NexDeckHub.Sample.cs`) |
| `hub/` | NEXDECK HUB: core, window/components, QR encoder, sample module, `build.ps1` |
| `tools/` | `gen_fonts.py`, `make_icon.py`, `make_nexapp.py`, `push.ps1`, fonts (Barlow, OFL) |
| `docs/` | Guides and references (start at [docs/getting-started.md](docs/getting-started.md)) |
| `AGENTS.md`, `llms.txt` | Entry points for AI agents |

## Documentation

- [Getting started](docs/getting-started.md) — toolchain, flashing, your first app
- [Architecture](docs/architecture.md) — launcher, partitions, boot flow, how apps and the HUB fit together
- [Hardware](docs/hardware.md) — board, pins, quirks
- [Library reference](docs/library.md) — `sq::`, `Canvas`, fonts, launcher and link helpers
- [Protocol](docs/protocol.md) — board ⇄ PC serial protocol (lines, events, blocks, SD commands)
- [HUB modules](docs/hub-modules.md) — writing the PC side of an app
- [App packages](docs/app-package.md) — the `.nexapp` format
- [Design guide](docs/design-guide.md) — palette, layout, icons, loading states, touch

## Em português

NEXDECK é uma plataforma de apps para a placa Guition ESP32-4848S040 (tela quadrada de 4", 480×480) e o
NEXDECK HUB é o app de bandeja do Windows que acompanha a placa: mostra os apps do cartão, instala pacotes
`.nexapp`, abre apps na placa e faz o lado PC de cada app. Este repositório tem a base (biblioteca, lançador,
HUB, modelos, ferramentas e documentação); os apps feitos pelos autores são privados. A documentação está em
inglês para alcançar mais criadores e agentes de IA; o código tem comentários em português.

## License

MIT (see [LICENSE](LICENSE)). Fonts in `tools/fonts` and the generated `sq_fonts.cpp` are Barlow, SIL Open Font
License 1.1 (`tools/fonts/OFL.txt`).
