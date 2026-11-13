# The 68000 — a compact, exhaustive tutorial

> The processor of the Atari ST, from the ground up: every term defined before it is used, every
> instruction with a plain statement of what it does, and a picture wherever a picture beats a
> paragraph. Scope: the **base MC68000**, the part this machine actually contains — running at
> 8 MHz, with a 16-bit data connection and a 24-bit address connection, and with none of the extras
> that later chips of the family added. Anything a later part adds appears in §12 only, so nothing
> in §1–§11 is "not on this chip".
>
> **How to read it.** §0 is the vocabulary (read it first — every other section uses its words);
> §1 is the registers; §2–§4 are the three ingredients of an instruction (data, operands, encoding);
> §5 is the whole instruction set; §6–§9 cover what happens outside the straight line — faults,
> device requests, the wire-level protocol, and time. Diagrams are plain ASCII so they survive in
> any editor, diff and terminal.
>
> Conventions used in every table: `$` prefixes a hexadecimal (base-16) number; an opcode word is
> written as its 16 bits, most significant bit first, with letters standing for fields
> (`1110 ccc d ss i tt rrr`); `†` marks a detail only a *cycle-exact* model needs — one that
> reproduces the exact timing of every access, not just the result.

---

## 0. The words this document uses

Nothing below is used before it is defined here. Read it once; the rest reads plainly.

### 0.1 Numbers, sizes and the five flags

| Term | Meaning |
|---|---|
| **bit** | one binary digit. Bit 0 is the least significant (rightmost), bit 31 the most significant |
| **byte** | 8 bits |
| **word** | 16 bits — always 16 here, not the host's word |
| **long** / **longword** | 32 bits |
| **MSB / LSB** | most / least significant bit of a value |
| **nibble** | 4 bits, exactly one hex digit. A byte is two nibbles |
| **hexadecimal** | base 16; one hex digit is a nibble, so a byte is two digits (`$4E`), a word four (`$4E71`), a long eight (`$12345678`) |
| **two's complement** | how signed integers are stored: `−n` is `2^width − n`, so `$FFFF` is −1. One adder serves signed and unsigned |
| **sign extension** | widening a value by copying its top bit into the new high bits: `$FF` as a byte becomes `$FFFF` as a word, so −1 stays −1 |
| **BCD** | binary-coded decimal: one byte holds two decimal digits, high nibble then low (`$42` is 42) |
| **flag** / **condition code** | one bit recording something about the last result. Five exist: **N** negative, **Z** zero, **V** overflow, **C** carry, **X** extend |
| **C** (carry) | 1 when an unsigned add carried out of the top bit, or a subtract borrowed into it |
| **V** (overflow) | 1 when a *signed* result does not fit: two positives gave a negative, or two negatives gave a positive |
| **X** (extend) | a copy of the carry that survives the instruction, so `ADD`, `ADDX`, `ADDX`… can chain over multi-word numbers. Only `ADDX/SUBX/NEGX/ABCD/SBCD/ROXx` and the shifts read it |
| **N / Z** | sign of the result / result is all zeros |
| **undefined flag** | the instruction may leave any value; never test it |
| **CCR** | condition code register: the low byte of `SR`, holding `X N Z V C` |
| **SR** | status register: the 16-bit register holding the flags plus the mode bits (§1.2) |

### 0.2 The machine's own parts

| Term | Meaning |
|---|---|
| **register** | storage inside the CPU, named, not addressed through memory |
| **Dn** | one of the eight 32-bit *data* registers `D0–D7`: arithmetic, shifting, data movement |
| **An** | one of the eight 32-bit *address* registers `A0–A7`: addresses, pointers, counters |
| **PC** | program counter: the address of the next instruction word to be fetched |
| **SP** | stack pointer. On this CPU it *is* `A7` — whichever of two hidden pointers is current (§1.3) |
| **stack** | memory used last-in-first-out. It **grows downward**: a push decrements the pointer, a pop increments it |
| **push / pop** | write to `−(SP)` / read from `(SP)+` |
| **supervisor mode** | the privileged mode (`S`=1 in `SR`), where instructions that touch machine control state are allowed |
| **user mode** | the unprivileged mode (`S`=0); a privileged instruction here causes an exception, not a silent no-op |
| **privileged instruction** | one that only runs in supervisor mode: `MOVE` to `SR`, `ANDI/ORI/EORI` to `SR`, `STOP`, `RESET`, `RTE`, `MOVE USP` |
| **instruction** | one opcode word plus whatever extension words it needs |
| **opcode** | the first word of an instruction: which operation, which registers, which size, which operands |
| **extension word** | a word following the opcode that supplies a constant, a displacement or an index |
| **operand** | a value an instruction acts on: the **source** is read, the **destination** is written |
| **immediate operand** | a constant carried in the instruction stream (`#5`), not fetched from a computed address |
| **prefetch** | fetching instruction words ahead of execution into a small internal queue (§1.4) |
| **trap** | an exception *caused by an instruction* rather than by a pin |
| **ALU** | arithmetic and logic unit: the part of the CPU that adds, subtracts, shifts and combines, and that produces the flags |
| **DMA** | direct memory access: a device that reads or writes memory on its own, borrowing the bus from the CPU (§8.4) |

### 0.3 Memory and operands

| Term | Meaning |
|---|---|
| **address space** | everything the address bus reaches: 24 bits, `$000000`–`$FFFFFF`, 16 MB |
| **big-endian** | the byte order here: the **most** significant byte of a value sits at the **lowest** address |
| **aligned** | a word or long starting at an even address. An odd one is an **address error**, never a slow access |
| **effective address (EA)** | the address an operand is actually read from or written to, once the mode has done its arithmetic |
| **addressing mode** | the rule that produces an EA: register, indirect, displacement, index, absolute, PC-relative, immediate (§3) |
| **displacement (`d16`, `d8`)** | a signed constant added to a base address, carried in an extension word |
| **index register (`Xn`)** | a `Dn` or `An` whose value is added to a base address, optionally only its low word |
| **absolute address** | a fixed address written in the instruction: `(xxx).W` 16-bit, `(xxx).L` 32-bit |
| **PC-relative** | an address computed from the PC, so the code works wherever it is loaded |
| **post-increment `(An)+` / pre-decrement `−(An)`** | use `An` then add the operand size / subtract first, then use `An` |
| **read-modify-write (RMW)** | an instruction that reads a memory operand, computes, writes it back: two bus cycles at one address |
| **data / data alterable / memory alterable / control** | the four operand classes, i.e. which modes an instruction accepts (§3.1) |

### 0.4 Control flow, exceptions, interrupts

| Term | Meaning |
|---|---|
| **branch** | a jump: the instruction replaces the PC instead of falling through to the next one |
| **branch displacement** | a signed distance measured from the *end* of the branch instruction (§5.5) |
| **subroutine** | code entered with `JSR`/`BSR` so that `RTS` can return: the return address is pushed first |
| **exception** | any event that diverts the CPU to a fixed handler: reset, fault, trap, trace, interrupt |
| **vector** | a 4-byte table entry: the address of a handler |
| **vector number** | the index selecting one vector; its address is `number × 4` |
| **vector table** | the 256 vectors at address 0. On the 68000 it is fixed there — there is no `VBR` to move it |
| **handler** | the routine a vector points to |
| **interrupt** | an exception requested by a device through the `IPL` pins (§7) |
| **interrupt level** | 1–7, a priority carried on three pins; 7 is highest |
| **interrupt mask (`I2–I0`)** | interrupts at or below the mask are ignored: level *L* is taken only if *L* > mask |
| **NMI** | non-maskable interrupt: level 7, never blocked, and *edge*-triggered — recognised on the transition into the level, not while it is held (§7) |
| **acknowledge cycle (IACK)** | the special bus cycle in which the CPU asks a device for its vector number |
| **autovector** | the vector the CPU picks itself (`24 + level`) when the board does not supply one |
| **spurious interrupt** | an acknowledged interrupt that no device claimed; it uses vector 24 |
| **trace** | single-step mode: with `T`=1, an exception follows every instruction |
| **reset** | the pin (and instruction) that returns the machine to a known state (§11) |
| **HALT** | the pin that stops the CPU at the end of the current bus cycle |
| **bus error** | a fault signalled by `BERR`: the addressed device did not respond, or refused |
| **double fault** | a bus error while the CPU is already processing an exception; only reset recovers |
| **privilege violation / illegal instruction / line A / line F** | the exceptions for a privileged instruction in user mode, an undefined opcode, and the two reserved blocks `$Axxx`/`$Fxxx` |

