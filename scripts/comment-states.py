#!/usr/bin/env python3
"""Check a C# script's micro-state comments against the microcode listing (#102).

The convention (`design/cpu-scripts.md`, ticket #102): every block of C# that expands a
microcode state carries one line above it, in the listing's own syntax —

    // <name> (<addr>)  <statements>                                       <charge>

— so a reader standing in the function sees which silicon state the code is, and a reviewer
diffs the comment against `scripts/microcode-walk.py --op <opcode>` instead of trusting it.

    scripts/comment-states.py --check FILE...              # every formatted comment in the files
    scripts/comment-states.py --complete --op 4e70 FILE... # every state of that listing, in order
    scripts/comment-states.py --index | grep rset          # the state index it checks against

What it checks, and nothing else:

  · the `(name, addr)` pair is a state the generator knows (a pair the ROM does not have is a
    finding, not a typo to fix by eye);
  · the charge is the state's **own** — 2 for a state, 4 for a state that touches the bus, 0 for
    one the listing leaves uncharged — and a fused pair's is the sum of its parts, in the pair's
    order (`b (363) + mmrw3 (34c)` is `0+4`: `b` is uncharged and the read is `mmrw3`'s); a state's
    name may carry `#` (`e#w1`), and a name the pattern cannot match is a name the index lacks — so
    the state *before* it would absorb its block's charge;
  · the line ends at column 104 (the charge right-aligned there), and continuation lines of the
    same state carry no name of their own;
  · a state is not commented twice inside one method;
  · with `--complete`, every state of the opcode's listing appears, once, in listing order —
    read across **every** file given (the closing pair `b`/`mmrw3` is the loop's, in `M68000.cs`,
    while the script that falls through to it is in `M68000.Scripts.cs`).

It cannot check that the C# *is* the state — no instrument reads a comment (`design/cpu-silicon.md`
§5). It checks the comment against the listing, which is what makes the comment checkable at all.

The index is built from the pinned generator: `m68000gen.py sdf` prints every state of the ROM with
its `// <addr> <name>` label and its own `m_icount` decrement. Standard library only; run
`scripts/fetch-m68000-microcode.sh` first.
"""

import argparse
import os
import re
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VENDOR = os.path.join(ROOT, "research", "vendor", "m68000")
GEN = os.path.join(VENDOR, "m68000gen.py")
LST = os.path.join(VENDOR, "m68000.lst")
WALK = os.path.join(ROOT, "scripts", "microcode-walk.py")

# The comment format's own column: the charge is right-aligned so the line ends here.
WIDTH = 104

# `// <name> (<addr>)  <statements>  <charge>` — the charge is the last token of the line.
# A name may carry `#` (the immediate forms: `e#w1`, `e#l1`, `o#w1`, `o#l1`); a name the pattern
# cannot match is not a name the index has, and then the state *before* it absorbs its block's
# charge — which read `chkr4`, `dvs1c`, `roml3` and five more as 4 for a while.
NAME = r"[a-z][a-z0-9_#]*"
STATE = re.compile(rf"^\s*//\s+({NAME})\s+\(([0-9a-f]{{3}})\)\s*(.*)$")
# A state fused into a helper names both: `// b (363) + mmrw3 (34c)  …  0+4`.
FUSED = re.compile(rf"^\+\s*({NAME})\s+\(([0-9a-f]{{3}})\)\s*")
CHARGE = re.compile(r"(\d+(?:\+\d+)?)\s*$")
CONTINUATION = re.compile(r"^\s*//\s{2,}\S")
OURS = re.compile(r"^\s*//\s+ours:")
LABEL = re.compile(rf"^\s*//\s+([0-9a-f]{{3}})\s+({NAME})\s*$")
METHOD = re.compile(r"^\s{4}(?:public|private|internal|protected|static|async|sealed|override|void|ValueTask|Task|bool|uint|int|ushort|byte)\b")


def state_index():
    """`(name, addr)` → the state's own charge, from the generator's whole-ROM dump."""
    with tempfile.TemporaryDirectory() as scratch:
        dump = os.path.join(scratch, "all-states.cpp")
        with open(os.devnull, "w") as quiet:
            subprocess.run([sys.executable, GEN, "sdf", LST, dump], stdout=quiet, check=True)
        with open(dump, "r", encoding="utf-8", errors="replace") as f:
            lines = f.read().splitlines()

    index = {}
    current = None
    charges = {}
    for line in lines:
        label = LABEL.match(line)
        if label:
            current = (label.group(2), label.group(1))
            charges.setdefault(current, set())
            continue
        if current is not None and "m_icount -= 4;" in line:
            charges[current].add(4)
        elif current is not None and "m_icount -= 2;" in line:
            charges[current].add(2)

    for pair, seen in charges.items():
        # A state's own cost is its `m_icount` decrement: 4 for a bus access, 2 for an idle state,
        # and **0** for a state that has neither (`rorw1`, `adrw1`, `cpmm1`, `chkr4` — the listing's
        # own silence is the charge, which is why `OR.w Dn,Dn` is 4 clocks). A state carrying both is a
        # bus state whose 2 is a fused idle inside it.
        index[pair] = 4 if 4 in seen else (2 if 2 in seen else 0)
    return index


def opcode_listing(opcode):
    """The `(name, addr)` states of one opcode's script, in listing order."""
    done = subprocess.run([sys.executable, WALK, "--op", opcode], capture_output=True, text=True)
    if done.returncode != 0:
        sys.exit(f"comment-states: microcode-walk.py --op {opcode} failed:\n{done.stderr.strip()}")
    found = []
    for line in done.stdout.splitlines():
        label = LABEL.match(line)
        if label:
            found.append((label.group(2), label.group(1)))
    return found


