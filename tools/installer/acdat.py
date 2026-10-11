"""
(Copied from the research scout reader scratchpad/research/scout/acdat.py, 2026-10-09, so
 tools/installer/datcheck.py is self-contained; since then only the default paths were made repository-relative.)

acdat.py - minimal, dependency-free reader for Asheron's Call (AC1) retail .dat files.

Format references (verified against GitHub on 2026-10-09):
  * ACEmulator/ACE  Source/ACE.DatLoader/{DatDatabase,DatDatabaseHeader,DatDirectory,
    DatDirectoryHeader,DatFile,DatReader}.cs                       (AGPL-3.0)
  * Chorizite/DatReaderWriter  DatReaderWriter/Lib/IO/{DatHeader.cs,DatBTree/*.cs}
    and DatDatabase.cs (zlib compression path)                       (MIT)

Layout summary
--------------
File offset 0x000: 256-byte ASCII version string (all zero in retail)
File offset 0x100: 64-byte "transactions" blob
File offset 0x140: DiskFileInfo_t header (this module: DatHeader), 21 x u32-ish fields
Everything else is fixed-size blocks of `block_size` bytes. Each block begins with a
u32 "next block" file offset (0 = last block of the chain), followed by block_size-4
payload bytes. Free blocks have bit 31 set on their next pointer.

Directory = B-tree. Each node is a 1716-byte logical record (spread over blocks):
  u32 branches[62]; u32 entry_count; Entry entries[61] (only entry_count valid)
  Entry (24 bytes): u16 flags; u16 version; u32 id; u32 offset; u32 size; u32 date; u32 iteration
  A node is a leaf if branches[0] == 0; otherwise it has entry_count+1 children.
  flags & 1  => payload is compressed: u32 uncompressed_size + zlib stream; `size`
               is then the *stored* (compressed) size.

Usage:
    import os
    from acdat import DatFile, default_client_dir
    with DatFile(os.path.join(default_client_dir(), "client_portal.dat")) as d:
        print(d.header)
        entries = d.entries()           # dict id -> Entry (walks the whole B-tree once)
        raw = d.read(0x13000000)        # bytes of the Region file
"""
from __future__ import annotations

import os
import struct
import zlib
from dataclasses import dataclass, field

HEADER_OFFSET = 0x140
RETAIL_MAGIC = 0x00005442            # 'BT'
NODE_BRANCHES = 62                   # 0x3E
NODE_ENTRIES = 61                    # 0x3D
ENTRY_SIZE = 24
NODE_SIZE = NODE_BRANCHES * 4 + 4 + NODE_ENTRIES * ENTRY_SIZE   # 1716
FLAG_COMPRESSED = 0x0001

DATASET_NAMES = {1: "Portal", 2: "Cell", 3: "Language"}  # HighRes shares Portal(1); subset distinguishes


@dataclass
class DatHeader:
    magic: int
    block_size: int
    file_size: int
    data_set: int
    data_subset: int
    free_head: int
    free_tail: int
    free_count: int
    btree_root: int
    new_lru: int
    old_lru: int
    use_lru: int
    master_map_id: int
    engine_pack_version: int
    game_pack_version: int
    version_major: bytes          # 16 bytes; DatReaderWriter treats it as a GUID
    version_minor: int
    version_string: str = ""      # 256-byte ASCII at file offset 0
    transactions: bytes = b""     # 64 bytes at file offset 0x100

    _FMT = "<15I16sI"             # 15 u32, 16-byte blob, u32  = 80 bytes

    @classmethod
    def parse(cls, buf320_plus: bytes) -> "DatHeader":
        version_string = buf320_plus[0:256].split(b"\0", 1)[0].decode("ascii", "replace")
        transactions = buf320_plus[256:320]
        vals = struct.unpack_from(cls._FMT, buf320_plus, HEADER_OFFSET)
        return cls(*vals, version_string=version_string, transactions=transactions)

    def as_lines(self):
        guid_hex = self.version_major.hex()
        return [
            f"magic               = 0x{self.magic:08X} ({'OK' if self.magic == RETAIL_MAGIC else 'UNEXPECTED'})",
            f"block_size          = {self.block_size} (0x{self.block_size:X}); payload/block = {self.block_size - 4}",
            f"file_size           = {self.file_size:,} (0x{self.file_size:X})",
            f"data_set            = {self.data_set} ({DATASET_NAMES.get(self.data_set, '?')})",
            f"data_subset         = {self.data_subset} (0x{self.data_subset:X})",
            f"free_head           = 0x{self.free_head:08X}",
            f"free_tail           = 0x{self.free_tail:08X}",
            f"free_count          = {self.free_count:,} blocks",
            f"btree_root          = 0x{self.btree_root:08X}",
            f"new_lru / old_lru   = 0x{self.new_lru:08X} / 0x{self.old_lru:08X}",
            f"use_lru             = {self.use_lru}",
            f"master_map_id       = 0x{self.master_map_id:08X}",
            f"engine_pack_version = {self.engine_pack_version}",
            f"game_pack_version   = {self.game_pack_version}",
            f"version_major(16B)  = {guid_hex}",
            f"version_minor       = {self.version_minor}",
            f"version_string@0    = {self.version_string!r}",
            f"transactions@0x100  = {self.transactions[:16].hex()}{'...' if any(self.transactions[16:]) else ' (rest zero)'}",
        ]


