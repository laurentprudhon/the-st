# The 68000 — a compact, exhaustive tutorial

> Everything the CPU does, in the smallest space that still loses nothing: programmer's model,
> addressing modes, every instruction by family, condition codes, exceptions, interrupts, bus
> cycles, timing. Scope: the **MC68000** as fitted to the ST (8 MHz, 64-pin DIP, 16-bit data bus,
> no cache, no MMU, no VBR). Instructions added by later parts are listed once in §12 and nowhere
> else, so nothing in §1–§11 is "not on this chip".
>
> Notation used throughout: opcodes are written MSB→LSB as 16 bit fields — `rrr` register number,
> `mmm` EA mode, `EA` = `mmm:rrr`, `ss` size (`01`=B, `11`=W, `10`=L), `d` direction, `i` immediate/
> register count, `cccc` condition, `q` quick immediate. `†` marks a note that only matters to a
> cycle-exact model; `$` = hex.

---

## 1. Programmer's model

Eight data registers `D0–D7`, eight address registers `A0–A7`, one 16-bit status register, one
32-bit PC. All registers are 32 bits wide; there are no separate byte/half registers.

| Register | Role | Writes |
|---|---|---|
| `D0–D7` | data, bit/shift/BCD/divide operands | byte/word write touches only that part |
| `A0–A6` | addresses, counters, base registers | **always** 32-bit; word writes sign-extend |
| `A7` | stack pointer — `USP` in user mode, `SSP` in supervisor mode | as above; the *other* one is hidden |
| `PC` | address of the next instruction word to be **fetched**, not of the next instruction | — |
| `SR` | see below | privileged paths only |

### Status register

```
15  14  13  12 11 10   9  8  7  6  5   4   3   2   1   0
 T   –   S   I2 I1 I0   –  –  –  –  –   X   N   Z   V   C
```

- `T` trace: when set, a trace exception (vector 9) follows every instruction.
- `S` supervisor: 1 = supervisor mode (privileged instructions allowed, `A7` = `SSP`).
- `I2–I0` interrupt mask: an external interrupt of level *L* is taken only if *L* > mask, except
  level 7, which is non-maskable (and edge-triggered).
- `X` extend: carry out of bit 31/15/7, used by `ADDX/SUBX/NEGX/ABCD/SBCD/ROXx` and by none of the
  plain add/sub instructions (they update `C`, never `X`).
