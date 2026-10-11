"""
ibt.py - unpack InstallShield setup.ibt (bundled setup.dll etc.) and SZDD ("compress.exe") files.

Stdlib only; never executes anything.

setup.ibt layout (InstallShield 10, observed in ac1install.exe):
    repeated until EOF:
        char name[]      NUL-terminated  stored name, e.g. "setup.dl_"
        char target[]    NUL-terminated  expanded name, e.g. "setup.dll"
        char version[]   NUL-terminated  e.g. "10.0.0.159"
        char size[]      NUL-terminated  decimal size of the stored payload
        u8   data[size]  usually an SZDD stream

SZDD (MS-DOS/Windows COMPRESS.EXE "mode A" LZSS):
    'SZDD' 88 F0 27 33 | u8 mode ('A') | u8 missing_char | u32 expanded_size | stream
    stream: control byte, LSB first, 1 = literal byte, 0 = match:
        b0 b1 -> pos = b0 | (b1 & 0xF0) << 4, len = (b1 & 0x0F) + 3
    4096-byte ring buffer prefilled with 0x20, first write index 4096 - 16.

Usage:
    python -I ibt.py <setup.ibt> [out-dir]
"""
from __future__ import annotations

import hashlib
import os
import struct
import sys

SZDD_MAGIC = b"SZDD\x88\xf0\x27\x33"


def szdd_decompress(data: bytes) -> bytes:
    if data[:8] != SZDD_MAGIC:
        raise ValueError("not SZDD")
    mode = data[8:9]
    if mode != b"A":
        raise NotImplementedError(f"SZDD mode {mode!r}")
    size = struct.unpack_from("<I", data, 10)[0]
    win = bytearray(b" " * 4096)
    wp = 4096 - 16
    out = bytearray()
    i = 14
    n = len(data)
    while i < n and len(out) < size:
        ctrl = data[i]
        i += 1
        for bit in range(8):
            if i >= n or len(out) >= size:
                break
            if ctrl & (1 << bit):
                c = data[i]
                i += 1
                out.append(c)
                win[wp] = c
                wp = (wp + 1) & 0xFFF
            else:
                if i + 1 >= n:
                    i = n
                    break
                b0, b1 = data[i], data[i + 1]
                i += 2
                pos = b0 | ((b1 & 0xF0) << 4)
                ln = (b1 & 0x0F) + 3
                for k in range(ln):
                    c = win[(pos + k) & 0xFFF]
                    out.append(c)
                    win[wp] = c
                    wp = (wp + 1) & 0xFFF
    if len(out) != size:
        raise ValueError(f"SZDD: produced {len(out)} bytes, header says {size}")
    return bytes(out)


def parse_ibt(blob: bytes):
    entries = []
    p = 0
    while p < len(blob):
        fields = []
        q = p
        for _ in range(4):
            e = blob.index(b"\0", q)
            fields.append(blob[q:e].decode("ascii"))
            q = e + 1
        stored, target, ver, size = fields
        size = int(size)
        entries.append({"stored_name": stored, "target_name": target, "version": ver,
                        "stored_size": size, "offset": q, "data": blob[q:q + size]})
        p = q + size
    if p != len(blob):
        raise ValueError(f"trailing bytes in ibt: {len(blob) - p}")
    return entries


def unpack(blob: bytes, out_dir: str | None):
    res = []
    for e in parse_ibt(blob):
        data = e.pop("data")
        if data[:8] == SZDD_MAGIC:
            data = szdd_decompress(data)
            e["compression"] = "SZDD"
        else:
            e["compression"] = "none"
        e.update({"expanded_size": len(data), "md5": hashlib.md5(data).hexdigest(),
                  "sha256": hashlib.sha256(data).hexdigest()})
        if out_dir:
            os.makedirs(out_dir, exist_ok=True)
            dest = os.path.join(out_dir, os.path.basename(e["target_name"]))
            with open(dest, "wb") as f:
                f.write(data)
            e["written_to"] = dest
        res.append(e)
    return res


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    with open(argv[1], "rb") as f:
        blob = f.read()
    for e in unpack(blob, argv[2] if len(argv) > 2 else None):
        print(e)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