@dataclass
class Entry:
    flags: int
    version: int
    id: int
    offset: int
    size: int
    date: int
    iteration: int

    @property
    def compressed(self) -> bool:
        return bool(self.flags & FLAG_COMPRESSED)


@dataclass
class WalkStats:
    nodes: int = 0
    leaf_nodes: int = 0
    max_depth: int = 0
    duplicate_ids: int = 0
    node_offsets: list = field(default_factory=list)


class DatFile:
    def __init__(self, path: str):
        self.path = path
        self.f = open(path, "rb")
        head = self.f.read(HEADER_OFFSET + 80)
        self.header = DatHeader.parse(head)
        if self.header.magic != RETAIL_MAGIC:
            raise ValueError(f"{path}: bad magic 0x{self.header.magic:08X}")
        self.block_size = self.header.block_size
        self.disk_size = os.path.getsize(path)
        self._entries = None
        self.walk_stats = None
        # gotcha counters
        self.chain_highbit_seen = 0

    # -- context manager --------------------------------------------------
    def __enter__(self):
        return self

    def __exit__(self, *a):
        self.close()

    def close(self):
        if self.f:
            self.f.close()
            self.f = None

    # -- raw block-chain reading -----------------------------------------
    def read_chain(self, offset: int, size: int) -> bytes:
        """Read `size` payload bytes starting at block `offset`, following next pointers."""
        out = bytearray()
        bs = self.block_size
        payload = bs - 4
        cur = offset
        f = self.f
        while len(out) < size:
            if cur == 0 or cur + 4 > self.disk_size:
                raise IOError(f"broken block chain at 0x{cur:08X} after {len(out)}/{size} bytes")
            f.seek(cur)
            blk = f.read(bs)
            nxt = struct.unpack_from("<I", blk, 0)[0]
            if nxt & 0x80000000:
                self.chain_highbit_seen += 1
                nxt &= 0x7FFFFFFF
            need = size - len(out)
            out += blk[4:4 + min(payload, need)]
            cur = nxt
        return bytes(out)

    def chain_blocks(self, offset: int, size: int):
        """Return the list of block offsets used by a record (for fragmentation stats)."""
        blocks = []
        bs = self.block_size
        payload = bs - 4
        n = (size + payload - 1) // payload if size else 1
        cur = offset
        for _ in range(n):
            blocks.append(cur)
            self.f.seek(cur)
            cur = struct.unpack("<I", self.f.read(4))[0] & 0x7FFFFFFF
        return blocks

    # -- B-tree directory --------------------------------------------------
    def read_node(self, offset: int):
        raw = self.read_chain(offset, NODE_SIZE)
        branches = struct.unpack_from(f"<{NODE_BRANCHES}I", raw, 0)
        count = struct.unpack_from("<I", raw, NODE_BRANCHES * 4)[0]
        if count > NODE_ENTRIES:
            raise ValueError(f"node 0x{offset:08X}: entry_count {count} > {NODE_ENTRIES}")
        base = NODE_BRANCHES * 4 + 4
        ents = []
        for i in range(count):
            fl, ver, oid, off, sz, date, it = struct.unpack_from("<HHIIIII", raw, base + i * ENTRY_SIZE)
            ents.append(Entry(fl, ver, oid, off, sz, date, it))
        is_leaf = branches[0] == 0
        kids = [] if is_leaf else list(branches[:count + 1])
        return kids, ents

    def entries(self) -> dict:
        """Walk the full B-tree (iteratively) and return {id: Entry}. Cached."""
        if self._entries is not None:
            return self._entries
        st = WalkStats()
        out = {}
        stack = [(self.header.btree_root, 0)]
        while stack:
            off, depth = stack.pop()
            kids, ents = self.read_node(off)
            st.nodes += 1
            st.node_offsets.append(off)
            st.max_depth = max(st.max_depth, depth)
            if not kids:
                st.leaf_nodes += 1
            for e in ents:
                if e.id in out:
                    st.duplicate_ids += 1
                out[e.id] = e
            for k in kids:
                stack.append((k, depth + 1))
        self._entries = out
        self.walk_stats = st
        return out

    def find(self, file_id: int):
        """Point lookup by descending the B-tree (keys are sorted within a node)."""
        off = self.header.btree_root
        while True:
            kids, ents = self.read_node(off)
            i = 0
            while i < len(ents) and file_id > ents[i].id:
                i += 1
            if i < len(ents) and ents[i].id == file_id:
                return ents[i]
            if not kids:
                return None
            off = kids[i]

    # -- file payloads ------------------------------------------------------
    def read_entry(self, e: Entry, decompress: bool = True) -> bytes:
        raw = self.read_chain(e.offset, e.size)
        if decompress and e.compressed:
            usize = struct.unpack_from("<I", raw, 0)[0]
            data = zlib.decompress(raw[4:])
            if len(data) != usize:
                raise ValueError(f"0x{e.id:08X}: decompressed {len(data)} != declared {usize}")
            return data
        return raw

    def read(self, file_id: int, decompress: bool = True) -> bytes:
        ents = self._entries
        e = ents.get(file_id) if ents is not None else self.find(file_id)
        if e is None:
            raise KeyError(f"0x{file_id:08X} not in {os.path.basename(self.path)}")
        return self.read_entry(e, decompress)


