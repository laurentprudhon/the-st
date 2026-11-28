# The port, batch by batch — where it stands

Living note; rewrite it in place as each batch lands (the full handoff for the current batch is under
`~/handoffs/`). Ticket **#80** is the campaign: expand MAME's microcode as hand-written C#.

## 0. Where the tree is

```bash
cd /opt/data/the-st && git log --oneline -2
git worktree add <scratch> <commit>     # baseline without editing the tree
```

Gate after the `MOVEM` batch: MAME **314 706 / 317 500 (99.10 %), 111 of 127 files green**, exact-clock
**290 680 (91.6 %)**; CLK **864 132 / 1 000 060** (per-file state unmoved, exact-clock 742 400 → 746 837);
sample **2 551**; `validate.sh` **PASS**, desktop 618, G8 9.18 ms at n 767 in validate / 8.93 standalone.
On CLK **not one record moves** — its `MOVEM` records were already red on the frame's status byte.

## 1. What the `MOVEM` batch was (the shape to expect from a mask-driven loop)

Two files, 2 500 each, **both 100 % exact-clock**, and **not one of the three failures was a clock
charge**: the table was right all along. The listing says it before any record does — `movem_*_list` is
`10 bus accesses, 0 idle states` — so every clock is a read. What the corpus caught:

1. **The load direction's `(An)+` increment lands once, last** (`popm6`: `m_da[ry] = m_at`, reached only
   when `step_movem` drains the mask) — so *any* fault leaves `An` where it stood. Pre-committing
   `address + width` per register put 249 records 2 high, and the old comment claiming a faulting word
   transfer keeps its step was wrong.
2. **The PC-relative classes read the transfer with `SSW_PROGRAM`** (`movem_l_dpc_list` /
   `movem_l_axpc_list` never reset `m_base_ssw`), so a fault frames a *program* access — 73 records at
   `ssp+1`. `(xxx).w`/`.l` are the **data** class (`movem_l_adr32_list` sets `SSW_DATA` per access), so the
   predicate is `mode == 7 && reg is 2 or 3`; widening it to `mode == 7` trades 73 records for 81.
3. **A clean load reads one word *ahead* of the list** (`popm5`'s `m_aob = m_au`, `m_at = m_au` while it
   fills the register it just read), so one *discarded* `fc 5` word read lands past the last register:
   1 331 clean records were exactly 4 short, every faulting one already exact.

### The loop's arithmetic, and the one invariant

| direction | states | accesses |
|---|---|---|
| mem → regs, word | `popm1`, `popm2`, `popm4`*, `popm6` | `n + 1` data + 2 program |
| mem → regs, long | `popm1`, `popm2`, (`popm3`,`popm5`)*, `popm6` | `2n + 1` data + 2 program |
| regs → mem, upward | `stmr1`, `stmr2`, (`stmr4`,`stmr6` for long)*, `mmrw2` | `n`/`2n` writes + 2 program |
| regs → mem, `-(An)` | `push1`, `push2`, (`push4`,`push5`)*, `push3` | `n`/`2n` writes + 2 program |

`*` = the loop body. `length = 4 ×` that count (no idles; the indexed EA adds one `n 2` before the loop).
`step_movem` takes `countr_zero(mask)` and `map_sp` (bit 15 = the live SP); `step_movem_predec` uses
`r ^ 0xf`, which is why the predecrement mask reads bit-reversed and why it writes the long **low word
first, at the higher address**. The whole register half reduces to: **`m_at` holds the address of the most
recently issued transfer access, and `popm6`/`push3` publishes it as `An` exactly once** — which is both
why `An` ends one width past the list and why a fault leaves it alone.

The core keeps this family as a **handler**, not a script: with both files at 100 % exact-clock there is no
corpus signal to drive a `movem1…` state list, and a rewrite nothing can verify is not worth the risk.
The reading above lives in those two handlers as doc comments plus inline comments at each fault return and
at the single `An` write — comments only, and the family re-run as the proof (same verdicts).

## 2. Traps worth keeping

- **A uniformly 4-short *clean* column, with faulting records exact, is a missing access** — not a wrong
  table row. Split `our_total − length` by direction and by fault (`re`/`we` in the record's own
  `transactions`) before touching `M68000.Timing.cs`.
