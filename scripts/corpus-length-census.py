#!/usr/bin/env python3
"""Measure the 680x0 corpus's `length` column against its own bus cycles.

For every record the corpus publishes two numbers we can compare:

    length        the reference's CPU clocks for the record (CLK's tick table)
    transactions  ["n", cycles] / ["t", cycles]   internal work
                  ["r"|"w", cycles, fc, address, size, data]   a bus cycle

and this core counts the second kind only (4 clocks each). The residue

    length - 4 x (number of r/w transactions)

is therefore "the clocks the reference charges beyond the bus": the internal work
the core does not model. This script tabulates that residue over a whole corpus
directory and says, per instruction shape, whether it is one constant.

It also splits off the records whose `length` is not an instruction's at all:
a faulting record runs the exception entry too (frame writes, the vector read, the
handler's first prefetches), so its residue is the entry's cost and must not be
mixed into a per-opcode table.

Usage:
    scripts/corpus-length-census.py                       # tests/680x0/v1 (the sample)
    scripts/corpus-length-census.py --dir tests/680x0/full # the whole 1 000 060
    scripts/corpus-length-census.py --table                # + the per-family markdown table
    scripts/corpus-length-census.py --shapes               # + every varying shape

No dependencies beyond the standard library; reads .json and .json.gz.
"""

import argparse
import collections
import glob
import gzip
import json
import os
import re
import sys

# --- EA modes, in the order the manual's table uses them -----------------------
# The corpus's own operand text is the key: it names the shape the reference saw,
# so no encoding has to be decoded (and a wrong decoder cannot silently agree).
EA_MODES = [
    ("Dn", r"D\d"),
    ("An", r"A\d"),
    ("(An)", r"\(A\d\)"),
    ("(An)+", r"\(A\d\)\+"),
    ("-(An)", r"-\(A\d\)"),
    ("(d16,An)", r"\(d16, A\d\)"),
    ("(d8,An,Xn)", r"\(d8, A\d, Xn\)"),
    ("(d16,PC)", r"\(d16, PC\)"),
    ("(d8,PC,Xn)", r"\(d8, PC, Xn\)"),
    ("(xxx).w", r"\(xxx\)\.w"),
    ("(xxx).l", r"\(xxx\)\.l"),
    ("#imm", r"#"),
    ("Q", r"Q"),  # the shift/quick immediate, named "Q" by the corpus
]

NAME = re.compile(r"^[0-9a-f]+ \[(.+?)\](?: \d+)?$")


def ea_mode(operand):
    """Collapse an operand text to its effective-address mode."""
    for label, pattern in EA_MODES:
        if re.fullmatch(pattern, operand):
            return label
    return operand


def split_operands(text):
    """Split an operand list on top-level commas (parentheses nest)."""
    out, depth, current = [], 0, ""
    for character in text:
        if character == "(":
            depth += 1
        elif character == ")":
            depth -= 1
        if character == "," and depth == 0:
            out.append(current.strip())
            current = ""
        else:
            current += character
    if current.strip():
        out.append(current.strip())
    return out


def parse_name(name):
    """'426a [CLR.w (d16, A2)] 7' -> ('CLR.w', ['(d16, A2)'])."""
    match = NAME.match(name)
    if not match:
        return None, None
    body = match.group(1)
    mnemonic, _, rest = body.partition(" ")
    return mnemonic, split_operands(rest) if rest else []


def open_corpus(path):
    if path.endswith(".json.gz"):
        return gzip.open(path, "rt", encoding="utf-8")
    return open(path, encoding="utf-8")


def looks_like_tests(path):
    """68000.official.json is upstream's decode map (an object): skip it."""
    with open_corpus(path) as stream:
        first = stream.read(1)
        while first and first.isspace():
            first = stream.read(1)
        return first == "["


def bus_cycles(transactions):
    return sum(1 for entry in transactions if entry[0] in ("r", "w"))


def clocked(transactions):
    """The reference's own internal clocks: its 'n' and 't' transactions."""
    return sum(entry[1] for entry in transactions if entry[0] in ("n", "t"))


def ends_in_exception(transactions):
    """A supervisor data read inside the vector table is an exception entry.

    Vectors 0-15 live at 0x00-0x3F; no ordinary instruction reads there. This is
    what separates a fault's `length` (instruction + entry + the handler's first
    prefetches) from an instruction's own.
    """
    return any(
        entry[0] == "r" and entry[2] == 5 and entry[3] < 0x40
        for entry in transactions
    )


def opcode_name(path):
    """The corpus's own file name: 'ADDX.w.json.gz' -> 'ADDX.w'."""
    name = os.path.basename(path)
    if name.endswith(".json.gz"):
        return name[: -len(".json.gz")]
    if name.endswith(".json"):
        return name[: -len(".json")]
    return name