### 0.5 The bus

| Term | Meaning |
|---|---|
| **bus** | the shared wires: address, data, control |
| **bus cycle** | one transfer on the bus: 4 clock periods, 8 states `S0–S7` (§8) |
| **clock** | the CPU's timing input; 8 MHz here, one period = 125 ns |
| **state** | half a clock period. A bus cycle counts `S0`…`S7` |
| **wait state** | a whole clock inserted into a cycle because the device is not ready |
| **`AS`** | Address Strobe: "the address and function code are valid now" |
| **`UDS` / `LDS`** | Upper / Lower Data Strobe: which half of the 16 data lines this cycle uses |
| **`R/W`** | 1 = read (device drives data), 0 = write (CPU drives data) |
| **`DTACK`** | Data Transfer Acknowledge: "this cycle is done" |
| **`BERR`** | Bus Error: abort this cycle |
| **`FC0–FC2`** | Function Code: program or data, supervisor or user space |
| **`IPL0–IPL2`** | interrupt request pins, carrying the level, active low |
| **`VPA` / `VMA` / `E`** | the 6800-peripheral handshake: Valid Peripheral Address (in), Valid Memory Address (out), enable clock (out) |
| **`BR` / `BG` / `BGACK`** | bus arbitration: request, grant, grant-acknowledge (§8.4) |
| **byte lane** | which half of the data bus a byte travels on: an even address uses `D15–D8`, an odd one `D7–D0` |
| **bus master** | whoever drives the bus — normally the CPU, otherwise a DMA device that asked for it |
| **three-state** | electrically disconnected (high impedance), so another master may drive the bus |

### 0.6 The names on this board

The CPU talks to a handful of named parts; they appear later, so they are named here.

| Name | What it is |
|---|---|
| **TOS** | The operating system in the machine's ROM: boot, BIOS, XBIOS, GEMDOS |
| **GEM** | The desktop layer TOS provides (AES/VDI): windows, drawing, mouse |
| **MFP** | MC68901 multi-function peripheral: timers, serial ports, and the interrupt controller that raises level 6 |
| **GLUE** | Atari chip: address decoding, video timing, and the bus control that answers the CPU with `DTACK` |
| **MMU** | Atari chip: turns the CPU's 24-bit addresses into DRAM row/column addresses and fetches video data |
| **Shifter** | Atari video chip: turns memory into pixels |
| **DMA chip** | Floppy and hard-disk data path on the ST; also sound on the STe |
| **VBL** | Vertical blank: the moment between frames, about 50 times a second (PAL) |
| **HBL** | Horizontal blank: the gap between scan lines, about 15 600 times a second |

Their wiring is the subject of
[../architecture/01-system-overview.md](../architecture/01-system-overview.md).

```
     address  ────▶ ┌──────────────────────────────────────┐ ────▶  address
        data  ◀───▶ │                                      │ ◀───▶  data
     control  ────▶ │              MC68000                 │ ────▶  control
   interrupt  ◀──── │                                      │ ◀────  interrupt, arbitration
       clock  ────▶ │   registers · ALU · prefetch queue   │
                    └──────────────────────────────────────┘
```
The signals behind those five groups are named in the table that follows.

Everything the CPU does is one of three things: compute inside that box, read the bus, or write the
bus. The rest of this document is those three.

---

## 1. The programmer's model

### 1.1 The registers

```
        ┌────┬────┬────┬────┬────┬────┬────┬────┐
 data   │ D0 │ D1 │ D2 │ D3 │ D4 │ D5 │ D6 │ D7 │   8 × 32 bits
        ├────┼────┼────┼────┼────┼────┼────┼────┤
address │ A0 │ A1 │ A2 │ A3 │ A4 │ A5 │ A6 │ A7 │   8 × 32 bits, and A7 is the SP
        └────┴────┴────┴────┴────┴────┴────┴────┘
        ┌──────────────────────────────┐  ┌────────────────────────────────┐
        │ PC    next word to fetch     │  │ SR    T – S I2–I0 – X N Z V C  │
        └──────────────────────────────┘  └────────────────────────────────┘
```

| Register | Width | Role | On a byte or word write |
|---|---|---|---|
| `D0–D7` | 32 | arithmetic, shifting, BCD, division, bit numbers | only that byte or word changes; the rest is untouched |
| `A0–A6` | 32 | addresses, counters, base registers | all 32 bits are written; a word write is sign-extended |
| `A7` | 32 | the stack pointer, `SP` | as any `An` |
| `PC` | 32 | address of the next instruction word to be fetched | written only by branches, `JMP`/`JSR` and exception entry |
| `SR` | 16 | flags, interrupt mask, mode, trace | via `MOVE` to `SR` (privileged) or `ANDI/ORI/EORI` to `SR` |

Those 18 longwords and the `SR` are the whole CPU state; everything else lives in memory. That is
why "save the machine" in an emulator means exactly these registers plus the pending opcode.

### 1.2 The status register, bit by bit

```
 bit  15  14  13  12  11  10   9   8   7   6   5   4   3   2   1   0
     ┌───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┐
     │ T │ – │ S │I2 │I1 │I0 │ – │ – │ – │ – │ – │ X │ N │ Z │ V │ C │
     └───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┘
       │     │   └──────┬──────┘               └────────────┬──────────┘
       │     │     interrupt mask                     the five flags = CCR
       │     └── supervisor (1 = supervisor mode)
       └── trace (1 = an exception after every instruction)
```

| Field | Meaning | Effect of setting it |
|---|---|---|
| `T`, bit 15 | trace | after the next instruction, exception 9 (vector `$24`) is taken |
| `S`, bit 13 | supervisor | `A7` becomes `SSP`; privileged instructions are allowed |
| `I2–I0`, bits 12–10 | interrupt mask | interrupts of level ≤ mask are ignored; 7 is never ignored |
| `X N Z V C`, bits 4–0 | the flags of §0.1 | drive `Bcc`, `Scc`, `DBcc` and every carry chain |
| `–` bit 14, bits 9–5 | unimplemented on the 68000 | read as 0, ignored on write |

`SR` is *read* whole by `MOVE SR,<ea>` and only *partly* written by `ANDI/ORI/EORI` to `SR`;
`MOVE <ea>,SR` writes all of it and is privileged. Entering supervisor mode is literally
`MOVE.W #$2700,SR` — mask 7, `S`=1, `T`=0.

### 1.3 Two stack pointers, one `A7`

```
   supervisor mode (S = 1)                 user mode (S = 0)
   ┌───────────────────────────────┐       ┌───────────────────────────────┐
   │  A7 ──▶ SSP                   │       │  A7 ──▶ USP                   │
   │         USP  hidden           │       │         SSP  hidden           │
   └───────────────────────────────┘       └───────────────────────────────┘
        MOVE An,USP  /  MOVE USP,An  reach the hidden one (privileged)
```

Both are full 32-bit registers; only one is reachable as `A7` at a time. A mode switch copies
nothing — it renames `A7` — which is why an exception handler runs on its own stack without
disturbing the user's.

### 1.4 The prefetch queue, and why the `PC` is not where you expect

The CPU fetches instruction words ahead of executing them and holds two in a small queue. `PC`
always points at the **next word to be fetched**, not at the instruction being executed:

