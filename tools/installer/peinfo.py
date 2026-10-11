"""
peinfo.py - stdlib-only static PE identification (never loads or executes the file).

Reports: size, MD5/SHA-256, machine, PE32/PE32+, TimeDateStamp, linker version, Authenticode
directory presence, VS_FIXEDFILEINFO + StringFileInfo (FileVersion, ProductVersion, CompanyName,
FileDescription, OriginalFilename, Comments, ...), and the CodeView debug record
(RSDS: GUID + age + PDB path, or NB10: signature + age + path) with the symbol-server key.

Usage:
    python -I peinfo.py <file> [<file> ...] [--json]
"""
from __future__ import annotations

import datetime
import hashlib
import json
import os
import struct
import sys
import uuid


def _u16(b, o): return struct.unpack_from("<H", b, o)[0]
def _u32(b, o): return struct.unpack_from("<I", b, o)[0]


class PEFile:
    def __init__(self, data: bytes):
        self.d = data
        if data[:2] != b"MZ":
            raise ValueError("not MZ")
        pe = _u32(data, 0x3C)
        if data[pe:pe + 4] != b"PE\0\0":
            raise ValueError("no PE header")
        self.machine, self.nsec, self.timestamp, _, _, self.optsz, self.chars = \
            struct.unpack_from("<HHIIIHH", data, pe + 4)
        o = pe + 24
        self.magic = _u16(data, o)
        self.linker = (data[o + 2], data[o + 3])
        if self.magic == 0x10B:
            self.image_base = _u32(data, o + 28)
            nrva_off = o + 92
        elif self.magic == 0x20B:
            self.image_base = struct.unpack_from("<Q", data, o + 24)[0]
            nrva_off = o + 108
        else:
            raise ValueError(f"unknown optional header magic 0x{self.magic:X}")
        self.dll_chars = _u16(data, o + 70)
        n = _u32(data, nrva_off)
        self.dirs = [struct.unpack_from("<II", data, nrva_off + 4 + 8 * i) for i in range(min(n, 16))]
        sh = o + self.optsz
        self.section_table_offset = sh
        self.sections = []
        for i in range(self.nsec):
            name, vs, va, rs, rp = struct.unpack_from("<8sIIII", data, sh + 40 * i)
            self.sections.append((name.rstrip(b"\0").decode("ascii", "replace"), va, vs, rp, rs))

    def section_table_bytes(self) -> bytes:
        return self.d[self.section_table_offset:self.section_table_offset + 40 * self.nsec]

    def rva2off(self, rva):
        for _, va, vs, rp, rs in self.sections:
            if va <= rva < va + max(vs, rs):
                return rva - va + rp
        return None

    # -- debug directory -------------------------------------------------------------------
    def codeview(self):
        out = []
        if len(self.dirs) <= 6:
            return out
        rva, size = self.dirs[6]
        if not rva:
            return out
        off = self.rva2off(rva)
        for i in range(size // 28):
            _ch, ts, _maj, _min, typ, sz, _addr, ptr = struct.unpack_from("<IIHHIIII", self.d, off + 28 * i)
            if typ != 2:
                out.append({"type": typ, "timestamp": ts})
                continue
            blob = self.d[ptr:ptr + sz]
            if blob[:4] == b"RSDS":
                g = uuid.UUID(bytes_le=bytes(blob[4:20]))
                age = _u32(blob, 20)
                path = blob[24:].split(b"\0", 1)[0].decode("cp1252", "replace")
                out.append({"type": 2, "format": "RSDS", "guid": str(g), "age": age, "pdb_path": path,
                            "symbol_key": g.hex.upper() + f"{age:X}", "timestamp": ts})
            elif blob[:4] == b"NB10":
                sig, age = struct.unpack_from("<II", blob, 8)
                path = blob[16:].split(b"\0", 1)[0].decode("cp1252", "replace")
                out.append({"type": 2, "format": "NB10", "signature": f"0x{sig:08X}", "age": age,
                            "pdb_path": path, "symbol_key": f"{sig:08X}{age:X}", "timestamp": ts})
            else:
                out.append({"type": 2, "format": blob[:4].hex()})
        return out

    # -- version resource ------------------------------------------------------------------
    def _find_version_resource(self):
        if len(self.dirs) <= 2 or not self.dirs[2][0]:
            return None
        base = self.rva2off(self.dirs[2][0])
        d = self.d

        def entries(off):
            nnamed, nid = _u16(d, off + 12), _u16(d, off + 14)
            for i in range(nnamed + nid):
                name, target = struct.unpack_from("<II", d, off + 16 + 8 * i)
                yield name, target

        for name, target in entries(base):
            if name == 16 and target & 0x80000000:          # RT_VERSION
                lvl2 = base + (target & 0x7FFFFFFF)
                for _, t2 in entries(lvl2):
                    if t2 & 0x80000000:
                        lvl3 = base + (t2 & 0x7FFFFFFF)
                        for _, t3 in entries(lvl3):
                            if not t3 & 0x80000000:
                                rva, size = struct.unpack_from("<II", d, base + t3)
                                o = self.rva2off(rva)
                                return d[o:o + size]
        return None

    def version_info(self):
        blob = self._find_version_resource()
        if not blob:
            return None
        res = {"strings": {}}

        def walk(off, end, depth):
            while off + 6 <= end:
                wlen, vlen, wtype = struct.unpack_from("<HHH", blob, off)
                if wlen == 0:
                    break
                k = off + 6
                kend = k
                while blob[kend:kend + 2] != b"\0\0":
                    kend += 2
                key = blob[k:kend].decode("utf-16-le", "replace")
                v = (kend + 2 + 3) & ~3
                if key == "VS_VERSION_INFO" and vlen >= 52:
                    ffi = struct.unpack_from("<13I", blob, v)
                    if ffi[0] == 0xFEEF04BD:
                        fv = (ffi[2] >> 16, ffi[2] & 0xFFFF, ffi[3] >> 16, ffi[3] & 0xFFFF)
                        pv = (ffi[4] >> 16, ffi[4] & 0xFFFF, ffi[5] >> 16, ffi[5] & 0xFFFF)
                        res["fixed_file_version"] = ".".join(map(str, fv))
                        res["fixed_product_version"] = ".".join(map(str, pv))
                    walk((v + vlen + 3) & ~3, off + wlen, depth + 1)
                elif key in ("StringFileInfo",) or (depth >= 1 and len(key) == 8 and wtype == 1 and vlen == 0
                                                     and all(c in "0123456789abcdefABCDEF" for c in key)):
                    walk(v, off + wlen, depth + 1)
                elif key == "VarFileInfo":
                    pass
                elif wtype == 1 and depth >= 2:
                    val = blob[v:v + 2 * vlen].decode("utf-16-le", "replace").split("\0", 1)[0]
                    res["strings"][key] = val
                off = (off + wlen + 3) & ~3

        walk(0, len(blob), 0)
        return res


def describe(path: str) -> dict:
    with open(path, "rb") as f:
        data = f.read()
    pe = PEFile(data)
    ts = datetime.datetime.fromtimestamp(pe.timestamp, datetime.timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")
    return {
        "path": path, "size": len(data),
        "md5": hashlib.md5(data).hexdigest(), "sha256": hashlib.sha256(data).hexdigest(),
        "machine": f"0x{pe.machine:04X}", "pe_format": "PE32" if pe.magic == 0x10B else "PE32+",
        "timedatestamp": f"0x{pe.timestamp:08X} ({ts})", "linker": f"{pe.linker[0]}.{pe.linker[1]}",
        "image_base": f"0x{pe.image_base:X}", "dll_characteristics": f"0x{pe.dll_chars:04X}",
        "authenticode_present": bool(len(pe.dirs) > 4 and pe.dirs[4][0]),
        "version": pe.version_info(), "codeview": pe.codeview(),
        "sections": [s[0] for s in pe.sections],
    }


def main(argv):
    paths = [a for a in argv[1:] if not a.startswith("--")]
    if not paths:
        print(__doc__)
        return 2
    res = []
    for p in paths:
        try:
            res.append(describe(p))
        except Exception as e:  # noqa: BLE001
            res.append({"path": p, "error": str(e)})
    if "--json" in argv:
        print(json.dumps(res, indent=1))
    else:
        for r in res:
            if "error" in r:
                print(f"{r['path']}: {r['error']}")
                continue
            v = r["version"] or {}
            s = v.get("strings", {})
            cv = r["codeview"][0] if r["codeview"] else {}
            print(f"{os.path.basename(r['path'])}: {r['size']:,} B md5={r['md5']} ts={r['timedatestamp']} "
                  f"linker={r['linker']} FileVersion={s.get('FileVersion')} fixed={v.get('fixed_file_version')} "
                  f"Company={s.get('CompanyName')!r} Desc={s.get('FileDescription')!r} "
                  f"CV={cv.get('format')} {cv.get('guid') or cv.get('signature')} age={cv.get('age')} {cv.get('pdb_path')}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
