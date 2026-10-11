"""Extract ACE's points of interest (teleport destinations) from the pinned ACE world database dump.

Writes src/Lifestone.Data/World/Data/ace-world-points-of-interest.json, a small AGPL-3.0 extract used by the M1
teleport panel (docs/navigation.md, docs/third-party/sky-nav.md).

Source: ACE-World-Database-v0.9.296.sql from ACEmulator/ACE-World-16PY-Patches (AGPL-3.0), as downloaded by the
installer into server/runtime/downloads/ace-world-v0.9.296/ (unzip it first). Read-only: the script only parses the
INSERT statements of four tables:
  points_of_interest          (name -> weenie class id)
  weenie                      (class name)
  weenie_properties_string    type 1 = PropertyString.Name
  weenie_properties_position  type 2 = PositionType.Destination
ACE's @telepoi command (Source/ACE.Server/Command/Handlers/AdminCommands.cs, HandleTeleportPoi, @95ef426d) teleports
to exactly that Destination position (after WorldObject.AdjustDungeon, which only re-cells dungeon positions).

Usage (run with python -I; pass paths, do not run it from inside the data folder):
  python -I tools/data/extract_ace_poi.py <ACE-World-Database-v0.9.296.sql> <out.json>
"""
import hashlib
import json
import re
import sys

TABLES = {"points_of_interest", "weenie", "weenie_properties_position", "weenie_properties_string"}
INSERT = re.compile(r"^INSERT INTO `([a-z_]+)` VALUES (.*);\s*$")
POSITION_TYPE_DESTINATION = 2
PROPERTY_STRING_NAME = 1


def tuples(values):
    """Yields the rows of an INSERT ... VALUES (...),(...) body (MySQL dump quoting)."""
    i = 0
    n = len(values)
    while i < n:
        if values[i] != "(":
            i += 1
            continue
        i += 1
        row = []
        while True:
            if values[i] == "'":
                i += 1
                s = []
                while True:
                    c = values[i]
                    if c == "\\":
                        nxt = values[i + 1]
                        s.append({"n": "\n", "r": "\r", "t": "\t", "0": "\0"}.get(nxt, nxt))
                        i += 2
                        continue
                    if c == "'":
                        if i + 1 < n and values[i + 1] == "'":
                            s.append("'")
                            i += 2
                            continue
                        i += 1
                        break
                    s.append(c)
                    i += 1
                row.append("".join(s))
            else:
                j = i
                while values[j] not in ",)":
                    j += 1
                tok = values[i:j].strip()
                if tok == "NULL":
                    row.append(None)
                else:
                    try:
                        row.append(int(tok))
                    except ValueError:
                        row.append(float(tok))
                i = j
            if values[i] == ",":
                i += 1
                continue
            i += 1  # ')'
            break
        yield row


def display_name(weenie_name, poi_name):
    """The place a portal leads to, from its weenie name: 'Portal to Holtburg' -> 'Holtburg', 'Tufa Portal' -> 'Tufa'."""
    name = weenie_name or poi_name
    if name.startswith("Portal to "):
        name = name[len("Portal to "):]
    if name.endswith(" Portal"):
        name = name[: -len(" Portal")]
    if name.startswith("the "):
        name = "The " + name[len("the "):]
    return name.strip()


def main(sql_path, out_path):
    sha = hashlib.sha256()
    rows = {t: [] for t in TABLES}
    with open(sql_path, "rb") as raw:
        for chunk in iter(lambda: raw.read(1 << 20), b""):
            sha.update(chunk)
    with open(sql_path, "r", encoding="utf-8", errors="strict") as f:
        for line in f:
            if not line.startswith("INSERT INTO `"):
                continue
            m = INSERT.match(line)
            if m and m.group(1) in TABLES:
                rows[m.group(1)].extend(tuples(m.group(2)))

    class_names = {r[0]: r[1] for r in rows["weenie"]}
    names = {r[1]: r[3] for r in rows["weenie_properties_string"] if r[2] == PROPERTY_STRING_NAME}
    destinations = {r[1]: r for r in rows["weenie_properties_position"] if r[2] == POSITION_TYPE_DESTINATION}

    by_wcid = {}
    for (poi_id, poi_name, wcid, _modified) in rows["points_of_interest"]:
        by_wcid.setdefault(wcid, []).append((poi_id, poi_name))

    out = []
    for wcid, aliases in sorted(by_wcid.items(), key=lambda kv: min(a[0] for a in kv[1])):
        d = destinations.get(wcid)
        if d is None:
            raise SystemExit(f"weenie {wcid} ({aliases}) has no Destination position")
        out.append({
            "displayName": display_name(names.get(wcid), aliases[0][1]),
            "poiNames": [a[1] for a in aliases],
            "wcid": wcid,
            "weenieClassName": class_names.get(wcid),
            "weenieName": names.get(wcid),
            "cell": "0x%08X" % d[3],
            "origin": [d[4], d[5], d[6]],
            "rotationWxyz": [d[7], d[8], d[9], d[10]],
        })

    doc = {
        "_license": "AGPL-3.0-only. Values extracted from the ACE world database (ACEmulator/ACE-World-16PY-Patches, "
                    "AGPL-3.0), release ACE-World-Database-v0.9.296.sql. See docs/third-party/sky-nav.md.",
        "_source": "ACE-World-Database-v0.9.296.sql (sha256 " + sha.hexdigest() + "): tables points_of_interest, "
                   "weenie (class_Name), weenie_properties_string type 1 (Name), weenie_properties_position type 2 "
                   "(Destination). One entry per destination weenie; poiNames are ACE's @telepoi names for it.",
        "_generator": "tools/data/extract_ace_poi.py",
        "_why": "The client dats do not name places; ACE's points_of_interest table (used by @telepoi) maps town and "
                "landmark names to portal weenies whose Destination is the place's drop point.",
        "destinations": out,
    }
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"{len(out)} destinations from {len(rows['points_of_interest'])} points of interest -> {out_path}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2])
