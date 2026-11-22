"""Empacota um app do NEXDECK para instalar pelo NEXDECK HUB ("Instalar novo app").

Formato .nexapp: um .zip com
  manifest.json  {"id": "hello_pc", "name": "Hello PC", "version": "1.0", "bin": "app.bin", "icon": "icon.icn"}
  app.bin        o firmware do app (o mesmo .bin que vai para /apps no cartão)
  icon.icn       ícone 88×88 do NEXDECK (ou icon.png, que o HUB converte)
O HUB grava /apps/<id>.bin e /apps/<id>.icn no cartão.

Uso: python -I tools/make_nexapp.py <sketch dir> <id> "<nome>" <versão>
     python -I tools/make_nexapp.py templates/hello_pc hello_pc "Hello PC" 1.0   → dist/hello_pc.nexapp
O .bin vem de firmware/build/<pasta>/<pasta>.ino.bin; o ícone, de <sketch dir>/<id>.icn (tools/make_icon.py).
"""
import json
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def main():
    if len(sys.argv) != 5:
        sys.exit(__doc__)
    sketch, app_id, name, version = sys.argv[1:]
    name = Path(sketch).name
    bin_path = ROOT / "firmware" / "build" / name / f"{name}.ino.bin"
    icon_path = ROOT / "firmware" / sketch / f"{app_id}.icn"
    if not icon_path.exists():
        icon_path = ROOT / "firmware" / "icons" / f"{app_id}.icn"
    if not bin_path.exists():
        sys.exit(f"compile antes: {bin_path} não existe (sh firmware/build.sh {sketch})")
    out = ROOT / "dist" / f"{app_id}.nexapp"
    out.parent.mkdir(exist_ok=True)
    manifest = {"id": app_id, "name": name, "version": version, "bin": "app.bin"}
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(bin_path, "app.bin")
        if icon_path.exists():
            z.write(icon_path, "icon.icn")
            manifest["icon"] = "icon.icn"
        z.writestr("manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2))
    print(f"{out.relative_to(ROOT)}: {out.stat().st_size} bytes")


if __name__ == "__main__":
    main()
