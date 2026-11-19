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
| 38 | after log retention landed, **36 of 124 opcode files went from green to failing** — most with `stalled at pc=000c00: nothing is queued and the instruction is not finished`, a few with `ASSERTION 1 failed — CPU's write at 0 is dated before the batch being drained (4)` | `SingleStepBus` (the corpus's own bus) kept an **index into the `Bus` wire's log** across a suspension: it recorded `_assertionIndex`, answered four ticks later, and read `log[at..at+3]`. Retention discards from the front, so the index landed on other records or past the end, the answer never landed, and the CPU waited on a bus that had forgotten its exchange | the four records are now read **on the resume that sees them** and kept as values (`_addressHigh/_addressLow/_control/_writeData`, filled in order). The rule the retention session wrote down — *an index into a log is only valid inside one resume* — is what this instrument violated. Corpus after the fix: 960 752/1 000 060 (96.1 %), 43 files green |
| 37 | `EOR` with a data-register destination (`EOR Dn,Dm`, `mode 000`) read and wrote address 0 and set its flags from that byte — the boot hung inside EmuTOS's `detect_monitor_change`, whose `eor.b d0,d1` over two GPIP reads never stopped branching | the `EOR Dn,<ea>` branch went straight to the effective-address helper, which answers 0 for a register operand | handle `mode == 0` on the register before any address is resolved |
| 39 | `ADDX`/`SUBX` 9 078 (and `ABCD`/`SBCD` before them) — on `-(Ay),-(Ax)` the base registers came out **2 short** | the destination register was pre-decremented *before* the source's read was known to have landed, so a faulted source left its decrement behind | `if (_busFaulted) return;` between the two pre-decrements |
| 40 | `ADDX.l`/`SUBX.l` on an odd register: the register came out 4 short | a long pre-decrement stepped straight to A − 4 and read there; the corpus's model steps a word at a time and reads the **low** word first, at A − 2, so an odd register faults with the register down 2 | `PredecrementAsync` handles a long as two word accesses, `A − 2` then `A − 4` |
| 41 | `ADDX.b`/`SUBX.b` 39 + 34 — the carry and the flags on a byte | three rules the manual leaves to the implementation: **Z is only ever cleared** (never set, so a chain reports the whole result), **V ignores the extend bit** (the operands' signs decide it), and the carry needs a **wider width** than the operand (`0xFF + 0x4A + X` carries out of a byte, and masking the source first loses it) | `SetExchangeFlags`: its own arithmetic, Z restored when the result is zero |
| 42 | any `(An)+` store: the register moved even when the access faulted | the post-increment was applied when the address was computed; the model applies it **when the access lands** — for a plain `MOVE` only (every other instruction increments before the access, and a fault keeps the increment) | `PostIncremented`/`SettlePostIncrement`: the register is put back on a faulted plain-`MOVE` store |
| 43 | `JSR` 4 036 — `ssp` was 18 bytes down instead of 14 on a jump to an odd address | the return address was pushed, *then* the prefetch's address error was raised | an odd target hands the PC to the prefetch's own fault check and pushes nothing |
| 44 | `MOVE.l <ea>,-(An)` on an odd register: the register came out 2 short, and the frame recorded A − 4 | a plain `MOVE`'s long store steps down a word at a time and writes the **low** word first (at A − 2), unlike a long *read*, which steps down 4 | `WriteEaAsync` handles the odd plain-`MOVE` long store at A − 2 |
| 45 | `RTE` 3 986 and `RTR` 3 547 — one byte of the frame's saved status register | the frame carried the status as it stood, bits 7–5 included; the 68000 stores only the defined ones, and the corpus compares raw frame bytes | the frame writes `oldSr & 0xA71F` |
| 46 | every shift and rotate 3 897 — and the `length`-table's tail | three rules: a count past the operand's width shifts the last bit out and nothing else, so **C and X come out zero** (the loop kept the sign bit running); **N and Z are read off the result even at a zero count**; a through-extend rotate at count zero leaves C and X as they were — except that C takes X's value | `Shift` computes the last bit out from the width, writes C/X from it, and reads N/Z/V from the result |
| 47 | `CMPI` (`CMP.b` 1 294, and every immediate compare) cleared or set `X` | the immediate path used the subtract's flag setter | `SetCompareFlags`, which leaves X alone |
| 48 | `CMPM.b` stepped both registers by 2 | a byte steps by one, except `A7`, which always moves a word — and the *destination* register has the same exception | a step per register, `size == 0 && reg == 7 ? 2 : SizeBytes(size)` |
| 49 | `DIVU`/`DIVS` 3 315 — the overflow case left `C` set and `N`/`Z` moved | an overflow touches `V` and clears `C`; `N`, `Z` and `X` stay as they were and the register is untouched | `_sr = (_sr \| FlagV) & ~FlagC` |
| 50 | `BTST Dn,#data` 138 — the bit came out of low memory and `pc` was 2 short | the immediate operand is the word that follows the opcode; the effective-address helper answered "no address" (0) and nothing was fetched | the immediate form fetches its word and tests its **low byte**, bits 0–7 |
| 51 | `NEGX` 31 — `Z` followed the result | `0 − operand − X` is a subtract from zero, so it carries `ADDX`'s zero rule: **Z only ever cleared** | `SetExchangeFlags(true, value, 0, result, size)` |
| 52 | `MOVE` onto an odd address: the flags stayed as they were | the model's `move()` updates the status from the value it is moving, and the bus cycle raises the address error afterwards — so a faulted store still leaves `N`, `Z`, `V`, `C` read off the value | the flags are set before the store, not after it |
| 53 | `JMP`/`JSR (xxx).l` — `pc` came back 24-bit | the absolute-long effective address was masked to 24 bits; the bus truncates at the wire, but the counter a jump stores is the whole value | the long form of `(xxx).l` keeps its 32 bits |
| 54 | **the whole corpus instrument**: every test failing, `OutOfMemoryException`, `ASSERTION 1 failed — CPU's write at 20 is dated before the batch being drained (9223372036854775807)` | `#40` made `Engine.Run` take the minimum over the queue *and* the chips' held dates; with nothing pending that minimum is `long.MaxValue`, and the corpus harness calls `Run(long.MaxValue)` for one instruction — so the loop drained an empty batch at the end of time for ever | `Run` stops when `NextInstant()` answers `long.MaxValue` |
| 55 | `cpu-tests --dir tests/680x0/full` died on `68000.official.json` | the decode map is an object, not an array of tests, and the file scan took every `*.json` | the scan keeps only files whose JSON opens with `[` |
| 56 | **`MOVE.w` 355, `MOVE.l` 377, `MOVEM.l` 404** — every one differs first in a byte of the group-0 frame at `$7F7`/`$7FF` | the core fetched each instruction word on demand and imitated the queue's saved counter with `pc − 4`/`pc − 2`. The corpus's reference has ADR 0009 §4's queue, so at a fault the frame saves the word in `IR` — and how far that has run ahead depends on how many fetches the instruction had *already issued*: a store that faults on a `-(An)` address keeps its trailing fetch (class 0), an extension-built address or any other fault cancels the read that was owed | the queue itself: `IRD`/`IR`/`IRC`, one read per consumed word, a refill at a jump's/branch's/exception's target, and `returnPc = _irAddress` with no arithmetic. 1 000 008 / 1 000 060; the 49 records left are a `MOVE` into an odd `(xxx).l` destination, where the reference takes the address's two words with one fetch fewer (ADR 0009 §4's falsifier: the read's placement inside the instruction) |
| 57 | the 49 records of row 56, and a **measured no** for the obvious rule: "a long destination address defers its last word's refill until the address is validated" | the rule was built and measured: it fixes 23 records and breaks 278 (**999 753 / 1 000 060**). The reference's placement is **per opcode**, not per shape — `33ea [MOVE.w (d16,A2),(xxx).l]` (odd destination) pays 2 reads where `33fc [MOVE.w #,(xxx).l]` (odd destination too) pays 3, and `MOVE.l` has records with the same source mode and the same destination parity landing in both buckets | reverted; the datum is Level 2's per-opcode script (ADR 0009 §4), and its first tool is `--reads`, which writes both lists per record. Resolved by row 58 |
| 58 | **`MOVE.w` 23, `MOVE.l` 26** — the whole residue of row 56: a `MOVE` into an odd `(xxx).l` destination, first difference in the group-0 frame's saved `pc` (`$7FE`/`$7FF`), two bytes high | a plain `MOVE`'s absolute-long destination has **its own micro-op script** in the reference (`MOVE_bw_AbsoluteLong`, `MOVE_l_AbsoluteLong`): it builds the pair from **one** read — the pop that takes the high word fetches the low one and the low one is read out of the queue rather than popped — where the generic EA machine (the one every other instruction uses) spends two. The script's second half is a **prefetch-first class**: a source that cost no bus cycle of its own (a data register, an address register or an immediate) fetches the word *after* the address before it stores, so an odd destination's fault keeps that read; every other source stores first and the fault cancels it. That is the whole difference between `33ea` (2 reads) and `33fc` (3), and it is why row 57's single clause could never hold | `FetchMoveAbsoluteLongAsync` takes the pair whole and leaves the queue a word short; `CloseAfterLongDestinationAsync` pays the two words the reference's closing prefetches read; `MoveSourceNeedsNoBusCycle` is the class read off the opcode's own source field. `--reads` on both files: **0 records** mismatched, and the read lists of the **other 16 081 records did not move** — the evidence that this is a placement, not a blanket rule. **1 000 057 / 1 000 060**, 122 of 124 files green, sample 2 976 / 2 976; the 3 left are the self-contradicting upstream records. Gate PASS, G8 9.75 ms, 300-frame records unchanged |
| 59 | the `length` column — `cpu-tests` prints "0 of 2 976 exact-cycle tests" (18 before `#70`'s queue), and the internal-clocks table ADR 0009 §3 calls missing | not a mystery and not a per-instruction datum: `length = 4 x (bus cycles) + the reference's own n/t clocks` on **all 1 000 060 records**, and the residue `length - 4 x bus` is a function of the instruction's **shape** (family, size, operand EA modes) for 1 231 of 1 333 plain-class shapes (73.4 % of 805 512 records; 85.0 % if the key is the whole opcode word). Every record outside that is in nineteen families — the shifts (177 314), `MUL`/`DIV` (19 888), `Bcc`/`DBcc`/`Scc` (13 246) and the data-register bit ops (3 873) — and each follows a value the core has in hand when it executes. Two splits were needed first: 194 548 records (19.5 %) are **exception entries** (a fault's `length` covers the group-0 frame and the handler's first fetches), and the shift family is `base + 2 x count` exactly (`base` 2 for `.b`/`.w`, 4 for `.l`), 177 314 records with no exception | `scripts/corpus-length-census.py` measures it, `LENGTH.md` is the table and the ruling. One datum stays open: a `BCHG`/`BCLR`/`BSET` on a data register pays 6 or 8 clocks with nothing in the record to separate the two — read CLK's source, do not guess (row 57) |
| 60 | **30 522 records differ first in a byte of the group-0 frame's saved `pc`** on MAME's corpus, and no expression the core could compute fit them (`_fetchPointer − 2` 23 058, `_pc` 20 173, `_irAddress` 17 440, `_pc + 2` 11 947, `_fetchPointer` 11 726 of 74 201 framed records) | MAME's microcode issues its **prefetch inside the instruction's idle clocks**; this core filled its queue lazily and fetched at the closing, so its counters only implied a pipeline depth by accident, and the same opcode shape took two values record to record (`MOVE.w` splits 50/50). The corpus obeys `saved = opcode + 2 + 2 × (prefetch words issued)` — `CLR.w (An)` (no idle before its access) `opcode + 2`, `CLR.w -(An)` (the EA's `n 2`) `opcode + 4`, `MOVEM` `opcode + 6/8/10` | `PrefetchAsync`: the one word behind the queue's head read **inside the idle clock**, idempotent so the instruction's read count is unchanged and only its *placement* moves — at the EA charge and the family charge (`ClocksAsync(n, prefetch: true)`), at `MOVEM`'s transfer loop in both directions and at `ADDX`/`SUBX`'s access; never at a leading charge (a branch would fetch the fall-through it abandons) or an exception entry. The frame saves `_fetchPointer − 2`. **Class 30 522 → 24 480** (with `MOVEM`'s 2 378, `ADDX`/`SUBX`'s 3 300 and the group-1/2 vector rule's 11 114, **state 80.9 % → 88.0 %**) (256 897 → 279 297, files green 30 → 62), bus +12 736 cycles (the reads a fault used to cancel), internal clocks and the clock classes unchanged. CLK's state falls 98.36 % → 82.2 % **by construction** (its records encode its own placement). Boot: desktop 611, G8 8.66 ms at n 760, validate PASS; **both folds moved** — `run.digest` `339c3c7e…`, `run.stream` `042fb662…`, the owner's re-bless |
| 61 | the group-1/2 frame's saved `pc`: **16 276 records**, and a rule that fixed 11 114 of them **hung the boot** | MAME saves the **instruction's own address** for the illegal lines, `ANDI`/`ORI`/`EORI`-to-`SR`, `MOVE`-to/from-`USP`, `MOVE`-to-`SR` and `RESET`, but not for `TRAP`/`TRAPV`/`CHK`/`RTE`. Applied as one rule the state verdict rose 256 983 → 268 097 and the 800-frame boot never reached the desktop (`frames_to_desktop=never`, 5 223 pops a frame against 35 811): EmuTOS runs on `TRAP`, so an `RTE` through the changed frame returns to the `TRAP` itself | reverted. **A frame rule is taken per family and the boot run before it lands** — the corpus alone calls it a pure win, which is why the corpus is not the whole verdict (`PLACEMENT.md` §the trap) |
| 62 | the group-0 frame's first word differs by **+8** on 3 321 records (and `sr`'s low byte by +8 on 5 211 more) | `BusStatusWord` set `0x08` for a fetch fault. MAME's `SSW_N` is its **nested-fault marker**, composed only on the frame's own writes (`SSW_DATA\|SSW_N\|SSW_CRITICAL`); `bser1` builds the saved word from `m_base_ssw`, which is `SSW_PROGRAM\|SSW_R` for a fetch | one bit: the fetch branch sets `0x10\|0x02` only. RTS/JSR/RTR/RTE go **2 500/2 500**, `Bcc` 2 499 — state 88.0 % → **89.6 %** |
| 63 | the group-0 frame's saved `pc` is **one word deep** on 19 791 records (every `.l` ALU/RMW family, `JMP`, `BSR`) | `m_pc` is set by a **fetch state** only (`roml1`), so an extension-word read inside the instruction must not move it — and this core's `_fetchPointer − 2` counted every read | `_fetchStateAddress`: written by the trailing fetch, `PrefetchAsync`, `RefillAsync` and the instruction boundary, and **not** by `FetchAsync` (which also serves extension words). +2 class **19 791 → 11 570**, with 2 752 records crossing to −2 and 1 200 to −4 — a per-script split **inside one opcode shape**, i.e. the measured argument for L2, not a clause to iterate on. State 89.6 % → **91.3 %** |
| 64 | `DBcc`'s counter, `LINK A7`'s saved long, `UNLNK`'s `ssp`/`usp`, `STOP`'s `pc` all differ by 1, 4, a stray value and 4 | every one of them is a register write placed **after the instruction's last fetch** in the microcode (`dbcc3` adds into the ALU and only `dbcc4`/`dbcc5` write it back; `link3` writes `high16(m_at)` latched before `A7` moves; `unlk3` sets `m_da[m_sp] = m_au` only once both words are in; `aaa01` rolls `m_au` back twice for `STOP`) | write them after the fetch and keep the register untouched when it faults; `STOP` reports a **stopped CPU's** counter (the opcode's own address) through the single-step seam only — rolling `_pc` back in the handler makes EmuTOS re-execute `STOP` for ever (`frames_to_desktop=never`, 14 464 instructions). 2 017 records, **four files green** — state 91.3 % → **91.9 %** |
| 65 | `ANDItoCCR`/`ORItoCCR`/`EORItoCCR` short **−12 clocks** on every record (7 500) | the charge table's predicate was `(op & 0x00C0) == 0x00C0`, which matches only the `0x7C` **to-`SR`** forms — the `0x3C` to-`CCR` forms were charged **nothing**. Measured residue: 12 (`o#w1`'s bus state 4 + `stiw1` 2 + `stiw2` 2 + `stiw3` 2 before the closing prefetch) | charge 12 for `(op & 0x00FF) == 0x3C`; all 7 500 exact — exact-clock 64.6 % → **67.4 %** |
| 66 | `OR.l`'s file **1 799 / 2 500** — the whole `.l` ALU/RMW commit-order and saved-`pc` class, 701 red records | a handler is a *shape*: `or.l Dn,(An)` read the operand, wrote it, then fetched at the closing, and an odd destination's fault kept a counter one word deep or a register one step ahead of the hardware | **the script, not a clause** (#78, the owner's ruling): the microcode expanded as C# — `M68000.Micro.cs` (the elementary operations) + `M68000.Scripts.cs` (18 shapes for `or.l`), the prefetch **between** the operand's read and its write, the long written **low word first**, the post-increment committing in `pinl3` (after both words), `m_pc = m_au` only in the states that begin a prefetch (absolute and PC-relative shapes), and the PC-relative operand read taking the instruction stream's bus status. `OR.l` line 8 goes **1 704/2 340 → 2 340/2 340**, the file **1 799 → 2 435 / 2 500** (the 65 left are `ORI`'s, untouched and red on `main` too), state **291 934 → 292 570**, exact-clock **213 891 → 214 492**, CLK **852 059 → 852 592**; no record that passed on `main` fails. Boot: desktop 612, G8 **8.58 ms** (A/B 3 pairs against `main`: 8.59 vs 8.58, pops 34 291 both), validate PASS — **both folds moved** and are the blessed pair from the owner's re-bless of 2026-11-19: `run.digest` `afaf347cd76aa05172e1bb5ce9c1cec851b515433ae6a0162d0bddf7494be38f`, `run.stream` `81f34a1181a4faf62f5d17ac89ce255ca9051f60bcc60c1b986138ebc8a95fa1` (Debug `boot --frames 800`). The ledger, the vocabulary and the recipe are `design/cpu-scripts.md` |
| 67 | the two-operand longs' own records: `AND.l` 576 red of 2 371, `EOR.l` 544 of 2 238, `ADD.l` 378 of 1 538, `SUB.l` 377 of 1 527, `CMP.l` 432 of 2 063, and the three address-register files 518 / 525 / 545 of 2 500 | the class row 66 named, on four more families: a handler is a *shape*, so the commit order, the prefetch's placement and the saved-`pc` depth were wrong in exactly the same way (`OR.l` was not special) | the `OR.l` script **generalised, not copied** — `AluLongOp` (one 32-bit operation: the low word's carry out *is* the high word's carry in, so `alu_add`+`alu_addc` is one statement) and `SetLongFlags` (`sr_nzvc`+`sr_nzvc_u`; Z is only ever cleared, which on a 32-bit result is `result == 0`; `extend: false` for a comparison) over three destination tails (data register, memory, address register — the last without a flag call, as MAME's `i=.......` says) and one dispatch keyed on the line's opmode. `AND.l` 1 795 → **2 371 / 2 371**, `EOR.l` 1 694 → **2 238 / 2 238**, `ADD.l` 1 160 → **1 538 / 1 538**, `SUB.l` 1 150 → **1 527 / 1 527**, `CMP.l` 1 631 → **2 063 / 2 063**, `ADDA.l`/`SUBA.l`/`CMPA.l` **2 500 / 2 500** each; state 292 570 → **296 465 (93.4 %)**, 69 → **72 files green**, exact-clock 214 492 → **215 930 (68.0 %)**, CLK 852 592 → **855 744** (the eight files rise, no file falls). No MAME record that passed on `main` fails; on CLK three records of a PC-relative operand's *faulting* shape cross from pass to fail, and their MAME counterparts pass — the frame's status word is the contested column, and the script follows MAME (`SSW_PROGRAM` on the operand's first word). The red left in those files is the immediates' and the quicks' (line 0 and line 5, untouched). Boot: desktop **612** unchanged, pops 34 291 → 34 279 a frame, instructions 4 100 721 → 4 099 631, exceptions 7 654 → 7 644, validate **PASS**, G8 **8.98 ms** at n 761 — and **both folds moved** (`run.digest` `554205a3e5c3b98628e893e6395e1126d20215e711bc2a5f563c3abdc0e9da20`, `run.stream` `e9f72d4b4c86b444be5d61307ac98c4b7a32ecb2fd672ed19afef4ff9b9c7ed2`) — **blessed by the owner on 2026-11-19, and that pair is `main`'s now** |

### The gate as it stands (2026-11-19, after rows 62–67), and the blessed pair

MAME's corpus — **296 465 / 317 500 state (93.4 %), 72 of 127 files green, exact-clock 215 930
(68.0 %)**; CLK beside it, never subtracted — **855 744 / 1 000 060**. `scripts/validate.sh` **PASS** on
the tree: desktop at frame **612**, G1/G2 identical (7 509 206 digest words), **G8 8.98 ms** at n 761,
34 279 pops a frame. **Both folds moved with row 67 and the owner re-blessed them on 2026-11-19**:
`run.digest` `554205a3…e9da20`, `run.stream` `e9f72d4b…9c7ed2` (Debug `boot --frames 800`). They move by
construction — a frame's status word and saved pc are state an `RTE` restores, and the CPU's own clock
changed; the pair before it was row 66's `afaf347c…` / `81f34a11…`.

Still open on this gate, in the order the measurement puts them: the group-0 saved `pc`'s remaining
**12 914** records (a +2/−2/−4 split inside one opcode shape — per-script placement, so it waits on L2,
`research/mame-microcode-core.md` §3; the scripted families are out of it, so what is left is `BSR`,
`UNLINK`, `JMP`, `MOVE`, `NOT`/`NEGX`/`TST`/`CLR`/`NEG`); the faulting records' **aborted cycle** (4 clocks
of `length`, no bus cycle — one path in `Cycle`, and it is what keeps every scripted family's
address-error records red); the **immediate and quick families** (`ORI`/`ANDI`/`EORI`/`ADDI`/`SUBI` on line
0, `ADDQ`/`SUBQ` on line 5), which share their files with the scripted longs and are a state block of
their own; the `sr` column's 3 026 (shifts, `DIV`); the clock column's `MOVEtoCCR` (−8 on 2 341, −4 on 159,
no discriminator yet), `ADDA.w`/`SUBA.w`, `MOVEfromSR`, `CHK`, `MULS`, `DIVS`, `CLR.w`. `TAS` and `TRAPV`
stay outside any 100 % claim (MAME disowns its own timing for both).

## What is still open

Measured 2026-11-17 (the session that wrote rows 39–55) against the whole corpus:

| Run | Tests | Passed | Opcode files fully green |
|---|---|---|---|
| the whole corpus as found — after `#40` had broken the instrument, before rows 39–55 | 1 000 060 | 974 788 (97.5 %) | 62 of 124 |
| the whole corpus after rows 39–53 | 1 000 060 | 998 921 (99.89 %) | 119 of 124 |
| the whole corpus after `#70` (the prefetch queue, row 56) | 1 000 060 | 1 000 008 (99.995 %) | 120 of 124 |
| the whole corpus after `#74` (the read's placement, row 58) | 1 000 060 | **1 000 057 (99.9997 %)** | **122 of 124** |
| the vendored sample (`tests/680x0/v1`, 24 tests a file) | 2 976 | **2 976 (100 %)** | 124 of 124 |
| **MAME's microcoded corpus, the gate since the owner's ruling of 2026-11-19** (`--corpus mame`) | 317 500 | **279 297 (88.0 %)** | 62 of 127 |

The sample runs in **0.4 s** again (`--dir tests/680x0/v1`), and the whole corpus in **26 s** — the
"5–6 min, 194 MB" of the earlier pages was row 54's instrument bug, not the corpus's size.

| Family | State | What it needs |
|---|---|---|
| `ADDX`/`SUBX`, `NEGX`, `ABCD`, `SBCD`, `NBCD`, `MOVEP` | green | — |
| `ASR`/`ASL`/`LSR`/`LSL`/`ROX`/`ROL`/`ROR`, `.b`/`.w`/`.l` | green but for the memory-shift frame cases below | — |
| `DIVU`/`DIVS`, `MULU`/`MULS`, `CMP`/`CMPA`/`CMPM`, `CHK`, `BTST`/`BCHG`/`BCLR`/`BSET` | green | — |
| `JSR`, `RTE`, `RTR`, `LEA`, `PEA`, `JMP`, `CLR`, `TST`, `NOT`, `NEG`, `EOR`, `OR`, `AND`, `ADD`, `SUB`, `MOVE from SR`, `MOVE to CCR/SR` | green | — |
| `MOVE.w` 23, `MOVE.l` 26 | green since row 58 | — (the row also closed the placement question: the reference's own `MOVE_*_AbsoluteLong` script, one read for the pair plus a prefetch-first class for a register/immediate source) |
| `ASL.b` 2, `DIVU` 1 | — | upstream's own records disagree with any `ASL.b`/`DIVU`: `ASL.b #2` on `$CDFB7FBE` is documented as `$2E5E4304`, which no byte operation can produce (the high 24 bits changed). 3 tests of 1 000 060; do not chase them |

The remaining 3 failures are the **upstream records that contradict themselves**; every other record of the
1 000 060 passes, and every one of them with the reference's own read placement (`--reads` reports 0
mismatches over `MOVE.w`, `MOVE.l`, `MOVE.b`, `CLR.w`, `ADD.w` and `AND.w`).

## Three things the corpus fixes that the manuals leave to the implementation

1. **The group-0 frame's saved program counter is the address of the word in the queue.** Since `#70`
   the core runs ADR 0009 §4's queue, so the frame saves `_irAddress` (the word in `IR`, i.e. the last
   program read − 2) with no offset arithmetic — and the measured rule holds on every record: a faulting
   instruction's frame is as many words ahead as the fetches it had *already issued*, so a fault that
   comes before the trailing fetch saves a counter two bytes lower than the same instruction completed
   (row 56).
2. **The group-0 frame's first word carries the implementation's access class.** The corpus sets the read
   bit for every instruction except a plain `MOVE` or a `MOVEM` store writing to memory: its model
   classifies the access by the instruction's operand handling, not by the direction of the faulting
   cycle (so `CLR`, a pure write, records "read"). The core reproduces the corpus, because here the
   corpus is the oracle.
3. **Extension words come out of the prefetch queue, not off the bus.** A test's base memory carries the
   two prefetch words and then only the bytes the reference model actually read, so an instruction's
   first extension word is the prefetched one. `#70` implemented the queue (row 56) and it took 1 087
   records with it: 998 921 → 1 000 008 of 1 000 060. **And where inside the instruction a read lands is a
   per-opcode datum, not a rule**: a plain `MOVE`'s absolute-long destination reads its pair with one
   fetch (row 58), the generic EA machine with two, and a source that took no bus cycle buys a prefetch
   before the store — 1 000 057 of 1 000 060.

A fourth, smaller one: the corpus's `length` is in CPU clock cycles and this core models only the bus's.
**0 of the 2 976** sample tests agree today (18 did before `#70`'s queue). Row 59 and `LENGTH.md` measure that
gap instead of naming it:
the residue is a function of the instruction's shape on 73.4 % of the plain records, the shifts are
`2 (or 4 for .l) + 2 x count` exactly, and the rest are four counters and one open datum — a build order,
not a research question.

## How to reproduce

```
scripts/fetch-680x0-tests.sh --full
tools/validation-harness/bin/Debug/net11.0/validation-harness cpu-tests --dir tests/680x0/full \
    --quiet --out report.txt --failures failures.tsv
scripts/corpus-length-census.py --dir tests/680x0/full --table     # LENGTH.md's numbers, 20 s

# the gate, since the ruling of 2026-11-19 (MAME's microcoded corpus: 127 files, 2 500 each)
scripts/fetch-m68000-tests.sh                                     # vendors tests/680x0/mame/ (git-ignored)
tools/validation-harness/bin/Debug/net11.0/validation-harness cpu-tests --corpus mame \
    --quiet --out report-mame.txt --failures failures-mame.tsv --clocks clocks-mame.tsv
```

`--corpus mame` reads MAME's own format (`pc` is `m_au`, so the opcode is at `pc − 4`; `ram` pieces
are 16-bit and arrive byte-split from upstream's `decode.py`; `re`/`we` are aborted address-error
cycles). `--clocks` dumps one line per record — the corpus's length, our bus cycles, internal clocks
and their sum — and, in a Debug build, the frame's saved `pc` beside the three quantities it could be
(`fetch_ptr`, `pc_counter`, `ir_addr`) plus whether the instruction had fetched a word of its own.
That dump is what rows 60 and 61 were read off.

`--out` writes the per-file table; `--failures` writes up to 20 detail lines per opcode file, tab
separated: opcode file, test name, first difference. Sort by the first column to see which file a fix
should target, by the third to see which field.
