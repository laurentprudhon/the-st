# The disk path — image model, track, deadlines, registers, drives

STATUS: **design (v1)** — 2026-11-15, ticket #29. Every ruling below is the owner's, quoted from the ticket's
comments; the arithmetic is mine and labelled as such. This document is the deliverable #29 owed; the code is
the build's.

## 1. The image model — sector and track, flux deferred

**Ruled** (owner, 2026-11-15: "*2*"): v1 reads **sector images** (`.st`, `.msa`, `.dim`) and **Pasti `.stx`**.
Flux (`.ipf`, `.scp`, `.hfe`) is a **declared defer**, not a silent gap, with the reversal condition *"a title in
the validation ladder needs it"*.

**Correction — 2026-11-21 (owner: "if .stt is similar to .st we want to add it to the emulate set").** `.stt`
is **added to the emulate set**, but the premise is corrected: it is **not** similar to `.st`, it is in the
**`.stx` family**, and it rides on the STX reader rather than the sector path. Evidence (`counted`, read from the
vendored Steem SSE v4.2.0 R10, not from memory):

- `diskman.cpp:6691–6731` — magic `"STEM"`, version 1, `AllTrackFlags`, `NumTracks`, `NumSides`, then a
  **per-track (4-byte offset, 2-byte length) table**; the loader sets `SectorsPerTrack = 0xff; // Variable`.
  A flat sector dump has none of that.
- `fdc.cpp:1570–1571` — the FDC consumes it as a **per-track byte stream** through the same call as STX:
  `int TrackBytes = disk.GetRawTrackData(side, track); if (TrackBytes) // STT, STX with track image`.

So `.stt` belongs on §2's block-stream track model, next to `.stx`, and costs the STX reader plus a header
parse — **not** the flux/MFM work. It is a small slice of the corpus: 37 PlanetEmu game entries and 11.5 MiB in
TOSEC's `[STT]` bracket, and no host treats it as a first-class format.


The census that bounds it (counted, `research/st-host-registry.md` §2.2): protected containers are **2,104 of
14,001** PlanetEmu game entries (15.0 %); sector-only loses **2,129 (15.2 %)** and the only surviving image of
some titles; `.stx` alone covers **1,444** entries and every preserved-original case in the twenty-title sample,
and it is what Atarimania — the catalogue's spine — actually serves.

**Why the defer is cheap later**: `.ipf` and `.scp` share one architecture — a bit-cell reader plus an MFM
stream — so the flux path is one piece of work for three formats, and the track model below is the layer it sits
on rather than a thing it replaces.

**Correction that came with this ruling**: `atari-st-software/file-formats/01-boot-sector-and-disk-format.md`
described `.ST` as raw MFM with IAM/IDAM, `.MSA` as "monochrome MFM, 16 spt, 4 MB", `.DIM` as side-by-side MFM
and `.STX` as a raw sector dump. All four were wrong; corrected on `main` (`f8a41df`).

## 2. The track — an ordered block stream

**Ruled** (owner: "*1*"): a track is a sequence of typed blocks — `gap(n)`, `sync(A1A1A1)`,
`IDAM(track, side, sector, size, crc)`, `gap`, `DAM + data + crc`, `gap` — with fuzzy masks attached to data
bytes. The FDC walks it with a **byte clock**; the track is circular with an index pulse.

A **sector index** (offset per sector ID) is kept as a **cache over the stream, never as the truth**.

Three reasons, all structural: STX is already a block stream, so a sector list would discard gap blocks the file
carries; the WD1772's **Read Track** command returns gaps and IDAMs, which is a copy from a stream and a
synthesis from a list; and ADR 0008 already gives this chip a DMA byte cadence, so the model and the scheduler
agree instead of needing a second source of truth.

Consequence: the FDC carries a byte position and a rotation/index model, and "sector not found" is a **real
timeout**, not a lookup miss.

## 3. The byte clock and the deadlines

**Ruled** (owner: "*1*"): **the sector period is the only resumable deadline**; byte cadence and command
duration are **arithmetic**.

