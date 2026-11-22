# App packages (`.nexapp`)

A `.nexapp` is a ZIP file:

```
manifest.json
app.bin        the firmware (what arduino-cli writes as <sketch>.ino.bin)
icon.icn       optional launcher icon (88×88 .icn), or icon.png (the HUB converts it)
```

`manifest.json`:

```json
{
  "id": "my_app",
  "name": "My App",
  "version": "1.0",
  "bin": "app.bin",
  "icon": "icon.icn"
}
```

| Field | Rules |
|---|---|
| `id` | `[A-Za-z0-9_-]{1,30}`; becomes `/apps/<id>.bin` and `/apps/<id>.icn`. Same id = update. |
| `name` | shown in the HUB |
| `version` | free text |
| `bin` | path inside the zip (default `app.bin`); must be an ESP32 image (first byte `0xE9`) |
| `icon` | `.icn` or a PNG/JPEG (converted to 88×88) |

Build one with `python -I tools/make_nexapp.py <sketch dir> <id> "<name>" <version>` (output in `dist/`).
Install it from NEXDECK HUB → *Instalar novo app*. The HUB writes the files with `FPUT` (app open) or `PUT`
(launcher open) and keeps the icon PNG in `%APPDATA%\NEXDECK HUB\icones` for the app card.

## `.icn` icon format

```
"ICN1"  width (1 byte)  height (1 byte)
then width × height pixels: RGB565 little-endian (2 bytes) + alpha (1 byte)
```

`tools/make_icon.py` draws a simple one; `write_icn(image, folder, id)` converts any 88×88 RGBA image.
