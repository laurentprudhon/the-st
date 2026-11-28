# The disk drive model — state, track map, write path, and the view

STATUS: **design (v1), proposal** — 2026-11-28. The study it implements is
`research/floppy-drive-mechanics.md`; the image/track model it extends is `design/disk-path.md` §2–§3.
The §8 decisions are the owner's; nothing here overrides a ruling without saying so.

## 1. What it is, and what it is not

`derived` The disk path has three layers, and this document is the middle one:

| layer | owns | document |
|---|---|---|
| the controller | the WD1772's commands, registers, byte clock, DMA/DRQ, CRC, address-mark search | `research/standard-parts-implementation.md` §4; `design/disk-path.md` §3 |
| **the drive** | **the spindle, the carriage, the heads, the write gate, the erase gate, the media bytes** | **this document** |
| the host | the image loader, the window, the visual renderer | `design/disk-path.md` §6; host layer #12 |

`derived` It is **not** the FDC's command semantics, and it is **not** a UI. The FDC asks the drive for a byte at a time and pulses `STEP`; the drive answers with `RD`, `IP`, `TR00`, `WPRT` and, on a write, consumes `WD`/`WG`. The host samples an immutable snapshot; the machine never calls the host.

`derived` It is **not** a flux model. The read path is the media's **byte/cell stream** from the track map; the FDC's DPLL and CRC are the controller's. The analogue residue (coercivity, residual magnetisation, head wear, the exact splice) is A1–A11 in `design/emulator-limitations.md` §6.1.

`derived` The existing `Disk` stub (`src/TheST.Machine/Chips/Stubs.cs`) stays until the build reaches the drive; this document does not change the stub's boot-poll contract.

## 2. The state

### 2.1 The drive

```csharp
namespace TheST.Machine.Chips;

/// <summary>The drive's electromechanical state. The FDC owns the command; this owns the mechanism.</summary>
sealed class DiskDrive
{
    public DiskDrive(DriveProfile profile, DiskMedia media);

    public DriveProfile Profile { get; }

    // The FDC/PSG inputs, as levels.
    public bool MotorOn;        // MO
    public bool DriveSelect;    // DS0/DS1
    public bool Side;           // SIDE ONE SELECT: false = side 0, true = side 1
    public bool Direction;      // DIRC: true = step-out, false = step-in
    public bool WriteGate;      // WG
    public bool WriteData;      // WD (the edge is the transition; the level is the pulse)
    public bool Density;        // DDEN/HD IN, if the profile has HD

    // The drive outputs, as levels.
    public bool ReadData;       // RD
    public bool Index;          // IP
    public bool Track0;         // TR00
    public bool WriteProtected; // WPRT
    public bool DiskChange;     // DCHG, if wired

    // The mechanism.
    public bool Present;        // a cartridge is in the drive
    public bool HeadLoaded;     // == Present (no load solenoid)
    public int Track;           // 0..Profile.MaxTrack
    public int StepPhase;       // Track*2 + 0/1, visual only
    public long LastStepTick;
    public long SettlingUntil;  // LastStepTick + 15 ms
    public long LastIndexTick;
    public long MotorOnTick;
    public bool EraseGate;      // the tunnel erase coil

    public long MediaVersion;   // bumped on insert and on every write
    public DriveActivity Activity; // Idle, Seek, Read, Write, Format

    public DriveSnapshot Snapshot(long now);
    public TrackMap? CurrentTrack();
    public void Step(long now);              // one STEP pulse, trailing edge
    public void SetSide(bool side, long now);
    public void WriteSector(long now, WriteWindow window, ReadOnlySpan<byte> bytes);
    public void WriteTrack(long now, ReadOnlySpan<byte> bytes);
    public void Insert(IMedia media, long now);
    public void Eject(long now);
}
```

`derived` `Ready` is not a stored bit: `Present && MotorOn && now >= MotorOnTick + 16_160_000 && now >= SettlingUntil && indexSeen`. The FDC's `h` flag and the TOS poll see the same predicate; software cannot set it.

