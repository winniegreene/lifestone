"""
mscab.py - stdlib-only Microsoft Cabinet (MSCF) lister/extractor.

* Lists CFHEADER / CFFOLDER / CFFILE records (names, sizes, DOS timestamps, attributes).
* Extracts folders stored uncompressed (type 0) or MSZIP (type 1) entirely in Python
  (MSZIP = per-CFDATA raw DEFLATE blocks with 'CK' prefix and a 32 KiB dictionary carried over).
* LZX (type 3) / Quantum (type 2) folders are NOT decoded here; `expand_with_windows()` calls the
  in-box, Microsoft-signed %SystemRoot%\\System32\\expand.exe on an already carved .cab, which runs
  as the invoking user (no elevation) - only used when the caller explicitly asks for it.

Usage:
    python -I mscab.py list <file.cab> [--offset N]
    python -I mscab.py extract <file.cab> <out-dir>          (type 0/1 only)
"""
from __future__ import annotations

import datetime
import os
import struct
import subprocess
import sys
import zlib

COMP_NAMES = {0: "none", 1: "MSZIP", 2: "Quantum", 3: "LZX"}


def dos_dt(d, t):
    try:
        return datetime.datetime(1980 + (d >> 9), (d >> 5) & 0xF, d & 0x1F,
                                 t >> 11, (t >> 5) & 0x3F, (t & 0x1F) * 2).isoformat()
    except ValueError:
        return f"invalid(0x{d:04X},0x{t:04X})"


def parse(f, base=0):
    f.seek(base)
    h = f.read(36)
    (sig, _, cb_cab, _, coff_files, _, vmin, vmaj, n_fold, n_files, flags, set_id,
     i_cab) = struct.unpack("<4sIIIIIBBHHHHH", h)
    if sig != b"MSCF":
        raise ValueError("not MSCF")
    p = 36
    cb_hres = cb_fres = cb_dres = 0
    if flags & 4:
        f.seek(base + p)
        cb_hres, cb_fres, cb_dres = struct.unpack("<HBB", f.read(4))
        p += 4 + cb_hres
    f.seek(base + p)
    hdr_tail = f.read(coff_files - p + 1)
    q = 0
    prev = nxt = None
    if flags & 1:
        prev = []
        for _ in range(2):
            e = hdr_tail.index(b"\0", q); prev.append(hdr_tail[q:e].decode("cp1252")); q = e + 1
    if flags & 2:
        nxt = []
        for _ in range(2):
            e = hdr_tail.index(b"\0", q); nxt.append(hdr_tail[q:e].decode("cp1252")); q = e + 1
    f.seek(base + p + q)
    folders = []
    for _ in range(n_fold):
        coff, nblk, typ = struct.unpack("<IHH", f.read(8))
        f.read(cb_fres)
        folders.append({"data_offset": coff, "blocks": nblk, "type_compress": typ,
                        "method": COMP_NAMES.get(typ & 0xF, f"?{typ & 0xF}"),
                        "lzx_window_bits": (typ >> 8) & 0x1F if (typ & 0xF) == 3 else None})
    f.seek(base + coff_files)
    files = []
    for _ in range(n_files):
        cb, uoff, ifold, d, t, attr = struct.unpack("<IIHHHH", f.read(16))
        name = bytearray()
        while True:
            c = f.read(1)
            if c in (b"\0", b""):
                break
            name += c
        enc = "utf-8" if attr & 0x80 else "cp1252"
        files.append({"name": name.decode(enc, "replace"), "size": cb, "folder_offset": uoff,
                      "folder": ifold, "datetime": dos_dt(d, t), "attributes": attr})
    return {"cabinet_size": cb_cab, "version": f"{vmaj}.{vmin}", "flags": flags, "set_id": set_id,
            "cabinet_index": i_cab, "reserve": [cb_hres, cb_fres, cb_dres], "prev": prev, "next": nxt,
            "folders": folders, "files": files}


def _folder_stream(f, base, info, fi):
    fo = info["folders"][fi]
    m = fo["type_compress"] & 0xF
    if m not in (0, 1):
        raise NotImplementedError(f"folder {fi}: {fo['method']} not decoded in Python")
    cb_dres = info["reserve"][2]
    f.seek(base + fo["data_offset"])
    out = bytearray()
    dictionary = b""
    for _ in range(fo["blocks"]):
        _csum, cb_data, cb_uncomp = struct.unpack("<IHH", f.read(8))
        f.read(cb_dres)
        data = f.read(cb_data)
        if m == 0:
            out += data
            continue
        if data[:2] != b"CK":
            raise ValueError("MSZIP block without CK signature")
        d = zlib.decompressobj(-15, zdict=dictionary) if dictionary else zlib.decompressobj(-15)
        blk = d.decompress(data[2:]) + d.flush()
        if len(blk) != cb_uncomp:
            raise ValueError(f"MSZIP block size {len(blk)} != {cb_uncomp}")
        out += blk
        dictionary = bytes(out[-32768:])
    return bytes(out)


def extract_python(path, out_dir, base=0):
    with open(path, "rb") as f:
        info = parse(f, base)
        streams = {}
        written = []
        for fe in info["files"]:
            if fe["folder"] >= 0xFFFD:
                raise NotImplementedError("continued-folder files not supported")
            if fe["folder"] not in streams:
                streams[fe["folder"]] = _folder_stream(f, base, info, fe["folder"])
            data = streams[fe["folder"]][fe["folder_offset"]:fe["folder_offset"] + fe["size"]]
            dest = os.path.join(out_dir, *fe["name"].replace("/", "\\").split("\\"))
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            with open(dest, "wb") as o:
                o.write(data)
            written.append(dest)
    return written


def expand_with_windows(cab_path, out_dir):
    """Run the in-box expand.exe (no shell, no elevation) on a carved cabinet."""
    sysroot = os.environ.get("SystemRoot", r"C:\Windows")
    exe = os.path.join(sysroot, "System32", "expand.exe")
    os.makedirs(out_dir, exist_ok=True)
    r = subprocess.run([exe, cab_path, "-F:*", out_dir], capture_output=True, text=True,
                       timeout=300, shell=False)
    return exe, r.returncode, r.stdout, r.stderr


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    base = int(argv[argv.index("--offset") + 1], 0) if "--offset" in argv else 0
    if argv[1] == "list":
        with open(argv[2], "rb") as f:
            info = parse(f, base)
        for k, v in info.items():
            if k != "files":
                print(f"{k}: {v}")
        for fe in info["files"]:
            print(f"  {fe['size']:>10,}  {fe['datetime']}  attr=0x{fe['attributes']:02X}  {fe['name']}")
        return 0
    if argv[1] == "extract":
        for w in extract_python(argv[2], argv[3], base):
            print(w)
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
