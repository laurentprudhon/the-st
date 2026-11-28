# The drive, the medium and the images

### What our five readers carry, what the flux deferral costs, and the ten titles that price it

*Subject: the 3.5" drive and diskette, the MFM track, the five v1 readers (`.st`, `.msa`, `.dim`, `.stx`, `.stt`) and the three deferred flux containers (`.ipf`, `.scp`, `.hfe`), on the 1040STE's disk path.
Studied 2026-11-28, the third session of this stretch; the controller itself is `research/standard-parts-implementation.md` §4's.*

*Basis: the media standards and the format documents are the primary source; the vendored readers are the test of what a decoder actually honours; the corpus census bounds the loss.
Labels: `documented`, `derived`, `inferred`, `unknown`; T1 hardware-captured, T2 hardware-corrected, T3 emulator-derived.
Companion pages: `design/disk-path.md` (the image model, the track, the byte clock, the drives, the loader seam), `research/standard-parts-implementation.md` §4 (the WD1772), `research/dma-controller-implementation.md` §4 (the FIFO and the burst), `atari-st-software/file-formats/01-boot-sector-and-disk-format.md` (the corrected container classification), `research/st-download-sources.md` and `research/st-host-registry.md` (the census), `design/validation.md` §4 (`D3`).
Sources are cited by number; the ledger's ids are stable across the campaign's studies.*

---

## 1. Executive summary

| question | answer | § |
|---|---|---|
| What is the medium? | A 3.5" double-density diskette: 135 TPI, **80 cylinders**, 300 RPM, MFM at **250 kbit/s of data** (500 kbit/s of channel cells), **6 250 bytes a revolution**, 9 × 512 × 2 × 80 = 720 KB.[44][52] | 4 |
| Why do 9 sectors fit, 10 fit tightly, and 11 not? | One 512-byte sector costs **614 bytes** with the standard gaps. 9 × 614 + 60 = **5 586**, leaving 664; 10 × 614 + 60 = **6 200**, leaving 50; 11 × 614 is **6 814**, 564 over. Eleven fits only if the PLL lock-up shrinks from 12 bytes to 3 and the post-data gap from 40 to 1 — **6 226 + 30 = 6 256**, which is Steem's own 11-sector layout and is *below* the WD1772's recommended minimum gaps; 12 needs a longer track.[44][60][61] | 4.2–4.3 |
| What do the five readers carry? | `.st`/`.msa`/`.dim` store **512-byte sectors and nothing else** — gaps, ID fields and CRCs are synthesised. `.stx` stores per-track bytes, each sector's ID/CRC/FDC status, an optional read-track image, a fuzzy-bit mask and (revision 2) per-sector and per-16-byte timing. `.stt` stores per-track bytes and sector IDs, with **no** fuzzy mask, timing or FDC status.[46][47][56][61] | 5 |
| What does the flux deferral cost? | Only the classes **STX cannot express**: sub-16-byte timing, timing in gaps and ID fields, flux that the FDC re-decodes rather than stores as bytes, and per-revolution variation. The sample's documented classes — Copylock's long sector, Dungeon Master's fuzzy sector, Turrican's overlap — are all STX-carryable.[51][52][53] | 6–8 |
| What is lost today? | A sector-only path cannot open **2 129 of 14 001** PlanetEmu game entries (15.2 %); protected containers are **2 104** (15.0 %), of which `.stx` is **1 444**, `.ipf` **413**, `.scp` **178**, `.hfe` **4**, `.dim`+`.stt` **65** (`research/st-download-sources.md`, `research/st-host-registry.md` §2.2). | 9 |
| What does that mean for the ten preserved-original titles? | Every one has an **STX** rendition — the sweep's own table (`research/st-download-sources.md`) for nine of them, and Atarimania's bundle note for Leisure Suit Larry 1 ("Pasti, SCP and IPF dumps"). None needs flux to be *represented*; the classes that would need it are not in the sample.[54] | 9 |
| What is the decision? | **Re-affirm the defer**, with the ten-title list as its evidence and a named watch list. The reversal condition fires only when a ladder title's *only* preserving master is `.ipf`/`.scp`/`.hfe`; no sample title is in that state (derived, §9.3). | 9.3 |
| What does the study change in the model? | The **32 µs byte clock becomes the nominal default**, overridden per track or per sector by a preserved image's timing; the loader gains a provenance contract (gaps as-dumped vs synthetic, FDC status from-image vs recomputed, fuzzy mask present or not). `design/disk-path.md` §1, §3, §6 and §7 move.[46][60] | 10 |

---

## 2. The evidence, and what each layer can answer

| layer | what it gives | what it cannot |
|---|---|---|
| the media standards (3.5" DD, MFM, 300 RPM, the ISO/ECMA track layout) | the geometry, the gap sizes, the bit-cell arithmetic | the ST's own variants (10/11/12 sectors, 82 tracks) |
| the WD1772 specification[44] | what the controller can *see and do*: address marks, the DAM window, `LOST`, force interrupt, the four-revolution search | what the medium holds beyond what the controller reads |
| Atari's FD/HD programming guide[23] | the ST-side register semantics, the double-strobe caution, the sector-count register | protection classes: it documents the machine, not the pirates |
| the format documents (Pasti[46], Atari image formats[47], IPF[48], HFE[49], SCP[50]) | what each file can store, field by field — the classification's primary source | which protections exist in the wild |
| the vendored readers (Steem SSE's `diskman.cpp`/`fdc.cpp`[61], Hatari's `stx.c`/`ipf_dsk.c`/`scp.c`[56][57][58]) | how a format is actually decoded, and where a reader gives up on a track | evidence of what real disks hold |
| the preservation analyses (KFAnalyze[51], the DM encyclopaedia[52], the Copylock record[53]) | per-title, per-track measurements of the classes | a census of titles |
| the census (`research/st-host-registry.md` §2.2, `research/st-download-sources.md`) and the ladder (`design/validation.md` `D3`) | how much of the corpus each class costs | which title needs which class, unless the sample is read |

### 2.1 What this study does *not* use, and why

`documented` Three internal pages overlap this subject and are not sources for it, because their numbers contradict the vendored code and the documents:

- `atari-st-hardware/floppy/01-physical-format.md` calls the ST's DD data rate **500 kbit/s** (it is 250 kbit/s of data; 500 kbit/s is the channel/cell rate), gives a track of **~10 500–11 000 bytes** (it is 6 250), a **32-byte** DMA FIFO (it is 16), and "raw flux at 100 kbit/s" (the drive's `RD` is flux pulses; there is no 100 kbit/s rate).[44][60]
- `atari-st-hardware/floppy/03-copy-protection.md` repeats the 500 kbit/s error and gives a class list whose individual claims are unlabelled; its Dungeon Master sentence ("sector 247") is close but imprecise — the game numbers its **eighth** sector 247.[52]
- `atari-st-software/file-formats/01-boot-sector-and-disk-format.md` §5's `.STX` body is corrected in place by this session, as the handoff asks; its correction block is otherwise the classification this study starts from.[46]

