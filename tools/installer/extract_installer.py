"""
extract_installer.py - unpack ac1install.exe (InstallShield 10 setup launcher + embedded Disk1)
WITHOUT running it. Stdlib only; reads the exe in place.

Stages
  1. PE overlay map  -> <out>/Disk1/*            (isoverlay.py; data2.cab is left in place by default)
  2. ISc( cabinet    -> <out>/files/<FileGroup>/<dir>/<name>   (iscab.py, MD5-verified per file)
  3. setup.ibt       -> <out>/setup_ibt/*        (ibt.py; SZDD-expanded in Python)
  4. engine32.cab    -> catalogued from its CFFILE table; with --expand-engine the carved cab is
                        expanded by %SystemRoot%\\System32\\expand.exe (LZX, in-box, no elevation)
  5. <out>/manifest.json + <out>/catalog.tsv

Re-running is incremental: files already present with the recorded size + MD5 are skipped.

Usage:
    python -I extract_installer.py [--exe <repo>\\ac1install.exe] [--out <repo>\\install]
                                   [--only name1,name2] [--expand-engine] [--carve-all]

The defaults are in the repository root (the folder that holds tools\\); both paths are gitignored.
"""
from __future__ import annotations

import hashlib
import json
import os
import sys
import time
from dataclasses import asdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ibt        # noqa: E402
import iscab      # noqa: E402
import isoverlay  # noqa: E402
import mscab      # noqa: E402

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_EXE = os.path.join(REPO_ROOT, "ac1install.exe")
DEFAULT_OUT = os.path.join(REPO_ROOT, "install")