# ---------------------------------------------------------------------------
# Little binary cursor with the AC-specific primitives used by DatLoader.
# ---------------------------------------------------------------------------
class Reader:
    __slots__ = ("b", "p")

    def __init__(self, b: bytes, p: int = 0):
        self.b = b
        self.p = p

    def remaining(self):
        return len(self.b) - self.p

    def u8(self):
        v = self.b[self.p]; self.p += 1; return v

    def u16(self):
        v = struct.unpack_from("<H", self.b, self.p)[0]; self.p += 2; return v

    def i16(self):
        v = struct.unpack_from("<h", self.b, self.p)[0]; self.p += 2; return v

    def u32(self):
        v = struct.unpack_from("<I", self.b, self.p)[0]; self.p += 4; return v

    def i32(self):
        v = struct.unpack_from("<i", self.b, self.p)[0]; self.p += 4; return v

    def f32(self):
        v = struct.unpack_from("<f", self.b, self.p)[0]; self.p += 4; return v

    def f64(self):
        v = struct.unpack_from("<d", self.b, self.p)[0]; self.p += 8; return v

    def vec3(self):
        v = struct.unpack_from("<3f", self.b, self.p); self.p += 12; return v

    def raw(self, n):
        v = self.b[self.p:self.p + n]; self.p += n; return v

    def skip(self, n):
        self.p += n

    def align4(self):
        self.p = (self.p + 3) & ~3

    def compressed_u32(self):
        """AC 'compressed uint': 1, 2 or 4 bytes depending on top bits of first byte."""
        b0 = self.u8()
        if not (b0 & 0x80):
            return b0
        b1 = self.u8()
        if not (b0 & 0x40):
            return ((b0 & 0x7F) << 8) | b1
        s = self.u16()
        return ((((b0 & 0x3F) << 8) | b1) << 16) | s

    def pstring16(self):
        n = self.u16()
        return self.raw(n).decode("cp1252", "replace")

    def pstring8(self):
        n = self.u8()
        return self.raw(n).decode("cp1252", "replace")


def default_client_dir():
    """The retail dat folder: $AC_CLIENT_DIR, else $LIFESTONE_DAT_DIR, else <repo>/client (the game's default)."""
    repo_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    return (os.environ.get("AC_CLIENT_DIR") or os.environ.get("LIFESTONE_DAT_DIR")
            or os.path.join(repo_root, "client"))


def scratch_out_dir():
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(here, "out")
    os.makedirs(out, exist_ok=True)
    return out