`documented` They are recorded here so a later session does not "correct" the study against them. They are not edited — the only in-place correction this session owes is the `.STX` body and its suffix rows in the file-formats page (§10.5).

---

## 3. The physical chain, end to end

`documented` The chain is five parts and two signals: **spindle → head → flux reversals on the medium → the drive's read amplifier and comparator → `RD` pulses → the WD1772's data separator → a byte stream → the FDC's byte clock**.[44][41]
The WD1772 has no analogue front end of its own: the drive's PCB amplifies the head signal and turns it into pulses, and the controller's internal DPLL recovers the bit cells from those pulses.[44]

| # | stage | what it does | what can go wrong there | can a reader express it? |
|---|---|---|---|---|
| 1 | spindle and hub | 300 RPM = 5 rev/s = 200 ms a revolution; the index pulse once a revolution | speed error, wow and flutter, a slipping hub, a slow start | SCP's per-revolution index time and HFE's bitrate opcodes carry a speed; an STX whole-track image carries a *length*; a sector image carries nothing.[50][49][46] |
| 2 | positioner and head | 80 cylinders at 135 TPI, two sides, one head a side | misalignment, wrong track, a clogged or worn head, azimuth error | only the *number* of the track is in any container; the analogue error is in none (see §11).[48] |
| 3 | the medium | cobalt-doped γ-Fe₂O₃, coercivity, written transitions | weak bits, dropout, wear, a marginal write current | the **symptom** is expressible — STX's fuzzy mask, IPF's fuzzy data descriptor, HFE v2's `RAND`, SCP's raw flux — the cause is not.[46][48][49][50] |
| 4 | read amplifier and comparator | amplifies, filters, slices the head signal into pulses | noise, gain drift, a dropout | not in any format; it is the drive's analogue state |
| 5 | `RD` | a pulse per flux reversal | missing or extra pulses, jitter | SCP/ HFE/IPF carry pulses or cells; STX carries the bytes the FDC made of them, not the pulses.[50][49][48] |
| 6 | data separator (DPLL) | a 2 µs inspection window; a pulse in the window is a 1 cell, no pulse a 0 cell; the PLL slides the window | wrong cell width, density change, PLL capture and loss | IPF describes a density; SCP/HFE carry the cells; STX carries only a per-sector (or per-16-byte) timing table.[44][46][48][50] |
| 7 | address-mark and DAM detector | finds `A1A1A1` sync, `FE` IDAM, `FB`/`F8` DAM; the DAM must be within **43 bytes** of the ID CRC in DD | a missed or false sync, a DAM outside the window, a deleted mark | STX's track image carries the bytes and its FDC status carries CRC/RNF/deleted; a sector image synthesises a clean mark.[46][44] |
| 8 | CRC | CRC-CCITT over the ID or the data field | a deliberate error | STX stores the FDC's CRC-error flag; IPF/SCP/HFE can carry the bad bytes; a sector image recomputes a good CRC.[46][48] |
| 9 | byte stream and `DRQ` | one byte per **32 µs** nominal, `DRQ` to the DMA chip | a late read sets `LOST`; a late write writes a zero and does not stop | **no image can express it**: it is the CPU/DMA model's timing, not the file's.[44][23] |
| 10 | index | 5 Hz; data may sit across it | data under or over the index | STX's track image, IPF's block order, SCP's index time; a sector image cannot.[46][48][50] |

`derived` The table's last column is the study's method rule made concrete: a format can carry a fault only if it has a **field** for that fault. "Supports copy protection" is not a field.

---

## 4. The geometry, written out

### 4.1 The constants, and the one that is not exact

| quantity | value | derivation |
|---|---|---|
| rotation | 300 RPM = 5 rev/s = **200 ms/rev** | drive specification[52] |
| data rate, DD | **250 kbit/s** = 31 250 B/s | MFM, one data bit per two channel cells[44] |
| channel (cell) rate | **500 kbit/s**; one cell = **2 µs** | 250 kbit/s × 2[52] |
| byte | **32 µs** nominal = 256 CPU clocks at 8 MHz | 8 data bits × 4 µs[44] |
| track | **6 250 bytes** | 31 250 B/s ÷ 5 rev/s[44] |
| sector payload | 512 bytes = **16.384 ms** | 512 × 32 µs[44] |
| disk, 9 spt, two sides, 80 tracks | **737 280 B = 720 KB** | 9 × 512 × 2 × 80 |
| disk, 10 spt | **819 200 B = 800 KB** | 10 × 512 × 2 × 80 |
| disk, 11 spt | **901 120 B = 880 KB** | 11 × 512 × 2 × 80 |
| HD, 18 spt | **1 474 560 B = 1.44 MB**; 500 kbit/s, 12 500 B/rev | 18 × 512 × 2 × 80[44] |

`documented` The *board's* clock is 8.010613 MHz, 0.133 % over the WD1772's 8 MHz ±0.1 %, so the real bit rate is **250.3 kbit/s** and a byte is **31.96 µs**.[44][41]
`derived` MFM is self-clocked, so the 0.13 % is absorbed by the data separator; the design's 32 µs is the nominal arithmetic and the model should keep it as the default, not pretend the oscillator is exact.[44]

### 4.2 One sector, in bytes

`documented` The WD1772's recommended System/34 layout, as the reference implementation builds it, is:[44][60]

| field | bytes | content |
|---|---|---|
| pre-ID gap | 12 | `00` (PLL lock-up) |
| ID sync | 3 | `A1 A1 A1` (written as `F5 F5 F5`) |
| ID address mark | 1 | `FE` |
| ID field | 4 | track, side, sector, size code |
| ID CRC | 2 | CRC-CCITT over sync + IDAM + ID |
| post-ID gap | 22 | `4E` |
| pre-data gap | 12 | `00` |
| data sync | 3 | `A1 A1 A1` |
| data address mark | 1 | `FB` (or `F8`, deleted) |
| data | 512 | the payload |
| data CRC | 2 | CRC-CCITT over sync + DAM + data |
| post-data gap | 40 | `4E` |
| **total** | **614** | |