`derived` `Track0` is the sensor: true when `Track == 0`, valid **2.8 ms** after the last step; the drive ignores a step-out while `Track0` is true and a step-in from `Profile.MaxTrack` (81 for the TEAC class).[104]

### 2.2 The media

```csharp
/// <summary>The bytes of a disk, as a block stream per track — the same truth as design/disk-path.md §2.</summary>
sealed class DiskMedia
{
    public int TrackCount;          // 80 nominal, Profile.MaxTrack+1 reachable
    public int Sides;               // 2
    public int Length;              // 6 250 nominal, 6 283 in a preserved STX
    public bool Blank;              // no image: an unformatted disk
    public bool WriteProtected;     // the tab
    public bool HighDensity;        // the HD hole
    public ulong Version;
    public Provenance Provenance;
    public TrackMap GetTrack(int track, int side);
    public void Replace(int track, int side, ReadOnlySpan<byte> bytes, WriteWindow window);
}
```

`derived` `TrackMap` and `Block` are the media's truth, not a sector list:

```csharp
sealed record TrackMap(int Track, int Side, int Length, ImmutableArray<Block> Blocks, ulong Version);

readonly record struct Block(
    BlockKind Kind, int Start, int Length, int Sector, BlockFlags Flags);

enum BlockKind { Gap, Sync, Idam, IdField, Data, Crc, Splice, Unknown }

[Flags] enum BlockFlags
{
    None = 0,
    Fuzzy = 1, CrcError = 2, Rnf = 4, Deleted = 8,
    WriteSplice = 16, Synthetic = 32
}

enum Provenance { AsDumped, SynthesizedGaps, Mixed }
```

`derived` The sector index is a cache over the blocks; the FDC's **Read Track** returns the blocks including gaps, and a write replaces a byte range, so a sector list cannot be the truth (the same argument as `design/disk-path.md` §2).

### 2.3 The profile

```csharp
sealed record DriveProfile(
    int MaxTrack = 81,              // TEAC FD-235HF class; 79 for the standard's media
    int StepMilliseconds = 3,       // same direction; 4 for a reversal
    int SettleMilliseconds = 15,
    int AccessMilliseconds = 18,
    int IndexJitterMicroseconds = 0, // nominal; ±400 is the drive's bound
    int EraseOnMicroseconds = 500,   // MC3471 typical
    int EraseOffMicroseconds = 1000,
    int WriteCurrentMilliamps = 0,   // 0 = not modelled; the byte path does not use it
    int TrackLength = 6250,
    bool HighDensity = false);
```

`derived` The profile is where a real-drive measurement lands: `MaxTrack` (81 vs 83), the erase delays (A10), the index jitter (A11), the exact `TrackLength` (6 250 vs 6 283). The defaults are the class's; the ST's exact drive is `unknown`.

## 3. The snapshot and the track map

```csharp
readonly record struct DriveSnapshot(
    long Tick,
    bool Present, bool WriteProtected, bool MotorOn, bool Ready,
    int Track, int Side, bool HeadLoaded,
    double AngleTurns,          // 0..1, (Tick - LastIndexTick) / RevolutionTicks
    int Rpm,                    // 300 nominal, 0 when stopped
    bool WriteGate, bool EraseGate,
    DriveActivity Activity,
    int BytePosition, int TrackLength, int SectorUnderHead,
    long LastIndexTick, long LastStepTick,
    ulong MediaVersion);

readonly record struct MediaDescriptor(
    int TrackCount, int Sides, int Length,
    bool Blank, bool WriteProtected, bool HighDensity, ulong Version);

readonly record struct DriveView(
    DriveSnapshot Snapshot,
    TrackMap? CurrentTrack,
    MediaDescriptor Media);
```

`derived` The invariants:

| # | invariant | why |
|---|---|---|
| 1 | immutable | the host can hold a snapshot across a frame; no torn reads of the track map |
| 2 | frame-boundary sampling | the machine is not re-entered per pixel |
| 3 | `BytePosition` is derived from `LastIndexTick` and `TrackLength`, not stored per byte | the same arithmetic as the FDC's byte clock; no per-byte wake |
| 4 | `CurrentTrack` is versioned | the host caches `(track, side, version)`; a write invalidates one track |
| 5 | no host callback | the machine has no UI dependency; the view is a pull, not a push |
| 6 | units are machine units | ticks, bytes, mm, rpm; the host scales |