```
 memory  $1000 ┌──────────┐
               │  opcode  │  ← executing now
         $1002 ├──────────┤
               │   ext    │  ← already fetched, in the queue
         $1004 ├──────────┤
               │  next    │  ← already fetched, in the queue   ◀── PC points here
         $1006 ├──────────┤
               │   ...    │
               └──────────┘
```

Two consequences matter more than any other implementation detail:

- a PC-relative operand (`d16(PC)`) is measured from the **address of its own extension word**, not
  from the next instruction;
- the address pushed by an exception is the address of the instruction *after* the one running —
  except on a fault, where it is the faulting instruction (§6).

---

## 2. Data in memory

The CPU is big-endian and requires alignment. Both are visible on the bus, so both are worth a
picture.

```
 long $12345678 stored at $1000

     $1000   $1001   $1002   $1003
    ┌───────┬───────┬───────┬───────┐
    │  12   │  34   │  56   │  78   │     highest byte at the lowest address
    └───────┴───────┴───────┴───────┘
    │ word 1        │ word 2        │     a long is two words, high word first
    └──── $1000 ────┴──── $1002 ────┘
```

| Access | Allowed addresses | Bus behaviour |
|---|---|---|
| byte | any | one byte, on one lane |
| word | even only | one word cycle, both lanes |
| long | even only | **two** word cycles: `$1000` then `$1002` |

```
 one word transfer on the 16 data lines, address $1000 (even: aligned)

      D15 D14 … D9 D8 │ D7 D6 … D1 D0
     ┌────────────────┼────────────────┐
     │  byte at $1000 │ byte at $1001  │
     └────────────────┴────────────────┘
               ▲ UDS             ▲ LDS
   a byte access at $1000 asserts only UDS; at $1001 only LDS; a word asserts both
```

There is no `A0` pin, so `A1` is what distinguishes the two byte cases: `A1`=0 is an even byte,
`A1`=1 an odd one, and every word or long has `A1`=0. `A1` high on a schematic therefore means
"odd byte access" and nothing else.

---

## 3. Addressing modes

An addressing mode is a rule for computing an **effective address**. Twelve exist, all encoded in a
single 6-bit field (`mmm:rrr`), so the mode and the register supplying the base form one unit.

```
  (A0)          A0 ──▶ ┌─────────┐
  (A0)+         A0 ──▶ ┌─────────┐   then A0 = A0 + size
  −(A0)         A0 = A0 − size,  A0 ──▶ ┌─────────┐
  8(A0)         A0 + 8 ──▶ ┌─────────┐
  8(A0,D1.W)    A0 + sign_extend(D1.W) + 8 ──▶ ┌─────────┐
  $1234         ┌─────────┐ sits at the fixed address $00001234
  8(PC)         (extension word address + 8) ──▶ ┌─────────┐
  #5            the operand *is* the word after the opcode — no address at all
  D0 , A0       the operand *is* the register — no address at all
```

| # | Mode field | Syntax | Effective address | Ext. words | Class |
|---|---|---|---|---|---|
| 1 | `000 rrr` | `Dn` | the register itself | 0 | data, alterable |
| 2 | `001 rrr` | `An` | the register itself | 0 | data (not byte), alterable as `An` |
| 3 | `010 rrr` | `(An)` | `An` | 0 | data, alterable, control |
| 4 | `011 rrr` | `(An)+` | `An`, then `An += 1/2/4` by size | 0 | data, alterable |
| 5 | `100 rrr` | `−(An)` | `An −= 1/2/4`, then `An` | 0 | data, alterable |
| 6 | `101 rrr` | `d16(An)` | `An + sign_extend(d16)` | 1 | data, alterable, control |
| 7 | `110 rrr` | `d8(An,Xn)` | `An + sign_extend(d8) + Xn` | 2 | data, alterable, control |
| 8 | `111 000` | `(xxx).W` | `sign_extend(xxx)`: an address in the first or last 32 KB | 1 | data, alterable, control |
| 9 | `111 001` | `(xxx).L` | `xxx` | 2 | data, alterable, control |
| 10 | `111 010` | `d16(PC)` | `ext_word_address + sign_extend(d16)` | 1 | data, control |
| 11 | `111 011` | `d8(PC,Xn)` | `ext_word_address + sign_extend(d8) + Xn` | 2 | data, control |
| 12 | `111 100` | `#imm` | the operand sits in the instruction stream | 1 or 2 | data |

Rules that decide behaviour, not just syntax:

- **`(An)+` and `−(An)` step by 2 even for a byte operand**, so a stack of bytes never becomes
  misaligned. `MOVEM` is the one exception (§5.1).
- **`An` as a source is always 32 bits.** Byte operations on `An` have no encoding at all, and a
  word operation on `An` (e.g. `MOVE.W` to `An`) sign-extends into all 32.
- **`Xn`** may be `Dn` or `An`, `.W` or `.L`. A `.W` index is sign-extended before the add; an `An`
  used as index is used whole. Its size never changes the timing.
- **The PC-relative base is the extension word's address** (§1.4).
- **`#imm` is a read like any other**: it costs a bus cycle, and a long immediate costs two words.
- **`d8(An,Xn)` takes two extension words**: first the *brief extension word* naming `Xn`, its size
  and (on later CPUs) a scale — always 1 here — then the 8-bit displacement in its low byte.

### 3.1 Which modes an instruction accepts

Instructions do not say "any mode"; they say "any *data alterable* mode", and that phrase carries
the legality table:

| Class | Modes | Typical user |
|---|---|---|
| **data** | `Dn`, `An`, `(An)`, `(An)+`, `−(An)`, `d16(An)`, `d8(An,Xn)`, `(xxx).W`, `(xxx).L`, `d16(PC)`, `d8(PC,Xn)`, `#imm` | a source that is only read |
| **data alterable** | data minus `An`, PC-relative and `#imm` | a destination that is a register or memory |
| **memory alterable** | `(An)`, `(An)+`, `−(An)`, `d16(An)`, `d8(An,Xn)`, `(xxx).W`, `(xxx).L` | `ADD Dn,<ea>` — the result must go back to memory |
| **control** | `(An)`, `d16(An)`, `d8(An,Xn)`, `(xxx).W`, `(xxx).L`, `d16(PC)`, `d8(PC,Xn)` | `LEA`, `JMP`, `JSR` — an address, never a value |

The two usual mistakes: `ADD.W #1,D0` (the memory form of `ADD` needs memory — write `ADDI`), and
`LEA D0,A0` (`LEA` needs an address to compute — write `MOVEA`).

---

## 4. How an instruction is encoded

Every instruction is one **opcode** word and, optionally, **extension words**. The two-operand
arithmetic instructions pack both operands into that single opcode word:

```
 15      12 11     9 8      6 5      3 2      0
 ┌──────────┬────────┬────────┬────────┬────────┐
 │  family  │ dst reg│dst mode│src mode│ src reg│
 └──────────┴────────┴────────┴────────┴────────┘

   ADD.W D0,(A1)  =  1101 000 1 10 010 001
                      └┬───┘ │ └┬┘ └─┬─┘ └┬┘
                       │     │  │     │    └─ source register: A1
                       │     │  │     └─ source mode: 010 = (An)
                       │     │  └─ size: 01 = byte, 11 = word, 10 = long
                       │     └─ direction: 0 = EA→Dn, 1 = Dn→EA
                       └─ ADD family
```

Extension words follow, in the order the operands are written:

```
 $1000 ┌───────────────────────┐
       │  opcode word          │
 $1002 ├───────────────────────┤
       │  extension word 1     │  displacement, immediate, or index descriptor
 $1004 ├───────────────────────┤
       │  extension word 2     │  only for a long immediate, or d8(An,Xn)
 $1006 ├───────────────────────┤
       │  next instruction     │
       └───────────────────────┘
```

So an instruction's length is not fixed: one opcode plus 0–2 extension words per operand that needs
one. That length is exactly why the `PC` of §1.4 matters.

---

## 5. The instruction set, by family