`documented` The WD177x specification states the *minimum* variations: the `4E` part of the post-ID gap must be **22 bytes**, the `00` part **12 bytes**; the `4E` part after the data must be at least **24 bytes** and the `00` part at least **8**; the leading and trailing gaps at least **2**.[44]
`derived` The ST's own formatter is looser at the index (60 `4E` before the first ID) and uses 40 after the data; that is the layout Hatari, IPF's Atari example and Steem all build.[60][48][61]
`derived` The "80-byte gap before each ID" sometimes quoted for MFM is the IBM 3740/System 34 **gap 4a at the index**, not a per-sector gap: the ST's index gap is 60 `4E`, and every ID is preceded by 12 `00` (the PLL lock-up) plus the 3-byte sync.[44][60]

### 4.3 Why 9 fit, 10 fit tightly, and 11 only by breaking the recommendation

| sectors | bytes at 614 a sector | + index gap | slack in 6 250 | verdict |
|---|---|---|---|---|
| 9 | 5 526 | 60 → **5 586** | **664** | the standard 720 KB track; the slack is the pre-index gap |
| 10 | 6 140 | 60 → **6 200** | **50** | the ST's 800 KB track; fits, with a 50-byte tail |
| 11 | 6 754 | 60 → **6 814** | **−564** | does **not** fit at 614 |
| 12 | 7 368 | 60 → **7 428** | **−1 178** | a long track, or 256-byte sectors |

`documented` Eleven becomes possible only by shrinking the sector: Steem's 11-sector layout uses a **3-byte** `00` lock-up (not 12) and a **1-byte** post-data gap (not 40), giving **566** a sector, **6 226** for eleven, and a **30-byte** index gap — 6 256 bytes of track.[61]
`derived` That is below the WD177x's own recommended minimums (12-byte lock-up, 24-byte post-data `4E`, 2-byte gaps): at the *recommended* minimums a 512-byte sector is **570** bytes and eleven are **6 270**, twenty bytes over the revolution.[44]
So the brief's "11 × 512 pushes past 6 250" is right at the recommended gaps and wrong as a law: an 11-sector track is a **non-standard byte stream that still fits in 200 ms**, which is why an STX/STT track image can carry it without a bit-cell reader. It is not, by itself, a long track.

`documented` A **long track** is different: it has *more than 6 250 bytes in one revolution*, which needs either smaller bit cells (a higher bit rate) or a slower spindle. Neither is a property of a byte list; it is a property of the cells' timing.[48][53]
`documented` Dungeon Master is the clean 10-sector case: a single-sided, 80-track, **10-sector** disk, 400 KB, so that the game fits on the cheaper single-sided media; the 10-sector format is not itself the protection.[52]
`documented` Theme Park Mystery is the clean 12-sector case: **12 sectors on every track**, with two overlapping sectors at the end reading as data over the index and fuzzy bits — a layout that cannot close at 250 kbit/s without a longer track.[51]

### 4.4 Sector sizes, extra tracks, and what changes

`derived` The ID field's size code selects 128, 256, 512 or 1024 bytes (`128 << code`); the FDC honours codes 0–3 and the readers that store the code can carry a non-standard value even if the FDC then masks it.[44][46]
At the same 614-byte overhead, the maximum sectors a track holds are:

| payload | bytes a sector | most sectors in 6 250 | capacity, two sides × 80 tracks |
|---|---|---|---|
| 128 | 230 | 26 (27 is 6 270) | 520 KB |
| 256 | 358 | 17 (18 is 6 504) | 680 KB |
| 512 | 614 | 10 (the 50-byte tail) | 800 KB at 10 spt |
| 1024 | 1 126 | 5 (6 is 6 816) | 800 KB at 5 spt |

`derived` These are arithmetic, not formats: a `.st`/`.msa`/`.dim` reader assumes 512 and a fixed sectors-per-track, so a 256- or 1024-byte track is outside them; an `.stx` carries `ID_Size` and a per-sector data length, and an `.stt` carries the length index and data length.[46][47]

`documented` Extra tracks are ordinary: the IPF analyzer supports **84 cylinders (0–83)**, the STX track number is **7 bits plus a side bit**, and the drive can step past track 79.[48][46]
`derived` A `.st` can hold 82 or 83 tracks only if its size says so (82 × 2 × 9 × 512 = 755 712 B); `.msa`/`.dim` declare the end track; `.stx`/`.stt` declare each track. A 40-track disk is a different medium and a different drive — **half tracks are not evidenced on the ST** and stay `unknown` (§11).[46][47]

`documented` HD changes the clock, not the layout: 500 kbit/s of data, 12 500 bytes a revolution, 18 × 512; the WD1772's 500 kbit/s maximum is exactly this mode.[44]
`derived` The five readers' HD support is uneven: `.dim` has a DD/HD header bit; `.st` has only its size; `.msa` has sectors-per-track and sides; `.stx`/`.stt` carry the track and the ID size, so HD is implicit in the data. A 1.44 MB `.st` is a valid 18-sector image, but its gaps are synthesised like any other sector image.[47]

---

## 5. The five readers, field by field

### 5.1 The core table: format × feature

`documented` Rows are the five v1 readers and the three deferred containers; columns are the fields a protection can need. **`—` means the format has no field for it**, not that the reader is buggy.[46][47][48][49][50]

| format | sector data | ID fields + CRC | FDC status (CRC/RNF/deleted) | gaps / read-track bytes | size ≠ 512 | duplicate / non-standard IDs | fuzzy / weak bits | cell-width or timing | track ≠ 6 250 B | extra tracks / geometry | revolutions |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `.st` | yes | — (synthesised) | — | — (synthesised) | — | — | — | — | — | size only | 1 |
| `.msa` | yes (RLE) | — (synthesised) | — | — (synthesised) | — | — | — | — | — | header: spt, sides, start/end | 1 |
| `.dim` | yes | — (synthesised) | — | — (synthesised) | — (512 assumed) | — | — | — | — | header: spt, sides, start/end, DD/HD | 1 |
| `.stx` | yes | yes | yes (bits 3/4/5) | **optional track image** | yes (`ID_Size`) | yes | **yes (mask)** | **yes (`ReadTime` + 16-byte table)** | yes (`trackLength`) | per track (7-bit track + side) | 1 |
| `.stt` | yes | yes (sector section) | — (no flags) | **raw section** | yes (length index) | yes | — | — | yes (`raw` length) | per track (up to 86) | 1 |
| `.ipf` | yes (blocks) | yes (descriptors) | yes (EDC description) | yes (`GAP` descriptors) | yes | yes | yes (`FZD`) | yes (density description) | yes (long/short track) | 84 cylinders | 1 |
| `.scp` | via flux | via decode | via decode | yes | yes | yes | yes (raw flux) | yes (ns transitions) | yes (index time) | up to 167 tracks | **up to 5** |
| `.hfe` v1 | via cells | via decode | via decode | yes | yes | yes | — | fixed bitrate | yes (`track_len`) | per track | 1 |
| `.hfe` v2/v3 | via cells | via decode | via decode | yes | yes | yes | **yes (`RAND`)** | **yes (`SET BITRATE`)** | yes | per track | 1 |

