"""
pdbinfo.py - stdlib-only MSF 7.00 / PDB identification and build matching.

Reports: MSF superblock (block size, block count, directory size), stream count/sizes,
PDB info stream (#1: version, signature/timestamp, age, GUID -> symbol-server key),
DBI stream (#3: version, age, machine, build/pdb-dll numbers, module count + object paths),
the section-header stream referenced by the DBI optional debug header, and build-path strings.

With --exe, compares against a PE's CodeView RSDS (GUID + age) and its section table
(byte-for-byte vs the PDB section-header stream) - the latter is an independent structural match.

Usage:
    python -I pdbinfo.py <file.pdb> [--exe <file.exe> ...] [--json]
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import struct
import sys
import uuid

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import peinfo  # noqa: E402

MSF7 = b"Microsoft C/C++ MSF 7.00\r\n\x1aDS\0\0\0"
MSF2 = b"Microsoft C/C++ program database 2.00\r\n\x1aJG\0\0"
PDB_VERSIONS = {19941610: "VC2", 19950623: "VC4", 19950814: "VC41", 19960307: "VC50",
                19970604: "VC98", 19990604: "VC70Dep", 20000404: "VC70", 20030901: "VC80",
                20091201: "VC110", 20140508: "VC140"}
DBG_HDR_NAMES = ["FPO", "Exception", "Fixup", "OmapToSrc", "OmapFromSrc", "SectionHdr", "TokenRidMap",
                 "Xdata", "Pdata", "NewFPO", "SectionHdrOrig"]


class MSF:
    def __init__(self, path):
        self.f = open(path, "rb")
        sb = self.f.read(56)
        if sb[:32] != MSF7:
            if sb[:len(MSF2)] == MSF2:
                raise NotImplementedError("PDB 2.00 (JG) format")
            raise ValueError("not an MSF 7.00 file")
        self.bs, self.fpm, self.nblocks, self.dir_bytes, _, self.block_map = struct.unpack_from("<6I", sb, 32)
        nd = (self.dir_bytes + self.bs - 1) // self.bs
        self.f.seek(self.block_map * self.bs)
        dir_blocks = struct.unpack(f"<{nd}I", self.f.read(4 * nd))
        d = b"".join(self._block(b) for b in dir_blocks)[:self.dir_bytes]
        n = _u32(d, 0)
        self.sizes = list(struct.unpack_from(f"<{n}I", d, 4))
        p = 4 + 4 * n
        self.blocks = []
        for s in self.sizes:
            k = 0 if s in (0, 0xFFFFFFFF) else (s + self.bs - 1) // self.bs
            self.blocks.append(struct.unpack_from(f"<{k}I", d, p))
            p += 4 * k

    def _block(self, i):
        self.f.seek(i * self.bs)
        return self.f.read(self.bs)

    def stream(self, i) -> bytes:
        s = self.sizes[i]
        if s in (0, 0xFFFFFFFF):
            return b""
        return b"".join(self._block(b) for b in self.blocks[i])[:s]

    def close(self):
        self.f.close()


def _u32(b, o): return struct.unpack_from("<I", b, o)[0]


def analyze(path):
    m = MSF(path)
    r = {"path": path, "size": os.path.getsize(path)}
    with open(path, "rb") as f:
        h_md5, h_sha = hashlib.md5(), hashlib.sha256()
        for blk in iter(lambda: f.read(8 << 20), b""):
            h_md5.update(blk)
            h_sha.update(blk)
    r.update({"md5": h_md5.hexdigest(), "sha256": h_sha.hexdigest()})
    r["msf"] = {"signature": "Microsoft C/C++ MSF 7.00", "block_size": m.bs, "num_blocks": m.nblocks,
                "file_size_by_blocks": m.bs * m.nblocks, "directory_bytes": m.dir_bytes,
                "num_streams": len(m.sizes)}
    info = m.stream(1)
    ver, sig, age = struct.unpack_from("<III", info, 0)
    g = uuid.UUID(bytes_le=bytes(info[12:28]))
    r["pdb_info"] = {"version": ver, "version_name": PDB_VERSIONS.get(ver, "?"), "signature": f"0x{sig:08X}",
                     "age": age, "guid": str(g), "symbol_key": g.hex.upper() + f"{age:X}"}
    # named stream map (string buffer of names)
    try:
        nlen = _u32(info, 28)
        names = info[32:32 + nlen].split(b"\0")
        r["pdb_info"]["named_streams"] = [x.decode("ascii", "replace") for x in names if x]
    except Exception:  # noqa: BLE001
        pass
    dbi = m.stream(3)
    if len(dbi) >= 64:
        (vsig, vhdr, dage, gsi, build, psi, pdbdll, symrec, rbld, modsz, secconsz, secmapsz, srcsz, tsmsz,
         mfc, dbgsz, ecsz, flags, machine, _pad) = struct.unpack_from("<iIIHHHHHHiiiiiIiiHHI", dbi, 0)
        r["dbi"] = {"version_header": vhdr, "age": dage, "machine": f"0x{machine:04X}",
                    "toolchain_build": f"{(build >> 8) & 0x7F}.{build & 0xFF}", "pdb_dll_version": pdbdll,
                    "pdb_dll_rbld": rbld, "flags": f"0x{flags:04X}", "global_stream": gsi, "public_stream": psi,
                    "sym_record_stream": symrec}
        # module info substream
        p = 64
        mods = []
        end = 64 + modsz
        while p < end:
            q = p + 64
            e1 = dbi.index(b"\0", q)
            mod = dbi[q:e1].decode("cp1252", "replace")
            e2 = dbi.index(b"\0", e1 + 1)
            obj = dbi[e1 + 1:e2].decode("cp1252", "replace")
            mods.append((mod, obj))
            p = (e2 + 1 + 3) & ~3
        r["dbi"]["module_count"] = len(mods)
        r["dbi"]["module_samples"] = [x[0] for x in mods[:5]] + ["..."] + [x[0] for x in mods[-3:]]
        # build roots from module paths
        roots = {}
        for mod, obj in mods:
            mm = re.match(r"^([A-Za-z]:\\[^\\]+)\\", mod)
            if mm:
                roots[mm.group(1).lower()] = roots.get(mm.group(1).lower(), 0) + 1
        r["dbi"]["module_path_roots"] = dict(sorted(roots.items(), key=lambda kv: -kv[1])[:8])
        dbg_off = 64 + modsz + secconsz + secmapsz + srcsz + tsmsz + ecsz
        n = dbgsz // 2
        idx = struct.unpack_from(f"<{n}H", dbi, dbg_off) if n else ()
        r["dbi"]["optional_debug_streams"] = {DBG_HDR_NAMES[i] if i < len(DBG_HDR_NAMES) else str(i):
                                              (v if v != 0xFFFF else None) for i, v in enumerate(idx)}
        # symbol record stream: count record kinds (S_PUB32 = 0x110E, S_GDATA32 = 0x110D, S_PROCREF = 0x1125 ...)
        if symrec not in (0xFFFF,) and symrec < len(m.sizes):
            sr = m.stream(symrec)
            kinds = {}
            q = 0
            while q + 4 <= len(sr):
                rl, kind = struct.unpack_from("<HH", sr, q)
                kinds[kind] = kinds.get(kind, 0) + 1
                q += 2 + rl
            names = {0x110E: "S_PUB32", 0x110D: "S_GDATA32", 0x110C: "S_LDATA32", 0x1125: "S_PROCREF",
                     0x1127: "S_LPROCREF", 0x1108: "S_UDT", 0x1107: "S_CONSTANT", 0x1126: "S_DATAREF"}
            r["dbi"]["symbol_records"] = {names.get(k, f"0x{k:04X}"): v for k, v in
                                          sorted(kinds.items(), key=lambda kv: -kv[1])[:10]}
        tpi = m.stream(2)
        if len(tpi) >= 16:
            tver, thdr, tbeg, tend = struct.unpack_from("<4I", tpi, 0)
            r["tpi"] = {"version": tver, "type_index_begin": f"0x{tbeg:X}", "type_index_end": f"0x{tend:X}",
                        "type_records": tend - tbeg}
        sh_idx = idx[5] if len(idx) > 5 and idx[5] != 0xFFFF else None
        r["_section_hdr_stream"] = m.stream(sh_idx) if sh_idx is not None else b""
        if r["_section_hdr_stream"]:
            secs = []
            sh = r["_section_hdr_stream"]
            for i in range(len(sh) // 40):
                name, vs, va, rs, rp = struct.unpack_from("<8sIIII", sh, 40 * i)
                secs.append(f"{name.rstrip(bytes(1)).decode('ascii', 'replace')}@0x{va:X}+0x{vs:X}")
            r["dbi"]["pdb_sections"] = secs
    m.close()
    return r


def compare(pdb, exe_path):
    pe = peinfo.describe(exe_path)
    cv = next((c for c in pe["codeview"] if c.get("format") == "RSDS"), None)
    with open(exe_path, "rb") as f:
        pef = peinfo.PEFile(f.read())
    out = {"exe": exe_path, "exe_md5": pe["md5"],
           "exe_file_version": (pe["version"] or {}).get("strings", {}).get("FileVersion"),
           "exe_codeview": cv}
    if cv:
        out["guid_match"] = cv["guid"].lower() == pdb["pdb_info"]["guid"].lower()
        out["age_match_pdbinfo"] = cv["age"] == pdb["pdb_info"]["age"]
        out["age_match_dbi"] = cv["age"] == pdb.get("dbi", {}).get("age")
    shs = pdb.get("_section_hdr_stream", b"")
    out["section_table_identical"] = bool(shs) and shs == pef.section_table_bytes()
    out["MATCH"] = bool(out.get("guid_match") and out.get("age_match_pdbinfo"))
    return out


def main(argv):
    args = [a for a in argv[1:]]
    if not args:
        print(__doc__)
        return 2
    pdb_path = args[0]
    exes = []
    i = 1
    while i < len(args):
        if args[i] == "--exe":
            exes.append(args[i + 1])
            i += 2
        else:
            i += 1
    r = analyze(pdb_path)
    r["comparisons"] = [compare(r, e) for e in exes]
    r.pop("_section_hdr_stream", None)
    print(json.dumps(r, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
