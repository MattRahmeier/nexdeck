# Getting started

## 1. Toolchain

| Tool | Notes |
|---|---|
| [arduino-cli](https://arduino.github.io/arduino-cli/) | `arduino-cli core install esp32:esp32@3.3.x` (tested with 3.3.13) |
| Arduino libraries | `arduino-cli lib install "GFX Library for Arduino" JPEGDEC` |
| Python 3 + Pillow | for `tools/*.py` (`pip install pillow`) |
| Windows + .NET Framework 4.x | for NEXDECK HUB (`csc.exe` ships with Windows) |
| CH340 driver | the board's USB-serial chip; Windows 10/11 usually installs it automatically |

Board settings (already in `firmware/build.sh`):
`esp32:esp32:esp32s3:PSRAM=opi,FlashSize=16M,PartitionScheme=app3M_fat9M_16MB,CDCOnBoot=default,UploadMode=default`

## 2. Flash the launcher (once)

Plug the board over USB-C, find its COM port (Device Manager → "USB-SERIAL CH340 (COMx)") and run:

```sh
sh firmware/build.sh launcher COM3
```

The NEXDECK welcome screen appears, then the app grid. Put a FAT32 microSD card in the slot; apps live in `/apps`.

## 3. Your first app

```sh
cp -r firmware/templates/hello_app firmware/my_app
mv firmware/my_app/hello_app.ino firmware/my_app/my_app.ino   # folder name = .ino name = app id
sh firmware/build.sh my_app                                   # → firmware/build/my_app/my_app.ino.bin
python -I tools/make_icon.py my_app 0E7490 MA firmware/my_app # icon: my_app.icn + my_app.png
python -I tools/make_nexapp.py my_app my_app "My App" 1.0     # → dist/my_app.nexapp
```

Install it:

- **NEXDECK HUB** → *Instalar novo app* → choose `dist/my_app.nexapp` (works with the board in the launcher or in
  any app that uses `SquareLink`), or
- **USB push** with the launcher open (run from PowerShell, not Git Bash):
  ```powershell
  powershell -File tools\push.ps1 -File firmware\build\my_app\my_app.ino.bin -Dest /apps/my_app.bin
  powershell -File tools\push.ps1 -File firmware\my_app\my_app.icn -Dest /apps/my_app.icn
  powershell -File tools\push.ps1 -Run my_app
  ```

Tap the icon on the board: the launcher copies the `.bin` to the app partition (10–20 s the first time) and
restarts into it. Hold the screen for 1 s to go back.

## 4. An app that talks to the PC

`firmware/templates/hello_pc` + `hub/NexDeckHub.Sample.cs` are a matching pair. Build both:

```sh
sh firmware/build.sh templates/hello_pc
python -I tools/make_nexapp.py templates/hello_pc hello_pc "Hello PC" 1.0
```
```powershell
powershell -ExecutionPolicy Bypass -File hub\build.ps1
hub\NEXDECKHub.exe
```

Install `dist/hello_pc.nexapp` from the HUB, open it on the board, then open *Hello PC* in the HUB, type a
message and save: it appears on the board, the PC clock updates every second and touches are counted in the HUB.
Read [protocol.md](protocol.md) and [hub-modules.md](hub-modules.md) next.

## 5. Your own HUB with your own modules

Keep your app modules in your own folder (they can be private) and compile the HUB with them:

```powershell
powershell -ExecutionPolicy Bypass -File hub\build.ps1 -Apps C:\my\hub-apps -Icons C:\my\icons -Out C:\my\NEXDECKHub.exe
```

Each module is a class that extends `HubApp`; register it in a `partial class HubApps` (see hub-modules.md).

## Tips

- Develop the UI as portable C++ (no Arduino calls) and render it on the PC with the same `sq_gfx.cpp`: compile
  with any C++17 compiler, write the framebuffer to a `.ppm`, check pixels and timing before flashing.
- `Serial.printf` debug output goes to the same USB port the HUB uses: pause the HUB (tray icon → *Pausar*) to
  use a serial monitor or `push.ps1`.
- Every flash/SD write takes the bus for a few ms and the RGB panel may skip a frame: write files when the screen
  is static, not during animations.