`derived` The table's shape is the study's answer to "what does the deferral cost": the sector formats lose **every** field; `.stt` loses the three that make a protected track *time* correctly; `.stx` loses only what is below its granularity (§8).

### 5.2 What each reader actually does

`documented` **`.st`** is a headerless concatenation of sectors in logical order (track, side, sector); its geometry is inferred from the file size (720 KB, 800 KB, 880 KB, 1.44 MB), and it has no field for anything else.[47][56]
`documented` **`.msa`** adds a five-word header (magic `$0E0F`, sectors per track, sides, first track, last track) and per-track RLE with `$E5` as the marker; uncompressed tracks are 512 × spt bytes.[47][56]
`documented` **`.dim`** adds a 32-byte header: magic `$4242`, a "detected" byte, an "all sectors / used sectors" byte, sides, sectors per track, first/last track, a DD/HD byte, and a copy of the BPB; Hatari strips it and treats the rest as `.st`.[47][56]
`documented` **`.stx`** is Pasti: a 16-byte file header (`RSY\0`, version 3, tool, reserved, track count, revision, reserved), then per track a 16-byte descriptor (`recordSize`, `fuzzyCount`, `sectorCount`, `trackFlags`, `trackLength`, track/side, type), then `sectorCount` × 16-byte sector descriptors (`dataOffset`, `bitPosition`, `readTime`, ID 6 bytes, `fdcFlags`, reserved), then the fuzzy mask, then the data record.[46][59]
`documented` The track flags are **bit 0 sector descriptors, bit 6 track image, bit 7 a 4-byte image header** (sync offset + size); the sector flags are **bit 7 fuzzy, bit 5 deleted, bit 4 RNF, bit 3 CRC error, bit 0 intra-sector bit-width variation**.[46][59]
`documented` The track image is **what the WD1772's Read Track returns** — gaps, IDAMs, DAMs, data — so an STX can carry the gaps when the imaging tool wrote one; a sector-only STX does not.[46][56]
`documented` The fuzzy mask is one byte per fuzzy sector byte; the mask is `~(read1 ^ read2)` and the reader returns `(data & mask) | (rand & ~mask)`, which is why a check that reads five times sees five different values.[46][56]
`documented` Revision 2's timing record stores two big-endian bytes per **16 bytes** of a sector, each in units of 32 FDC cycles + 28 (nominal 0x7F/0x80 per 16-byte block); Hatari also honours a whole-sector `readTime`, with 0 meaning the standard 32 µs a byte.[46][56]
`documented` **`.stt`** is STEEM's track container: `STEM`, version 1, flags, all-track flags, track count (≤86), sides, then per track a 4-byte offset and 2-byte length; each track is `TRCK` + a data-flags word and either a **sector section** (ID bytes, CRC, data offset/length) or a **raw section** (a byte stream), or both.[47][61]
`documented` `.stt` has no fuzzy mask, no timing record and no per-sector FDC status; its sector section carries an ID and a data offset, and its raw section carries the bytes, but not whether the FDC would report CRC, RNF or deleted.[47]

### 5.3 The deferred readers

`documented` **`.ipf`** is not a capture but a **description of the master**: CAPS/INFO/IMGE/DATA records, a format/track descriptor, block and data descriptors, and data samples; the analyzer records *how* the track was mastered (MFM, gaps, long/short tracks, fuzzy data), not the measured timings.[48]
`documented` **`.scp`** is raw flux: a 16-byte header, a track-offset table, per track a `TRK` header and 12-byte per-revolution descriptors (index time, length, sample count), then 16-bit little-endian deltas at the capture sample rate; up to five revolutions a track.[50]
`documented` **`.hfe`** is a cell-state bitstream: a 512-byte header (`HXCPICFE`), a track offset LUT, then per-track 512-byte blocks interleaving side 0 and side 1; v2/v3 add opcodes for `SET BITRATE`, `RANDOM`, `SKIP BITS`, `SET INDEX`.[49]

---

## 6. Every format in the ST world, and what it physically is

`documented` The corpus counts by extension; the loader must decide by **magic and structure**, because an extension is not a format (`research/st-download-sources.md` §0–§2).

| container | what it physically is | the ST physical format it usually holds |
|---|---|---|
| `.st` | headerless 512-byte sector dump | MFM DD, 9/10/11 spt, 80 tracks, two sides; HD by size |
| `.msa` | RLE sector dump + geometry | MFM DD, 9–11 spt, 512; not "16 spt / 4 MB" |
| `.dim` | sector dump + 32-byte geometry | MFM DD or HD, 9–11 spt (18 at HD), 512 |
| `.stx` | track-level container (Pasti) | whatever the disk was: FM/MFM, DD/HD, 9/10/11/12 spt, 80/82/83 tracks, whole tracks |
| `.stt` | track-level container (STEEM) | same as `.stx`, without timing/fuzzy |
| `.ipf` | master description (CAPS/SPS) | FM/MFM/GCR, DD/HD, long/short tracks, 84 cylinders |
| `.scp` | raw flux, per revolution | any encoding, any density, any track length |
| `.hfe` | cell-state bitstream | FM/MFM/GCR/custom, fixed or variable bitrate (v2/v3) |
| `.raw` | **ambiguous**: Kryoflux's raw flux stream is not self-describing | flux, if it is Kryoflux; otherwise unknown |
| `.img` | **ambiguous**: a sector dump with an optional 8-byte header, or a flux dump | must be inspected; never assumed |
| `.zip`/`.7z`/`.gz` | wrappers | any of the above inside |

`documented` A `.stx` is not "an ST with headers": it is a **per-track** format, so one track can be 9 sectors, the next 12, one FM and the next MFM; that is the point of the block stream in `design/disk-path.md` §2.[46][48]
`derived` The host containers are not media claims: a `.zip` says nothing about the disk, and `.raw`/`.img` are names two different tools use for two different things. The loader's first job is therefore **identification**, and its second is the block stream.

---

## 7. The protection classes, and what the user sees

### 7.1 The class table

`documented` The classes below are the ones the preservation record actually names, each with its physical mechanism; the columns say whether a reader has a field for it, not whether the title is "supported".[51][52][53][62]