Flags column: `*` = set from the result, `0` = cleared, `–` = unaffected, `u` = undefined.

### 5.1 Data movement

| Mnemonic | Sizes | Syntax | What it does | Encoding | Flags |
|---|---|---|---|---|---|
| `MOVE` | B/W/L | `MOVE <ea>,<ea>` | copies the source operand to the destination; memory-to-memory is allowed | `00 ss` + dst reg/mode + src mode/reg | `N Z`, `V C`=0 |
| `MOVEA` | W/L | `MOVEA <ea>,An` | loads an address register; a word source is sign-extended to 32 bits | `MOVE` with dst mode `001` | — |
| `MOVEQ` | L | `MOVEQ #d8,Dn` | loads a sign-extended 8-bit constant: the shortest way to load a small number | `0111 rrr 0 dddddddd` | `N Z`, `V C`=0 |
| `MOVEM` | W/L | `MOVEM <list>,<ea>` · `MOVEM <ea>,<list>` | copies any chosen set of registers to or from consecutive memory words or longs | `0100 1 d 0 1 ss mmm rrr` + mask | — |
| `MOVEP` | W/L | `MOVEP d16(An),Dn` · `MOVEP Dn,d16(An)` | moves 2 or 4 bytes between `Dn` and every *second* byte of memory: the 8-bit-peripheral transfer | `0000 rrr 1 ss 001 aaa` + d16 | — |
| `LEA` | L | `LEA <ea>,An` | computes the effective address and puts it in `An`; reads no memory | `0100 rrr 111 mmm rrr` | — |
| `PEA` | L | `PEA <ea>` | computes the effective address and pushes it on the stack | `0100 1000 01 mmm rrr` | — |
| `EXG` | L | `EXG Dx,Dy` · `EXG Ax,Ay` · `EXG Dx,Ay` | swaps the whole 32-bit contents of two registers | `1100 rrr 1 01000/01001/10001 rrr` | — |
| `SWAP` | W | `SWAP Dn` | exchanges the high and low words of `Dn` | `0100 1000 0100 0 rrr` | `N Z`, `V C`=0 |
| `LINK` | — | `LINK An,#d16` | builds a stack frame: pushes `An`, makes `An` the frame pointer, then adds `d16` to `SP` | `0100 1110 0101 0 rrr` + d16 | — |
| `UNLK` | — | `UNLK An` | tears the frame down: `SP` = `An`, then pops `An` | `0100 1110 0101 1 rrr` | — |
| `MOVE SR,<ea>` | W | `MOVE SR,<ea>` | copies the whole status register out; not privileged | `0100 0000 11 mmm rrr` (`40C0`) | — |
| `MOVE <ea>,SR` | W | `MOVE <ea>,SR` | loads the whole status register: the instruction that enters and leaves supervisor mode | `0100 0110 11 mmm rrr` (`46C0`) | all |
| `MOVE <ea>,CCR` | B | `MOVE <ea>,CCR` | loads only the five condition codes, from a byte | `0100 0100 11 mmm rrr` (`44C0`) | all |
| `MOVE USP` | L | `MOVE An,USP` · `MOVE USP,An` | reads or writes the stack pointer that is *not* current | `0100 1110 0110 0/1 rrr` | — |
| `ANDI/ORI/EORI to CCR` | B | `ANDI #b,CCR` | combines a constant with the five flags: clear, set or invert them | `023C` `003C` `0A3C` + byte | per op |
| `ANDI/ORI/EORI to SR` | W | `ANDI #w,SR` | combines a constant with the whole status register; privileged | `027C` `007C` `0A7C` + word | per op |

`MOVEM` is the only instruction whose mask and address arithmetic surprise people:

```
 MOVEM.L D0-D2/A0,(A1)               MOVEM.L D0-D2/A0,-(A1)
   A1+0   D0                           A1-16  D0  ┐ written in reverse
   A1+4   D1                           A1-12  D1  │ register order, so the
   A1+8   D2                           A1-8   D2  │ layout comes out the same
   A1+12  A0                           A1-4   A0  ┘ either way
   A1 unchanged                        A1 = old A1 − 16
```

- the mask is a word: bit 0 = `D0` … bit 7 = `D7`, bit 8 = `A0` … bit 15 = `A7`, **except** with the
  pre-decrement destination, where bit 0 is `A7` and the bits run the other way;
- with `−(An)` each register is written at `An −= size` *before* the write, so the frame ends up in
  ascending order and `An` points at its base;
- loading with word size sign-extends each register to 32 bits;
- there is no `MOVEM.B`; the smallest step is a word.

### 5.2 Integer arithmetic

| Mnemonic | Sizes | Syntax | What it does | Encoding | Flags |
|---|---|---|---|---|---|
| `ADD` | B/W/L | `ADD <ea>,Dn` · `ADD Dn,<ea>` | `Dn` + source → `Dn`, or `Dn` + memory → memory (read, add, write back) | `1101 rrr d ss mmm rrr` | `X N Z V C` |
| `ADDA` | W/L | `ADDA <ea>,An` | adds to an address register; word sources sign-extend; never touches a flag | `ADD` with dst mode `001` | — |
| `ADDI` | B/W/L | `ADDI #imm,<ea>` | adds a constant to the operand and writes it back | `0000 0110 ss mmm rrr` | `X N Z V C` |
| `ADDQ` | B/W/L | `ADDQ #q,<ea>` | adds 1–8, encoded in the opcode: the cheapest way to move a pointer | `0101 qqq 0 ss mmm rrr` | `X N Z V C` |
| `ADDX` | B/W/L | `ADDX Dy,Dx` · `ADDX −(Ay),−(Ax)` | `Dx` + `Dy` + `X`: the carry-propagating add of a multi-word sum | `1101 rrr 1 ss 0 0/1 rrr` | `X N Z V C` |
| `SUB` | B/W/L | `SUB <ea>,Dn` · `SUB Dn,<ea>` | `Dn` − source → `Dn`, or memory − `Dn` → memory | `1001 rrr d ss mmm rrr` | `X N Z V C` |
| `SUBA` | W/L | `SUBA <ea>,An` | subtracts from an address register; no flags | dst mode `001` | — |
| `SUBI` | B/W/L | `SUBI #imm,<ea>` | subtracts a constant and writes back | `0000 0100 ss mmm rrr` | `X N Z V C` |
| `SUBQ` | B/W/L | `SUBQ #q,<ea>` | subtracts 1–8 | `0101 qqq 1 ss mmm rrr` | `X N Z V C` |
| `SUBX` | B/W/L | `SUBX Dy,Dx` · `SUBX −(Ay),−(Ax)` | `Dx` − `Dy` − `X`: borrow-propagating subtract | `1001 rrr 1 ss 0 0/1 rrr` | `X N Z V C` |
| `CMP` | B/W/L | `CMP <ea>,Dn` | computes `Dn` − source **and keeps only the flags**: the comparison of an `if` | `1011 rrr 0 ss mmm rrr` | `N Z V C` |
| `CMPA` | W/L | `CMPA <ea>,An` | compares an address register with a sign-extended source | dst mode `001` | `N Z V C` |
| `CMPI` | B/W/L | `CMPI #imm,<ea>` | compares the operand with a constant, storing nothing | `0000 1100 ss mmm rrr` | `N Z V C` |
| `CMPM` | B/W/L | `CMPM (Ay)+,(Ax)+` | compares two memory operands while advancing both pointers: a string compare in one instruction | `1011 rrr 1 ss 001 rrr` | `N Z V C` |
| `NEG` | B/W/L | `NEG <ea>` | replaces the operand with `0 − operand` | `0100 0100 ss mmm rrr` | `X N Z V C` |
| `NEGX` | B/W/L | `NEGX <ea>` | replaces it with `0 − operand − X`, for multi-word negation | `0100 0000 ss mmm rrr` | `X N Z V C` |
| `CLR` | B/W/L | `CLR <ea>` | writes zero into the operand | `0100 0010 ss mmm rrr` | `N`=0 `Z`=1 `V C`=0, `X` – |
| `EXT` | W/L | `EXT.W Dn` · `EXT.L Dn` | sign-extends the low byte into the word, or the low word into the long, of the same register | `0100 1000 ss 000 rrr` | `N Z`, `V C`=0 |
| `MULU` | W | `MULU <ea>,Dn` | unsigned 16×16 → 32-bit product, left in `Dn` | `1100 rrr 011 mmm rrr` | `N Z`, `V C`=0 |
| `MULS` | W | `MULS <ea>,Dn` | signed 16×16 → 32-bit product; 38–70 clocks, data-dependent | `1100 rrr 111 mmm rrr` | `N Z`, `V C`=0 |
| `DIVU` | W | `DIVU <ea>,Dn` | 32÷16 unsigned: quotient into the low word, remainder into the high word | `1000 rrr 011 mmm rrr` | `N Z` u, `V`, `C`=0 |
| `DIVS` | W | `DIVS <ea>,Dn` | the signed form; 120–158 clocks | `1000 rrr 111 mmm rrr` | as `DIVU` |
| `ABCD` | B | `ABCD Dy,Dx` · `ABCD −(Ay),−(Ax)` | adds two packed BCD bytes plus `X`, producing BCD | `1100 xxx 1 0000 0/1 rrr` | `X N Z V C` |
| `SBCD` | B | `SBCD Dy,Dx` · `SBCD −(Ay),−(Ax)` | subtracts a packed BCD byte plus `X` | `1000 xxx 1 0000 0/1 rrr` | `X N Z V C` |
| `NBCD` | B | `NBCD <ea>` | negates a packed BCD byte: `0 − operand − X` | `0100 1000 00 mmm rrr` | `X N Z V C` |

