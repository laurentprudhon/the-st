# The disk drive model — state, track map, write path, and the view

STATUS: **design (v1)** — 2026-11-28. The study it implements is
`research/floppy-drive-mechanics.md`; the image/track model it extends is `design/disk-path.md` §2–§3.
The owner's 2026-11-28 rulings — no disk lets TOS time out, **two separate read/write capabilities**, and
**write-back to the source** — are §2.3 and §5.3; the remaining open decisions are §8.

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

`derived` The model separates **what the machine can read** from **what it can write** (§2.3): the copy protection is an asymmetry between those two sets, not a property of one "drive profile". The write goes to the **source image**, as it would go to the disk (§5.3).

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
    BlockKind Kind, int Start, int Length, int Sector, int ByteTicks, BlockFlags Flags);

enum BlockKind { Gap, Sync, Idam, IdField, Data, Crc, Splice, Unknown }

[Flags] enum BlockFlags
{
    None = 0,
    Fuzzy = 1, CrcError = 2, Rnf = 4, Deleted = 8,
    WriteSplice = 16, Synthetic = 32
}

enum Provenance { AsDumped, SynthesizedGaps, Mixed }
```

`derived` The sector index is a cache over the blocks; the FDC's **Read Track** returns the blocks including gaps, and a write replaces a byte range, so a sector list cannot be the truth (the same argument as `design/disk-path.md` §2). `ByteTicks` is the **nominal 1 024** for a uniform track; a sector write on a long track writes 531 bytes at the FDC's 32 µs against the track's 31.83 µs, so the written block carries 1 024 and its neighbours 1 018.5 — a **local mixed rate**. The simple uniform `Length` arithmetic is the nominal case; a mixed track's visual and read use the block's `ByteTicks`.

### 2.3 The two capabilities, and the profile

`derived` The ST's copy protections live on an **asymmetry**: the machine can **read** features it cannot **write**. The model therefore has two capability records, not one; the write record has the **standard** path (Format / Write Sector) and the **raw** path (Write Track), because the FDC's raw command can produce some features the formatter cannot.

```csharp
[Flags] enum MediaFeature
{
    None = 0,
    LongTrack = 1,        // more than 6 250 bytes per revolution
    FuzzyBits = 2,        // weak/unstable bits
    NoFlux = 4,           // a DC-erased gap
    HalfTrack = 8,        // a track between the nominal tracks
    DuplicateIds = 16,    // more than one ID with the same C/H/S
    BadCrc = 32,          // a CRC that does not match
    DeletedDam = 64,      // an F8 data mark
    Overlap = 128,        // a sector that crosses the index
    LongSector = 256,     // a data field longer than 512 bytes
    DensityChange = 512   // a mid-track cell-rate change
}

sealed record ReadCapability(
    MediaFeature Features,
    int MaxTrackLength,
    int MinCellTicks,
    int MaxCellTicks);

sealed record WriteCapability(
    MediaFeature Standard,      // Format / Write Sector
    MediaFeature Raw,           // Write Track (arbitrary bytes)
    int TrackLength,            // 6 250 at 300 rpm, 250 kbit/s
    int StandardMaxSectors,     // the Formatter's 9/10; a Write Sector on an existing 11/12-sector track is not limited by it
    int RawMaxSectors,          // 11 with sub-minimum gaps; 12 needs a long track
    int StandardMinGapBytes,    // 12 in the ST format
    int RawMinGapBytes);        // 3 in the 11-sector layout
```

`derived` The ST's defaults:

```
Read = new ReadCapability(
    Features: LongTrack | FuzzyBits | NoFlux | HalfTrack | DuplicateIds |
              BadCrc | DeletedDam | Overlap | LongSector,   // DensityChange is A5/A11
    MaxTrackLength: 7_368,   // 12 × 614 at 300 rpm
    MinCellTicks: 869,       // 27.14 µs
    MaxCellTicks: 1_024);    // 32 µs