| class | what the disk holds | sector (`.st`/`.msa`/`.dim`) | `.stx` | `.stt` | flux (`.ipf`/`.scp`/`.hfe`) | what the user sees when the reader cannot carry it |
|---|---|---|---|---|---|---|
| duplicate sector IDs | two sectors with the same track/side/sector, different data | **no** — one logical sector | **yes** | **yes** | yes | the check reads the first match and gets the wrong data: a boot-time reset or "insert original disk" |
| deleted data mark | `F8` instead of `FB` | **no** | **yes** (bit 5) | raw only | yes | the check sees a normal sector where it expected a deleted one: the copy branch runs |
| deliberate CRC error | bad ID or data CRC | **no** — the reader recomputes | **yes** (bit 3) | **no** (no status) | yes | the check expects an error and gets none: refusal at the loader |
| RNF / missing sector | an ID with no data field | **no** | **yes** (bit 4) | length 0 only | yes | the check waits for a sector that never arrives: a hang or a "disk error" |
| non-standard sector number | 0, 247, >spt | **no** | **yes** (ID byte) | **yes** (ID byte) | yes | the loader seeks a sector the image does not have: a loop or a timeout |
| sector size ≠ 512 | 128/256/1024, or a bogus code | **no** | **yes** (`ID_Size`) | **yes** (length index) | yes | the transfer length is wrong: a corrupted load or a hang |
| over-formatted / long track | > 6 250 bytes a revolution | **no** | **yes** (track image length + timing) | **yes** (raw length) | yes | the check compares read times and sees a nominal track: the copy branch runs |
| extra tracks (82/83) | data beyond track 79 | **no** (unless the file is sized) | **yes** | **yes** | yes | the loader never reaches the track: a missing file or a boot refusal |
| data in gaps / under index / overlap | bytes the sector path never sees | **no** | **yes** (track image) | **yes** (raw) | yes | `Read Track` returns clean gaps: the check fails |
| weak / fuzzy bits | bits at the DPLL window's edge | **no** | **yes** (mask) | **no** | yes | five reads return the same bytes: the check concludes "copy" |
| cell-width variation | a sector written at 4.2 µs cells (Copylock) or a four-segment Speedlock sector | **no** | **yes** (timing table, 16-byte granularity) | **no** | yes | the sector read time is nominal: the comparison fails |
| sector read-time comparison | a check on one sector's time against another's | **no** | **yes** (read time / timing table) | **no** | yes | the comparison sees two nominal sectors: the copy branch runs |
| index-to-index timing | a check on the drive's own revolution | **no** | **approx.** (track length × byte time) | **approx.** (raw length) | **yes** (SCP index time / HFE bitrate) | a nominal 300 RPM model passes or fails on its own unless the reader takes the period from the image's length/index time |
| interleave / skew | a physical sector order the check knows | **no** — logical order only | **yes** (order + track image) | **yes** (order) | yes | `Read Track` returns the logical order: the check fails |
| mixed FM/MFM on one track | a density switch mid-track | **no** | **no** (bytes are decoded) | **no** | yes | the first wrong-density sector is RNF: a hang |
| half tracks | tracks at half-step positions | **no** | **no** | **no** | maybe (IPF/SCP) | not evidenced on the ST; stays `unknown` (§11) |
| write splice / erase | the undefined bytes where a sector was rewritten | **no** | **no** (it stores what Read Track returned) | **no** | **partly**: IPF excludes modified disks; SCP captures them | not a title check; a data-integrity effect |

### 7.2 The documented exemplars

`documented` **Dungeon Master / Chaos Strikes Back** (FTL, 1987): single-sided, 80 tracks, **10 sectors** of 512 (400 KB); on track 0 the **eighth** sector's ID says **247** (`$F7`), a value the WD1772's own format command treats as a command and cannot write; **sector 7** has fuzzy bits and a bad data CRC, verified several times during play.[52]
`documented` **Populous** (Bullfrog/EA, 1989): the Rob Northen Copylock **long sector** — track 0, sector 6 takes **17 197 µs** against a normal **16 480 µs** (4.2 % longer), because the clock changes from 4 µs to 4.2 µs inside that sector; the sector also carries a CRC error.[51][53][62]
`documented` **Xenon 2** (Bitmap Brothers, 1989): the Copylock "internal" type, where the serial key is passed in `D0` and the loader decrypts through the 68000's trace vector.[53]
`documented` **Turrican** (Rainbow Arts, 1990): track 7 has **overlapping sectors** 0, 16 and 1 (a sector inside a sector), the first two with fuzzy bytes, and a **no-flux area** that violates MFM and makes the decoder produce different bytes on different reads.[51]
`documented` **Theme Park Mystery** (Image Works): **12 sectors on every track**, two overlapping at the end reading as data over the index, with fuzzy bits; the "data over index" is fake because there is no room at the start of the track.[51]
`documented` **Colorado** (Rainbow Arts): the Macrodos/Speedlock **four-segment** intra-sector bit-rate variation.[51]
`documented` **D50 Editor** (DrTs): a half-formatted sector whose end is pseudo-random flux with timing violations, driving the DPLL "crazy"; the violations are symmetric so the track length is unchanged.[51]
`documented` **The Pawn** (Magnetic Scrolls, 1986): a standard single-sided 80-track MFM disk whose protection is a **non-standard track/sector number** (`$F7`) that cannot be written on an ST, the same mechanism as Dungeon Master's 247.[55]

### 7.3 What the user actually sees

`derived` The symptom depends on **where the check sits**, not on the class:

