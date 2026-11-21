# The DMA controller (C398739-001) at the silicon level

### What the 40-pin symbol, Atari's ACSI guide and three models establish, and what only the board can settle

*Subject: the STE's disk-DMA controller, `U300` on the 1040STE board, Atari house number C398739-001, 40-pin DIP.
Studied 2026-11-21, the third part of the per-chip campaign after the GSTMCU and the GST Shifter — and the first of the three with a **primary prose specification of its own interface**, which is why its answers are firmer than the Shifter's even though its silicon is just as invisible.*

*Basis: **no die photograph and no Atari drawing** — the recovered CAD set has no DMA sheet, and no STE custom has ever been decapped publicly [2][4][5][12].
The study therefore rests on the service manual's **40-pin symbol, complete** (`design/board-inventory.md` §3.6), Atari's own **ACSI/DMA Integration Guide** of 28 June 1991 — written for programmers of this exact port, and publishing its register map, its quirks and its timing diagrams [20] — the two careful secondary documents that measure the port (`mikrosk`'s DMA-port article [21], DrCoolZic's FD/HD programming reference [23]), the two neighbouring studies' documented sides of every shared wire, and three emulator models used only as a checklist.*

*Labels: `documented` (a primary source or a data file says so), `derived` (arithmetic over counted facts), `inferred` (a re-implementation or a careful reader implies it), `unknown`; T1 hardware-captured, T2 hardware-corrected, T3 emulator-derived.
Companion pages: `research/gstmcu-silicon-implementation.md` (§5.4 the grants, §3.4 the counter), `research/gst-shifter-implementation.md` (§5.7, the sound path's other end), `design/disk-path.md` (the subsystem), `design/crossings.md` (the subscription table).*

---

## 1. Executive summary

| question | answer | § |
|---|---|---|
| Do die photographs or drawings exist? | **Neither** — the same position as the Shifter, and here it matters less than anywhere else in this campaign, because the part's interface is specified in prose by its own vendor. | 2.1 |
| What is it made of? | `derived` **A port with a FIFO on it.** 37 signal pins in four groups — a CPU port, a device port, a request port and a data path shared with the CPU bus — and the visible state is one 16-bit mode word, a 3-bit status, an 8-bit sector count and two sixteen-byte FIFOs. No address pins, no `DTACK`, no `BERR`, no interrupt pin, no reset pin. | 3.1, 3.4 |
| Microcode, PLA or odd random logic? | **Random logic with no table** — the same answer as both neighbouring chips, and this time the register file is small enough to count from the interface alone. | 3.2 |
| What counts? | `documented` A **byte handshake** (one per device request), the two FIFO pointers, an **8-bit sector count** decremented once per 512 bytes. Nothing in this chip counts addresses. | 3.3 |
| Clocking? | One 8 MHz `CLK` in (pin 39) — the same clock the WD1772 takes [23]. Every timing below is a whole number of 125 ns clocks, because that is the only edge the chip has. | 3.3 |
| Who addresses memory during a transfer? | **U400, not U300.** The service manual says it outright: the memory controller produces *all* the addresses for video, sound and DMA and "these addresses never appear on the system address bus". U300 supplies bytes and asks; the GSTMCU addresses, times and performs the cycle. | 3.4, 4.3 |
| Where is the 24-bit address counter? | **In the GSTMCU** (`DMAH/DMAM/DMAL`, sheet 16) — written at `$FF8609/0B/0D`, read back at the same three addresses, only 22 bits implemented on this family. | 4.4 |
| Is the sound channel in this chip? | **No**, and the 40-pin symbol is a complete falsifier: there is no pin it could use. | 4.7 |
| What does the emulator still not know? | the shipped revision (`-001` vs `-001A`), the power-on state of a chip with no reset pin, the width of the select that makes `$FF8600` an alias or not, and the mechanism by which the direction bit reaches U400. | 8 |

`derived` The one-sentence machine: *a twenty-four-bit base pointer lives in the memory controller and a sixteen-byte FIFO lives here; the disk controller or the ACSI device hands over one byte per request and this chip answers it in half a microsecond, and every sixteen bytes it raises one level to the GSTMCU, which takes the bus from the 68000, writes or reads eight words from its own counter, and gives it back — thirty-two times a sector, four microseconds each.*

`derived` Three findings reorganise the campaign's arithmetic, and each corrects a sentence a previous session wrote:

1. **This part is not the machine's second bus master; it is a client of the memory controller.** It owns no address line and no bus strobe (§3.1, §3.4). The Blitter is a bus master; this chip asks one.
2. **The DMA address counter is a GSTMCU register**, `$FF8609/0B/0D` included, so "the DMA register file" is split across two packages — mode, status and sector count here, address and direction there (§4.4).
3. **The bus is asked for once per sixteen bytes, not once per sector**: 32 grants a sector, ~39 a frame at the floppy's rate, 234 class-1 records a frame — which replaces `design/disk-path.md` §3's six wakes a sector (§4.2, and the dated correction in that file).

---

## 2. The evidence, and what each layer can answer

### 2.1 No decap, no drawing — and a symbol that is complete

`documented` The recovered archive that carries the GSTMCU carries nothing for this part: Zietz's catalogue lists the GSTMCU, the Blitter and nothing else of this board's customs, and the public decap archives were re-checked for this study and are unchanged at five Atari parts, every one Lynx or 7800 [2][4][5][12].
`documented` What exists instead is the service manual's sheet 3 symbol — **40 pins, every one named**, which is the one primary source this study has and is enough, because the part's whole function is its pin set (`design/board-inventory.md` §3.6).
`derived` The absence of an address bus on the symbol is not a transcription gap: pins 4–19 are the sixteen data lines of the *CPU* bus, and there is no second group of eight or twenty-four address outputs anywhere on the package (§3.1).
A part that cannot drive an address cannot perform a memory cycle, and that single observation decides a third of this study.

| what a U300 decap would give | what it cannot give |
|---|---|
| the shipped mask and its revision (`-001`, `-001A`) [27] | any register's meaning — Atari's guide states them [20] |
| the true FIFO depth (two sixteen-byte buffers per every model, "sixteen bytes" per Atari) | the ACK timing at a sector boundary, which is a scope measurement |
| proof that the handshake is a state machine rather than a counter | anything about the counter, which is not in this package |

`inferred` The trade is poor again, and for a specific reason: the questions this study leaves open are all *electrical* — a level, an edge, a strobe's width — and a decap answers none of them. §7 is the shopping list for the instrument that does.

### 2.2 Atari's own guide is the prose specification this campaign had been missing

`documented` The **ACSI/DMA Integration Guide** of 28 June 1991 covers exactly this part, names it "the DMA chip" and "the DMA channel", and states its register map, its mode register bit by bit, its FIFO behaviour, its programming order and its anomalies [20].
It is the document the GSTMCU study had to substitute the April 1988 memo for, and it is better: it is about *this* chip.
`documented` Four sentences in it are load-bearing enough to quote, and each settles a question the brief left open:

> "The interrupt outputs of the internal floppy disk controller and the external ACSI DMA port are logically
> OR'ed. The pin of the MFP GPIP will read as a '0' if either the FDC or a selected ACSI device controller is
> asserting its interrupt request." [20]

> "During the transfer a very fast hardware 'handshake' takes place with the host asserted _ACK signal and the
> peripheral controller asserted _DRQ signal. This occurs on each byte without host processor intervention." [20]

> "DMA Address — Load the low order byte first, then mid, then high. The DMA pointer must be set before
> writing to the sector count register." [20]

> "On DMA reads to the Atari ST this FIFO is only written to the memory of the Atari ST when it is completely
> filled. Therefore, transfers of less than 16 bytes will remain in the FIFO until it is flushed or filled by
> subsequent commands." [20]

`derived` The first sentence decides who drives the two interrupt nets: neither of them is this chip's, and the OR is on the board (§4.5).
The second decides the direction of `ACK` and `DRQ`, which the service manual's symbol states the other way round (§5.1).
The third decides the counter's write order, which no other source would have guessed.
The fourth decides the unit of transfer, and therefore the cost of the steal (§4.2).

### 2.3 The three models are a checklist, and each answers a different question

`inferred` No vendored model is evidence of silicon; each is one author's reading, and they disagree in ways §5 adjudicates.
What they are good for is the checklist of constants nobody sourced:

| model | where its DMA lives | what it models | what it does not |
|---|---|---|---|
| Hatari `fdc.c` | inside the FDC emulation | one 16-byte FIFO, a 32-clock burst per block, the sector count, "verified on a real STF" notes on two quirks [29] | the second FIFO, any bus arbitration |
| Steem SSE `dma.cpp` | its own `TDma` | **two** 16-byte FIFOs used alternately, a burst of eight words, status bits, "the chip has no reset pin" [30] | the MFP-side interrupt, real grant timing |
| MAME `stmmu.cpp` | inside the **MMU** device | the whole `$FF8604/06/09` block, a 32-byte FIFO, the byte packing order, a 22-bit address mask [24][25] | the ACSI device (its `hdc_transfer` is unimplemented) |
| MiSTer `rtl/dma.v` | its own module, a bus master itself | `CS`+`A1` only, the mode and status words, a 32-byte FIFO, a request asserted only during a block, and a status bit 0 hardwired "OK" [26] | the ACSI bus, the second FIFO as two |

`derived` The distribution is itself a finding: MAME puts the register block in the MMU because on the ST the MMU is what addresses memory; the MiSTer core makes the DMA a bus master because its ST core has no separate MMU arbiter; and the two that model the *chip* (Hatari, Steem) model one and two FIFOs respectively. §4.4's conclusion — the counter is not here — is what MAME's file layout was already saying.

### 2.4 The board's own parts, and the port the chip talks to

`documented` The floppy side is the WD1772 (`U301`), and the two signals that matter are its own outputs: `DRQ` (pin 27) "indicates that the Data Register is full (on a Read) or empty (on a Write operation)", and `INTRQ` (pin 28) is "set at the completion of any command, reset by a read of the Status Register" [28].
`derived` That names the inventory's one illegible WD1772 pin — 28 is `INTRQ` — and it fixes the direction of the two wires the brief had backwards (§5.1, §5.9).
`documented` The ACSI side is a 19-pin header (`J301`): data on 1–8, `_CS` on 9, `_IRQ` on 10, `RESET` on 12, `_ACK` on 14, `A1` on 16, `R/W` on 18, `_DRQ` on 19 [21].
`derived` Everything the DMA controller can see of a hard disk is those eight pins plus its own five control lines; the protocol above them is Atari's, documented in the same guide as the register map [20].

---

## 3. The chip the record shows

### 3.1 The census: 37 signals, four ports and a shared data path

`documented` The 40 pins, grouped by what they connect to rather than by number (`design/board-inventory.md` §3.6, all named on the manual's sheet 3):

| port | pins | count | what it is |
|---|---|---|---|
| **CPU** | `D0–D15` (4–19), `A1` (2), `R/W` (1), `FCS` (3), `CLK` (39) | 20 | the 68000's bus: sixteen data lines, **one** address bit, direction, chip select, clock |
| **device** | `CD7–CD0` (29–36), `CR/W` (25), `CA1` (26), `CA2` (27), `FDCS` (22), `HDCS` (24) | 13 | an 8-bit private bus with its own direction, two address lines and two chip selects |
| **request** | `FDRQ` (21), `HDRQ` (23), `ACK` (37) | 3 | two request inputs and one acknowledge output (directions corrected, §5.1) |
| **memory** | `RDY` (38) | 1 | one output: "I want the bus", to the GSTMCU's pin 135 |
| **power** | `GND` (20, 28), `VCC` (40) | 3 | — |

`derived` Counted, that is 37 signals and 3 power pins, and the arithmetic closes on the package: there is no room and no pin for an address bus, a `DTACK`, a `BERR`, an `IPL`, an interrupt output or a reset input.
Three of those absences are decisions (§3.4) and the fourth is a quirk (§4.6).

```
            ┌───────────────────────┐
D0-D15    ──┤ U300                  ├─ CD0-CD7 ─▶ WD1772 U301 / J301 ACSI
A1 FCS R/W──┤ C398739-001           ├─ FDCS HDCS CR/W CA1 CA2 ─▶
CLK 8 MHz ──┤ mode | status         ├◀─ FDRQ, HDRQ  (inputs)
            │ count | FIFO x2       ├─ ACK ─▶  (output)
            └───────────┬───────────┘
                        │ RDY (out, L)  → GSTMCU pin 135
                        ▼
            ┌───────────────────────┐
            │ GSTMCU (U400)         ├─ MAD0-MAD9, RAS/CAS ─▶ DRAM
            │ counter + DTACK       ├◀─ CPU bus A1-A23: U400 drives the cycle
            └───────────────────────┘
```

`derived` The drawing is the whole architecture: the data flows *through* this chip and the addresses never come out of it.

### 3.2 No table, and a register file small enough to count

`inferred` Nothing in this part's description is an array: no source mentions a ROM, a PLA or a RAM, and the only regular structure anyone models is the FIFO, which Atari calls "what logically looks like a sixteen byte FIFO" [20].
It is the same conclusion the two neighbouring studies reached from a cell library and from absence, reached here from the interface.
`derived` The state the interface forces is small — one 16-bit mode word, a status word, an 8-bit sector count, two sixteen-byte buffers and the handshake's phase — which is why three independent models can agree on the behaviour while disagreeing about the FIFO's shape (§5.5): the visible behaviour does not depend on it.

### 3.3 What counts, and on which clock

`documented` The chip takes one clock, `CLK` at 8 MHz (pin 39), the same 8 MHz the WD1772 and the DMA arithmetic use [23].
`derived` Three things count, and none of them counts addresses:

| counter | what clocks it | width | evidence |
|---|---|---|---|
| byte handshake | one step per device request, `FDRQ`/`HDRQ` in, `ACK` out | a phase, not a number | `documented` timing: `ACK` no more than 240 ns after `_DRQ`, asserted for ~250 ns [21] |
| FIFO fill / drain | the same handshake, and the burst | two pointers over sixteen bytes each | `documented` "multiples of eight words (16 bytes)" [20]; `inferred` two buffers [23][26][30] |
| sector count | decremented once per **512 bytes**, i.e. per 32 blocks | 8 bits, 1–255 | `documented` 255 × 512 = 127.5 KB maximum [20][23] |

`derived` The byte handshake is the only clock-like thing in the chip that is not the 8 MHz input, and its period is the *device's*: 32 µs for a double-density floppy byte (250 kbit/s data rate, §4.1), 500 ns at the ACSI port's measured peak of 2 MB/s [22].
That is the brief's "a handshake held for a sector is a level" lesson, restated correctly: the handshake is per byte, and it is the *bus request* that is a level over a block (§4.2).

### 3.4 Three absences, and what each one decides

- **No address pins.** `derived` 37 signals of which 16 are data and none is an address output; the service manual's own sentence — the memory controller "produces all of the addresses for video, sound, and DMA" and "these addresses never appear on the system address bus" (`research/bus-arbitration.md`, quoting the service manual) — is the architectural statement of the same fact. U300 can therefore never be the master of a memory cycle: it drives `D0–D15` and asks on `RDY` (§4.3).
- **No `DTACK` and no `BERR`.** `derived` A register access to this chip is answered by the GSTMCU's own decode (`REGXACK` → the `DTACK` flop of `research/gstmcu-silicon-implementation.md` §5.4), which is why the chip's mode and sector-count writes are two-clock answers and why a *device* cycle's slowness is elsewhere entirely.
- **No interrupt output.** `documented` Atari says it in as many words: the chip has "no interrupt capability" and the completion interrupts come from the controllers [20][23]. The board's `FDINT` and `HDINT` are inputs to the GSTMCU, which NORs them into `*DISKINT` for the MFP's `I05` [6] (§4.5, §5.9).

`derived` The fourth absence is not architectural but legal: **no reset pin**.
The chip has no `RESET` input at all, and Steem's model records the consequence in a comment ("no reset pin on that chip") [30].
The only reset the part has is the direction-bit toggle it performs on itself (§4.6).

### 3.5 The register block as this chip sees it

`documented` The CPU port gives the chip one address bit and a chip select; with `R/W` that is four access types, and the family's decode arithmetic — a decoder question answered by counting the address pins, as the brief's last method rule says — gives:

| the CPU's address | `A1` | what this chip sees |
|---|---|---|
| `$FF8604`, `$FF860C` | 0 | **register 0** — the device window, or the sector count (mode bit 4) |
| `$FF8606`, `$FF860E` | 1 | **register 1** — status on a read, the mode word on a write |
| `$FF8600`, `$FF8608` | 0 | the same register 0, *if* the select covers them — `unknown` (§8) |
| `$FF8602`, `$FF860A` | 1 | the same register 1, under the same proviso — `unknown` |

`derived` The brief's "four addresses, not eight" is this table's first two rows: the chip can distinguish exactly two registers, and every other address in the sixteen-byte block is either an alias of one of them or belongs to another chip.
`documented` The three address-counter bytes are **not** this chip's (they are `A1`=1 too, and would collide with the mode word if they arrived here), which is the register-level proof of §4.4: the GSTMCU decodes `$FF8609/0B/0D` itself and asserts `FCS` only for its own two registers [2][23].

### 3.6 What is not in this chip

Four things that look like they belong here and do not:

- **The DMA address counter** — `DMAH/DMAM/DMAL`, sheet 16 of the recovered GSTMCU design, and "in fact located in the MMU chip" [2][23].
- **Every byte's memory cycle** — the addressing, the `RAS`/`CAS` pair and the `DTACK` that ends the cycle are the memory controller's, and the steal is its arbitration (§4.3).
- **The end-of-transfer interrupt** — raised by the WD1772 or the ACSI device, OR'ed on the board, delivered to the MFP's `I05` [20][6].
- **Anything of the sound channel** — no pin, no register, no clock (§4.7).

---

## 4. The chip in operation

### 4.1 One device byte

`documented` The sequence, in the read direction (device → memory), with each pin and the side that drives it:

| # | event | net | driver | receiver | time |
|---|---|---|---|---|---|
| 1 | the FDC assembles a byte and its data register fills | `DRQ` `U301.27` → `FDRQ` `U300.21` | WD1772 | U300 | every 32 µs on a DS floppy [23] |
| 2 | this chip selects the device and starts its cycle | `FDCS` `U300.22` → `CS` `U301.1`; `CR/W` `U300.25` → `R/W` `U301.2`; `CA1`/`CA2` `U300.26/27` → `A0`/`A1` `U301.3/4` | U300 | WD1772 | the mode register's bits 1–3 choose which register |
| 3 | the byte crosses the private bus | `CD0–CD7` `U300.29–36` ↔ `DAL0–DAL7` `U301.5–12` | WD1772 | U300 | — |
| 4 | the byte lands in the FIFO, in the half its position decides | internal | U300 | — | first byte → `D8–D15`, second → `D0–D15` (§4.2) |

`documented` In the write direction the same pins run the other way round: this chip reads a word from memory into a FIFO and, on the device's request, drives it onto `CD0–CD7` with `CR/W` high and `FDCS` low [20][23].
`derived` For the ACSI device the handshake is the port's own and uses the third pin: the peripheral asserts `_DRQ` (open collector, "only peripherals that have been commanded may drive it"), the host asserts `_ACK` "for about 250 ns" no more than 240 ns later, and the byte is on `CD0–CD7` [20][21].
`derived` So one device byte costs this chip **half a microsecond at most** — the measured `ACK` answer is ≤240 ns [21] — and costs the CPU **nothing** — the byte never touches the CPU bus, which is the whole point of the part.

### 4.2 One block, and one stolen burst

`documented` Sixteen bytes fill a FIFO and nothing goes to memory until they do: "transfers of less than 16 bytes will remain in the FIFO until it is flushed" [20], and the models agree that there are **two** such buffers so that one can drain while the other fills [23][26][30].
`documented` A full FIFO raises the request: `RDY` (pin 38, active low) to the GSTMCU's pin 135, which is the catalogue's `RDY` row — driver Disk, subscriber System Control Unit, class 1, "~60 while streaming" [`design/crossings.md` §5].
`derived` The burst is **eight words**, "eight 16-bits words from the FIFO into the memory" [23], i.e. one FIFO's worth, and at four clocks a word it is **32 clocks = 4 µs**; both models that count cycles charge exactly `4 × 16 / 2` [29][30].
`derived` The arithmetic this fixes, and each number is a fact with a datasheet behind it:

| quantity | value | derivation |
|---|---|---|
| device bytes per block | 16 | `documented` [20] |
| blocks per 512-byte sector | **32** | 512 ÷ 16 |
| memory cycles per block | 8 | one word each |
| CPU clocks per block | **32** | 8 × 4; 4 µs at 8 MHz |
| CPU clocks per sector | **1 024** | 256 words × 4 clocks = **128 µs** |
| share of a sector's 16.4 ms | **0.78 %** | 128 µs ÷ 16.38 ms |
| blocks per frame, streaming | **39** | 625 bytes/frame ÷ 16 (31 250 B/s ÷ 50 Hz) |
| clocks per frame, streaming | 1 250 of 160 256 | 0.78 %, the same figure |

`derived` The brief's "64–128 µs" range collapses to 128 µs at four-clock cycles, and its "under 1 %" is confirmed at 0.78 % — the steal is cheap because the FIFO does the waiting.
`derived` The two-FIFO design has an exact justification in the same arithmetic: at the ACSI port's measured peak of 2 MB/s a 16-byte block arrives in **8 µs** [22], so one block can drain in 4 µs while the other fills, with 4 µs of slack for the grant.
At the floppy's 32 µs a byte there are 512 µs between requests and the arbitration could hardly fail.

### 4.3 The bus steal, drawn

`documented` The chain, from this chip's output to the CPU and back, is the catalogue's five rows and the GSTMCU study's §5.4:

```
  U300            U400 (GSTMCU)   68000
  │               │               │
  │──────────────▶│               │     RDY (38, L): a level per block
  │               │──────────────▶│     BR (29, wired-OR), relayed
  │               │               │◀    BG (28): the CPU's cycle is done
  │               │──────────────▶│     BGACK (26): a function of ADDRSEL
  │               │               │
  │               •               │     DTACK: a flop on ICLK8B, 8 MHz edges
  │               •               │     MAD0-MAD9 + RAS/CAS: the DRAM cycle
  │               •               │     DMAH/DMAM/DMAL: the address, +1/word
  │◀──────────────│               │     D8-D15: the eight words, from U300
  │               │               │
  │──────────────▶│               │     RDY released, then BGACK, BR
  │               │               │
  │               │               │
```

`documented` `DTACK` is "the output of a flip-flop clocked on `ICLK8B`", so a cycle quantises to whole clocks [2].
`derived` The cost of one lost slot is therefore a multiple of 125 ns, and one granted burst is a multiple of four clocks; there is no stall of unknown length, which is the property every timing model wants and none of the models has.
`derived` Six class-1 records per burst — three to acquire (`BR`, `BG`, `BGACK`) and three to release — by the catalogue's own rule, which gives **234 records a frame** while the floppy streams continuously (39 × 6).
`derived` And this is exactly where the path differs from the Blitter's: the Blitter drives `BR`/`BG`/`BGACK` itself and takes mastership of the CPU bus, while this chip has neither the pins nor the grant logic — it asks one line and a *memory controller* does the work [2][`design/subsystems-spec.md` §3].
`derived` So `RDY`'s duty is not "milliseconds while the FDC dribbles" (the brief's sentence) but **the length of one burst, every sixteen bytes** — a level four microseconds long, asserted about every half millisecond on a floppy and every eight microseconds at the ACSI port's peak.
The level-versus-edge lesson survives; the scale was wrong by three orders of magnitude (§5.4).

### 4.4 The counter, end to end

`documented` The three bytes at `$FF8609/0B/0D` are a 24-bit base pointer with a decrement-per-block register beside it, written **low, mid, high** and read back **high, mid, low** [20][23].
`derived` Where they live is settled by three independent routes: the recovered design has the counter on sheet 16 of the GSTMCU (`DMAH/DMAM/DMAL`, clocked by the `DMADIR`/`RDY` handshake with U300) [2]; the programming reference says the address register "is in fact located in the **MMU chip**" [23]; and MAME's model implements `$FF8604/06/09` inside its `st_mmu_device`, with the FDC's `drq` wired into the same device [24][25].
`derived` Only **22 bits** reach the DRAM decode — writing `$41` to `$FF8609` is equivalent to writing `$01` — which is 4 MB and closes against the machine's maximum memory, as the brief's arithmetic rule demanded [23].
`derived` Follow one write: a store to `$FF8609` puts the byte on `D8–D15`, the GSTMCU's decode matches the address and raises its own acknowledge (no `FCS`, so U300 never sees it), and the value lands in `DMAH` [2][23].
A `$FF8606` write with bit 4 set followed by a `$FF8604` write loads the sector count in this chip and, via `$FF8606` bit 8's direction, arms the transfer [20][23].
`derived` During the burst the counter's three bytes are the address driven into the DRAM address mux, one increment per word; software reading `$FF8609/0B/0D` mid-transfer therefore sees the transfer's *position*, which is the cheapest T1 instrument on this whole subsystem (Atari's own note says the same: the pointer must be set before the count, and the count starts the transfer [20]).
`derived` At terminal count the sector count reaches zero and "the DMA will stop to transfer data" [20][23]; the counter does not reload, does not wrap and does not clear — it simply stops advancing, and the next operation rewrites it.
The status word's bit 1 then reads 0 (count zero), which is the only software-visible consequence inside this chip [23].
`unknown` **How the direction reaches U400.** The mode word's bit 8 is this chip's, and the GSTMCU needs it to choose read or write for its own cycles; but no pin between the two packages carries a direction, and the GSTMCU's decode does produce a `DMADIRB` term [2].
The reading this study adopts — `derived` from the `$FF8260` precedent of one register decoded twice — is that U400 latches the direction off the same bus write.
The falsifier is a write to `$FF8606` with bit 8 toggled and a scope on the counter's behaviour: if U400's direction follows, the register is duplicated; if it does not, the net exists and is not on the symbol.

### 4.5 Where the end-of-transfer interrupt comes from

`documented` Not from here.
"The DMA chip has no interrupt capability" [21][23]; the completion is signalled by the controllers, and Atari's guide states the OR on the board [20].
`derived` The complete chain on this board, with each link sourced:

| link | driver | receiver | evidence |
|---|---|---|---|
| floppy command complete | WD1772 `INTRQ` (pin 28) | GSTMCU `FDINT` (pin 16) | `documented` [28] + the symbol |
| ACSI device complete | `J301.10` `_IRQ` | GSTMCU `HDINT` (pin 15) | `documented` [21] + the symbol |
| the two combined | GSTMCU `*DISKINT` = NOR(`*HDINT`, `FDINT`) | MFP `I05` (GPIP bit 5) | `documented` [6] |
| what a program sees | GPIP bit 5 reads 0 while a device asserts | TOS polls it; AHDI can enable it | `documented` [20][23] |

`derived` The STE's addition is not the interrupt but its *use*: the GSTMCU itself consumes `*DISKINT` so that it knows when to stop incrementing its DMA counter, which is the mechanism by which the transfer can end without software polling every byte [6].
On the STF those two signals "do not go to the MMU", so a DMA disk transfer had to be watched by a polling routine for its whole duration — the brief's hardware-argued claim, confirmed from this end and now attributable to a specific chip and pin [6].
`derived` One line, two sources, one vector bit: a handler must read the GPIP bit, clear the device's own interrupt (the WD1772 clears its `INTRQ` on a status read [28], an ACSI device on its status byte), and cannot tell the two apart from the MFP's side alone.

### 4.6 One register write

`documented` A store of the mode word to `$FF8606` [20][23]:

| bit | meaning | note |
|---|---|---|
| 8 | write (1) / read (0) — memory ← device or memory → device | **toggling it resets the chip**: FIFOs flushed, sector count cleared [20][23] |
| 7 | gate `_DRQ` to the internal FDC (1) or the external ACSI port (0) | Atari's wording: "1 gates _DRQ to the internal bus" [20] |
| 6 | disable | **ignored on this family** — "writing `$90` is equivalent to writing `$D0`" [23] |
| 5, 0 | reserved | — |
| 4 | 1 = the sector count register at `$FF8604`; 0 = the device window | [20][23] |
| 3 | 1 = `HDCS` asserted, 0 = `FDCS` | [20] |
| 2 | `CA2` — the FDC's `A1`; "not used for DMA operations" [20] | [20][23] |
| 1 | `CA1` — the FDC's `A0`, and the ACSI port's command-block marker | [20][21] |

`derived` So the "four addresses" of §3.5 are exactly what this part needs and no more: one word that is a device window or a counter, one word that is a mode or a status, and one address bit to choose between them.
The device's own register file is reached *through* the window, its address taken from bits 1 and 2 — which is why `$FF8604` is not "the FDC register" but a port whose meaning the mode word sets.

`documented` A read of `$FF8606` returns three bits: 0 error/no-error, 1 sector count not zero, 2 the device's request line [23][24]; the rest are reserved and, on real hardware, "the unused bits … are also changed by the DMA operations" (Hatari's note from a real STF) [29].
`documented` A read of the device window returns the device's byte in the low half; the upper half reads all ones [23] (one model returns zeros instead [26], §5.8), and a **byte** access to the window is a bus error on this family [29].
`documented` Four behaviours of this register block are quirks rather than definitions, and all four are Atari's or measured:

1. **The double strobe.** "It is possible to get a double strobe from the DMA chip if the DMA Mode Control Register is not written to immediately after the DMA Data Port" — hence the long write that puts the next mode word in the low half of the same instruction [20]. **No model implements this**: Hatari, Steem, MAME and the RTL have no notion of it (§5.7) [29][30][24][26]; it is filed as [#95](https://github.com/laurentprudhon/the-st/issues/95) with the probe that would settle it.
2. **Polling the status during a transfer may disrupt it** — Atari's and DrCoolZic's advice is to watch the GPIP bit instead [21][23].
3. **Reading the sector count returns garbage**: it is write-only, and Hatari returns "the most recent value" with a comment that this is what a real STF does [23][29].
4. **Writing the sector count triggers the operation**, and in the write direction "the first 32 bytes from memory are written into the internal FIFOs" immediately [23] — the two buffers prefilled before any device request, which is the closest thing this chip has to a burst of its own.

### 4.7 The sound channel: nothing, and the falsifier

`derived` The falsifier the brief asks for, stated as two pins and two documents: for any part of the sound path to be in this chip it would need a request input from the Shifter and a load/data strobe back to it — `SREQ` (`U401.36`) and `SLOAD`/`LOAD` (`U401.37`) — and this chip's 40 pins contain neither, nor a clock other than the 8 MHz one it shares with the FDC (§3.1, §4.1).
The two documents that would have to be wrong are **the service manual's sheet 3 symbol** (every pin named, three of them power, and no net left over) and **Atari's own ACSI guide** (which describes this part's whole function and never mentions a sample, a frame or an address counter) [20].
The scope that would settle it: `SREQ` and `LOAD` on `U401` pins 36 and 37 against every pin of `U300` during a sound frame.
Nothing will be found.
`derived` The positive statement, for the record: the counters are the GSTMCU's (`FBASE/FCOUNT/FEND`, 21-bit sound address, sheet 17, and the April 1988 memo's semantics), the requester, FIFO and byte split are the Shifter's (U401 §3.7 and §4.5), and the sound channel needs a clock this chip does not receive [2][6][16].
`documented` This is the third end of a finding that is now closed from all three: the memo specifies it from the counters' side, the Shifter study finds the requester, and this chip's pin census excludes it.

---

## 5. Conflicts in the record, adjudicated

**1. `FDRQ`, `HDRQ` and `ACK` point the wrong way in our own inventory.** `documented` The service manual's symbol marks `FDRQ`/`HDRQ` as outputs and `ACK` as an input, and `design/board-inventory.md` §3.6 transcribed that faithfully.
`derived` The nets contradict it: `FDRQ` (`U300.21`) is wired to the WD1772's `DRQ` (`U301.27`), which the datasheet calls "this active high **output**" [28]; a net between two chips cannot have two drivers.
And the ACSI protocol is explicit the other way round: `_DRQ` "is driven by the peripheral with an open collector output", while `_ACK` is what "the host asserted" [20][21].
`derived` The symbol is drawn from the *peripheral's* eye view — "a request from the host, an acknowledge from the device" — which is exactly the naming trap the brief's first method rule warns about.
Corrected: `FDRQ` and `HDRQ` are **inputs**, `ACK` is an **output** (§5.9).

**2. Whose counter is it?** `documented` The programming documents call `$FF8609/0B/0D` "the DMA address counter", and two emulators keep it with the DMA's registers [29][30].
`derived` The design, the reference and MAME's structure say otherwise: sheet 16 of the GSTMCU, "in fact located in the MMU chip", and `st_mmu_device`'s own `map()` [2][23][24].
The name is the programmer's, not the silicon's — and this study quotes the counter's behaviour but never its address.

**3. Status bit 0: "1 = no error" or "1 = error"?** `documented` Hatari says 1 = no error, "verified on a real STF"; Steem, DrCoolZic and MAME agree; the `mikrosk` article and the MiSTer RTL's *comment* say 1 = error [29][30][23][24][21][26].
`derived` The MiSTer **code** hardwires `1'b1` on that bit and its comment is the odd one out — which makes the split 5–2 with the two dissenters being a comment and a magazine-era article.
Adjudicated: **bit 0 = 1 is "no error"**, and the pass criterion is §7.2.

**4. `RDY`'s duty: a sector or a block?** `derived` The brief says "a level, not an edge… asserted for milliseconds while the FDC dribbles bytes"; the RTL asserts its request only while a block is draining (`ram_br = fifo_read_in_progress || fifo_write_in_progress`), Steem says "the CPU must free the bus for each 16-byte transfer", and DrCoolZic says the request follows a full FIFO [26][30][23].
Adjudicated: a level **per block**, four microseconds long, about 39 times a frame.
The brief's shape is right and its scale is wrong; §4.2's table replaces it.

**5. One FIFO of sixteen bytes or two buffers?** `documented` Atari: "what logically looks like a sixteen byte FIFO", filled "in multiples of eight words" [20]; Steem and DrCoolZic: two, used alternately [30][23]; MiSTer: a 32-byte array addressed as "2×8 word" [26]; Hatari: one, with a note that a second is needed only in write mode [29].
`derived` Both statements are true at different levels: the *programmer's* unit is sixteen bytes, and the *silicon* has two so that the arithmetic of §4.2 closes.
The model needs two buffers and one 16-byte contract.

**6. "The DMA holds the bus for the whole transfer."** `documented` The `mikrosk` article says the chip "holds the buss until all buffered data has been transferred" [21].
`derived` Read literally that is the block, not the sector — the same sentence begins "buffers 16 bytes of data before requesting the memory buss" [21].
With `RDY` per block (§5.4) the sentence is consistent and the brief's long level is not.

**7. The double strobe is documented and unmodelled.** `documented` Atari's anomaly is quoted in §4.6 [20]; no vendored model has any notion of it [29][30][24][26].
It is the one behaviour in this study that the record states, the hardware shows, and every emulator ignores — a candidate for a labelled `unknown` in our model rather than a guessed rule.

**8. The device window's high byte.** `documented` DrCoolZic: "the 8 upper bits consistently reads 1" [23]; MiSTer's RTL returns `{8'h00, byte}` [26]; MAME returns the byte with no high half at all [24].
Two implementations and one document disagree, and only a board settles it (§7.3).

**9. `FDINT`/`HDINT` do not leave this chip, and the inventory's U400 rows inherit the error.** `documented` The brief's §1 says "`*HDINT` and `*FDINT` leave U300 into the GSTMCU"; Info-Coach says the opposite — they are "the \*HDINT and FDINT interrupt signal **inputs** that takes the signals coming respectively from the hard disk controller (HD) and from the floppy disk controller (FD)" — and Atari's guide states the OR on the board [6][20]. §4.5's table is the corrected chain, and `design/board-inventory.md` §3.6/§3.7 carry a dated correction.

**10. "The machine's second bus master."** `derived` The brief and `design/subsystems-spec.md` §3 both call this chip the second master, on the strength of its burst.
It has no address pins, no `DTACK`, no grant logic and no way to drive a memory cycle (§3.1) — the master is the GSTMCU, and this chip is the *client* whose FIFO feeds it.
The subsystem boundary the owner drew (the burst belongs to the chip that produces it) stands; the words "bus master" do not.

---

## 6. What the model needs

Ranked by what each costs us when wrong.
Tiers per the campaign.

| # | question in our core | the claim that answers it | tier | home |
|---|---|---|---|---|
| 1 | Is a disk transfer a burst or a stream, and how often does the CPU lose the bus? | one 16-byte block per request, 8 words, 32 clocks, 32 blocks a sector | T2 (Atari's guide + two models + the RTL) | `design/disk-path.md` §3 (corrected 2026-11-21) |
| 2 | Does the DMA controller address memory? | no — the GSTMCU does, from its own counter; U300 drives data and asks on `RDY` | T2 (service manual + design + reference) | `design/subsystems-spec.md` §3, `design/disk-path.md` §4 |
| 3 | Where do the three address bytes live? | the GSTMCU, written low→high, read high→low, 22 bits | T2 (design sheet 16 + reference) | Disk's register table |
| 4 | What is terminal count? | the sector count hits 0 and the chip stops; no reload, no wrap | T2 (guide + reference) | Disk's status path |
| 5 | Where does the disk interrupt come from, and what does software see? | the controllers' `INTRQ`/`_IRQ` → GSTMCU NOR → `*DISKINT` → MFP `I05`; GPIP bit 5 reads 0 | T1 (the STE's own board description) | `design/timers-and-events.md`, `crossings.md` §4 |
| 6 | Is the sound channel in this chip? | no | T1 (pin census) | Sound — closed |
| 7 | Is status bit 0 "no error" when set? | yes | T3 → T1 by §7.2 | Disk's status read path |
| 8 | Does the chip reset with the machine? | it has no reset pin; only the direction-bit toggle resets it | T3 (Steem's comment) → §7.6 | Disk's reset path |
| 9 | Can software read the transfer's position? | yes — `$FF8609/0B/0D`, the running counter | T2 (guide) | Disk's counter read |
| 10 | Do transfers shorter than 16 bytes work? | no — the remainder stays in the FIFO; software must over-transfer to flush it | T2 (Atari's guide, three sources) | Disk's loader seam |

`derived` Two of these change a document rather than a constant: #1 rewrites the wake arithmetic of the disk path (six → 192+ records a sector, 234 a frame while streaming), and #2 moves the *addressing* out of Disk into the System Control Unit's burst answer, which is where the request already goes (`crossings.md` §5).

---

## 7. The experiments that would settle the rest

Seven tests, each a TOS program or a bootable `$4E71` filler on the real 1040STE, with a scope on a named pin or a read-back through a named register.

1. **The select's width, and the `$FF8600` alias.** Write a distinctive word to `$FF8600`, read `$FF8604`; then the same at `$FF8602`/`$FF8606`. *Pass:* a match means the select covers the whole sixteen-byte block and §3.5's last two rows are real aliases; a bus error or a no-op means the GSTMCU selects only the two registers and the "reserved" rows are truly reserved.
2. **Status bit 0's polarity.** Set the sector count to 0 (Atari's own error condition), read `$FF8606`. *Pass:* bit 0 reads **1** if §5.3 is right (the majority reading); bit 0 reading 0 while the transfer is healthy would invert every error test in our model.
3. **The device window's high byte.** Read `$FF8604` with mode bit 4 clear from a byte and from a word access. *Pass:* the high half reads all ones (DrCoolZic) and a byte access raises a bus error (§4.6); anything else moves §5.8.
4. **`RDY`'s shape.** Scope `RDY` (U300.38) and `BGACK` (U400.26) across one whole sector read with a known image. *Pass:* one `RDY` assertion per sixteen bytes, eight `BGACK`-held cycles each, ~500 µs apart and ~4 µs long; a single assertion for the sector falsifies §4.2 and restores the brief's model.
5. **The counter's readback cadence.** Poll `$FF8609/0B/0D` in a tight loop during a read and correlate with `RDY` on the scope. *Pass:* the value jumps by 16 at each burst and does not move between them (the counter advances on the granted cycles); a value that moves on the *request* instead means the counter is incremented by the handshake, which is the memo's open question reappearing one chip over.
6. **The reset state.** Power on with a mode word written by a previous session's program (a warm boot through the reset vector), and read `$FF8606`/`$FF8604` before anything writes them. *Pass:* a non-zero mode word proves the chip survives reset (§3.4); a zero word means it is cleared by something not on the symbol, and the "no reset pin" reading needs a second look.
7. **The double strobe.** Scope `FDCS` (U300.22) and `CA1`/`CA2` while writing two command bytes to the device window *without* rewriting the mode word between them, and again with the Atari long write. *Pass:* the first sequence shows a repeated strobe for one intended byte; the second does not. That is the only measurement in this study that no emulator can predict, and it decides whether §5.7 is a model rule or a footnote.

`derived` Tests 4 and 5 are the cheapest and settle the most: they turn §4.2 and §4.4 from T2 into T1 and they need one channel of a scope each.

---

## 8. What is still unknown

- **The shipped mask.** Two revisions circulate — `C025913-38` and `C398739-001A`, the second the "good DMA" of the community's hard-disk folklore — and nothing in the record says which sits on this board or whether the difference is electrical or logical [27].
- **The power-on state.** With no reset pin, the chip's mode word and FIFOs are defined only by the previous write; §7.6 is the measurement.
- **The select's width**, and therefore whether `$FF8600/02/08/0A/0C/0E` are aliases (§3.5, §7.1).
- **How the direction reaches U400** (§4.4): a latched copy of `$FF8606`, or a net the symbol does not name.
- **The priority between `FDRQ` and `HDRQ`** if both assert. Nothing in the record ranks them, and ADR 0007 forbids inventing a ranking; mode bit 7 gates one of them, so the interesting case is a mode write landing between two requests — a labelled `unknown` with §7.4's scope as the probe.
- **The silicon FIFO depth**, and whether the write direction's 32-byte prefill (§4.6) is one operation or two.
- **`$FF8604`'s high byte** on the device window (§5.8).
- **What the two reserved mode bits would do** (0 and 5) and whether bit 6 is ignored at the pin level or only in the models [23].
- **The ACK timing at a sector boundary**, where the FIFO's flush and the device's last request meet: `DRQ` is measurably **shorter on the last two bytes of a sector**, which is the one timing irregularity the field literature records and no document explains [22].

**Not modelled — by ruling or by the record, not by doubt:**

- **The sound channel.** Out of this chip (§4.7); the campaign's three-ended finding stands.
- **The ACSI protocol above the pin level.** Command blocks, device numbers, AHDI semantics: software and devices, not silicon; the model needs the eight data pins and the four control lines.
- **The analogue and mechanical side** — head, spindle, pre-compensation, the drive's own timing: the WD1772's and the drive's, per the study rule that where the mechanism lives decides whose finding it is.

---

## 9. Licence position

`documented` No image is vendored and no proprietary drawing is reproduced: the service manual's symbol is cited through the repository's own transcription, Atari's guide is quoted in short passages with the page context (a 1991 vendor manual for programmers, cited by URL, not reproduced), and the two field articles are referenced by URL [20][21][23].
`derived` The two kinds of borrowing are the same the neighbouring studies allowed themselves: transcriptions of published statements (the register map, the mode bits, the quirks, the programming order) and measurements (the burst's clock count, the block arithmetic, the frame budget) — each cited, none reproduced as a figure.
`derived` **No sheet was drawn for this chip**, and the reason is a rule rather than an omission: the only structure the record gives is the pin census and the register pair, both of which are tables (§3.1, §3.5), and anything beyond them — a block diagram of a FIFO with no drawing behind it — would be the outrun-the-evidence failure the 68000 study warns about.

---

## Sources

[2] https://www.chzsoft.de/asic-web/ASICs-new.zip — ASICs-new.zip — recovered Atari CAD archive (Zietz + fpgaarcade)
[4] https://siliconpr0n.org/archive/doku.php?do=search&id=atari — siliconpr0n archive — search result for 'atari'
[5] https://siliconpr0n.org/archive/doku.php?id=furrtek:atari:c301719-001 — siliconpr0n — furrtek:atari:c301719-001 (Lynx Mikey)
[6] http://info-coach.fr/atari/hardware/STE-HW.php — Info-Coach — Atari STE hardware (GST MCU chapter)
[12] http://visual6502.org/images — visual6502 — photographed-parts index (die shots)
[16] https://www.atari-forum.com/viewtopic.php?t=29658 — Atari-Forum — "ST Chipset decap" (ijor's reverse engineering thread)
[20] https://archive.org/stream/ACSI_DMA_Guide_6-28-1991/ACSI_DMA_Guide_6-28-1991_djvu.txt — Atari — ACSI/DMA Integration Guide, 28 June 1991
[21] https://mikrosk.github.io/doitarchive/doit_st/0f0a.htm — mikrosk DOIT archive §15.10 — "The Atari DMA Port"
[22] https://atari.8bitchip.info/AcsiDmaExD.html — atari.8bitchip.info — ACSI_DMA extra info (measured handshake timings)
[23] http://info-coach.fr/atari/documents/_mydoc/FD-HD_Programming.pdf — DrCoolZic — Atari ST FD/HD Programming (v1.2)
[24] https://github.com/mamedev/mame/blob/mame0289/src/mame/atari/stmmu.cpp — MAME — `st_mmu_device` (the DMA register block and FIFO)
[25] https://github.com/mamedev/mame/blob/mame0289/src/mame/atari/atarist.cpp — MAME — `atarist.cpp` (FDC `drq` into the MMU, `intrq` to MFP `i5`)
[26] https://github.com/MiSTer-devel/AtariST_MiSTer/blob/master/rtl/dma.v — MiSTer — `rtl/dma.v` (Harbaum/Szombathelyi)
[27] https://www.exxosforum.co.uk/atari/last/DMASG/index.htm — exxos — "LaST Upgrade Part 47: DMA Survival Guide" (revisions `C025913-38` / `C398739-001A`)
[28] http://info-coach.fr/atari/documents/_mydoc/WD1772-JLG.pdf — Western Digital — WD1772 specification (pin functions, `DRQ`/`INTRQ`)
[29] https://github.com/hatari/hatari/blob/master/src/fdc.c — Hatari — `fdc.c` (16-byte FIFO, 32-clock burst, real-STF notes)
[30] https://github.com/mattiasgustavsson/steem-crt/blob/main/steem/disk_dma.cpp — Steem SSE — `dma.cpp` (two FIFOs, `TDma`, no reset pin)
