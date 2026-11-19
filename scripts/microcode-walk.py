#!/usr/bin/env python3
"""Print MAME's microcode walk for a 68000 opcode (or one exception state).

The 68000 is microcoded: an instruction is a script of micro-states, two clocks each, four if the
state touches the bus. MAME's `m68000gen.py` carries that transcribed ROM and can generate the
script as C++; this tool runs it for one opcode or one state so a question like "where does this
clock sit", "what does this frame save" or "when does the post-increment commit" is answered by
reading the hardware's own state list instead of guessing.

    scripts/microcode-walk.py --op 80a9              # OR.l (d16,An),D0
    scripts/microcode-walk.py --op 4e75              # RTS
    scripts/microcode-walk.py --state address_error  # the entry script
    scripts/microcode-walk.py --list                 # the entry states worth reading

Requires `scripts/fetch-m68000-microcode.sh` first. Standard library only.

The generator is not importable (its main body runs at import), so it is executed with a patched
argv; the one call in its `s` mode that omits `gen_mode` is patched too — upstream's `i` mode is
fine as is.
"""

import argparse
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VENDOR = os.path.join(ROOT, "research", "vendor", "m68000")
GEN = os.path.join(VENDOR, "m68000gen.py")
LST = os.path.join(VENDOR, "m68000.lst")

# The generator's `states` table holds the entry scripts only — the per-instruction states are
# reached through an opcode (`--op`), not by name.
STATES = [
    ("address_error", "group-0 entry: the frame's words, SSW, the vector"),
    ("bus_error", "group-0 entry, the bus-error vector"),
    ("illegal", "group-1/2 entry (illegal, privilege, TRAP are separate vectors)"),
    ("trace", "the trace exception"),
    ("interrupt", "autovector entry: the mask, the vector lookup"),
    ("linea", "the A-line trap"),
    ("linef", "the F-line trap"),
    ("reset", "the reset sequence"),
    ("double_fault", "the halt path"),
]


def run(argv):
    """Execute the generator with `argv`, returning its stdout."""
    with open(GEN, "r", encoding="utf-8") as f:
        source = f.read()

    # upstream's `s` mode calls generate_intermediate_code_from_state(state) with one argument
    # short; supply the mode its sibling call sites use.
    source = source.replace(
        "code = generate_intermediate_code_from_state(state)",
        "code = generate_intermediate_code_from_state(state, GEN.direct|GEN.full)",
    )

    import contextlib
    import io

    saved = sys.argv
    sys.argv = argv
    out = io.StringIO()
    try:
        with contextlib.redirect_stdout(out):
            exec(compile(source, GEN, "exec"), {"__name__": "__main__"})
    finally:
        sys.argv = saved

    return out.getvalue()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--op", help="an opcode word, hex (e.g. 3e31); the decode table picks the script")
    ap.add_argument("--state", help="an exception/utility state name (see --list)")
    ap.add_argument("--list", action="store_true", help="list the states worth reading")
    args = ap.parse_args()

    if not (os.path.exists(GEN) and os.path.exists(LST)):
        sys.exit("run scripts/fetch-m68000-microcode.sh first")

    if args.list:
        for name, what in STATES:
            print(f"{name:<14} {what}")
        return

    if args.state:
        known = {name for name, _ in STATES}
        if args.state not in known:
            sys.exit(f"--state takes an entry script: {', '.join(sorted(known))}\n"
                     f"(a per-instruction state is reached through its opcode: --op 4e75)")
        text = run([GEN, "s", LST, args.state])
    elif args.op:
        opcode = args.op.lower().removeprefix("0x")
        text = run([GEN, "i", LST, opcode])
    else:
        ap.error("one of --op, --state, --list")

    if "Illegal instruction" in text:
        sys.exit(f"{args.op}: not in m68000.lst (an illegal line, or a typo)")
    print(text.rstrip())

    buses = text.count("m_icount -= 4;")
    idles = text.count("m_icount -= 2;")
    print()
    print(f"# bus accesses in this listing: {buses}   idle states: {idles}")
    print("# 4 clocks a bus access, 2 a state; loops (shifts, MUL, DIV) repeat their block,")
    print("# so this is a listing, not a total — the total is the script's length.")


if __name__ == "__main__":
    main()
