"""
iscab.py - stdlib-only reader/extractor for InstallShield cabinets ("ISc(" data1.hdr + dataN.cab).

Format reference: twogood/unshield (MIT), lib/{cabfile.h,libunshield.c,file.c,file_group.c,
component.c,directory.c,helper.c} at commit 0d1e7cbac7986fbd5aa050da8249a27428fee057 - read as
documentation only; no unshield code or binary is used or executed. Everything here is a fresh
Python implementation of the documented layout.

Supported: header major versions 6..16 (v6+ 0x57-byte file descriptors, 64-byte volume headers),
which covers the InstallShield 9/10 cabinets in ac1install.exe (common.version 0x01009500 -> 9).
Also: per-chunk raw-deflate compression, FILE_OBFUSCATED de-obfuscation, FILE_SPLIT spanning
across volumes, LINK_PREV de-duplicated files, and MD5 verification of every extracted file.

Layout summary (all little-endian, offsets relative to the start of data1.hdr):
    CommonHeader @0  : u32 'ISc(' ; u32 version ; u32 volume_info ; u32 cab_desc_off ; u32 cab_desc_size
    CabDescriptor @cdo:
        +0x0c u32 file_table_offset   +0x14 u32 file_table_size  +0x18 u32 file_table_size2
        +0x1c u32 directory_count     +0x28 u32 file_count       +0x2c u32 file_table_offset2
        +0x3e u32 file_group_offsets[71]   +0x15a u32 component_offsets[71]
    File table @cdo+fto: u32 dir_name_offset[directory_count] (relative to cdo+fto)
    File descriptor i @cdo+fto+fto2+i*0x57 (v6+):
        u16 flags ; u64 expanded ; u64 compressed ; u64 data_offset ; u8 md5[16] ; u8 ?[16] ;
        u32 name_offset ; u16 directory_index ; u8 ?[12] ; u32 link_prev ; u32 link_next ;
        u8 link_flags ; u16 volume
    Volume (dataN.cab): CommonHeader (20 B) + VolumeHeader v6 (64 B): data_offset, first/last file
        index, first/last file offset/expanded/compressed (u32 lo/hi pairs).
    Compressed payload: repeated { u16 n ; u8 raw_deflate[n] } chunks, each an independent raw
        DEFLATE stream (inflateInit2(-15)), each yielding <= 64 KiB.

Usage:
    python -I iscab.py list    <volume-dir>                 (dir holding data1.hdr, data1.cab, data2.cab...)
    python -I iscab.py extract <volume-dir> <out-dir> [--only NAME[,NAME...]] [--manifest path.json]
"""
from __future__ import annotations

import hashlib
import json
import os
import struct
import sys
import time
import zlib
from dataclasses import dataclass, field, asdict

CAB_SIGNATURE = 0x28635349      # 'ISc('
MSCF_SIGNATURE = 0x4643534D
MAX_FILE_GROUP_COUNT = 71
MAX_COMPONENT_COUNT = 71

FILE_SPLIT = 1
FILE_OBFUSCATED = 2
FILE_COMPRESSED = 4
FILE_INVALID = 8
LINK_PREV = 1
LINK_NEXT = 2


# ---------------------------------------------------------------------------
# volume providers: map volume index (1-based) -> (file object, base offset, size)
# ---------------------------------------------------------------------------
class DirVolumes:
    """dataN.hdr / dataN.cab files in a directory (case-insensitive lookup)."""

    def __init__(self, directory: str, prefix: str = "data"):
        self.dir = directory
        self.prefix = prefix
        self._names = {n.lower(): n for n in os.listdir(directory)}
        self._open = {}

    def _path(self, index, suffix):
        n = self._names.get(f"{self.prefix}{index}.{suffix}".lower())
        return os.path.join(self.dir, n) if n else None

    def header_bytes(self) -> bytes:
        p = self._path(1, "hdr") or self._path(1, "cab")
        if not p:
            raise FileNotFoundError(f"no {self.prefix}1.hdr/.cab in {self.dir}")
        with open(p, "rb") as f:
            return f.read()

    def volume(self, index: int):
        if index not in self._open:
            p = self._path(index, "cab")
            if not p:
                raise FileNotFoundError(f"volume {index} ({self.prefix}{index}.cab) missing in {self.dir}")
            self._open[index] = (open(p, "rb"), 0, os.path.getsize(p))
        return self._open[index]

    def close(self):
        for f, _, _ in self._open.values():
            f.close()
        self._open.clear()