`derived` The snapshot is **under 128 bytes** (a tick, two longs, a version, eight ints/bools and the angle); the track map is a shared immutable structure. A 2 × 82 × 6 250-byte disk is **~1 MB** if every track is materialised; a sparse map (only loaded/written tracks) is the default.

## 4. Time and events

`derived` The model lives on the machine's tick grid; `RevolutionTicks = 6_400_000` at 32 MHz for 300 rpm. The **angle is arithmetic**:

```
AngleTurns = (now - LastIndexTick) / RevolutionTicks
BytePosition = (now - LastIndexTick) * TrackLength / RevolutionTicks
```

`derived` The product must be 64-bit (`long`) or use a fractional accumulator; a 32-bit `now × 6 283` overflows. The `BytePosition` is clamped to `[0, TrackLength)`; `SectorUnderHead` is the block whose `[Start, Start+Length)` contains it.

`derived` The **wakes**:

| wake | when | cost |
|---|---|---|
| index | every 6.4 M ticks while ready | 5/s |
| motor ready | once, at `MotorOnTick + 16_160_000` | one |
| seek complete | once, at `LastStepTick + 15.8–17.9 ms` | per seek |
| write gate / erase | at the command's start/end and the erase delays | per write |
| media change | on insert/eject | rare |

`derived` The **byte position is not a wake**. The FDC's DRQ owns the byte cadence; the visual samples the arithmetic. The write path applies the bytes in a **bulk** update at the FDC's command boundary, with the start/end ticks recorded for the splice and the erase; the media version bumps once per command. This is the same rule as `design/disk-path.md` §3: the sector period is the only resumable deadline.

`derived` The read path: the drive exposes the media's bytes at the FDC's current byte position; the FDC's DPLL/CRC run on the byte/cell stream. The drive does **not** synthesise `RD` pulses per flux transition; the controller's byte clock is the interface. If a later flux layer is added, it sits **under** this model, not in it.

## 5. The write path

### 5.1 The sector write

`derived` The FDC's **Write Sector** command (after the ID match) supplies the bytes from **gap 2** through the **first byte of gap 3**; it does not rewrite the ID. The drive's window is:

```csharp
readonly record struct WriteWindow(
    int Start,          // gap2Start
    int End,            // gap3Start + 1
    int Sector,         // the ID's sector, for the cache
    long StartTick,     // WG asserted
    long EndTick,       // WG deasserted
    bool Deleted);
```

`derived` The media update:

1. **Check** `Present`, `WriteProtected`, `Ready`, `now >= LastStepTick + 576_000`, `now >= sideChange + 3_200`.
2. **Replace** `[Start, End)` with the FDC's bytes; the bytes outside stay old.
3. **Mark** `BlockKind.Splice` at `Start` and `End - 1` (or a 2–4 byte hatch), and `BlockKind.Unknown` for the first/last bytes whose phase the model cannot know. The **data field** and **CRC** are new; the **ID** and its CRC are untouched.
4. **Erase** `EraseGate` on at `StartTick + 16_000`, off at `EndTick + 32_000`; this sets a **trim flag**, not zeros.
5. **Bump** `MediaVersion`; invalidate the track's cache.
6. **Return** the FDC's status: write-protect if step 1 failed; otherwise the command's normal completion.

`derived` The **splice** is the *start* of the new bytes against the old; the gap 2 zero preamble is what lets the DPLL resync, and the FDC's `Read Track` may read the splice bytes incorrectly.[23][110] The model marks them; it does not invent a pseudo-random pattern for them. If the host wants a deterministic "undefined" fill for the visual, it is a **display** choice, not media data.

### 5.2 The track write / format

