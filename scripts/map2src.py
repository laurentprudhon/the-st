#!/usr/bin/env python3
"""map2src.py - say where each line of an EmuTOS .sym file is defined in its source tree.

A Hatari .sym file maps address -> name; it carries no source information at all. This tool reads the
three artefacts that do:

  * the linker map (emutos.map)  - which object file each symbol came from,
  * the objects themselves       - `m68k-atari-mint-nm -l` reads the compiler's a.out stabs,
  * the source tree              - to quote the definition line.

and writes one .src line per .sym line, in the same order, so the two files diff line by line:

    <address> <type> <name> <location> <source text>

`location` is `file:line` and comes from two sources, marked so a reader can tell them apart:

    bios/bios.c:777     exact: the compiler's own line info for that symbol (nm -l).
    ~bios/lowstram.c:53 derived: this toolchain's a.out stabs carry line info for *code* only, so data
                        and BSS symbols are located by searching the single source file the object was
                        built from, and only when the name matches exactly once there.
    -                   no location: linker-provided symbols (__text, __etext) and the object-name
                        markers map2sym emits (e.g. `startup.o`).

The .sym must have been built with `-g` for the line info to exist. `-g` does not change a single byte
of the linked ROM image (verified: etos256*.img hashes are identical with and without it, all 19
countries), so the shipped bytes and this source map come from the same build.

Usage:
    map2src.py --map emutos.map --sym etos256uk.sym --out etos256uk.src --objects BUILD_DIR
               [--source-root DIR] [--nm m68k-atari-mint-nm] [--check]
"""

import argparse
import os
import re
import subprocess

OBJECT_HEADER = re.compile(
    r"^ \.(?:text|data|bss|first_stram|low_stram|lowstram|last_stram|laststram|stack)\s+0x([0-9a-f]+)\s+0x([0-9a-f]+)\s+(\S+)\s*$")
MAP_SYMBOL = re.compile(r"^\s+0x([0-9a-f]+)\s+(\S+)\s*$")
# `0x0000000000000380                _proc_lives = 0x380` - a symbol *assigned* by the linker script.
MAP_ASSIGN = re.compile(r"^\s+0x([0-9a-f]+)\s+(\S+)\s+=\s+\S+\s*$")
NM_SYMBOL = re.compile(r"^(?:([0-9a-f]+))?\s+([A-Za-z])\s+(\S+)(?:\s+(\S+):(\d+))?\s*$")
NM_MEMBER = re.compile(r"^(\S+\.o):\s*$")

# map2sym emits one symbol per object file, named after the object: not a source entity.
OBJECT_MARKER = re.compile(r"^[\w.-]+\.o$")
LINKER_SYMBOLS = {"__text", "__etext", "__data", "__edata", "_etext", "_edata", "__bss", "__endbss"}


def parse_map(path):
    """([(address, name, object)], {symbols assigned by the linker script}) in link order."""
    rows, owner, assigned = [], None, set()
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            m = OBJECT_HEADER.match(line)
            if m:
                owner = m.group(3)
                continue
            m = MAP_ASSIGN.match(line)
            if m:
                assigned.add(m.group(2))
                continue
            m = MAP_SYMBOL.match(line)
            if m and owner:
                rows.append((int(m.group(1), 16), m.group(2), owner))
    return rows, assigned


def nm_read(nm_cmd, path, source_root):
    """(symbol -> 'file:line', file hints) for one object or archive, from the compiler's stabs."""
    table, hints = {}, []
    if not os.path.exists(path):
        return table, hints
    out = subprocess.run([nm_cmd, "-l", path], capture_output=True, text=True).stdout
    for line in out.splitlines():
        if NM_MEMBER.match(line):
            continue
        m = NM_SYMBOL.match(line)
        if not m:
            continue
        _, symchar, name, src, lineno = m.groups()
        if not src:
            continue
        if m.group(1) is None or symchar in ("U", "N", "?"):
            # undefined or nameless: its location is a *use site* in this object, not a definition.
            hints.append(os.path.relpath(src, source_root) if os.path.isabs(src) else src)
            continue
        src = os.path.relpath(src, source_root) if os.path.isabs(src) else src
        hints.append(src)
        if lineno and lineno != "0" and not name.startswith(".L"):
            table.setdefault(name, "%s:%s" % (src, lineno))
    return table, hints


