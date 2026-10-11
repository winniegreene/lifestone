"""Dump the retail client's command-name / command-ID table (string2command) from acclient.exe.

Read-only: the exe is opened as a byte file and parsed as PE32. Nothing is executed.
Stdlib only. Run with:  python -I tools/protocol/dump_command_table.py <path-to-acclient.exe> [out.tsv]

Table locations for build 11.6096 (MD5 116d9a66a70b6af449dc3a28d82f2f6d) come from
Mag-nus/acclient-AI-RE '2015-10 11.6096/docs/13-client-differences-2013-vs-2015.md':
name pointers at VA 0x00804F48, IDs at VA 0x008055B8, 0x19C (412) entries.

Output: one line per entry, "<ordinal>\t0x<ID>\t<name>".
Used by Lifestone.Net tests to check the motion-command table (u16 wire value -> full u32 command)
against the user's own client, so it is never committed as data.
"""
import hashlib
import struct
import sys

EXPECTED_MD5 = "116d9a66a70b6af449dc3a28d82f2f6d"
NAMES_VA = 0x00804F48
IDS_VA = 0x008055B8
COUNT = 0x19C


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    data = open(argv[1], "rb").read()
    md5 = hashlib.md5(data).hexdigest()
    if md5 != EXPECTED_MD5:
        print(f"refusing: exe MD5 {md5} is not 11.6096 ({EXPECTED_MD5}); table VAs would be wrong", file=sys.stderr)
        return 1

    pe = struct.unpack_from("<I", data, 0x3C)[0]
    nsec = struct.unpack_from("<H", data, pe + 6)[0]
    optsz = struct.unpack_from("<H", data, pe + 20)[0]
    imagebase = struct.unpack_from("<I", data, pe + 24 + 28)[0]
    sections = []
    off = pe + 24 + optsz
    for _ in range(nsec):
        vsize, va, rawsize, rawptr = struct.unpack_from("<IIII", data, off + 8)
        sections.append((va, max(vsize, rawsize), rawptr))
        off += 40

    def va2off(v):
        rva = v - imagebase
        for va, size, rawptr in sections:
            if va <= rva < va + size:
                return rawptr + (rva - va)
        raise ValueError(f"VA 0x{v:08X} not in any section")

    def cstr(v):
        o = va2off(v)
        return data[o:data.index(b"\0", o)].decode("latin-1")

    lines = []
    for i in range(COUNT):
        name_ptr = struct.unpack_from("<I", data, va2off(NAMES_VA) + 4 * i)[0]
        cid = struct.unpack_from("<I", data, va2off(IDS_VA) + 4 * i)[0]
        lines.append(f"{i}\t0x{cid:08X}\t{cstr(name_ptr)}")

    out = "\n".join(lines) + "\n"
    if len(argv) > 2:
        with open(argv[2], "w", encoding="utf-8", newline="\n") as f:
            f.write(out)
    else:
        sys.stdout.write(out)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