The arithmetic, all consistent with this ticket's own DCYC figure:

| quantity | value | derivation |
|---|---|---|
| track length | **6,250 bytes** | standard MFM double-density track |
| rotation | 300 RPM = 5 rev/s = **200 ms/rev** | drive spec |
| byte rate | **31,250 B/s → 32 µs/byte** | 6,250 × 5; 256 CPU cycles at 8 MHz |
| sector read | **16.4 ms** | 512 bytes × 32 µs |
| DCYC answers | **312 per frame** | 31,250 ÷ 50 frames = 625 bytes/frame, one answer per 2 bytes |
| seek | `steps × step_rate` | WD1772 rates: **6 / 12 / 2 / 3 ms** per step (corrected 2026-11-28 — the datasheet's `r1 r0` field in order, `research/standard-parts-implementation.md` §4.2) |

So the FDC's byte position is `(now − sector_start) × 31,250 B/s`, computed on demand; a DRQ or a DCYC answer
costs no wake; the chip wakes **once per sector boundary** plus the burst's wakes.

**The six-wake arithmetic, confirmed on paper** (derived, not measured): one sector read is one ownership
transfer — three wakes to acquire `BR`/`BG`/`BGACK`, three to release — so **6 wakes per sector**, and the
512-byte stream between them is DCYC answers, which are computed. Worst case with the disk streaming
continuously: 6 × 9 sectors × 5 rev/s = **270 wakes/s**, against a frame budget measured in milliseconds. The
measurement that would confirm it is a real sector read in the emulator, which belongs to the build.

Rejected, and why: byte-cadence-as-deadline would be **512 wakes per sector** (31,250/s) against a perf chain
currently measuring a 3.56 ms/frame ring; command-duration-as-deadline cannot interrupt a read mid-sector and
invents DRQ timing; two deadlines give one device two sources of truth about which byte is in flight.

> ### Correction — 2026-11-21 (U300 study, `research/dma-controller-implementation.md` §4.2, §4.3)
>
> **"One sector read is one ownership transfer" is wrong, and the six-wake figure with it.** The DMA controller
> buffers **sixteen bytes** and asks the bus for one **burst** of eight words when a FIFO fills
> (`$FF8609`'s counter answers, `RDY` to the GSTMCU's pin 135), so a 512-byte sector is **32 bursts**, not one
> transfer: 6 records each is **192 records a sector**, and continuous streaming costs **39 bursts and 234
> records a frame** (625 bytes ÷ 16 × 6) where `design/crossings.md` §5 carried "~60".
>
> The byte cadence below is unchanged and so is the ruling: the chip does wait on the FDC for the whole 16.4 ms,
> and the sector period stays the only *resumable* deadline — the 32 bursts are a cadence inside it, computed
> from the transfer position rather than scheduled. What changes is the **wake accounting**: the 270 wakes/s
> above is a floor by a factor of 32 (≈ 8 640/s while streaming), and the FDC's 32 µs byte clock is what makes
> the bursts cheap (4 µs of bus time each, §4.2's table).

## 4. The register file — Disk owns it, Sound is a client

**Ruled** (owner: "*1*"): Disk owns `$FF8600`–`$FF860F`; Sound (#26) reads the counters through Disk's
interface. The file lives where the silicon is — `U300` is inside this chip per the partition — so the ticket's
"stated once, here or there, and not twice" is satisfied by the partition itself.

The deadline ruling is what makes it cheap: **the three counters are arithmetic, not state**. Nobody advances
them; they are computed from the transfer position. Sound's access is therefore a *read of a computed value*,
and ownership reduces to "who owns the computation".

| register | kind |
|---|---|
| control, command, sector count, sector number, track, side, DMA address | **value logs** (append-only records of what the CPU wrote) |
| `$FF8609` / `$FF860B` / `$FF860D` (address counters) | **arithmetic** (computed on read) |
| status | **computed** from FDC state + transfer position — so the write-1-to-acknowledge quirks live in the status *read path*, not in a stored flag |

> ### Correction — 2026-11-21 (U300 study, `research/dma-controller-implementation.md` §3.5, §4.4)
>
> **"The file lives where the silicon is" needs a second home, because the silicon has two.** `$FF8604` (the
> device window or the sector count) and `$FF8606` (mode on a write, status on a read) are **U300's** — the chip
> decodes `A1` and `R/W` and nothing else, which is why it can distinguish exactly two registers. The three
> address bytes are **not**: the 24-bit counter is the GSTMCU's (`DMAH/DMAM/DMAL`, sheet 16; only 22 bits
> implemented), decoded by U400 and never seen by U300, which has no address pins and cannot drive a memory
> cycle at all.
>
> The ruling above stands at the *virtual chip* level — both halves are inside Disk's address space and Disk
> owns the computation — but a model that keeps the pointer inside the DMA controller is modelling a pin that
> does not exist, and the SCU has to be told the address and the direction for every burst it performs.

## 5. The drives — literal pins

**Ruled** (owner: "*1*"): literal pins, and let the FDC time out.

- **Unselected drive**: outputs released — `RD` idles high, `IP`/`TR00`/`WPRT` undriven.
- **Selected drive with no image**: motor spins, `IP` at 300 RPM, `TR00` per head position, `WPRT` per the tab,
  `RD` silent → the sector search runs to its timeout and reports **record not found**.

Why: the stub contract is "answer the boot poll without hanging and without a bus fault, and report no disk",
and the only way to know the real design does that is to let the FDC's own timeout produce the error rather
than special-casing the empty drive. A title polling `IP`/`TR00` while waiting for a disk then sees what a real
ST with an empty drive shows.

### 5.1 The stub contract (#10 takes this before the real design exists)

The stub models **no pins**. It answers the boot poll with the FDC's "record not found" status, never asserts
`BERR`, never blocks, and says in its own comment that it is a stub — so a later session does not mistake the
immediate answer for the modelled timeout.

## 6. The loader seam (#12)

**Ruled** (owner: "*1*"): the loader **normalises every format to the block stream**; the FDC has one input
type. `.stx` is read directly; `.st`/`.msa` are expanded into a synthetic block stream using the standard layout
(9/10/11 sectors per track, standard gaps and IDAMs).

For `.st`/`.msa` the gaps are a **fiction, but an honest one** — those formats carry no gap information, so any
gap is a default rather than a loss. The expansion must therefore mark the image **`gaps: synthetic`** versus
**`gaps: as-dumped`**: a protection that inspects inter-sector gaps passes on a real disk and fails on a
synthesised one, and without the flag that failure looks like an emulator bug rather than a property of the
file. It is the same distinction the catalogue carries for renditions (`own-scan` / `archived-scan-degraded`),
so the two layers agree instead of inventing separate vocabularies.

## 7. What this document does not decide

- **The flux path** (`.ipf`, `.scp`, `.hfe`) — deferred with its reversal condition stated in §1.
- **The host file picker** (#12) — this document fixes the machine-side interface only.
- **`BERR` on a valid TOS access** — the stub must never assert it; the real design's behaviour on an invalid
  access is not settled here.
- **The `BR`/`BG`/`BGACK` wiring** — #23 owns the inventory; until it lands every net defaults to tristate,
  which reports a conflict rather than a wrong value.
- **The six-wake confirmation as a measurement** — §3 confirms it arithmetically; a real sector read in the
  emulator is the measurement, and it belongs to the build.

## 8. What would reopen this

| falsifier | reopens |
|---|---|
| a title in the validation ladder needs `.ipf` or `.scp` | §1 — the flux path becomes v1 work |
| a real sector read shows more than 6 wakes per transfer | §3 — the burst's wake accounting |
| a protection fails on a synthesised-gap `.st` that passes on the same game's `.stx` | §6 — the loader's default layout |
| the perf chain shows the disk's arithmetic dominating a frame | §3 — the deadline choice |
