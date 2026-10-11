"""
datcheck.py - validate an AC1 DatDatabase (e.g. client_highres.dat) before it is trusted/copied.

Checks (stdlib only, read-only):
  * DiskFileInfo header @0x140: magic 0x5442, block_size, file_size == on-disk size, data_set/subset,
    free list, B-tree root, pack versions, version_major/minor.
  * Full B-tree walk: node/leaf counts, depth, duplicate IDs, global in-order key ordering,
    entry offsets block-aligned and inside the file, flags histogram (compressed entries).
  * Reads EVERY record through its block chain (chain integrity, no free-bit blocks in use).
  * Iteration record 0xFFFF0001.
  * RenderSurface (0x06) records: header id == directory id, pixel length matches format math,
    bytes consumed == record size.
  * Optional --portal <client_portal.dat> [...]: SurfaceTexture (0x05) reference coverage, i.e. how
    many first-choice RenderSurface ids missing from the portal are supplied by this dat.

Usage:
    python -I datcheck.py <file.dat> [--portal <client_portal.dat>]... [--json out.json]
"""
from __future__ import annotations

import collections
import datetime
import json
import os
import struct
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from acdat import DatFile, Reader, NODE_SIZE  # noqa: E402

PIXEL_FORMATS = {
    20: "R8G8B8", 21: "A8R8G8B8", 22: "X8R8G8B8", 23: "R5G6B5", 24: "X1R5G5B5", 25: "A1R5G5B5",
    26: "A4R4G4B4", 28: "A8", 41: "P8", 50: "L8", 101: "INDEX16", 243: "CUSTOM_LSCAPE_R8G8B8",
    244: "CUSTOM_LSCAPE_ALPHA", 500: "CUSTOM_RAW_JPEG",
    0x31545844: "DXT1", 0x33545844: "DXT3", 0x35545844: "DXT5",
}
BPP = {"R8G8B8": 24, "A8R8G8B8": 32, "X8R8G8B8": 32, "R5G6B5": 16, "X1R5G5B5": 16, "A1R5G5B5": 16,
       "A4R4G4B4": 16, "A8": 8, "P8": 8, "L8": 8, "INDEX16": 16, "CUSTOM_LSCAPE_R8G8B8": 24,
       "CUSTOM_LSCAPE_ALPHA": 8}
TYPE_NAMES = {0x01: "GfxObj", 0x02: "Setup", 0x03: "Animation", 0x04: "Palette", 0x05: "SurfaceTexture",
              0x06: "RenderSurface", 0x08: "Surface", 0x09: "MotionTable", 0x0A: "Wave", 0x0D: "Environment",
              0x0E: "tables", 0x0F: "PaletteSet", 0x10: "Clothing", 0x11: "GfxObjDegradeInfo", 0x12: "Scene",
              0x13: "Region", 0x14: "KeyMap", 0x15: "RenderTexture", 0x16: "RenderMaterial",
              0x17: "MaterialModifier", 0x18: "MaterialInstance", 0x20: "SoundTable", 0x22: "EnumMapper",
              0x25: "DidMapper", 0x27: "DualDidMapper", 0x30: "CombatTable", 0x31: "String",
              0x32: "ParticleEmitter", 0x33: "PhysicsScript", 0x34: "PhysicsScriptTable", 0x40: "Font",
              0xFF: "Iteration"}


def expected_pixel_len(fmt, w, h):
    if fmt in ("DXT1", "DXT3", "DXT5"):
        return max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * (8 if fmt == "DXT1" else 16)
    if fmt in BPP:
        return w * h * BPP[fmt] // 8
    return None


def parse_iteration(data: bytes):
    r = Reader(data)
    total = r.i32()
    ranges = []
    n = total
    while n > 0 and r.remaining() >= 8:
        consecutive = r.i32()
        start = r.i32()
        ranges.append((start, consecutive))
        n += consecutive
    return total, ranges


def walk_inorder(d: DatFile):
    """Recursive in-order walk; returns (ordered ids, stats)."""
    ids = []
    st = {"nodes": 0, "leaves": 0, "max_depth": 0}

    def visit(off, depth):
        kids, ents = d.read_node(off)
        st["nodes"] += 1
        st["max_depth"] = max(st["max_depth"], depth)
        if not kids:
            st["leaves"] += 1
            ids.extend(e.id for e in ents)
            return
        for i, e in enumerate(ents):
            visit(kids[i], depth + 1)
            ids.append(e.id)
        visit(kids[len(ents)], depth + 1)

    visit(d.header.btree_root, 0)
    return ids, st


