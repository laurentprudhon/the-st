# The comments pass — one method per commit, and what a listing costs

The format (the state line's syntax, column 104, the `ours:` rule, the checker) is
`design/cpu-scripts.md`'s last section and the skill's own entry. This file is what a converter needs
before starting: which methods can be converted, how to grade one, and the tables that were measured
rather than guessed.

## 1. A candidate must be a script method

Naming a listing's states is only honest where the code expands them. `EXG`, `LINK`, `SWAP`, `TAS`'s
register form and `NBCD` have listings of two to five states and **no script**: the core expands them as
short handlers in `M68000.Instructions.cs`, with a `M68000.Timing.cs` row for their clocks, so there is no
C# for their address latches or their restore states. Candidates live in `M68000.Scripts.cs`; the pair's
own helper in `M68000.cs` is the only exception.

Paste a state's **name**, address, statements and charge from `microcode-walk.py --op` only. MAME's data-flow
rendering (`m68000gen.py sdf`) renames the same states — `bsri1`–`bsri3` for a byte form's prologue where the
listing says `bsrw1`–`bsrw3` — and a label taken from it fails the checker's ROM index; the generated C++ is
the reference for the *order* of statements, never for labels. Hand-eliding every state is the slow part of
the pass: a throwaway script that prints `name (addr): stmt; stmt; read at m_aob` with the engine's
boilerplate already stripped (`m_base_ssw`, the suspension block and `m_inst_substate`, every `m_icount`, the
odd-address branch, `set_ftu_const()`, `m_inst_state`/`m_next_state`, `m_irc = m_edb`, `m_dbin = m_edb`, the
`// alu r=…` line) turns each state into one line to copy and pad to column 104.

Cheapest first, by measured shape:

| method | opcodes | states |
|---|---|---|
| `BranchSubroutineAsync` | `6100`, `61ec` | `bsrw1`–`bsrw3`, `malw3` + the pair |
| `CompareMemoryAsync` | `b108`, `b148`, `b188` | `cmmw1`–`cmmw4`; `cmml1`–`cmml7` |
| `JumpSubroutineAsync` | `4e90`… | `jsra1`, `jsaw2`, `jsaw3` (`jsal1`/`jsal2` for the long) + the pair |
| `JumpAsync` | `4ed0`… | `jmpa1`/`jmpd1`/`jmaw1`/`jmal1`+`jmal2`/`jmpx0`–`jmpx3`, then `bbci3` + the pair |
| `CheckAsync` | `4180` | `chkr1`, `chkr2`, `trap1`–`trap9`, the pair, `chkr3`, `mmrw3`, `chkr4` |

Converted already: the pair's helper, `BranchSubroutineAsync`, `CompareMemoryAsync`, and the operand pair with
the memory tail (`adrl1`/`adrl2`, `adsl2`/`adsl3`, `morl1`/`morl2`/`morw2`; `--complete --op 8190` reads 0).
Work several methods in one session, one commit each; the handoff is written when the context is full, not
once per instruction.

`AluIntoDataRegisterAsync` is one helper over **two** listings: `roml1`–`roml3` for a memory source and
`rorl1`–`rorl3` for a register source, and the last state's charge differs (4 against 2). Name both sets in
the one block, each in the file's order — `--complete` filters to the listing it was asked for, so both read 0.

`JumpSubroutineAsync`'s indexed case is the one to read twice: `--op 4eb0` prints `jsrx2` **and** `jsrx3`
because `jsrx1` tests the extension word's long-index bit, so the two are alternatives and the code takes one
— write the branch into the comment before naming either.

Last are `MUL`/`DIV`'s loop states and `MOVEM`'s walk: a state named twice inside one method is a finding,
and those loops repeat their block within a single listing, so the loop body is named once, in prose.
`JumpAsync` writes the pair **inline** (`_flowTransferred`, `_ird`, `_ir`) instead of calling
`RefillAsync`, so it becomes the second site naming `b`/`mmrw3` — allowed, since the duplicate rule is per
method.

## 1b. A shared tail is one method, and the method owns its states

The die reuses tails between scripts the way a program reuses functions, so the C# must too — two
inline copies of the same tail is where the two scripts drift apart, and the comment pass is what
makes the copies visible. Before converting a method, ask which of its statements *are* another
method's states; the answer is usually yes at the end:

| helper | states it expands |
|---|---|
| `CloseInstructionAsync` / `TrailingFetchAsync` | the pair `b`+`mmrw3` of every fall-through |
| `RefillAsync` | `malw3` + the pair, for a transfer that must refill two words |
| `RefillFromAsync(target, first)` | the pair alone, when the script's own last state *is* the target's read (`bbci3`, `jsrd3`) |
| `PushReturnAddressAsync` | `bsrw2`/`bsrw3` and `jsaw2`/`jsaw3` — one helper, two listings |
| `AluIntoDataRegisterAsync` | `roml1`/`roml2` + `rorl1`–`rorl3`, both on the shared `roml3` tail |

A script that ends on a shared part *calls* it and names it in prose; the helper carries the state
lines, and `--complete` reads across files so the script's listing is still satisfied. **The order test
runs one method at a time** (`fc33348`): a listing whose states live in three helpers — `adsl1` in
`ReadLongEaAsync`, `adsl2`/`adsl3` in `ReadLongOperandFromStreamAsync`, `roml1`..`roml3` in
`AluIntoDataRegisterAsync` — cannot be compared as one file-order sequence, but each method's own
sequence must follow the listing, which is the check that catches a block pasted one state too high.
States named **once** only, still: a helper's state shared with another method says nothing by position,
so never name a helper's state a second time at a call site.

## 1c. Traps the pass paid for

- **A state whose name carries `#` must be matched by the index's label pattern**, or the state *before* it
  absorbs the whole block's charge — `chkr4`, `roml3`, `dvs1c`, `mulm3`, `mulm6`, `roal4`, `bcsr5`, `btsr3`
  and `rcal3` read 4 where the ROM charges 0 or 2, and `e#w1`/`e#l1`/`o#w1`/`o#l1` could not be named at all.
  The pattern takes `#` since `5f2f41f`; the immediate family is nameable now.
- **A state that ends `goto mmrw3` charges nothing itself** — `chkr4` is CHK's own `b` (four statements, no
  `m_icount`), and the read is `mmrw3`'s. Write the 0, not the 4 the old index reported.