```
 where the flags come from, in one picture

    dst ──┐
          ├──▶ [ adder ] ──▶ result ──▶ N = top bit of the result
    src ──┘        │                     Z = result is all zeros
                   ├──▶ C = carry out of the top bit           (the unsigned test)
                   └──▶ V = 1 when the sign of the result is
                            wrong for the inputs: + + → −, − − → +

    ADDX/SUBX/NEGX/ABCD/SBCD also read X in, and copy C out into X
```

```
 DIVU <ea>,D0 :   D0 = $00000064 (100), divisor = 7

   before   D0 = 0000 0064
   after    D0 = 0002 000E
                 └──┬──┘ └──┬──┘
                 remainder  quotient
   if the quotient does not fit in 16 bits: V=1 and D0 is left untouched
   a zero divisor is not a flag: it raises exception 5 before D0 changes
```

Multi-precision rule: `ADDX`, `SUBX`, `NEGX`, `ABCD` and `SBCD` never *set* `Z` — they only clear it
when the result is non-zero. A chain over several words therefore leaves `Z`=1 only if every word
was zero, which is how a 64-bit comparison reports equality.

### 5.3 Logic, tests and bits

| Mnemonic | Sizes | Syntax | What it does | Encoding | Flags |
|---|---|---|---|---|---|
| `AND` | B/W/L | `AND <ea>,Dn` · `AND Dn,<ea>` | bitwise AND, into `Dn` or back into memory | `1100 rrr d ss mmm rrr` | `N Z`, `V C`=0 |
| `ANDI` | B/W/L | `ANDI #imm,<ea>` | bitwise AND with a constant, masking bits out | `0000 0010 ss mmm rrr` | `N Z`, `V C`=0 |
| `OR` | B/W/L | `OR <ea>,Dn` · `OR Dn,<ea>` | bitwise OR, into `Dn` or back into memory | `1000 rrr d ss mmm rrr` | `N Z`, `V C`=0 |
| `ORI` | B/W/L | `ORI #imm,<ea>` | bitwise OR with a constant, setting bits | `0000 0000 ss mmm rrr` | `N Z`, `V C`=0 |
| `EOR` | B/W/L | `EOR Dn,<ea>` | bitwise exclusive-OR into memory; there is no `EOR <ea>,Dn` form | `1011 rrr 1 ss mmm rrr` | `N Z`, `V C`=0 |
| `EORI` | B/W/L | `EORI #imm,<ea>` | bitwise exclusive-OR with a constant, inverting bits | `0000 1010 ss mmm rrr` | `N Z`, `V C`=0 |
| `NOT` | B/W/L | `NOT <ea>` | one's complement: every bit inverted | `0100 0110 ss mmm rrr` | `N Z`, `V C`=0 |
| `TST` | B/W/L | `TST <ea>` | compares the operand with zero and stores nothing: the cheapest way to set `N`/`Z` from a value | `0100 1010 ss mmm rrr` | `N Z`, `V C`=0 |
| `TAS` | B | `TAS <ea>` | reads the byte, sets its bit 7 and writes it back as one uninterruptible pair: test-and-set | `0100 1010 11 mmm rrr` | `N Z`, `V C`=0 |
| `BTST` | B/L | `BTST Dn,<ea>` · `BTST #n,<ea>` | tests one bit and nothing else; the only bit op that is pure read | `0000 rrr 1 00 mmm rrr` · `0000 1000 00 mmm rrr` + byte | `Z` |
| `BCHG` | B/L | `BCHG Dn,<ea>` · `BCHG #n,<ea>` | tests a bit, complements it, writes the operand back | `0000 rrr 1 01 …` · `0000 1000 01 …` | `Z` |
| `BCLR` | B/L | `BCLR Dn,<ea>` · `BCLR #n,<ea>` | tests a bit, clears it, writes back | `0000 rrr 1 10 …` · `0000 1000 10 …` | `Z` |
| `BSET` | B/L | `BSET Dn,<ea>` · `BSET #n,<ea>` | tests a bit, sets it, writes back | `0000 rrr 1 11 …` · `0000 1000 11 …` | `Z` |

```
 the operand's size decides the bit numbering

   BTST #5,D1     a 32-bit value  →  bit number mod 32   ──┐
   BTST #5,(A0)   a byte in memory →  bit number mod 8    ──┤  Z = NOT(that bit)
   BTST D0,(A0)   number taken from D0, then mod 8        ──┘
```

`Z` always reports the bit's **previous** value, complemented: `Z`=1 means the bit was 0. That is
why `BTST` + `BEQ` reads as "branch if the bit is clear", and why a `BSET` loop can test and set in
a single pass.

### 5.4 Shifts and rotates

| Mnemonic | Sizes | Syntax | What it does | Flags |
|---|---|---|---|---|
| `ASL` | B/W/L (W in memory) | `ASL #n,Dn` · `ASL Dn,Dm` · `ASL <ea>` | shifts left, zeros in at the bottom: multiply by 2. On this CPU it *is* `LSL` — the two mnemonics differ only in name | `X N Z V C` |
| `ASR` | B/W/L (W in memory) | as above | shifts right copying the top bit into the vacated positions: signed divide by 2 | `X N Z V C` |
| `LSL` | B/W/L (W in memory) | as above | shifts left, zeros in at the bottom: unsigned multiply by 2 | `X N Z V C` |
| `LSR` | B/W/L (W in memory) | as above | shifts right, zeros in at the top: unsigned divide by 2 | `X N Z V C` |
| `ROXL` | B/W/L (W in memory) | as above | rotates left *through X*: the bit leaving the top enters `X`, the old `X` enters the bottom | `X N Z V C` |
| `ROXR` | B/W/L (W in memory) | as above | rotates right through `X` | `X N Z V C` |
| `ROL` | B/W/L (W in memory) | as above | rotates left in a ring of the operand's own size; `X` untouched | `N Z V C` |
| `ROR` | B/W/L (W in memory) | as above | rotates right in a ring; `X` untouched | `N Z V C` |