class OverlayVolumes:
    """Volumes embedded in a setup.exe overlay (see isoverlay.py) - read in place, no carving."""

    def __init__(self, exe_path: str, entries, prefix: str = "data"):
        self.exe = exe_path
        self.prefix = prefix
        self.by_name = {e.name.lower(): e for e in entries}
        self.f = open(exe_path, "rb")

    def header_bytes(self) -> bytes:
        e = self.by_name.get(f"{self.prefix}1.hdr") or self.by_name.get(f"{self.prefix}1.cab")
        self.f.seek(e.data_offset)
        return self.f.read(e.size)

    def volume(self, index: int):
        e = self.by_name.get(f"{self.prefix}{index}.cab")
        if e is None:
            raise FileNotFoundError(f"volume {index} not in overlay")
        return (self.f, e.data_offset, e.size)

    def close(self):
        self.f.close()


# ---------------------------------------------------------------------------
@dataclass
class FileDesc:
    index: int
    name: str
    directory_index: int
    directory: str
    flags: int
    expanded_size: int
    compressed_size: int
    data_offset: int
    md5: str
    volume: int
    link_previous: int
    link_next: int
    link_flags: int
    unknown_2a: str = ""
    unknown_40: str = ""
    file_group: str = ""

    @property
    def valid(self):
        return not (self.flags & FILE_INVALID) and self.name and self.data_offset


@dataclass
class FileGroup:
    name: str
    first_file: int
    last_file: int
    raw_tail: str = ""


@dataclass
class Component:
    name: str
    file_groups: list = field(default_factory=list)


