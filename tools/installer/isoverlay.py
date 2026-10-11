"""
isoverlay.py - map the PE overlay of an InstallShield "Setup.exe with embedded Disk1" stub.

Stdlib only. Never executes anything; it only reads bytes.

Container layout (as found in ac1install.exe, InstallShield 10 setup launcher 10.0.0.159):

    [PE32 stub: .text/.rdata/.data/.rsrc]   (unsigned; no IMAGE_DIRECTORY_ENTRY_SECURITY)
    [overlay = end of last section's raw data]
        repeated until EOF:
            char  name[]      NUL-terminated  e.g. "data1.cab"
            char  relpath[]   NUL-terminated  e.g. "Disk1\\data1.cab"
            char  version[]   NUL-terminated  e.g. "0.0.0.0" / "10.0.0.159"
            char  size[]      NUL-terminated, decimal ASCII
            u8    data[size]  stored verbatim (no compression, no XOR)

Usage:
    python -I isoverlay.py <setup.exe>                 # list entries
    python -I isoverlay.py <setup.exe> --carve <dir>   # write each entry to <dir>\\<relpath>
           [--skip-larger-than N]                      # e.g. leave the 597 MB data2.cab in place
"""
from __future__ import annotations

import hashlib
import json
import os
import struct
import sys
from dataclasses import dataclass, asdict


@dataclass
class OverlayEntry:
    name: str
    relpath: str
    version: str
    size: int
    header_offset: int   # file offset of the name string
    data_offset: int     # file offset of the first payload byte
    magic: str           # first 8 payload bytes, hex


def pe_overlay_start(f) -> tuple[int, dict]:
    """Return (overlay_offset, info) for a PE file object."""
    f.seek(0)
    d = f.read(0x1000)
    if d[:2] != b"MZ":
        raise ValueError("not an MZ file")
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    if d[pe:pe + 4] != b"PE\0\0":
        raise ValueError("no PE signature")
    machine, nsec, ts, _, _, optsz, chars = struct.unpack_from("<HHIIIHH", d, pe + 4)
    opt = pe + 24
    magic = struct.unpack_from("<H", d, opt)[0]
    dd_base = opt + (96 if magic == 0x10B else 112)
    sec_dir = struct.unpack_from("<II", d, dd_base + 4 * 8)  # IMAGE_DIRECTORY_ENTRY_SECURITY
    end = 0
    sections = []
    for i in range(nsec):
        name, vs, va, rs, rp = struct.unpack_from("<8sIIII", d, opt + optsz + 40 * i)
        sections.append({"name": name.rstrip(b"\0").decode("ascii", "replace"),
                         "vsize": vs, "va": va, "raw_size": rs, "raw_ptr": rp})
        end = max(end, rp + rs)
    info = {"machine": machine, "timestamp": ts, "characteristics": chars,
            "optional_magic": magic, "sections": sections,
            "authenticode_dir": {"offset": sec_dir[0], "size": sec_dir[1]}}
    return end, info


def _read_cstr(f, limit=1024) -> bytes | None:
    out = bytearray()
    while len(out) < limit:
        c = f.read(1)
        if not c:
            return None
        if c == b"\0":
            return bytes(out)
        out += c
    return None


def _printable(b: bytes) -> bool:
    return all(32 <= c < 127 for c in b)


def parse_overlay(path: str) -> tuple[list[OverlayEntry], dict]:
    size = os.path.getsize(path)
    with open(path, "rb") as f:
        start, info = pe_overlay_start(f)
        entries: list[OverlayEntry] = []
        off = start
        while off < size:
            f.seek(off)
            fields = [_read_cstr(f) for _ in range(4)]
            if any(x is None for x in fields) or not all(_printable(x) for x in fields) \
                    or not fields[3].isdigit():
                break
            name, rel, ver, sz = (x.decode("ascii") for x in fields)
            n = int(sz)
            data = f.tell()
            if data + n > size:
                break
            magic = f.read(8).hex()
            entries.append(OverlayEntry(name, rel, ver, n, off, data, magic))
            off = data + n
        info.update({"file_size": size, "overlay_start": start, "overlay_end_parsed": off,
                     "overlay_fully_parsed": off == size})
    return entries, info


def carve(path: str, entries: list[OverlayEntry], out_dir: str, skip_larger_than: int | None = None):
    """Copy each overlay entry to out_dir/relpath, returning [(entry, out_path|None, md5, sha256)]."""
    results = []
    with open(path, "rb") as f:
        for e in entries:
            md5 = hashlib.md5()
            sha = hashlib.sha256()
            f.seek(e.data_offset)
            left = e.size
            dest = None
            if skip_larger_than is None or e.size <= skip_larger_than:
                dest = os.path.join(out_dir, *e.relpath.split("\\"))
                os.makedirs(os.path.dirname(dest), exist_ok=True)
            out = open(dest, "wb") if dest else None
            try:
                while left:
                    chunk = f.read(min(left, 8 << 20))
                    if not chunk:
                        raise IOError(f"short read in {e.name}")
                    md5.update(chunk)
                    sha.update(chunk)
                    if out:
                        out.write(chunk)
                    left -= len(chunk)
            finally:
                if out:
                    out.close()
            results.append((e, dest, md5.hexdigest(), sha.hexdigest()))
    return results


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    path = argv[1]
    entries, info = parse_overlay(path)
    print(f"file size        {info['file_size']:,}")
    print(f"overlay start    0x{info['overlay_start']:X}")
    print(f"overlay parsed   0x{info['overlay_end_parsed']:X} (complete={info['overlay_fully_parsed']})")
    print(f"authenticode dir {info['authenticode_dir']}")
    for e in entries:
        print(f"  0x{e.data_offset:08X} {e.size:>12,}  {e.relpath:<22} ver={e.version:<12} magic={e.magic}")
    if "--carve" in argv:
        out_dir = argv[argv.index("--carve") + 1]
        lim = None
        if "--skip-larger-than" in argv:
            lim = int(argv[argv.index("--skip-larger-than") + 1])
        res = carve(path, entries, out_dir, lim)
        manifest = []
        for e, dest, md5, sha in res:
            print(f"  {'carved ' if dest else 'skipped'} {e.relpath:<22} md5={md5} sha256={sha}")
            d = asdict(e)
            d.update({"carved_to": dest, "md5": md5, "sha256": sha})
            manifest.append(d)
        with open(os.path.join(out_dir, "overlay_manifest.json"), "w", encoding="utf-8") as fh:
            json.dump({"source": path, "info": info, "entries": manifest}, fh, indent=2)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