def census(directory):
    files = sorted(
        path for path in glob.glob(os.path.join(directory, "*.json*")) if looks_like_tests(path)
    )
    if not files:
        sys.exit(f"no test files under {directory}")

    total = 0
    exceptions = 0
    traps = 0
    plain = 0
    lengths_disagree = 0
    shapes = collections.defaultdict(lambda: collections.defaultdict(collections.Counter))
    for path in files:
        name = opcode_name(path)
        with open_corpus(path) as stream:
            records = json.load(stream)
        for record in records:
            total += 1
            transactions = record.get("transactions") or []
            if 4 * bus_cycles(transactions) + clocked(transactions) != record["length"]:
                lengths_disagree += 1
            if ends_in_exception(transactions):
                exceptions += 1
                continue
            if name == "TRAP":
                traps += 1
                continue
            plain += 1
            mnemonic, operands = parse_name(record["name"])
            if mnemonic is None:
                continue
            shape = (mnemonic, tuple(ea_mode(operand) for operand in operands))
            residue = record["length"] - 4 * bus_cycles(transactions)
            shapes[name][shape][residue] += 1
    return total, exceptions, traps, plain, lengths_disagree, shapes


def report(directory, show_table, show_shapes):
    total, exceptions, traps, plain, disagree, shapes = census(directory)

    print(f"corpus                  {directory}")
    print(f"records                 {total:,}")
    print(f"  exception entries     {exceptions:,}  ({100 * exceptions / total:.1f} %) — a fault's length carries the entry")
    print(f"  TRAP file             {traps:,}  ({100 * traps / total:.1f} %) — always an entry")
    print(f"  plain instructions    {plain:,}  ({100 * plain / total:.1f} %)")
    print(f"length != 4xbus + n/t   {disagree:,}  (0 means the reference's list and its length agree by construction)")

    groups = [(file, shape, counter) for file, by_shape in shapes.items() for shape, counter in by_shape.items()]
    constant = [group for group in groups if len(group[2]) == 1]
    covered = sum(sum(group[2].values()) for group in constant)
    print()
    print(f"instruction shapes      {len(groups):,}")
    print(f"  one residue each      {len(constant):,}  ({100 * len(constant) / len(groups):.1f} %)")
    print(f"  records they cover    {covered:,}  ({100 * covered / plain:.2f} % of the plain instructions)")

    varying = collections.Counter()
    for file, shape, counter in groups:
        if len(counter) > 1:
            varying[file] += sum(counter.values())
    print()
    print(f"records in a varying shape   {sum(varying.values()):,}")
    for file, count in varying.most_common():
        print(f"  {file:<12} {count:>7,}")

    if show_table:
        print()
        print("## The residue per family, by effective address")
        print()
        print("`residue` = the clocks the reference charges beyond 4 a bus cycle. One row per")
        print("(family, source EA); a second number is given where the destination's EA mode")
        print("moves it. A blank means the family has no records of that shape.")
        for file in sorted(shapes):
            by_source = collections.defaultdict(set)
            for (mnemonic, modes), counter in shapes[file].items():
                if len(counter) == 1:
                    by_source[modes[0] if modes else "-"].add(next(iter(counter)))
            order = {label: index for index, (label, _) in enumerate(EA_MODES)}
            stable = {key: next(iter(values)) for key, values in by_source.items() if len(values) == 1}
            stable = dict(sorted(stable.items(), key=lambda pair: (order.get(pair[0], 99), pair[0])))
            unstable = {key: sorted(values) for key, values in by_source.items() if len(values) > 1}
            if not stable:
                continue
            row = ", ".join(f"{key} {stable[key]}" for key in stable)
            print(f"- **{file}** — {row}")
            if unstable:
                print(f"  - destination/size-dependent: {unstable}")

    if show_shapes:
        print()
        print("## Every varying shape")
        for file in sorted(shapes):
            for (mnemonic, modes), counter in sorted(shapes[file].items()):
                if len(counter) > 1:
                    print(f"- {mnemonic} {list(modes)}: {sorted(counter)} ({sum(counter.values())} records)")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dir", default="tests/680x0/v1", help="corpus directory (default: the sample)")
    parser.add_argument("--table", action="store_true", help="print the per-family residue table")
    parser.add_argument("--shapes", action="store_true", help="print every varying shape")
    arguments = parser.parse_args()
    report(arguments.dir, arguments.table, arguments.shapes)


if __name__ == "__main__":
    try:
        main()
    except BrokenPipeError:  # piping into head is not an error
        pass