class ISCabinet:
    def __init__(self, volumes, force_version: int | None = None):
        self.vols = volumes
        self.hdr = volumes.header_bytes()
        h = self.hdr
        sig, self.version, self.volume_info, self.cdo, self.cds = struct.unpack_from("<5I", h, 0)
        if sig != CAB_SIGNATURE:
            raise ValueError("not an InstallShield cabinet header" +
                             (" (it is an MSCF/Microsoft CAB)" if sig == MSCF_SIGNATURE else ""))
        if force_version is not None:
            self.major = force_version
        elif self.version >> 24 == 1:
            self.major = (self.version >> 12) & 0xF
        elif self.version >> 24 in (2, 4):
            self.major = (self.version & 0xFFFF) // 100 if (self.version & 0xFFFF) else 0
        else:
            self.major = 0
        if self.major < 6:
            raise NotImplementedError(f"major version {self.major} (<6) not implemented")
        self.unicode = self.major >= 17
        c = self.cdo
        self.file_table_offset = struct.unpack_from("<I", h, c + 0x0C)[0]
        self.file_table_size, self.file_table_size2, self.directory_count = struct.unpack_from("<3I", h, c + 0x14)
        self.file_count, self.file_table_offset2 = struct.unpack_from("<2I", h, c + 0x28)
        self.file_group_offsets = struct.unpack_from(f"<{MAX_FILE_GROUP_COUNT}I", h, c + 0x3E)
        self.component_offsets = struct.unpack_from(f"<{MAX_COMPONENT_COUNT}I",
                                                    h, c + 0x3E + 4 * MAX_FILE_GROUP_COUNT)
        ft = c + self.file_table_offset
        self.dir_offsets = struct.unpack_from(f"<{self.directory_count}I", h, ft)
        self.directories = [self._str(ft + o) for o in self.dir_offsets]
        self.files = [self._read_fd(i) for i in range(self.file_count)]
        self.file_groups = self._read_offset_lists(self.file_group_offsets, self._read_file_group)
        self.components = self._read_offset_lists(self.component_offsets, self._read_component)
        for g in self.file_groups:
            if g.first_file >= 0:
                for i in range(g.first_file, min(g.last_file, self.file_count - 1) + 1):
                    if not self.files[i].file_group:
                        self.files[i].file_group = g.name

    # -- strings -------------------------------------------------------------
    def _str(self, absoff: int) -> str:
        h = self.hdr
        if self.unicode:
            end = absoff
            while h[end:end + 2] != b"\0\0":
                end += 2
            return h[absoff:end].decode("utf-16-le", "replace")
        end = h.index(b"\0", absoff)
        return h[absoff:end].decode("cp1252", "replace")

    def _hstr(self, rel: int) -> str:
        return self._str(self.cdo + rel) if rel else ""

    # -- descriptors -----------------------------------------------------------
    def _read_fd(self, i: int) -> FileDesc:
        h = self.hdr
        base = self.cdo + self.file_table_offset
        p = base + self.file_table_offset2 + i * 0x57
        flags, exp, comp, doff = struct.unpack_from("<HQQQ", h, p)
        md5 = h[p + 0x1A:p + 0x2A].hex()
        unk2a = h[p + 0x2A:p + 0x3A].hex()
        name_off, dir_idx = struct.unpack_from("<IH", h, p + 0x3A)
        unk40 = h[p + 0x40:p + 0x4C].hex()
        lprev, lnext = struct.unpack_from("<II", h, p + 0x4C)
        lflags = h[p + 0x54]
        vol = struct.unpack_from("<H", h, p + 0x55)[0]
        name = self._str(base + name_off) if name_off else ""
        d = self.directories[dir_idx] if dir_idx < self.directory_count else f"?dir{dir_idx}"
        return FileDesc(i, name, dir_idx, d, flags, exp, comp, doff, md5, vol, lprev, lnext, lflags,
                        unk2a, unk40)

    def _read_offset_lists(self, heads, ctor):
        out = []
        seen = set()
        for head in heads:
            nxt = head
            while nxt and nxt not in seen:
                seen.add(nxt)
                p = self.cdo + nxt
                _name_off, desc_off, nxt = struct.unpack_from("<3I", self.hdr, p)
                out.append(ctor(desc_off))
        return out

    def _read_file_group(self, off) -> FileGroup:
        p = self.cdo + off
        name = self._hstr(struct.unpack_from("<I", self.hdr, p)[0])
        first, last = struct.unpack_from("<ii", self.hdr, p + 4 + 0x12)
        return FileGroup(name, first, last, self.hdr[p + 4:p + 4 + 0x12].hex())

    def _read_component(self, off) -> Component:
        p = self.cdo + off
        name = self._hstr(struct.unpack_from("<I", self.hdr, p)[0])
        q = p + 4 + 0x6B
        n, tbl = struct.unpack_from("<HI", self.hdr, q)
        groups = []
        if tbl and n <= MAX_FILE_GROUP_COUNT:
            t = self.cdo + tbl
            groups = [self._hstr(struct.unpack_from("<I", self.hdr, t + 4 * k)[0]) for k in range(n)]
        return Component(name, groups)

    # -- volume headers ----------------------------------------------------------
    def volume_header(self, index: int) -> dict:
        f, base, size = self.vols.volume(index)
        f.seek(base)
        b = f.read(20 + 64)
        sig = struct.unpack_from("<I", b, 0)[0]
        if sig != CAB_SIGNATURE:
            raise ValueError(f"volume {index}: bad signature 0x{sig:08X}")
        v = struct.unpack_from("<16I", b, 20)
        j = lambda lo, hi: lo | (hi << 32)  # noqa: E731
        return {
            "data_offset": j(v[0], v[1]),
            "first_file_index": v[2], "last_file_index": v[3],
            "first_file_offset": j(v[4], v[5]),
            "first_file_size_expanded": j(v[6], v[7]),
            "first_file_size_compressed": j(v[8], v[9]),
            "last_file_offset": j(v[10], v[11]),
            "last_file_size_expanded": j(v[12], v[13]),
            "last_file_size_compressed": j(v[14], v[15]),
            "volume_size": size,
        }

    # -- extraction ------------------------------------------------------------------
    def resolve(self, fd: FileDesc) -> FileDesc:
        seen = set()
        while fd.link_flags & LINK_PREV and fd.index not in seen:
            seen.add(fd.index)
            fd = self.files[fd.link_previous]
        return fd

    def iter_data(self, index: int, chunk_cb=None):
        """Yield decompressed byte chunks of file `index`; verifies size + MD5 at the end."""
        orig = self.files[index]
        fd = self.resolve(orig)
        if not fd.valid:
            raise ValueError(f"file {index} ({orig.name}) is invalid/has no data")
        rd = _VolumeReader(self, fd)
        md5 = hashlib.md5()
        total = 0
        if fd.flags & FILE_COMPRESSED:
            left = fd.compressed_size
            while left > 0:
                n = struct.unpack("<H", rd.read(2))[0]
                if n == 0:
                    raise ValueError(f"{fd.name}: zero-length chunk at {total} (old-style stream?)")
                raw = rd.read(n)
                d = zlib.decompressobj(-15)
                out = d.decompress(raw) + d.flush()
                left -= 2 + n
                md5.update(out)
                total += len(out)
                yield out
        else:
            left = fd.expanded_size
            while left > 0:
                out = rd.read(min(left, 1 << 20))
                left -= len(out)
                md5.update(out)
                total += len(out)
                yield out
        if total != fd.expanded_size:
            raise ValueError(f"{fd.name}: expanded {total} != declared {fd.expanded_size}")
        if md5.hexdigest() != fd.md5:
            raise ValueError(f"{fd.name}: MD5 {md5.hexdigest()} != stored {fd.md5}")

    def extract(self, index: int, dest_path: str) -> dict:
        os.makedirs(os.path.dirname(dest_path), exist_ok=True)
        sha = hashlib.sha256()
        md5 = hashlib.md5()
        n = 0
        tmp = dest_path + ".part"
        with open(tmp, "wb") as out:
            for chunk in self.iter_data(index):
                out.write(chunk)
                sha.update(chunk)
                md5.update(chunk)
                n += len(chunk)
        os.replace(tmp, dest_path)
        return {"size": n, "md5": md5.hexdigest(), "sha256": sha.hexdigest(), "md5_verified": True}

    def out_relpath(self, fd: FileDesc) -> str:
        parts = []
        if fd.file_group:
            parts.append(_sanitize(fd.file_group))
        if fd.directory:
            parts += [_sanitize(x) for x in fd.directory.replace("/", "\\").split("\\") if x]
        parts.append(_sanitize(fd.name))
        return os.path.join(*parts)