```
   ASL/LSL     C ◀── bit31 ◀── … ◀── bit0 ◀── 0        X ◀── the bit shifted out
   ASR         bit31 ──▶ … ──▶ bit0 ──▶ C               bit31 copies into itself
   LSR         0 ──▶ bit31 ──▶ … ──▶ bit0 ──▶ C
   ROL/ROR     bit31 ──▶ (ring) ──▶ bit0 ──▶ bit31      X untouched
   ROXL/ROXR   X ◀── bit31 ◀── … ◀── bit0 ◀── X         a ring of size+1 bits
```

```
 encoding        1110 CCC D SS I TT RRR        memory form  1110 0 TT D 11 MMM RRR
                        │  │  │  │  │  │                     word only, count 1
                        │  │  │  │  │  └─ register
                        │  │  │  │  └─ 00 AS   01 LS   10 ROX   11 RO
                        │  │  │  └─ 0 = count in the opcode, 1 = count in Dn
                        │  │  └─ size: 01 B, 11 W, 10 L
                        │  └─ direction: 0 right, 1 left
                        └─ count 1–8, or the register holding the count
```

A count of 8 is encoded as 0, and a register count is taken modulo 64. With an effective count of
zero nothing moves: `C` is cleared, `X` is untouched, and `N Z V` still describe the operand.

### 5.5 Program control

| Mnemonic | Syntax | What it does | Encoding | Flags |
|---|---|---|---|---|
| `Bcc` | `Bcc <label>` | branches if the condition holds, otherwise continues with the next instruction | `0110 cccc dddddddd` | — |
| `BRA` | `BRA <label>` | branches unconditionally (`Bcc` with the "true" condition) | `0110 0000 …` | — |
| `BSR` | `BSR <label>` | pushes the address of the next instruction, then branches: a subroutine call | `0110 0001 …` | — |
| `DBcc` | `DBcc Dn,<label>` | if the condition is true, falls through at once; otherwise decrements `Dn`'s low word and branches back unless it reached −1: a counted loop with an early exit | `0101 cccc 11001 rrr` + d16 | — |
| `Scc` | `Scc <ea>` | writes `$FF` if the condition is true and `$00` if not: a branch-free boolean | `0101 cccc 11 mmm rrr` | — |
| `JMP` | `JMP <ea>` | continues at the address the operand gives; pushes nothing | `0100 1110 11 mmm rrr` | — |
| `JSR` | `JSR <ea>` | pushes the return address, then continues at the address the operand gives | `0100 1110 10 mmm rrr` | — |
| `RTS` | `RTS` | pops the return address into `PC` | `4E75` | — |
| `RTR` | `RTR` | pops `CCR`, then `PC`: return from a `JSR` that saved the flags | `4E77` | all |
| `RTE` | `RTE` | pops `SR`, then `PC`: return from an exception; privileged | `4E73` | all |
| `NOP` | `NOP` | does nothing for one instruction time: padding, timing sync | `4E71` | — |
| `STOP` | `STOP #imm` | loads `SR`, then stops executing until an interrupt above the new mask (or level 7) arrives | `4E72` + imm | all |
| `RESET` | `RESET` | pulses the `RESET` pin for 124 clocks to reset external devices; the CPU's own registers are untouched | `4E70` | — |
| `TRAP #n` | `TRAP #n` | raises exception `32+n` by software: a deliberate, chosen exception | `4E40+n` | — |
| `TRAPV` | `TRAPV` | raises exception 7 if `V` is set, does nothing otherwise: an overflow check | `4E76` | — |
| `CHK` | `CHK <ea>,Dn` | raises exception 6 if `Dn` is negative or above the bound, and sets `N` to say which | `0100 rrr 110 mmm rrr` | `N` |
| — | any unassigned opcode | raises exception 4: illegal instruction (conventionally written `ILLEGAL`, `$4AFC`) | — | — |

```
 a branch displacement is measured from the END of the branch instruction

   $2000 ┌──────┬──────┐
         │ 66 06│      │   BNE +6
   $2002 ├──────┴──────┤ ◀── counting starts here
         │      …      │
   $2008 └─────────────┘ ◀── target = $2002 + 6

   d8 = $FE  →  target is the branch itself (a deliberate infinite loop)
   d8 = $00  →  "the displacement is the next word" (16-bit form)
   d8 = $FF  →  "32-bit displacement": a 68020 form, illegal on this CPU
```

```
 DBcc is a do-while loop with an early exit

   ┌──────────────────┐
   │ DBcc D0,loop     │◀───────────────────┐
   └───┬──────────┬───┘                    │
       │          │                        │ back while
 true  │          │ false                  │ D0 ≠ $FFFF
       ▼          ▼                        │
  fall through   D0 = D0 − 1 (low word) ───┘
```

### 5.6 Condition codes

`cccc` is the 4-bit field shared by `Bcc`, `DBcc` and `Scc`. A condition only tests the five flags —
no comparison happens in the branch itself: `CMP` sets the flags, the branch reads them.

| `cccc` | Name | True when | Read as |
|---|---|---|---|
| `0000` | `T` | always | — |
| `0001` | `F` | never | — |
| `0010` | `HI` | `C`=0 and `Z`=0 | unsigned: above |
| `0011` | `LS` | `C`=1 or `Z`=1 | unsigned: below or same |
| `0100` | `CC`, `HS` | `C`=0 | unsigned: ≥ |
| `0101` | `CS`, `LO` | `C`=1 | unsigned: < |
| `0110` | `NE` | `Z`=0 | not equal |
| `0111` | `EQ` | `Z`=1 | equal |
| `1000` | `VC` | `V`=0 | no overflow |
| `1001` | `VS` | `V`=1 | overflow |
| `1010` | `PL` | `N`=0 | positive |
| `1011` | `MI` | `N`=1 | negative |
| `1100` | `GE` | `N`=`V` | signed: ≥ |
| `1101` | `LT` | `N`≠`V` | signed: < |
| `1110` | `GT` | `Z`=0 and `N`=`V` | signed: > |
| `1111` | `LE` | `Z`=1 or `N`≠`V` | signed: ≤ |

```
 one comparison, four conclusions              after  CMP src,dst

   dst − src  ──▶  C      borrow: dst < src seen as unsigned
               ──▶  Z      equal
               ──▶  N,V    sign and overflow

   unsigned:   above = C=0,Z=0    below = C=1        same = Z=1
   signed  :   greater = Z=0,N=V  less = N≠V         same = Z=1
```

`Scc` with `T`/`F` is "always"/"never"; `Scc` is also the branch-free way to compute a boolean
(`TST.W D0` then `SNE D1`).

---

## 6. Exceptions

An exception is a forced subroutine call: the CPU records where it was, changes mode, and jumps
through a vector. Three groups exist and they differ in what they leave on the stack:

| Group | Cause | Frame | Restartable? |
|---|---|---|---|
| 0 | reset: initial `SSP` and `PC` | **none** | — |
| 1 | trace, interrupt, `TRAP`, `TRAPV`, `CHK`, zero divide, privilege violation, illegal instruction, line A/F | 6 bytes | yes, precisely |
| 2 | bus error, address error | 14 bytes | **no** on the 68000; the 68010 fixed that |

```
 exception entry, in five steps

   ┌───────────────────────────────────────────────────┐
   │ 1  the vector number n is determined              │  instruction, IACK cycle, or internal
   │ 2  the old SR is copied; new SR: S=1, T=0,        │
   │    I = the interrupt level (else unchanged)       │
   │ 3  PUSH PC                 SP = SP − 4            │
   │ 4  PUSH SR                 SP = SP − 6            │
   │ 5  PC ← long at 4 × n                             │
   └───────────────────────┬───────────────────────────┘
                           ▼
                     handler … RTE
```

```
 group 1 — trace, interrupt, TRAP, TRAPV, CHK, zero divide, …
   6 bytes; RTE pops SR, then PC

   SP+0 ┌────────────────────┐
        │  SR          2     │
   SP+2 ├────────────────────┤
        │  PC          4     │   the next instruction: RTE lands after the trap
   SP+6 └────────────────────┘
```

