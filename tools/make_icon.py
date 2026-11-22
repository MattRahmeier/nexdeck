"""Draws a simple app icon in the NEXDECK style and writes <id>.icn (board) and <id>.png (HUB card).

All launcher icons are the same rounded square (88x88), flat color, no gradients; the launcher draws
them floating with a drop shadow and the name below (docs/design-guide.md).

Usage:
  python -I tools/make_icon.py <id> <hex color> <text> [out dir]
  python -I tools/make_icon.py hello_pc 334155 Hi firmware/templates/hello_pc
For richer icons draw your own 88x88 RGBA image and convert it with write_icn() from this module.

.icn format: "ICN1", width (1 byte), height (1 byte), then per pixel RGB565 little-endian (2 bytes)
followed by alpha (1 byte). The launcher reads /apps/<id>.icn next to /apps/<id>.bin.
"""
import struct
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SIZE = 88
S = SIZE * 4  # draw 4x bigger, then downscale (smooth edges)


def base(color):
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), radius=S * 0.24, fill=255)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(Image.new("RGB", (S, S), color), (0, 0), mask)
    return img


def write_icn(img, out_dir, name):
    small = img.resize((SIZE, SIZE), Image.LANCZOS).convert("RGBA")
    data = bytearray(b"ICN1" + bytes([SIZE, SIZE]))
    raw = small.tobytes()
    for i in range(0, len(raw), 4):
        r, g, b, a = raw[i:i + 4]
        data += struct.pack("<HB", ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3), a)
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    (out / f"{name}.icn").write_bytes(data)
    small.save(out / f"{name}.png")
    print(f"{out / name}.icn / .png")


def text_icon(color_hex, text):
    color = tuple(int(color_hex[i:i + 2], 16) for i in (0, 2, 4))
    img = base(color)
    font = ImageFont.truetype(str(ROOT / "tools" / "fonts" / "Barlow-SemiBold.ttf"), int(S * (0.42 if len(text) <= 2 else 0.3)))
    ImageDraw.Draw(img).text((S / 2, S / 2), text, font=font, fill=(248, 250, 252, 255), anchor="mm")
    return img


if __name__ == "__main__":
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    name, color, text = sys.argv[1:4]
    write_icn(text_icon(color, text), sys.argv[4] if len(sys.argv) > 4 else ".", name)
