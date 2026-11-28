# Handoff — the drive, the media, and the image formats: what our five readers carry and what they lose

> **Status — executed 2026-11-28.** Study: [`research/floppy-media-and-images.md`](../../research/floppy-media-and-images.md)
> (465 lines; `verify --min-coverage 0.30` green at **47 %**).
>
> - [x] §2's criteria 1–7: the chain and its faults (§3), the geometry and the 9/10/11/12 budget (§4), the
>   format × feature table (§5.1), every container and its physical format (§6), the protection classes (§7.1),
>   the priced deferral (§8), the loader contract and the decision (§10).
> - [x] §7's experiments: the byte budget was run (9 → 5 586, 10 → 6 200, 11 → 6 814 at the standard 614-byte
>   sector; the "11 does not fit" is corrected to "not at the recommended gaps" — Steem's 566-byte layout fits
>   at 6 256); a real `.stx`'s track 0 was range-read (The Pawn 2.1: 9 sectors, flags `0x61`, a **6 283-byte
>   track image**, read times 16 306/16 346 µs); the ten-title sample was read; the preservation community's
>   notes were searched (KFAnalyze, the DM encyclopaedia, Copylock).
> - [x] §8's definition of done: the study with its two tables; `design/disk-path.md` §1, §3, §6.1 and §7
>   updated; the `.STX` body replaced with a dated correction; `design/emulator-limitations.md` §6.1 gains
>   A1–A6. Commits `1674030` and `5dc2953` on `origin/main`, from `/opt/data/the-st-wt/media`; shared tree
>   fast-forwarded.
> - [x] **The decision**: the flux defer is **re-affirmed**, and the deferral is **cheaper than the brief
>   assumed** — every preserved-original title in the ten-title sample has an `.stx`/Pasti master, and STX
>   carries the sample's documented classes (Copylock's long sector, Dungeon Master's fuzzy sector and 247,
>   Turrican's overlap). The watch list is the ten plus the Pasti-only remainder; the trigger is a title whose
>   *only* preserving master is `.ipf`/`.scp`/`.hfe`.

**Goal of the session.** Write `research/floppy-media-and-images.md`: an in-depth study of the *media* side of the
disk path — the physical drive and the 3.5" diskette, the recording formats an Atari ST actually meets, the five
image formats the emulator reads and the four it defers, and — the deliverable that makes it a study rather than a
survey — **a priced table of what the deferral costs**: which copy-protection classes each reader can carry, and
which ones a user of this emulator will simply not be able to run.

