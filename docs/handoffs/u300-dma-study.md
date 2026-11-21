# Handoff — U300, the DMA controller (C398739-001)

**STATUS — executed 2026-11-21.** `research/dma-controller-implementation.md` is written, `verify
--min-coverage 0.30` is green (35 %), the six acceptance criteria below are met (§2 ticked), the per-chip index
entry is **done**, and the corrections this session earned are dated in `design/disk-path.md` §3/§4,
`design/crossings.md` §4/§5, `design/board-inventory.md` §3.6/§3.7 and in both neighbouring studies. The
brief's own two wrong bullets are corrected in place below (`FDINT`/`HDINT` do not come from U300; `RDY` is a
level per burst, not per sector).

**Goal of the next session.** Write `research/dma-controller-implementation.md`: the STE's DMA controller
studied to the standard the GSTMCU and Shifter studies reached — what it *is*, how it operates (one device
byte, one memory word, one stolen bus cycle, one sector, one register write), what the record disputes, and
what only the board can settle.

Read first, in this order: the U300 entry in `docs/handoffs/silicon-research-per-chip.md` §4, the
**April 1988 memo** as quoted in `research/gstmcu-silicon-implementation.md` §4 (it is the only prose spec
of any of these channels — and it is about the *other* chip), that study's §5.4 (the grants: `RDY`, `BR`,
`BGACK`, `DMADIRB`) and §5.5 (the register decode), the Shifter study's `research/gst-shifter-implementation.md`
§4.5 and §5.7 (the sound path's requester end, and the finding that it is not in U300), `design/disk-path.md`
(the subsystem's design document), `design/subsystems-spec.md` §1's Disk row and §3, and
`design/crossings.md`'s rows for `RDY`, `BR`, `DCYC` and `FDINT`/`DINT`.

---

## 1. What is already established about this part

* **No decap and no drawing, and unlike the GSTMCU there is no substitute.** Atari's recovered CAD has no DMA
  controller sheet (the archive's catalogue lists the GSTMCU, the Blitter and nothing else of this board's
  customs), and no public decap of any STE custom exists. The basis is the service manual's **40-pin symbol,
  complete** (`design/board-inventory.md` §3.6: 40 pins, every one named), the two neighbouring studies'
  documented sides of the shared wires, the ACSI protocol as Atari documented it, and three emulator models.
* **The sound channel is not here, and that is now a three-ended finding.** The GSTMCU memo specifies it from
  the counter side (`XSRQ`, `SLOADB`, `SINT`), the Shifter study finds the requester, the FIFO and the byte
  split at the other end, and the board article says the Shifter manages the DMA sound with `SREQ`/`*SLOAD`
  while the counters sit in the GSTMCU. **U300 is not in that path at all.** The per-chip index's role cell
  still says "STE sound DMA" and is corrected with this brief — the study must *test* the claim rather than
  inherit either reading.
* **What this chip does have** is the disk path and the machine's second bus master: `FDRQ`/`FDCS` and
  `HDRQ`/`HDCS` (floppy and hard-disk request/select), `CD7–CD0` + `CA1`/`CA2` + `CR/W` + `ACK` (the 8-bit
  ACSI device bus), `D0–D15` (the 16-bit machine bus), `RDY` to the GSTMCU, `R/W`, `A1`, `FCS`, `CLK`.
* **The grant path is documented from U400's side** and is the cheapest lever: `RDY` (pin 38) reaches the
  GSTMCU, which relays `BR`; `BGACK` is a function of the address select, so a steal is cycle-granular; the
  DMA address counter on that side is 24 bits (`DMAH/DMAM/DMAL`, sheet 16) and the direction is `DMADIRB`.
  *(Executed: confirmed, and sharpened — `RDY` is asserted **per sixteen-byte burst**, so a sector is 32
  steals; the counter is 22 bits; and U300 has no address pins, so U400 performs every word of the burst.)*
* **The STE fixed the ST's worst disk behaviour on this chip's output lines.** `*HDINT` and `*FDINT` leave
  U300 into the GSTMCU, which NORs them into `*DISKINT` on the MFP's I05 — the pin that was *not connected*
  on the STF, which is why ST software polls the DMA counter through a whole transfer. That is the study's
  one hardware-argued claim to verify from this end.
  *(Executed — corrected: they do **not** leave U300, which has no interrupt pin at all. `FDINT` is the
  WD1772's `INTRQ` (`U301.28`) and `HDINT` the ACSI header's `_IRQ` (`J301.10`), both inputs to the GSTMCU's
  combiner; the NOR output `*DISKINT` leaves on `U400.17`. The claim itself survives — the STF's MMU never saw
  the pair — but its source was this board's controllers, not this chip.)*
* **Half the model is missing in public sources.** `research/gstmcu-reconstructions.md` §7 records that the
  MiSTer core's `rtl/dma.v` is an **ST-class** DMA with no `$FF890x` sound support, and that the
  DMA-controller ↔ MMU `RDY` timing is therefore only half modelled anywhere.

## 2. Acceptance criteria

A reader who has the study must be able to do these without opening another document:

1. ✅ **Follow one device byte to one memory word**: `*FDRQ`/`*HDRQ` request → `*ACK` per byte on `CD0–CD7` →
   the two bytes packed into one 16-bit word on `D0–D15` → the `RDY` handshake that turns it into a stolen
   bus cycle, naming every pin and stating which side drives it.
2. ✅ **Draw the bus steal**: `RDY` → the GLUE's `BR` relay → the 68000's `BG` → `BGACK`, what one cycle
   costs (`DTACK` on `ICLK8B`, whole 8 MHz edges, 250 ns granularity), and why this path differs from the
   Blitter's, which asks the CPU directly.
3. ✅ **Follow the counter end to end**: a write to `$FF8609/0B/0D` → the 24-bit address on the machine bus
   during a granted slot → `DMADIRB`'s direction → and what happens at terminal count, with the readback
   behaviour stated.
4. ✅ **Say where the end-of-transfer interrupt comes from**: `*HDINT`/`*FDINT` → `*DISKINT` → MFP I05, what a
   program sees, and what the STF did instead.
5. ✅ **Say what of the sound channel is in this chip — nothing — and give the falsifier**: which two pins and
   which two documents would have to be wrong for the claim to fall, and the scope on them that would
   settle it.
6. ✅ **Follow one register write**: `*FCS` with `A1` only (so four addresses, not eight), the byte lanes, what
   a read returns, and which of `$FF8604`/`$FF8606` is a command to the FDC and which is a mode to this chip.

## 3. Evidence, and what each layer can answer

| layer | what it gives | what it cannot |
|---|---|---|
| service manual, sheet 3 (40-pin symbol) | **the complete pin list and directions** — the one primary source this study has | internal structure, timing, register semantics |
| the April 1988 memo (GSTMCU archive) | the sound channel from the other end — `SINT` 480 ns, `XSRQ`'s rule, frame boundaries | anything about this chip, and it never names it |
| Atari's ACSI/ASCI protocol documentation | the device-side handshake: `*ACK`, `CA1`/`CA2`, `CR/W`, byte order | what the controller does with the bytes |
| Hatari `fdc.c` + `dmaSnd.c`, MAME `atarist`, MiSTer `rtl/dma.v` | behaviour to explain, and a checklist of constants nobody sourced | silicon truth — T3 by definition, and the RTL is ST-class only |
| the board: WD1772, the 19-pin ACSI header, the drive | sector cadence, device population, the analogue side of "is it spinning" | logic |
| our own harness | the only instrument that settles a timing question on this machine | anything about a cycle it cannot reach |

**The 40-pin symbol is complete and that makes this the cheapest chip of the campaign so far** — no crop to
read, no question for the owner about a pin. Start there, then the ACSI documents, then the memo, and only
then an emulator.

## 4. Method rules the previous two chips earned

* **A name tells you which side, not which function.** `CR/W` is the direction toward the *device*, `*FDCS`
   a select this chip drives, `*ACK` an input from the device, `RDY` an output to the GLUE. Read a net's
  pins, never its letters — the Shifter study's `SHR01` lesson one chip over.
* **A handshake held for a whole sector is a level, not an edge.** `RDY` is asserted for milliseconds while
  the FDC dribbles bytes; model the duty, not an imagined per-byte pulse (the memo's `SINT` lesson).
  *(Executed — half wrong: the handshake is **per byte** (240 ns answer, 250 ns wide) and the level is the
  **bus request**, four microseconds per sixteen-byte burst. Both halves are needed.)*
* **A byte counter and a word counter are not the same counter.** Two device bytes make one memory word; if
  the study finds one counter, say which side it counts and what the other side derives from.
* **Ask where the mechanism lives before modelling it.** The sector's timing, the MFM encode and the
  rotation are the WD1772's; a cadence that the FDC already fixes is not a finding about U300.
* **No priority table.** If `*FDRQ` and `*HDRQ` can both be asserted, the ordering is a gate somewhere or it
  is undefined; ADR 0007 forbids inventing a ranking, so an unresolved case is a labelled `unknown` with the
  probe that ends it.
* **Close the arithmetic against the part.** The counter's width, the sector's size and the device's bit rate
  are all facts with a datasheet behind them; if the traced behaviour needs a 23-bit address on a 4 MB
  machine or a sector longer than its own rotation, the reading is wrong, not the part.
* **The 84-pin/40-pin package is a design constraint.** Every register address here is `A1` and nothing else:
  a decoder question in this family is usually answered by counting the address pins.

## 5. Arithmetic seeds to check, not to copy

* **A sector is 256 words, and almost all of them wait.** 512 bytes = 256 memory words; a DS track is 9
  sectors = 4 608 bytes at 300 rpm, so a sector slot is ~22 ms while its 4 096 MFM bits take ~16.4 ms at
  250 kbit/s. The DMA is idle-waiting on the FDC for most of that, which is why the steal is cheap and why
  `RDY` is a level.
* **The steal's cost, from the other chip's numbers**: `DTACK` is a flop on `ICLK8B`, so a cycle quantises to
  whole 8 MHz edges and costs a multiple of 250 ns; 256 words a sector is 64–128 µs of CPU time spread over
  ~16 ms — under 1 %. If the study derives anything near a millisecond, the grant model is wrong.
* **The counter is 24 bits on U400's side** (sheet 16, `DMAH/DMAM/DMAL`, three byte registers). A 23-bit
  counter here, or a 32-bit one, means one of the two chips is misread — and that is a finding.
* **The sound channel's top rate is one word per 20 µs at 50 kHz**, and the GLUE offers at most 48 blanking
  windows a line for sound and refresh together. If any part of that path were in U300 it would need a
  clock this chip does not receive.
* **`*DISKINT` is a NOR of two inputs** and reaches one MFP pin (I05). Two sources, one line, one vector:
  say which bit the MFP sets and what a handler must do to tell them apart.

## 6. What must stay `unknown` until measured

* The `*ACK` handshake's timing at a sector boundary — a scope on `CD0–CD7` and `*ACK` while the WD1772 is
  commanded; nothing in the record states it.
* Whether the address counter advances on the device's request or on the granted slot (the same shape of
  question the memo left open for the sound counter, and the same kind of answer: a readback poll).
* The priority, if any, between `*FDRQ` and `*HDRQ` when both assert.
* The undefined bits of `$FF8606`, and whether the mode register has any behaviour beyond direction.
* Which device byte becomes `D8–D15` — the packing order, which software can see through a single read.
* Terminal-count behaviour: does the transfer stop, reload, or run on? The ST and the STE may differ, and the
  `*DISKINT` line is the difference software sees.

## 7. Definition of done

1. ✅ `research/dma-controller-implementation.md` in the study shape (§1 summary, §2 evidence, §3 the chip as the
   record shows it, §4 the chip in operation, §5 conflicts, §6 what the model needs, §7 tests, §8 what stays
   unknown, §9 licence), every acceptance criterion in §2 demonstrably met, `verify --min-coverage 0.30` green.
2. ✅ `docs/handoffs/silicon-research-per-chip.md` §4's U300 entry updated to **done** with a one-line pointer,
   its role cell corrected (**no sound DMA**, dated), and this file's items ticked.
3. ✅ The two neighbouring studies' claims about this chip confirmed or corrected in place, with a date: the
   GSTMCU study's `RDY`/`BR` relay (§5.4) and its disk counter row (§3.4); the Shifter study's §5.7 finding
   that the sound path is a two-chip mechanism.
4. ✅ Commits: one for the study, one for the index and the corrections; pushed to `origin main`.
5. ✅ Anything that changes our model lands as a dated correction in `design/disk-path.md` or
   `design/subsystems-spec.md`, or as a ticket — never as prose inside a ranking table.

---

*Written 2026-11-21, closing the U401 study with the next chip's brief. The ordering stays as the per-chip
plan set it: the DMA is the campaign's third chip because the sound channel is the one place where our core
is most likely to be subtly wrong, and this is the last of the three ends to be read.*