Write = new WriteCapability(
    Standard: DeletedDam | Overlap,
    Raw: DuplicateIds | BadCrc | DeletedDam | Overlap | LongSector,
    TrackLength: 6_250,
    StandardMaxSectors: 9,
    RawMaxSectors: 11,
    StandardMinGapBytes: 12,
    RawMinGapBytes: 3);

not writable = LongTrack | FuzzyBits | NoFlux | HalfTrack | DensityChange
```

`derived` The **read** flags are what the machine can encounter in an image; `HalfTrack` and `DensityChange` are the two the **byte map cannot represent** — they need the flux layer (deferred), so the byte model can only flag them, not draw them. The **write** flags are what the machine can produce: the standard path cannot make duplicate IDs, a bad CRC or a long sector; the raw Write Track can; neither can make a fuzzy bit, a no-flux gap, a half-track or a long track. That is the copy-protection asymmetry in one line.

`derived` The write path checks the **command's** feature set, not the union: a Write Sector writes `DeletedDam`/`Overlap` and cannot make a duplicate ID, a bad CRC or a long sector; a Format creates 9/10; a raw Write Track creates 11, duplicate IDs, a bad CRC, a long sector and an overlap. No path creates a fuzzy bit, a no-flux gap, a half-track, a long track or a density change. A Write Sector **on a pre-existing long track** is allowed — the `LongTrack` is not a feature it writes, only a feature it cannot create; that sector write is a local mixed rate (§5.1). The raw path's copyable features are why the copy protection is not "the ST cannot write any of it" but "the **standard** writer cannot create the layout, and the physical features are beyond both".

`derived` The profile:

```csharp
sealed record DriveProfile(
    ReadCapability Read,
    WriteCapability Write,
    int MaxTrack = 81,              // TEAC FD-235HF class; 79 for the standard's media
    int StepMilliseconds = 3,       // same direction; 4 for a reversal
    int SettleMilliseconds = 15,
    int AccessMilliseconds = 18,
    int IndexJitterMicroseconds = 0, // nominal; ±400 is the drive's bound (A11)
    int EraseOnMicroseconds = 500,   // MC3471 typical
    int EraseOffMicroseconds = 1000,
    int WriteCurrentMilliamps = 0,   // 0 = not modelled; the byte path does not use it
    int TrackLength = 6250,
    bool HighDensity = false);