def find_source_file(source_root, basename, hints):
    """The one source file an object was built from: prefer what nm says, else a unique basename hit."""
    if hints:
        return os.path.join(source_root, max(set(hints), key=hints.count))
    hits = []
    for dirpath, dirnames, filenames in os.walk(source_root):
        if dirpath.startswith(os.path.join(source_root, "obj")):
            continue
        for ext in (".c", ".S", ".s"):
            if basename + ext in filenames:
                hits.append(os.path.join(dirpath, basename + ext))
    return hits[0] if len(hits) == 1 else None


def search_linker_script(source_root, name):
    """(path, line) of `name = ...` in a linker script, if exactly one script assigns it."""
    hits = []
    for dirpath, dirnames, filenames in os.walk(source_root):
        dirnames[:] = [d for d in dirnames if d not in (".git", "obj")]
        for fn in filenames:
            if not fn.endswith(".ld"):
                continue
            full = os.path.join(dirpath, fn)
            try:
                with open(full, encoding="utf-8", errors="replace") as fh:
                    for lineno, line in enumerate(fh, 1):
                        if re.match(r"^\s*%s\s*=" % re.escape(name), line):
                            hits.append((full, lineno))
            except OSError:
                continue
    return hits[0] if len(hits) == 1 else None


def search_definition(path, name, is_code):
    """Line of the definition of `name` in `path`, or None unless there is exactly one candidate."""
    ident = name[1:] if name.startswith("_") and not name.startswith("__") else name
    if path.endswith((".S", ".s")):
        # assembler: the definition is a label, or a .comm/.lcomm/.set that names the symbol
        patterns = [r"^\s*%s\s*:" % re.escape(name),
                    r"^\s*\.(?:comm|lcomm|set)\s+%s\b" % re.escape(name)]
    elif is_code:
        patterns = [r"^[A-Za-z_][\w \t\*]*\b%s\s*\(" % re.escape(ident),
                    r"^\s*%s\s*:" % re.escape(name)]
    else:
        patterns = [r"^[A-Za-z_][\w \t\*]*\b%s\b\s*(?:\[|=|;|,)" % re.escape(ident),
                    r"^\s*%s\s*:" % re.escape(name)]
    try:
        with open(path, encoding="utf-8", errors="replace") as fh:
            lines = fh.readlines()
    except OSError:
        return None
    for pattern in patterns:
        regex = re.compile(pattern)
        hits = [n for n, line in enumerate(lines, 1)
                if regex.match(line) and "extern" not in line
                and not line.lstrip().startswith(("#", "*", "/", "return"))]
        if hits:
            return hits[0] if len(hits) == 1 else None
    return None


