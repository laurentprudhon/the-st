# The instruction code, the two control stores and the decode PLA bank

Depth for any decode work (the `Decode` surface, #89's PLA terms, a family predicate, a legality
question). The numbers come from `research/m68000-silicon-implementation.md` §7–§10; read that before
trusting a secondary account.

## 1. The instruction code is one 16-bit word read as fields

| bits | field | notes |
|---|---|---|
| 15–12 | the **line** | 16 blocks; the only field whose meaning never moves |
| 11–8 | **operation** (immediates, `Bcc`/`Scc`/`DBcc` conditions, `TRAP` vector nibble) | position varies by line — see §2 |
| 11–9 | **operation** (single-operand RMW, quick immediates, shift counts, register numbers) | |
| 8–6 | **opmode**: destination class *and* operation, two-operand ALU lines | 1 = `<ea>,Dn`, 3 = `<ea>,An`, 5 = `Dn,<ea>` |
| 7–6 | **size** (00 b, 01 w, 10 l, 11 = the line's special) — but a *bit operation's* operation, and part of an opmode | |
| 5–3 / 2–0 | **effective address**: mode, register | |
| 11–6 | the **destination** (its own mode/register) on two-operand families | the source is 5–0 |

Everything else (immediate, `d16`, absolute address, register list, bit number) is an **extension word in
the instruction stream**, not part of the code — which is why the prefetch queue, not the bus, supplies it.

## 2. Where the operation field sits, per line

- **0**: bits 11–8 for the six immediates (`0` ORI, `1` ANDI, `2` SUBI, `3` ADDI, `5` EORI, `6` CMPI); bits
  7–6 for the bit operations' kind (`00` BTST, `01` BCHG, `10` BCLR, `11` BSET), with bit 8 set for the
  register-number form and bits 11–8 = `8` for the immediate-number form; bits 7–6 again for `MOVEP`, whose
  distinguishing fact is **EA mode 001**.
- **1/2/3**: no operation field — the line *is* the size (1 byte, 3 word, 2 long) and bits 11–6 are the
  destination. `MOVEA` is the same word with destination mode 001.
- **4**: bits 11–9 for the single-operand RMW (`0` NEGX, `1` CLR, `2` NEG, `3` NOT, `5` TST); bits 8–6 for
  `CHK` (110) and `LEA` (111); bits 11–8 for the status moves (`0` from SR, `2` to CCR, `3` to SR) and the
  0x48xx group, where size 00 = NBCD, 01 = SWAP (mode 0) or PEA, 10/11 = EXT (mode 0) or MOVEM (any other
  mode); the whole word for the `4E4x`–`4E7x` block (`4E50` LINK, `4E58` UNLK, `4E40–4E4F` TRAP +
  vector, `4E60`/`4E68` MOVE USP, `4E70`–`4E77`, `4E80–4EBF` JSR, `4EC0–4EFF` JMP).
- **5**: bit 8 (`0` ADDQ, `1` SUBQ) with the immediate in bits 11–9 (000 = 8); size 11 is the condition
  block — `Scc` (bits 11–8 = condition, any EA) or `DBcc` (bits 5–0 = 111001).
- **6**: bits 11–8 = condition, where 0 is BRA and 1 is BSR.
- **7**: `MOVEQ` — bits 11–9 = `Dn`, bits 7–0 = data, size long.
- **8/9/B/C/D**: bits 8–6 = opmode (destination class + operation). The odd members hide inside: DIVU 011,
  DIVS 111, SBCD `$8100`, MULTU/MULTS 011/111, ABCD `$C100`, EXG `$C140`/`$C148`/`$C188`, ADDX/SUBX
  `$D100`/`$9100`, CMPM (mode 001), and the to-CCR/to-SR forms (`$xx3C`/`$xx7C`).
- **E**: register form when size ≠ 11 (`bits 4–3` = kind: `00` AS, `01` LS, `10` ROX, `11` RO; bit 8 =
  direction; count in bits 11–9, or a `Dn` when bit 5 = 1); memory form when size = 11 (kind in bits 10–9,
  direction bit 8, EA in 5–0).
- **A / F**: the whole line — exceptions 10 and 11, never "unknown".

## 3. The manual's words versus the code's

- The manual **documents the fields** (§4 draws `family`/`dst reg`/`dst mode`/`src mode`/`src reg` and the
  size and direction bits) and the **top-nibble partition** (§10: "the top nibble of the opcode word selects
  the family, so a decoder starts there"), but it names only two of the sixteen blocks: **"line A" and
  "line F"**. It never says "opmode" and never numbers lines 0–E.
- So **"the manual's family" is two different things**: the 16 top-nibble blocks (manual §10) versus ADR
  0015's `Family` (the 78 instruction families). Settlement used here: **`Family` = the 78**; the sixteen
  blocks are the **`Line`**, the A1 PLA's coarsest term, an *address partition*, never a decoded family.
- A **line is not stored anywhere** — not in the instruction, not in either ROM. It is the four bits the
  decode examines once to pick the block; the µROM only ever sees the resulting 10-bit address.

## 4. The three layers, and what each encodes

| layer | size | it encodes |
|---|---|---|
| **µROM** (sequencing) | 544 × 17, 34 rows × 16 cols, 25 addresses unused | *what runs next and what bus access it declares*. **Format I** (bit 1 = 0): `FC` 16–15, `NMA` 14–5 (10-bit next address), `C`/`I` 4/0 (IRC→IR, TVN update), `TY` 3–2 (NMA / A1 / A2 / A3). **Format II** (bit 1 = 1): `CBC` 6–2 (5-bit condition), `NMBA` 14–7 (8-bit base) whose target bits 7:6 come from the condition logic → a **4-way branch**. Every word also carries a four-character access label (class `i/f/n/t` × mode `p/r/w` × type) that declares the bus cycle instead of timing it. |
| **nanoROM** (datapath) | 336 × 68 (66 driven), 84 rows × 4 cols | *the atoms of one microstate*: register/ALU transfers, the AU input constant (`db`, `db±1`, `db±2`, `db−4`, `db+alb`), destinations `d`/`a`/`pc`, the 4-bit mid-state field, the ALU mode (3 bits + variant). Addressed by the same 10 bits, many-to-one. No order, no time, no bus. |
| **PLA bank keyed on `IRD`** | ~130 of ~200 control lines | *which register file `RX`/`RY` mean, the operand size, the read/write direction and byte lane, the exception/interrupt vector, the ALU's row* — everything the two ROMs cannot say. |

Also hard-wired decode, not store content: the three entry PLAs **A1/A2/A3** (149 `A1` terms: 129 keyed on
opcode bits, 20 on the EA field alone; `A2` after an immediate, `A3` after an EA calculation; `A2`/`A3` share
one AND matrix that also produces `IOC` and `PRIV`), the overrides **`A0`** (exception pending at a boundary)
and **`A0S`** (reset, bus error, address error, immediately), the constants ROM (0, ±1, ±2, +4, +8, size)
that lets the AU write `PC` without a bus, and `DCR`/`PREN`/`FTU`.

**The rule that follows**: ROM (sequencing) → ROM (datapath) → PLA keyed on `IRD`. An emulation that stops
at the two ROMs is emulating the datapath and guessing the rest — the reason our own split is
`Decode` (the decision) above `Scripts` (the µROM words) above `ControlStores` (the nanoword atoms).

## 5. Writing `Decode`: gather it from the code, and witness it on the corpus

- **Do not write the 78 predicates from the manual.** A manual-first pass scored **143 885 / 312 500** and
  failed exactly where the encoding lumps: `0x0F00` bit-op masks claiming line C's `ABCD`, `PEA`'s
  `(0xFFC0, 0x4840)` swallowing SWAP/EXT/MOVEM, `CHK`/`LEA` as `0xFFC0` (not `0xF1FF`/`0xF1C0`), and the
  shifts / `DBcc` vs `Scc` / `MOVEP` / line-5 sizes simply absent.
- **Transcribe the conditions the core already has**: `ExecuteAsync`'s `switch (op >> 12)` and each line
  handler's inner switches are 100 % green on both columns, so any disagreement with them is a bug in
  `Decode` by construction. That is also where the ~38 scattered predicates live (12 `IsScripted*` helpers
  plus ~26 inline `(op & mask) == value` tests) — the decode table, already written and already validated.
- **Witness**: `validation-harness cpu-surface --families <dir>` grades every record whose own name carries
  its file's mnemonic (`ADDQ` inside `ADD.b` is excluded by that filter, not by a rule) against
  `Decode.TryDecode(initial.prefetch[0])`. It can see family membership, never legality — legality is the
  `.lst`'s 65 536-opcode golden.
- **Name shape**: at most one family per function; two tolerable (`Move`/`Movea`); past two it must be a
  dispatcher that calls one body per family. A pure rename makes `Line4Async` 76 characters long and loses
  which family the call site meant.
