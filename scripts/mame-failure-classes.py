#!/usr/bin/env python3
"""Classify the MAME gate's failures: first differing field, then the frame's own offsets.

`cpu-tests --corpus mame --failures FILE` writes one line per failing record:

    ADD.l	008 ADD.l (A1)+, D1 d299	a1 a2924a6d (corpus a2924a69)

The first token after the case name is the *first* field that differs, which is the only cheap
classifier there is (a later field's difference is usually a consequence). This script joins those
lines back to `tests/680x0/mame/<file>.json` — the record's `transactions` say whether the
instruction framed (a read of function code 5 below $40 is the vector fetch) and `final.ssp` is the
frame's base — and prints:

  * the field-level table (how many records first differ on `a1`, `sr`, `ram`, `pc` …);
  * for `ram`, the offset relative to the frame's base, which is what separates "the frame's saved
    PC word" from "a stack write above the frame" from "somewhere else";
  * the same split per family, so a rule can be priced before it is written.

    scripts/mame-failure-classes.py --failures /tmp/mame-fail.txt --clocks /tmp/mame-clk.txt

Standard library only. `--failures` is required; `--clocks` adds the exact-clock column.
"""

import argparse
import collections
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MAME = os.path.join(ROOT, "tests", "680x0", "mame")

# A failure line: "FILE\tINDEX NAME\tFIELD values…". The values are hex words with the corpus's own
# value in parentheses, e.g. "a1 a2924a6d (corpus a2924a69)" or "ram 4542ab 08 (corpus 06)".
LINE = re.compile(r"^([^\t]+)\t(\d+) ([^\t]*)\t(.*)$")
RAM = re.compile(r"ram ([0-9a-f]+) ([0-9a-f]+) \(corpus ([0-9a-f]+)\)")
FRAME_SIZE = 14  # the group-0 frame; a 6-byte frame's two words are a prefix of the same offsets

# The two frames the core can enter, measured from the corpus's own stack pointer movement
# (`initial.ssp − final.ssp`). Ascending from the frame's base:
#   14 bytes (group 0, an address error): 6 undefined bytes (MAME writes its own residue there),
#            then the faulting instruction's word, then SR, then the saved PC;
#   6 bytes  (group 1/2): SR, then the saved PC.
FRAMES = {
    14: {0: "undefined", 2: "undefined", 4: "undefined", 6: "ir", 8: "sr", 10: "pc.hi",
         12: "pc.lo"},
    6: {0: "sr", 2: "pc.hi", 4: "pc.lo"},
}


def load(file):
    path = os.path.join(MAME, file + ".json")
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def framed(record):
    """The record entered an exception: a supervisor read below $40 is the vector fetch."""
    for t in record["transactions"]:
        if t[0] == "r" and t[2] == 5 and t[3] < 0x40:
            return True
    return False


