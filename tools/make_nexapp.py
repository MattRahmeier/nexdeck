"""Empacota um app do NEXDECK para instalar pelo NEXDECK HUB ("Instalar novo app").

Formato .nexapp: um .zip com
  manifest.json  {"id": "hello_pc", "name": "Hello PC", "version": "1.0", "bin": "app.bin", "icon": "icon.icn"}
  app.bin        o firmware do app (o mesmo .bin que vai para /apps no cartão)
  icon.icn       ícone 88×88 do NEXLauncher (ou icon.png, que o HUB converte)
  pc.dll         lado PC do app (módulo do HUB de hub/build-module.ps1), opcional
O HUB grava /apps/<id>.bin, /apps/<id>.icn e /apps/<id>.dll no cartão; qualquer HUB que veja o cartão
copia o pc.dll sozinho.

Uso: python -I tools/make_nexapp.py <sketch dir> <id> "<nome>" <versão> [--pc <módulo.dll>]
     python -I tools/make_nexapp.py templates/hello_pc hello_pc "Hello PC" 1.0   → dist/hello_pc.nexapp
O .bin vem de firmware/build/<pasta>/<pasta>.ino.bin; o ícone, de <sketch dir>/<id>.icn (tools/make_icon.py).
"""
import json
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def main():
    args = sys.argv[1:]
    pc_path = None
    if "--pc" in args:
        i = args.index("--pc")
        pc_path = Path(args[i + 1])
        del args[i:i + 2]
    if len(args) != 4:
        sys.exit(__doc__)
    sketch, app_id, name, version = args
    folder = Path(sketch).name
    bin_path = ROOT / "firmware" / "build" / folder / f"{folder}.ino.bin"
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
        if pc_path:
            z.write(pc_path, "pc.dll")
            manifest["pc"] = "pc.dll"
        z.writestr("manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2))
    print(f"{out.relative_to(ROOT)}: {out.stat().st_size} bytes")


if __name__ == "__main__":
    main()
