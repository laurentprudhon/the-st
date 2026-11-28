# The drive and the diskette

### The electromechanical half — spindle, actuator, heads, medium, write path, and the view

*Subject: the 1040STE's 3.5" double-sided double-density drive and its 90 mm cartridge: what turns, what moves, what touches the medium, what a write actually does, and what the emulator must expose so the drive can be drawn and written to.
Studied 2026-11-28, the fourth session of this stretch. Companion to `research/floppy-media-and-images.md` (the medium and the five readers) and `design/disk-path.md` (the image model, the track, the byte clock); the model and the view contract this study feeds are `design/disk-drive-model.md`.*

*Basis: the drive manufacturers' specifications[104][105]. The 90 mm cartridge standard is ECMA-125[106], with ECMA-100[107] as the DD companion. The write path is Motorola AN-0917[108]. The 3.5" practice is the floppy user guide[109] and Info-Coach[110]; the FDC's side is the WD1772[44]. Atari's FD/HD guide[23] adds the command semantics; the media study supplies the ST's own layout.*
Labels: `documented` (a source states it), `derived` (arithmetic on stated numbers), `inferred` (the ST's drive from a class representative), `unknown` (nothing states it). Tiers: T1 hardware-captured, T2 hardware-corrected, T3 emulator-derived.
Sources are cited by number; the ledger's ids are stable across the campaign's studies.*

---

## 1. Executive summary

| question | answer | § |
|---|---|---|
| What turns the disk? | A **3-phase brushless DC motor, direct drive, 300 rpm**: 480 ms to rated speed, **505 ms to READY**, **490 ms auto-chuck** on insertion, and **no rotation at all while no disk is installed**. The index is the **spindle rotor's Hall/FG sensor**, not an index hole; its period is **197–203 ms**, burst error **±400 µs**.[104][110] | 4 |
| Where is the head? | A **4-phase stepper, 20 steps/rev, 2 steps per track**, 0.1875 mm pitch; **one STEP pulse = one track**, 0.8 µs pulse, access on the trailing edge; **3 ms** same direction, **4 ms** after a reversal, **18 ms** access including settling, **15 ms** settle, **94–95 ms** average seek. Track 0 is an optical sensor; the head is loaded whenever a disk is installed — there is **no load solenoid**. Two heads: side 0 lower, side 1 upper; effective track **0.115 mm**, line-of-access offset **0.35 mm**, azimuth **0° ± 18′**.[104][105] | 5 |
| Where is the data? | **80 concentric tracks per side**, 0.1875 mm apart; radii **39.5 → 24.6875 mm** on side 0 and **38.0 → 23.1875 mm** on side 1 — the side-1 track is **not under** the side-0 track, it is 1.5 mm nearer the hub, and the two tracks of a cylinder are spatially interleaved. Recording area 20.6–42.0 mm; 0.115 mm track and 0.0725 mm guard band; ±0.028 mm operating tolerance; **counter-clockwise looking at side 0**; index when Reference Line B is parallel to the line of access.[106][109] | 3 |
| How is a byte written? | The FDC serializes MFM into `WD` pulses; each pulse toggles the head current between the two R/W coils (saturated recording). The **tunnel erase head behind the R/W gap** (the mechanism is Motorola's[108]; the 3.5" head is the inferred class, §6.2) trims both track edges: on about **500 µs** after writing starts, off about **1 ms** after it ends. The write gate must be true **18 ms after the last step** and **100 µs after a side change**; a write-protected disk ignores `WD`.[104][108][109] | 6 |
| Where is the write splice? | At the **start of gap 2** (the 6/12 zero bytes) and **after the first byte of gap 3**; the FDC writes the gap, the data field and its CRC, **not the ID**. The first flux change after a rewrite sits at a different phase because the spindle speed varies, and the 12-byte zero preamble lets the DPLL resync. `Read Track` can read the splice bytes incorrectly; after a format the **index gap's first bytes may be ill-defined**.[23][106][110] | 6 |
| What must the model expose? | A **`DriveSnapshot` at each frame boundary** — present, write-protected, motor, ready, track, side, head loaded, spindle angle, byte position, sector under the head, write gate, erase gate, activity, media version — and a **`TrackMap`** for the current track: blocks, sectors, splices, fuzzy/CRC flags, provenance. `design/disk-drive-model.md` §3. | 8–9 |
| What must the write path do? | Write the **exact bytes** into the track map at the FDC's byte cadence; keep the ID; rewrite gap 2, the data field, the CRC and the first gap-3 byte; **mark the splice and the undefined bytes**; respect the write-protect tab; bump the media version; keep the geometry. The mechanics are approximate; the bytes are exact. | 6, 8 |
| What is **not** modelled? | The spin-up curve, actuator resonance/micro-stepping, the analogue read/write chain, coercivity and residual magnetisation, head wear, the ST drive's exact erase timing and current, and the index phase tolerance. These are A1–A11 in `design/emulator-limitations.md` §6.1. | 12 |

---

## 2. The evidence, and what each layer can answer

| layer | what it gives | what it cannot |
|---|---|---|
| TEAC FD-235HF/C929/C940/C991 service manual[104] | **the interface truth**: motor/index/ready timing, step timing and the step-ignore conditions, write-gate conditions, write/read data windows, side select, track 00, write protect, disk change, auto-chuck, no-disk behaviour | the ST's exact drive model, and the erase-coil timing (the drive's own PCB hides it) |
| Chinon F-354C specification[105] | a **double-sided 135 TPI** mechanism of the ST's class: 300 rpm, 3 ms/track, 15 ms settle, 95 ms average, 0.5 s ready, the radii, 6.25 KB unformatted/track, ±1.5 % speed variation, steel-belt drive, head soft-landing | whether the 1040STE's built-in drive is this exact mechanism |
| ECMA-125[106] | **the cartridge and the medium**: case/disk/hub dimensions, 80 tracks, 0.1875 mm pitch, 0.115 mm track, 39.5/38.0 mm radii, rotation direction, index definition, track-location and offset-angle tolerances, trim erase, index gap, overwrite, the 1 ms index-pulse timer | the ST's 9-sector layout, 60-byte index gap, 82/83-track tricks |
| ECMA-100[107] | the DD companion standard for the 90 mm 1 Mbyte family — cited as the family, not for numbers (its PDF is image-only in this session) | the same as above |
| Motorola AN-0917[108] | **the write path and the head**: straddle vs tunnel erase, the erase turn-on/off reason, write current vs `Rext`, 40 mA erase example, saturated recording, the 2–20 mV read signal and gain of 100, precompensation | the ST drive's actual currents; the analogue media |
| The floppy user guide[109] | **the 3.5" practice**: no index hole, spatially interleaved cylinder tracks, gap 2/gap 3 write-gate timing, head load, 3 ms step / 15 ms settle, 0.19 mm track, motor start | per-drive measurements |
| Info-Coach's FD-Hard page[110] | **the 3.5" index sensor** (Hall effect, rotor, no index hole) and **the write splice** — why the DPLL preamble exists and why `Read Track` sees the splice | the ST's own drive calibration |
| WD1772[44] and Atari's FD/HD guide[23] | the FDC's side of the contract: commands, the `p` precompensation bit, `Read Track`'s always-on address-mark detector, `Write Track` from index to index, the 6-revolution motor wait, write-protect status | the mechanics under the FDC |
| The media study and `design/disk-path.md` | the ST's **614-byte sector**, 60-byte index gap, 6 250-byte nominal track, image lengths (6 283 bytes in The Pawn 2.1), the block stream | the electromechanical layer — this study |
| The 1040STE field service manual[41] | the built-in drive is a **1 MB unformatted (720 KB formatted)** DS DD unit | which mechanism is inside a given machine |
| `design/board-inventory.md`, `atari-st-hardware/pins/04-din14-floppy-port.md` | the ST's nets: the WD1772's `MO`/`STEP`/`DIRC`/`WD`/`WG`/`DDEN`, the drive's `RD`/`IP`/`TR00`/`WPRT`, the PSG's side/drive selects | the pin page's **driver column is wrong** — see §11.2 |

### 2.1 What this study does *not* use, and why

`documented` The **Chinon SF-354 service manual** is an image-only PDF; its search snippet gives the single-sided variant's headline numbers, but a snippet is not a source. The F-354C[105] and the TEAC[104] carry the class; the SF-354's single-sided geometry is the same media with one head, not a different mechanism.

`documented` The **DIN14 pin page** names the right *functions* but the wrong *drivers*. It says the side select is `WD1772` pin 2 and the drive selects are pins 6/7; those pins are `R/W` and data pins on the WD1772, not selects[44], and the standard-parts study settles that the **PSG's port A** drives the side and the two drive selects (§9.9), while the FDC drives `MO`/`STEP`/`DIRC`/`WD`/`WG`/`DDEN`. The page is not a source for the drive model; §11.2 records the correction.

`documented` The older `atari-st-hardware/floppy/01-physical-format.md` and `03-copy-protection.md` repeat the 500 kbit/s and 10 500-byte-track errors the media study corrected[44][60]; they are not sources here either.

---

## 3. The cartridge and the medium

### 3.1 The parts

`documented` The cartridge is a substantially square case with a central hub access hole on side 0, a head window on both sides, an **identification hole** (the HD hole) and a **write-inhibit hole**, plus a sliding shutter and a liner.[106]

| part | value | source |
|---|---|---|
| case | 94 × 90 × 3.3 mm nominal (the ST's 3.5" cartridge) | media study; [106] §6.1 |
| disk | **85.8 mm ± 0.2**, **0.080 mm ± 0.008** thick | [106] §6.3 |
| hub | central diameter **25.00 mm**, flange **31.15 mm max**, surface 1.36 mm ± 0.10 from side 0 | [106] §6.4 |
| recording area | radii **20.6 mm max to 42.0 mm min** | [106] §8.1 |
| tracks | **80 per side**, centreline pitch **0.1875 mm** | [106] §8.2.1 |
| track width | **0.115 mm ± 0.008** after trim erase | [106] §8.2.2; [104] |
| guard band | **0.0725 mm** (pitch − width) | derived |
| side 0 radii | **39.5000 mm** (track 00) → **24.6875 mm** (track 79) | [106] §8.2.3.1 |
| side 1 radii | **38.0000 mm** → **23.1875 mm** | [106] §8.2.3.1 |
| track-location tolerance | **±0.028 mm** operating, **±0.020 mm** at test | [106] §9.2, §8.2.3.2 |
| line of access | parallel to a radial line, **0.35 mm** from it | [106] §8.2.3.3 |
| recording offset angle | θ = arcsin(d/Rn), **±0°18′** | [106] §9.3 |
| rotation | **counter-clockwise looking at side 0** | [106] §4.15 |
| index | when **Reference Line B** is parallel to the line of access | [106] §10 |
| write-inhibit | hole **open = protected**; covered = writable | [106] §6.1.7; [104] §8.3.11 |
| identification | the HD hole distinguishes 1 MB (DD) from 2 MB (HD) cartridges | [104] §8.3.14 |

`documented` The disk is a PET sheet coated on both sides with a flexible magnetic layer — gamma iron oxide in the common DD media, with cobalt-doped, higher-coercivity formulations for HD.[106][110][111]
`stated` The commonly quoted coercivities are **665 Oe for 720 KB DD** and **720 Oe for 1.44 MB HD** 3.5" media[111]; ECMA does not state a coercivity, it states the **Typical Field** and the **Reference Field** and defines the **Test Recording Current** as **148–152 %** of the current that produces the Reference Field.[106] §4.8–4.10.

`derived` The write current is therefore **not a universal number**. It is set by the drive's head and its `Rext`; the Motorola IC's curve gives **16.5 mA at 2 kΩ down to 2.6 mA at 10 kΩ** (`I = 30/Rext`, I in mA, Rext in kΩ), and the **40 mA** erase-coil example uses a 10 Ω coil at 12 V.[108] A model that hard-codes one current is modelling one drive, not the medium.

### 3.2 The hub and the index

`documented` The hub is held against the drive spindle **by magnetic attraction** and **ensures centring in a unique angular position**.[106] §4.2, §6.6. It has **two orientation holes**: a square first hole ≥ 4.0 mm and an off-centre rectangular second hole 8.0 × 4.5 mm, positioned by two perpendicular Reference Lines A and B with γ = 15° ± 3°.[106] §6.4.2. The spindle's pins engage them, so the disk can only mount one way.

`documented` A 3.5" drive has **no index hole**: the index is the spindle rotor's **Hall-effect (or FG) sensor**, and one pulse per revolution is produced by a magnetic coating on the rotor.[110][104] §8.3.9. This is why the index is a property of the *drive*, not of the disk: the disk's angular position is fixed to the hub, but the pulse comes from the motor.

`derived` The model can therefore set **index at track byte 0** (the convention the media study uses for images)[46] and compute the disk's angle as `(now − indexTick) / revolution × 360°`[106]. The physical chain is: hub orientation → disk's angular position; rotor sensor → index; the two meet because the hub's Reference Line B is the standard's index reference. The convention is not a measurement of a particular drive's phase; it is the model's phase zero. §11.6.

### 3.3 The track, the cylinder, and what "under the head" means

`documented` The two tracks of a cylinder are **not exactly over each other**: side 1's track 00 is at 38.0 mm, 1.5 mm nearer the hub than side 0's 39.5 mm, and the tracks are spatially interleaved.[106][109] A single-sided drive cannot read the back of a disk written by a double-sided drive by flipping it, because the tracks do not line up.[109]

`derived` The head's radial position is therefore side-dependent[106]:

```
r(track, side) = (side == 0 ? 39.5000 : 38.0000) − 0.1875 × track     [mm]
r(0, 0) = 39.5000   r(79, 0) = 24.6875   r(81, 0) = 24.3125
r(0, 1) = 38.0000   r(79, 1) = 23.1875   r(81, 1) = 22.8125
```

`documented` The head's line of access is 0.35 mm from a radial line, and the transition angle at radius Rn is arcsin(0.35/Rn) ± 18′.[106] §8.2.3.3, §9.3. `derived` That is a **0.51° offset at track 00 and 0.81° at track 79** (arcsin(0.35/39.5) = 0.508°, arcsin(0.35/24.6875) = 0.812°); the head is not on a pure radius, and the visual's radial spoke is an idealisation. The model does not need the angle for byte positions (the image's byte order is along the track), but the **visual** should not draw the head on the exact radial line if it claims to show the line of access.

`derived` The 80 tracks occupy 39.5 − 24.6875 = **14.8125 mm** of radial span on side 0 (79 × 0.1875), and the recording area extends to 20.6 mm, so there is room for **tracks 80 and 81** before the medium's inner limit. The TEAC drive's step-in is ignored **from track 81**, so the drive's mechanical stop is 81, not 79.[104] §8.3.4(4)(c). The ST's 82/83-track protection tricks are therefore **drive-dependent**, not supported by this mechanism; §11.4.

---

## 4. The spindle

### 4.1 What the drive guarantees

| quantity | value | source |
|---|---|---|
| motor | **3-phase brushless DC**, direct drive, motor shaft direct to spindle | [104] §3; [105] |
| speed | **300 rpm** nominal, 300 ± 3 rpm | [104]; [106] §7.4.2 |
| speed variation | **< ±1.5 %** long-term and instantaneous (Chinon) | [105] |
| spin-up | rated speed within **480 ms** of `MOTOR ON` | [104] §8.3.2 |
| READY | **505 ms max** after motor start; false within **0.3 ms** after `MOTOR ON` false | [104] §8.3.13 |
| auto-chuck | **490 ms approx. (500 ms max.)** on each insertion, by rotating the spindle | [104] §12.2 |
| power-on reset | **< 400 ms**, including auto-recalibration | [104] §9.1 |
| index | **1 per revolution**, rotor Hall/FG, **197–203 ms** period | [104] §8.3.9 |
| index burst error | **±400 µs or less** | [104] §3 |
| no disk | **the spindle motor does not rotate at any condition while no disk is installed** | [104] §12.2(1) |
| disk torque | running 0.0005–0.0025 N·m at 300 rpm; starting ≤ 0.006 N·m without heads | [106] §7.4 |

`documented` The index is gated by **index detection × drive select × ready × seek-complete**, and "ready" includes power on, a disk installed, auto-chuck complete, `MOTOR ON` true for about 505 ms, and an index detected after motor-on.[104] §8.3.9. The pulse width is 1.5–5 ms and its leading edge is the reference.[104]

`documented` The Chinon mechanism uses a flat 3-phase brushless direct-drive motor and a **steel-belt** head drive; the TEAC uses a **stepping motor and lead screw**.[105][104] Both give the same 0.1875 mm/track. The class is what the model needs; the exact mechanism inside a given 1040STE is not read.

### 4.2 The model

`derived` The drive's angular state is **one integer and one tick**:

```
AngleTicks = (now − IndexTick) mod RevolutionTicks
RevolutionTicks = 6_400_000          (200 ms at 32 MHz, nominal 300 rpm)
BytePosition = AngleTicks * TrackLength / RevolutionTicks
IndexTick += RevolutionTicks         (a wake at each index)
```

The angle is computed on demand, not by a per-byte wake; the visual can interpolate between frame snapshots. The index is a wake because the FDC's command deadlines and the media's splice mask use it[104]; the byte position is arithmetic, exactly as `design/disk-path.md` §3 rules for the FDC's byte clock[44].

`derived` The motor state is a small state machine[104]:

| state | condition | outputs |
|---|---|---|
| `NoDisk` | no cartridge | motor off, no index, `READY` false, `TR00` per carriage, `WPRT` per tab (undefined with no disk), `RD` silent |
| `Chucking` | disk inserted, `MOTOR ON`, 0–490 ms | motor turning, no index until the rotor sensor sees its mark, `READY` false |
| `Spinning` | `MOTOR ON` ≥ 480 ms | 300 rpm nominal, index every 6.4 M ticks |
| `Ready` | spinning + 505 ms + index + seek complete | index and `RD` gated on, FDC may read |
| `Stopping` | `MOTOR ON` false | `READY` false within 0.3 ms, no index after the rotor stops |

`inferred` The spin-up curve itself is not in any source: the specs give the **endpoints** (0 → 300 rpm in 480 ms), not the shape. The model uses a **linear ramp** to 300 rpm over 480 ms, then constant speed; the visual's rotation is therefore nominal and its acceleration is an approximation. This is **A7**.

### 4.3 The no-disk conflict

`documented` TEAC §12.2(1): the spindle **does not rotate at any condition while no disk is installed**.[104] The reason is mechanical: the rotor's load and the chuck are the disk; without a cartridge the drive's controller keeps the motor off even with `MOTOR ON` asserted.

`documented` `design/disk-path.md` §5 rules the opposite for the model: "**selected drive with no image: motor spins, `IP` at 300 RPM**, `TR00` per head position, `WPRT` per the tab, `RD` silent → the sector search runs to its timeout and reports **record not found**." The intent is to let the FDC's own timeout produce the error instead of special-casing an empty drive.

`derived` The two statements are about **different states**, and the model must not conflate them:

- **`NoDisk`** — no cartridge. The real drive does not spin and produces **no index**. The FDC's 4-revolution search needs index pulses; with none, its "4 revolutions" deadline cannot be counted[23][44], and the command may never complete. A real ST with an empty drive relies on TOS's own timeout (or the drive's `READY` being false) rather than on an FDC record-not-found. The visual must show **a still disk and no index**.
- **`DiskWithoutImage`** — a cartridge is present but the host has no image for it (a blank or unformatted disk). The drive spins, the index runs, and the FDC's search finds no ID and times out with record-not-found[23][44]. This is the state §5's rule describes, and it is the one the boot poll needs.

`derived` The recommended model is therefore **two states**, not one: `NoDisk` (motor off, no index, `READY` false) and `DiskWithoutImage` (motor on, index, blank media, FDC timeout). If §5's rule is meant to cover `NoDisk`, it contradicts TEAC and the visual would show a spinning empty drive; if it is meant to cover `DiskWithoutImage`, the wording should say "no image" rather than "no disk". This is the owner's call (it changes the FDC timeout rule), recorded as a decision in `design/disk-drive-model.md` §8.1. The study does not silently change the ruling.

---

## 5. The actuator and the heads

### 5.1 The mechanism

| quantity | value | source |
|---|---|---|
| motor | **4-phase stepper, 20 steps/rev**, **2 steps per track** | [104] §3 |
| transmission | lead screw (TEAC) / steel belt (Chinon) | [104][105] |
| track pitch | **0.1875 mm** | [106] §8.2.1 |
| STEP pulse | negative, **0.8 µs or more**; **one track space per pulse**; access starts at the trailing edge | [104] §8.3.4 |
| step interval | **≥ 3 ms** same direction, **≥ 4 ms** after a direction change | [104] §8.3.4 |
| access time | completes within **18 ms** of the trailing edge, including settling | [104] §8.3.4 |
| settling | **15 ms or less**, excluding track-to-track | [104] §3 |
| average seek | **94 ms** including settling (TEAC); **95 ms** average access, 100 ms latency (Chinon) | [104][105] |
| track 00 | optical sensor; **TR00 valid > 2.8 ms** after the STEP pulse | [104] §8.3.8 |
| step ignored | (a) write protect false + write gate true; (b) TR00 true + step-out; (c) step-in from track 81 | [104] §8.3.4(4) |
| head load | **not equipped**; the head is loaded whenever a disk is installed | [104] §3 |
| heads | **2**; side 0 is the lower head, side 1 the upper; side select **HIGH = side 0**, **LOW = side 1**; read valid > 100 µs, write start > 100 µs after the change | [104] §8.3.7 |
| track width | **0.115 ± 0.008 mm** effective after trim erase | [104][106] |
| azimuth | **0° ± 18′** | [104] §3 |
| line of access | 0.35 mm from a radial line | [106] §8.2.3.3 |

`documented` The drive's own step-ignore rules are the mechanical stop, not the software's: a step-out while `TR00` is true is ignored, and a step-in **from track 81** is ignored.[104] §8.3.4(4). `derived` The drive therefore has **82 positions** (0–81), not 80, and not 83. The 80 tracks of the standard are the recording area; tracks 80 and 81 are the extra positions the mechanism can reach. The ST's 82/83-track protections need a drive that steps further; that is a different mechanism or a modified stop. §11.4.

`documented` The head is a **read/write head with an erase gap**; the effective track width after trim erase is the 0.115 mm figure.[104] §3. The erase gap is the **tunnel-erase** pair behind the R/W gap, not a separate erase head over the track.[108] The head is in contact with the medium; the drive has a **head soft-landing** mechanism on loading, and no load solenoid.[105][104]

### 5.2 The model

`derived` The carriage is a **track index, a side, a step phase and a settle deadline**:

```
Track       0..MaxTrack           (81 for the FD-235HF class; 79 for the standard's media)
Side        0 or 1
StepPhase   Track * 2 + (0 or 1)  (the stepper's 2 steps/track; visual only)
Direction   in/out
SettlingUntil = lastStepTick + 480_000   (15 ms; seek-complete is 15.8–17.9 ms)
Track0      Track == 0            (valid 2.8 ms after the step)
```

`derived` A STEP pulse does **one track**, not one stepper step: the interface is geared 2:1 by the mechanism[104]. The FDC's `STEP` output is one track per pulse; the drive's stepper does two physical steps. The visual can show the stepper phase as a 2-per-track detail, but the model's unit is the track.

`derived` The FDC's step rates (6/12/2/3 ms per step) must be **≥ the drive's 3 ms same-direction minimum**; the 2 ms rate is below the drive's spec and may miss steps on a real mechanism.[44][104] `derived` The ST's TOS uses the 3 ms rate (the standard-parts study's `r1 r0` table), so the drive is inside its spec. A model that lets software use 2 ms should mark the risk, not silently accept it; the drive's step-ignore/seek-error behaviour is a real-drive measurement (A8).

`derived` The visual's head position is the radius formula of §3.3; the head's carriage is at track 0 on the **outside**, and the head moves **inward** as the track number rises[106]. The side-1 head is 1.5 mm nearer the hub; the two heads are on opposite surfaces, so a single-head visual must say which side is selected.

---

## 6. The write path

### 6.1 Signals and the write gate

`documented` The drive writes only when it is **selected**, the **write gate** is asserted, and the disk is **not write-protected**; a protected disk ignores `WRITE DATA`.[104] §8.3.5–8.3.6.

| signal | timing | source |
|---|---|---|
| `WRITE GATE` true | **18 ms after the effective final STEP pulse** and **100 µs after a side change** | [104] §8.3.5(3) |
| `WRITE GATE` false | do not motor-off, step, deselect, change side, or change HD for **650 µs (2 MB) / 690 µs (1 MB)** | [104] §8.3.5(4) |
| `WRITE DATA` | negative pulse **0.1–1.1 µs**; leading edge; ignored if WG false or WP true | [104] §8.3.6 |
| read after write | read data valid after **650 µs (2 MB) / 690 µs (1 MB)** from WG false | [104] §8.3.10(4)(c) |
| side change | read valid > 100 µs; write start > 100 µs | [104] §8.3.7 |
| index | one pulse per revolution, 1.5–5 ms wide, gated by ready + seek-complete | [104] §8.3.9 |
| disk change | true on power-on or removal; false after install + a STEP with drive select | [104] §8.3.12 |
| write protect | true when the write-inhibit hole is **open**; write inhibited | [104] §8.3.11 |

`documented` The write data window for MFM at 1 MB is **t1 = 4 µs, t2 = 6 µs, t3 = 8 µs** (2 MB: 2/3/4 µs), with the magnetization window **t7 = ±700 ns** (2 MB: ±350 ns).[104] §8.3.2, Fig. 8.3-2/8.3-3. `derived` Those are the drive's **data-bit** timing, not the FDC's bit cell: at 250 kbit/s a data bit is 4 µs, and the 4/6/8 µs table is the MFM data pattern's one-, one-and-a-half- and two-bit intervals; the FDC's 32 µs byte is 16 MFM **cells** at 2 µs.[104][44]

### 6.2 The head, the write current, and the erase coil

`documented` There are two head families: **straddle erase** (an erase core on each side of the R/W core, erase at the same time) and **tunnel erase** (two erase cores behind the R/W gap, flux parallel to the R/W flux).[108] `inferred` The 3.5" class uses the **tunnel** type: the TEAC spec says only "read/write head with erase gap", and the floppy user guide's downstream erase head fits the observed delays[104][109]. The Motorola tunnel description supplies the mechanism[108]. The erase coil turns on **a period after writing begins** and stays on **a period after it ends**, because the erase gap is physically downstream of the R/W gap[108]. The turn-on delay is based on the displacement and the **linear velocity of track 00**; the turn-off is slightly longer to cover the **slower linear velocity of track 79**.[108]

`documented` Typical MC3471 application: **turn-on 500 µs, turn-off 1.0 ms**, ±15 % with 5 % capacitors and 1 % resistors; the erase current example is **40 mA** through a 10 Ω coil at 12 V.[108] `inferred` The ST drive's internal values are not in its public spec; the model uses 500 µs/1 ms as the default and exposes them as parameters. This is **A10**.

`documented` The write current is `I = 30 / Rext` mA (2 kΩ → 16.5 mA, 10 kΩ → 2.6 mA), ±3 %; the **33 % outer-track boost** is an 8" drive practice and is not used by all drives.[108] The current must saturate the medium; the alternating pulses leave no DC bias; the direction of the first pulse is irrelevant because only flux **reversals** carry data.[108]

`documented` The erase function is **not** the erase of old data: the R/W coils overwrite the old data, and the erase coil only **trims the track edges** to create guard bands and remove the old-information fringe the track-location/offset tolerances can leave.[108][106] §9.3 Note 6. `derived` A model that treats "erase" as "zero the track" would be wrong: the erase gate is a **trim** flag on the track map, not a data operation.

### 6.3 The sequence of a sector write

`documented` When overwriting a sector, the FDC first looks for the right header; the erase head is **not yet on**. When the header is recognised, the erase head switches on and the R/W head toggles to writing. Because of the spacing between the heads, the erase head cannot erase right after the header; it has to **skip a gap**.[109] The write gate turns active with the beginning of the **6/12 bytes of 0x00** (gap 2) and inactive after the **first byte of gap 3**.[109]

`derived` For the ST's MFM layout (the media study's 614-byte sector):

```
... gap-after-ID 22 | 12×00 (gap 2) | 3×A1 | FB | 512 data | CRC | 40×4E (gap 3) | next ID ...
                     ^ WG on                         ^ WG off after the first 4E
```

The FDC's Write Sector command writes **from gap 2 through the first byte of gap 3**; it does **not** rewrite the 6-byte ID or its CRC[109][23]. The model's write window is `[gap2Start, gap3Start + 1)`; the bytes before and after are old. The `TrackMap` marks the window and the two splice boundaries.

`documented` The first flux change after a rewrite is at a different position for each write because the spindle speed varies; the **12-byte zero preamble** gives the DPLL time to adjust to the new frequency and centre the inspection window.[110] `documented` `Read Track`'s address-mark detector is always on, so a write splice can make the chip look for an address mark inside a data block; the gap bytes may be read incorrectly during splice time.[23]

`documented` `Write Track` starts at the **leading edge of the index pulse** and continues to the next index; the data register supplies the bytes; if a byte is missing, a zero is substituted; the index pulse ends the write.[23] The ECMA format's index gap is **32–146 bytes** and "some of the first bytes may have become ill-defined due to overwriting"; the track gap after the last sector is `4E` until index.[106] §10.1, §10.6. `derived` A whole-track format therefore has its splice at the **index**, and the first index-gap bytes are the undefined region; a sector write has its splice in **gap 2/gap 3**, and the index gap is untouched.

`documented` The ECMA test procedure masks the write-current-off discontinuity with a **1 ms index-pulse timer**.[106] Appendix H. `derived` The FDC's 650/690 µs read inhibit and the drive's erase turn-off (1 ms) are the same physical problem: the head is still settling when the write current stops, and the read path must not trust the first fraction of a millisecond.

### 6.4 Write protect, disk change, and the media version

`documented` `WRITE PROTECT` is true when the write-inhibit hole is **open**; the drive inhibits writing and the FDC reports write-protect in its status.[104] §8.3.11; [44] §5. `derived` The model checks `WriteProtected` **before** it asserts `WriteGate`; a write command on a protected disk returns the FDC's write-protect status and changes no bytes.

`documented` `DISK CHANGE` is true on power-on or disk removal and returns false when a disk is installed **and** a STEP is received with the drive selected.[104] §8.3.12. `derived` The ST's 14-pin connector may not carry the signal (the pin page does not list it; the internal 34-pin header does); the model can use a **media version** and a host-side insertion event rather than a pin.

`derived` Every write bumps `MediaVersion`. The visual caches the `TrackMap` by `(track, side, version)`; a write invalidates only the written track. The media image (if persisted) must record the write **and** the splice/undefined flags, not just the 512 bytes; an `.st`/`.msa`/`.dim` sector image cannot carry the flags, an `.stx` track image can[46][48].

### 6.5 Overwrite and residual

`documented` ECMA's overwrite test: record 1f for one revolution, overwrite with 2f for one revolution, and require the residual 1f average signal amplitude to be **less than 125 %** of the reference's residual.[106] §8.3.1.6. `derived` This is an analogue property of the medium, the head and the write current; the model's byte-level write is **clean** (the old bytes are replaced) and does not model the residual. The visual can show the track as rewritten; it must not pretend to show a residual signal. This is part of **A2/A6**.

---

## 7. One revolution, one seek, one write — in machine ticks

`derived` The machine's time domain is 32 MHz (ADR 0001); all values below are integer ticks at that clock. The 300 rpm revolution is **6 400 000 ticks** exactly[106], and the nominal 32 µs byte is **1 024 ticks**[44]; 6 250 × 1 024 = 6 400 000, which is why the nominal geometry closes.

| quantity | value | ticks at 32 MHz | source |
|---|---|---|---|
| revolution | 200 ms | **6 400 000** | [106] 300 rpm |
| byte (nominal) | 32 µs | **1 024** | [44]; media study |
| 512-byte payload | 16.384 ms | **524 288** | derived |
| 614-byte sector | 19.648 ms | **628 736** | media study |
| 60-byte index gap | 1.92 ms | **61 440** | media study |
| step pulse | 0.8 µs | **26** | [104] |
| step interval | 3 ms / 4 ms | **96 000 / 128 000** | [104] |
| access incl. settle | 18 ms | **576 000** | [104] |
| settle | 15 ms | **480 000** | [104] |
| seek complete | 15.8–17.9 ms | **505 600–572 800** | [104] |
| TR00 valid | 2.8 ms | **89 600** | [104] |
| WG after step | 18 ms | **576 000** | [104] |
| side change | 100 µs | **3 200** | [104] |
| WG off → read | 650 / 690 µs | **20 800 / 22 080** | [104] |
| erase on | 500 µs | **16 000** | [108] |
| erase off | 1 ms | **32 000** | [108] |
| index burst | ±400 µs | **±12 800** | [104] |
| motor to speed | 480 ms | **15 360 000** | [104] |
| ready | 505 ms | **16 160 000** | [104] |
| auto-chuck | 490 ms | **15 680 000** | [104] |
| power reset | 400 ms | **12 800 000** | [104] |
| ready off | 0.3 ms | **9 600** | [104] |
| average seek | 94 ms / 95 ms | **3 008 000 / 3 040 000** | [104][105] |
| latency | 100 ms | **3 200 000** | [105] |

### 7.1 A revolution at track N

`derived` At the index, `AngleTicks = 0`, `BytePosition = 0`. The head is at radius `r(N, side)`; the disk turns counter-clockwise; the head sees byte 0, then 1, … The ST's 9-sector layout is **5 586 bytes** (9 × 614 + 60) and the nominal track has **664 bytes** of slack before the index[60][61]. At byte 5 586 the last sector's gap ends; the slack is filled with `4E` (the track gap) until index.

`derived` A preserved `.stx` track may be **6 283 bytes** (The Pawn 2.1)[46]; at a 200 ms revolution its byte period is **31.83 µs**, not 32 µs[60], and the FDC's nominal 32 µs clock would overrun the track by 33 bytes. The model therefore uses the **track length** to compute the byte position and the sector's phase, not a hard-coded 6 250; the media study's "nominal 32 µs, overridable by the image" rule applies to the drive's angle as well. `derived` The visual's sector arcs use `byte / trackLength × 360°`, so a long track's sectors are slightly compressed in angle; that is the image's cadence, not a drive error.

### 7.2 A seek

`derived` A 40-track seek at 3 ms/step is **120 ms** (3 840 000 ticks), plus the 15 ms settle (**480 000**) = 135 ms; the drive's "access within 18 ms" is per **single** track[104], not per multi-track move, so the step interval dominates[44]. The average seek of 94 ms is consistent with ~26 tracks at 3 ms + 15 ms settle (26 × 3 + 15 = 93 ms); the exact average distance is not stated.

`derived` `WRITE GATE` cannot be asserted until **18 ms after the last step**, so a write following a seek waits the settle plus 3 ms; the FDC's Write Sector command already waits for the sector ID, so the drive's 18 ms is normally hidden behind the ID search. A write immediately after a step on the same track still pays it.

### 7.3 A sector write

`derived` The 512-byte payload is **524 288 ticks** (16.384 ms); the full write window (gap 2 + 3×A1 + FB + data + CRC + first gap-3 byte) is **531 bytes = 543 744 ticks** (16.992 ms) at 32 µs[104][109]. The erase gate turns on **16 000 ticks** after the write starts and off **32 000 ticks** after it ends; the read path is valid **20 800/22 080 ticks** after `WG` false. The splice boundaries are at the window's start and end; the bytes in the window are new, the bytes outside are old, and the first/last few bits are undefined.

### 7.4 A motor start

`derived` Insertion → auto-chuck 490 ms (15 680 000), `MOTOR ON` → 480 ms to speed (15 360 000, overlapping the chuck in a real drive), READY at 505 ms (16 160 000), and the FDC's `h` flag waits **6 revolutions = 1.2 s** (38 400 000) before a command if the drive is not already ready.[23][44] The ST sets `h = 0` (the guide's note) and relies on the drive's READY, so the emulator's boot poll sees READY at the drive's 505 ms, not the FDC's 1.2 s.

---

## 8. The model

`derived` The drive is a **state machine plus a media store**. The FDC owns the command and the byte stream; the drive owns the spindle, the carriage, the heads, the write gate, the erase gate and the media. The model must not move the FDC's byte clock into the drive, and must not put the drive's mechanics into the FDC.

### 8.1 State

```csharp
sealed class DriveState
{
    public bool Present;             // a cartridge is in the drive
    public bool WriteProtected;      // the tab is open
    public bool MotorOn;             // MOTOR ON input
    public bool Ready;               // motor on + 505 ms + index + seek complete
    public int  Track;               // 0..MaxTrack
    public int  Side;                // 0 or 1
    public bool HeadLoaded;          // == Present (no load solenoid)
    public int  StepPhase;           // Track*2 + 0/1, visual only
    public bool Track0;              // TR00 output, valid 2.8 ms after step
    public long IndexTick;           // last index leading edge
    public long SettlingUntil;       // lastStep + 15 ms
    public bool WriteGate;           // WG input as seen by the drive
    public bool EraseGate;           // the tunnel erase coil
    public long MediaVersion;        // bumped on every write/insert
    public DriveActivity Activity;   // Idle, Seek, Read, Write, Format
}
```

`derived` `Ready` is a **derived** predicate[104], not a stored bit: `Present && MotorOn && now ≥ motorReadyAt && now ≥ SettlingUntil && indexSeen`. The model should not let software set it directly.

### 8.2 Media

```csharp
sealed class MediaState
{
    public int  TrackCount;          // 80 nominal, 82 reachable
    public int  Sides;               // 2
    public int  TrackLength;         // 6 250 nominal, 6 283 in a preserved STX
    public bool Blank;               // no image: an unformatted disk
    public ImmutableArray<TrackMap> Tracks;  // 2 × TrackCount
    public ulong Version;
    public Provenance Provenance;    // gaps as-dumped / synthetic; status from-image / recomputed
}

sealed record TrackMap(int Track, int Side, int Length, ImmutableArray<Block> Blocks, ulong Version);

readonly record struct Block(
    BlockKind Kind, int Start, int Length, int Sector, BlockFlags Flags);

enum BlockKind { Gap, Sync, Idam, IdField, Data, Crc, Splice, Unknown }
[Flags] enum BlockFlags { Fuzzy, CrcError, Rnf, Deleted, WriteSplice, Synthetic }
```

`derived` The block stream is the same model as `design/disk-path.md` §2; the drive adds the **splice** and **unknown** kinds and the **version**[46][60]. The `TrackMap` is the truth; the sector index is a cache. The FDC's Read Track reads the blocks including gaps; the write path replaces a range and leaves the rest.

### 8.3 The snapshot

```csharp
readonly record struct DriveSnapshot(
    long Tick,
    bool Present, bool WriteProtected, bool MotorOn, bool Ready,
    int Track, int Side, bool HeadLoaded,
    double AngleTurns,              // 0..1, (now - IndexTick) / RevolutionTicks
    int Rpm,                        // 300 nominal, 0 when stopped
    bool WriteGate, bool EraseGate,
    DriveActivity Activity,
    int BytePosition, int TrackLength, int SectorUnderHead,
    long LastIndexTick, long LastStepTick,
    ulong MediaVersion);

readonly record struct DriveView(
    DriveSnapshot Snapshot,
    TrackMap? CurrentTrack,          // null when no media
    MediaDescriptor Media);          // write-protect tab, HD hole, track count, blank
```

`derived` The snapshot is **immutable and cheap**; the host samples it at frame boundaries (50 Hz) or on change. The `TrackMap` is versioned and cached; a write changes one track. The host must not call into the machine for each pixel; the machine must not know the host exists.

### 8.4 Time and events

`derived` The model needs **wakes** at:

- **index** — every 6.4 M ticks while ready[104] (the FDC's sector deadlines, the splice mask, the visual's revolution);
- **motor ready** — once, at 505 ms after `MOTOR ON`[104];
- **seek complete** — once, at `lastStep + 15.8–17.9 ms`[104];
- **write gate/erase** — at the command's start/end and the erase delays[108] (or computed on demand from the command's timestamps);
- **media change** — on insert/eject[104].

The **byte position is arithmetic**[44]: `(now − IndexTick) × TrackLength / RevolutionTicks`, with a 64-bit product or a fractional accumulator for lengths > 6 250. No per-byte wake for the visual; the FDC's DRQ owns the byte cadence. The write path can apply the bytes in a **bulk** update at the command's end (the FDC already streamed them), with the **start/end timestamps** recorded for the splice and the erase; the media version bumps once per command.

### 8.5 What is exact, approximate, not modelled

| layer | fidelity | why |
|---|---|---|
| byte positions, sector phases, track length | **exact** (from the image/nominal) | arithmetic on integers; the image's cadence |
| index period, revolution | **exact nominal** 6.4 M ticks; jitter optional | 300 rpm; A4's jitter is a bound |
| track/side, head radius | **exact formula**; mechanics nominal | 0.1875 mm pitch; the head's step is a track |
| seek timing | **step + settle** (3/4 ms, 15 ms); no acceleration curve | A8 |
| motor start/ready | **480/505/490 ms** endpoints; linear ramp | A7 |
| write bytes, ID, CRC, gap window | **exact** at the byte level | the FDC's stream; the block model |
| write splice extent | **marked** in gap 2/gap 3; exact bit phase unknown | A6/A9 |
| erase gate | **500 µs/1 ms** default; trim only | A10 |
| analogue read/write, coercivity, residual | **not modelled** | A1–A3, A5 |
| head wear, media dropout | **not modelled**; image masks only | A3 |

---

## 9. The visual representation

`derived` The view has one job: answer **"where is the head, what is under it, and what is the drive doing?"** — at a glance, without lying about what is modelled. It is a **host-side** renderer fed by the snapshot and the track map; the machine has no UI.

### 9.1 The three views

**The disk face** — a circle with 80 concentric tracks[106] (side 0 and side 1; the selected side in front), the hub, the shutter, the write-protect tab and the HD hole, the index mark, and the head at `r(track, side)`. The disk rotates **counter-clockwise**; the head is fixed at the line of access. The sector under the head is highlighted as an arc; the track map's sectors are drawn at their byte angles. A seek moves the head **radially**; a read/write pulses the head; a write lights the erase band.

**The track strip** — a linear map of the current track: gap/sync/IDAM/ID/data/CRC/splice blocks, the head's byte position, the sector under the head, the write window, the fuzzy/CRC/RNF flags, the provenance[46]. This is where the **write splice** is visible: the block edges at gap 2 and gap 3, the new bytes in a different shade, the undefined bytes hatched.

**The timeline** — index pulses, revolutions, motor on/off, seek steps, read/write/format, write gate, erase gate, splice markers. A 2-second window is enough to see the 5 Hz index and the 3 ms step train[104].

**The status panel** — present, write-protected, motor, ready, track, side, head loaded, rpm, byte position, sector, media version, activity, provenance. The panel is the model's own words; the pictures are the same data[104].

### 9.2 The contract

| item | rule |
|---|---|
| sampling | one snapshot per frame (50 Hz) or on change; the host interpolates the angle between snapshots |
| units | ticks (machine time), bytes (track position), mm (radius), rpm; the host scales to pixels |
| angle | `(now − IndexTick) / RevolutionTicks`; 0 at index, increasing CCW looking at side 0 |
| head | `r = (side == 0 ? 39.5 : 38.0) − 0.1875 × track` mm; the selected side's head |
| sector under head | the `Block` whose `[Start, Start+Length)` contains `BytePosition`; its `Sector` if any |
| track map | versioned; the host caches `(track, side, version)`; a write bumps the version |
| no media | `Present == false`: no disk face's tracks, no index, no rotation; the head is parked |
| blank media | tracks drawn as unformatted (no blocks), the head moves, the FDC times out |
| write | the write window is `[gap2Start, gap3Start+1)`; new bytes, splice edges, erase band |
| provenance | synthetic gaps vs as-dumped, from-image vs recomputed FDC status, fuzzy mask present; colour the difference |
| no lying | the visual must not draw a residual signal, a head vibration, or a spin-up curve the model does not have |

### 9.3 Edge cases the view must survive

`derived` The model will be asked to draw these; each is a test of the contract:

- **No disk** — no rotation, no index, no tracks[104]. The old design's "spinning empty drive" would fail this.
- **Blank disk** — rotation and index, no blocks, FDC record-not-found[23]; the visual shows an unformatted surface.
- **Write-protected** — the write command is refused[104]; the visual shows the tab open and the WG never asserted.
- **Long track** — 6 283 bytes; the sector arcs are compressed by `byte / 6 283`, not `byte / 6 250`[60].
- **11/12 sectors** — the track strip has more blocks than the nominal 9; the index gap may be shorter than 60; the view uses the track map, not a hard-coded 9[61].
- **Fuzzy/CRC/RNF** — the block flags colour the sector; the visual does not "fix" them[46].
- **Deleted DAM** — the data block is drawn as deleted; the read path's status is from the image[46].
- **Splice** — the block edges and the hatching; a `Read Track` of a just-written sector may show the splice bytes as data[23], and the view should say so.
- **Track 80/81** — the carriage can reach them[104]; the disk face shows the extra annuli; the standard's 80 tracks are the recording area.
- **Side 1** — the head is 1.5 mm nearer the hub[106]; the view must not draw it under side 0.
- **HD** — the 1.44 MB hole; the ST's DD drive ignores it[104]; the view can show the hole but the model is DD unless the drive profile says HD.

### 9.4 A sketch of the three views

```
 disk face (side 0, 80 tracks)                 track strip (track 12, 9 sectors)
        index 0°                                       index
          |                                            |
     .--- + ---.        head at r(12,0)          |4E×60|00×12 A1A1A1 FE 00 0C 01 02 CRC|...
    /     |     \       = 37.25 mm               | gap  | ID | gap | 00×12 A1A1A1 FB ...
   |   o  |  o   |      byte 2 431              |      ^ head byte 2 431
    \     |     /       sector 4                 |      |<-- write window -->|
     '--- + ---'        angle 140°               |......|####################|......
          |                                            ^ splice          ^ splice
        CCW                                      (new bytes, old bytes, undefined at the edges)
```

---

## 10. What the model needs

| claim | where it lands | action |
|---|---|---|
| the drive's state, snapshot and track map | `design/disk-drive-model.md` §2–3 | the new contract |
| the write path | `design/disk-drive-model.md` §5 | byte window, ID, splice, erase, WP, version |
| the visual contract | `design/disk-drive-model.md` §6 | the three views, units, edge cases |
| the byte position and index arithmetic | `design/disk-path.md` §3 | reuse; the drive's angle is the same arithmetic |
| the no-disk vs no-image state | `design/disk-path.md` §5 | **owner decision**; §4.3 above |
| the analogue rows | `design/emulator-limitations.md` §6.1 | A7–A11 added; A6 re-worded |
| the pin page's driver column | `atari-st-hardware/pins/04-din14-floppy-port.md` | dated correction: PSG port A for side/drive selects; the FDC for MO/STEP/DIRC/WD/WG/DDEN |
| the code | `src/TheST.Machine/Chips/DiskDrive.cs` (future) | not this study; the stub stays until the build |

`derived` The code surface is three types and one boundary: `DiskDrive` (the state machine), `DiskMedia` (the track maps), `DriveSnapshot`/`TrackMap` (the immutable view contract). The FDC's `Disk` stub is replaced by the drive + media; the host's renderer is outside the machine. The drive model costs no per-byte wake; its only new deadline is the index, and the write path is a bulk update at the FDC's command boundary.

---

## 11. Conflicts adjudicated

### 11.1 No-disk motor — TEAC vs `design/disk-path.md` §5

`documented` TEAC: no rotation with no disk.[104] `documented` The design: motor spins with no image.[internal] `derived` These are different states (NoDisk vs DiskWithoutImage); the design's wording conflates them. **Resolution:** the model has both; `NoDisk` is motor-off/no-index[104]; `DiskWithoutImage` is the blank-disk timeout[23]. The FDC's timeout rule for `NoDisk` is the owner's decision (does TOS's timeout suffice, or does the FDC need a synthetic index?). §4.3.

### 11.2 The DIN14 pin page's drivers

`documented` The pin page says the side select is `WD1772` pin 2 and the drive selects are pins 6/7; those pins are `R/W` and data pins on the WD1772, not selects.[44] `documented` The standard-parts study settles the PSG's port A: bit 0 side, bit 1 drive 0, bit 2 drive 1; the WD1772's `MO`/`STEP`/`DIRC`/`WD`/`WG`/`DDEN` go to the drive.[internal] **Resolution:** the page's function list is usable, its driver column is not; a dated correction belongs on the page. The drive model uses logical signals, not pins, so this does not block the model.

### 11.3 The F-354C is a class representative, not the ST's exact drive

`documented` The 1040STE's built-in drive is a 1 MB unformatted (720 KB formatted) DS DD unit.[41] `documented` The F-354C is a 135 TPI DS mechanism with the same media geometry.[105] `unknown` Which mechanism a given 1040STE has. **Resolution:** the model uses the class's numbers; the exact model is a drive-profile field (`MaxTrack`, `EraseOn/Off`, `WriteCurrent`) that a measurement can fill.

### 11.4 Track 81 vs the 82/83-track tricks

`documented` The TEAC's step-in is ignored from track 81; the drive's reachable positions are 0–81.[104] `documented` The ST's protections include 82/83-track claims (media study). **Resolution:** the model's `MaxTrack` is a profile (81 for this class; 83 only if a measured drive supports it); the visual shows the extra annuli as "out of spec / drive-dependent", not as standard tracks.

### 11.5 Head load

`documented` The TEAC has **no head load mechanism**; the head is loaded whenever a disk is installed.[104] `documented` The floppy user guide says PC drives load the heads when the motor speeds up, typical 4 ms, unload 480 ms.[109] **Resolution:** the two statements are about different drive families; for the 3.5" TEAC-class drive, `HeadLoaded == Present`; the 4 ms load is not modelled. The visual shows the head in contact when the disk is present.

### 11.6 Index phase

`documented` The index occurs when Reference Line B is parallel to the line of access.[106] `derived` The model sets index at track byte 0, which is a **convention**, not a measurement of the drive's phase. **Resolution:** the convention is the model's phase zero; the image's sector positions are relative to it; the visual's index mark is at byte 0. A real-drive measurement (E1) would give the phase error, but nothing depends on it for byte-accurate reads.

### 11.7 Write current and erase values

`documented` The write current is `30/Rext` mA (2.6–16.5 mA in the example) and the erase current 40 mA; the erase delays 500 µs/1 ms are the MC3471's typical application.[108] `unknown` The ST drive's actual values. **Resolution:** the model exposes them as `DriveProfile` parameters; the defaults are the Motorola values; the write path's bytes do not depend on them, and the visual only needs the gate, not the current.

### 11.8 The 60-byte index gap vs ECMA's 32–146

`documented` ECMA's index gap is 32–146 bytes; the ST's format uses 60.[106][internal] **Resolution:** 60 is inside the standard; the model uses the image's gap, not ECMA's maximum or the ST's nominal; the visual's track strip uses the block stream.

### 11.9 The 32 µs byte vs the long track

`documented` The nominal byte is 32 µs[44]; a preserved STX track can be 6 283 bytes[46], which at 200 ms is 31.83 µs/byte.[internal media study] **Resolution:** the drive's angle and byte position use the **track length**, not a hard-coded 6 250; the FDC's nominal clock is a default, exactly as `design/disk-path.md` §3 corrected. The visual's arcs use the track map's lengths.

---

## 12. What stays unknown

`documented` The register's §6.1 already carries A1–A6. This study adds **A7–A11**; none is a blocker for the byte-level model, and the visual marks the approximations.

| # | unknown | status | what would settle it |
|---|---|---|---|
| A1 | head alignment and azimuth on a worn drive | `unmeasurable` | a per-drive mechanical error; the image has no head geometry |
| A2 | write current and the medium's coercivity | `unmeasurable` | the mastering drive and media batch; a test write on the actual media |
| A3 | media wear, dropout, marginal bits | `unmeasurable` | an image can record the observed mask, not the cause |
| A4 | the reference drive's speed error, wow and flutter | `open` | a tachometer; the model is nominal |
| A5 | preamp/AGC and DPLL capture range | `open` | a BER sweep against cell width |
| A6 | write splice and erase on a rewritten sector | **`open`, partly modelled** | the byte path now exists (`design/disk-drive-model.md` §5); the exact splice extent/phase is A9 |
| **A7** | **spindle spin-up/deceleration curve** | **`open`** | a logic analyser on `IP`/`MO` at power-on; the model uses a linear 480 ms ramp and 300 rpm constant |
| **A8** | **actuator dynamics, resonance, micro-stepping, seek errors at 2 ms** | **`open`** | a scope on `STEP`/`TR00` and a step-settle sweep; the model uses 3/4 ms + 15 ms settle and one track per STEP |
| **A9** | **the write splice's exact extent and bit phase** | **`open`** | a write/rewrite test and a flux capture of the gap; the model marks the gap 2/gap 3 window and hatches the edges |
| **A10** | **the ST drive's erase-head timing and current** | **`open`** | a head-current probe or the drive's schematic; the model uses 500 µs/1 ms and 40 mA as defaults |
| **A11** | **the ST drive's index phase and burst tolerance** | **`open`** | an index-vs-byte-0 capture; the model sets index at byte 0 and uses ±400 µs only as a bound |

`derived` None of A7–A11 changes a **read** result: the FDC's byte stream is the image's, and the drive's nominal index/step timing is enough for the sector deadlines. They change the **visual's** honesty and the **write's** analogue residue; the model labels them.

---

## 13. Experiments

`derived` These are the measurements that would move a row from `unknown` to T1; they are not required for the model, but they are the only way to make the visual claim a real drive.

- **E1 — spin-up and index.** Logic analyser on `MOTOR ON`, `INDEX`, `READY` at power-on and on insertion[104]; 10 trials; fit the 0→300 rpm curve and the index period. Settles A7 and the index phase (A11).
- **E2 — step and settle.** Scope `STEP`, `TR00`, and the head's position (a test disk with a known track)[104]; step 1, 40 and 80 tracks; same-direction 3 ms and reversal 4 ms; measure the settle and the 2 ms risk. Settles A8.
- **E3 — write splice.** Write a known sector over a known track; `Read Track` the gap 2/gap 3 region before and after[23]; repeat 10×; measure the undefined byte window and the first-flux phase. Settles A9 and refines A6.
- **E4 — erase.** Write a track, read the adjacent track and the guard band; vary the erase delay on a drive with an accessible control (or use the MC3471's RC)[108]; measure the trim. Settles A10 (partly).
- **E5 — visual prototype.** A host-side renderer fed by a synthetic `DriveSnapshot` and `TrackMap`: 80 tracks, head radius, angle, sector under head, seek animation, write window, splice, erase band[106]. This is the **view's acceptance test**, not an emulator build.
- **E6 — model read-back.** Write a sector, read it back, compare the 512 bytes and the CRC[46]; write-protect; long track; 11 sectors; track 81. This is the **write path's acceptance test** when the code exists.

`derived` The model's acceptance is not "the visual looks right"; it is: (1) the FDC reads back what the write path wrote; (2) the snapshot's byte position matches the FDC's byte position at the same tick; (3) the track map's blocks and the image's bytes agree; (4) the visual's radius/angle match the formulas; (5) the no-disk and blank-disk states are distinguishable. The first three are machine tests; the last two are host tests.

---

## 14. Licence

`documented` The TEAC and Chinon specifications are manufacturer documents[104][105]. The ECMA standards are ECMA's[106][107]. The Motorola note is Motorola's[108]; the floppy user guide and Info-Coach's page are their authors'[109][110]. The study's own text is the project's. No tables or figures are reproduced; the numbers are cited.

---

## Sources

[23] http://info-coach.fr/atari/documents/_mydoc/FD-HD_Programming.pdf — Info-Coach — DMA / WD1772 Programming
[41] https://jsobola.atari8.info/DEREATARI/atarisch/1040ste_fsm.pdf — Atari — STe 520/1040 Computer Field Service Manual, C302481-001 Rev A, August 1991
[44] https://hxc2001.com/download/datasheet/floppy/thirdparty/FDC/WesternDigital/WD1772-JLG-V11.pdf — Western Digital — WD1772 Floppy Disk Controller Specification V1.1
[46] http://info-coach.fr/atari/documents/_mydoc/Pasti-documentation.pdf — J.-L. Guérin — Pasti File Documentation v0.5
[48] https://www.kryoflux.com/download/ipf_documentation_v1.6.pdf — J.-L. Guérin — IPF Documentation v1.6
[60] https://github.com/hatari/hatari/blob/master/src/includes/fdc.h — Hatari — `src/includes/fdc.h` (track-layout constants)
[61] https://github.com/steem/steem/raw/master/steem/diskman.cpp — Steem SSE — `steem/diskman.cpp` (STT/STX/ST loaders)
[104] https://katalog.atcomp.cz/katalog/2051001001/HW_HF_C929_C940_C991_Rev-A.pdf — TEAC — FD-235HF/C929/C940/C991 90 mm FDD Service Manual
[105] http://bitsavers.trailing-edge.com/pdf/chinon/Chinon_F-354C_135TPI_Double-Sided_3.5_In_Specifications.pdf — Chinon — F-354CAE/F-354C 135 TPI Double-Sided 3.5" Specifications
[106] https://ecma-international.org/wp-content/uploads/ECMA-125_1st_edition_december_1987.pdf — ECMA — ECMA-125: 90 mm Flexible Disk Cartridges, MFM at 15916 ftprad, 80 tracks (1st ed., Dec. 1987)
[107] https://ecma-international.org/publications-and-standards/standards/ecma-100/ — ECMA — ECMA-100: 90 mm Flexible Disk Cartridges (DD companion standard)
[108] https://ia801905.us.archive.org/35/items/bitsavers_motorolaapandWritingInFloppyDiskSystems_1029819/AN-0917_Reading_and_Writing_In_Floppy_Disk_Systems_text.pdf — Motorola — AN-0917: Reading and Writing in Floppy Disk Systems
[109] https://info-coach.fr/atari/hardware/_fd-hard/floppy-ug.pdf — M. Haardt, A. Knaff, D. C. Niemi — The Floppy User Guide
[110] https://info-coach.fr/atari/hardware/FD-Hard.php — J.-L. Guérin (DrCoolZic) — Atari ST Floppy Disks, hardware point of view
[111] https://www.retrotechnology.com/herbs_stuff/drive.html — Herb Johnson — Floppy drive and media notes (DD 665 Oe / HD 720 Oe)