def frame_offsets(record):
    """The frame's own bytes, absolute address -> (word name, byte lane).

    The corpus's `ram` is byte-addressed (a word write contributes two entries at A and A+1), so
    the low lane of a word write is the odd address. `final.ssp` is the frame's base for a framed
    record; a record that did not frame has no frame, only stack writes above `ssp`. The frame's
    size is the stack pointer's own movement, which is what separates a group-0 frame (14 bytes)
    from a group-1/2 one (6 bytes) — their layouts differ.
    """
    ssp = record["final"]["ssp"]
    size = (record["initial"]["ssp"] - ssp) & 0xFFFFFF
    words = FRAMES.get(size)
    out = {}
    if not framed(record) or words is None:
        return out
    for o, name in words.items():
        out[ssp + o] = (name, "hi")
        out[ssp + o + 1] = (name, "lo")
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--failures", required=True, help="a --failures dump from cpu-tests")
    ap.add_argument("--clocks", help="a --clocks dump, for the exact-clock column")
    ap.add_argument("--top", type=int, default=12, help="families per table")
    args = ap.parse_args()

    if not os.path.isdir(MAME):
        sys.exit("no %s — run scripts/fetch-m68000-tests.sh first" % MAME)

    records = {}
    fields = collections.Counter()
    by_field_family = collections.defaultdict(collections.Counter)
    ram_class = collections.Counter()
    ram_by_family = collections.defaultdict(collections.Counter)
    framed_total = framed_failing = 0
    delta_by_class = collections.defaultdict(collections.Counter)

    for line in open(args.failures, "r", encoding="utf-8"):
        line = line.rstrip("\n")
        if not line:
            continue
        m = LINE.match(line)
        if not m:
            continue
        file, index, name, detail = m.group(1), int(m.group(2)), m.group(3), m.group(4)
        if file not in records:
            records[file] = load(file)
        record = records[file][index]
        if framed(record):
            framed_failing += 1

        field = detail.split(" ")[0]
        fields[field] += 1
        by_field_family[field][file] += 1

        if field != "ram":
            # The delta, where the field is one number: ours - corpus.
            ours = re.match(r"^[a-z0-9]+ ([0-9a-f]+) \(corpus ([0-9a-f]+)\)", detail)
            if ours:
                d = int(ours.group(1), 16) - int(ours.group(2), 16)
                delta_by_class[name.split(" ")[1] if len(name.split(" ")) > 1 else file][d] += 1
            continue

        # ram: classify by offset from the frame's base.
        m2 = RAM.search(detail)
        if not m2:
            ram_class["ram (unparsed)"] += 1
            continue
        addr = int(m2.group(1), 16)
        delta = int(m2.group(2), 16) - int(m2.group(3), 16)
        offsets = frame_offsets(record)
        if addr in offsets:
            name, lane = offsets[addr]
            what = "frame %s (%s byte)" % (name, lane)
        elif addr >= record["final"]["ssp"]:
            what = "stack write above the frame"
        else:
            what = "ram elsewhere"
        ram_class[what] += 1
        ram_by_family[what][file] += 1
        delta_by_class[what][delta] += 1

    total = sum(fields.values())
    print("failures: %d   of them framed records: %d" % (total, framed_failing))

    print("\n## first differing field\n")
    print("| field | records | share |")
    print("|---|---|---|")
    for field, n in fields.most_common():
        print("| `%s` | %d | %.1f %% |" % (field, n, 100.0 * n / total))

    print("\n## ram, by where it is (with the measured delta, ours − corpus)\n")
    print("| class | records | delta (records) |")
    print("|---|---|---|")
    for what, n in ram_class.most_common():
        d = ", ".join("%+d: %d" % (k, v) for k, v in delta_by_class[what].most_common(3))
        print("| %s | %d | %s |" % (what, n, d))

    for field, _ in fields.most_common():
        fam = by_field_family[field]
        print("\n### `%s` by family (%d records, top %d)" % (field, fam.total(), args.top))
        for file, n in fam.most_common(args.top):
            print("  %-10s %5d" % (file, n))

    for what, _ in ram_class.most_common():
        fam = ram_by_family[what]
        print("\n### %s by family (%d records, top %d)" % (what, fam.total(), args.top))
        for file, n in fam.most_common(args.top):
            print("  %-10s %5d" % (file, n))

    if args.clocks:
        exact = wrong = 0
        with open(args.clocks, "r", encoding="utf-8") as f:
            header = f.readline()
            for line in f:
                parts = line.split("\t")
                if len(parts) < 6:
                    continue
                length, ours = int(parts[2]), int(parts[5])
                if length == ours:
                    exact += 1
                else:
                    wrong += 1
        print("\n## exact-clock\n")
        print("%d / %d = %.1f %% (%d records stop early or late)" %
              (exact, exact + wrong, 100.0 * exact / max(exact + wrong, 1), wrong))


if __name__ == "__main__":
    main()