```
 group 2 — bus error, address error
   14 bytes; not restartable on the 68000

   SP+0  ┌────────────────────────────┐
         │ status word (R/W, FC)  2   │
   SP+2  ├────────────────────────────┤
         │ faulting address       4   │
   SP+6  ├────────────────────────────┤
         │ instruction register   2   │
   SP+8  ├────────────────────────────┤
         │ SR                     2   │
   SP+10 ├────────────────────────────┤
         │ PC                     4   │
   SP+14 └────────────────────────────┘
```

Because `PC` is pushed first and `SR` second, the handler sees `0(SP)`=`SR` and `2(SP)`=`PC`, and
`RTE` pops them in the opposite order. A group-1 return lands on the instruction *after* the trap; a
group-2 return cannot be trusted, which is why the ST's bus-error handler reports and aborts rather
than retries.

### 6.1 The vector table

| Vector | Address | Exception | Vector | Address | Exception |
|---|---|---|---|---|---|
| 0 | `$000` | initial `SSP` | 11 | `$02C` | line F |
| 1 | `$004` | initial `PC` | 12–14 | `$030` | reserved |
| 2 | `$008` | bus error | 15 | `$03C` | uninitialised interrupt |
| 3 | `$00C` | address error | 16–23 | `$040` | reserved |
| 4 | `$010` | illegal instruction | 24 | `$060` | spurious interrupt |
| 5 | `$014` | zero divide | 25–31 | `$064` | autovectors, levels 1–7 |
| 6 | `$018` | `CHK` | 32–47 | `$080` | `TRAP #0`–`#15` |
| 7 | `$01C` | `TRAPV` | 48–63 | `$0C0` | reserved |
| 8 | `$020` | privilege violation | 64–255 | `$100` | user-defined (TOS/GEM live here) |
| 9 | `$024` | trace | | | |
| 10 | `$028` | line A | | | |

`4 × vector` is the address, so the whole table is 1 KB at address 0 — and this CPU cannot move it.

### 6.2 Which exception wins

- **Trace beats a pending interrupt** at the end of an instruction.
- Trace is *not* taken for the instruction that itself raised an exception.
- An address error beats the instruction's own effect: it is raised when the access is attempted, so
  the instruction never completes.
- A second bus error while entering the first one's handler is a **double fault**: the CPU halts and
  nothing but `RESET` brings it back.

---

## 7. Interrupts

An interrupt is an exception requested from outside through `IPL2–IPL0`, which carry the level
active low. Level 7 is special: non-maskable and edge-triggered — the request is recognised on the
*transition* into 7, so a level held at 7 does not re-interrupt.

```
        ┌───────────────────────────────────────────────────────────┐
  IPL ─▶│ level L on IPL2–IPL0   (000 = 7, 001 = 6 … 111 = 0/none)  │
        └────────────────────┬──────────────────────────────────────┘
                             ▼
                 L > I2–I0 ?  or  L = 7 ?        no ──▶ ignored at this boundary
                             │ yes
                             ▼
   ┌───────────────────────────────────────────────────────────────┐
   │ IACK cycle: FC=111, A23–A4 = 1s, A3–A1 = L, read              │
   │    device answers   DTACK + vector on D7–D0  → that vector    │
   │                     VPA                      → vector 24 + L  │
   │                     BERR                     → vector 24      │
   └────────────────────┬──────────────────────────────────────────┘
                       ▼
      SR: I := L, S=1, T=0 · PUSH PC · PUSH SR · PC ← 4 × vector
```

| Case | Condition | What happens |
|---|---|---|
| masked | level ≤ `I2–I0` | not taken now; it stays pending and is taken when the mask drops |
| taken | level > mask, or level 7 | processed at the next instruction boundary |
| edge-triggered | level 7 | one interrupt per transition into 7, however long it is held |
| autovector | board asserts `VPA` | vector `24 + L`, no data read: `$64`…`$7C` |
| spurious | `BERR` during IACK | vector 24 (`$60`): a device raised the level and then did not answer |

Latency is the rest of the current instruction plus the acknowledge cycle plus the entry sequence:
the CPU never abandons an instruction halfway.

On this machine the MFP raises level 6 **and supplies its own vector** through `DTACK`, while VBL
(level 4, `$70`) and HBL (level 2, `$68`) are autovectored. The level-6 autovector `$78` therefore
never sees an MFP interrupt — see [../bus/02-interrupts.md](../bus/02-interrupts.md).

---

## 8. The bus

### 8.1 The signals

| Group | Lines | Meaning |
|---|---|---|
| address | `A1–A23` | word address; there is **no `A0`** — the byte lane comes from the strobes |
| data | `D0–D15` | `D15–D8` = even byte, `D7–D0` = odd byte |
| strobes | `AS`, `UDS`, `LDS` | address valid, upper byte, lower byte (all active low) |
| control | `R/W`, `DTACK`, `BERR`, `HALT`, `RESET`, `CLK` | direction, done, fault, stop, reset, time |
| space | `FC2–FC0` | `001` user data, `010` user program, `101` supervisor data, `110` supervisor program, `111` CPU space |
| interrupt | `IPL2–IPL0` | the level, active low |
| arbitration | `BR`, `BG`, `BGACK` | request, grant, grant acknowledge |
| 6800 | `E`, `VPA`, `VMA` | peripheral clock and handshake (unused by the ST) |

All three function codes are driven, so a slave can tell a program fetch from a data access and a
supervisor access from a user one; the ST uses that mainly to recognise the `FC`=111 IACK cycle.

### 8.2 One bus cycle

Four clock periods, eight states, two per clock:

```
           ┌────────────────────────────────┐
 A/FC/RW   ·   ████████████████████████████    valid from S1 to S0 of the next cycle
 AS        ·   ·    ███████████████████████    asserted on the rising edge of S2
 UDS/LDS   ·   ·    ███████████████████████    same edge, for the bytes in play
 DTACK     ·   ·    ?    ?    ?    ·    ·      sampled on each falling edge in S3–S5
 data      ·   ·    ·    ·    ·    █████████    read: device drives, latched at end of S6
           └────────────────────────────────┘
             S0 S1  S2 S3  S4 S5  S6 S7        two states per clock period
                                                 wait states are inserted inside S3–S5
```

| State | What happens |
|---|---|
| `S0` | bus idle; address high-impedance; the previous cycle ends |
| `S1` | address, `FC` and `R/W` driven and valid |
| `S2` | `AS` and the data strobes asserted, on the rising edge |
| `S3–S5` | the CPU waits here; every clock `DTACK` is late adds a whole clock |
| `S6` | the transfer: data latched (read) or driven (write) |
| `S7` | strobes negated, cycle over |

A slave may stretch a cycle by any number of whole clocks: that is how the ST's GLUE and MMU serve
slow ROM, and how an absent device becomes a bus error instead of a hang.

### 8.3 Sequences worth modelling exactly

```
 a long access is two word cycles, high word first

   MOVE.L D0,$1000     cycle 1: address $1000, D31–D16
                       cycle 2: address $1002, D15–D0
```

```
 a read-modify-write holds the bus across the pair

   ADD.W D0,(A1)       cycle 1   read  (A1) ──▶ [ ALU ] ──┐
                       cycle 2   write (A1) ◀─────────────┘    AS never released in between
```

| Instruction | Bus behaviour |
|---|---|
| `TAS` | read, then write, with **one extra dead clock** before the write; still one uninterruptible pair |
| `CLR` | **reads before it writes**, so it faults on absent memory like any read |
| `MOVEM` | one cycle per register, no gap, in the order of §5.1 |
| `MOVEP` | one byte cycle per byte, on every second address |
| `BSET/BCLR/BCHG`, ALU to memory | RMW: read, compute, write the same address |
| interrupt acknowledge | one CPU-space cycle, `FC`=111, no data written |
| `RESET` | the `RESET` pin held for 124 clocks |

### 8.4 Arbitration, HALT and BERR