`derived` The FDC's **Write Track** command supplies the whole block stream from **index to index**; the drive replaces the track with the bytes, writes until the next index, and marks the **index gap's first bytes** as unknown (ECMA: "some of the first bytes may have become ill-defined due to overwriting").[23][106] The window is `[0, Length)`; the splice is at the index; the erase gate follows the same delays. A `Format` is a `WriteTrack` with the standard layout, not a special media operation.

### 5.3 Write protect, media change, persistence

`derived` `WriteProtected` is the tab: the drive refuses the write, the FDC reports write-protect, the media version does not change.[104] The visual shows the tab open and the WG never asserted.

`derived` `Insert`/`Eject` set `Present`, reset the head to track 0 (auto-recal on power-on; the real drive's insertion does not necessarily recalibrate, but the ST's boot does), set `MediaVersion++`, and clear the track cache. The disk-change signal is optional on the ST's connector; the host's insertion event is the truth.

`derived` **Persistence**: the model writes **in memory**. A host may export the result as a `.stx`/`.stt` (bytes + ID/CRC/status + fuzzy) or a `.st`/`.msa`/`.dim` (sectors only); the source image is **read-only** unless the host explicitly asks for a write-back. A sector-only image cannot carry the splice/undefined flags; the export must say so. This is the same provenance rule as `design/disk-path.md` §6.1.

## 6. The visual contract