def _arg(argv, name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


def _hash_file(path):
    md5, sha = hashlib.md5(), hashlib.sha256()
    with open(path, "rb") as f:
        while True:
            b = f.read(8 << 20)
            if not b:
                break
            md5.update(b)
            sha.update(b)
    return md5.hexdigest(), sha.hexdigest()


def main(argv):
    exe = _arg(argv, "--exe", DEFAULT_EXE)
    out = _arg(argv, "--out", DEFAULT_OUT)
    only = _arg(argv, "--only")
    only = {x.strip().lower() for x in only.split(",")} if only else None
    os.makedirs(out, exist_ok=True)
    t0 = time.time()
    manifest = {"source": exe, "source_size": os.path.getsize(exe), "tool": "tools/installer/extract_installer.py"}

    # 1 -- overlay -----------------------------------------------------------------------------
    entries, info = isoverlay.parse_overlay(exe)
    lim = None if "--carve-all" in argv else 64 << 20
    carved = isoverlay.carve(exe, entries, out, lim)  # relpaths already start with Disk1\
    manifest["pe_overlay"] = {"info": info, "entries": [
        dict(asdict(e), carved_to=dest, md5=md5, sha256=sha) for e, dest, md5, sha in carved]}
    print(f"[1] overlay: {len(entries)} entries, fully parsed={info['overlay_fully_parsed']}")
    by_name = {e.name.lower(): e for e in entries}

    # 2 -- InstallShield cabinet ------------------------------------------------------------------
    vols = iscab.OverlayVolumes(exe, entries)
    cab = iscab.ISCabinet(vols)
    desc = iscab.describe(cab)
    desc["volumes"] = {i: cab.volume_header(i) for i in (1, 2) if f"data{i}.cab" in by_name}
    files_out = []
    for fd in cab.files:
        rec = {k: v for k, v in asdict(fd).items()}
        rec["valid"] = bool(fd.valid)
        placeholder = bool(fd.valid and fd.expanded_size == 0 and fd.file_group.startswith("<Engine>"))
        if placeholder:
            rec["note"] = "zero-length <Engine> placeholder (real file ships in engine32.cab)"
        elif fd.valid and (only is None or fd.name.lower() in only):
            rel = cab.out_relpath(fd)
            dest = os.path.join(out, "files", rel)
            rec["relpath"] = os.path.join("files", rel)
            if os.path.exists(dest) and os.path.getsize(dest) == fd.expanded_size and \
                    _hash_file(dest)[0] == fd.md5:
                md5, sha = _hash_file(dest)
                rec.update({"size": fd.expanded_size, "md5": md5, "sha256": sha,
                            "md5_verified": True, "skipped_existing": True})
                print(f"[2] {fd.index:4} exists, MD5 OK  {rel}")
            else:
                t1 = time.time()
                r = cab.extract(fd.index, dest)
                rec.update(r)
                rec["seconds"] = round(time.time() - t1, 2)
                print(f"[2] {fd.index:4} {r['size']:>12,}  MD5 OK  {rel}  ({rec['seconds']}s)", flush=True)
        elif not fd.valid:
            rec["note"] = ("external to cabinet (ships loose in Disk1 / PE overlay)" if fd.data_offset == 0 and fd.name
                           else "FILE_INVALID entry with no name/data" if fd.flags & iscab.FILE_INVALID
                           else "invalid entry")
        files_out.append(rec)
    desc["files"] = files_out
    manifest["installshield_cab"] = desc
    vols.close()

    # 3 -- setup.ibt ------------------------------------------------------------------------------
    if "setup.ibt" in by_name:
        e = by_name["setup.ibt"]
        with open(exe, "rb") as f:
            f.seek(e.data_offset)
            blob = f.read(e.size)
        manifest["setup_ibt"] = ibt.unpack(blob, os.path.join(out, "setup_ibt"))
        print(f"[3] setup.ibt: {len(manifest['setup_ibt'])} entries")

    # 4 -- engine32.cab ---------------------------------------------------------------------------
    if "engine32.cab" in by_name:
        e = by_name["engine32.cab"]
        with open(exe, "rb") as f:
            einfo = mscab.parse(f, e.data_offset)
        eng = {"mscf": einfo, "expanded": False}
        if "--expand-engine" in argv:
            cab_path = os.path.join(out, "Disk1", "engine32.cab")
            dest = os.path.join(out, "engine32")
            tool, rc, so, se = mscab.expand_with_windows(cab_path, dest)
            eng.update({"expand_tool": tool, "expand_rc": rc, "expand_stdout": so[-2000:], "expand_stderr": se[-2000:]})
            got = []
            for fe in einfo["files"]:
                p = os.path.join(dest, fe["name"])
                ok = os.path.exists(p) and os.path.getsize(p) == fe["size"]
                md5, sha = _hash_file(p) if os.path.exists(p) else (None, None)
                got.append({"name": fe["name"], "size_ok": ok, "md5": md5, "sha256": sha})
            eng["expanded"] = all(g["size_ok"] for g in got)
            eng["expanded_files"] = got
        manifest["engine32_cab"] = eng
        print(f"[4] engine32.cab: {len(einfo['files'])} files, {einfo['folders']}, expanded={eng['expanded']}")

    # 5 -- outputs ----------------------------------------------------------------------------------
    manifest["seconds"] = round(time.time() - t0, 1)
    with open(os.path.join(out, "manifest.json"), "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=1, default=str)
    with open(os.path.join(out, "catalog.tsv"), "w", encoding="utf-8") as fh:
        fh.write("source\tindex\tfile_group\tdirectory\tname\tflags\texpanded_size\tcompressed_size\tvolume\tmd5\tsha256\toutput\tnote\n")
        for r in files_out:
            fh.write("\t".join(str(x) for x in (
                "data1.hdr/data*.cab", r["index"], r["file_group"], r["directory"], r["name"], f"0x{r['flags']:04X}",
                r["expanded_size"], r["compressed_size"], r["volume"], r.get("md5") or r["md5"],
                r.get("sha256", ""), r.get("relpath", ""), r.get("note", ""))) + "\n")
        for d in manifest["pe_overlay"]["entries"]:
            fh.write("\t".join(str(x) for x in (
                "overlay", "", "", "Disk1", d["name"], "", d["size"], d["size"], "", d["md5"], d["sha256"],
                d["carved_to"] or f"(in place at exe offset 0x{d['data_offset']:X})", f"version={d['version']}")) + "\n")
        for d in manifest.get("setup_ibt", []):
            fh.write("\t".join(str(x) for x in (
                "setup.ibt", "", "", "", d["target_name"], d["compression"], d["expanded_size"], d["stored_size"],
                "", d["md5"], d["sha256"], d.get("written_to", ""), f"version={d['version']}")) + "\n")
        eng = manifest.get("engine32_cab")
        if eng:
            ex = {g["name"]: g for g in eng.get("expanded_files", [])}
            for fe in eng["mscf"]["files"]:
                g = ex.get(fe["name"], {})
                fh.write("\t".join(str(x) for x in (
                    "engine32.cab", "", "", "", fe["name"], eng["mscf"]["folders"][fe["folder"]]["method"], fe["size"], "",
                    "", g.get("md5", ""), g.get("sha256", ""), "engine32\\" + fe["name"] if g.get("size_ok") else "",
                    fe["datetime"])) + "\n")
    print(f"done in {manifest['seconds']}s -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