def fused_pairs(state):
    """Every `(name, addr)` one state line names — one, or a fused pair's two (`+ name (addr)`)."""
    pairs = [(state.group(1), state.group(2))]
    rest = state.group(3)
    while True:
        fused = FUSED.match(rest)
        if not fused:
            return pairs
        pairs.append((fused.group(1), fused.group(2)))
        rest = rest[fused.end():]


def blocks(lines):
    """Group the file's comment lines into state blocks: (line number, pairs, text, lines).

    A block is a state line plus the continuation lines that wrap its statements; the charge is the
    last token of the block's **last** line (the format right-aligns it at column 104 there). A
    fused line names both states, and its charge has one part per state, in the same order.
    """
    number = 0
    while number < len(lines):
        state = STATE.match(lines[number])
        if not state:
            number += 1
            continue
        start = number
        text = [lines[number]]
        while number + 1 < len(lines) and CONTINUATION.match(lines[number + 1]):
            number += 1
            text.append(lines[number])
        yield start + 1, fused_pairs(state), " ".join(part.strip(" \t/") for part in text), text
        number += 1


def check_file(path, index, findings):
    with open(path, "r", encoding="utf-8") as f:
        lines = f.read().splitlines()

    method_no = 0
    seen = set()
    blocks_by_start = {start: (pairs, text, block) for start, pairs, text, block in blocks(lines)}
    for number, line in enumerate(lines, start=1):
        if METHOD.match(line) and "(" in line:
            method_no += 1
        if number not in blocks_by_start:
            continue

        pairs, text, block = blocks_by_start[number]
        named = " + ".join(f"{name} ({addr})" for name, addr in pairs)
        charge = CHARGE.search(text)
        if charge is None:
            findings.append(f"{path}:{number}: `{named}` carries no charge")
        else:
            stated = charge.group(1)
            parts = [int(part) for part in stated.split("+")]
            if len(parts) != len(pairs):
                findings.append(f"{path}:{number}: `{named}` names {len(pairs)} state(s) and its charge "
                                f"`{stated}` has {len(parts)} part(s)")
            for (name, addr), part in zip(pairs, parts):
                if (name, addr) not in index:
                    findings.append(f"{path}:{number}: `{name} ({addr})` is not a state of the ROM")
                elif index[(name, addr)] != part:
                    findings.append(f"{path}:{number}: `{name} ({addr})` charges {part}, the state's own is "
                                    f"{index[(name, addr)]}")
        for offset, part in enumerate(block):
            if len(part) > WIDTH:
                findings.append(f"{path}:{number + offset}: line is {len(part)} wide, the format's column is {WIDTH}")
        for name, addr in pairs:
            if (method_no, name, addr) in seen:
                findings.append(f"{path}:{number}: `{name} ({addr})` is commented twice in one method")
            seen.add((method_no, name, addr))
    return findings


def check_complete(paths, index, opcode, findings):
    """The opcode's whole listing, once each and in order — read across **every** file given.

    A listing is not always one file's: the closing pair `b` (363) + `mmrw3` (34c) is the loop's
    (`M68000.cs`), while the script that falls through to it is a method of `M68000.Scripts.cs`, so
    `--complete --op 4e71 M68000.cs` (NOP is nothing but that pair) and a family's listing split
    between the two are the same check. The files are read in the order they are given and each in
    line order.

    The order test reads only the states that are named **once** in the files: a state shared with
    another method (`mmrw3` is also a script's own last state) is named where that other method needs
    it, so its position says nothing about this listing's; its presence still counts.
    """
    wanted = opcode_listing(opcode)
    if not wanted:
        sys.exit(f"comment-states: --op {opcode} has no states in the listing")

    found = []
    for path in paths:
        with open(path, "r", encoding="utf-8") as f:
            for line in f.read().splitlines():
                state = STATE.match(line)
                if state:
                    found.extend(fused_pairs(state))

    label = " ".join(paths)
    seen = {}
    for pair in found:
        seen[pair] = seen.get(pair, 0) + 1

    for pair in wanted:
        if pair not in seen:
            findings.append(f"{label}: the listing's `{pair[0]} ({pair[1]})` is not commented")

    order = [pair for pair in found if pair in wanted and seen[pair] == 1]
    if order != [pair for pair in wanted if seen.get(pair) == 1]:
        findings.append(f"{label}: `{opcode}`'s states are not in listing order")
    return findings


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("files", nargs="*", help="the C# file(s) whose comments are checked")
    parser.add_argument("--check", action="store_true", help="check every formatted comment (the default with files)")
    parser.add_argument("--complete", action="store_true", help="require the opcode's whole listing, in order")
    parser.add_argument("--op", help="the opcode whose listing --complete reads (hex, e.g. 4e70)")
    parser.add_argument("--index", action="store_true", help="print the state index")
    args = parser.parse_args()

    if not (os.path.exists(GEN) and os.path.exists(LST)):
        sys.exit("comment-states: run scripts/fetch-m68000-microcode.sh first")

    if args.index:
        for (name, addr), charge in sorted(state_index().items(), key=lambda item: (item[0][1], item[0][0])):
            print(f"{addr} {name:<16} {charge}")
        return 0

    if not args.files:
        parser.error("give at least one file (or --index)")

    index = state_index()
    findings = []
    for path in args.files:
        if not os.path.exists(path):
            sys.exit(f"comment-states: no such file: {path}")
        check_file(path, index, findings)

    if args.complete:
        if not args.op:
            parser.error("--complete needs --op <opcode>")
        check_complete(args.files, index, args.op, findings)

    for finding in findings:
        print(finding)
    if findings:
        print(f"\ncomment-states: {len(findings)} finding(s)")
        return 1
    print(f"comment-states: 0 findings in {len(args.files)} file(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
