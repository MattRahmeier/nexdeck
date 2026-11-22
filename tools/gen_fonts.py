"""Generates the bitmap fonts of the SquareBoard library (firmware/libraries/SquareBoard/src/sq_fonts.cpp).

Each glyph becomes a 4-bit alpha bitmap (2 pixels per byte). All fonts here are Barlow (SIL OFL 1.1,
tools/fonts/OFL.txt). Italic styles are synthesized (sheared) from the upright font.

Usage:  python -I tools/gen_fonts.py
Apps that need their own fonts can import this module and call render()/emit() with their own SPECS
and output path (see docs/library.md, "Custom fonts").
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FONTS = ROOT / "tools" / "fonts"
OUT = ROOT / "firmware" / "libraries" / "SquareBoard" / "src" / "sq_fonts.cpp"

# ASCII + Portuguese/Spanish/French accents + typographic punctuation (text that arrives from the PC)
LATIN = ([chr(c) for c in range(32, 127)] + list("ÁÀÂÃÉÊÍÓÔÕÚÇáàâãéêíóôõúç") + ["−", "×", "·"]
         + list("ÄËÏÖÜäëïöüÑñÈèÌìÒòÙùÎîÛûºª°«»€“”‘’–—…"))
DIGITS = list("0123456789-+−")

# (C name, file, size in px, characters[, "italic"])
SPECS = [
    ("FONT_LIFE", "BarlowCondensed-SemiBold.ttf", 108, DIGITS),   # huge digits
    ("FONT_HUGE", "BarlowCondensed-SemiBold.ttf", 210, DIGITS),
    ("FONT_TINY", "BarlowCondensed-SemiBold.ttf", 22, DIGITS),
    ("FONT_BIG", "BarlowCondensed-SemiBold.ttf", 64, LATIN),      # titles
    ("FONT_MED", "Barlow-SemiBold.ttf", 30, LATIN),               # buttons, page titles
    ("FONT_SMALL", "Barlow-Medium.ttf", 22, LATIN),
    ("FONT_CLOCK", "BarlowCondensed-SemiBold.ttf", 70, list("0123456789:-")),
    ("FONT_TEMP", "BarlowCondensed-SemiBold.ttf", 50, list("0123456789-−°")),
    ("FONT_COND", "BarlowCondensed-SemiBold.ttf", 28, LATIN),
    ("FONT_HEAD", "Barlow-SemiBold.ttf", 21, LATIN),
    ("FONT_BODY", "Barlow-Medium.ttf", 16, LATIN),
    ("FONT_CAP", "Barlow-SemiBold.ttf", 13, LATIN),
    ("FONT_MINI", "Barlow-SemiBold.ttf", 11, LATIN),
    ("FONT_WATCH", "BarlowCondensed-SemiBold.ttf", 150, list("0123456789:")),
    # rich text family (bold/italic), for apps that show formatted text
    ("FONT_TITLE", "Barlow-SemiBold.ttf", 36, LATIN),
    ("FONT_TXT", "Barlow-Medium.ttf", 24, LATIN),
    ("FONT_TXT_B", "Barlow-SemiBold.ttf", 24, LATIN),
    ("FONT_TXT_I", "Barlow-Medium.ttf", 24, LATIN, "italic"),
]

SHEAR = 0.2  # synthesized italic slant


def pack(img):
    w, h = img.size
    px, data = img.tobytes(), bytearray()
    for y in range(h):
        row = px[y * w:(y + 1) * w]
        for x in range(0, w, 2):
            data.append(((row[x] >> 4) << 4) | ((row[x + 1] >> 4) if x + 1 < w else 0))
    return data


def render_fit(cname, file, target, chars):
    """Each glyph at the size that makes its largest side `target` px, cropped to its outline (icon fonts)."""
    glyphs, data = [], bytearray()
    for ch in sorted(set(chars), key=ord):
        probe = Image.new("L", (300, 300))
        ImageDraw.Draw(probe).text((60, 60), ch, font=ImageFont.truetype(str(FONTS / file), 124), fill=255)
        b = probe.getbbox()
        size = round(124 * target / max(b[2] - b[0], b[3] - b[1]))
        img = Image.new("L", (3 * size, 3 * size))
        ImageDraw.Draw(img).text((size, size), ch, font=ImageFont.truetype(str(FONTS / file), size), fill=255)
        img = img.crop(img.getbbox())
        glyphs.append((ord(ch), img.width, img.height, 0, 0, img.width, len(data)))
        data += pack(img)
    return cname, target, 0, glyphs, data


def render(spec):
    cname, file, size, chars = spec[:4]
    italic = len(spec) > 4 and spec[4] == "italic"
    if isinstance(size, tuple):
        return render_fit(cname, file, size[1], chars)
    font = ImageFont.truetype(str(FONTS / file), size)
    ascent, descent = font.getmetrics()
    glyphs, data = [], bytearray()
    for ch in sorted(set(chars), key=ord):
        if font.getmask(ch).size == (0, 0) and ch != " ":
            continue
        adv = round(font.getlength(ch))
        bbox = font.getbbox(ch)  # relative to the pen origin, line top = 0
        if bbox[2] <= bbox[0] or bbox[3] <= bbox[1]:
            glyphs.append((ord(ch), 0, 0, 0, 0, adv, len(data)))
            continue
        w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]
        img = Image.new("L", (w, h), 0)
        ImageDraw.Draw(img).text((-bbox[0], -bbox[1]), ch, font=font, fill=255)
        xoff = bbox[0]
        if italic:
            # slant around the baseline: x' = x + SHEAR * (baseline - y)
            base = ascent - bbox[1]                     # baseline row inside the glyph image
            right = int(SHEAR * max(base, 0)) + 2        # top leans right
            left = int(SHEAR * max(h - base, 0)) + 2     # descenders lean left
            wide = Image.new("L", (left + w + right, h), 0)
            wide.paste(img, (left, 0))
            img = wide.transform(wide.size, Image.AFFINE, (1, SHEAR, -SHEAR * base, 0, 1, 0), Image.BICUBIC)
            box = img.getbbox()
            if box:
                img = img.crop((box[0], 0, box[2], h))
                xoff += box[0] - left
            w = img.width
        off = len(data)
        data += pack(img)
        glyphs.append((ord(ch), w, h, xoff, bbox[1], adv, off))
    return cname, ascent, descent, glyphs, data


def emit(fonts, out=OUT, header="sq_fonts.h", credits="Barlow (The Barlow Project Authors, SIL OFL 1.1)"):
    lines = ["// Generated by tools/gen_fonts.py — do not edit by hand.",
             f"// Fonts: {credits}.",
             f'#include "{header}"', ""]
    for cname, ascent, descent, glyphs, data in fonts:
        low = cname.lower()
        lines.append(f"static const uint8_t {low}_bits[] = {{")
        for i in range(0, len(data), 24):
            lines.append("  " + ",".join(str(b) for b in data[i:i + 24]) + ",")
        lines.append("};")
        lines.append(f"static const Glyph {low}_glyphs[] = {{")
        for cp, w, h, xo, yo, adv, off in glyphs:
            lines.append(f"  {{{cp},{w},{h},{xo},{yo},{adv},{off}}},")
        lines.append("};")
        lines.append(f"const Font {cname} = {{{low}_glyphs, {len(glyphs)}, {low}_bits, {ascent}, {descent}}};")
        lines.append("")
        print(f"{cname}: {len(glyphs)} glyphs, {len(data)} bytes")
    Path(out).parent.mkdir(parents=True, exist_ok=True)
    Path(out).write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    emit([render(s) for s in SPECS])