def quote(source_root, location):
    """The definition line, or '<line above> <line>' when the compiler points at an opening brace."""
    if not location or location == "-":
        return ""
    path, _, lineno = location.lstrip("~").rpartition(":")
    try:
        with open(os.path.join(source_root, path), encoding="utf-8", errors="replace") as fh:
            lines = fh.readlines()
        idx = int(lineno) - 1
        text = lines[idx].strip()
        if text in ("{", ""):
            for back in range(idx - 1, max(idx - 4, -1), -1):
                prev = lines[back].strip()
                if prev and not prev.startswith(("*", "/*", "//")):
                    return ("%s %s" % (prev, text)).strip()
        return text
    except (OSError, ValueError, IndexError):
        return ""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--map", required=True)
    ap.add_argument("--sym", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--objects", required=True, help="build directory holding obj/*.o")
    ap.add_argument("--source-root", default=None, help="EmuTOS source tree (default: the build dir)")
    ap.add_argument("--nm", default="m68k-atari-mint-nm")
    ap.add_argument("--check", action="store_true", help="verify the output lines up with the .sym")
    args = ap.parse_args()

    objects_dir = os.path.abspath(args.objects)
    source_root = os.path.abspath(args.source_root or objects_dir)

    map_rows, script_symbols = parse_map(args.map)
    by_object = {}
    for _, name, owner in map_rows:
        by_object.setdefault(owner, set()).add(name)

    exact, files = {}, {}
    for owner in by_object:
        member = re.match(r"^(.*\.a)\(([^)]+)\)$", owner)
        path = os.path.join(objects_dir, member.group(1) if member else owner)
        table, hints = nm_read(args.nm, path, source_root)
        exact.update(table)
        base = os.path.splitext(os.path.basename(member.group(2) if member else owner))[0]
        files[owner] = find_source_file(source_root, base, hints)

    def location(name, owner, is_code):
        if name in exact:
            return exact[name]
        if name in script_symbols:
            # the fixed system-variable block: `_proc_lives = 0x380` lives in tosvars.ld, not in C
            hit = search_linker_script(source_root, name)
            return "~%s:%d" % (os.path.relpath(hit[0], source_root), hit[1]) if hit else "-"
        if name in LINKER_SYMBOLS or OBJECT_MARKER.match(name) or owner not in files:
            return "-"
        src = files[owner]
        if not src:
            return "-"
        lineno = search_definition(src, name, is_code)
        return "~%s:%d" % (os.path.relpath(src, source_root), lineno) if lineno else "-"

    sym_rows = [l.split() for l in open(args.sym, encoding="utf-8") if l.strip()]
    owner_of = {}
    for _, name, owner in map_rows:
        owner_of.setdefault(name, owner)

    lines = [
        "# %s - where each line of %s is defined in the EmuTOS 1.4 source tree."
        % (os.path.basename(args.out), os.path.basename(args.sym)),
        "# Line N here describes line N of the .sym: <address> <type> <name> <file>:<line> <source text>",
        "#   file:line  from the compiler's line info (the build used -g; -g does not change one byte",
        "#              of the linked ROM image, so these bytes are the shipped bytes)",
        "#  ~file:line  located by source search: a.out stabs carry line info for code only, so data and",
        "#              BSS symbols are found in the one source file the object was built from, and only",
        "#              when the name matches exactly once there",
        "#  -           no source location: linker-provided symbols (__text, __etext) and the object-name",
        "#              markers map2sym emits (e.g. 'startup.o')",
        "# Paths are relative to the EmuTOS 1.4 source root.",
    ]
    counts = {"exact": 0, "derived": 0, "none": 0}
    for row in sym_rows:
        if len(row) < 3:
            raise SystemExit("malformed .sym line: %r" % row)
        addr, typ, name = row[0], row[1], row[2]
        loc = location(name, owner_of.get(name), typ == "T")
        if loc == "-":
            counts["none"] += 1
        elif loc.startswith("~"):
            counts["derived"] += 1
        else:
            counts["exact"] += 1
        lines.append(("%-12s %s %-38s %-26s %s" % (addr, typ, name, loc,
                                                   quote(source_root, loc))).rstrip())

    with open(args.out, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")

    total = len(sym_rows)
    print("  %s: %d/%d symbols located (%d exact, %d derived, %d none)"
          % (os.path.basename(args.out), counts["exact"] + counts["derived"], total,
             counts["exact"], counts["derived"], counts["none"]))

    if args.check:
        written = [l for l in open(args.out, encoding="utf-8") if not l.startswith("#")]
        if len(written) != total:
            raise SystemExit("%s has %d symbol lines, .sym has %d" % (args.out, len(written), total))
        for a, b in zip(written, sym_rows):
            if a.split()[:3] != b[:3]:
                raise SystemExit("line drift: %r vs %r" % (a.split()[:3], b[:3]))


if __name__ == "__main__":
    main()