- a **boot-sector or loader check** that fails usually resets the machine or prints "insert original disk"; the title never starts.
- a **mid-load check** usually hangs in a sector-search loop — the FDC is waiting for a sector the image does not have, so the loader spins until the four-revolution timeout and retries.
- an **in-game check** can be silent for a while: a key item disappears, a level refuses to load, or the game resets at a later disk access (the Copylock record's Hook example is a mug that vanishes).[53]
- a **timing check** (Copylock's long sector, an index-to-index comparison) is the quietest: the data loads, and the title decides later that the disk is a copy.

`derived` This is why the loss table names the **field** and not the symptom: the same missing field can produce a reset, a hang or a silent pass depending on the loader's code.

---

## 8. The flux deferral, priced

### 8.1 What STX actually loses, and what STT loses on top

`derived` STX is much closer to a flux reader than the phrase "track image" suggests. It carries gaps (track image), duplicate and non-standard IDs, deleted DAMs, CRC and RNF flags, non-512 sizes, fuzzy bits, a whole-sector read time and a 16-byte-block timing table, and a track length that can be longer than 6 250.[46]
What it cannot carry is therefore a short list:

| what is lost | why STX cannot hold it | what a bit-cell reader must implement | can flux save it? |
|---|---|---|---|
| timing **inside** a 16-byte block | the timing table's granularity is 16 bytes | per-cell or per-transition widths, a DPLL model, and the FDC's sampling window | yes (SCP/HFE/IPF) |
| timing of **gaps and ID fields** | the timing record covers sector data only; the track image is one uniform 200 ms | cell timing for the whole track, not only its data fields | yes |
| flux the FDC **re-decodes** (no-flux areas, PLL abuse) | the track image stores the bytes a particular read produced; the next read may differ | a real MFM decoder over cells, with sync search and resynchronisation | yes, if the cells are captured (SCP) or described (IPF) |
| **per-revolution** variation | one mask, one track image | multiple revolutions, each with its own flux/index time | yes (SCP up to 5); HFE/ IPF one |
| **FM/MFM mixed** on one track | the bytes are already decoded at one density | a per-block encoding and a density switch | yes (IPF/SCP/HFE) |
| the drive's **analogue** behaviour | no field exists, in any format | a model of the head, preamp, AGC and media | **no** |

`documented` The last row is the honest bound: head alignment, write current, coercivity, wear and the preamp's transfer curve are not in the image and cannot be, because no imaging tool measures them per disk. They belong in `design/emulator-limitations.md`, not in a format.[48]
`derived` `.stt` loses **all of the above and the mask, the timing and the FDC status as well**: it is STX's byte stream without the fields that make a protected track reproducible (except via its raw section, which carries bytes but not status).[47]

### 8.2 What the bit-cell reader would be

`derived` The path is not "add three parsers"; it is a second track representation:

1. a **cell stream** per track, with variable cell widths and an index, replacing the byte list at the FDC's input;
2. an **MFM decoder** with a data separator (window, PLL capture, sync search, resync), because IPF/HFE/SCP all end in cells, not bytes;
3. **three parsers**: IPF's CAPS/IMGE/DATA descriptor language, SCP's per-revolution flux, HFE's bitstream and opcodes;
4. the **fuzzy/weak** path (SCP's raw flux, HFE's `RAND`, IPF's `FZD`) and, for SCP, the per-revolution index;
5. the **FDC's byte cadence** driven by the decoded cells rather than by a constant 32 µs;
6. the existing **block stream** stays for `.stx`/`.stt`/sector images — the cell stream is a layer below it, not a replacement.

`documented` The size is measurable in the vendored reference: Hatari's native IPF decoder is **1 401 lines** of C (BSD-3-Clause), its SCP reader **643**, and its STX reader **2 134**; the flux path also needs the shared MFM/ cell machinery the two decoders feed.[57][58][56]
`derived` So the deferral is roughly **two parsers and a cell layer** on top of the STX reader we already need — not a rewrite of the disk path, but not a week either. The study prices it as a **second track representation plus ~2 000 lines of reference parser**, and the corpus is the only thing that can say whether that work buys a title the STX path cannot run.

---

## 9. The named titles, and the decision

### 9.1 The ten preserved-original titles

`documented` The twenty-title sweep's table gives, for each protected title, the bit-level image observed; the ten below are the sample's preserved-original cases whose original survives in a bit-level container. The rest of the protected sample is §9.2. `sweep` below means `research/st-download-sources.md` Parts 1–2; [54] is Atarimania's LSL page.

| # | title | publisher, year | preserving master(s) | class documented | which v1 reader carries it | source |
|---|---|---|---|---|---|---|
| 1 | Populous | Bullfrog/EA, 1989 | `.stx` | Copylock long sector (t0 s6, 4.2 %) + CRC error | **STX** (timing table / `ReadTime`) | [51][53]; sweep |
| 2 | Speedball 2 | Bitmap Brothers/Mirrorsoft, 1990 | `.stx` | `unknown` | STX | sweep |
| 3 | Shadow of the Beast | Psygnosis, 1989/90 | `.stx` | `unknown` | STX | sweep |
| 4 | Robocop | Ocean, 1989 | `.stx`, `.ipf`, `.scp` | `unknown` | STX | sweep |
| 5 | Sensible Soccer | Sensible/Renegade, 1992 | `.stx`, `.msa` (coverdisk) | `unknown` | STX | sweep |
| 6 | Xenon 2 | Bitmap Brothers/Mirrorsoft, 1989 | `.stx` | Copylock internal (trace-vector decoder) | **STX** | [53]; sweep |
| 7 | Leisure Suit Larry 1 | Sierra, 1987 | `.ipf` ×2, `.scp`, `.hfe`; Atarimania bundle also **Pasti** | `unknown` | STX | sweep; [54] |
| 8 | F-19 Stealth Fighter | MicroProse, 1990 | `.hfe`/Pasti/`.raw`/`.scp` in one zip | `unknown` | STX | sweep |
| 9 | Turrican | Rainbow Arts, 1990 | `.scp`, `.stx`, `.hfe` | overlapping sectors + fuzzy + no-flux area (track 7) | **STX** (track image + mask; NFA caveat, §9.3) | [51]; sweep |
| 10 | Captain Blood | ERE/Exxos, 1988 | `.hfe` + Pasti | `unknown` | STX | sweep |

`documented` The sweep's own conclusion is the one this table tests: "SCP buys 1.3 % and is the only master for a few titles", while "IPF+STX covers 13.3 % and every preserved-original case in this sample" (`research/st-download-sources.md` Part 6).
`documented` Leisure Suit Larry 1 is the apparent exception — its sweep row lists `.scp` + `.hfe` + `.ipf` and no `.stx` — but its Atarimania page says the download "includes Pasti, SCP and IPF dumps", so the STX rendition exists and the sweep's row is a display limit, not a loss.[54]
`derived` The ten therefore do not meet the reversal condition *as a set*: each has an STX master, and the three classes that are documented among them are all in STX's field set (§8.1), with one caveat — a no-flux area's decode is only as good as the imager's byte capture (Turrican's track 7), which §12.4's next experiment would test. The classes that would need flux — a **mid-track** density change the uniform track-image timing cannot express, a per-revolution instability, a decode the imager could not stabilise — are not documented for any of the ten.[51][52]

### 9.2 The rest of the protected sample, and two exemplars

`documented` The remainder is mixed: **Maniac Mansion** has an `.stx` and a Spanish `.ST`; **The Great Giana Sisters** has no public dump (cracks only); **Moktar**, **Jim Power**, **Game Over II** and **Navy Moves** are preserved as **Pasti** (HFE alongside for Moktar and Jim Power, in the sweep's table). Pasti is `.stx`, so the last four are on the same reader as the ten (`research/st-download-sources.md` Part 1).
`documented` Outside the sample, **Dungeon Master** (fuzzy + 247) and **Theme Park Mystery** (12 sectors + overlap) are the two titles that pin the classes most tightly; both are documented in the preservation record, and both are STX-carryable in principle — a long track is a track image longer than 6 250, which the container allows.[51][52][46]

### 9.3 The decision

`derived` **Re-affirm the v1 defer.** The named list above is the evidence: every preserved-original title in the sample has an STX master, and the only classes the STX reader loses are ones no sample title is documented to use. The deferral's reversal condition — *"a title in the validation ladder needs it"* — is not met by the sample.

`derived` The **watch list** is the ten titles above plus the Pasti-only remainder (Moktar, Jim Power, Game Over II, Navy Moves); the trigger is concrete: if any of them loses its STX/Pasti rendition, or a title enters the ladder whose only preserving master is `.ipf`/`.scp`/`.hfe`, the condition fires and the cell reader is scheduled. The first candidate to watch is Leisure Suit Larry 1, whose sweep row is flux-only until Atarimania's bundle note is read.[54]

`derived` This is a **re-pricing downward**, not a re-affirmation by default: the handoff expected STX to lose the long-sector and fuzzy classes, and it does not. The residual is a narrow cell-level layer, and the study's recommendation is to keep the defer until a title is found in that residue.

---

## 10. What this changes in the model

### 10.1 `design/disk-path.md` §1 — the defer, re-affirmed with a named list

`derived` §1 keeps the five readers and the defer; this study adds the priced table and the watch list. The census sentence stays as it is; the new sentence is that **the loss is 2 129 entries, but the ten preserved-original titles are all STX-reachable**, which is the difference between a corpus statistic and a title decision (`research/st-download-sources.md` Part 6).

### 10.2 §3 — the byte clock is a default, not a constant

`derived` §3's "32 µs a byte" is the nominal arithmetic and remains right for a standard track. A preserved image can carry a different cadence: an STX `ReadTime` per sector, a 16-byte timing table, or a track image longer than 6 250 bytes; an IPF density description or an SCP index time does the same at cell level.[46][48][50]
`derived` The model should therefore compute the **byte position** from the track's own cadence where the image supplies one, and fall back to 32 µs where it does not. The correction belongs in §3 beside the existing burst correction; it does not change the sector period as the only resumable deadline.

### 10.3 §6 — what the loader can be handed

`derived` The loader normalises every format to the block stream, as §6 rules. It must also pass the stream's **provenance**, because a check that fails on a synthesised track is a property of the file, not a bug:

| flag | values | why the FDC model needs it |
|---|---|---|
| `gaps` | `as-dumped` / `synthetic` | a gap check passes on a real disk and fails on a synthesised one |
| `fdc-status` | `from-image` / `recomputed` | STX's CRC/RNF/deleted flags are data; a sector image's are arithmetic |
| `fuzzy` | `mask` / `none` | a check that reads five times needs five different values |
| `timing` | `measured` / `nominal` | a long sector or a whole-track rate |
| `geometry` | `declared` / `inferred` | a `.st`'s size is a guess; a `.stx`/`.stt`/`.dim` header is not |
| `provenance` | `preserved-original` / `cracked` / `unknown` | a crack's clean CRC is not evidence about the original |

`derived` The loader must never "repair" a preserved track: no recomputed CRC over a bad one, no renumbering a 247, no gap synthesis when a track image exists. This is the same rule as §6's `gaps: synthetic` flag, extended to the fields the study found.

### 10.4 §7 — the one decision for the owner

`derived` §7's flux row becomes: **deferred, re-priced 2026-11-28** — the five readers and the ten-title list are the evidence; the bit-cell reader is scheduled only when a ladder title's only master is `.ipf`/`.scp`/`.hfe`. The row also gains the study's residual list (§8.1) so the next session does not have to re-derive what STX already carries.

### 10.5 The file-formats page (definition of done 3)

`derived` The `.STX` body in `atari-st-software/file-formats/01-boot-sector-and-disk-format.md` is replaced by a dated correction (2026-11-28) carrying the track-level classification, and the suffix table's four floppy rows are corrected with it; the 2026-11-15 correction block's three levels stay as they are.

### 10.6 `design/emulator-limitations.md` — the analogue rows (definition of done 4)

`derived` Six rows, A1–A6, are added under §6.1: head alignment and azimuth, write current and coercivity, media wear, the reference spindle's speed and wow, the preamp/DPLL analogue capture range, and the write splice. The first three are `unmeasurable`, the last three `open`; nothing is modelled, and no ticket is created.

---

## 11. What stays unknown, and where

| what | why it stays unknown | where it lives |
|---|---|---|
| the analogue drive and medium (head alignment, write current, coercivity, wear) | not in any image, and not measurable per disk | `design/emulator-limitations.md` §6.1, rows A1–A6 |
| which protection each individual title uses, beyond the sample | a census question; the images carry the evidence, but only a pass over the dumps answers it | the catalogue, not this study |
| the reliability of STX's timing fields across producers | the format is reverse-engineered; a real file already shows a producer (tool `0x00AF`, revision 2) whose `readTime` is non-zero inside the documented 2 % window, and the doc itself warns of tool bugs[46][55] | a note in this study; a later census pass |
| whether a 16-byte timing table reproduces Copylock's 4.2 % check bit-exactly | only a real title run on the model can say; the field exists and Hatari applies it[56] | the build, once the disk path exists |
| half tracks on the ST | no evidence in the record; the drive steps whole tracks | `unknown`, not modelled |
| Theme Park Mystery's exact 12-sector layout (long track or short sectors) | KFAnalyze gives 12 sectors and an NFA, not the full track; no image is vendored[51] | a preservation query, not a model question |

`documented` No ticket is created for any row: the owner's 2026-11-28 ruling puts board-only questions in `design/emulator-limitations.md`, and the rest are catalogue work, not promises.[41]

---

## 12. The experiments

### 12.1 The byte budget, run

`derived` A short Python probe built the standard 614-byte sector and the 9/10/11/12 tracks from the field table; it reproduces §4.3 exactly: 9 → 5 586 (slack 664), 10 → 6 200 (slack 50), 11 → 6 814 (over by 564), 12 → 7 428 (over by 1 178), and the squeezed 11-sector sector at 566 gives 6 226 + 30 = 6 256, below the WD1772's recommended minimum of 570 a sector (6 270).[44][61]
`derived` What it settles: the 9/10 arithmetic is exact; the "11 does not fit" claim is true only at the recommended gaps; 12 is a long-track claim. The probe is arithmetic, not a model, and is not committed.

### 12.2 A real `.stx`'s track 0, read

`documented` The Pawn 2.1's STX (a public reverse-engineering repository; only the header and track descriptor were range-read, 160 bytes, no disk content) gives:[55]

| field | value |
|---|---|
| file header | `RSY\0`, version 3, tool `0x00AF`, **80 tracks**, **revision 2** |
| track 0 descriptor | record size **9 006**, fuzzy **0**, **9 sectors**, flags **`0x61`** (sector descriptors + track image, no 4-byte sync header), track length **6 283**, track 0 side 0 |
| sector 0 | data offset 6 286, bit position 1 264, read time **16 306 µs**, ID 0/0/1 size 2, CRC `0x6FCA`, status 0 |
| sector 1 | data offset 860, bit position 6 496, read time **16 346 µs**, ID 0/0/2 size 2 |
| track image | **present**, sized 6 283 bytes (the 2-byte header is `0x188B`), which is the answer to "does STX store gaps?" — yes, when the imager wrote one |

`derived` Three things the experiment settles: (1) STX stores a **track image**, so it can carry gaps, data under index and overlap; (2) a real producer's `readTime` can be non-zero even when the value is within the documented 2 % of standard, so the field is a hint, not a normalised flag; (3) the tool id `0x00AF` is outside the doc's known `0x01`/`0xCC`, which is exactly the producer-reliability caveat §11 records.[46][55]
The track length 6 283 is also a useful calibration: a "standard" single-sided disk's track image is not exactly 6 250, and the model must read `trackLength`, not assume.[55]

### 12.3 The ladder sample

`derived` §9's ten-title table is the sample answer: all ten have an STX rendition, and the three documented classes are STX-carryable. The experiment's real result is negative — **no title in the sample needs the cell reader** — which is the decision's input.

### 12.4 The search that was not optional

`documented` The preservation community's own analyses answered the class questions the handoff would otherwise have left as `unknown`: KFAnalyze's per-track measurements for Turrican, Dungeon Master, Populous, Colorado, Theme Park Mystery and D50; the DM encyclopaedia's full disk analysis; and the Copylock record's mechanism.[51][52][53]
`derived` No class in §7.1 is a guess: each is either measured in the preservation record, a field in one of the formats, or explicitly marked `unknown` (the half-track row).

`derived` Not run, and why: the model's `Read Track`/`Read Sector` read-back waits for the Disk chip (there is no FDC model yet); a real title boot waits for the same. The study's arithmetic and the format evidence are what can be done without them, and they are enough for the decision.

---

## 13. Licence position

`documented` No disk image is vendored and no copyrighted sector content is in this repository. The `.stx` probe read only the file header and the track 0 descriptor (160 bytes of format metadata) from a public repository, not a sector payload.[55]
`documented` The datasheets and format documents are cited by URL and quoted in sentences only; the vendored readers are referenced by file and line.[44][46][48][56][57][58][60][61]
`derived` The study's tables and arithmetic are drawn from those sources, not from an image; no sheet is added.

---

## Sources

[23] http://info-coach.fr/atari/documents/_mydoc/FD-HD_Programming.pdf — Info-Coach — DMA / WD1772 Programming
[41] https://jsobola.atari8.info/DEREATARI/atarisch/1040ste_fsm.pdf — Atari — STe 520/1040 Computer Field Service Manual, C302481-001 Rev A, August 1991 (2017 retyped/redrawn copy)
[44] https://hxc2001.com/download/datasheet/floppy/thirdparty/FDC/WesternDigital/WD1772-JLG-V11.pdf — Western Digital — WD1772 Floppy Disk Controller Specification V1.1, J.-L. Guérin edition, November 2008
[46] http://info-coach.fr/atari/documents/_mydoc/Pasti-documentation.pdf — J.-L. Guérin (DrCoolZic) — Pasti File Documentation v0.5, January 2014
[47] http://info-coach.fr/atari/documents/_mydoc/FD_Image_File_Format.pdf — J.-L. Guérin (DrCoolZic) — Atari Image File Formats v1.0, September 2014
[48] https://www.kryoflux.com/download/ipf_documentation_v1.6.pdf — J.-L. Guérin (DrCoolZic) — Interchangeable Preservation Format (IPF) Documentation v1.6, April 2018
[49] https://www.hxc2001.com/floppy_drive_emulator/HFE-file-format.html — HxC2001 — HFE file format specifications (v1, v2, v3 opcodes)
[50] https://www.cbmstuff.com/downloads/scp/scp_image_specs.txt — CBMSTUFF — SuperCard Pro Image File Specification v2.5, February 2024
[51] https://info-coach.fr/atari/software/pc-projects/kfanalysis.php — Info-Coach — KFAnalyze examples (Turrican, Dungeon Master, Populous, D50, Colorado, Theme Park Mystery)
[52] http://dmweb.free.fr/community/documentation/copy-protection/copy-protection-atari-st/ — Dungeon Master Encyclopaedia — detailed analysis of the Atari ST disks of Dungeon Master and Chaos Strikes Back
[53] https://en.wikipedia.org/wiki/Rob_Northen_copylock — Wikipedia — Rob Northen Copylock (history, serial track, trace-vector decoder, in-game checks)
[54] https://www.atarimania.com/games/atari-st-games-leisure-suit-larry-i-in-the-land-of-the-lounge-lizards-9818 — Atarimania — Leisure Suit Larry I (the "Pasti, SCP and IPF dumps" bundle note)
[55] https://github.com/sarnau/The-Pawn-Atari-ST — Sarnau — The Pawn Atari ST analysis and its `The Pawn 2.1.stx` (header/track descriptor range-read only)
[56] https://github.com/hatari/hatari/blob/master/src/floppies/stx.c — Hatari — `src/floppies/stx.c` (the STX reader: track image, fuzzy mask, timing table)
[57] https://github.com/hatari/hatari/blob/master/src/floppies/ipf_dsk.c — Hatari — `src/floppies/ipf_dsk.c` (the native IPF decoder, BSD-3-Clause)
[58] https://github.com/hatari/hatari/blob/master/src/floppies/scp.c — Hatari — `src/floppies/scp.c` (the SCP reader)
[59] https://github.com/hatari/hatari/blob/master/src/includes/floppies/stx.h — Hatari — `src/includes/floppies/stx.h` (the STX structures and flags)
[60] https://github.com/hatari/hatari/blob/master/src/includes/fdc.h — Hatari — `src/includes/fdc.h` (the standard track-layout constants)
[61] https://github.com/steem/steem/raw/master/steem/diskman.cpp — Steem SSE — `steem/diskman.cpp` (the STT/STX/ST loaders; `fdc.cpp` carries the 11-sector layout referenced in §4.3)
[62] https://info-coach.fr/atari/documents/_mydoc/Atari-Copy-Protection-v12.pdf — J.-L. Guérin (DrCoolZic) — Atari Floppy Disk Copy Protection v1.2 (the class catalogue and the Populous/DM/TPM examples)