- **An inlined trap path is the exception entry's code.** `--op 4180` lists `chkr1`, `chkr2`, `trap1`…`trap9`,
  the pair, `chkr3`, `mmrw3`, `chkr4`: the nine `trap` states are `EnterExceptionAsync(6, 0)`'s, entered from
  `chkr2`'s branch *and* `chkr3`'s sign test, so they are pasted once above the two calls and the paragraph
  says so. The pair in the middle is why `--complete --op 4180` needs it named somewhere that counts as
  "named more than once" or the order test fails on a listing whose states span two files.
- **A rebase after a gate run invalidates the stamp** (the stamp quotes `child HEAD / parent <ref>`), and
  other sessions land docs hourly — fetch, rebase, and re-run the gate on the tree you are about to push.

## 2. Which listings end on the closing pair

Measure the tail (`microcode-walk.py --op <op> | grep -E '^\s*// [0-9a-f]{3} '`); do not assume a family
falls through to it:

| opcode | tail |
|---|---|
| `4e71` NOP | `b` `mmrw3` — the whole listing |
| `4e40` TRAP | `trap1`–`trap9` `b` `mmrw3` |
| `4e90` JSR (An), `4eb9` JSR (xxx).l | `jsra1` `jsaw2` `jsaw3` (`jsal1` `jsal2`) `b` `mmrw3` |
| `33c0` MOVE.w Dn,(xxx).w | `ralw1` `ralw2` `maww2` `b` `mmrw3` |
| `4180` CHK | `chkr1` `chkr2` `trap1`–`trap9` `b` `chkr3` `mmrw3` `chkr4` |
| `3080` MOVE.w Dn,(An) | … `mmrw3` — **alone, no `b`** |
| `48a7` MOVEM store | `push1` `push2` `push5` `push3` `mmrw3` — alone |
| `303c` MOVE.w #,D0 | `e#w1` `rrgw1` `rrgw2` — no pair at all |

