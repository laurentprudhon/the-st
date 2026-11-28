#!/usr/bin/env python3
"""Group a `cpu-tests --clocks` dump's residue (our_total - length) by operand shape.

The clock column is a class problem: after the gate's state column is green, every remaining
record that is off on clocks is invisible to `--failures` (state only). This is the instrument
that names the classes and their sizes, keyed on the corpus's own operand text -- never on a
hand-rolled opcode decoder (two were written and both were wrong).

Usage:
    cpu-tests --corpus clk --dir tests/680x0/full \\
        --clocks clk.clocks.txt --failures clk.fail.txt
    python3 clk-residue-by-shape.py clk.clocks.txt clk.fail.txt
    python3 clk-residue-by-shape.py clk.clocks.txt clk.fail.txt --class 8
    python3 clk-residue-by-shape.py clk.clocks.txt clk.fail.txt --verify-faults tests/680x0/full

`--verify-faults` samples the named residue class and checks, per record, that it is really a
group-0 exception entry: `initial.ssp - final.ssp == 14` and seven `w` transactions in the
record's own `transactions`. Do this instead of counting `--clocks`' `vector` column -- that
column is written only inside EnterExceptionAsync and keeps the previous record's value.
"""
import argparse
import collections
import gzip
import json
import os
import random
import re


def read_failures(path):
    """(file, name) pairs of the records that fail STATE, from a --failures dump."""
    out = set()
    with open(path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 2:
                out.add((parts[0], parts[1]))
    return out


def shape_of(text):
    """Collapse an operand text to its shape: keep the modes, drop the register numbers."""
    text = re.sub(r"\b([AD])(\d)\b", r"\1n", text)
    return re.sub(r"#\$[0-9A-Fa-f]+", "#", text)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("clocks")
    ap.add_argument("failures")
    ap.add_argument("--class", dest="klass", type=int,
                    help="only this residue value (e.g. 8); omit for the whole profile")
    ap.add_argument("--verify-faults", dest="corpus", default=None,
                    help="a corpus dir (tests/680x0/full) to check a group-0 frame against")
    ap.add_argument("--sample", type=int, default=40)
    args = ap.parse_args()

    failing = read_failures(args.failures)
    rows = []
    with open(args.clocks, encoding="utf-8", errors="replace") as handle:
        handle.readline()
        for line in handle:
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 6:
                continue
            file, name = parts[0], parts[1]
            if (file, name) in failing:
                continue                      # exact-clock is only meaningful for passing state
            length, total = int(parts[2]), int(parts[5])
            match = re.match(r"^[0-9a-f]+ \[(.*)\] \d+$", name)
            rows.append((file, name, match.group(1) if match else name, total - length))

    off = [row for row in rows if row[3] != 0]
    print(f"passing records: {len(rows)}  off-clock: {len(off)}")

    histogram = collections.Counter(row[3] for row in off)
    print("\nresidue -> records   (a +-2 is a STATE, a +-4 is a READ)")
    for value, count in sorted(histogram.items(), key=lambda kv: -kv[1]):
        print(f"  {value:+6d}  {count:6d}")

    selected = off if args.klass is None else [row for row in off if row[3] == args.klass]
    if args.klass is not None:
        print(f"\nclass {args.klass:+d}: {len(selected)} records")

    by_file = collections.Counter(row[0] for row in selected)
    print("\nby file:")
    for file, count in by_file.most_common(20):
        print(f"  {file:14s} {count:5d}")

    by_shape = collections.Counter(shape_of(row[2]) for row in selected)
    print(f"\nby shape ({len(by_shape)} shapes):")
    for shape, count in by_shape.most_common(30):
        print(f"  {count:5d}  {shape}")

    if args.corpus and selected:
        sample = random.Random(7).sample(selected, min(args.sample, len(selected)))
        grouped = collections.defaultdict(list)
        for file, name, _, _ in sample:
            grouped[file].append(name)
        ok, bad = 0, []
        for file, names in grouped.items():
            path = os.path.join(args.corpus, file + ".json.gz")
            if not os.path.exists(path):
                bad.append((file, "no such corpus file"))
                continue
            with gzip.open(path, "rt", encoding="utf-8") as handle:
                records = {record["name"]: record for record in json.load(handle)}
            for name in names:
                record = records.get(name)
                if record is None:
                    bad.append((file, name, "missing"))
                    continue
                delta = record["initial"]["ssp"] - record["final"]["ssp"]
                writes = sum(1 for t in record["transactions"] if t[0] == "w")
                if delta == 14 and writes == 7:
                    ok += 1
                else:
                    bad.append((file, name, delta, writes))
        print(f"\nframe check on a sample of {len(sample)}: {ok} carry a 7-word group-0 frame "
              f"(ssp -14), {len(bad)} do not")
        for entry in bad[:5]:
            print("  ", entry)


if __name__ == "__main__":
    random.seed(7)
    main()