```
  CPU owns the bus          a device asks            hand-over                    back
  ────────────────          ─────────────            ─────────                    ────
  driving A/D/AS        ◀── BR asserted ──▶  finishes the current bus cycle,      BGACK
                                             asserts BG, three-states the  ─────▶  negated
                                             bus when BGACK is seen                 CPU resumes
```

- `BR` is sampled every cycle; `BG` is granted at the end of the current bus cycle, not immediately.
- `HALT` alone: the CPU finishes the current cycle, releases the bus and waits; on release it
  **reruns** the interrupted cycle.
- `HALT` with `BERR`: rerun mode — the faulting cycle is retried rather than turned into an
  exception (how a slow or demand-paged device stalls transparently).
- A bus error while an exception is being processed is a double fault (§6.2).
- `RESET` and `HALT` together reset the machine and leave the CPU parked until `HALT` is released.

---

## 9. Timing

One clock is 125 ns at 8 MHz; the minimum bus cycle is 4 clocks (500 ns); a long access is two
cycles (1 µs). A wait state adds 125 ns.

The verified effective-address times — clocks, with bus cycles as `r/w` — are the basis of every
other figure, because an instruction's time is these bus cycles plus a small fixed cost:

| Mode | Byte/Word | Long |
|---|---|---|
| `Dn`, `An` | 0 | 0 |
| `(An)` | 4 (1/0) | 8 (2/0) |
| `(An)+` | 4 (1/0) | 8 (2/0) |
| `−(An)` | 6 (1/0) | 10 (2/0) |
| `d16(An)` | 8 (2/0) | 12 (3/0) |
| `d8(An,Xn)` | 10 (2/0) | 14 (3/0) |
| `(xxx).W` | 8 (2/0) | 12 (3/0) |
| `(xxx).L` | 12 (3/0) | 16 (4/0) |
| `d16(PC)` | 8 (2/0) | 12 (3/0) |
| `d8(PC,Xn)` | 10 (2/0) | 14 (3/0) |
| `#imm` | 4 (1/0) | 8 (2/0) |

The index register's size does not change the time. Building on the table:

- register-to-register ALU work is 4 clocks (byte/word) or 8 (long);
- each word moved to or from memory adds 4 clocks, each long 8 — hence `MOVE.W Dn,(An)` = 8 and
  `MOVE.L Dn,(An)` = 12;
- the write half of a read-modify-write adds its own 4 or 8;
- `MULU`/`MULS` are 38–70 clocks and `DIVU`/`DIVS` 76–140 / 120–158, all data-dependent: they cannot
  be constants in a cycle-exact core;
- exception entry adds the two stacked words and the vector fetch; interrupt entry adds the
  acknowledge cycle before that.

```
 cost of one instruction  ≈  fixed cost  +  Σ (EA times)  +  Σ (write cycles)

   MOVE.W D0,(A1)     4  +  0 (Dn)                +  4 (write)      =  8 clocks
   MOVE.W (A1),D0     4  +  4 (EA read)                              =  8
   ADD.W  D0,$1234    4  +  8 (abs.W EA read)     +  4 (write back)  = 16
```

Per-instruction totals are tabulated in the M68000 Programmer's Reference Manual, Appendix D; this
table plus these rules reproduces its MOVE and ALU figures, which is the cross-check to run before
any of them becomes a test golden.

---

## 10. The opcode map

The top nibble of the opcode word selects the family, so a decoder starts there. Read the grid as one
4-bit index: rows are `0`, `4`, `8`, `C`; columns add `0`–`3`.

```
            +0          +1          +2          +3
      ┌───────────┬───────────┬───────────┬───────────┐
   $0 │ imm, bit, │ MOVE.B    │ MOVE.L    │ MOVE.W    │
      │ MOVEP     │           │           │           │
      ├───────────┼───────────┼───────────┼───────────┤
   $4 │ misc:     │ ADDQ,SUBQ │ Bcc       │ MOVEQ     │
      │ NEGX…TAS  │ Scc, DBcc │ BRA, BSR  │           │
      │ TRAP,JMP… │           │           │           │
      ├───────────┼───────────┼───────────┼───────────┤
   $8 │ OR, DIVU, │ SUB, SUBA,│ line A    │ CMP, CMPA │
      │ DIVS,SBCD │ SUBX      │ ($Axxx)   │ CMPM, EOR │
      ├───────────┼───────────┼───────────┼───────────┤
   $C │ AND, MULU │ ADD, ADDA,│ shifts    │ line F    │
      │ MULS,ABCD │ ADDX      │ rotates   │ ($Fxxx)   │
      │ EXG       │           │           │           │
      └───────────┴───────────┴───────────┴───────────┘
```

Line A (`$Axxx`) and line F (`$Fxxx`) are not illegal instructions: they raise exceptions 10 and 11,
which is how TOS and GEM get their trap calls, and why a core must raise them rather than treat them
as "unknown".

---

## 11. Reset

```
   RESET held low for at least 10 clocks
              │
              ▼
   ┌──────────────────────────────────────────┐
   │ S=1, T=0, I=7   (SR reads back as $2700) │
   │ SSP ← long at $000000                    │
   │ PC  ← long at $000004                    │
   │ no stack frame is pushed                 │
   └───────────────────┬──────────────────────┘
                       ▼
              first instruction fetch at PC
```

No other register is defined by reset; software must not assume zeros. The `RESET` **instruction**
is unrelated: it pulses the pin for 124 clocks to reset peripherals and leaves the CPU's registers
alone.

---

## 12. What this CPU does not have

Everything below belongs to a later member of the family and must **trap as illegal** (vector 4) on a
68000 — not behave differently, not be silently ignored.

| Missing | Introduced by | Consequence on the 68000 |
|---|---|---|
| `VBR`, `SFC`, `DFC` | 68010 | vector table fixed at 0; `MOVE` to/from them is illegal |
| `MOVEC`, `RTD` (`4E74`), `BKPT` | 68010 | illegal |
| `MOVE CCR,<ea>` (`42C0`) | 68010 | illegal; only `MOVE <ea>,CCR` exists here |
| precise, restartable bus-error frames | 68010 | the 68000 pushes the 14-byte frame of §6 |
| `EXTB.L`, `MULS.L`, `DIVS.L`, `CHK2`, `CMP2`, bit-field ops, `PACK`/`UNPK` | 68020 | illegal |
| 32-bit displacements, memory-indirect modes, scaled indexes | 68020 | illegal; `d8 = $FF` in a branch is not a long branch here |
| `CACR`/`CINV` (cache), `PMOVE`/`PTEST` (MMU), `FMOVE`… (FPU) | 68020/68881 | illegal; the STe's blitter is a memory-mapped device, not a coprocessor |

The practical reading: a game or demo that probes for a 68010 or 68020 feature must see an
illegal-instruction trap, and TOS must not be able to tell this CPU from a 68000.

---

## Appendix: the whole set at a glance

```
MOVE   MOVEA  MOVEQ  MOVEM  MOVEP   LEA  PEA  EXG  SWAP  LINK  UNLK
MOVE SR,<ea>   MOVE <ea>,SR   MOVE <ea>,CCR   MOVE USP   ANDI/ORI/EORI to CCR,SR
ADD  ADDA  ADDI  ADDQ  ADDX        SUB  SUBA  SUBI  SUBQ  SUBX
CMP  CMPA  CMPI  CMPM              NEG  NEGX  CLR  EXT
MULU  MULS  DIVU  DIVS  ABCD  SBCD  NBCD
AND  ANDI  OR  ORI  EOR  EORI  NOT  TST  TAS
ASL  ASR  LSL  LSR  ROL  ROR  ROXL  ROXR        (register and memory forms)
BTST  BCHG  BCLR  BSET
Bcc  BRA  BSR  DBcc  Scc  JMP  JSR  RTS  RTR  RTE
TRAP  TRAPV  CHK  NOP  STOP  RESET
```

The complete 68000 set, twelve addressing modes, one 16-bit opcode word: everything the ST's
software can ask its CPU to do, and everything a core must implement to be called a 68000.
