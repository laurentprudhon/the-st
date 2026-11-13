# What the corpus says about the 68000 core

Measured on 2026-11-13 against upstream commit `e0d5ece` (the corpus and its format are in `README.md`),
with `validation-harness cpu-tests` in a Debug build.

| Run | Tests | Passed | Opcode files fully green |
|---|---|---|---|
| the whole corpus, first measurement of the core as it stood | 1 000 060 | 319 315 (31.9 %) | 0 of 124 |
| the whole corpus, after the 2026-11-13 second session's fixes | 1 000 060 | 887 989 (88.8 %) | 30 of 124 |
| the whole corpus, after the 2026-11-13 third session's fixes (rows 28–35) | 1 000 060 | **957 309 (95.7 %)** | **42 of 124** |
| the vendored sample (24 tests per file, `tests/680x0/v1`) | 2 976 | 2 657 (89.3 %) | 73 of 124 |

The sample tracks the whole corpus to within a point, which is what it is for: a two-second check instead
of a five-minute one. A claim about the core must still be measured against `--full`.

## Defects the corpus found, and what fixed them

Rows are classes of failures the corpus pinned to one cause, in the order they were found. That order is
also the order a later session should re-triage in: each row is one design or decode mistake, and most of
them were invisible in the boot (EmuTOS exercises perhaps a third of the instruction set, and does not
care why a flag is wrong as long as the branch lands).

