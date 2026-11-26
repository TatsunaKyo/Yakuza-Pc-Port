"""Build the Windows distribution from Ivan's V2.0.0 ZIP and this fork's entry point."""
import argparse
import hashlib
from pathlib import Path
import zipfile

UPSTREAM_SHA = "e0e7e895fd10b719718533bbc871d713fb2251ae4996e0595523ab680665825c"
PREFIX = "Yakuza-win64/"


def build(original, entry, output, disc_folder=""):
    with original.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != UPSTREAM_SHA:
            raise ValueError("Use the upstream V2.0.0 Yakuza-win64.zip")
    if any(c in disc_folder for c in "\r\n"):
        raise ValueError("The starting folder must be one line")
    with zipfile.ZipFile(original) as src, zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as dst:
        for info in src.infolist():
            name = info.filename
            data = src.read(info)
            if name == PREFIX + "Yakuza.exe":
                name = PREFIX + "Yakuza-runtime.exe"
            elif name == PREFIX + "Yakuza.ini":
                contents = data.decode().splitlines()
                i = contents.index("iso=") + 1
                contents[i:i] = ["window_layout=borderless", "disc_folder=" + disc_folder]
                data = ("\r\n".join(contents) + "\r\n").encode()
            elif name == PREFIX + "defaults/config/mods.ini":
                assert data.count(b"fullscreen=On") == 1
                data = data.replace(b"fullscreen=On", b"fullscreen=Off")
            elif name == PREFIX + "LEIA-ME.txt":
                data += (b"\r\nNesta versao, Yakuza.exe abre .iso.gz (incluindo Restored). "
                         b"Alt+Enter alterna a janela sem bordas e a janela grande.\r\n")
            dst.writestr(name, data)
        dst.write(entry, PREFIX + "Yakuza.exe")
        srcdir = Path(__file__).resolve().parents[1] / "package/windows"
        for path in sorted(srcdir.glob("*.cs")):
            dst.write(path, PREFIX + "source/" + path.name)
        dst.write(srcdir / "README.txt", PREFIX + "README.txt")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("original", "entry", "output"):
        parser.add_argument(name, type=Path)
    parser.add_argument("--disc-folder", default="", help="optional first-run picker directory")
    args = parser.parse_args()
    build(args.original, args.entry, args.output, args.disc_folder)
