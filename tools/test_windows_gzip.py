"""Exercise the packaged Windows entry, its gzip reader, and actual desktop window geometry."""
import ctypes as C
from ctypes import wintypes as W
import gzip
import hashlib
from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile
import time
import zipfile
import zlib

import pycdlib

ROOT = Path(__file__).resolve().parents[1]
USER = C.windll.user32
USER.FindWindowW.argtypes = [W.LPCWSTR, W.LPCWSTR]
USER.FindWindowW.restype = W.HWND
USER.FindWindowExW.argtypes = [W.HWND, W.HWND, W.LPCWSTR, W.LPCWSTR]
USER.FindWindowExW.restype = W.HWND
USER.GetClientRect.argtypes = USER.GetWindowRect.argtypes = [W.HWND, C.POINTER(W.RECT)]
USER.GetWindowLongW.argtypes = [W.HWND, C.c_int]
USER.IsZoomed.argtypes = USER.SetForegroundWindow.argtypes = [W.HWND]
USER.GetForegroundWindow.restype = W.HWND
try:
    USER.SetProcessDpiAwarenessContext(C.c_void_p(-4))
except AttributeError:
    USER.SetProcessDPIAware()


def wait_for(fn, seconds=20):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        value = fn()
        if value:
            return value
        time.sleep(0.1)
    raise AssertionError("Timed out waiting for " + fn.__name__)


def stop(proc):
    if proc.poll() is None:
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], capture_output=True)
        proc.wait(timeout=20)


def check_import(probe, temp):
    cache = temp / "test cache"
    def run(source, good=True):
        r = subprocess.run([probe, "import", source, cache], capture_output=True, text=True, timeout=180)
        if good:
            assert r.returncode == 0, r.stderr
            return Path(r.stdout.strip())
        assert r.returncode != 0, "damaged input was accepted"

    raw = b"arbitrary disc data without a serial" + bytes(range(256)) * 3000
    source = temp / "Restauration à Tokyo.ISO.GZ"
    packed = gzip.compress(raw[:80]) + gzip.compress(raw[80:])
    source.write_bytes(packed)
    cached = run(source)
    assert cached.read_bytes() == raw
    stamp = cached.stat().st_mtime_ns
    assert run(source) == cached and cached.stat().st_mtime_ns == stamp
    bad = temp / "damaged.iso.gz"
    for data in (packed[:-4], packed[:-8] + bytes([packed[-8] ^ 1]) + packed[-7:], b"plain file"):
        bad.write_bytes(data)
        run(bad, False)
        assert set(cache.iterdir()) == {cached}

    big = temp / "large.iso.gz"
    block = bytes(4 << 20)
    digest = hashlib.sha256()
    with big.open("wb") as f:
        z = zlib.compressobj(1, wbits=31)
        for _ in range(1024):
            digest.update(block)
            f.write(z.compress(block))
        tail = b"after the 4 GiB boundary"
        digest.update(tail)
        f.write(z.compress(tail) + z.flush())
    result = run(big)
    with result.open("rb") as stream:
        assert result.stat().st_size > 2**32 and hashlib.file_digest(stream, "sha256").digest() == digest.digest()
    result.unlink()
    print("Verified arbitrary contents, Unicode, multiple members, CRC, truncation, cache reuse and over 4 GiB.", flush=True)


def check_runtime(port, temp):
    raw = temp / "test disc.iso"
    iso = pycdlib.PyCdlib()
    iso.new(interchange_level=2)
    for path in sorted((ROOT / "disc").iterdir()):
        if path.is_file():
            iso.add_file(str(path), iso_path="/" + path.name.upper() + ";1")
    iso.write(str(raw))
    iso.close()
    source = temp / "Disc with spaces.ISO.GZ"
    with raw.open("rb") as src, gzip.open(source, "wb") as out:
        shutil.copyfileobj(src, out)
    proc = subprocess.Popen([port / "Yakuza.exe", "--iso", source], cwd=temp)
    try:
        boot = port / "disc/SLUS_213.48"
        wait_for(lambda: boot.is_file() and boot.stat().st_size == (ROOT / "disc/SLUS_213.48").stat().st_size, 45)
        assert boot.read_bytes() == (ROOT / "disc/SLUS_213.48").read_bytes()
        cfg = (port / "Yakuza.ini").read_text(encoding="utf-8-sig")
        saved, = [s[4:] for s in cfg.splitlines() if s.startswith("iso=")]
        assert Path(saved).parent.resolve() == (port / "Discs").resolve()
        assert Path(saved).read_bytes() == raw.read_bytes()
        print("V2.0.0 opened the gzip import and extracted its original boot file.", flush=True)
    finally:
        stop(proc)
    return raw


def check_layout(port, probe, raw, temp):
    geo = temp / "Window probe"
    geo.mkdir()
    shutil.copy2(probe, geo / "Yakuza-runtime.exe")
    shutil.copy2(port / "Yakuza.exe", geo)
    shutil.copy2(port / "Yakuza.ini", geo)
    proc = subprocess.Popen([geo / "Yakuza.exe", "--iso", raw])
    try:
        w = wait_for(lambda: USER.FindWindowW("GLFW30", "Yakuza window geometry test"))
        gs = wait_for(lambda: USER.FindWindowExW(w, 0, "ps2x_pcsx2_gs", None))
        def size(hwnd, client):
            r = W.RECT()
            assert (USER.GetClientRect if client else USER.GetWindowRect)(hwnd, C.byref(r))
            return r.right - r.left, r.bottom - r.top
        def borderless():
            style = USER.GetWindowLongW(w, -16) & 0xFFFFFFFF
            return (style & 0x80000000 and not style & 0x00C00000 and
                    size(w, False) == (USER.GetSystemMetrics(0), USER.GetSystemMetrics(1)-1) and
                    size(gs, True) == size(w, True))
        wait_for(borderless)
        USER.SetForegroundWindow(w)
        wait_for(lambda: USER.GetForegroundWindow() == w)
        def alt_enter():
            for key in (0x12, 0x0D): USER.keybd_event(key, 0, 0, 0)
            time.sleep(0.18)
            for key in (0x0D, 0x12): USER.keybd_event(key, 0, 2, 0)
            time.sleep(0.1)
        alt_enter()
        wait_for(lambda: USER.IsZoomed(w) and USER.GetWindowLongW(w,-16) & 0x40000 and size(gs,True) == size(w,True))
        alt_enter()
        wait_for(borderless)
        print("Verified borderless monitor bounds, maximize and resize styles, GS child size, and both Alt+Enter transitions.", flush=True)
    finally:
        stop(proc)


if __name__ == "__main__":
    with tempfile.TemporaryDirectory() as d:
        temp = Path(d)
        with zipfile.ZipFile(sys.argv[1]) as bundle:
            bundle.extractall(temp)
        port = temp / "Yakuza-win64"
        probe = port / "probe.exe"
        compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
        subprocess.run([compiler, "/nologo", "/target:exe", "/platform:x64", "/out:"+str(probe),
                        "/r:System.Windows.Forms.dll", ROOT / "tools/windows_probe.cs",
                        ROOT / "package/windows/Disc.cs"], check=True)
        check_import(probe, temp)
        raw = check_runtime(port, temp)
        check_layout(port, probe, raw, temp)