def check(path, portals=()):
    t0 = time.time()
    rep = {"path": path, "disk_size": os.path.getsize(path)}
    d = DatFile(path)
    h = d.header
    rep["header"] = {
        "magic": f"0x{h.magic:08X}", "block_size": h.block_size, "file_size": h.file_size,
        "file_size_matches_disk": h.file_size == rep["disk_size"], "data_set": h.data_set,
        "data_subset": h.data_subset, "free_head": f"0x{h.free_head:08X}", "free_tail": f"0x{h.free_tail:08X}",
        "free_count": h.free_count, "btree_root": f"0x{h.btree_root:08X}",
        "master_map_id": f"0x{h.master_map_id:08X}", "engine_pack_version": h.engine_pack_version,
        "game_pack_version": h.game_pack_version, "version_major": h.version_major.hex(),
        "version_minor": h.version_minor, "version_string": h.version_string, "use_lru": f"0x{h.use_lru:08X}",
    }
    ents = d.entries()
    ordered, st = walk_inorder(d)
    ws = d.walk_stats
    rep["btree"] = {"files": len(ents), "nodes": ws.nodes, "leaf_nodes": ws.leaf_nodes,
                    "levels": ws.max_depth + 1, "duplicate_ids": ws.duplicate_ids,
                    "inorder_strictly_increasing": all(a < b for a, b in zip(ordered, ordered[1:])),
                    "inorder_count_matches": len(ordered) == len(ents), "node_size": NODE_SIZE}
    bs = h.block_size
    misaligned = sum(1 for e in ents.values() if e.offset % bs)
    out_of_range = sum(1 for e in ents.values() if e.offset + 4 > h.file_size)
    flags = collections.Counter(e.flags for e in ents.values())
    versions = collections.Counter(e.version for e in ents.values())
    dates = [e.date for e in ents.values() if e.date]
    rep["entries"] = {"misaligned_offsets": misaligned, "out_of_range_offsets": out_of_range,
                      "flags_histogram": {f"0x{k:04X}": v for k, v in sorted(flags.items())},
                      "version_histogram": dict(sorted(versions.items())),
                      "date_min": datetime.datetime.fromtimestamp(min(dates), datetime.timezone.utc).date().isoformat() if dates else None,
                      "date_max": datetime.datetime.fromtimestamp(max(dates), datetime.timezone.utc).date().isoformat() if dates else None}
    by_type = collections.defaultdict(lambda: [0, 0])
    is_cell = h.data_set == 2
    for e in ents.values():
        if e.id == 0xFFFF0001:
            t = "0xFF Iteration"
        elif is_cell:
            lo = e.id & 0xFFFF
            t = "LandBlock xxyyFFFF" if lo == 0xFFFF else "LandBlockInfo xxyyFFFE" if lo == 0xFFFE else "EnvCell xxyy01nn+"
        else:
            t = f"0x{e.id >> 24:02X} {TYPE_NAMES.get(e.id >> 24, '?')}"
        by_type[t][0] += 1
        by_type[t][1] += e.size
    rep["by_type"] = {t: {"count": c, "bytes": b} for t, (c, b) in sorted(by_type.items())}
    # read every record
    d.chain_highbit_seen = 0
    read_errors = []
    rs_stats = collections.Counter()
    rs_bad = []
    sizes = collections.Counter()
    total_bytes = 0
    for oid in ordered:
        e = ents[oid]
        try:
            data = d.read_entry(e)
        except Exception as ex:  # noqa: BLE001
            read_errors.append(f"0x{oid:08X}: {ex}")
            continue
        total_bytes += len(data)
        if not is_cell and oid >> 24 == 0x06:
            r = Reader(data)
            rid, _cat, w, hh, fmtid, n = r.u32(), r.u32(), r.i32(), r.i32(), r.u32(), r.i32()
            fmt = PIXEL_FORMATS.get(fmtid, f"0x{fmtid:X}")
            consumed = 24 + n + (4 if fmt in ("P8", "INDEX16") else 0)
            exp = expected_pixel_len(fmt, w, hh)
            ok = rid == oid and consumed == len(data) and (exp is None or exp == n)
            rs_stats[fmt] += 1
            sizes[f"{w}x{hh}"] += 1
            if not ok:
                rs_bad.append(f"0x{oid:08X} fmt={fmt} {w}x{hh} len={n} exp={exp} consumed={consumed}/{len(data)} id=0x{rid:08X}")
    rep["records"] = {"read_ok": len(ordered) - len(read_errors), "read_errors": read_errors[:20],
                      "bytes_read": total_bytes, "free_bit_blocks_in_chains": d.chain_highbit_seen}
    if rs_stats:
        rep["render_surfaces"] = {"formats": dict(rs_stats.most_common()), "top_sizes": dict(sizes.most_common(10)),
                                  "bad": len(rs_bad), "bad_samples": rs_bad[:10]}
    if 0xFFFF0001 in ents:
        total, ranges = parse_iteration(d.read(0xFFFF0001))
        rep["iteration"] = {"total": total, "ranges(start,consecutive)": ranges}
    hr_ids = set(ents)
    d.close()
    # coverage vs portal(s)
    cov = []
    for pp in portals:
        p = DatFile(pp)
        pe = p.entries()
        st_ids = [i for i in pe if i >> 24 == 0x05]
        first_missing = first_missing_in_hr = refs = refs_in_portal = refs_in_hr = refs_none = 0
        unresolved_st = 0
        for sid in st_ids:
            r = Reader(p.read(sid))
            r.u32(); r.i32(); r.u8()
            n = r.i32()
            lst = [r.u32() for _ in range(n)]
            refs += len(lst)
            any_ok = False
            for x in lst:
                inp, inh = x in pe, x in hr_ids
                refs_in_portal += inp
                refs_in_hr += inh
                refs_none += not (inp or inh)
                any_ok |= inp or inh
            unresolved_st += not any_ok
            if lst and lst[0] not in pe:
                first_missing += 1
                first_missing_in_hr += lst[0] in hr_ids
        rs_portal = {i for i in pe if i >> 24 == 0x06}
        cov.append({"portal": pp, "portal_iteration": parse_iteration(p.read(0xFFFF0001))[0] if 0xFFFF0001 in pe else None,
                    "surface_textures": len(st_ids), "refs": refs, "refs_in_portal": refs_in_portal,
                    "refs_in_highres": refs_in_hr, "refs_in_neither": refs_none,
                    "first_ref_missing_from_portal": first_missing,
                    "of_those_found_in_highres": first_missing_in_hr,
                    "surface_textures_with_no_resolvable_ref": unresolved_st,
                    "id_overlap_portal_highres": len(hr_ids & set(pe)),
                    "highres_06_ids_not_referenced_by_any_portal_05": None,
                    "_rs_portal_count": len(rs_portal)})
        # how many highres textures are referenced at all
        referenced = set()
        for sid in st_ids:
            r = Reader(p.read(sid))
            r.u32(); r.i32(); r.u8()
            n = r.i32()
            referenced.update(r.u32() for _ in range(n))
        cov[-1]["highres_06_ids_not_referenced_by_any_portal_05"] = len({i for i in hr_ids if i >> 24 == 0x06} - referenced)
        p.close()
    if cov:
        rep["portal_coverage"] = cov
    rep["seconds"] = round(time.time() - t0, 1)
    rep["VERDICT"] = ("PASS" if (h.magic == 0x5442 and rep["header"]["file_size_matches_disk"]
                                 and ws.duplicate_ids == 0 and rep["btree"]["inorder_strictly_increasing"]
                                 and misaligned == 0 and out_of_range == 0 and not read_errors
                                 and not rs_bad) else "FAIL")
    return rep


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    portals = [argv[i + 1] for i, a in enumerate(argv) if a == "--portal"]
    rep = check(argv[1], portals)
    s = json.dumps(rep, indent=1)
    print(s)
    if "--json" in argv:
        with open(argv[argv.index("--json") + 1], "w", encoding="utf-8") as fh:
            fh.write(s)
    return 0 if rep["VERDICT"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