`derived` The renderer is **host-side** (host layer #12). It pulls `DriveView` at a frame boundary (50 Hz) and draws:

| view | data | must not |
|---|---|---|
| disk face | `Present`, `Track`, `Side`, `AngleTurns`, `r(track, side)`, the track map's sector arcs, the index mark, the hub/shutter/tab/HD hole | draw side 1 under side 0; spin a `NoDisk` drive; draw a track the map does not have |
| track strip | `CurrentTrack.Blocks`, `BytePosition`, `SectorUnderHead`, `WriteGate`, `EraseGate`, the splice/unknown flags | invent a CRC/fuzzy status the image does not carry |
| timeline | index ticks, motor, seek, read/write, WG, erase, splice | show a spin-up curve the model does not have |
| status | the snapshot's fields + `MediaDescriptor` + provenance | claim a real-drive measurement |

`derived` **Geometry** (side 0; side 1 is 1.5 mm nearer the hub):

```
r(track) = 39.5000 - 0.1875 * track   mm
r(0) = 39.5000   r(79) = 24.6875   r(81) = 24.3125
```

`derived` **Angle**: `AngleTurns` is 0 at index and increases **counter-clockwise looking at side 0**; the head is fixed at the line of access. The sector arcs are `block.Start / Length × 360°`, so a 6 283-byte track's arcs are slightly compressed; the view uses the map's `Length`, not 6 250.

`derived` **Sector under the head**: the block range containing `BytePosition`; if it is a gap, the status says "in gap" and the nearest sector; if the track is blank, "unformatted". The write window is the `[Start, End)` range; the erase band is a thin annulus at the track's edges, not a data colour.

`derived` **Provenance**: colour or icon the gaps as **synthetic** vs **as-dumped**, the FDC status as **from-image** vs **recomputed**, the fuzzy mask as **present** vs **absent**. A user looking at a `.st` and an `.stx` of the same title must see why they differ.

`derived` **Edge cases**: `NoDisk` (no rotation, no index, no tracks); `DiskWithoutImage` (rotation, index, blank tracks, FDC timeout); write-protected (WG never asserted); long track (6 283); 11/12 sectors (map, not a 9-sector constant); fuzzy/CRC/RNF (flags, not "fixed"); deleted DAM; splice (hatch); track 80/81 (extra annuli); side 1 (1.5 mm); HD (hole shown, model DD unless the profile says HD).

`derived` **Acceptance** (E5): the renderer's radius/angle match the formulas at tracks 0/40/79/81; the sector under the head matches the FDC's byte position at the same tick; a seek animates the head without moving the disk's data; a write shows the window and the splice; a `NoDisk` view shows no rotation.

## 7. Fidelity

| layer | fidelity | why |
|---|---|---|
| bytes, block ranges, sector phases | **exact** | from the image/nominal; integer arithmetic |
| track length, long tracks | **exact per image** | 6 250 vs 6 283; the map's `Length` |
| index period / revolution | **exact nominal**; jitter optional | 300 rpm; A4/A11 |
| track/side, head radius | **exact formula**; mechanics nominal | 0.1875 mm; one STEP = one track |
| seek | **step + settle** (3/4 ms, 15 ms); no resonance | A8 |
| motor | **480/505/490 ms** endpoints; linear ramp | A7 |
| write bytes, ID, CRC, gap window | **exact** at the byte level | the FDC's stream; the block model |
| write splice | **marked**; exact phase unknown | A6/A9 |
| erase | **500 µs/1 ms** default; trim flag | A10 |
| analogue, coercivity, residual, wear | **not modelled** | A1–A3, A5 |

## 8. Open decisions (owner)

1. **`NoDisk` vs `DiskWithoutImage`** — the study's §4.3. The current `design/disk-path.md` §5 rule ("motor spins with no image") is right for a blank disk and wrong for an empty drive. Does the model get two states, and does the FDC get a synthetic index or rely on TOS's timeout for `NoDisk`?
2. **`MaxTrack`** — 81 (the TEAC class) or 83 (the ST's 82/83-track protections)? The study's evidence is 81; 83 needs a measured drive.
3. **Write persistence** — in-memory only, a sidecar, or write-back to the source? The proposal is in-memory + explicit export; the source image stays read-only.
4. **Index jitter** — nominal (0) or ±400 µs? The visual is nominal; a real-drive measurement could add the jitter.
5. **Drive profile defaults** — the F-354C class's numbers; a measured 1040STE drive can override.
6. **The FDC's write window** — who supplies gap 2/gap 3: the FDC's Write Sector command or the drive's formatter? The proposal: the FDC supplies; the drive writes the range.
7. **The pin page correction** — the dated correction of the DIN14 driver column (PSG port A for side/drive selects; FDC for `MO`/`STEP`/`DIRC`/`WD`/`WG`/`DDEN`).
8. **The undefined bytes' display** — hatch vs a deterministic fill; the media must not carry invented data.

## 9. Cost, and the tests

`derived` The model is **three types and one boundary**: `DiskDrive`, `DiskMedia`, `DriveSnapshot`/`TrackMap`; the host's renderer is outside. It adds **one wake per index** (5/s), a motor-ready wake, a seek-complete wake and the write's two wakes. It adds no per-byte wake; the FDC's byte clock stays the controller's. The snapshot is 64 bytes; the media is ~1 MB per 2-sided 82-track disk if fully materialised, less if sparse.

`derived` The tests are the acceptance of §6 and §5:

| test | pass |
|---|---|
| write a sector, read it back | 512 bytes and CRC match; ID unchanged |
| write-protect | FDC status write-protect; media version unchanged |
| long track | the 6 283-byte track's reads/writes stay inside the map |
| 11 sectors | the map's blocks, not a 9-sector constant |
| track 81 | the head reaches it; `TR00` false; step-in ignored |
| no disk | no index; no rotation; `Ready` false |
| blank disk | index; no blocks; FDC timeout |
| snapshot vs FDC | `BytePosition` equals the FDC's byte position at the same tick |
| visual formulas | radius/angle match at 0/40/79/81 |

`derived` The first five are machine tests; the last four are host/view tests. A visual that passes the look but fails test 8 is not the model's visual.

## 10. Relationship to the existing pages

- `design/disk-path.md` §2 (the block stream) and §3 (the byte clock) are the parent model; this document adds the drive's state, the media's write path and the view contract.
- `research/floppy-drive-mechanics.md` is the evidence for every number here; its §12 is the A7–A11 register.
- `design/emulator-limitations.md` §6.1 carries the analogue rows; A6 is re-worded to say the byte-level write path exists.
- `atari-st-hardware/pins/04-din14-floppy-port.md` gets a dated correction of its driver column; the drive model uses logical signals, so the pin error does not block it.
- The FDC model (`research/standard-parts-implementation.md` §4) keeps the command/DRQ/CRC; the drive model is the `RD`/`IP`/`TR00`/`WPRT` side.
