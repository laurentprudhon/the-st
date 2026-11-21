#!/usr/bin/env python3
"""Generates the two variant CPU sources that cannot be expressed as a compile-time switch.

The A/B variants are the machine's own files, linked straight from `src/` and never copied. Two
variants do have to be different *code*:

  C  (Sync)   — the same instruction set with no async at all: every `await` is deleted, every
                `ValueTask<T>` becomes `T` (a forwarder keeps its body, so its leaf's type is what it
                returns), and the two real waits become direct device calls. `B - C` is then the price
                of the state machine itself.
  D  (Folded) — the same async code with the bus helpers folded into one leaf, so a bus cycle costs
                one suspended frame instead of three. `A - D` was the price of the extra frames until
                #62 landed the fold: since then A *is* folded and D is a control that must equal it.
                To re-run the pre-#62 contrast, check the sources of record out of `115da43` first.

Both are generated from the sources of record, so they cannot drift from the CPU they price.

    python3 gen.py            # writes Gen/Sync/ and Gen/Folded/
"""

import pathlib
import re

HERE = pathlib.Path(__file__).resolve().parent
SRC = HERE.parent.parent / "src" / "TheST.Machine" / "Cpu"


def read(name: str) -> str:
    return (SRC / name).read_text()


def strip_async(text: str) -> str:
    text = re.sub(r"async ValueTask<([^>\n]+)> ", r"\1 ", text)
    text = re.sub(r"ValueTask<([^>\n]+)> ", r"\1 ", text)
    text = re.sub(r"async ValueTask ", "void ", text)
    # a bare `ValueTask` is a wrapper's signature: #62's forwarders have no `async` to strip, and
    # their leaf is synchronous here, so the return type follows it
    text = re.sub(r"\bValueTask ", "void ", text)
    text = text.replace("await ", "")
    return text


def main() -> None:
    # The CPU is six partial files of one class (#78 split it into its die's blocks). Gen processes
    # every one of them: a variant that carried only two would be a bench against another core.
    sources = sorted(p.name for p in SRC.glob("M68000*.cs"))
    text = {name: read(name) for name in sources}

    def renamed(t: str, space: str) -> str:
        return t.replace("namespace TheST.Machine.Cpu;", f"namespace {space};")

    # ---- C: no async at all ----------------------------------------------------------------
    sync: dict[str, str] = {}
    for name, t in text.items():
        t = t.replace(
            "await WaitFor(long.MaxValue, Wires.Answer, _answerSeq);",
            "Engine.Inline(this, Wires.Answer, long.MaxValue, _answerSeq);   // the device answers on this stack",
        ).replace(
            "await WaitFor(takeable ? Clock + 4 * TicksPerCpuClock : long.MaxValue, Wires.Ipl, seq);",
            "Engine.Inline(this, Wires.Ipl, takeable ? Clock + 4 * TicksPerCpuClock : long.MaxValue, seq);",
        )
        t = strip_async(t).replace("_ = Run();", "Run();")
        sync[name] = renamed(t, "TheST.Machine.Cpu.Sync")

    # ---- D: the folded shape, which since #62 *is* the shape of record ----------------------
    # The bus's helpers are folded into one leaf in the core itself (A − D is spent: `A is folded and
    # D is a control that must equal it`, README). D is therefore the sources unchanged, in its own
    # namespace — the pre-#62 contrast reproduces by checking the sources of record out of 115da43.
    folded: dict[str, str] = {}
    for name, t in text.items():
        folded[name] = renamed(t, "TheST.Machine.Cpu.Folded")

    for variant, files in (("Sync", sync), ("Folded", folded)):
        target = HERE / "Gen" / variant
        target.mkdir(parents=True, exist_ok=True)
        for stale in target.glob("*.cs"):     # a file the core no longer has is not a variant's
            if stale.name not in files:
                stale.unlink()
        lines = 0
        for name, body in files.items():
            (target / name).write_text(body)
            lines += body.count("\n")
        print(f"wrote {target.relative_to(HERE)}/ ({lines} lines, {len(files)} files)")


if __name__ == "__main__":
    main()