```

`derived` The profile is where a real-drive measurement lands: `MaxTrack` (81 vs 83), the erase delays (A10), the index jitter (A11), the read's `MaxTrackLength`/cell range (A5), the exact `TrackLength` (6 250 vs 6 283). The defaults are the class's; the ST's exact drive is `unknown`.

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

`derived` The write path checks the **capability** first. A Write Sector is the `Standard` path; a Write Track is the `Raw` path. If the command would produce a feature outside the path's set (a `FuzzyBits`, `NoFlux`, `HalfTrack`, `LongTrack` or `DensityChange` write), the drive refuses and the FDC returns the error; the media is unchanged. The standard path cannot produce `DuplicateIds`, `BadCrc` or `LongSector`; the raw path can.

`derived` The media update:

1. **Check** `Present`, `WriteProtected`, `Ready`, `now >= LastStepTick + 576_000`, `now >= sideChange + 3_200`, and the **command's** capability: a Write Sector checks the `Standard` features it writes; a Write Track/Format checks the features it would create. A pre-existing long track is not a feature the sector write creates.
2. **Replace** `[Start, End)` with the FDC's bytes; on a track longer than 6 250 the FDC's 32 µs write clock covers `ceil(531 × Length / 6250)` track bytes, so `End = Start + ceil(531 × Length / 6250)`; the track's `Length` is unchanged, and the written block's `ByteTicks` is 1 024 while its neighbours keep the track's rate — a **local mixed rate** that the source's `.stx` timing can carry and a sector-only source cannot. On a 6 250-byte track the window is 531 bytes and the rate is uniform.
3. **Mark** `BlockKind.Splice` at `Start` and `End - 1` (or a 2–4 byte hatch), and `BlockKind.Unknown` for the first/last bytes whose phase the model cannot know. The **data field** and **CRC** are new; the **ID** and its CRC are untouched.
4. **Erase** `EraseGate` on at `StartTick + 16_000`, off at `EndTick + 32_000`; this sets a **trim flag**, not zeros.
5. **Clear** the `Fuzzy`/`NoFlux` flags in `[Start, End)` — the write gate overwrites them; the source's read-only feature in that range is gone. **Bump** `MediaVersion`; invalidate the track's cache.
6. **Persist** the updated track to the source store (§5.3); return the FDC's status: write-protect if step 1 failed; otherwise the command's normal completion.

`derived` The **splice** is the *start* of the new bytes against the old; the gap 2 zero preamble is what lets the DPLL resync, and the FDC's `Read Track` may read the splice bytes incorrectly.[23][110] The model marks them; it does not invent a pseudo-random pattern for them. If the host wants a deterministic "undefined" fill for the visual, it is a **display** choice, not media data.

`derived` The long-track sector write is the one place the FDC's 32 µs clock and the track's 31.83 µs cadence disagree: the FDC writes 531 bytes in 16.99 ms, which is 534 bytes of a 6 283-byte track; the model writes the **scaled** range and leaves the rest. A whole-track `WriteTrack` on a 6 283-byte track writes 6 250 bytes and leaves the last 33 bytes old — the raw path cannot make a long track; that is the read/write asymmetry in the write-back.

### 5.2 The track write / format

`derived` The FDC's **Write Track** command supplies the whole block stream from **index to index**; the drive writes until the next index and marks the **index gap's first bytes** as unknown (ECMA: "some of the first bytes may have become ill-defined due to overwriting").[23][106] The window is `[0, 6 250)`; a longer source track is **normalized** to 6,250 bytes — the ST's write clock writes 6,250 a revolution, so the long-track feature is lost and the source's track length becomes 6,250; a longer stream is truncated at the index. The splice is at the index; the erase gate follows the same delays. A `Format` is a `WriteTrack` with the standard layout, not a special media operation. The raw path can write duplicate IDs, a bad CRC, a long sector and an overlap; it cannot write a fuzzy bit, a no-flux gap, a half-track, a long track or a mid-track density change. The source store (§5.3) persists the bytes.

### 5.3 Write protect, media change, persistence

`derived` `WriteProtected` is the tab: the drive refuses the write, the FDC reports write-protect, the media version does not change.[104] The visual shows the tab open and the WG never asserted.

`derived` `Insert`/`Eject` set `Present`, reset the head to track 0 (auto-recal on power-on; the real drive's insertion does not necessarily recalibrate, but the ST's boot does), set `MediaVersion++`, and clear the track cache. The disk-change signal is optional on the ST's connector; the host's insertion event is the truth.

`derived` **The source is the disk.** The loader opens the image **read-write**; `WriteSector`/`WriteTrack` update the track map **and** the source store; `Flush` on eject/exit. There is no in-memory-only mode and no read-only source by default. The real disk has no undo; the host may keep a backup, but the model's contract is in-place.

```csharp
interface IMediaStore
{
    MediaFormat Format;
    bool CanWrite(MediaFeature feature, WriteScope scope); // the format's limit, not the drive's
    void WriteSector(int track, int side, int sector, ReadOnlySpan<byte> data);
    void WriteTrack(int track, int side, ReadOnlySpan<byte> bytes);
    void Flush();
}