A script whose own last state *is* `mmrw3` (MOVE's `(An)`/`(An)+` store shapes, `MOVEM`'s store direction)
names it again in its own block; the checker's order test reading only states named once is what keeps that
from looking like a misordering.

## 3. Grading one method, in the order that has worked

1. Rebase onto `origin/main` **before** grading: the gate quotes `child HEAD / parent <ref>`, and a rebase
   after the run makes the stamp describe a commit that never existed. Other sessions land docs hourly.
2. `python3 scripts/comment-states.py --check src/TheST.Machine/Cpu/*.cs` → 0 findings in 10 files.
3. `python3 scripts/comment-states.py --complete --op <op> <files in reading order>` → 0. The script's
   file comes first and `M68000.cs` second, because the pair's block is there.
4. The comments-only guard with the metadata lines and `//` lines dropped → empty:
   `git diff -U0 -- 'src/TheST.Machine/Cpu/*.cs' | grep -E '^[+-]' | grep -vE '^(\+\+\+|---) ' | grep -vE '^[+-][[:space:]]*//'`
5. Commit, then `scripts/cpu-shape-check.sh <the commit you rebased onto>` on the **clean** tree, then push
   (fast-forward `main` in the primary checkout, push, fast-forward the worktree back).

The guard is line-shaped, so it cannot see a trailing comment edited in place: a conversion **adds whole
comment lines** and leaves every code line, trailing comments included, exactly as it stands. Where a
statement already carries a trailing state name, the state line goes above the group of statements.

## 4. Proving a comments-only tree costs nothing

A comment edit still moves the assembly's identity bytes, so "identical DLL" is the wrong test and a plain
byte diff will always show something. Build both sources **in the same tree** and diff them:

```bash
f=src/TheST.Machine/Cpu/<file>.cs
cp "$f" /tmp/child.cs
dotnet build TheST.sln -c Release && cp src/TheST.Machine/bin/Release/net11.0/TheST.Machine.dll /tmp/child.dll
git show <parent>:"$f" > "$f"
dotnet build TheST.sln -c Release && cp src/TheST.Machine/bin/Release/net11.0/TheST.Machine.dll /tmp/parent.dll
cp /tmp/child.cs "$f"     # restore, and rebuild before you grade anything
```

A comment-only diff leaves a handful of short differing runs and nothing else: the PE timestamp (8 bytes
into the COFF header, at `e_lfanew + 8`), the MVID (16 bytes, the first entry of the metadata `#GUID`
stream) and the debug directory's CodeView/PDB stamps. Roslyn derives the MVID and the PDB checksum from
the compilation *inputs*, comments included, which is why they move. Any differing byte inside `.text`
outside those runs is a code change. Stdlib Python names the runs: read `e_lfanew` at `0x3c`, the optional
header's data-directory entry 14 (the CLI header), the metadata root's stream headers, then the `#GUID`
stream's first 16 bytes.

A build that returns in a fraction of a second usually recompiled nothing — check that the two DLL hashes
differ before concluding the sources differ.

## 5. A failing G8 on a loaded host is the host

A comments-only change cannot move frame time, so a failing G8 row on such a tree is measurement noise.
Read `/proc/loadavg` before believing a figure, run `dotnet build-server shutdown` first, and judge by the
parent's own spread: a parent reading 9.54 / 10.44 / 9.80 ms across three pairs cannot resolve a 0.3 ms
delta. Re-run near idle and quote the rerun's stamp, not the first one.
