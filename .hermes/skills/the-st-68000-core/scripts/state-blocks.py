#!/usr/bin/env python3
"""Print opcodes' listings compacted to what a state comment carries (the-st's comments pass).

    python3 state-blocks.py 4e90 4ea8 ...

For each state of each opcode's listing it prints one line

    <state> (<addr>): <statements>

where the statements are the listing's own, with the engine's boilerplate dropped and the accesses
collapsed — i.e. the text a state line pastes, in order. Add the charge from
`scripts/comment-states.py --index` and pad to column 104.

Dropped (what the convention elides): `m_base_ssw`, the suspension block and its `m_inst_substate`,
every `m_icount`, the odd-address branch, `set_ftu_const()`, `m_inst_state = …`, the `m_next_state`
trace lines, `m_irc = m_edb` / `m_dbin = m_edb`, and the per-state `// alu r=…` comment. A read
becomes `read at <expr>` and a write `write m_dbout at <expr>`, the `& ~1` word alignment dropped.

The listing stays the authority for the *names* — never the generated data-flow C++, which labels the
same script differently (`bsri1` where the listing says `bsrw1`). This spares only the mechanical part
of the transcription.
"""

import os
import re
import subprocess
import sys

ROOT = os.environ.get("THE_ST_ROOT", "/opt/data/the-st")
WALK = os.path.join(ROOT, "scripts", "microcode-walk.py")

LABEL = re.compile(r"^// ([0-9a-f]{3}) ([a-z][a-z0-9_]*)$")
BOILER = re.compile(
    r"^(m_base_ssw\s*=|m_edb\s*=\s*m_(opcodes|program)\.|m_icount\s*[-+]?=|if\(|else$|\}|"
    r"return;|m_inst_substate|m_inst_state\s*=|set_ftu_const\(\)|m_next_state|m_irc = m_edb|"
    r"m_dbin = m_edb|goto |[a-z0-9_]+:$|//|#)"
)
READ = re.compile(r"^m_edb = m_(?:opcodes|program)\.read_interruptible\((.*?)\);?$")
WRITE = re.compile(r"^m_program\.write_interruptible\((.*?), m_dbout\);?$")


def main():
    if not os.path.exists(WALK):
        sys.exit(f"state-blocks: no {WALK} (set THE_ST_ROOT, or fetch the microcode first)")
    for opcode in sys.argv[1:]:
        done = subprocess.run([sys.executable, WALK, "--op", opcode], capture_output=True, text=True)
        if done.returncode != 0:
            sys.exit(done.stderr)
        print(f"### {opcode}")
        label, statements = None, []

        def flush():
            if label and statements:
                print(f"  {label}: " + "; ".join(statements))

        for raw in done.stdout.splitlines():
            line = raw.strip()
            m = LABEL.match(line)
            if m:
                flush()
                label, statements = f"{m.group(2)} ({m.group(1)})", []
                continue
            if label is None or not line:
                continue
            read = READ.match(line)
            if read:
                statements.append(f"read at {read.group(1).replace(' & ~1', '')}")
                continue
            write = WRITE.match(line)
            if write:
                statements.append(f"write m_dbout at {write.group(1).replace(' & ~1', '')}")
                continue
            if BOILER.match(line):
                continue
            statements.append(line.rstrip(";"))
        flush()


if __name__ == "__main__":
    main()
