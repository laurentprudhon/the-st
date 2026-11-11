# Is there a "Super Hi-Res" mode on the Atari STE? — and what `MWE`/`MWD`/`MWK` really carry

Research note for ticket [#27](https://github.com/laurentprudhon/the-st/issues/27), 2026-11-11.
Claim labels: **`documented`** (a primary source states it), **`implemented`** (code does it),
**`absent`** (the logic is not there), **`disputed`** (sources contradict each other).

## 1. The question

Ticket #27's sixth sub-question asks whether the STE's fourth resolution — "Super Hi-Res", reached
through the Shifter's `MWE`/`MWD`/`MWK` pins — is implemented or declared a stub. The ticket inherited
the premise from `design/crossings.md` §2 ("a Super Hi-Res write", `Memory → Video`) and
`design/board-inventory.md` §4.2 ("shifter memory write path for Super Hi-Res", `U401.29–.31 ← U400`).

## 2. Answer: the STE has no Super Hi-Res mode, and the three pins are the Microwire link

**There is no fourth resolution on this machine.** `documented` by Atari's own
*STE Developer Addendum* (5/25/1989, Rob Zdybel, Atari Corp.), whose complete list of the STE's new
features is: *"Extended color palette of 4096 colors, from 512 · Hardware support for horizontal and
vertical scrolling · Ready for external GENLOCK · Stereo 8 bit PCM sound · Light gun, paddle and new
joystick ports"*. Its register pages add only `FF8240–FF825E` (palette), `FF8264` (HSCROLL), `FF8206`
(line offset) and `FF8204` (base low) — no mode register beyond `FF8260`'s two bits, no fourth mode.

The *Atari STE FAQ* (The Paranoid/Paradox) lists the same feature set — *"4096 instead of 512 colours ·
Horizontal and Vertical hardware scrolling · Blitter · 8 Bit DMA stereo sound · National LMC 1992
soundchip, connected over Microwire serial port"* — and no extra resolution. **`absent`** in all three
vendored emulators: Hatari, Steem SSE and NeoST have no STE Super Hi-Res path. (Hatari's
`RES_SUPERHIRES` in `src/cpu/custom.h` is the *Amiga* AGA constant inside the vendored Musashi CPU
header, unused by the ST/STE code.)

The name belongs to the **TT** Shifter (640×480 in 16 colours / 320×480 in 256 colours, registers at
`$FFC000`–`$FFC2xx`), and to the Amiga's SuperHiRes chipset — not to a 1040STE.

**The repo's two sources for the claim both fail on inspection:**

| Artefact | What it says | Why it cannot be this machine |
|---|---|---|
| `atari-st-hardware/ste-enhancements/02-super-hires.md` | 640×480, 16 colours, control at `$FFC000`/`$FFC200`, "GST Shifter (C029145)" | `$FFC000` is the **TT** Shifter's address space; the STE Shifter answers at `$FF82xx`. The page also contradicts itself on total memory and visible lines |
| `atari-st-hardware/tos/01-tos-hardware-versions.md` | "TOS 2.x (STe) … Super Hi-Res modes: 640×256, 640×512, 320×512, 320×260, 320×120, 160×120" | no Atari document, no emulator and no STE register map contains these modes; the list is `unconfirmed` and matched by no source in this repo |

## 3. `MWE` / `MWD` / `MWK` are Micro*W*ire, driven by the Shifter into the LMC1992

`documented` by the ST Magazine n°44 STE hardware study (Sept 1990) — the same article
`research/bus-arbitration.md` already cites for this board — in its GST Shifter section:

> "The GST Shifter ensures also the communication with circuit LMC1992 (controller hi-fi of volume and
> tonality) thanks to the signals \*MWE, MWD, and MWK following to the *Microwire Standard*. … The data
> are serially transmitted by MWD (MicroWire Data) in synchronization with signal MWK (MicroWire clocK)
> provided that the signal \*MWE (MicroWire Enable) is active. The message is on 11 bits: the 2 upper
> bits are always 1 and 0 to select the circuit, the 3 following bits are the circuit register number
> which corresponds to a command and the 6 lower bits are the data for the selected register."

Corroborated three ways:

1. **The LMC1992's pinout** (`design/board-inventory.md` §3): "the first three pins constitute the
   Microwire interface and are connected to the three corresponding signals of the Shifter".