| # | Symptom | Cause | Fix |
|---|---|---|---|
| 1 | every test `stalled` | the harness's bus never armed a date for its answer — a harness bug, not a core bug | `SingleStepBus` waits for `min(answer instant, wire)` |
| 2 | `ssp` unchanged where the corpus stacked a 7-word frame | no address-error detection at all | `Cycle` faults on a word access at an odd address; `EnterExceptionAsync` writes the group-0 frame |
| 3 | `Bcc`/`DBcc`/`BSR`/`JMP` framed where the corpus did not, or the reverse | a branch to an odd address faults on the *prefetch*, inside the branch | an odd program counter after an instruction raises vector 3 with `AA = pc` |
| 4 | `ADDQ`/`SUBQ` to an address register | the result was sign-extended and truncated to the operand size | a full 32-bit add/sub; flags untouched, size field ignored |
| 5 | `ADD/SUB/AND/OR/EOR Dn,<ea>` wrote the wrong register | the direction test read the opmode field wrong (`0x01C0 == 0x00C0`), so the "Dn to memory" branch was dead code and every memory destination ran as its reverse | test opmode bit 2 |
| 6 | those instructions two bytes too far in `pc`, extension words fetched twice | the memory destination was addressed once to read and again to write | address once, then `ReadAt`/`WriteAt` |
| 7 | `EOR Dn,<ea>` ran as a compare | the EOR test tested bits that are always set for EOR | test bit 8 |
| 8 | every `BTST/BCHG/BCLR/BSET` wrong | bit operations were selected by the *bit-number register* field, so only `D4` counted; `BSET`'s operation field (`11`) was excluded again | select by bit 8 |
| 9 | `(d8, An, Xn)` / `(d8, PC, Xn)` operands pointed elsewhere | the index register was never added to the effective address | add `IndexValue(extension)` |
| 10 | `ANDI/ORI/EORI` to CCR and to SR (96 tests) | the detection demanded a size field of `11`, which those forms do not use, and only `ORI` was recognised | `mode 7, reg 4`, bit 7 selects the SR form, and AND/OR/EOR each done properly |
| 11 | `MOVE from SR` took an illegal-instruction exception | its size field is `11`; the code required `01` | size 3 is the SR form, size 0 the (68010) CCR form |
| 12 | `TAS` on memory was a `TST` | the memory form was missing | read, set Z from the original, write `\| 0x80` |
| 13 | `RTS` never popped | the address was read but A7 left alone | `A7 += 4` |
| 14 | all eight shift/rotate files | the register-count form was decoded as the memory form (bit 5 vs the mode field), and X was written only by the rotate-through-extend kinds | decode by the size field and bit 5; the shifting kinds move the last bit into C *and* X |
| 15 | `ADDX/SUBX` and `ABCD/SBCD/NBCD` to `-(An)` | the result went to a data register instead of memory; the BCD borrow/carry arithmetic was wrong | write back at the predecremented address; recompute the BCD chain |
| 16 | `MOVEM` to `-(An)` stored in the wrong order | the predecrement mask was walked upwards | walk it downwards (A7 first, D0 last) |
| 17 | a faulted instruction still changed flags or registers | most call sites did not check the fault after a bus cycle | a guard after every read *and* every write in the instruction set |
| 18 | `CHK` took an illegal-instruction exception | its "size" field reads as `10` | decode by bits 8-6 = `110` |
| 19 | `NEGX` executed as `MOVE from SR` | the `0x4000` block was keyed on nothing but its top nibble | split on the size field: `11` is `MOVE from SR`, everything else is `NEGX` |
| 20 | `MOVEP` (16 130 tests), `NBCD`, `MOVE USP` | unimplemented, or a mask that swallowed the other direction (`0x4E68 & 0xFFF0` is `0x4E60`) | each implemented; `MOVE USP` splits on its bit 3 |
| 21 | `TRAP`/`TRAPV`/`RTE`/`RTR` frames | the six-byte frame wrote PC low where PC high belongs, and saved the *next* instruction word for a 68000 that had not read it | the frame is SR, PC high, PC low; only the group-0 frame carries the fetch-on-demand adjustment |
| 22 | every byte operation on an odd address read the even byte | the corpus bus ignored the lane bits: A0 is not on the address bus, so a byte access is A1–A23 plus a lane | `address += control & 1` for a non-word access |
| 23 | `CMP`/`CMPA`/`CMPM` changed X | they were computed as a plain subtraction | `SetCompareFlags` saves and restores X |
| 24 | `NEG`/`NOT` to memory wrote the wrong address | the operand was addressed twice, re-reading the displacement word | address once, then `ReadAt`/`WriteAt` |
| 25 | `MOVEM` stores executed as `NBCD`, and the boot's inlined `memset` "cleared" one zero byte per instruction | the `0x48xx` block never dispatched the store form: `EXT.W`/`EXT.L` occupied the only slots, and everything else fell through to `NBCD` | dispatch `(op & 0xFF80) == 0x4880` with `mode >= 2` in that block, before `PEA`/`NBCD` |
| 26 | a word `MOVEM` moved every register by one byte, a long by two; `(An)+`/`-(An)` moved the base twice | the transfer width was the EA helper's size *index* (1 = word, 2 = long) reused as a byte count, and the base register was handed to the helper that advances it per operand | a byte width (2/4) for the address arithmetic, the base register handled here, `(An)+` advanced by the 2-byte access the corpus measures |
| 27 | every static bit operation (`BTST #n,<ea>` and the `BCHG/BCLR/BSET` forms) left the flags as they were, and the following branch took the wrong path | bit 8 was read as "immediate form", so the `0x08xx` block fell into the immediate-arithmetic path, where operation field `4` means nothing | the `0x08xx` block is the static form (bit number in the following extension word), bit 8 selects the register form |
| 28 | `MOVEM` to `-(An)` wrote the *wrong registers*: a save of `d2-d7/a2-a6` restored one register off, so a subroutine that pushed fifteen registers got a clobbered `A6` back | the predecrement form's mask is **reversed** — bit n selects register 15−n — and the core read it straight | walk the bits upwards, store `15 − bit`; 528 of the corpus's clean predecrement tests pin it |
| 29 | every `MOVEP` test failed (16 130), in both directions | the direction bit was inverted (`form <= 1` for memory → register, which is `form >= 2`), and the dispatch demanded a non-zero size field, so the *word* memory → register form never reached `MOVEP` at all and was executed as a bit operation, silently doing nothing | `form >= 2` is the store; `mode 001` with bit 8 set is `MOVEP` whatever its bits 7-6 are |
| 30 | `MOVEP.w` memory → register truncated the data register | the loaded word was assigned to the whole register | keep the high half: `(Dn & 0xFFFF0000) \| value` |
| 31 | `ABCD`/`SBCD`/`NBCD` 25 % — the value wrong on any operand that is not valid BCD, and Z/V wrong | the decimal digit-by-digit chain cannot express a nibble sum ≥ 20 (`0xBD + 0xBD`), and Z was set from a carry | the FX68K chain: binary add/subtract with X, then adjust each nibble from the *binary* carry; `C = X` = the decimal carry, `V` = the adjusted high nibble's overflow, `N` = bit 7, `Z` cleared only by a non-zero result |
| 32 | `CHK` 5 % — its flags | the flags were not written at all | `Z`, `V` and `C` always cleared; `N` **set** by the `Dn < 0` trap, **cleared** by the `Dn > bound` trap, left alone when there is no trap; X untouched, comparison signed |
| 33 | the boot's first VBL took an address error at `0x004EB9` and panicked every frame | `ADDQ`/`SUBQ` to memory resolved the effective address *twice* (once to read, once to write), so the following instruction's opcode word was fetched as a second displacement — the victim was EmuTOS's `subq.w #1,_vblsem.w`, whose second "displacement" was the `4eb9` of the next `jsr` | resolve the address once, `ReadAt`/`WriteAt` against it |
| 34 | `Bcc` 38 tests, all the `xxFF` forms | a displacement of `0xFF` was read as a 32-bit long displacement (a 68020 form that the 68000 does not have) | `0xFF` is −1 |
| 35 | `CMPA.w/l` 5 512 / 5 615 — the flags changed on an instruction that faulted | the branch had no fault guard after its source read, so a faulted operand was still compared | `if (_busFaulted) return;` after the read |
| 36 | `MOVE from CCR` (`42C0`–`42FF`) executed instead of raising an illegal instruction — found by the *boot*, not the corpus: EmuTOS's `_detect_cpu` probes it first, so the ROM believed it ran on a 68060 | the whole `0x42xx` block with operand size `11` was implemented as the 68010 instruction, and the test masked the low six bits so only `move.w ccr,d0` was caught | `(op & 0xFF00) == 0x4200 && size == 3` → `EnterExceptionAsync(4)` |
| 37 | `EOR` with a data-register destination (`EOR Dn,Dm`, `mode 000`) read and wrote address 0 and set its flags from that byte — the boot hung inside EmuTOS's `detect_monitor_change`, whose `eor.b d0,d1` over two GPIP reads never stopped branching | the `EOR Dn,<ea>` branch went straight to the effective-address helper, which answers 0 for a register operand | handle `mode == 0` on the register before any address is resolved |