- `N` negative, `Z` zero, `V` overflow (two's-complement signed), `C` carry/borrow.

`CCR` is the low byte (`X N Z V C`); `SR` is the whole word. Bits 14 and 9–5 are unimplemented on
the 68000: they read as 0 and are ignored on write. `MOVE` to/from `SR`, and `ANDI/ORI/EORI` to
`CCR`/`SR`, are the only ways in; `MOVE` to `SR` is privileged.

### Supervisor model

Two stack pointers exist; `A7` is an alias for whichever is current. The 68000 has **no `VBR`**:
every exception vector is read from low memory, `vector × 4`, in the first 1 KB (`$000–$3FF`) — so
the table lives in RAM at address 0 and the ST's TOS installs its handlers there.

Privileged instructions (privilege violation, vector 8, if executed in user mode): `MOVE` to `SR`,
`ANDI/ORI/EORI` to `SR`, `STOP`, `RESET`, `RTE`, and both directions of `MOVE USP`.

---

## 2. Data in memory

Big-endian. A word at even address *N* occupies `N` (high byte) and `N+1` (low byte); a long
occupies `N..N+3` with the high word first. Words and longs **must** start at an even address:
an odd address is an address error (vector 3), never a rotated access. Bytes may live anywhere.

On the bus, the even byte travels on `D15–D8` and the odd byte on `D7–D0`, selected by `UDS`/`LDS`
(§8). The 68000 has no `A0` pin; `A23–A1` plus the two strobes encode every byte access.

---

## 3. Addressing modes

Twelve syntactical forms, encoded in one 6-bit field (`mmm:rrr`). A mode is *alterable* if it can
be a destination, *control* if it supplies an address without reading memory, *data* if it can be
read.

| # | Mode field | Syntax | Meaning | Class |
|---|---|---|---|---|
| 1 | `000 rrr` | `Dn` | data register | data, alterable |
| 2 | `001 rrr` | `An` | address register | data (`A`), alterable as `An` only |
| 3 | `010 rrr` | `(An)` | memory at `An` | data, alterable, control |
| 4 | `011 rrr` | `(An)+` | then `An += 1/2/4` by size | data, alterable |
| 5 | `100 rrr` | `-(An)` | first `An -= 1/2/4`, then use it | data, alterable |
| 6 | `101 rrr` | `d16(An)` | `An + sign_extend(d16)`, one extension word | data, alterable, control |
| 7 | `110 rrr` | `d8(An,Xn)` | `An + sign_extend(d8) + Xn`, two extension words | data, alterable, control |
| 8 | `111 000` | `(xxx).W` | sign-extended 16-bit absolute, one extension word | data, alterable, control |
| 9 | `111 001` | `(xxx).L` | 32-bit absolute, two extension words | data, alterable, control |
| 10 | `111 010` | `d16(PC)` | `PC + sign_extend(d16)` | data, control |
| 11 | `111 011` | `d8(PC,Xn)` | `PC + sign_extend(d8) + Xn` | data, control |
| 12 | `111 100` | `#imm` | immediate, 1 word (B/W) or 2 (L), in the extension words | data |

Notes that decide real behaviour:

- **`An` as a source** is always the full 32 bits; byte operations on `An` are illegal (no encoding),
  and word operations on `An` (e.g. `MOVE.W` to `An`) sign-extend into all 32 bits.
- **`(An)+`/`-(An)`** adjust by the operand size, and by 2 even for byte operations — the stack
  never becomes misaligned. Only `MOVEM` uses its own rule (below).
- **The index register `Xn`** is `Dn` or `An`, `.W` or `.L`; a `.W` index is sign-extended before the
  add. `An` used as `Xn` is used whole, never sign-extended. The syntax `d8(An,Xn)` always names
  `Xn`; the size and the D/A choice live in the extension word.
- **PC-relative base** is the address of the **extension word** (opcode address + 2), not the next
  instruction. Together with the prefetch (§8) this is the classic source of off-by-2 bugs. †
- **`#imm`** is fetched as an extension word *before* the operand is used, so an immediate operand
  costs a read cycle even though it never reaches memory.
- **Two extension words** for `d8(An,Xn)`: first the brief index word (`Xn`, size, scale†
  — scale is always 1 on the 68000), then the 8-bit displacement in the low byte.

### Which modes an instruction accepts

Notation in §5's tables: **D** = data alterable (`Dn` + all memory modes 3–9), **M** = memory
alterable (3–9, no `Dn`), **C** = control (3, 6, 7, 8, 9, 10, 11), **A** = anything readable
(D + `An` + PC-relative + `#`), **R** = `An` destination, **X** = `Dn` only.

---

## 4. How an instruction is encoded

One opcode word, then zero to four extension words, in the order the operands appear left to right
(immediate first for `#imm,<ea>`). The two-operand ALU instructions pack both EAs in the single
opcode word; the source EA is always bits 5–0.

```
15 14 13 12 | 11 10 9 | 8 7 6 | 5 4 3 | 2 1 0
 family     | dst reg | dst mode | src mode | src reg
```

The 68000 fetches words in pairs into a 2-word prefetch queue; the visible `PC` is the address two
words past the opcode being executed (or past the last extension word fetched). Every access the
instruction performs — opcode fetch, extension fetch, operand read, operand write — is a separate
bus cycle (§8). †

---

## 5. The instruction set, by family

### 5.1 Data movement

| Mnemonic | Sizes | Syntax | Encoding | Flags | Notes |
|---|---|---|---|---|---|
| `MOVE` | B/W/L | `MOVE <ea>,<ea>` | `00 ss` + dst reg/mode (11-6) + src mode/reg (5-0) | `N Z`, `V C`=0 | mem→mem allowed; dst mode `001` makes it `MOVEA`; byte to `An` illegal |
| `MOVEA` | W/L | `MOVEA <ea>,An` | `MOVE` with dst mode `001` | — | word sign-extends to 32 bits |
| `MOVEQ` | L | `MOVEQ #d8,Dn` | `0111 rrr 0 dddddddd` | `N Z`, `V C`=0 | 8-bit immediate sign-extended to 32; 4 clocks |
| `MOVEM` | W/L | `MOVEM <list>,<ea>` / `MOVEM <ea>,<list>` | `0100 1 d 0 1 ss mmm rrr` + mask word | — | see below |
| `MOVEP` | W/L | `MOVEP d16(An),Dn` / `MOVEP Dn,d16(An)` | `0000 rrr 1 ss 001 aaa` + d16 | — | byte-wide transfers on alternate addresses: `d16+0,2` (W) or `+0,2,4,6` (L) |
| `LEA` | L | `LEA <ea>,An` | `0100 rrr 111 mmm rrr` | — | EA only, no memory read |
| `PEA` | L | `PEA <ea>` | `0100 1000 01 mmm rrr` | — | computes EA, pushes it |
| `EXG` | L | `EXG Dx,Dy` / `EXG Ax,Ay` / `EXG Dx,Ay` | `1100 rrr 1 01000 rrr` / `…01001…` / `…10001…` | — | whole 32 bits |
| `SWAP` | W | `SWAP Dn` | `0100 1000 0100 0 rrr` | `N Z`, `V C`=0 | exchanges the two halves of `Dn` |
| `LINK` | — | `LINK An,#d16` | `0100 1110 0101 0 rrr` + d16 | — | `-(SP)←An`, `An←SP`, `SP+=d16` (negative d16 allocates) |
| `UNLK` | — | `UNLK An` | `0100 1110 0101 1 rrr` | — | `SP←An`, `An←(SP)+` |
| `MOVE SR,<ea>` | W | `MOVE SR,<ea>` | `0100 0000 11 mmm rrr` (`40C0`) | — | non-privileged |
| `MOVE <ea>,SR` | W | `MOVE <ea>,SR` | `0100 0110 11 mmm rrr` (`46C0`) | all | privileged |
| `MOVE <ea>,CCR` | B | `MOVE <ea>,CCR` | `0100 0100 11 mmm rrr` (`44C0`) | all | loads the low byte into `CCR` |
| `MOVE USP` | L | `MOVE An,USP` / `MOVE USP,An` | `0100 1110 0110 0 rrr` / `…0110 1 rrr` | — | privileged |
| `ANDI/ORI/EORI to CCR` | B | `ANDI #b,CCR` | `023C` / `003C` / `0A3C` + byte | per op | non-privileged |
| `ANDI/ORI/EORI to SR` | W | `ANDI #w,SR` | `027C` / `007C` / `0A7C` + word | per op | privileged |

`MOVEM` in detail — the only instruction whose register order and address arithmetic are surprising:

- The mask is a 16-bit extension word: bit 0 ↔ `D0` … bit 7 ↔ `D7`, bit 8 ↔ `A0` … bit 15 ↔ `A7` —
  **except** with the predecrement destination, where the mask is reversed (bit 0 ↔ `A7`).
- Register→memory in increasing address order after the base `An`; `-(An)` writes in decreasing
  order, decrementing *before* each write, so the last register written sits at `An`.
- Memory→register with `(An)+` reads in increasing order; word transfers sign-extend into the full
  32-bit register.
- Byte size does not exist (there is no `MOVEM.B`); the minimum step is 2.

### 5.2 Integer arithmetic

| Mnemonic | Sizes | Syntax | Encoding | Flags | Notes |
|---|---|---|---|---|---|
| `ADD` | B/W/L | `ADD <ea>,Dn` / `ADD Dn,<ea>` | `1101 rrr d ss mmm rrr` | `X N Z V C` | `d=0` EA+`Dn`→`Dn`; `d=1` `Dn`+EA→EA (memory only) |
| `ADDA` | W/L | `ADDA <ea>,An` | as `ADD` with dst mode `001` | — | never touches flags; word sign-extends |
| `ADDI` | B/W/L | `ADDI #imm,<ea>` | `0000 0110 ss mmm rrr` | `X N Z V C` | |
| `ADDQ` | B/W/L | `ADDQ #q,<ea>` | `0101 qqq 0 ss mmm rrr` | `X N Z V C` | `qqq=000` means 8; on `An` no flags, no byte |
| `ADDX` | B/W/L | `ADDX Dy,Dx` / `ADDX -(Ay),-(Ax)` | `1101 rrr 1 ss 0 0 rrr` / `…1 ss 0 1 rrr` | `X N Z V C` | `Dx+Dy+X`; `Z` is only *cleared* (when the result ≠ 0) and never set, so a chain accumulates "all words zero" |
| `SUB` | B/W/L | `SUB <ea>,Dn` / `SUB Dn,<ea>` | `1001 rrr d ss mmm rrr` | `X N Z V C` | mirrors `ADD` |
| `SUBA` | W/L | `SUBA <ea>,An` | dst mode `001` | — | |
| `SUBI` | B/W/L | `SUBI #imm,<ea>` | `0000 0100 ss mmm rrr` | `X N Z V C` | |
| `SUBQ` | B/W/L | `SUBQ #q,<ea>` | `0101 qqq 1 ss mmm rrr` | `X N Z V C` | `qqq=000` means 8 |
| `SUBX` | B/W/L | `SUBX Dy,Dx` / `SUBX -(Ay),-(Ax)` | `1001 rrr 1 ss 0 0 rrr` / `…0 1 rrr` | `X N Z V C` | `Dx−Dy−X`; same `Z` rule as `ADDX` |
| `CMP` | B/W/L | `CMP <ea>,Dn` | `1011 rrr 0 ss mmm rrr` | `N Z V C` | `Dn − EA`, result discarded |
| `CMPA` | W/L | `CMPA <ea>,An` | dst mode `001` | `N Z V C` | word sign-extends |
| `CMPI` | B/W/L | `CMPI #imm,<ea>` | `0000 1100 ss mmm rrr` | `N Z V C` | |
| `CMPM` | B/W/L | `CMPM (Ay)+,(Ax)+` | `1011 rrr 1 ss 001 rrr` | `N Z V C` | both `An` post-increment |
| `NEG` | B/W/L | `NEG <ea>` | `0100 0100 ss mmm rrr` | `X N Z V C` | `0 − EA` |
| `NEGX` | B/W/L | `NEGX <ea>` | `0100 0000 ss mmm rrr` | `X N Z V C` | `0 − EA − X`; same `Z` rule as `ADDX` |
| `CLR` | B/W/L | `CLR <ea>` | `0100 0010 ss mmm rrr` | `N`=0, `Z`=1, `V C`=0, `X` — | † performs a **read** before the write |
| `EXT` | W/L | `EXT.W Dn` / `EXT.L Dn` | `0100 1000 ss 000 rrr` (`ss`=`10`=W, `11`=L) | `N Z`, `V C`=0 | byte→word, word→long |
| `MULU` | W | `MULU <ea>,Dn` | `1100 rrr 011 mmm rrr` | `N Z`, `V C`=0 | 16×16→32 unsigned; 38–70 clocks, data-dependent |
| `MULS` | W | `MULS <ea>,Dn` | `1100 rrr 111 mmm rrr` | `N Z`, `V C`=0 | signed; 38–70 clocks |
| `DIVU` | W | `DIVU <ea>,Dn` | `1000 rrr 011 mmm rrr` | `N Z` (undefined), `V`, `C`=0 | 32÷16: quotient in low word, remainder in high; overflow → `V`=1, `Dn` unchanged |
| `DIVS` | W | `DIVS <ea>,Dn` | `1000 rrr 111 mmm rrr` | same | signed; 76–140 unsigned / 120–158 signed clocks |
| `ABCD` | B | `ABCD Dy,Dx` / `ABCD -(Ay),-(Ax)` | `1100 xxx 1 0000 0 rrr` / `…1 0000 1 rrr` | `X N Z V C` | packed BCD add with `X`; `Z` per multi-precision rule |
| `SBCD` | B | `SBCD Dy,Dx` / `SBCD -(Ay),-(Ax)` | `1000 xxx 1 0000 0 rrr` / `…0 1 rrr` | `X N Z V C` | BCD subtract |
| `NBCD` | B | `NBCD <ea>` | `0100 1000 00 mmm rrr` | `X N Z V C` | `0 − EA` in BCD |

Division by zero does not set a flag: it raises the zero-divide exception (vector 5) before `Dn`
is modified.

### 5.3 Logic and bit manipulation

| Mnemonic | Sizes | Syntax | Encoding | Flags | Notes |
|---|---|---|---|---|---|
| `AND` | B/W/L | `AND <ea>,Dn` / `AND Dn,<ea>` | `1100 rrr d ss mmm rrr` | `N Z`, `V C`=0 | `d=1` memory destination only |
| `ANDI` | B/W/L | `ANDI #imm,<ea>` | `0000 0010 ss mmm rrr` | `N Z`, `V C`=0 | to `CCR`/`SR`: §5.1 |
| `OR` | B/W/L | `OR <ea>,Dn` / `OR Dn,<ea>` | `1000 rrr d ss mmm rrr` | `N Z`, `V C`=0 | |
| `ORI` | B/W/L | `ORI #imm,<ea>` | `0000 0000 ss mmm rrr` | `N Z`, `V C`=0 | |
| `EOR` | B/W/L | `EOR Dn,<ea>` | `1011 rrr 1 ss mmm rrr` | `N Z`, `V C`=0 | destination is memory only — no `EOR <ea>,Dn` form exists |
| `EORI` | B/W/L | `EORI #imm,<ea>` | `0000 1010 ss mmm rrr` | `N Z`, `V C`=0 | |
| `NOT` | B/W/L | `NOT <ea>` | `0100 0110 ss mmm rrr` | `N Z`, `V C`=0 | one's complement |
| `TST` | B/W/L | `TST <ea>` | `0100 1010 ss mmm rrr` | `N Z`, `V C`=0 | reads the operand (can fault); `An` not allowed |
| `TAS` | B | `TAS <ea>` | `0100 1010 11 mmm rrr` | `N Z`, `V C`=0 | reads, tests, sets bit 7, writes back — the 68000's atomic test-and-set |
| `BTST` | B/L | `BTST Dn,<ea>` / `BTST #n,<ea>` | `0000 rrr 1 00 mmm rrr` / `0000 1000 00 mmm rrr` + byte | `Z` | bit number mod 32 for `Dn`, mod 8 for memory; the only bit op that reads, and the only one whose static form also accepts `d(PC)`/`#` as source |
| `BCHG` | B/L | `BCHG Dn,<ea>` / `BCHG #n,<ea>` | `0000 rrr 1 01 mmm rrr` / `0000 1000 01 mmm rrr` + byte | `Z` | complement bit, write back |
| `BCLR` | B/L | `BCLR Dn,<ea>` / `BCLR #n,<ea>` | `0000 rrr 1 10 mmm rrr` / `0000 1000 10 mmm rrr` + byte | `Z` | clear bit, write back |
| `BSET` | B/L | `BSET Dn,<ea>` / `BSET #n,<ea>` | `0000 rrr 1 11 mmm rrr` / `0000 1000 11 mmm rrr` + byte | `Z` | set bit, write back |

`Z` is set to the complement of the bit's *previous* value, so a loop on `BTST` tests and a loop on
`BSET` avoids a second read. The rewriting forms (`BCHG/BCLR/BSET`, `TAS`, arithmetic to memory)
are read-modify-write: two bus cycles at the same address, with the bus held for the pair (§8). †

Sizes: bit operations are byte (`Dn` bit 0–7) or long (`Dn` bit 0–31); they never write `An`.

### 5.4 Shifts and rotates

Register form — count 1–8 immediate or a data register (mod 64):

```
1110 CCC D SS I TT RRR
      |   |  | |  |
      |   |  | |  +-- register
      |   |  | +----- type: 00 AS, 01 LS, 10 ROX, 11 RO
      |   |  +------- 0 = immediate count, 1 = count in Dn
      |   +---------- size 01 B, 11 W, 10 L
      +-------------- direction: 0 right, 1 left
```

Memory form — one word, count 1, no register count:

```
1110 0 TT D 11 MMM RRR
```

| Mnemonic | Sizes | Syntax | Flags | Notes |
|---|---|---|---|---|
| `ASL`/`ASR` | B/W/L (+W memory) | `ASd #n,Dn` · `ASd Dn,Dm` · `ASd <ea>` | `X N Z V C` | arithmetic: the sign bit is replicated; `V` set if the sign changes at any step |
| `LSL`/`LSR` | B/W/L (+W memory) | as above | `X N Z V C` | zeros shifted in |
| `ROXL`/`ROXR` | B/W/L (+W memory) | as above | `X N Z V C` | rotate through `X` (33/17/9-bit ring) |
| `ROL`/`ROR` | B/W/L (+W memory) | as above | `N Z V C` | plain ring; `X` untouched; `V` set if the sign changes |

Count 0 (immediate 0 = 8, or a register holding 0 / a multiple of the size): nothing moves, `C` is
cleared, `X` is untouched, `N Z V` describe the unchanged operand. `C` is the last bit shifted out;
`V` is meaningful only for single-bit shifts.

### 5.5 Program control

| Mnemonic | Syntax | Encoding | Flags | Notes |
|---|---|---|---|---|
| `Bcc` | `Bcc <label>` | `0110 cccc dddddddd` | — | 8-bit displacement (`$FE` = −2, i.e. the instruction itself); a displacement of `$00` means "16-bit displacement follows in one extension word" |
| `BRA` | `BRA <label>` | `0110 0000 …` | — | condition `0000` = always true |
| `BSR` | `BSR <label>` | `0110 0001 …` | — | pushes the return address, then branches |
| `DBcc` | `DBcc Dn,<label>` | `0101 cccc 11001 rrr` + d16 | — | if *cc* is true, fall through; else `Dn−1` (word), branch unless `Dn` = `$FFFF` |
| `Scc` | `Scc <ea>` | `0101 cccc 11 mmm rrr` | — | writes `$FF` if *cc* true, `$00` otherwise; byte only; with `T`/`F` it is "always"/"never" |
| `JMP` | `JMP <ea>` | `0100 1110 11 mmm rrr` | — | control modes only |
| `JSR` | `JSR <ea>` | `0100 1110 10 mmm rrr` | — | pushes return address |
| `RTS` | `RTS` | `4E75` | — | pops the PC |
| `RTR` | `RTR` | `4E77` | all | pops `CCR` then PC (used after `JSR`, not for exceptions) |
| `RTE` | `RTE` | `4E73` | all | privileged; pops `SR` then PC — the reverse of the exception push order |
| `NOP` | `NOP` | `4E71` | — | one opcode word, nothing else |
| `STOP` | `STOP #imm` | `4E72` + imm | all | privileged; loads `SR`, then idles until an interrupt of level > mask (or level 7) arrives; the PC pushed for that interrupt is the instruction after `STOP` |
| `RESET` | `RESET` | `4E70` | — | privileged; asserts the `RESET` pin for 124 clocks; the CPU's own registers are **not** reset |
| `TRAP #n` | `TRAP #n` | `4E40+n` (`n`=0–15) | — | vector `32+n`; always privileged-agnostic (works in user mode) |
| `TRAPV` | `TRAPV` | `4E76` | — | vector 7 if `V` is set, else nothing |
| `CHK` | `CHK <ea>,Dn` | `0100 rrr 110 mmm rrr` | `N` | word, signed: trap to vector 6 if `Dn < 0` or `Dn > bound`; `N`=1 for the low case, 0 for the high, undefined in range |
| `ILLEGAL` | — | any unassigned opcode | — | vector 4, including `$4AFC`, the conventional "illegal" instruction |

`Bcc` on the 68000 has only the 8- and 16-bit forms; the 32-bit displacement (`d8 = $FF`) is a
68020 extension. `DBcc` counts in the low word of `Dn` and leaves the high word alone.

### 5.6 Condition codes

| `cccc` | Name | True when | Alias |
|---|---|---|---|
| `0000` | `T` | always | — |
| `0001` | `F` | never | — |
| `0010` | `HI` | `C=0` and `Z=0` | unsigned > |
| `0011` | `LS` | `C=1` or `Z=1` | unsigned ≤ |
| `0100` | `CC` | `C=0` | `HS`, unsigned ≥ |
| `0101` | `CS` | `C=1` | `LO`, unsigned < |
| `0110` | `NE` | `Z=0` | signed/unsigned ≠ |
| `0111` | `EQ` | `Z=1` | = |
| `1000` | `VC` | `V=0` | no overflow |
| `1001` | `VS` | `V=1` | overflow |
| `1010` | `PL` | `N=0` | plus |
| `1011` | `MI` | `N=1` | minus |
| `1100` | `GE` | `N=V` | signed ≥ |
| `1101` | `LT` | `N≠V` | signed < |
| `1110` | `GT` | `Z=0` and `N=V` | signed > |
| `1111` | `LE` | `Z=1` or `N≠V` | signed ≤ |

Comparison is `Dn − EA`, so `CMP`/`CMPI`/`CMPM` plus the table above is the whole branch idiom.

---

## 6. Exceptions

An exception is a hardware-initiated subroutine call whose vector number selects a 4-byte entry in
the table at address 0. There are three groups:

| Group | Cause | Stack frame | Notes |
|---|---|---|---|
| 0 | reset, initial SSP/PC | none | `S`=1, `T`=0, `I`=7; `SSP`←`(0)`, `PC`←`(4)` |
| 1 | trace, interrupt, trap-like instructions | 6 bytes | restarts cleanly |
| 2 | bus error, address error | **14 bytes** | not restartable with precision on the 68000 † |

### The vector table

| Vector | Address | Exception | Vector | Address | Exception |
|---|---|---|---|---|---|
| 0 | `$000` | initial SSP | 11 | `$02C` | line F (coprocessor) |
| 1 | `$004` | initial PC | 12–14 | `$030` | reserved |
| 2 | `$008` | bus error | 15 | `$03C` | uninitialised interrupt |
| 3 | `$00C` | address error | 16–23 | `$040` | reserved |
| 4 | `$010` | illegal instruction | 24 | `$060` | spurious interrupt |
| 5 | `$014` | zero divide | 25–31 | `$064` | autovectors, levels 1–7 |
| 6 | `$018` | `CHK` | 32–47 | `$080` | `TRAP #0–#15` |
| 7 | `$01C` | `TRAPV` | 48–63 | `$0C0` | reserved |
| 8 | `$020` | privilege violation | 64–255 | `$100` | user-defined (the ST's TOS/GEM use these) |
| 9 | `$024` | trace | | | |
| 10 | `$028` | line A (unimplemented) | | | |

### The processing sequence (group 1)

1. The vector number is determined internally (instruction, or the IACK cycle for an interrupt).
2. The current `SR` is copied out; the new `SR` gets `S`=1, `T`=0, and `I` = the interrupt level
   (unchanged for non-interrupt exceptions).
3. `PC` is pushed (4 bytes), *then* the old `SR` (2 bytes) — so at the handler, `0(SP)` = `SR`,
   `2(SP)` = `PC`. `PC` is the address of the next instruction (the instruction after `STOP`,
   `TRAP`, or the one that faulted).
4. `PC` ← `(vector × 4)`; the first handler instruction is fetched.

`RTE` reverses steps 3–4: pop `SR`, then `PC`.

The **group 2 frame** on a 68000 pushes 14 bytes, in this order from the new `SP`:

| Offset | Content |
|---|---|
| `0(SP)` | status word: R/W flag, function code of the faulting access, and (undocumented) high bits of the opcode |
| `2(SP)` | faulting address, long |
| `6(SP)` | instruction register (the opcode being executed) |
| `8(SP)` | `SR` at the time of the fault |
| `10(SP)` | `PC` |

An **address error** is raised for any word/long access (instruction fetch included) at an odd
address; a **bus error** is raised when `BERR` is asserted during a cycle. Because the 68000 does
not record which internal step failed, the frame is not reliably restartable — a handler normally
reports the fault and kills the process. A second fault inside the exception sequence (a double bus
fault) **halts** the CPU (§8). †

Trace and interrupt both pending at the end of an instruction: **trace is taken first**.
Trace is not taken for the instruction that caused another exception.

---

## 7. Interrupts

Three pins, `IPL2–IPL0`, encode a level 0–7 (0 = none, encoded as all three high because the lines
are active low). The rule:

```
interrupt taken  ⟺  level > I2–I0      (or level = 7, which is always taken)
```

Level 7 is edge-sensitive: it is recognised on the transition from a lower level to 7, and it is
never masked. Levels 1–6 are level-sensitive and must stay asserted until the acknowledge.

Recognition happens between instructions, after the current instruction completes, so the latency
is the rest of the current instruction plus the acknowledge and exception sequence. A pending
level that is masked at the end of an instruction is simply not seen; it will be seen as soon as
the mask drops (`MOVE` to `SR`, `ANDI` to `SR`, `RTE`).

### The acknowledge cycle

After deciding to take level *L*, the CPU runs one special bus cycle:

- `FC2–FC0` = `111` (CPU space), `A23–A4` = all ones, `A3–A1` = *L*, `R/W` = read (there is no `A0`
  pin; on the byte lanes the address is even).
- A device that wants to supply a vector asserts `DTACK` and puts the vector number on `D7–D0`
  (e.g. the ST's MFP, whose `VR` register forms `(VR & $F0) + source`).
- If the board asserts `VPA` instead of `DTACK`, the CPU **autovectors**: the vector number is
  `24 + L`, no data is read.
- If `BERR` is asserted instead, the **spurious interrupt** (vector 24) is taken.

Then the group-1 sequence runs: mask ← *L*, `S`=1, `T`=0, push `PC`, push `SR`, jump through
`(24+L)×4` or `vector×4`.

Vectors 25–31 (`$64`–`$7C`) are therefore the seven autovector entries; on the ST, VBL uses level 4
(`$70`) and HBL level 2 (`$68`), both autovectored, while the MFP supplies a real vector through
`DTACK`, so the level-6 autovector `$78` is not what an MFP handler is entered through.

An interrupt whose level was asserted but whose acknowledge yields `BERR` (no device claimed it) is
the classic spurious-interrupt source, usually a stale MFP/GLUE condition.

---

## 8. The bus

### Signals

| Group | Lines | Meaning |
|---|---|---|
| address | `A23–A1` | word address; **no `A0`** — byte position comes from the strobes |
| data | `D15–D0` | `D15–D8` = even byte, `D7–D0` = odd byte |
| strobes | `AS`, `UDS`, `LDS` | address strobe, upper/lower data strobe (all active low) |
| control | `R/W`, `DTACK`, `BERR`, `HALT`, `RESET`, `CLK` | direction, slave acknowledge, fault, halt, reset |
| space | `FC2–FC0` | function code: `001` user data, `010` user program, `101` supervisor data, `110` supervisor program, `111` CPU space (IACK) |
| interrupt | `IPL2–IPL0` | level, active low |
| arbitration | `BR`, `BG`, `BGACK` | bus request / grant / grant acknowledge |
| peripheral | `E`, `VPA`, `VMA` | 6800-style clock and acknowledge (unused by the ST) |

All three function codes are driven, so a slave can tell a program fetch from a data access and a
supervisor access from a user one; the ST uses that chiefly to tell an interrupt acknowledge cycle
(`FC`=111) apart from ordinary memory and I/O traffic.

### A bus cycle

Four clock periods minimum, eight states `S0–S7` (two per clock). In the minimum case:

| State | Event |
|---|---|
| `S0` | bus idle, address still high-impedance; the previous cycle ends here |
| `S1` | address, `FC` and `R/W` driven and valid, held until `S0` of the next cycle |
| `S2` | on the rising edge: `AS` asserted, and `UDS`/`LDS` for the bytes in play |
| `S3–S5` | the slave answers; the CPU waits here, inserting whole clocks, until `DTACK` or `BERR` |
| `S6` | the transfer: on a read the device drives data, latched at the end of `S6`; on a write the CPU drives it |
| `S7` | strobes negated, cycle ends |

`DTACK` and `BERR` are sampled on the falling edge of each clock while the cycle sits in `S3–S5`;
each clock they arrive late inserts a full clock (a wait state) into the cycle. `BERR` at the same
point aborts the cycle and enters the bus error exception. A slave may therefore stretch a cycle
arbitrarily — this is how the ST's GLUE and MMU make slow ROM, and how a chip that is not there
produces a bus error instead of a hang.

Byte lanes: for a byte access at an even address, `UDS` is asserted and the byte sits on `D15–D8`;
at an odd address, `LDS` and `D7–D0`. A word access asserts both. Address `A1` is 0 for all word
and long accesses (odd ones are address errors), so `A1` doubles as "this is a byte access at an
odd address".

Long accesses are **two consecutive word cycles**, high word first, at `addr` then `addr+2`, for
reads and writes alike. Nothing on the bus ever transfers 32 bits at once — this is why a long
access costs twice a word and why a device can see the halves separately.

Special sequences worth modelling exactly:

- **Read-modify-write** (`ADD.W Dn,(An)`, `TAS`, `BSET`, …): a read cycle followed by a write cycle
  to the same address. The 68000 does not release the bus between them (no arbitration in the
  gap), which is what makes `TAS` a lock. `TAS` is the odd one: its write starts later than a normal
  RMW pair, one dead clock, so its total is not simply read+write. †
- **`MOVEM`** performs one cycle per register, in the order of §5.1, no gap.
- **`MOVEP`** performs one byte cycle per transferred byte at every second address.
- **`CLR`** reads before it writes (so `CLR` on absent memory still bus-errors).
- **`RESET`** asserts the `RESET` pin for 124 clocks.
- Interrupt acknowledge is a single cycle in CPU space with `FC`=111, as described in §7.

### Arbitration, HALT, BERR

`BR` is sampled every cycle; when asserted the CPU finishes the current bus cycle, asserts `BG`, and
**three-states** the bus after the current cycle (it may complete the instruction's remaining
cycles only after `BGACK` is negated). The requesting device asserts `BGACK` and drives the bus, and
the handshake ends when `BG` and `BGACK` drop.

`HALT` asserted alone stops the CPU at the end of the current bus cycle with the bus released; when
released, the interrupted cycle is rerun. `HALT` with `BERR` causes a re-run of the faulting cycle
(the "rerun" bus-error mode). A bus error during exception processing (double bus fault) leaves the
CPU halted until `RESET`.

### The prefetch queue

Two words are prefetched ahead of execution; the visible `PC` is the address of the **next word to
be fetched**, and the fetch of an instruction's extension words consumes prefetch slots. This is
why a PC-relative operand and a stacked PC differ by the instruction length plus the queue, and why
self-modifying code needs a flush: on the 68000 that flush is implicit in any branch, but not in a
write to the next instruction.

---

## 9. Timing

Clocks, not nanoseconds: at 8 MHz one clock is 125 ns and the minimum bus cycle is 4 clocks
(500 ns). Every memory operand costs its bus cycles. Verified effective-address times (clocks,
with bus cycles as `r/w`) — these are the 68000's numbers and the basis of every other figure:

| Mode | Byte/Word | Long |
|---|---|---|
| `Dn`, `An` | 0 | 0 |
| `(An)` | 4 (1/0) | 8 (2/0) |
| `(An)+` | 4 (1/0) | 8 (2/0) |
| `-(An)` | 6 (1/0) | 10 (2/0) |
| `d16(An)` | 8 (2/0) | 12 (3/0) |
| `d8(An,Xn)` | 10 (2/0) | 14 (3/0) |
| `(xxx).W` | 8 (2/0) | 12 (3/0) |
| `(xxx).L` | 12 (3/0) | 16 (4/0) |
| `d16(PC)` | 8 (2/0) | 12 (3/0) |
| `d8(PC,Xn)` | 10 (2/0) | 14 (3/0) |
| `#imm` | 4 (1/0) | 8 (2/0) |

The index register's size does not change the time. Building on that table:

- register-to-register ALU work is 4 clocks (byte/word) or 8 (long);
- a single memory operand adds 4 clocks per word transferred, 8 per long — so `MOVE.W Dn,(An)` is
  8, `MOVE.L Dn,(An)` is 12, and `MOVE.W (An),Dn` is 8;
- the extra write half of a read-modify-write adds its own 4 or 8;
- `MULU/MULS` are 38–70 and `DIVU`/`DIVS` 76–140 / 120–158, all data-dependent, so they cannot be
  modelled as constants in a cycle-exact core;
- exception entry adds the two stacked words plus the vector fetch; interrupt entry adds the
  acknowledge cycle before that.

Individual totals for every instruction are tabulated in the M68000 Programmer's Reference Manual,
Appendix D; the table above plus these rules reproduces its MOVE and ALU figures exactly, which is
the useful cross-check before encoding any of them as goldens.

---

## 10. Opcode map

The top nibble selects the family, which is how a decoder starts:

| Nibble | Contents |
|---|---|
| `0` | immediate ops (`ORI/ANDI/SUBI/ADDI/EORI/CMPI`), bit ops (`BTST/BCHG/BCLR/BSET`), `MOVEP` |
| `1` | `MOVE.B` |
| `2` | `MOVE.L` |
| `3` | `MOVE.W` |
| `4` | miscellaneous: `NEGX/CLR/NEG/NOT`, `MOVE from SR`, `NBCD`, `PEA`, `SWAP`, `EXT`, `TST`, `TAS`, `MOVEM`, `TRAP`, `LINK/UNLK`, `RESET/NOP/STOP/RTE/RTS/TRAPV/RTR`, `JSR/JMP`, `CHK`, `LEA` |
| `5` | `ADDQ`, `SUBQ`, `Scc`, `DBcc` |
| `6` | `BRA`, `BSR`, `Bcc` |
| `7` | `MOVEQ` |
| `8` | `OR`, `DIVU`, `DIVS`, `SBCD` |
| `9` | `SUB`, `SUBA`, `SUBX` |
| `A` | line A — unimplemented, trap to vector 10 |
| `B` | `CMP`, `CMPA`, `CMPM`, `EOR` |
| `C` | `AND`, `MULU`, `MULS`, `ABCD`, `EXG` |
| `D` | `ADD`, `ADDA`, `ADDX` |
| `E` | shifts and rotates |
| `F` | line F — unimplemented, trap to vector 11 |

Line A and line F are the two holes: `$Axxx` and `$Fxxx` raise exceptions rather than decode as
instructions, which is exactly how TOS/GEM get their trap calls, and why an emulator must raise
them rather than treat the opcode as illegal (vector 4).

---

## 11. Reset

`RESET` asserted for at least 10 clocks, then:

1. `S`=1, `T`=0, `I`=7;
2. `SSP` ← long at `$000`;
3. `PC` ← long at `$004`;
4. no stack frame is pushed, no vector is fetched beyond those two.

Everything else (registers, the rest of `SR`) is left undefined by the CPU; software must not
assume zeros. The `RESET` *instruction* is different: it pulses the pin (§5.5) and resets the
peripherals, not the CPU.

---

## 12. What this CPU does not have

Instructions and resources that exist on 68010/68020+ and must **not** be accepted by a 68000 core:

| Missing | Introduced by | Consequence |
|---|---|---|
| `VBR`, `SFC`, `DFC` | 68010 | vector table is fixed at 0; `MOVE` to/from `SFC`/`DFC`/`VBR` are illegal |
| `MOVEC` | 68010 | illegal |
| `RTD` | 68010 | illegal (`4E74`) |
| `BKPT` | 68010 | illegal |
| format/`$0` stack frames, restartable bus errors | 68010 | the 68000 pushes the 14-byte frame of §6 |
| `EXTB.L`, `MULS.L`, `DIVS.L`, `CHK2`, `CMP2`, `TAS`-less atomics, `BFxxx`, `PACK/UNPK` | 68020 | illegal |
| 32-bit displacements, memory-indirect modes, scaled indexes, `(d32,An)`, `([bd,An],Xn)` | 68020 | illegal |
| `MOVE CCR,<ea>`, `MOVE` from `CCR`-adjacent encodings | 68010 | `42C0` is illegal; only `MOVE <ea>,CCR` exists |
| cache (`CACR`, `CINV`), MMU (`PMOVE`, `PTEST`), FPU (`FMOVE`, …) | 68020/68881 | ST has no 68881; the STe's blitter is memory-mapped, not a coprocessor |

On the ST the practical reading of the last three rows: the ROM's 68010/68020 instructions never
execute, but a game or demo that probes for them expects an illegal-instruction trap, not different
behaviour.

---

## Appendix: the whole set at a glance

```
MOVE  MOVEA  MOVEQ  MOVEM  MOVEP  LEA  PEA  EXG  SWAP  LINK  UNLK
MOVE SR/<ea>,SR/<ea>,CCR/USP   ANDI/ORI/EORI to CCR, SR
ADD  ADDA  ADDI  ADDQ  ADDX    SUB  SUBA  SUBI  SUBQ  SUBX
CMP  CMPA  CMPI  CMPM          NEG  NEGX  CLR  EXT
MULU  MULS  DIVU  DIVS  ABCD  SBCD  NBCD
AND  ANDI  OR  ORI  EOR  EORI  NOT  TST  TAS
ASL  ASR  LSL  LSR  ROL  ROR  ROXL  ROXR      (register and memory forms)
BTST  BCHG  BCLR  BSET
Bcc  BRA  BSR  DBcc  Scc  JMP  JSR  RTS  RTR  RTE
TRAP  TRAPV  CHK  NOP  STOP  RESET
```

The complete 68000 set, twelve addressing modes, one 16-bit opcode word: everything the ST's
software can ask its CPU to do, and everything a core has to implement to be called a 68000.
