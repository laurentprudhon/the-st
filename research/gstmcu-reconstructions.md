# Open-source RTL reconstructions of the Atari ST/STE chipset

Research note for the-st (Atari 1040STE emulator). Scope: FPGA/HDL reconstructions of
GSTMCU (MMU+GLUE), Shifter, DMA, MFP — what they *implement*, with `file:line` evidence.

Claim labels used throughout:
- `implemented` — the RTL logic actually does this.
- `commented` — a comment asserts it; code may not.
- `absent` — the logic is not present in that project.

All line numbers were taken from `git clone --depth 1` on 2026-11-01; they refer to the
HEAD described in the project table.

---

## 1. Projects found

| Project | Repo | Language | Licence | ST vs STE | Last activity |
|---|---|---|---|---|---|
| **gstmcu** (standalone sim model) | `gyurco/gstmcu` | Verilog-2001 | GPL-2.0-or-later (`hdl/gstmcu.v:7-9`) | **STE GSTMCU + GST Shifter**, plus `st` switch to model plain ST | 2025-01-03 (`4a6db7c`) |
| **MiSTery / AtariST_MiSTer** | `MiSTer-devel/AtariST_MiSTer` | Verilog + SystemVerilog | GPL-3.0 (repo `LICENSE`) | **ST/STE** ("Atari ST/STe for MiSTer", `README.md:1`) | 2026-06-04 |
| fx68k (CPU only) | vendored at `AtariST_MiSTer/rtl/fx68k` | SystemVerilog | see subdir | n/a — 68000 core, no MMU | pinned in-tree |
| stBlitter | `AtariST_MiSTer/rtl/stBlitter.sv` | SystemVerilog | Jorge Cwik | ST/STE blitter incl. HOG | in-tree |
| zerkman/zest | moved to **codeberg.org/zerkman/zest** (GitHub repo is a 3-line stub) | VHDL | see repo | **ST only** — separate `hdl/mmu.vhd`, `hdl/glue.vhd`, `hdl/shifter.vhd` | 2025-06-30 (GitHub stub commit date) |

Notes on lineage — this matters for how much weight to give each:

- `gstmcu/README.md:5-6`: "model tries to be an exact replica based on the schematics
  recovered by Christian Zietz", containing "both the identical gate-level circuits with
  asynchronous clocking, and a synchronous model hopefully suitable for FPGA synthesis".
  Source schematic: `ST4081S.PDF`, present in `AtariST_MiSTer/doc/ST4081S.PDF`.
- `gstmcu/README.md:8-9`: video shifter core "based on the reverse engineering work by
  Jorge Cwik" (atari-forum thread 29658).
- `AtariST_MiSTer/README.md:5-9` claims "Cycle accurate STe GLUE+MMU combo
  (re-created from the original schematics)" — this is project marketing; the *code* is
  the same family as gstmcu (`rtl/gstmcu/*.v`, 1115 lines vs 1155), authored by the same
  person (Gyorgy Szombathelyi). Treat the two as one model in two packagings: gstmcu is
  the reference/simulation one, MiSTer's copy the synthesis one.