2. **Atari's own programming example** (*STE Developer Addendum*): `MWMASK equ $ffff8924`,
   `MWDATA equ $ffff8922`; the routine writes `$07ff` into the mask register, polls it until it reads
   `$07ff` again (transfer complete), then writes the command word — an **11-bit** message, matching
   the three-bit register number plus six data bits.
3. **The reconstruction** (`gstshifter.v`, `implemented`): writing `$FF8922` (`A == 6'h11`) starts a
   16-bit shift at 1 Mbit/s (one bit every 8 clocks of the 8 MHz tick), the **mask register rotates**
   (`mw_mask_reg <= {mw_mask_reg[14:0], mw_mask_reg[15]}`) and drives `mw_clk`, `mw_done` marks the
   end, and **writing `$FF8924` during a transfer aborts it** (`mw_cnt <= 0`, the author's note:
   "Systematic Error demo relies on it").

**So the inventory row is wrong in three ways at once**: direction (`in` — they are the Shifter's
*outputs*), driver (`← U400` — they go to the LMC1992, i.e. Video → Sound), and purpose
("memory write path for Super Hi-Res" — they are the Microwire control link). The catalogue row in
`design/crossings.md` §2 inherited all three errors.

## 4. What this changes

| Artefact | Wrong | Correct |
|---|---|---|
| `design/crossings.md` §2 | `MWE`/`MWD`/`MWK`, driver Memory, subscriber Video, "a Super Hi-Res write", ~0/frame | The **Microwire link**: driver **Video**, subscriber **Sound**, class 3 for `MWK` (clock) + data for `MWD`, one class-1 record per **completed transfer** (≤ a few per frame, 16 in the RTL, 11 bits documented) |
| `design/board-inventory.md` §3/§4.2 | `U401.29–.31` "in ← U400", "shifter memory write path for Super Hi-Res" | outputs of U401 to the LMC1992's Microwire pins; net function = MicroWire Enable/Data/clocK |
| `design/subsystems-spec.md` §1 Video row | "Super Hi-Res interpretation" as a Video function | the **Microwire controller** it already lists; no fourth resolution exists to interpret |
| Ticket #27, sub-question 6 | "Super Hi-Res implemented or a declared stub" | the question **dissolves**: there is nothing to implement, and the pins belong to the Microwire seam (sub-question 7) |
| `atari-st-hardware/ste-enhancements/02-super-hires.md` | describes a TT mode as an STE one | `disputed`; not a source for this machine |

## 5. What is still unknown

1. **The schematic's own net function for `U401.29/30/31`** — the inventory reads them as inputs from
   `U400`, the article and the LMC1992 pinout make them outputs to `U508`. Sheet 4 at 600 dpi, or a
   continuity test on a real board, settles it. Nothing in the model depends on the pin direction; the
   *driver chip* is what the catalogue row needs, and Video is the only candidate.
2. **Where the Microwire transfer's timing lives.** The RTL shifts a 16-bit field; Atari documents an
   11-bit message; Hatari polls the mask and shifts 16 times at 8 CPU cycles each. Whether the model
   must count bit-clock edges at all, or can record one command per completed transfer, is a design
   question for #27 (sub-question 7) and #26.
3. **`$FF8920`/`$FF8921` (SAMPMOD)** is decoded inside the Shifter in the reconstruction
   (`gstshifter.v:119,162`) and listed as *Memory's* in `design/subsystems-spec.md` §1. That is #26's
   seam, not this ticket's; recorded here so it is not lost.

## 6. Sources

- *STE Developer Addendum*, Atari Corp., 5/25/1989 (Rob Zdybel) — feature list, `FF8240–FF825E`,
  `FF8264`, `FF8204`, `FF8206`, and the `MWMASK`/`MWDATA` routine. Copy:
  `goto10retro.com` mirror, `Ste_Dev_Add_5_25_1989.pdf`.
- *Atari STE Hardware*, ST Magazine n°44, Sept 1990 (HardmaSTer), English translation by DrCoolZic,
  `info-coach.fr/atari/hardware/STE-HW.php` § "The GST Shifter".
- *Atari STE FAQ* compiled by The Paranoid / Paradox (`atari-wiki`, `Number0000009/atari-wiki`).
- `gyurco/gstmcu` — `hdl/gstshifter.v` (Microwire state machine, `A == 6'h11`/`6'h12`).
- Vendored: `hatari/` (`ioMemTabSTE.c`, `video.c`, `dmaSnd.c`), `steemsse-V4.2.0_R10/`, `neost/`.
