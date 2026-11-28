# A study that must be drawn: design contract and synthetic view

Use when the study's subject is a physical subsystem the emulator must expose — a drive, a tape, a scanner, a
mechanical actuator — and the user asks to *see* it, not only read it. The study settles the evidence; this file is
the companion artifact and its acceptance test.

## The split

- The **machine** owns the mechanism and the bytes; it has no UI dependency.
- The **host** owns the window and the renderer; it pulls an immutable snapshot at frame boundaries.
- The **design contract** (`design/<subsystem>-model.md`) is the interface between them: state, snapshot, media map,
  write path, units, edge cases. It is not a second study and not an implementation.

## The state

Write the state as the mechanism, not as the image:

- the actuators and their limits (`Track`, `Side`, `HeadLoaded`, `MaxTrack`);
- the time state (`LastIndexTick`, `LastStepTick`, `MotorOnTick`, `SettlingUntil`);
- the gates (`WriteGate`, `EraseGate`, `WriteProtected`, `Present`);
- the media version (one counter that invalidates the host's cache after a write).

`Ready` is a derived predicate over those fields, not a stored bit software can set. A **profile** record holds the
per-drive numbers (`MaxTrack`, erase delays, index jitter, write current) so a real-drive measurement can override the
defaults without changing the mechanism.

## The snapshot and the media map

- The **snapshot** is small, immutable and sampled at a frame boundary: tick, present, write-protected, motor, ready,
  track, side, head loaded, angle, rpm, gates, activity, byte position, track length, sector under the head, last
  index/step ticks, media version.
- The **media map** is a versioned block stream per track: gap, sync, ID, data, CRC, **splice**, **unknown**, with
  flags for fuzzy/CRC/RNF/deleted and a provenance marker (as-dumped vs synthesised). It is the truth; a sector list
  is a cache.
- Units are the machine's: ticks, bytes, millimetres, rpm. The host scales; the machine does not know pixels.
- Invariants: no live reads, no host callbacks, one snapshot per frame, one cache entry per `(track, side, version)`.
  A write invalidates one track, not the disk.

## Time

The angle and byte position are arithmetic on the index:

```
AngleTurns  = (now - LastIndexTick) / RevolutionTicks
BytePosition = (now - LastIndexTick) * TrackLength / RevolutionTicks
```

Use a 64-bit product or a fractional accumulator; a 32-bit product overflows on a long track. Wake at the **index**
and at the mechanical deadlines (motor ready, seek complete, write/erase edges) — never per byte for the view. The
controller's byte clock owns the byte cadence; the view is a projection.

## The write path

The design contract must say more than "bytes are written":

- the **window** in bytes (start, end, sector, start/end tick), and that the ID is **not** rewritten;
- the **splice** at the window edges, and the bytes the model cannot know (mark `Unknown`, do not invent data);
- the **erase gate** as a trim flag with its delays, not as a zero-fill of the track;
- the **write-protect** check before any byte changes;
- the **media-version** bump and the persistence rule (in memory by default; export or write-back only on request).

Separate **exact** from **nominal** in a fidelity table: byte ranges, sector phases and the block map are exact; the
spin-up curve, actuator dynamics, erase current and analogue residue are nominal or absent. The visual must not draw
what the model does not have.

## The view

A view that answers *where is the head, what is under it, what is the drive doing?* has four parts:

1. **Disk face** — tracks at their real radii, the selected side, the head at its radius, the index, the sector arcs,
   the hub/shutter/tab/HD hole. Do not draw side 1 under side 0; the two heads are offset.
2. **Track strip** — the block stream across the track length, the head's byte, the write window.
3. **Sector zoom** — the current sector's gap/sync/ID/data/CRC/gap blocks, the head byte, the write window and its
   splice edges. This is the view that makes a write legible.
4. **Timeline** — index pulses, motor, seek, read/write, write gate, erase, splice, with a short rolling window.

Add a status panel with the snapshot's fields and the provenance. Colour or icon the difference between as-dumped and
synthesised blocks, from-image and recomputed status, present and absent fuzzy masks.

## The synthetic mock

- Generate the data from the same constants as the study; label it **synthetic, not the emulator** and name the
  contract it exercises.
- Use a visible slow-motion factor for rotation and a larger one for the write, and say both. If the disk keeps
  spinning during the write, the write window drifts away from the head; **freeze the timeline during the write** (or
  scale both by the same factor) and snap the head to the window's start. A mock whose write arc and zoom window
  disagree is worse than no mock.
- Show the window even when idle (dashed, labelled) and make it live when writing; show the erase band and the splice
  hatch; keep the sector under the head highlighted so the labels are secondary.
- Drive the controls that prove the contract: seek slider, side toggle, write-protect toggle, write button, pause. Test
  the write to completion and the media-version bump, then the side/track change.
- If the browser harness refuses `file://`, serve the directory over localhost (`python3 -m http.server`) and open the
  `http://127.0.0.1:<port>/...` URL; a browser `file://` refusal is a harness fact, not a reason to rewrite the mock.
- Review the rendered frame with vision and the DOM: sector count, head position, window alignment, status values. A
  screenshot that looks right but whose DOM says the window is elsewhere is a bug.

## The code sketch

A design sketch is code the owner will review. Before writing it, **grep the tree for the seam types it names**: a
plausible `IClock`/`IMedia` pair that the engine does not have forces the implementer to invent a second time domain.
Pass the existing `long now` and the concrete media type; keep the snapshot a `readonly record struct` and the media
an immutable map. Then compile the sketch mentally against the namespace and the chip's existing constructor shape.

## Acceptance

1. The controller reads back what the write path wrote; the ID is unchanged.
2. The snapshot's byte position equals the controller's byte position at the same tick.
3. The block map and the image's bytes agree; a long track uses its own length.
4. The view's radius/angle match the formulas at the outer, middle and inner tracks.
5. `NoDisk` and `DiskWithoutImage` are distinguishable; a blank disk rotates, an empty drive does not.
6. A write-protected disk leaves the media version unchanged.