Because the two GSTMCU copies are near-identical, citations below are to **gstmcu**
(`/opt/data/cache/scratch/gstmcu/hdl/`) unless the MiSTer copy differs. I diffed them
directly: the *arbitration and timing expressions are byte-identical* — e.g.
`addrselb` at `gstmcu.v:705` == `AtariST_MiSTer/rtl/gstmcu/gstmcu.v:688`, `vidclkb` at
`764` == `747`, `DCYC_N` at `765` == `748`. The differences are I/O plumbing only
(OE_L/OE_H, KHZ500, BLANK_H/V, MDE60/PAL, `mlatch` vs `stlatch`). So the two projects
corroborate each other rather than providing independent evidence — **one model, one
source (Zietz's ST4081S schematics)**.

---

## 2. Q1 — MMU/GSTMCU bus arbitration: 2-clock slots, round-robin?

**Answer: yes for the slot structure; "round-robin, no priority" is *not* what the RTL
expresses — there is a fixed priority encoded in the address mux and in the enable
equations.**

The timing base is an 8-phase 16 MHz generator, `clockgen.v`. Two of its phases are named
in the source as exactly the arbitration events (`implemented`):

- `clockgen.v:94` — `reg time5_s; // addrsel`
- `clockgen.v:96` — `reg time7_s; // cycsel`
- `clockgen.v:54-60` — the phases are a ripple chain clocked on alternating edges of the
  16 MHz `clk` and wrapping every 8 phases, so each `timeN` is a single 16 MHz-period
  pulse. **Eight phases = 4 clocks at 8 MHz = exactly two DRAM cycles** — which is the
  structural basis for alternating internal/external slots of 2 clocks each.

Slot selection (`implemented`), `gstmcu.v:705-707`:

```
wire addrselb = turbo ? ~time4_t : ~time5_s;
wire lcycsel  = turbo ? time4_t  : time7_s;
wire cycsel_en= turbo ? (time3_t & ~time4_t) : (time6_s & ~time7_s);
```

`addrselb` is a **level**, high except during the single phase-5 pulse. Read literally:
the address bus carries the *external* (CPU/DMA) address only while the phase-5 pulse
holds `addrselb` low, and holds an *internal* (video/sound/refresh) address for the other
seven phases. RAS is committed twice per 8-phase window off that level
(`implemented`), `gstmcu.v:619-621`:

```
wire rascyc = turbo ? ~time3_t : time0_s;
assign RAS0_N = ~( (rascyc & addrselb & (~refb | (ram1a & vos))) | (~rascyc & ~addrselb & ram1a & ~ramcycb) );
```

i.e. one RAS assertion in the `rascyc` (phase-0) half using the internal address and one
in the `~rascyc & ~addrselb` half using the external address — **two DRAM cycles per
window, one internal and one external, in fixed alternation**. A coarser encoding of the
same thing is exported for existing code (`gstmcu.v:638`):
`assign bus_cycle = { ~time4_s, ~time0_s };`

Caveat on precision: the *alternation* is clearly `implemented`; the exact phase-to-client
mapping inside each half is my reading of the level polarities, not a comment in the RTL.
gstmcu never writes down "internal then external".

The actual owner arbitration is **not** a counter. It is a 4-bit `casez` priority encoder
on the address mux (`implemented`), `gstmcu.v:133-138`:

```
casez ({ addrselb, ixdmab, snden, refb })
    4'b00??: ADDR = dma_reg;                 // DMA_ADDR
    4'b01??: ADDR = A;                       // CPU_ADDR
    4'b1??0: ADDR = 0;                       // REFRESH_ADDR
    4'b1?11: ADDR = { 2'b0, snd };           // SND DMA ADDR
    4'b1?01: ADDR = vid_reg;                 // VIDEO ADDR
```

`addrselb=0` is the external half: DMA chip first (`ixdmab=0`), CPU second — a fixed
priority, not round-robin; the CPU only gets the slot when the DMA/blitter is not asking.
`addrselb=1` is the internal half. Within internal, `casez` order is refresh → sound →
video, but sound and refresh are mutually exclusive by construction (`mcucontrol.v:63`
`refb = pk016 | pk024`, `mcucontrol.v:95` `snden = ~pk016 & pk024`), so the internal slot
is claimed by whichever request is asserting, with **refresh taking the slot whenever the
sound request FF is clear**.

Bus mastership / bus grant is a separate, purely combinational handshake on the 8 MHz
clock (`implemented`), `gstmcu.v:546-551` + `583-600`:

```
wire p9033_i = (ready & ~ias) | (p9033 & ias);
wire ixdma_i = ixdma | (~ias & ~BG_N);
wire br_o_i  = (br_n | br_o) & (BG_N | br_o) & p9033 & bgack_n;
```
and the address-mux term `ixdmab = ~ixdma` (`gstmcu.v:546`). `assign BGACK_N_O = ixdmab;`
(`gstmcu.v:549`) — the DMA chip only sees BGACK when the current bus cycle has actually
finished, which is why DMA/blitter bus steals are cycle-granular and never split a 4-clock
68000 bus cycle.

**What is *absent*:** there is no round-robin rotate register anywhere in `gstmcu.v`; I
found no `rr`/`rotate`/`last_grant` state. The "video always gets 4 slots, sound gets 1,
refresh gets the leftovers" behaviour falls out of the three request FFs, not a scheduler.

---

## 3. Q2 — Video fetch groups: 4 consecutive slots or interleaved?

**Answer: neither, cleanly — and that is the finding.** The RTL has no 4-slot burst
counter. It advances a video address counter **once per 8-phase window** (500 ns) while
the display is active, and the four-word group is reassembled *inside the shifter* from a
4-deep load pipeline fed by the individual DRAM cycles. Whether those four cycles fall in
consecutive slots is then a consequence of the request FFs, not of a fetch state machine.

Fetch pacing (`implemented`), `gstmcu.v:764`:

```
wire vidclkb = turbo ? ~(time0_s | vidb) : ~(time5_s | vidb); // vidclk is always single speed
```

`vidclkb` is the video *address counter* clock (not an enable): `vidcnt.v:11`
`wire c = ~vidclkb;`, so `vidcnt` advances on the rising edge of `c = time5_s | vidb` —
i.e. **once per 8-phase window, at phase 5, and only while `vidb` is set**. `vidb` is the
registered display-enable: `gstmcu.v:825` `wire ideb = ~DE;` → `mcucontrol.v:87`
`vidb <= ideb;` at the `c1_en_p` strobe (c1 = `~(lcycsel & time1)`, `mcucontrol.v:51`).
So video fetches are confined to the DE window at a fixed one-per-500 ns cadence, and
`vidb` gates the *whole* video client rather than individual slot grants.

Group assembly in the shifter (`implemented`) — a 4-deep word pipeline that shifts on
every `LOAD` (`shifter_video.v:35-40`):

```
if (~LOAD_D & LOAD) begin
    shdout_next3 = DIN;   shdout_next2 = shdout3;
    shdout_next1 = shdout2; shdout_next0 = shdout1;
end
```
and a 4-bit shift register that counts the loads to decide when to reload the pixel
shifters (`shifter_video.v:83`):

```
else if (~LOAD_D & LOAD) rdelay = { 1'b1, rdelay[3:1] };
```
with `Reload <= &pixCntr` (`shifter_video.v:106`) and `pixCntr` preset to `4'h4`
(`shifter_video.v:96`). **Four loads ⇒ one reload ⇒ one 16-pixel group.** The `LOAD`
input of the video shifter is wired to `DCYC_N` (`gstshifter.v:43`,
`input LOAD_N, // DCYC_N`), so on the STE the word-load strobe *is* the DCYC signal —
a fact worth carrying into an emulator: video fetch and DCYC are the same edge.

Whether the four slots are consecutive is therefore a property of when `vidb`/`snden`/
`refb` are asserted, not of a 4-state fetch machine. The RTL contains **no 4-slot burst
counter** and no "video owns 4 slots" arbiter — `absent`.

---

## 4. Q3 — Refresh: period, length, does it preempt video?

- Request generation (`implemented`): `mcucontrol.v:63` `assign refb = pk016 | pk024;`
  where `pk016` is `hde1` sampled (`mcucontrol.v:75,88`) and `pk024` is the sound-DMA
  request `sreq` sampled (`mcucontrol.v:123,148`). A refresh therefore rides on the same
  FF pair that schedules sound DMA: **refresh is granted whenever neither of those two
  internal requests is pending**, i.e. it is the idle internal client, not a timer.
- Length: one internal (2-clock) slot — the address mux branch `4'b1??0` is a single
  `ADDR` selection (`gstmcu.v:136`). RAS-only, `gstmcu.v:620-621` gates `RAS0_N`/`RAS1_N`
  with `(~refb | (ram1a & vos))` — refresh asserts RAS without CAS.
- Does it preempt video? **Not during DE** — because `refb` and `snden` are built from
  `pk016 = hde1`, i.e. the refresh client is only offered the slot when the horizontal
  display window is closed. The RTL never arbitrates refresh against an in-flight video
  group; `vos = ~(vidb & ~snden)` (`mcucontrol.v:64`) further masks the RAM bank select.
- **Refresh counter: `absent`.** There is no row-address counter in the RTL, and the
  author says so at `gstmcu.v:23-27`:
  > `// TODO for replacing a real chip: // - paddle, pen counters // - refresh address generation // - memory address multiplexer`

  The refresh address is hard-wired: `gstmcu.v:136` `ADDR = 0; // REFRESH_ADDR`. So these
  models refresh *every* row every line with CAS-less RAS pulses and do not model the
  8-bit refresh counter at all. Any emulator behaviour that depends on *which* row is
  refreshed (e.g. bus contention artifacts, refresh-address aliasing) cannot be sourced
  from this RTL.

---

## 5. Q4 — Sound DMA placement

- Request / grant (`implemented`): `mcucontrol.v:95` `assign snden = ~pk016 & pk024;`
  and `mcucontrol.v:94` `assign sndclk = ~(addrselb & snden);` — the sound address counter
  (`sndcnt.v`) is clocked exactly when the sound slot is granted, so **one sound fetch =
  one internal slot = 2 clocks**.
- Placement relative to DE: `snden` requires `pk016` (i.e. `hde1`) **high**, i.e. sound
  DMA is given the internal slot inside the display window — the complement of the
  refresh condition. So on a line where sound is active: one slot goes to sound, the rest
  to video; where sound is idle the slot falls to refresh.
- Load strobe (`implemented`): `gstmcu.v:769` `assign SLOAD_N = sload_n_loc | (time1_s & turbo);`
  with `mcucontrol.v:151` `sload_n_l(... ~(time1 & addrselb & snden), sload_n);` — the
  sound register file latches at **phase 1** of the granted slot.
- `sndclk_en` (`gstmcu.v:768`): `(turbo ? (~time0_s & ~time7_s) : (addrselb & time4)) & snden`
  — in normal speed the sound counter enable is gated on phase 4 + `addrselb`.
- The sound address counter itself is a plain 21-bit ripple counter reset by `sframe`
  (`sndcnt.v:14` `wire l = ~sframe;`), incrementing once per `sndclk`. Frame compare is
  `mcucontrol.v:98` `wire pk061 = (snd == sft) & ~c1;` and `stoff` (`mcucontrol.v:100`)
  ends the transfer — this is the end-of-frame/loop logic a software emulator needs for
  `$FF890x` frame-end interrupts.

---

## 5b. Independent corroboration: zerkman/zest (VHDL, ST-only)

This is the one genuinely **independent** implementation found (different author, different
language, ST rather than STE), so it is useful as a cross-check on Q1-Q3. It is *not* an
STE source — do not take STE behaviour from it.

- Its slot counter is an explicit 2-bit `cnt` at 16 MHz: `hdl/mmu.vhd:78`
  `signal cnt : unsigned(1 downto 0);` incremented on the fast clock enable
  (`mmu.vhd:340` `cnt <= cnt + 1;`). Four phases = one 8 MHz bus cycle — the same
  2-clock slot quantum as gstmcu, reached by different code.
- CPU/DMA RAM access is **phase-quantised**, same conclusion as gstmcu Q5.9
  (`implemented`), `mmu.vhd:233-234`:
  > `if (cnt = 1 and (DMAn = '0' or (RAMn = '0' and iRWn = '1' ...))`
  > `or ((cnt = 1 or cnt = 2) and RAMn = '0' and iRWn = '0' ...)`

  ⇒ reads granted at phase 1, writes at phases 1-2.
- Video fetch has **one dedicated phase, not a 4-slot burst** (`implemented`),
  `mmu.vhd:246-247`: `if ((en8fck = '1' and cnt = 2 and mode_bus = '0') or cnt = 3) and sde = '1' ...`
  drives `log_adr <= video_ptr`. Corroborates gstmcu Q2: the video pointer advances at a
  fixed phase rather than from a 4-slot burst counter — neither implementation contains a
  "video owns 4 consecutive slots" state machine.
- DTACK generation is also phase-pinned: `mmu.vhd:333-338` sets `sdtackn <= '1'` at `cnt = 0`
  and `cmpcsn_en <= '1'` at `cnt = 2`.

So Q1-Q3 conclusions are supported by two independent authors; Q4/Q5 (sound DMA, STE
quirks) rest on gstmcu alone.

---

## 6. Q5 — Undocumented behaviour worth harvesting

Each item below is something the RTL does that a doc-driven emulator would get wrong.
Label in brackets.

1. **VSYNC counter double-reload on STE.** `gstmcu.v:947-955`:
   > "From the STE schematics, it seems the counter can be reloaded second time to another
   > value if the mode registers are changed after the first reload (in the VBL handler for
   > example). Confirmed by Level 16 fullscreen demo behavior on a real STe."
   The code implements it as an alternating toggle (`vsc_load <= ~vsc_load` inside the
   `vsc == 511 || vsc_load` branch, STE path) and explicitly notes "ST should reload the
   counter only once". **[implemented + commented]** This is the single most unusually
   well-attested undocumented fact in the corpus — the author names the demo used to
   confirm it on hardware.
2. **VSYNC set point is 508/510, not 512**: `gstmcu.v:938`
   `wire [8:0] ivsync_set = mde1 ? 9'd510 : 9'd508;` **[implemented]**
3. **Short lines at the DE edges leave words unloaded.** `shifter_video.v:110-111`:
   > "mid and hi res leave 2 or 1 words unloaded when STe hard scroll is used"
   implemented as `if (!rdelay[0] && !(scroll & !DE)) Reload <= 1'b0;` **[implemented + commented]**
4. **Pixel counter overshoot after the last reload**: `gstshifter.v:292-295` sets the
   counter to `6'h1f` (mid) / `6'h3f` (mono) — "31 pixels after the last reload" /
   "63 pixels after the last reload". **[implemented + commented]** Relevant to right-border
   and horizontal-scroll artifacts.
5. **Color bit order is 0321 on STE, 0123 on ST** — `gstshifter.v:236-237`
   ("For ST compatibility reasons the STE has the color bit order 0321"). **[commented]**
   A palette write through the ST path silently permutes bits.
6. **Blitter HOG vs cooperative mode — 64-cycle bus-hold timeout.**
   `stBlitter.sv:235-237`:
   > "Non HOG DMA cycle counter. Counts 64 bus cycles (any bus cycles).
   >  BLITTER Buglet: Starts counting as soon as BUSY is set."
   implemented as a 7-bit counter incremented on every `AS` falling edge while busy, with
   `wire timeOut = tmoCntr[6];` (`stBlitter.sv:239`) and
   `wire stopBlit = (timeOut & !Hog) | extDmaReq | !busy;` (`stBlitter.sv:438`).
   **[implemented + commented]** In cooperative (non-HOG) mode the blitter releases the bus
   after 64 AS edges *counted from BUSY*, not from the first data cycle — so a blitter that
   is set busy but starved still burns its window.
7. **Blitter BUSY reads back as 0 while the control register is being written**, and that
   write still toggles the interrupt: `stBlitter.sv:185-190`
   ("BUSY is always deasserted while being written! This will even toggle INTn if Blitter
   still busy") with `busyMask = !ctrlWr`. **[implemented + commented]**
8. **DCYC semantics.** `gstmcu.v:765` `assign DCYC_N = dcyc_n_loc | (time5_s & turbo);`
   and `mcucontrol.v:191`:
   `mlatch dcyc_l(clk32, !porb, 1'b0, clk ^ turbo, !resb | (time1 & addrselb & viden), dcyc);`
   ⇒ DCYC is asserted when the cycle lands at phase 1, in the internal-slot half, while
   video is *not* enabled (`viden = ~vidb`). It is the same signal the STE Shifter uses as
   its word-load strobe (`gstshifter.v:43`). **[implemented]** An emulator that treats DCYC
   as a pure DMA-sound signal will mis-time STE video fetches.
9. **Slow-RAM write strobes are phase-quantised**: `mcucontrol.v:198-201` gate `we_n`,
   `wdat_n` with `~time0` and `~time1`, and `cmpcs_n` with `~time1` — i.e. the CPU's
   write window is truncated to phase 2..3 of the slot. **[implemented]** This is where
   "ST RAM is only accessible in half the bus cycle" comes from, concretely.
10. **`$FF8260` resolution register is decoded in GLUE but the value is consumed by the
    Shifter over a separate `vmapb` path**: `gstmcu.v:231` `mdesel = idev & ias & A[15:1] == {12'h826,3'b000}`
    and `gstmcu.v:246` `vmapb = ~(idev & A[15:6] == {8'h82, 2'b01})` with the comment
    "video regs also in shifter" — the register is visible in *both* chips and the shifter
    side is what actually latches the mode. **[implemented + commented]** (Note: the
    duplicate-`$FF8260` behaviour is expressed here as a shared decode, not as two
    independent registers; I did **not** find a second storage element for it —
    treat "duplicated register" claims as **not confirmed** by this RTL.)
11. **`$FF820A` sync register**: `gstmcu.v:232` `syncsel = idev & ias & A[15:1] == {12'h820,3'b101}`
    → `// FF820A-B`, feeding `hsyncgen`/`vsyncgen` polarity. **[implemented]**
12. **MFP Timer A clocked by DMA-active**: **not found** in either repo. The MFP model
    (`AtariST_MiSTer/rtl/mfp/`) does not take a DMA-active input; `mfp_timer.v:146` only
    notes a prescaler guess ("From this it can be guessed the prescaler resets at 200
    unconditionally"). **[absent]** — this is a Hatari/SteemSE-level behavioural hack, not
    an RTL-sourced fact.

---

## 7. What these RTL projects do NOT tell us

- **Refresh row address / counter** — hard-wired to 0, explicitly TODO
  (`gstmcu.v:23-27,136`). No refresh period register, no row counter, no refresh-vs-video
  preemption logic to observe.
- **Paddle and pen counters** — TODO (`gstmcu.v:24`), stubbed as `wire padrb = 1; // TODO`
  and `wire penrb = 1; // TODO` (`gstmcu.v:302-303`).
- **The memory address multiplexer** — TODO (`gstmcu.v:26`); these models emit
  `ADDR[23:1]` plus separate RAS/CAS and leave row/column multiplexing to the board.
- **No 4-slot video burst arbiter** exists, so the "4 consecutive internal slots" model
  cannot be confirmed or refuted here; the RTL only fixes *one word per granted slot* and
  the 4-load group assembly.
- **No round-robin scheduler.** Priority is a static `casez`. Any emulator claim of
  "fair round-robin" is not supported by this code.
- **Interlace**: `gstmcu.v:772` `wire interlace = 0; // investigate is it useful or not?`
  — hard-tied off. **[absent]**
- **DMAC (`RDY`/`DCYC`) handshake on the DMA-chip side** — gstmcu models only the GLUE
  side (`RDY_N_I/O`, `DCYC_N` outputs). The ACSI/DMA controller's own state machine lives
  in `AtariST_MiSTer/rtl/dma.v`, which is an ST-class DMA, **not** the STE DMA-sound
  variant; I did not find an STE `$FF890x`-capable DMAC model there — STE sound is
  handled inside the GST Shifter (`gstshifter.v`, `sndcnt.v`) instead. So the DMA-chip ↔
  MMU `RDY` timing is **only half modelled**.
- **MFP Timer A / DMA-active coupling** — absent (see Q5.12).
- **Licence/coverage caveat**: fx68k is a CPU, not an MMU; it ships no bus-arbitration
  model of the ST side. zerkman/zest is a real independent implementation (§5b) but it is
  **ST-only** — it has no STE Shifter, no DMA sound, no `$FF826x` hard-scroll registers —
  so it cannot arbitrate any STE question.

---

## 8. Highest-value undocumented facts harvested

Short list, ordered by how much an emulator would otherwise get wrong:

1. **STE VSYNC reloads twice** if the video mode registers are written inside the VBL
   handler — confirmed on hardware via the Level 16 fullscreen demo (`gstmcu.v:947-955`).
2. **VSYNC assertion at vsc == 508 (60 Hz) / 510 (mono/`mde1`)** (`gstmcu.v:937`).
3. **In cooperative mode the blitter drops the bus after 64 `AS` edges counted from BUSY**,
   including cycles where it is starved (`stBlitter.sv:235-250,438`).
4. **DCYC is also the STE Shifter's word-load strobe**, asserted at phase 1 of a slot when
   video is inactive — it is not just a sound DMA handshake (`gstmcu.v:765`,
   `mcucontrol.v:191`, `gstshifter.v:43`).
5. **Sound DMA and refresh share one slot-request FF pair**: sound gets the internal slot
   inside DE, refresh gets it when sound is idle; they are mutually exclusive by
   construction (`mcucontrol.v:63,95`).
6. **STE palette bit order is 0321**, ST is 0123 (`gstshifter.v:236-237`).
7. **Hard-scroll leaves 1-2 words unloaded in mid/hi res**, and the pixel counter overshoots
   to 31/63 pixels after the last reload (`shifter_video.v:110-111`, `gstshifter.v:292-295`).
8. **CPU write window is phase-quantised to slots 2..3** (`mcucontrol.v:198-201`).

---

## 9. Reproduction

```
cd /opt/data/cache/scratch
git clone --depth 1 https://github.com/gyurco/gstmcu
git clone --depth 1 https://github.com/MiSTer-devel/AtariST_MiSTer
```
gstmcu ships a Verilator testbench (`gstmcu/tb/ste_tb.v`, `Makefile`) that renders a frame
from `stram.bin` — usable as a golden reference for the-st's own shifter/MMU once the
emulator can boot.


---

## Verification pass (2026-11-01, by the parent session)

Every load-bearing claim above was re-checked against the RTL itself over the GitHub API, not taken from
the report. What follows is what the code actually says.

**Repos exist and are what they claim:** `gyurco/gstmcu` — "Simulation model of the Atari STE GSTMCU",
Verilog, last push 2025-01-02, header states *"Based on ST4081S.PDF recovered by Christian Zietz"* and
GPL-2.0-or-later. `MiSTer-devel/AtariST_MiSTer` — "Atari ST/STe for MiSTer", last push 2026-06-04.
`zerkman/zest` — Atari ST in VHDL. (`mist-devel/atari-st` and `MiSTer-devel/Atari_ST_MiSTer` do not
exist, as reported.)

**The address mux, verbatim (`hdl/gstmcu.v:132-139`):**

```verilog
casez ({ addrselb, ixdmab, snden, refb })
    4'b00??: ADDR = dma_reg;      // DMA_ADDR
    4'b01??: ADDR = A;            // CPU_ADDR
    4'b1??0: ADDR = 0;            // REFRESH_ADDR
    4'b1?11: ADDR = { 2'b0, snd }; // SND DMA ADDR
    4'b1?01: ADDR = vid_reg;      // VIDEO ADDR
endcase
```

Read precisely, this settles both questions left open by `research/memory-bus-slots.md`:

1. **Internal versus external: no priority, just phase.** `addrselb` is a decode of the 8-phase time
   counter (`gstmcu.v:705`: `addrselb = turbo ? ~time4_t : ~time5_s`). So Cwik's "two clocks internal,
   two external, alternating" is a *phase*, exactly as documented.
2. **Inside the internal class there *is* a priority: refresh → sound DMA → video.** `refb = 0` selects
   the refresh address ahead of both sound and video, and that arm is unconditional on `snden`/video
   state. **So refresh does preempt video during DE** — the open question in
   `research/memory-bus-slots.md` §6 is answered, subject to the caveat below.
3. **Video fetches are interleaved at slot granularity, not fetched in 4-slot bursts.** There is no
   burst state machine: `vid` advances one address per fetch clock (`gstmcu.v:1078`, `vid = vid_reg + 1`)
   and every internal slot independently picks its owner through the `casez` above. A 16-pixel group is
   therefore four separate slot decisions, and refresh or sound can occupy the slots between them.

**Caveat that limits claim 2:** the refresh *address* is hard-wired to `0` in the model
(`4'b1??0: ADDR = 0; // REFRESH_ADDR`) — the row counter is not implemented. So the RTL proves refresh's
*priority*, not its *period*. The 7.8 µs figure stays derived, not implemented.

**Two undocumented behaviours confirmed in the code:**

- **STE VSYNC double reload** (`hdl/gstmcu.v:940-960`), comment verbatim: *"From the STE schematics, it
  seems the counter can be reloaded second time to another value if the mode registers are changed after
  the first reload (in the VBL handler for example). Confirmed by Level 16 fullscreen demo behavior on a
  real STe."* The ST path deliberately reloads once. This is a hardware-confirmed quirk with a named
  demo — the strongest class of evidence available without a die shot.
- **Blitter non-HOG bus timeout** (`rtl/stBlitter.sv:235-250, 438` in the MiSTer core): a 7-bit counter
  of `AS` falling edges while busy, `timeOut = tmoCntr[6]`, so **64 bus cycles**, and
  `stopBlit = (timeOut & !Hog) | ...`. The author's own comment calls it a *"BLITTER Buglet: starts
  counting as soon as BUSY is set"* — it counts **every** `AS` edge in the window, including cycles the
  blitter never used. A blitter model that counts only its own accesses will run too long in non-HOG
  mode.

**What this changes:** the two questions left open were *behavioural*, and the RTL answers them at slot
granularity. The model can now implement `#7`'s ordering as: an 8-phase counter → `addrselb` picks
internal/external → internal slots arbitrate refresh → sound → video → external slots serve CPU, then
the DMA chip, then the Blitter. No precedence table between internal and external exists, and none
should be invented.