- **`--out` / `--failures` take a file path**; `--out <dir>` throws after the run.
- **A symlink dir for `--dir` needs both `X.json` and `X.json.bin`**; `.bin` alone gives
  `no test file matches ''`. Prefer a `--file` prefix only one file carries.
- **Run `boot` from the repo root** — elsewhere the harness aborts with a core dump.
- **A handoff's predicted cause is a hypothesis.** "The clock is the loop's per-register idle count" was
  the previous note's line and it was false; measure by direction and shape first.
- **`--reads` is the right instrument for a loop family**: it matched on 2 499 + 2 500 records before any
  clock work, so the remaining work was placement only. (Ours prints 24-bit-masked addresses where the
  corpus records whole ones; that difference is benign.)

## 3. Next, in the measurement's order

The 16 files still red, **measured on the `MOVEM` tree** (`--failures` + `--clocks` a file, 8 runs ≈ 10 s) —
the order is cheapest-first, and each step closes whole files:

| file(s) | state | exact-clock | what the dump says |
|---|---|---|---|
| `CMP.w` / `CMP.l` (their last 162 / 165) | 2 338 / 2 335 | 2 338 / 2 253 | **`CMPM`**: every failure is `ram` on `(An)+,(An)+`, plus 4 `a2` each on `(A7)+`; the residue is exactly **−4** on those same 162/165 records (the aborting read) and **+4** on 82 `CMP.l` ones |
| `ASR.b` / `ASR.l` | 1 966 / 2 208 | **2 500 / 2 500** | `sr` only, spread evenly over the eight `Dn` shapes — the arithmetic shift's **flag** semantics; clocks already right |
| `ASR.w` | 2 035 | 2 258 | the same `sr` class **plus** 64 `ram` on the indexed memory shape and −4 on 242 memory records |
| `MOVEtoCCR` | 2 211 | 159 | **−8** on 1 504, **−4** on 837 — a *charge* class (`stiw*`), nearly the whole file |
| `MOVEfromSR` | 2 251 | 163 | −4 on 1 490, −8 on 443, **−2** on 404 (the register form's idle) |
| `MOVEtoSR` | 2 377 | 1 360 | −8 on 766, −4 on 374, exact on 1 360 — shape-dependent |
| `ASL.w` `LSL.w` `LSR.w` `ROL.w` `ROR.w` `ROXL.w` `ROXR.w` | 2 415–2 435 | — | the shift-kind files, last |
| `Bcc` | 2 499 | — | one record; confirm it is upstream's before chasing |

1. **`CMPM`** — 327 records, one class, and **two files go green**. Three suspects, and the class listing
   (`cmpm*`) decides which: the aborting read's 4 (`ChargeAbortedReadAsync`), the group-0 frame, and the
   `A7` step. The +4 on 82 records is a second datum in the same file — do not let it become a clause.
2. **`ASR`'s flag rule** — one rule, two files, and both are already exact-clock, so it is flags only:
   start from row 46's three rules (C/X zero past the width, N/Z from the result at count zero, ROX's
   count-zero case) and ask what `ASR` does with **V** and with the sign-propagating C.
3. **The three status moves** — one small state block (`stiw1`…`stiw3`, `frsr`, `MOVEtoSR`'s privilege
   check); here the **clock** is the work, and the 661 state failures are all `ram` in
   extension-addressed shapes (`(d8,An,Xn)` 209/197/101, then `(xxx).w`, `(d16,An)`, `(xxx).l`), so a
   frame class rides along.
4. **The shifts' memory forms** (`ASR.w`'s 64 `ram` and the shift-kind files' frames), then the seven
   shift-kind files and `Bcc` — 127 of 127 if all four land.

## 4. Verification, unchanged

`--failures` before and after (the failure **set** *and* the first differing field per record); `--corpus
mame` whole; `--corpus clk --dir tests/680x0/full` beside it, **diffed per file against a worktree
baseline**; `--reads` on a ported family; `boot --frames 800` from the repo root for the folds;
`validate.sh` for the gate; environment line in the same command:
`export DOTNET_ROOT=/opt/data/.dotnet PATH=/opt/data/.dotnet:$PATH DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.
Pushing needs the token: `set -a && . /opt/data/.env && set +a && export GH_TOKEN="$GITHUB_TOKEN"`.