class _VolumeReader:
    def __init__(self, cab: ISCabinet, fd: FileDesc):
        self.cab = cab
        self.fd = fd
        self.seed = 0
        self._open(fd.volume)

    def _open(self, vol: int):
        fd = self.fd
        vh = self.cab.volume_header(vol)
        self.f, self.base, self.vsize = self.cab.vols.volume(vol)
        self.vol = vol
        if fd.flags & FILE_SPLIT:
            if fd.index == vh["last_file_index"] and vh["last_file_offset"] != 0x7FFFFFFF:
                off, exp, comp = vh["last_file_offset"], vh["last_file_size_expanded"], vh["last_file_size_compressed"]
            elif fd.index == vh["first_file_index"]:
                off, exp, comp = vh["first_file_offset"], vh["first_file_size_expanded"], vh["first_file_size_compressed"]
            else:
                raise ValueError(f"split file {fd.index} not first/last in volume {vol}")
        else:
            off, exp, comp = fd.data_offset, fd.expanded_size, fd.compressed_size
        self.left = comp if fd.flags & FILE_COMPRESSED else exp
        self.pos = self.base + off
        if off >= self.vsize:
            raise ValueError(f"file {fd.index} ({fd.name}) data offset 0x{off:X} beyond volume {vol}")

    def read(self, n: int) -> bytes:
        out = bytearray()
        while n:
            k = min(n, self.left)
            if k == 0:
                self._open(self.vol + 1)
                continue
            self.f.seek(self.pos)
            b = self.f.read(k)
            if len(b) != k:
                raise IOError(f"short read in volume {self.vol}")
            self.pos += k
            self.left -= k
            n -= k
            out += b
        if self.fd.flags & FILE_OBFUSCATED:
            out = _deobfuscate(out, self.seed)
            self.seed += len(out)
        return bytes(out)


_ROR = bytes((((b ^ 0xD5) >> 2) | ((b ^ 0xD5) << 6)) & 0xFF for b in range(256))


def _deobfuscate(buf: bytearray, seed: int) -> bytearray:
    t = buf.translate(_ROR)
    return bytearray((t[i] - ((seed + i) % 0x47)) & 0xFF for i in range(len(t)))


_BAD = set('<>:"|?*') | {chr(c) for c in range(32)}


def _sanitize(s: str) -> str:
    s = "".join("_" if ch in _BAD else ch for ch in s).strip()
    return s if s not in ("", ".", "..") else "_"


def describe(cab: ISCabinet) -> dict:
    return {
        "common_version": f"0x{cab.version:08X}", "major_version": cab.major,
        "volume_info": cab.volume_info, "cab_descriptor_offset": cab.cdo, "cab_descriptor_size": cab.cds,
        "file_table_offset": cab.file_table_offset, "file_table_size": cab.file_table_size,
        "file_table_offset2": cab.file_table_offset2,
        "directory_count": cab.directory_count, "file_count": cab.file_count,
        "directories": cab.directories,
        "file_groups": [asdict(g) for g in cab.file_groups],
        "components": [asdict(c) for c in cab.components],
    }


def main(argv):
    if len(argv) < 3 or argv[1] not in ("list", "extract"):
        print(__doc__)
        return 2
    vols = DirVolumes(argv[2])
    cab = ISCabinet(vols)
    if argv[1] == "list":
        print(json.dumps(describe(cab), indent=1))
        for fd in cab.files:
            print(f"{fd.index:4} fl={fd.flags:04X} vol={fd.volume} {fd.expanded_size:>12,} "
                  f"{fd.compressed_size:>12,} {fd.file_group}|{fd.directory}|{fd.name}")
        return 0
    out = argv[3]
    only = None
    if "--only" in argv:
        only = {x.lower() for x in argv[argv.index("--only") + 1].split(",")}
    results = []
    for fd in cab.files:
        if not fd.valid or (only and fd.name.lower() not in only):
            continue
        rel = cab.out_relpath(fd)
        t0 = time.time()
        r = cab.extract(fd.index, os.path.join(out, rel))
        r.update({"index": fd.index, "relpath": rel, "seconds": round(time.time() - t0, 2)})
        print(f"{fd.index:4} {r['size']:>12,} md5 OK  {rel}")
        results.append(r)
    if "--manifest" in argv:
        with open(argv[argv.index("--manifest") + 1], "w", encoding="utf-8") as fh:
            json.dump(results, fh, indent=1)
    vols.close()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
