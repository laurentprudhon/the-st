# Loops that count in the address adder (`MUL`/`DIV`/`RESET`)

The die has no multiplier and no divider: those families are microcode loops whose length is a function
of the data, and **the counter is the address adder's register** — `m_au = ext32(m_ftu)` loads a constant
that the constants ROM put in the field register, the branch `auz` tests the adder's *six low bits*, and the
step is `m_au - 1`. Freeing the adder for that is the whole reason a core splits its program counter into a
separate cell: counting in a cell that also holds the PC destroys the next instruction's address.

## The recipe, per family

1. **The constant**: `const → FTU` then `AU ← ext32(FTU)`. A multiply starts at the constant itself; a divide
   starts at `ftu + 1` (sixteen iterations, sixteen quotient bits). Never the literal in the C#.
2. **The exit test**: the six low bits (`(au & 0x3f) == 0`), a named branch variable — six and not sixteen
   because the counter is shaped like a small address.
3. **The step**: a *unit* decrement, distinct from any word-stepping helper (`AuPlus(n)` adds `2n`).
4. **The restore**: the adder must be given back before the instruction closes, and *which* value restores
   it depends on **where that family's trailing fetch sits**:
   - **fetch before the loop** → the PC register still holds the consumed position + 2, so restore from it;
   - **fetch after the loop** → the PC register is the *previous* instruction's, and restoring from it costs
     real records (the divide measured 649 of 2 500). Latch the position before spending the adder
     (`AT ← AU`, the die's `m_at = m_au`) and restore from the latch (`AU ← AT`). The die latches AT and
     copies it into `m_pc` *inside* its loop for exactly this reason.
5. **The witness is the clock column**, not the state column: the loop's states, idles and the count are
   unchanged, so every record of the family must stay exact-clock. A loop that gains or loses an iteration
   shows as clocks, never as a wrong register value.

## Where a family refuses the conversion

A divide whose epilogue decrements the counter once more and then jumps to tails shared with other
instructions has no obvious restore point: the counter is live past the loop, and the restore has to be
placed with the tail in hand. Land the families whose restore you can justify and **name the ones you
reverted, with the number** — a half-converted goto-maze is the shape the campaign's rule exists to prevent.

**`DIVS` is that case worked out, and it has *two* leaving paths.** `dvumz` (the loop's overflow branch,
`dvs08`'s no-borrow) leaves **before any decrement** and restores from the latch itself; `dvs14` is the
first state after the loop's *last* decrement (`dvs12`'s `m_au = m_au - 1` is dead — nothing reads the
counter again), and every tail (`dvuma`, `dvum4`, `leaa2`) is reached from it, so one `AuEqualsAt()` there
covers three exits. Latch with `AtEqualsAu()` where the die does (`dvs02`'s `m_pc = m_au`): the family's
trailing fetch is *after* the loop, so `_pc` is not the consumed position.

## `RESET` is a countdown, not a family — and `CHK` has neither

`RESET` (`0x4E70`) holds the pin for as long as a loop on the adder takes: `rset2` does
`m_au = ext32(m_aluo) - 2` off the constants ROM's zero, `rset3` steps it by −2 and `rset4` branches on
`!(m_au & 0x3f)` — the same `auz` test, a different step. Thirty-one passes take `0xFFFFFFFE` to
`0xFFFFFFC0`, so the hold is `4 + 4 × passes` and the shape table's flat 128 can go. `CHK` is four states
(`chkr1`, `chkr2`, then `chkr3` or `trap1`) with **no counter**: its adder traffic is a net-zero transfer
pair (`chkr1`'s −2 against `chkr2`'s `m_pc = m_au; m_au = m_au + 2` that builds the trap's frame counter),
which the frames already take from `_pc`. Both of CHK's paths are in MAME's records (1 447 vector 6, 1 053
vector 3), so its reading is measured rather than latent.

## The trap: a table-charged family's clocks cannot be split into states

`MUL`/`DIV` were already scripted loops, so expressing the counter changed no clock shape. `RESET` was
**table-charged** (one `ClocksAsync(_own)` of 128), and turning that into its 62 micro-states moves
`run.digest` (`88c7144f…`) while **`run.stream` stays byte-identical**, the desktop and the pops untouched:
separating states that no bus access divides splits one engine instant into N, and the identity fold counts
batches, empty or not (#39's class). The pair-preserving form is one wait — `ClocksAsync(4 + 4 × passes)` —
which is what the table did, so the *count* can be the loop's while the states stay fused; the separation is
a re-bless, not a slice. Two facts that came with it: `TrailingFetchAsync` writes `_at`, so a restore latch
taken with `AtEqualsAu()` must come **after** the instruction's read; and the shape table's *placement* of
that read (inside the hold) is not the die's (`rset5`, after it) — moving it moves **both** folds
(`1333d164…` / `0085e730…`, desktop 602 and 34 122.2 pops a frame unchanged).

## Reading the live loop before changing it

`scripts/microcode-walk.py --op <opcode>` prints the family's states in order; the loop's entry state shows the
`m_au = ext32(m_ftu)` load, its branch state the `!(m_au & 0x3f)` test, and — for a divide — a
`set_16l(m_pc, m_at)` inside the body, which is the die telling you the position is kept in AT while the adder
counts.