enum WriteScope { Standard, Raw }
```

| source format | sector write | raw track write | what survives |
|---|---|---|---|
| `.stx` | yes: track image + sector descriptor CRC/status | yes: track image, sector descriptors, fuzzy mask zeroed | bytes, IDs/CRC/status/fuzzy, track length |
| `.stt` | yes: track image/IDs | yes: track image/IDs | bytes/IDs; no fuzzy/timing |
| `.st` / `.dim` | yes: the 512-byte sector | **not representable** — refuse; the host may convert to `.stx`/`.stt` | sectors only |
| `.msa` | yes: the sector, re-compress the track | **not representable** — refuse/convert | sectors only |

`derived` The format's `CanWrite` is separate from the drive's `WriteCapability`: a `.st` source is a sector-only **representation**; a raw `WriteTrack` on it is not a drive limitation, it is a file limitation. A title that writes raw-track features needs a `.stx`/`.stt` source, or the host converts; the model must not silently drop the raw bytes. A write clears the fuzzy mask in the written range; a `.stx`'s sector descriptor CRC/status is recomputed by the FDC; the track length and offsets stay — **except** a whole-track `WriteTrack` on a longer track, which normalizes it to 6,250 bytes, so the `.stx` writer rebuilds the track image and the sector descriptors' offsets. The write-protect **tab** is the drive's `WPRT`; a read-only **file** is a host failure, reported separately.

## 6. The visual contract

`derived` The renderer is **host-side** (host layer #12). It pulls `DriveView` at a frame boundary (50 Hz) and draws:

| view | data | must not |
|---|---|---|
| disk face | `Present`, `Track`, `Side`, `AngleTurns`, `r(track, side)`, the track map's sector arcs, the index mark, the hub/shutter/tab/HD hole | draw side 1 under side 0; spin a `NoDisk` drive; draw a track the map does not have |
| track strip | `CurrentTrack.Blocks`, `BytePosition`, `SectorUnderHead`, `WriteGate`, `EraseGate`, the splice/unknown flags | invent a CRC/fuzzy status the image does not carry |
| timeline | index ticks, motor, seek, read/write, WG, erase, splice | show a spin-up curve the model does not have |
| status | the snapshot's fields + `MediaDescriptor` + provenance | claim a real-drive measurement |

`derived` The status panel also carries the two **capability badges**: **read** (the features the machine can read) and **write** (standard/raw, with the not-writable features named). A write attempt outside the path's set shows the refusal; a successful write marks the **source dirty** and, on `Flush`, the file. The view must not draw a feature the byte map cannot represent (`HalfTrack`, `DensityChange`) as if it were a track; it flags it as flux-only.

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
| track length, long tracks | **read exact per image; a whole-track write normalizes to 6 250** | 6 250 vs 6 283; the map's `Length` |
| read/write capability sets | **exact per the ST's documented sets**; the raw/standard scopes | §2.3; the bounds A1/A5 |
| write-back | **exact bytes**; the source format's limits | `IMediaStore`; the `.st` raw refusal |
| mixed-rate track | **block `ByteTicks`**; the FDC's 32 µs sector | A9/A11 |
| index jitter | **nominal 0** for v1 | A11 |
| index period / revolution | **exact nominal**; jitter optional | 300 rpm; A4/A11 |
| track/side, head radius | **exact formula**; mechanics nominal | 0.1875 mm; one STEP = one track |
| seek | **step + settle** (3/4 ms, 15 ms); no resonance | A8 |
| motor | **480/505/490 ms** endpoints; linear ramp | A7 |
| write bytes, ID, CRC, gap window | **exact** at the byte level | the FDC's stream; the block model |
| write splice | **marked**; exact phase unknown | A6/A9 |
| erase | **500 µs/1 ms** default; trim flag | A10 |
| analogue, coercivity, residual, wear | **not modelled** | A1–A3, A5 |

## 8. Decisions (owner)

1. **`NoDisk` vs `DiskWithoutImage`** — **settled 2026-11-28**: no disk → no rotation, no index, **no synthetic index**; the FDC's search waits and **TOS's timeout ends the poll**; the machine must not hang. `DiskWithoutImage` still spins and times out with record-not-found. The two states are distinct in the snapshot and the view.
2. **`MaxTrack`** — **open**: 81 (the TEAC class) or 83 (the ST's 82/83-track protections)? The default is 81; 83 needs a measured drive.
3. **Write persistence** — **settled 2026-11-28**: **write-back to the source**, as the disk; §5.3. A sector-only source (`.st`/`.msa`/`.dim`) cannot hold a raw track write; the store refuses or offers a conversion, and the host must not silently drop it.
4. **Index jitter** — **nominal 0 for v1; A11 open**. The read path is unaffected (the data is self-clocked; the index only times the 4-rev timeout). The TEAC's **period** is 197–203 ms (±1.5 %, ±94 bytes a revolution); its **burst** error is ±400 µs (±12.5 bytes). A Write Track's tail between indexes varies by up to ±94 bytes (period) or ±12 (burst); a protection that measures index-to-index time or byte count could see it. A measured profile can replace 0 later.
5. **Drive profile defaults** — the F-354C class's numbers; a measured 1040STE drive can override.
6. **The FDC's write window** — the proposal: the FDC supplies gap 2/gap 3; the drive writes the range; not ruled.
7. **The pin page correction** — done 2026-11-28.
8. **The undefined bytes' display** — hatch, not a deterministic media fill.
9. **The two capabilities** — **settled 2026-11-28**: the §2.3 feature sets, the standard/raw write scopes and the visual badges.
10. **A sector-only source with a raw write** — the store's `CanWrite` is checked **before** the write: if the format cannot hold the raw track, the host refuses or converts; the drive does not silently write to a file that cannot hold it.

## 9. Cost, and the tests

`derived` The model is **four types and one boundary**: `DiskDrive`, `DiskMedia`, `IMediaStore`, `DriveSnapshot`/`TrackMap`; the host's renderer is outside. It adds **one wake per index** (5/s), a motor-ready wake, a seek-complete wake and the write's two wakes. It adds no per-byte wake; the FDC's byte clock stays the controller's. The snapshot is under 128 bytes; the media is ~1 MB per 2-sided 82-track disk if fully materialised, less if sparse.

`derived` The tests are the acceptance of §6 and §5:

| test | pass |
|---|---|
| write a sector, read it back | 512 bytes and CRC match; ID unchanged |
| write-protect | FDC status write-protect; media version unchanged |
| long track | the 6 283-byte track's reads/writes stay inside the map; a sector write covers 534 track bytes; the track length is unchanged |
| 11 sectors | the map's blocks, not a 9-sector constant |
| track 81 | the head reaches it; `TR00` false; step-in ignored |
| blank disk | index; no blocks; FDC timeout |
| snapshot vs FDC | `BytePosition` equals the FDC's byte position at the same tick |
| visual formulas | radius/angle match at 0/40/79/81 |
| read asymmetry | a 6 283-byte track, a fuzzy sector, an NFA gap, a duplicate-ID/bad-CRC track load and read; the FDC returns the bytes/status; the view flags the feature |
| write-back | a sector write changes the source `.stx`/`.st` file; a reload shows the bytes; the media version is bumped |
| raw write | a `.stx` source with duplicate IDs/bad CRC/long sector survives a Write Track + write-back + reload |
| sector-only raw write | a `.st` source refuses the raw track write (or the host converts); the source is not silently corrupted |
| fuzzy cleared | a `.stx` fuzzy sector written by a sector write has its mask zeroed for the written range; reload confirms |
| long-track sector | the 6 283-byte track's 531-byte window covers 534 track bytes; the track length is unchanged |
| no disk | no index; no rotation; `Ready` false; TOS's timeout ends the poll without a hang |

`derived` The machine tests are the write/read-back, the capabilities, the no-disk timeout and the write-back; the host/view tests are the formulas and the badges. A visual that passes the look but fails the write-back or the capability badge is not the model's visual.

## 10. Relationship to the existing pages

- `design/disk-path.md` §2 (the block stream) and §3 (the byte clock) are the parent model; this document adds the drive's state, the media's write path and the view contract.
- `research/floppy-drive-mechanics.md` is the evidence for every number here; its §6.6–§6.7 are the asymmetry and the write-back, and its §12 is the A7–A11 register.
- `design/emulator-limitations.md` §6.1 carries the analogue rows; A6 is re-worded to say the byte-level write path exists.
- `atari-st-hardware/pins/04-din14-floppy-port.md` gets a dated correction of its driver column; the drive model uses logical signals, so the pin error does not block it.
- The FDC model (`research/standard-parts-implementation.md` §4) keeps the command/DRQ/CRC; the drive model is the `RD`/`IP`/`TR00`/`WPRT` side.
