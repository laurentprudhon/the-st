# Rules the corpus settled that a manual reader gets wrong

One line each; the full symptom → cause → fix is in `FINDINGS.md` (rows 39–55). Most of these are silent in
the boot and only the corpus reports them, and they are the ones a fresh session re-derives by hand.

- **`ADDX`/`SUBX`: `Z` is only ever *cleared*, never set** (so a chain reports the whole result), **`V` ignores
the extend bit** (the operands' signs decide it), and the carry needs a **wider width** than the operand —
`0xFF + 0x4A + X` carries out of a byte, and masking the source before the add loses exactly that carry.
`NEGX` shares the Z rule (it is a subtract from zero).
- **A shift's `C` and `X` are *zero* once the count exceeds the operand's width** — the last bit out is decided
by the width, not by a loop that keeps running (the sign bit keeps "shifting out" in a naive loop). And
**`N`/`Z` are read off the result even at count zero**, which the manual calls unaffected. `ROX` at count zero
leaves `X` and takes `C` from it.
- **`CMPI` is a comparison: it leaves `X` alone** (`CMPM` too, and `CMPM.b` steps a byte by one except `A7`).
- **A divide's overflow touches `V` and *clears* `C`**, leaves `N`/`Z`/`X` and the register as they were.
- **`MOVE` sets `N`,`Z`,`V`,`C` from the value it is moving** (this core's model, corpus as oracle) — and it
sets them **before** the store, so a store that faults on an odd address still leaves them set.
- **`BTST Dn,#data` is a byte test**: fetch the extension word (it is the operand) and test its low byte, bits
0–7.
- **The exception frame's saved `SR` is masked to the defined bits (`0xA71F`)** — the corpus compares raw frame
bytes, and bits 7–5 read back zero.
- **A group-0 frame's first word is `SSW`, and its access class is the last *operand* access's, not the
fetch's.** `SSW` is a latch (`_ssw`) written by one composition and read by the frame builder. Latching it per
bus cycle — the die's own write site — costs **50 752 records**, because an exception that is not a bus fault
(`TRAP`, privilege, address error) keeps the *data* class of the last operand access, where the fetch that
followed the opcode would say program. So the write site stays the frame, and moving it to the microinstruction
needs that number in hand, not the schema alone.
- **The two other exception latches are cheap and already settled, so do not re-litigate them.** `INL` is the
pending level **sampled at the boundary** (the part holds `IPL0–IPL2` continuously, but the boundary is the only
instant any consumer here can see) and both sampling sites — the interrupt sampler and the `STOP` wait's second
sample — read that one latch. `TPEND` is trace-pending latched from `PSW.T` at the boundary and consumed by the
**trace exception (vector 9)**, which the boundary raises before any interrupt. Neither moved a fold or a
record: EmuTOS never sets `T`, so a correct-but-unexercised path is proven latent by the folds staying put,
not by a corpus count.
- **`JSR` to an odd target pushes nothing**: the fault belongs to the prefetch that follows the jump. Hand the
odd PC to the existing post-instruction odd-PC check rather than raising the frame inside `JSR`.
- **`(xxx).l` keeps its 32 bits.** The bus truncates at the wire; the PC a `JMP`/`JSR` stores is the whole value.
- **`-(An)` on a long**: a *read* steps down 4 and faults at `A − 4`; an `ADDX`-style word-at-a-time read and a
plain `MOVE`'s store both step down 2 and fault at `A − 2`. Three rules, three call sites — do not unify them.