This is not a chip study and not a silicon study: the part is finished (`research/standard-parts-implementation.md`
§4), the controller is done (U300's study), and what is left is **what the disk is** and **what our files can say
about it**.

Read first, in this order: `design/disk-path.md` §1 (the v1 set and the flux defer, with its reversal condition),
§2 (the track as an ordered block stream), §3 (the byte clock), §5–§6 (the drives and the loader seam), §7 (what
that file deliberately does not decide); `research/standard-parts-implementation.md` §4 (the WD1772's commands,
timing, `LOST`, force interrupt) and §4.3 (one sector as a timetable); `research/dma-controller-implementation.md`
§4 (the 16-byte FIFO, 32 bursts, `DCYC`); `atari-st-software/file-formats/01-boot-sector-and-disk-format.md` — its
2026-11-15 correction block is the classification this study starts from, and its §".STX Format" body below that
block is stale and will need fixing; `research/st-host-registry.md` §2.2 (the census); and `design/validation.md`
§4's `D3` rung, where the copy-protected images live.

---

## 1. What is already decided, so the session does not re-open it

* **The v1 readers are five, and the ruling is the owner's** (2026-11-15, quoted in `design/disk-path.md` §1):
  **sector images** `.st`, `.msa`, `.dim`, plus **Pasti `.stx`** and — added 2026-11-21 — **`.stt`**, which rides
  on the STX reader because it is a per-track byte stream, not a sector dump.
* **Flux is a declared defer, not a gap**: `.ipf`, `.scp`, `.hfe` are out of v1, with the reversal condition
  *"a title in the validation ladder needs it"*. `.ipf` and `.scp` share one architecture (a bit-cell reader over
  an MFM stream), so the defer is one piece of work for three formats.
* **The census that bounds the loss** (counted, `research/st-host-registry.md` §2.2): protected containers are
  **2 104 of 14 001** PlanetEmu game entries (15.0 %); a sector-only path loses **2 129** entries (15.2 %) and the
  only surviving image of some titles; `.stx` alone covers **1 444** entries and every preserved-original case in
  the twenty-title sample.
* **A track is a block stream, not a sector list** (`design/disk-path.md` §2): `gap(n)`, `sync`, `IDAM`, `DAM +
  data + crc`, with fuzzy masks on data bytes, walked at a **byte clock** of 32 µs (6 250 bytes a revolution at
  300 RPM). "Sector not found" is a real timeout.
* **The FDC's own behaviour is settled**: eleven commands, the 16 µs register re-access, the four-revolution ID
  search, the data-mark window, `LOST`, force interrupt, the 6-revolution motor-on wait.
* **The register split is settled**: `$FF8604`/`$FF8606` are U300's, `$FF8609/B/D` the GSTMCU's 24-bit counter.
* **The loader seam is #12's** and this study does not design it — but it does decide what the loader can be
  *handed*, so §4 of the study must say so in one paragraph.

## 2. Acceptance criteria

A reader who has the study must be able to do these without opening another document:

1. **Walk the physical chain end to end**: spindle → head → flux reversals on the medium → the drive's read
   amplifier → the WD1772's data separator → a byte stream → the byte clock. Naming, for each step, what can go
   wrong there and which of our readers can *express* that fault.
2. **State the geometry and its arithmetic**: 80 tracks a side, two sides, 9 sectors × 512 bytes = 720 KB; 6 250
   bytes a revolution at 250 kbit/s and 300 RPM; and then **why 9 sectors fit, 10 fit tightly and 11 do not** —
   with the byte budget for ID fields, gaps and CRC written out, and what a 10-sector (800 KB) and an 11-sector
   track actually change.
3. **Classify every image format by what it stores** — sectors, track bytes, bit cells, flux — and say for each
   of our five readers exactly which fields of a track it can and cannot hold. That is a table with a row per
   format and a column per feature, and it is the study's core artefact.
4. **Relate the five to every format used for ST emulation**, including the ones we do not read (`.ipf`, `.scp`,
   `.hfe`, and any host-side containers such as `.zip`-wrapped images or `.raw`/`.img`), and say which physical
   format each one actually *is*: FM or MFM, double or high density, 9/10/11/18 sectors, 80/82/83 tracks, whole or
   half tracks.
5. **Name the protection classes and put each one in the table**: duplicate sector IDs in one track, deleted data
   marks, deliberate CRC errors, over-formatted (long) tracks, extra and half tracks, weak/fuzzy bits, flux
   cell-width manipulation (density tricks), data hidden in the gaps, sector sizes other than 512, index-to-index
   timing checks. For each: **which of our readers can carry it, which cannot, and what the user sees when it
   cannot** (a boot failure, a mid-game check, a loader that hangs).
6. **Price the flux deferral**: for each class the sector and STX readers lose, what the bit-cell reader would
   have to implement, and which classes even flux cannot save (e.g. a check that depends on the drive's analogue
   behaviour).
7. **Say what the study would change in the model**: the rows of `design/disk-path.md` §1 and §7 that this study
   moves, the loader contract it implies, and the one decision it puts to the owner (re-affirm the defer with a
   named title list, or schedule the bit-cell reader).

## 3. The evidence, and what each layer can answer

| layer | what it gives | what it cannot |
|---|---|---|
| the media standards (3.5" DD 720 KB, MFM 250 kbit/s, 300 RPM, the ISO/ECMA track layout) | the geometry, the gap sizes, the bit-cell arithmetic | the ST's own variants (10 and 11 sectors, 82 tracks) |
| the WD1772 specification (campaign source 44) | what the controller can *see and do*: address marks, the DAM window, `LOST`, Read Track | what the media holds beyond what the controller reads |
| Atari's FD/HD programming guide (source 23) | the ST-side register semantics, the double-strobe caution, the sector-count register | protection classes: it documents the machine, not the pirates |
| the format specifications themselves (Pasti/STX, CAPS/IPF, SCP, HFE) | what each file can store, field by field — the classification's primary source | which protections exist in the wild |
| the vendored readers (`steemsse-V4.2.0_R10/steem/diskman.cpp`, Hatari's `src/floppies/stx.c` and `ipf_dsk.c`) | how a format is actually decoded, and where a reader gives up on a track | evidence of what real disks hold |
| the census (`research/st-host-registry.md` §2.2) and the ladder (`design/validation.md` `D3`) | how much of the corpus each class costs | which individual title needs which class, unless the sample is read |

## 4. Method rules inherited, plus one

Inherited: every claim labelled, `T1`/`T2`/`T3` where it applies; **the vendor's prose beats every model**; count,
don't copy; a source in a list is not a source read; **search before writing `unknown`**; no tickets for what only
a board can settle.

New: **a file format is a claim about the medium, and the study's job is to test the claim.** For each reader,
state what the format *asserts* about the track (e.g. STX: "these bytes, with these timings and these fuzzy
masks"), then ask what physical feature that assertion cannot express. The loss table is the answer, and it is
only credible if each row names the field the format lacks — not the protection it therefore cannot run.

## 5. Seeds to check, not to copy

* **6 250 bytes a revolution**: 250 kbit/s ÷ 8 = 31 250 B/s, ÷ 5 rev/s = 6 250. Then 9 × (512 + ID + CRC + gaps)
  must be ≤ 6 250 — write the sum out; the classic figures are an 80-byte gap before each ID and a 22-byte gap
  after it in MFM, and the balance is what a 10th sector has to eat.
* **The 10-sector track** (800 KB) is the ST's own second format: it shortens the inter-sector gaps rather than
  the data. If the numbers cannot close without shortening a gap the WD1772 needs, the 10-sector track is
  evidence about the gap, not about the sector.
* **11 sectors and the "long track"**: 11 × 512 = 5 632 data bytes, and the overhead pushes past 6 250 — which is
  exactly why a long-track protection is a *flux* claim: either the track was written at a slightly higher bit
  rate, or the drive's spindle is not 300 RPM when the loader reads it. Both are invisible to a sector image.
* **The sector-size field** is two bits (128/256/512/1024) and the FDC honours it; a protection can use it, and
  most sector images cannot.
* **32 µs a byte = 256 CPU clocks at 8 MHz**; the DMA's 16-byte FIFO is 32 bursts a sector at ~4 µs of bus each.
  Any protection figure that needs more bus time than a revolution holds is wrong, not exotic.
* **The `LOST` bit is a read-side event, and a write-side one too** (a late byte is *written as zero*): a
  protection that depends on the *machine* failing to keep up is a protection our byte-accurate model will
  reproduce naturally — check whether one exists before building anything for it.

## 6. What must stay `unknown`, and where

* The analogue behaviour of a real drive and medium (head alignment, write current, a worn disk): out of reach,
  and out of scope — recorded in `design/emulator-limitations.md`, not modelled.
* Which protection each *individual* title uses, beyond the twenty-title sample: a census question, answerable
  from the corpus itself (the images carry their own evidence), and only worth a pass if the study's table needs
  a title to close a class.
* The exact byte-level layout of the STX container's timing field is documented by its author, but its *reliability*
  across producers is not: a note, not a measurement.

## 7. The experiments that would settle the rest

1. **Synthesise and read back.** Build a track in the model's block form (9 sectors, then 10 with short gaps, then
   a duplicate-ID track, then a bad-CRC track) and read each with the model's `Read Track` and `Read Sector`. What
   the block model cannot express is the loss table's first column; this costs no hardware and settles most of §2.3.
2. **Read a real `.stx`'s track 0 and print its block stream.** The file carries the answer to "does STX store
   gaps, or only sectors?" better than any documentation.
3. **The ladder sample.** Take the ten titles the census marks as preserved-original-only and ask, per title,
   which class it needs — that is the reversal condition made concrete, and it is the study's decision input.
4. **Not an experiment, a search**: before writing "nobody preserves this class", search the preservation
   community's own notes — the campaign's rule, and it has paid every time.

## 8. Definition of done

1. `research/floppy-media-and-images.md`, with the two tables that are the study: **format × feature** and
   **protection class × format (and what the user sees)**; `verify --min-coverage 0.30` green.
2. `design/disk-path.md` §1 and §7 updated: the defer re-affirmed or re-priced, with the named title list from
   §7.3 as its evidence, and the loader paragraph §2.7 asks for.
3. `atari-st-software/file-formats/01-boot-sector-and-disk-format.md`'s stale `.STX` body replaced by the
   correction block's classification (a dated correction, not a silent rewrite).
4. `design/emulator-limitations.md` gains the analogue rows and nothing else; **no tickets**.
5. Commits pushed to `origin main` from a worktree, and the shared tree fast-forwarded.

## 9. Where this sits

It is the **third** session of this stretch: the IKBD closes the per-chip campaign
(`docs/handoffs/ikbd-hd6301-study.md`), the closing audit then asks whether every question in our core is labelled,
and this study answers the one question the audit will name as the largest unpriced gap — **what the model cannot
play, and why**.
