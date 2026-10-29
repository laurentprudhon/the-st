# Physical board inventory — Atari 1040STE (CA4003290 family)

Scope: a mature-production Atari 1040STE, CA4003290 board, TOS 1.62, 30-pin SIMMs,
PAL / French Peritel. Serial, MIDI, printer and cartridge are out of scope except where
they share a pin with an in-scope subsystem.

Inventory built bottom-up: manual → packages → pins (`ic.pin`) → nets → subsystems.

---

## 1. Chosen document

### 1.1 Primary document

**Atari STe 520/1040 Computer Field Service Manual**, Atari Corporation part number
**C302481-001 Rev A, August 1991** (81 pages).

| Item | Value |
|---|---|
| File used | `atari-st-hardware/docs/downloads/ste_fsm_1991.pdf` (6.8 MB, 81 pages) |
| Obtained from | `https://jsobola.atari8.info/DEREATARI/atarisch/1040ste_fsm.pdf` |
| Text layer | Yes — the whole manual is retyped, and section 9 schematics are vector redraws |
| Printed vs PDF page | printed page = PDF page − 5 (manual's own note: numbering runs five behind) |
| Assembly covered | PCB sub-assembly **CA400329-101** (`ASSY PCB SUB STE NTSC W/MOD`), schematic **0300780-001** |
| Parts list revision | ASSY **CA400338-201** → **CA400339-201** → **CA400340-111** → **CA400329-101**; dated August 1991 |

**Provenance caveat, stated up front.** This file is not a photographic scan of Atari's
printing. Its cover reads "Updated by jer/eightbitter, May 2017": the body text and parts
list were retyped, and the eight section-9 schematic sheets were **redrawn in a CAD tool
in June 2017** (title blocks read "drawn by jer", 01 June 2017). The drawing content,
reference designators, pin numbers and net names agree with the 1989 factory drawing
wherever both were legible — including the drawing number itself (§1.2) — but a redraw can
carry transcription errors of its own (see §6). Anything that matters is therefore either
cross-checked against the 1989 factory drawing or marked as coming from the redraw only.

Why this is the best available document for a *mature* CA4003290 board: it is the last
service manual Atari published for the STE (1991), it covers the production board
(CA400329-101) rather than the 1989 pre-production drawing, its parts list carries the
production part numbers (C398739-001 DMA, C300589-001 GSTMCU, C300588-001 shifter), and
unlike every scan of this manual it is fully machine-readable, so pin numbers and net names
can be transcribed without guessing.

### 1.2 Secondary / cross-check document

**Atari 1040STE schematic set C300780-001 Rev A**, six A3 sheets, drawn by C./D. Penn,
October 1989, plus **Bill of Material EMR072 Rev 5** (ATMC rev 5 / world rev 14, authorised
09 SEP 1989), assembly `CA400329-0XX` / `SD400329-001`.

| Item | Value |
|---|---|
| File used | `atari-st-hardware/docs/downloads/atari_1040ste_service_manual.pdf` (13 pages) |
| Obtained from | already present in the repository; the file is a German "Circuit Diagram Service" reprint (cover: "Service Manual / 1040STE / PREISGRUPPE 25 / FACH P 1560") |
| Contents | PDF p1 cover; p2–p5 BOM pages 1–4; p6–p11 schematic sheets 1–6; p12 assembly drawing CA400329-1XX sheet 5 of 5 (rev 6, 12/7/89); p13 mechanical parts drawing |
| Resolution | schematic sheets 2468 × 1760 px over an A3 frame ≈ 100 dpi; BOM pages 1760 × 1228 px |
| Text layer | **none** — image-only scan |
| Drawing-number cross-check | the 1991 parts list cites the schematic as **`0300780-001`**; the 1989 title block reads **`C300780-001`** — the same drawing, with the O/0 transcription difference that runs through the whole retyped manual |

This is the *pre-production* drawing: its BOM **lists no 144-pin GSTMCU part at all** (the
BOM runs to item 76 and ends with `-- END --`, so it appears complete — the omission is in
the document, not in my reading), lists a separate 60-pin blitter `C101643` at U101, and
places the 555 timer at U106 rather than U104. It was used only to confirm sheet content and
to date the design; it is **not** the basis of the inventory.

### 1.3 Documents examined and rejected

| File | What it actually is | Why rejected |
|---|---|---|
| `atari_st_field_service_manual.pdf` (repo) | 49-page print of a **WordPress blog archive** ("The Blog Archive of Doom", retrohacks.net-style site). Contains no Atari content at all. | Not a manual. This is the file the repository label implies is the STE field service manual; it must not be used. |
| `atari_520st_1040st_service_manual.pdf` (repo) | 69-page scan, P/N C026118-002 Rev B, Jan 1987 | 520ST/1040ST, not STE |
| `atari_mega_ste_service_manual.pdf` (repo) | 270-page Mega STE manual, section 8 = Mega STE schematics | Different machine; no CA400329 board |

### 1.4 Pages actually read

| Printed page | PDF page | Content |
|---|---|---|
| 52–55 | 57–60 | Section 7 parts list, ASSY CA400329-101 and sub-assemblies |
| 58 | 63 | Section 9 index ("1. CPU, Blitter, ROM, clk … 8. DMA test fixture") |
| 59 | 64 | Sheet 1 — *ATARI 1040STe CPU, BLITTER, ROM and Cartridge* |
| 60 | 65 | Sheet 2 — *Atari 1040STe Input/Output* (PSG, MFP, RS-232, MIDI, parallel) |
| 61 | 66 | Sheet 3 — *ATARI 1040STe 03 DMA FDD CLK* |
| 62 | 67 | Sheet 4 — *Atari STe MCU, SHIFTER and VIDEO* |
| 63 | 68 | Sheet 5 — *ATARI 1040ST STEREO SOUND, JOYS and PADS* |
| 64 | 69 | Sheet 6 — *ATARI 1040STe RAM* |
| 65 | 70 | Sheet 7 — *Atari STe Power Supply Mitsumi SR98 CA070086-002* |
| — | 71 | Sheet 8 — DMA test fixture (out of scope, not read beyond its title) |

Cross-check pages read from the 1989 scan: BOM pages 1–4 (PDF p2–p5), schematic sheets 1–6
(PDF p6–p11) at reduced fidelity, title blocks only for sheets 2–6.

### 1.5 How pins were read, and what "verified" means

The 1991 sheets are vector drawings: every pin number, pin name and net label is a text
object with coordinates. Pins were extracted by pairing pin-number text with the pin-name
text lying 1.8 pt to its right on the same edge of a symbol, and the pairing was accepted
only when that offset held across the whole pin row (constant `dx = 1.8`, no unresolved
pins). The method was validated on devices whose pinout is fixed and known independently:

* U102/U103 extract to exactly the 27C010 32-pin assignment (A0=12 … A16=2, /OE=24, /CE=22);
* U301 extracts to exactly the WD1772 28-pin assignment (WG=21, WD=22, STEP=16, DIRC=17,
  /WPRT=25, TR00=23, /MO=20, IP=24, /RD=19, DDEN=26, DRQ=27);
* U100 extracts to the 68000 PLCC68 assignment with the characteristic skips at pins
  55/60 (VCC/GND) in the A1–A20 run.

Confidence tags used in §3:

| Tag | Meaning |
|---|---|
| **V** | pin number and pin name both read from the drawing, pairing unambiguous |
| **V\*** | pin number read, but the symbol row was staggered — pin *name* order verified, number position cross-checked against the datasheet-standard part |
| **U** | not legible / not extracted; **no number is asserted** |

Nothing in §3 or §4 is filled in from memory of what the chip "ought" to have. Where a
signal is obviously present but its pin number could not be resolved, the pin number is
written `unverified`.

**Whole-section limitation.** Sheet 3 (DMA/FDC) and sheet 4 (MCU/shifter) were fully
extractable. Sheet 2 (I/O) was only partly extractable: the YM2149's data/control pins and
both 6850 ACIAs sit in symbol rows whose pin-name text is drawn at a different offset and
could not be paired — those pin numbers are marked `unverified` and only their port pins
are given. Sheet 5 (sound) and sheet 6 (RAM) were read at signal level only. A sharper
scan of the 1989 sheets, or the original 1991 D-size plots, would be needed to close the
remaining gaps.

---

## 2. IC list

Every package on the board that carries an active device, from the Section 7 parts list
(ASSY CA400329-101). "Custom" = Atari-proprietary or Atari-programmed part.

### 2.1 Atari custom / programmed parts

| Ref | Drawn on | Part number as printed | Package | Function |
|---|---|---|---|---|
| U400 | sheet 4 (centre) | **C300589-001** IC STE MCU GLUE 144P | 144-pin PGA/PLCC | **GSTMCU** — glue, MMU, DMA arbitration, video timing, DRAM control, pad/pen ADC, blitter control |
| U401 | sheet 4 | **C300588-001** IC STESHFTR 84P NATIONAL | 84-pin PLCC | **GST Shifter** — bitplane shift, palette, DE/BLANK, Super Hi-Res |
| U300 | sheet 3 | **C398739-001** IC DMA CONTL 40P (list prints `0398739-001`) | 40-pin DIP | **DMA controller** — bus arbitration, disk/sound DMA, ACSI |
| U101 | sheet 1 | **C101643** IC ST BLITTER CUSTOM 68 PIN | 68-pin PLCC | **Blitter** (discrete; merged into GSTMCU on later boards) |
| U102 | sheet 1 | **C301163-002** IC TOS ROM 1.62 USA HI STE | 32-pin DIP | TOS 1.62 high ROM |
| U103 | sheet 1 | **C301164-002** IC TOS ROM 1.62 USA LO STE | 32-pin DIP | TOS 1.62 low ROM |
| U202 | sheet 2 | **C025983** IC YM-2149 SOUND 40P | 40-pin DIP | PSG (3 tone + noise + envelope), 2 × 8-bit I/O ports |
| U203 | sheet 2 | **C025984** IC 68901 MFP 48P | 48-pin DIP | MFP: timers, USART, interrupt controller, 8-bit GPIO |
| U200 | sheet 2 | **C025985** IC 6850 ACIA 24P | 24-pin DIP | ACIA — MIDI (out of scope) |
| U201 | sheet 2 | **C025985** IC 6850 ACIA 24P | 24-pin DIP | ACIA — RS-232 (out of scope) |
| U208 | sheet 2 | **C025988** IC PC900 PHOTO COUPLER 6P | 6-pin DIP | MIDI input opto-isolator (out of scope) |

### 2.2 Standard parts

| Ref | Part number as printed | Package | Function |
|---|---|---|---|
| U100 | **C398061-001** IC 68000 PLCC 68P | 68-pin PLCC | CPU, 8 MHz, 16-bit data / 24-bit address |
| U301 | **C026028-002** IC 1772 FDD CONTROLLER 28P ST | 28-pin DIP | WD1772 floppy disk controller |
| U206 | **C025986** IC MC1488 RS232C DRIVER 14P | 14-pin DIP | RS-232 line driver (out of scope) |
| U207 | **C101739-001** IC 1489A RS-232C RECEIVER 14P | 14-pin DIP | RS-232 line receiver (out of scope) |
| U403 | **C101001-117** IC MC1377 ENCODER 20 PIN (list prints `01001 17`) | 20-pin DIP | PAL/NTSC RGB→composite encoder (Peritel/modulator path) |
| U508 | **C398058-001** IC LMC1992 28P | 28-pin DIP | Stereo volume/bass/treble + mixer control |
| U502, U503 | **C398057-001** IC DAC0802 16P | 16-pin DIP | 8-bit DAC ×2 — L/R stereo DMA sound |
| U504, U507 | **C398055-001** IC LF347 14P | 14-pin DIP | Quad op-amp — audio output stage |
| U505, U506 | **C398056-001** IC MF4-100 8P | 8-pin DIP | Switched-capacitor low-pass filter (audio) |
| U104 | **C019748** IC 555 TIMER 8P | 8-pin DIP | Reset / power-on delay timer |
| U513, U514 | **C025989** IC LM556 DUAL PRECISION 14P | 14-pin DIP | Dual timer — floppy / ACSI timing, mouse/joystick timing |
| U204, U205 | **C070447** IC TL497A SW REGULATOR 14P | 14-pin DIP | Switching regulator (−12 V rail for RS-232) |
| U210, U305 | **C101609-006** IC 7406 DIP 14P | 14-pin DIP | Hex inverter, open collector — drive stage for FDD/ACSI/IRQ |
| U209 | **C101611-086** IC 74LS86 DIP 14P | 14-pin DIP | Quad XOR — address decode / video combine |
| U211 | **C101611-004** IC 74LS04 DIP 14P | 14-pin DIP | Hex inverter |
| U304, U306 | **C101611-074** IC 74LS74 DIP 14P | 14-pin DIP | Dual D flip-flop — clock/DE pipeline |
| U900 | **C101611-164** IC 74LS164 DIP 14P | 14-pin DIP | 8-bit shift register — keyboard/MIDI serialisation |
| U307, U509, U510, U512, U632 | **C101611-244** IC 74LS244 DIP 20P | 20-pin DIP | Octal buffer — address/control buffering |
| U302 | **C101611-245** IC 74LS245 DIP 20P | 20-pin DIP | Octal bus transceiver — data bus buffer |
| U511 | **C101611-373** IC 74LS373 DIP 20P | 20-pin DIP | Octal latch — video/palette address latch |
| U500, U501 | **C300259-374** IC 74F374 DIP 20P | 20-pin DIP | Octal D flip-flop — video shift pipeline |
| U405 | **C101610-257** IC 74S257 DIP 16P | 16-pin DIP | Quad 2:1 mux — RGB/mono video select |
| U405 (sic) | **C300259-257** IC 74F257 DIP 16P | 16-pin DIP | Alternative part for the same location (see §6) |
| U500 (sic) | **C070734-002** IC LM78L82 VOLTAGE REGULATOR 3P | TO-92 | Regulator; the printed location collides with the 74F374 above (see §6) |

### 2.3 Oscillators, crystal, non-IC packages that matter to the inventory

| Ref | Part | Value | Notes |
|---|---|---|---|
| Y200 | C025993 | 2.4576 MHz crystal | MFP / YM2149 clock |
| U303 | C398066-001 | 8.010613 MHz oscillator | 68000 clock (32.042454/4) |
| U402 | C398068-001 | 32.215905 MHz oscillator | NTSC master clock |
| U402 | C398097-001 | 32.084988 MHz oscillator | **Peritel/PAL master clock** — one of the two is fitted, never both |
| U600–U603 | C398063-001 / C398152-001 | 30-pin SIMM sockets on `PCB SIMM 256K x 8 DIP STE` | 4 sockets; 16 × `C0101743-002` DRAM 256K×1 120 ns populate them (1040STE = 2 banks populated) |
| P100 | C101008-122 | resistor network 1.2 kΩ 9P | pull-ups |
| P101–P103, P500–P502 | C101008-472 | resistor network 4.7 kΩ 9P | bus pull-ups |
| P104, P105, P600 | C070448 | resistor network 10 kΩ 9P | pull-ups |
| S100 | C070119 | push switch | reset |
| VR / U500 | C070734-002 | LM78L82 | regulator (location as printed) |

### 2.4 Connectors (nets leaving the board)

| Ref | Part | Function | In scope? |
|---|---|---|---|
| J100 | C070136 | 6-pin DIN, power in from PSU | yes (power) |
| J103 | C070129 | 6-pin right angle, cartridge | no (shares bus pins) |
| J200, J201 | C070033 | 8-pin DIN ×2 — MIDI in/out | no |
| J202 | C070132-002 | DB-25 — printer | no |
| J203 | (D-25 female) | RS-232 serial | no |
| J301 | C070130-002 | DB-19 — ACSI hard disk | yes (disk) |
| J302 | CA070023-009 | 4-pin cable, FDD power | yes (disk) |
| J303 | CA070024 | 34-pin flat cable, FDD data | yes (disk) |
| J306 | C070131 | 14-pin DIN — external floppy | yes (disk) |
| J304 | — | external FDD control header | yes (disk) |
| J500, J501 | C398066-001 (as printed) | DB-15 — joystick/paddle ports 0 and 1 | yes (controllers) |
| J502, J503 | C398065-001 | phono jacks — audio L/R | yes (sound) |
| J600 | C070136 | 13-pin DIN — monitor/Peritel | yes (video) |

---

## 3. Pin list

Format: `Uxxx.PIN` — pin number, signal name as printed, direction, active level.
Direction and active level are properties of the **device pin** (from the manual's symbol
and the part's own datasheet behaviour), not of the net.

### 3.1 U100 — MC68000, PLCC68 (C398061-001)

Sheet 1 (printed p59). 24 pins of the symbol resolved cleanly (V); the remainder of the
symbol is drawn on staggered rows whose pin numbers could not be separated, and are listed
as `unverified` with no number.

| Pin | Signal | Dir | Active | Tag |
|---|---|---|---|---|
| 1 | VCC | pwr | — | V |
| 2 | A21 | out | H | V |
| 3 | A22 | out | H | V |
| 4 | A23 | out | H | V |
| 5 | GND | pwr | — | V |
| 6 | D15 | I/O | H | V |
| 7 | D14 | I/O | H | V |
| 8 | D13 | I/O | H | V |
| 9 | D12 | I/O | H | V |
| 10 | D11 | I/O | H | V |
| 11 | D10 | I/O | H | V |
| 12 | D9 | I/O | H | V |
| 13 | D8 | I/O | H | V |
| 14 | D7 | I/O | H | V |
| 15 | D6 | I/O | H | V |
| 16 | D5 | I/O | H | V |
| 17 | D4 | I/O | H | V |
| 18 | D3 | I/O | H | V |
| 19 | D2 | I/O | H | V |
| 20 | D1 | I/O | H | V |
| 21 | D0 | I/O | H | V |
| 23 | AS | out | L | V |
| 24 | UDS | out | L | V |
| 25 | LDS | out | L | V |
| 26 | R/W | out | H=read | V |
| 27 | DTACK | in | L | V |
| 33 | VCC | pwr | — | V |
| 34 | CLK | in | ↑ | V |
| 35 | GND | pwr | — | V |
| 38 | BERR | in | L | V |
| 43 | FC2 | out | H | V |
| 44 | FC1 | out | H | V |
| 45 | FC0 | out | H | V |
| 46–54, 56–59, 61–67 | A1 … A20 | out | H | V |
| 28, 30, 37 | BG / BR / BGACK (order on the row is BR, BG, BGACK) | — | L | V* |
| 22, 29, 31, 32, 36, 39, 40, 41, 42, 68 | HALT, RESET, IPL0–IPL2, VMA, VPA, E, GND — present on the symbol, pin numbers not separable | — | — | U |

The 68000's `A1…A20` run is pins 46–67 with gaps at 55 and 60 (VCC and GND), and
`A21…A23` are pins 2–4 — the standard PLCC68 corner assignment.

### 3.2 U101 — ST Blitter, PLCC68 (C101643)

Sheet 1. Discrete blitter, present on this board revision; on later CA4003290 boards the
same function is inside U400 and U101 is not fitted (see §6).

| Pin | Signal | Dir | Active | Tag |
|---|---|---|---|---|
| 1–5 | D4, D3, D2, D1, D0 | I/O | H | V |
| 58–68 | D15 … D5 | I/O | H | V |
| 32–51, 53–55 | A1 … A23 (gap at pin 52) | in | H | V |
| 10 | DTACK | out | L | V |
| 28, 29, 30 | FC2, FC1, FC0 | in | H | V |
| 14, 52 | VCC | pwr | — | V |
| 16, 17, 56, 57 | GND | pwr | — | V |
| 13 | BR | out | L | V |
| 11 | BG | in | L | V |
| 15 | CLK | in | ↑ | V |
| 19 | HALT | out | L | V |
| 21 | VMA | out | L | V |
| 23 | VPA | out | L | V |
| 24 | BERR | out | L | V |
| 25, 26, 27 | IPL2, IPL1, IPL0 | out | L | V |
| 12, 20, 22 | BLIT-INT-related / bus control (names partly illegible) | — | — | U |

`A1…A23` on the blitter are the *CPU* address bus, not the MAD bus: the blitter is a bus
master that drives addresses onto the system bus through the GSTMCU.

### 3.3 U102 / U103 — TOS ROM 27C010, 32-pin DIP

Sheets 1. Both devices wired identically; U102 = high half, U103 = low half.

| Pin | Signal | Dir | Active | Tag |
|---|---|---|---|---|
| 1 | VPP (tied) | — | — | U (row not resolved) |
| 2 | A16 | in | H | V |
| 3 | A15 | in | H | V |
| 4 | A12 | in | H | V |
| 5 | A7 | in | H | V |
| 6 | A6 | in | H | V |
| 7 | A5 | in | H | V |
| 8 | A4 | in | H | V |
| 9 | A3 | in | H | V |
| 10 | A2 | in | H | V |
| 11 | A1 | in | H | V |
| 12 | A0 | in | H | V |
| 13, 14, 15, 17, 18, 19, 20, 21 | I/O0 … I/O7 | I/O | H | V |
| 16 | GND | pwr | — | V |
| 22 | CE | in | L | V |
| 23 | A10 | in | H | V |
| 24 | OE | in | L | V |
| 25 | A11 | in | H | V |
| 26 | A9 | in | H | V |
| 27 | A8 | in | H | V |
| 28 | A13 | in | H | V |
| 29 | A14 | in | H | V |
| 30 | NC | — | — | V |
| 31 | PGM | in | H | V |
| 32 | VCC | pwr | — | V |

Jumper block W102/W103/W104 selects 27C256 / 27C512 / 27C010-27C1001-571000 (sheet 1
"ROM jumper settings", note: *jumpers are shown set for 1 Meg ROMs*).

### 3.4 U400 — GSTMCU, 144-pin (C300589-001)

Sheet 4 (printed p62). This is the densest symbol in the set and the extraction is
incomplete: three staggered pin-number rows on the top edge were resolved, the bottom edge
was not. Pins listed as `unverified` exist on the symbol (names legible) but their numbers
could not be separated.

| Pin | Signal | Dir | Active | Tag |
|---|---|---|---|---|
| 3 | CLK8 | out | ↑ | V |
| 4 | CLK4 | out | ↑ | V |
| 5 | CLK2 | out | ↑ | V |
| 6 | KHZ500 | out | ↑ | V |
| 7 | ROM6 | out | L | V |
| 8 | ROM5 | out | L | V |
| 9 | ROM4 | out | L | V |
| 10 | ROM3 | out | L | V |
| 11 | ROM2 | out | L | V |
| 12 | ROM1 | out | L | V |
| 13 | ROM0 | out | L | V |
| 17 | DINT | out | L | V |
| 18 | SNDIR | out | H | V |
| 19 | SNDCS | out | L | V |
| 25 | VPA | out | L | V |
| 26 | BGACK | in | L | V |
| 27 | BGO | out | L | V |
| 28 | BGI | in | L | V |
| 29 | BR | in | L | V |
| 30 | EINT3 | in | L | V |
| 31 | EINT5 | in | L | V |
| 32 | EINT7 | in | L | V |
| 33 | IPL0 | out | L | V |
| 34 | IPL1 | out | L | V |
| 35 | IPL2 | out | L | V |
| 36 | VCC | pwr | — | V |
| 48 | N6850 | out | L | V |
| 72 | VCC | pwr | — | V |
| 83 | IACK | in | L | V |
| 98 | PADRST | out | L | V |
| 99 | JOYAH | in | H | V |
| 100 | JOYAL | in | H | V |
| 101 | JOYWE | out | L | V |
| 102 | JOYWL | out | L | V |
| 103 | BUTTON | in | H | V |
| 106 | SLOAD | out | H | V |
| 107 | SREQ | in | L | V |
| 108 | VCC | pwr | — | V |
| 110 | MAD0 | out | H | V |
| 111 | MAD1 | out | H | V |
| 112 | MAD2 | out | H | V |
| 113 | MAD3 | out | H | V |
| 114 | MAD4 | out | H | V |
| 115 | MAD5 | out | H | V |
| 116 | MAD6 | out | H | V |
| 117 | MAD7 | out | H | V |
| 118 | MAD8 | out | H | V |
| 119 | MAD9 | out | H | V |
| 120 | VCC | pwr | — | V |
| 121 | WE | out | L | V |
| 122 | CAS1L | out | L | V |
| 123 | CAS1H | out | L | V |
| 124 | RAS1 | out | L | V |
| 125 | CAS0L | out | L | V |
| 126 | CAS0H | out | L | V |
| 127 | RAS0 | out | L | V |
| 129 | WDAT | in | H | V |
| 130 | HDAT | in | H | V |
| 131 | LATCH | out | ↑ | V |
| 135 | RDY | in | L | V |
| 136 | FCS | out | L | V |
| 138 | DCYC | out | H | V |
| 139 | CMPCS | out | L | V |
| 140 | BLANK | out | L | V |
| 141 | DE | out | H | V |
| 142 | VSYNC | out | L | V |
| 144 | VCC | pwr | — | V |
| — | HSYNC (drawn on the top edge; the number extracted next to it, 146, is outside the package and is wrong — number **unverified**) | out | L | U |
| — | MAL0–MAL9 / refresh counter, D0–D15, A1–A23, AS, LDS, UDS, R/W, DTACK, BERR, RESET, TEST, PEN, PAD0X, PAD0Y, PAD1X, PAD1Y, FDINT, HDINT, BINT, SINT, SNDCS, FCS, CMPCS (bottom edge and left/right columns) | — | — | U |
| — | GND (several pins, bottom edge) | pwr | — | U |

Signals seen on the U400 symbol but with no pin number resolved (bottom edge, legible
names only): `AS`, `LDS`, `UDS`, `R/W`, `DTACK`, `BERR`, `RESET`, `TEST`, `VMA`, `PEN`,
`PAD0X`, `PAD0Y`, `PAD1X`, `PAD1Y`, `FDINT`, `HDINT`, `BINT`, `SINT`, `RAM`, `DEV`, `DMA`,
`CLK`, `A1–A23`, `D0–D15`, `GND`.

### 3.5 U401 — GST Shifter, 84-pin PLCC (C300588-001)

Sheet 4. Both edges resolved.

| Pin | Signal | Dir | Active | Tag |
|---|---|---|---|---|
| 1 | PD7 | in | H | V |
| 2–9 | PD8 … PD15 | in | H | V |
| 10 | R/W | in | H=read | V |
| 11 | VCC | pwr | — | V |
| 12 | A6 | in | H | V |
| 13 | A5 | in | H | V |
| 14 | A4 | in | H | V |
| 15 | A3 | in | H | V |
| 16 | A2 | in | H | V |
| 17 | A1 | in | H | V |
| 18 | CS | in | L | V |
| 19 | LD | in | ↑ | V |
| 20 | HD | in | ↑ | V |
| 21 | SD7 | I/O | H | V |
| 22 | SD6 | I/O | H | V |
| 23 | SD5 | I/O | H | V |
| 24 | SD4 | I/O | H | V |
| 25 | SD3 | I/O | H | V |
| 26 | SD2 | I/O | H | V |
| 27 | SD1 | I/O | H | V |
| 28 | SD0 | I/O | H | V |
| 29 | MWE | in | L | V |
| 30 | MWD | in | H | V |
| 31 | MWK | in | ↑ | V |
| 32 | VCC | pwr | — | V |
| 33 | SCLK | out | ↑ | V |
| 34 | FCLK | in | ↑ | V |
| 36 | SREQ | out | L | V |
| 37 | LOAD | in | ↑ | V |
| 38–41 | R3, R2, R1, R0 | out | H | V |
| 42–45 | G3, G2, G1, G0 | out | H | V |
| 46–49 | B3, B2, B1, B0 | out | H | V |
| 50 | MONO | in | H | V |
| 51 | COLOR | in | H | V |
| 53 | CLK | in | ↑ | V |
| 54 | DE | in | H | V |
| 56–71 | MD0 … MD15 | in | H | V |
| 74, 75 | GND | pwr | — | V |
| 77 | HDAT | in | H | V |
| 78–84 | PD0 … PD6 | in | H | V |
| 35, 52, 55, 72, 73, 76 | present on symbol, names illegible | — | — | U |

### 3.6 U300 — DMA controller, 40-pin DIP (C398739-001)

Sheet 3 (printed p61). Complete.

| Pin | Signal | Dir | Active | Tag |
|---|---|---|---|---|
| 1 | R/W | in | H=read | V |
| 2 | A1 | in | H | V |
| 3 | FCS | in | L | V |
| 4–19 | D0 … D15 | I/O | H | V |
| 20 | GND | pwr | — | V |
| 21 | FDRQ | out | H | V |
| 22 | FDCS | out | L | V |
| 23 | HDRQ | out | H | V |
| 24 | HDCS | out | L | V |
| 25 | CR/W | out | H=read | V |
| 26 | CA1 | out | H | V |
| 27 | CA2 | out | H | V |
| 28 | GND | pwr | — | V |
| 29–36 | CD7 … CD0 | I/O | H | V |
| 37 | ACK | in | L | V |
| 38 | RDY | out | L | V |
| 39 | CLK | in | ↑ | V |
| 40 | VCC | pwr | — | V (pin number inferred from the package's power pin; not on the extracted rows) |

### 3.7 U301 — WD1772 FDC, 28-pin DIP (C026028-002)

Sheet 3. Complete.

| Pin | Signal | Dir | Active | Tag |
|---|---|---|---|---|
| 1 | CS | in | L | V |
| 2 | R/W | in | H=read | V |
| 3 | A0 | in | H | V |
| 4 | A1 | in | H | V |
| 5–12 | DAL0 … DAL7 | I/O | H | V |
| 13 | MR | in | L | V |
| 14 | GND | pwr | — | V |
| 15 | VCC | pwr | — | V |
| 16 | STEP | out | L | V |
| 17 | DIRC | out | H | V |
| 18 | CLK | in | ↑ | V |
| 19 | RD | out | L | V |
| 20 | MO | out | L | V |
| 21 | WG | out | L | V |
| 22 | WD | out | L | V |
| 23 | TR00 | in | L | V |
| 24 | IP | in | L | V |
| 25 | WPRT | in | L | V |
| 26 | DDEN | out | H | V |
| 27 | DRQ | out | H | V |
| 28 | name illegible on the drawing (a supply/test pin on this package) | — | — | U |

### 3.8 U203 — MFP 68901, 48-pin DIP (C025984)

Sheet 2 (printed p60).

| Pin | Signal | Dir | Active | Tag |
|---|---|---|---|---|
| 1 | R/W | in | H=read | V |
| 2–6 | RS1 … RS5 | in | H | V |
| 10 | RC | in | — | V |
| 11 | VCC | pwr | — | V |
| 13 | TAO | out | — | V |
| 14 | TBO | out | — | V |
| 15 | TCO | out | — | V |
| 16 | TDO | out | — | V |
| 17 | X1 | in | 2.4576 MHz | V |
| 18 | X2 | out | 2.4576 MHz | V |
| 19 | TAI | in | ↑ | V |
| 20 | TBI | in | ↑ | V |
| 22–29 | IO0 … IO7 | I/O | H | V |
| 30 | TR | out | — | V |
| 31 | RR | in | — | V |
| 32 | IRQ | out | L | V |
| 33 | IEO | out | L | V |
| 34 | IEI | in | L | V |
| 35 | CLK | in | ↑ | V |
| 36 | GND | pwr | — | V |
| 37–44 | D0 … D7 | I/O | H | V |
| 45 | IACK | in | L | V |
| 47 | DS | in | L | V |
| 48 | CS | in | L | V |
| 21, 46 | present on symbol, names illegible | — | — | U |

Port A/B pins (`IOA0–IOA7` = 21,20,19,18,17,16,15,14; `IOB0–IOB7` = 13,12,11,10,9,8,7,6)
appear on the sheet as a separate block at a different pin-number offset and are **not**
included above: the 68901's GPIO ports are on the same pins as that block only if the
symbol is read differently, and I could not reconcile the two rows. Treat the IOA/IOB
numbers as `unverified`.

### 3.9 U202 — YM2149, 40-pin DIP (C025983)

Sheet 2. Only the two 8-bit ports resolved.

| Pin | Signal | Dir | Active | Tag |
|---|---|---|---|---|
| 38 | ACC | out | — | V |
| 21, 20, 19, 18, 17, 16, 15, 14 | IOA0 … IOA7 | I/O | H | V |
| 13, 12, 11, 10 | IOB0 … IOB3 | I/O | H | V |
| 9, 8, 7, 6 | IOB4 … IOB7 | I/O | H | U (same block, offset unresolved) |
| — | DA0–DA7, BC1, BC2, BDIR, CLK, IRQ, CS, R/W, TEST | — | — | U |

Port A is the **keyboard/joystick read port** and port B the **controller select/write
port** on the STE, which is why they are in scope; the sound pins are in scope as well but
their numbers are not resolved.

### 3.10 U200 / U201 — 6850 ACIA, 24-pin DIP (C025985)

Sheet 2. Out of scope (MIDI / RS-232) except that the ACIA drives `N6850` from U400 pin 48
and both share the MFP's interrupt structure. Pin numbers were **not** resolved: the 6850
symbol rows in this redraw place the pin-name text at zero offset, and the pairing produced
duplicates. Signals legible: `CS0`, `CS1`, `CS2`, `R/W`, `RS`, `D0–D7`, `IRQ`, `RXD`,
`TXD`, `RXC`, `TXC`, `RTS`, `CTS`, `DCD`, `CLK`, `GND`, `VCC`.

### 3.11 U302 — 74LS245 octal transceiver, 20-pin

Sheet 3. Standard pinout; the extracted rows confirm it (A-side 2,4,6,8,11,13,15,17;
B-side 18,17,16,15,14,13,12,11; `DIR` on 1, `/OE` on 19).

| Pin | Signal | Dir | Active | Tag |
|---|---|---|---|---|
| 1 | DIR | in | H = A→B | V |
| 2–9 | A1 … A8 | I/O | H | V* |
| 10 | GND | pwr | — | V* |
| 11–18 | B8 … B1 | I/O | H | V* |
| 19 | /OE (printed `2OE`) | in | L | V |
| 20 | VCC | pwr | — | V* |

### 3.12 U307 — 74LS244 octal buffer, 20-pin

Sheet 3. Drawn as two 4-bit halves with `1OE`/`2OE`; numbers extracted as 2,4,6,8 (1A1–1A4),
11,13,15,17 (2A1–2A4), outputs 18,16,14,12 and 9,7,5,3. Marked **U** for the individual
numbers: the row mixed U302 and U307 columns and the halves could not be separated with
confidence.

### 3.13 U403 — MC1377 encoder, 20-pin

Sheet 4. Out of the enumerated set: only `U403` and the part number are legible; the
symbol's pins are drawn on a rotated block and were not extracted. **All pins unverified.**

### 3.14 U508 — LMC1992, 28-pin

Sheet 5 (printed p63).

| Pin | Signal | Dir | Active | Tag |
|---|---|---|---|---|
| 9 | LSI | in | — | V |
| 10 | LTI | in | — | V |
| 11 | LTO | out | — | V |
| 12 | LAO | out | — | V |
| 18 | RAO | out | — | V |
| 19 | RTO | out | — | V |
| 20 | RTI | in | — | V |
| 21 | RSI | in | — | V |

(Left/right serial in-out, treble in-out, bass/volume out — the four-wire serial control
interface to the GSTMCU's `SNDCS`/`SNDIR` lines is on the pins not resolved.)

### 3.15 U502 / U503 — DAC0802, 16-pin

Sheet 5. Not extracted (rotated symbol block); the sheet shows the two DACs fed from the
shifter's `SD0–SD7` and clocked from `SNDCS`/`SNDIR`. **All pins unverified.**

### 3.16 U104 — 555 timer, 8-pin

Sheet 1. Signals legible: `TRG`, `THR`, `DIS`, `OUT`, `RES` (reset), `VCC`, `GND`, plus the
`RESET` net. Pin numbers were extracted as 3→OUT, 6→THR, 7→DIS but the row is incomplete;
treat numbers as **U**.

---

## 4. Connection list

Nets that join two or more pins. Power (`VCC`, `+5 V`, `+12 V`, `−12 V`, `GND`) is excluded.
"off-board" marks a net that leaves the PCB through a connector.

Notation: `Uxxx.NN` — pin. Where one endpoint's pin number is unverified the IC is given
without a pin (`U400.HSYNC`).

### 4.1 CPU and system bus (sheet 1)

| Net | Endpoints | Notes |
|---|---|---|
| D0–D15 | U100.21…6 · U101.5…1,68…58 · U300.4…19 · U203.37…44 (D0–D7) · U102/U103.13,14,15,17,18,19,20,21 · U302 (A/B side) | buffered onto the peripheral bus by U302 |
| A1–A20 | U100.46–54, 56–59, 61–67 · U101.32–51, 53–55 · U401.17…12 (A1–A6) · U102/U103 (A0–A16) | A21–A23: U100.2–4 only (ROM/decoder) |
| A21, A22, A23 | U100.2, U100.3, U100.4 · U101 (A21–A23 present on the blitter symbol) | |
| AS / LDS / UDS | U100.23, .25, .24 · U400.AS/.LDS/.UDS · U101 | nets `XAS`, `XLDS`, `XUDS` in the 1989 drawing |
| R/W | U100.26 · U400.R/W · U300.1 · U301.2 · U401.10 | |
| DTACK | U100.27 · U400.DTACK · U101.10 | |
| BERR | U100.38 · U400.BERR · U101.24 | |
| FC0/FC1/FC2 | U100.45, .44, .43 · U400.FC0–2 · U101.30, .29, .28 | function-code lines, decode to `CMPCS`/`FCS` |
| IPL0/IPL1/IPL2 | U400.33, .34, .35 · U100 (unverified pin) | interrupt priority, driven by MFP + GSTMCU |
| IACK | U400.83 · U203.45 · U200/U201 | interrupt acknowledge chain |
| VPA / VMA | U400.25 · U100 · U101.23, .21 | MFP auto-vector / 6800 peripheral cycle |
| BR / BG / BGACK | U400.29, .28, .27, .26 · U100 (unverified pins) · U101.13, .11 | bus arbitration for DMA and blitter |
| CLK (8 MHz) | U303 (8.010613 MHz osc) → U100.34 · U400.CLK · U300.39 · U401.53 | |
| CLK8 / CLK4 / CLK2 | U400.3, .4, .5 → U401.FCLK, U300, MFP | divided pixel clocks |
| KHZ500 | U400.6 · MFP timer input (net label `KHZ500` on sheets 1 and 2; which of `TAI`/`TBI` it reaches is not legible) | 500 kHz reference for MFP timers / floppy |
| ROM0–ROM6 | U400.13, .12, .11, .10, .9, .8, .7 → U102.CE / U103.CE and the ROM jumper block | chip-select/segment decode |

### 4.2 RAM access, refresh and video fetch (sheets 1, 4, 6)

| Net | Endpoints | Notes |
|---|---|---|
| MAD0–MAD9 | U400.110–119 → SIMM sockets U600–U603 (address pins) | multiplexed row/column address to the DRAMs |
| RAS0 / RAS1 | U400.127, .124 → SIMM bank 0 / bank 1 | row address strobe per bank |
| CAS0L / CAS0H | U400.125, .126 → SIMM bank 0 low/high byte | |
| CAS1L / CAS1H | U400.122, .123 → SIMM bank 1 low/high byte | |
| WE | U400.121 → SIMM sockets | write enable, common to both banks |
| MWE / MWD / MWK | U401.29, .30, .31 ← U400 | shifter memory write path for Super Hi-Res |
| MD0–MD15 | U401.56–71 | video data from RAM to shifter (16-bit fetch) |
| DE | U400.141 → U401.54 | display enable, gates the video fetch slot |
| BLANK | U400.140 → U403 / video output stage | blanking |
| HSYNC / VSYNC | U400.142 (VSYNC, V) · U400.HSYNC (U) → U403 and J600 | off-board at J600 |
| LATCH | U400.131 → U511 (74LS373) / palette latch | |
| SLOAD | U400.106 → U401.LOAD (37) | shifter load |
| SREQ | U401.36 → U400.107 | shifter request for a fetch slot |
| SCLK | U401.33 | shifter clock out |

### 4.3 Video output (sheets 4, 5)

| Net | Endpoints | Notes |
|---|---|---|
| R0–R3, G0–G3, B0–B3 | U401.41–38, .45–42, .49–46 → U405 (74S257) → U403 (MC1377) | 12-bit RGB, 4096 colours |
| MONO | U401.50 → U405 | mono monitor select |
| COLOR | U401.51 | colour/mono mode select |
| PD0–PD15 | U401.78–84, .1–9 | palette / pixel data path to the 74F374 pipeline (U500/U501) and U511 |
| HSYNC/VSYNC → J600 | U400 → J600 pins 9/10 region | **off-board**, 13-pin DIN monitor/Peritel |
| AUDIO L / R | U508 → J502 / J503 | **off-board**, phono jacks |

### 4.4 Sound (sheets 2, 5)

| Net | Endpoints | Notes |
|---|---|---|
| SNDCS | U400.19 → U508, U502/U503 | sound chip select (PSG or DMA DACs) |
| SNDIR | U400.18 → U508 | LMC1992 serial data/clock direction |
| SD0–SD7 | U401.28…21 → U502/U503 (DAC0802) and U202 (YM2149 data) | 8-bit sound sample bus |
| IOA0–IOA7 | U202.21…14 → keyboard matrix / joystick read | **in scope for controllers** |
| IOB0–IOB3 | U202.13…10 → controller select / FDD side select | |
| ACC | U202.38 | A/D converter input (paddles) |

### 4.5 Disk and storage (sheet 3)

| Net | Endpoints | Notes |
|---|---|---|
| FCS | U300.3 ← U400.136 | DMA chip select from GSTMCU |
| FDCS / FDRQ | U300.22, .21 → U301.1 (CS), U301.27 (DRQ) | FDC chip select and DMA request |
| CD0–CD7 | U300.36…29 ↔ U301.12…5 (DAL7…DAL0) | DMA data bus FDC↔DMA |
| CR/W | U300.25 ↔ U301.2 (R/W) | |
| CA1 / CA2 | U300.26, .27 → U301.4, .3 (A1, A0) | register address |
| RDY | U300.38 → U400.135 | DMA ready/status |
| DCYC | U400.138 → U300 | DMA cycle acknowledge |
| HDCS / HDRQ | U300.24, .23 → J301 (DB-19 ACSI) | **off-board** hard disk DMA |
| ACK | U300.37 ← J301 | **off-board** |
| STEP / DIRC / WG / WD / MO / RD / DDEN | U301.16, .17, .21, .22, .20, .19, .26 → J303 (34-pin) and J306 (14-pin DIN) | **off-board** drive control |
| TR00 / WPRT / IP | U301.23, .25, .24 ← J303/J306 | **off-board** drive status |
| MR | U301.13 ← reset circuit | |
| FDD POWER | J302 | **off-board**, 4-pin |

### 4.6 Controllers (sheets 2, 4, 5)

| Net | Endpoints | Notes |
|---|---|---|
| JOYAH / JOYAL / JOYWE / JOYWL | U400.99, .100, .101, .102 → J500/J501 via 74LS244 (U509/U510) | **off-board** DB-15 joystick ports |
| BUTTON | U400.103 ← J500/J501 fire buttons | **off-board** |
| PEN | U400.PEN ← light pen / J500 pin 4 | **off-board** |
| PAD0X / PAD0Y / PAD1X / PAD1Y | U400 → 74LS244 → J500/J501 | analogue paddle inputs into the GSTMCU ADC |
| PADRST | U400.98 | paddle/pad reset |
| IOA0–IOA7 | U202.21…14 ↔ keyboard + joystick matrix | shared with sound port |

### 4.7 System coordination, reset, power

| Net | Endpoints | Notes |
|---|---|---|
| RESET | U104 (555) → through the reset network to U400, U100, U301.MR (13) and U203; also S100 | active low; the exact fan-out beyond the 555 output is drawn partly as net labels and is not fully traceable in this redraw |
| MFPCS | U400.MFPCS → U203.48 (CS) | active low |
| CMPCS | U400.139 → (68000 shadow / cartridge) | out-of-scope consumer, listed for completeness |
| N6850 | U400.48 → U200/U201 (ACIA CS) | out of scope, shares a GSTMCU pin |
| DINT | U400.17 → MFP / 68000 interrupt | disk interrupt |
| FDINT / HDINT | U400 → IPL chain | floppy / hard-disk interrupt |
| BINT / SINT | U400 → blitter interrupt / sound interrupt | |
| EINT3 / EINT5 / EINT7 | U400.30, .31, .32 | MFP external interrupt inputs (parallel/serial/MIDI) |
| TEST | U400.TEST | factory test, tied inactive |

---

## 5. Subsystem grouping

Each IC and net assigned once. Where a part serves two subsystems it is assigned to the one
that owns its pins, with the secondary use noted.

### 5.1 Video

ICs: **U401** (GST Shifter), **U403** (MC1377 encoder), **U405** (74S257 RGB/mono mux),
**U500/U501** (74F374 pixel pipeline), **U511** (74LS373 palette latch),
**U304/U306** partly (DE/LOAD pipeline), **U209** partly (video combine gate).

Nets: `MD0–MD15`, `PD0–PD15`, `SD0–SD7` (shared with sound), `R0–R3/G0–G3/B0–B3`,
`MONO`, `COLOR`, `DE`, `BLANK`, `HSYNC`, `VSYNC`, `SLOAD`, `SREQ`, `SCLK`, `FCLK`, `LATCH`,
`MWE`, `MWD`, `MWK`, `J600` (monitor/Peritel, off-board).

### 5.2 Sound

ICs: **U202** (YM2149), **U508** (LMC1992), **U502/U503** (DAC0802), **U504/U507** (LF347),
**U505/U506** (MF4-100 filters), **U513/U514** partly (LM556 used for the audio/paddle
timing? — assigned to disk/controllers, see §6).

Nets: `SNDCS`, `SNDIR`, `SD0–SD7`, `LSI/LTI/LTO/LAO/RAO/RTO/RTI/RSI`, `ACC`,
`AUDIO L/R` → J502/J503 (off-board).

### 5.3 Disk / storage

ICs: **U300** (DMA controller), **U301** (WD1772), **U305** (7406 drive buffers),
**U307** (74LS244, ACSI data), **U302** (74LS245, data bus side), **U513/U514** (LM556 —
floppy/ACSI timing).

Nets: `FCS`, `FDCS`, `FDRQ`, `CD0–CD7`, `CR/W`, `CA1`, `CA2`, `RDY`, `DCYC`, `HDCS`,
`HDRQ`, `ACK`, `STEP`, `DIRC`, `WG`, `WD`, `MO`, `RD`, `DDEN`, `TR00`, `WPRT`, `IP`, `MR`,
plus `J301`, `J302`, `J303`, `J304`, `J306` (all off-board).

### 5.4 Controllers

ICs: **U400** (GSTMCU joystick/paddle/pen ADC and port logic), **U509/U510/U512** (74LS244),
**U202** (YM2149 port A/B), **U632** (74LS244), **U513/U514** partly.

Nets: `JOYAH`, `JOYAL`, `JOYWE`, `JOYWL`, `BUTTON`, `PEN`, `PAD0X/PAD0Y/PAD1X/PAD1Y`,
`PADRST`, `IOA0–IOA7`, `IOB0–IOB7`, plus `J500`/`J501` (off-board).

### 5.5 Power / reset

ICs: **U104** (555 reset timer), **U204/U205** (TL497A switching regulators),
**LM78L82** regulator, PSU (sheet 7, Mitsumi SR98 CA070086-002), **U513/U514** partly
(power-fail timing).

Nets: `RESET`, `POWER` (J100, off-board), `+5 V`, `+12 V`, `−12 V`, `GND` (excluded from
§4).

### 5.6 RAM access and refresh

ICs: **U400** (MMU/refresh/RAS-CAS generation), the 16 DRAMs and 4 SIMM sockets
**U600–U603**, **U101** partly (blitter writes through the same path).

Nets: `MAD0–MAD9`, `RAS0/RAS1`, `CAS0H/CAS0L/CAS1H/CAS1L`, `WE`, `MWE/MWD/MWK`.

### 5.7 CPU and buses

ICs: **U100** (68000), **U101** (blitter, bus master), **U302** (74LS245 data buffer),
**U307** (74LS244 address/control buffer), **U102/U103** (TOS ROMs), **U209** (74LS86
decode), **U210/U305** (7406 open-collector drives).

Nets: `D0–D15`, `A1–A23`, `AS/LDS/UDS`, `R/W`, `DTACK`, `BERR`, `FC0–FC2`, `VMA/VPA`,
`BR/BG/BGACK`, `CLK`, `ROM0–ROM6`.

### 5.8 System coordination

ICs: **U400** (GSTMCU — decode, clocks, interrupts, bus arbitration), **U203** (MFP —
timers, interrupt controller), **U900** (74LS164 keyboard/MIDI serialiser),
**U200/U201** (ACIAs — out of scope, share `N6850`).

Nets: `MFPCS`, `CMPCS`, `DINT`, `FDINT`, `HDINT`, `BINT`, `SINT`, `IPL0–IPL2`, `IACK`,
`EINT3/EINT5/EINT7`, `CLK8/CLK4/CLK2`, `KHZ500`, `N6850`, `TEST`.

**Out of scope but sharing pins:** `N6850` (U400.48), `CMPCS` (U400.139), `EINT3/5/7`
(parallel, serial and MIDI interrupt inputs), the cartridge bus on `J103`, and the two
6850 ACIAs. These are listed only because they occupy in-scope GSTMCU pins.

---

## 6. Open questions

1. **The redrawn schematics are a 2017 CAD redraw, not Atari artwork.** Everything above
   comes from that redraw except the parts list, which is a retyped transcript of Atari's.
   A pin-level check against a photographic scan would be worth doing for the GSTMCU
   before any emulator depends on pin numbers.
2. **U400's bottom-edge pin numbers are unresolved.** That is roughly half the GSTMCU:
   the CPU bus (`AS`, `LDS`, `UDS`, `R/W`, `DTACK`, `BERR`, `RESET`, `VMA`), `D0–D15`,
   `A1–A23`, the pad/pen ADC pins and the interrupt outputs are legible by *name* only.
   Closing this needs either the original D-size plot or a higher-resolution scan.
3. **`HSYNC` on U400 extracted with pin number 146**, which does not exist on a 144-pin
   package. The number is wrong; the signal is real.
4. **YM2149 data/control pins and both 6850 ACIA pin numbers could not be paired.** The
   redraw draws those symbol rows with zero text offset, which defeats the pairing rule.
5. **Designator disagreements between the two documents:**
   * 555 timer: **U104** in the 1991 parts list and on the 1991 sheet 1, **U106** in the
     1989 BOM.
   * 74LS164: **U900** (1991) vs **U211** (1989).
   * Blitter: 1989 BOM calls it `C101643 IC ST BLITTER CUSTOM 60PIN` at U101; the 1991
     list calls it `C101643 IC ST BLITTER CUSTOM 68 PIN` at U101. The package is a 68-pin
     PLCC socket, so 68 is right and "60PIN" is the 1989 list's error.
   * 74LS244 vs 74LS266: the 1989 BOM reads `C101611-266 IC 74LS266 OCTAL TRI-STATE 20P`
     at U307,509,510,512,632; the 1991 list reads `C101611-244 IC 74LS244 DIP 20P` at the
     same locations. 74LS244 is a 20-pin octal buffer, 74LS266 is a 14-pin quad XOR — the
     pin count in the description proves **74LS244** is correct.
6. **Two part numbers share location U405** in the 1991 list (`C101610-257` 74S257 and
   `C300259-257` 74F257) and the LM78L82 regulator is printed at **U500**, which is also a
   74F374. These look like transcription errors in the retyped parts list; the drawing
   shows one 74S257-type mux at U405 and the regulator elsewhere.
7. **The 1989 BOM has no GSTMCU at all**, and no separate entry for the 144-pin chip; the
   part (`C300589-001`) appears only in the 1991 list. Anyone reconstructing the board from
   the 1989 document alone would conclude the STE has no GSTMCU, which is why the 1991
   manual was chosen as primary.
8. **Blitter population.** The parts list and sheet 1 include a discrete blitter at U101;
   the board-generation note in `02-atari-ste-hardware-overview-for-emulation.md` §1.2 says
   later CA4003290 boards drop it and fold the function into the GSTMCU. This inventory
   describes the **with-blitter** population. A no-blitter board differs in exactly one
   package (U101) and its artefact nets; the GSTMCU pinout is unchanged as far as this
   drawing shows.
9. **Part-number OCR damage in the 1991 list** (it is a retyped document with visible OCR
   artefacts): `0398739-001` for the DMA (should be `C398739-001`), `01001 17` for the
   MC1377, `IC ST GLITTER` for BLITTER, `0398088`-style splits in the oscillator rows.
   Numbers in §2 are given as printed, with the obvious corrections noted.
10. **RAM population is not fixed by the board.** 520STE = one bank (2 SIMMs), 1040STE =
    two banks. The inventory lists four sockets because the PCB has four; which are
    populated is a configuration question, not a board-revision question.
11. **Peritel vs modulator.** Sheet 1 carries a "Stuffed in NON-Modulator Versions ONLY"
    block (`U402` 32.084988 MHz Peritel oscillator, RF output components) and a
    "Stuffed in PAL systems only" note. For the French Peritel target the 32.084988 MHz
    oscillator is fitted and the 32.215905 MHz NTSC one is not. Parts-list quantities
    cannot distinguish the two variants.
12. **LM556 assignment is uncertain.** U513/U514 appear on sheet 3 (disk) region and on
    sheet 5 in the audio/controller area; the two sheets show them in different contexts
    and I could not determine which subsystem each half serves.