## What is still open

Measured 2026-11-13 (third session) against the whole corpus, after rows 28–34:

| Family | State | What it needs |
|---|---|---|
| `MOVEP.w/l`, `ABCD`, `SBCD`, `NBCD` | green | — |
| `MOVEM.w/l` | 7 556 / 8 065 | the residue is the exception frame's access-address/status words (the conventions below), not the transfer |
| `CHK` | 7 990 / 8 065 | the same frame words on its two trap paths |
| everything else | one-bit flag differences on instructions whose access faulted, and the frame conventions below |

The remaining failures across the corpus are dominated by one class: **the group-0/2 frame's own words**
(the saved `pc` from the two-word prefetch, and the status word's access class — see the three
conventions below). That is design work — the real prefetch queue, ADR 0009 §4 — not a bug list.


## Three things the corpus fixes that the manuals leave to the implementation

1. **The group-0 frame's saved program counter is one word behind this core's.** The corpus's model has
   the 68000's two-word prefetch queue, so at a fault it saves a program counter that already ran two
   words ahead. This core fetches on demand (ADR 0009 §4's recorded gap), so its saved value is `pc − 2`
   of its own counter. Measured: `pc − 2` matches the frame tests, `pc` does not, `pc − 4` matches the
   *prefetch* fault's frame.
2. **The group-0 frame's first word carries the implementation's access class.** The corpus sets the read
   bit for every instruction except a plain `MOVE` or a `MOVEM` store writing to memory: its model
   classifies the access by the instruction's operand handling, not by the direction of the faulting
   cycle (so `CLR`, a pure write, records "read"). The core reproduces the corpus, because here the
   corpus is the oracle.
3. **Extension words come out of the prefetch queue, not off the bus.** A test's base memory carries the
   two prefetch words and then only the bytes the reference model actually read, so an instruction's
   extension word is the prefetched one. This is the same ADR 0009 gap as (1), and it is why the real
   prefetch queue is the largest single item between this core and a green corpus: it is design work,
   not debugging.

A fourth, smaller one: the corpus's `length` is in CPU clock cycles and this core models only the bus's.
18 of the 2 976 sample tests happen to agree, which is exactly the size of the internal-cycle table that
is missing.

## How to reproduce

```
scripts/fetch-680x0-tests.sh --full
tools/validation-harness/bin/Debug/net11.0/validation-harness cpu-tests --dir tests/680x0/full \
    --quiet --out report.txt --failures failures.tsv
```

`--out` writes the per-file table; `--failures` writes up to 20 detail lines per opcode file, tab
separated: opcode file, test name, first difference. Sort by the first column to see which file a fix
should target, by the third to see which field.
