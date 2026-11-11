# The stores are data structures, not chips: `Ram`, `Rom`, and the System Control Unit

The DRAM and the TOS ROMs leave the virtual chip that owned them. They become two plain C# data
structures — `Ram` and `Rom` — whose interface is the pins of the parts (bank, row, column, byte
lane) and nothing else: no cycle cost, no latch state, no strobe sequence, no record, no
subscription, no wake. What remains of the chip is its bus, its timing and its beam, and it is named
the **System Control Unit**.

**Status**: accepted (2026-11-11), on the owner's word — *"ok with adr 0014"* — after reading the
text. Ticket: [#31](https://github.com/laurentprudhon/the-st/issues/31). Shape:
[`design/system-control-unit.md`](../design/system-control-unit.md) §1–§2, sketch
[`design/stores-sketch.cs`](../design/stores-sketch.cs).

## Context

- **The chip owned three unlike things.** `design/system-control-unit.md` §1 listed, as one member set: the 16
  DRAMs and their mux, the DRAM controller and its decode, the refresh counter, the video fetch
  stream, the sound fetch stream, sync generation, the chip-select decode, `DTACK`/`BERR` and (from
  #20 and #28) the `IPL` arbitration. Only the first block is *storage*. Everything else is a
  timeline — a phase counter, a beam, a decode and an arbitration — and those cannot leave.
- **The project's own rules already said so.** ADR 0008 §5: *a chip needs a wake only where it
  meets another chip*. `GLOSSARY.md` "Virtual chip": *"the smallest thing that can make progress on
  its own"*. A store makes no progress. `design/partition-audit.md` §3 Finding 1 reached this for
  the ROMs and stopped one step short, recommending ROM → the chip *because* "a ninth chip called
  'ROM' would be a chip whose only function is to return the bytes at an address, which is a data
  structure, not a subsystem". This ADR finishes that argument for both stores.
- **The split is cheap for the machine** (the cheapest fact in the ticket): no pin of the DRAMs or
  the ROMs ever crosses to another virtual chip. `MAD0–MAD9` (`U400.110–119`), `RAS0/1`,
  `CAS0H/L`, `CAS1H/L`, `WE` and `ROM0–ROM6` all stay inside `U400`'s decode and the sockets beside
  it (`design/board-inventory.md` §4.2). No `design/crossings.md` row names them, so no rate, no
  record and no instant moves.
- **The owner's constraint, verbatim**: *"the C# interface of the ram and rom data structures should
  reflect as much as possible the real pins (row / columns) but not the timing, all folded in a
  single call."* And, on why the name matters: *"GSTMCU is too cryptic when you want to understand
  the machine, what about keeping only three letters of the name : SystemControlUnit ?"*
- **`Memory` was a nickname, not a part.** The board has `U400`, 16 DRAMs per bank and two 27C010s;
  it has no "Memory". Once the stores left, the nickname named the thing that had left and not the
  chip that stayed — which is what made the rename part of this decision rather than a tidy-up.
- **A cost to record either way.** A store behind plain calls makes ADR 0005's memory leaf cheaper
  to *reach for*, and the owner has ruled the leaf stays a last resort (*"it would really cripple
  the cpu and memory coroutines logic and readability"*). §"Decision 6" states why it does not
  change the leaf: the leaf inlines the slot wait, not the store's read.

## Decision

### 1. Two stores, inside the chip, with no timeline

`Ram` and `Rom` are members of the System Control Unit's *state*, not of its set of chips: they own
no clock, subscribe to nothing, are woken by nothing, and every instant at which they are touched is
the chip's own slot grid. They are not virtual chips — the machine still has eight — and no
`crossings.md` row, rate or record changes.

### 2. The interface is the pins, one call in and one value out

```csharp
public enum Lane : byte { High = 0, Low = 1 }        // CASnH / CASnL — and U102 / U103

public sealed class Ram
{
    public ushort Read (int bank, int row, int column);
    public void   Write(int bank, int row, int column, ushort word);
    public byte   ReadByte (int bank, int row, int column, Lane lane);
    public void   WriteByte(int bank, int row, int column, Lane lane, byte value);
    public void   Refresh(int row);
    public Span<byte> MachineBytes { get; }
}

public sealed class Rom
{
    public ushort Read(int address);
    public byte   ReadByte(int address, Lane lane);
    public Span<byte> MachineBytes { get; }
}
```

- **The parameters are the pins.** `RAS0`/`RAS1` select the bank; `MAD0–MAD9` carry the row and then
  the column; `CASnH`/`CASnL` pick the byte lane; `WE` picks the direction. Nothing else is a
  parameter.
- **The controller computes row and column.** It owns `MAD0–MAD9` and the DRAM address latch; the
  store receives two indices and performs no address arithmetic. The pins are on the socket, the
  sequence is the decode's — and the boundary is exactly there.
- **The byte lanes are explicit because they are real strobes** (`CAS0L`/`CAS0H`) and real parts
  (`U102` high byte, `U103` low byte). A one-byte CPU write must not disturb the other lane, so the
  lane is a parameter rather than a mask the store applies on a whole word.
- **`Rom` is its own type, not a `Ram` with a read-only flag.** Its part has `A0–A16`, flat, with no
  multiplexing, no strobes and no `WE`; a shared type would give it a `column` parameter that does
  not exist and a runtime flag where the hardware has two different parts.
- **No timing crosses it.** No cycle cost, no latch state, no strobe sequence, no record, no
  subscription. A read is a value; the instant it was read at is the controller's, computed before
  the call and unaffected by it.

### 3. The population is fixed: two banks, 512 × 512, 1 MB

| | Parts | Wiring | Capacity |
|---|---|---|---|
| Bank 0 | 16 × `C0101743-002` (41256-class, 256K×1) | `RAS0`, `CAS0L`/`CAS0H`, 512 rows × 512 columns × 16 bits | 512 KB |
| Bank 1 | 16 × `C0101743-002` | `RAS1`, `CAS1L`/`CAS1H` | 512 KB |
| Total | 32 parts, four SIMM sockets `U600–U603` | | **1 MB** |

`design/board-inventory.md` §2.3's "16 × … (1040STE = 2 banks populated)" is the count **per bank**;
16 × 256K×1 = 512 KB per bank is what makes its arithmetic and the machine's 1 MB agree. The
population is a constant, not a parameter: one machine, no switches (the map's standing preference).

**Refresh is one row, not one row per bank.** One 512-row counter, one row address strobed on both
`RAS` lines — the only reading under which 7.8 µs per row (512 rows ÷ 4 ms) and ≈8.2 slots per PAL
line are both true; a per-bank refresh would halve the period to 3.9 µs and double the slot budget.
Hence `Refresh(int row)` and not `Refresh(int bank, int row)`. `derived` from ADR 0011 §5's figures.

### 4. The host's view: `MachineBytes`, machine byte order, written only while stopped

- **Zero-copy and exact**: a `Span<byte>` over the store in **machine byte order** (big-endian
  pairs, index `2n` the high byte of word `n`). That is why the backing array is a `byte[]` and the
  word accessors assemble from two bytes: a raw span over a `ushort[]` would show every word
  byte-swapped on a little-endian host and the debugger would lie.
- **Host-only, which keeps ADR 0005's invariant 1 intact.** "No chip has a private handle on RAM"
  survives because the host is not a chip: a virtual chip's only route to memory is still its pins,
  and no chip type may hold this span.
- **No record, no instant.** A host write has no bus cycle behind it, so it appends nothing and
  takes no machine time. **The host writes only while the machine is stopped** — before instant 0
  (provisioning, fixtures, side-loading) or between two scheduler steps (a debugger poke, then
  resume). One rule, and determinism survives it without a new record kind.
- `Rom.MachineBytes` is the provisioning path (ADR 0010 loads the shipped image into the store);
  the chip-facing ROM stays read-only.

### 5. The name: the System Control Unit — and what "memory" now means

The chip is the **System Control Unit** (`SystemControlUnit` in code). The partition names its
virtual chips for roles and their parts in a second column — CPU, Video, Sound, Disk, Timers,
Controllers, Blitter — and "Memory" was a role name that stopped describing the role. "System
control" is the board's own description of this function (`atari-st-hardware/components/03-glue-mmu-dma.md`
§153: Glue, "System control logic"), and the part it is stays in the second column (`U400`, 144-pin).

The vocabulary rule that comes with it: **"memory" names the resource — the RAM and the ROM as
storage — and never the chip.** That is what keeps `Memory service` and `Memory leaf` legible without
re-importing the old name, and it is recorded in `GLOSSARY.md`. The rename itself is a vocabulary
sweep, not a decision of this ADR; it is recorded here only because the split is what made the old
name false.

### 6. What the split does not touch

- **The slot grid, the owner equation and refresh scheduling** stay the chip's (ADR 0011). Refresh
  remains filler, never a preemptor, and never a deadline: `Ram.Refresh(row)` is the store's half of
  it — the chosen row — and nothing more.
- **The beam**: `DE`, `BLANK`, `HSYNC`, `VSYNC` and `DCYC` stay, because `U400` drives them.
- **The `IPL` arbitration** stays, because `U400` drives `IPL0–IPL2` (#28, ADR 0013 §2).
- **The 4-word video fetch buffer** stays. It is a timing structure, not storage: filled one word per
  internal slot, drained as one group. Moving it into `Ram` would move a delivery instant into a
  store that has none.
- **The five-chip `nextDeadline` list** stays five; the chip's list is still the three that meet
  another chip.
- **ADR 0005's uniformity and its leaf** stand. The stores are *inside* one chip, so a call to them is
  not a private calling convention between chips (invariant 1 untouched). The leaf inlines the slot
  wait and not merely the store's read, so the split neither crosses the leaf's line nor makes it
  cheaper to justify.

## Invariants

1. A store has no record, no subscription, no anchor and no wake; it cannot make progress on its own.
2. Every store access carries no timing: no cycle cost, no latch state, no strobe sequence.
3. `Ram` and `Rom` are reachable from the System Control Unit's own code and from the host layer;
   never from another virtual chip (`MachineBytes` included).
4. Row and column are computed by the controller; the store performs no address arithmetic.
5. Nothing crosses out of a store, because nothing crosses into one from another chip.
6. The stores add no parameter to the machine: one population, no size switch, no board revision.

## Consequences

- **#10** implements against these names: `Ram`/`Rom` as above, the System Control Unit for the chip,
  and `MachineBytes` for the debugger and the loader. Its measurement is unchanged (the 40 % bill of
  ADR 0005 is CPU bus traffic, not store calls) and its digest must be identical to a run in which
  the store calls are counted and violations asserted.
- **#26 and #27** inherit the same boundary from the other side: the fetch streams stay in the chip,
  the fetched words come out of `Ram`, and the Shifter still receives groups, not bytes.
- **#29 and #30** are unaffected: a bus master reaches memory through the external slot and the
  chip's decode, exactly as before.
- **`design/system-control-unit.md`** is the implementable shape (renamed from `design/memory.md`,
  §1/§2/§6 rewritten); `design/stores-sketch.cs` carries the sketch.
- **The sweep** — six ADRs, four design docs, the glossary, the partition audit, the map's decision
  lines and the closed tickets' comments — is one dated vocabulary change, in one commit, with
  `> ### Correction` blocks rather than silent rewrites.
- **A cost, recorded**: a store behind plain calls is easy to reach for, and the leaf must stay a
  last resort. The mitigation is structural (the leaf's seven conditions in ADR 0008 §7 are about the
  slot wait) and not a promise.

## Considered options

- **Keep the stores inside the chip as members with pins** (the status quo). Rejected: the pins never
  cross anything, so modelling them as chip pins is modelling an interface nobody uses, and it keeps
  a data structure in the list of things that own a timeline — the list #10 has to read.
- **A ninth virtual chip called RAM (or ROM).** Rejected by `design/partition-audit.md` Finding 1's
  own argument: a chip whose only function is to return the bytes at an address is a data structure,
  and it would need a wake it cannot have.
- **One `IStore` type with an `IsReadOnly` flag and a shared `Address(bank, row, column)` struct.**
  Rejected: it gives the ROM three fields where its part has one, and a runtime flag where the
  hardware has two different parts.
- **Flat addresses (`Read(int wordAddress)`).** Rejected by the owner's constraint: it is the one
  reading that throws away the row/column pins the interface exists to reflect, and it hides that a
  DRAM access is two strobes over a multiplexed bus.
- **A `Ram` whose `Read` appends a record, or whose `Refresh` is a scheduled instant.** Rejected: the
  first double-counts the CPU's own bus cycle and changes the digest by construction; the second
  re-opens ADR 0008's revision of 2026-11-09 (refresh is not a deadline).
- **The host as a second writer during a run, with a `HostWrite` record.** Rejected in favour of the
  stopped-machine rule: it puts the host into the machine's record stream to buy a capability the
  debugger does not need.
- **Keeping the name `Memory`.** Rejected: after the split it names the stores, which no longer
  belong to the chip, and `GSTMCU` was rejected as cryptic by the owner — the residue is a system
  controller, and "system control" is the board's own word for it.

## Falsifier

1. **Mechanism.** After #10, the digest stream must be **byte-identical** to a run in which every
   store call is counted and every rule above is asserted — no new record, no moved instant. A single
   differing record means the split moved an instant.
2. **Boundary.** `grep` over the solution for `Ram`/`Rom`/`MachineBytes` references outside the
   System Control Unit's own code and the host layer must return **0** lines. A single hit means a
   chip found a private handle and the store became a chip by the back door.
3. **Cost.** If the walking skeleton cannot hold **50 Hz with 2× headroom** on the reference host with
   the CPU bus traffic behind these calls, the response is ADR 0005's leaf (which inlines the slot
   wait) — never a different store boundary.
4. **Nothing.** The 512-row refresh figure, the bank count and the part geometry are inventory facts:
   correcting one changes a constant and §2's table, not this decision. A second machine or a memory
   upgrade would re-open the "one population" constant only, and would be a new effort by the map's
   own scope rule.
