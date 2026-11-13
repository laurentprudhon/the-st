# What proves the timing model — the validation plan

The operational answer to ticket [#11](https://github.com/laurentprudhon/the-st/issues/11): what
"the walking skeleton proves the timing model" *means*, which instruments carry it, which rows gate a
run, and which are manual or deferred. #10 builds against this file and interprets nothing.

Ruling: ticket #11, 2026-11-09 (owner's answers, quoted in the ticket's resolution comment). The
instrument contracts are fixed elsewhere and not re-decided here — the per-instant digest and its
allow-list in [ADR 0008](../docs/adr/0008-the-schedulers-rules.md) §6, the assertions in §5, the
integral sample counts in [ADR 0001](../docs/adr/0001-one-oscillator-one-time-domain.md), the
retention watermark in [ADR 0004](../docs/adr/0004-wires-are-append-only-value-logs.md) (Revision
2026-11-07), the acceptance test in [ADR 0010](../docs/adr/0010-tos-image-provisioning.md).

No ADR 0011. Every row below traces to an accepted ADR clause or to a rate in
[`crossings.md`](crossings.md); a decision with no alternative worth recording is a checklist, and any
future session re-derives this checklist identically from those documents.

CI is permanently offline (ADR 0010). **"Automated" means a script in this repository, run by hand**,
whose exit code is the verdict. There is no hosted pipeline, now or later.

## 1. The two axes, and why one is not enough

The **digest** proves the machine took the same path twice. It cannot prove the path is an ST's: a
model whose VBL runs at 60 Hz, or whose frame is 599 000 ticks, produces two byte-identical digest
streams and is still wrong. Determinism is consistency, not fidelity.

The **anchors** prove the path is shaped like the machine's: the clock arithmetic is the one
ADR 0001 chose, the beams and the timers fire at the catalogue's rates, and an operating system
written for real hardware runs to its desktop on it. None of them proves the model is *reproducible*.

So the pass criterion is the conjunction. A run passes when **every gate row in §3 passes**, and no
row may be waived by "it boots" or by "the digests match".

## 2. The instrument

One artefact, `tools/validation-harness/` — a headless runner with no Avalonia, no window, no host
clock inside the machine:

- boots one `roms/emutos-1.4/etos256<lang>.img` (the 18 images, the manifest and `LICENSE.txt` are
  #10's packaging job; the language is fixed by argument so a run is reproducible),
- runs exactly `N` emulated frames (default: enough to reach the desktop and then 100 more),
- writes three files into `runs/<stamp>/`: `run.digest` (the per-instant fold stream, one hex word per
  batch), `run.state` (the probe values: committed registers and logs, plus the OS variables named in
  G6), and `run.report` (one page — the §3 tables with measured values filled in),
- is driven by `scripts/validate.sh`, which runs the gates, prints the report and exits 0 or 1.

> **Note — 2026-11-11** (ticket #10, delivered). The packaging is in the tree: the 18 PAL images, the
> source tarball and `LICENSE.txt`, 19/19 verified against `roms/emutos-1.4-manifest.json`. The run's
> language argument defaults to **`uk`** — the fallback image ADR 0010 names, and the one §3's
> `_deskstart 0x00e2b394` quotes. G6's addresses are **per image** (`_deskstart` is `0x00e2b4b4` in
> `fr`, `0x00e2b408` in `de`), so the harness reads them from the booted image's own `.sym`;
> `_nvbls` (`$454`) and `_gl_apid` are common to all 18.

Two build configurations, deliberately:

| Build | Used by | Why |
|---|---|---|
| **Debug** | G1–G7 | the assertions and the digest exist only here (ADR 0008 §6: "debug builds and the test harness only — never release") |
| **Release** | G8 only | a frame time measured with per-instant assertions on is a measurement of the assertions |

Frames are **never compared**. Digest equality implies equal records, equal values and therefore equal
pixels, because the output stage is a pure function of committed state; a frame hash would add a
second way to fail and no coverage. The only pixel-level evidence is the owner's eyes, once (M1).

## 3. The gate

`scripts/validate.sh` runs these in order and stops reporting a pass if any fails.

| Row | Assertion | Instrument | Pass condition | Failure it catches | Status |
|---|---|---|---|---|---|
| **G1** | Same image, twice, byte-identical digest stream at T=1 | Two headless runs, same image, same `N`; compare `run.digest` files | Files identical, byte for byte | The model read host state: a pool, a dictionary's iteration order, allocation timing, `DateTime.Now`, a race the run happened to win | automated |
| **G2** | The same stream with host scheduling perturbed | Third run with `--inject`: yields, GC pressure, allocation noise, frame pacing — **only** what ADR 0008 §6 allows | Identical to G1's stream | The leak G1 cannot see: a host dependency that is *stable* within one process and differs across pacing. Nothing may touch an instant, a record's contents or `seq` | automated |
| **G3** | The seven scheduler assertions silent, and #9's two CPU assertions silent | Debug build, whole run; the harness fails the row on the first assertion | No assertion fires from boot to desktop | A chip suspended with nothing queued (lost obligation); a `nextDeadline` not queued; a backdated instant; a subscription nobody catalogued; a dead class-1 row; a loop with no suspension in it; a CPU access list disagreeing with its M68000UM chapter-7 row; a nested-entry budget overrun | automated |
| **G4** | The clock arithmetic is ADR 0001's | Pure arithmetic unit test; no emulation | Frame = 640 000 ticks; every clock is 32 MHz ÷ 2ⁿ; the four dividers give exactly 1 000 / 500 / 250 / 125 samples per frame; no period is a floating-point value (the type is integer ticks) | A float period sneaking into the sound clock; an accumulator that does not carry; a frame length drifting from 640 000; a mixed-up divider | automated |
| **G5** | Per-chip rates match the catalogue, per frame | The harness differences the chips' counters over the four frames after the desktop is up and divides by four (`boot`'s own rate window; `run.report` carries the row) | Video group deliveries = **4 000**/frame; `DE` records = **400**/frame (200 asserting the window open plus 200 releasing, two subscribers); Timer B counts = **200**/frame; video chunk writes = **4 000**/frame; HBL = **625 per two frames** (313 lines, the wrap line straddling the frame boundary); total class-1 ≤ the catalogue's ceiling for the workload | A subscription that never fires (a liveness bug the digest cannot see — two runs can both be silent); a beam or a timer at the wrong rate; a delivery amalgamated into a tick table | automated |
| **G6** | The machine's own clocks, read from the OS | The harness reads EmuTOS's system variables over the settle window after the desktop is seen, addresses from `roms/emutos-1.4/etos256<lang>.sym` | `_frclock` ($466) advanced by `N` ± 1 over `N` frames on the desktop (the 200 Hz counter `_vbclock` beside it); `_gl_apid` (0x0000e32a, BSS) non-zero, so the AES handed an application its id; the PC inside the desktop's text (`_deskstart` 0x00e2b394 → the desk object files) on at least one sampled frame — the AES event loop waits inside the BIOS's `evnt_multi`, so the PC is *not* routinely there. `_nvbls` ($454) is the vbl queue's **slot count** (`ARRAY_SIZE(vbl_list)` = 8), reported and never a frame counter | A VBL that is dropped or doubled — which both runs drop *identically*, so the digest passes; a VBL interrupt that never reaches the CPU; a "desktop" that is a static screen rather than a running AES event loop | automated |
| **G7** | Retention never truncates under a live reader | Targeted test: a sampler reads the `IPL` wire 100 ticks back while a writer keeps appending; debug assertion in the log that refuses any discard below `min(subscription.from, live sampling window)` | The sampler's answer equals the value of the last record at or before its instant; no assertion fires | A log truncated under a live sampler — a wrong *value* with an unchanged *record stream*, which is the one failure a digest test structurally cannot catch (ADR 0004, Revision 2026-11-07) | automated |
| **G8** | The frame fits its budget | Release build, per-frame wall time over the run, at #18's measured crossing density | Mean ≤ **5 ms of 20 ms** (25 %), reported with `n`, the CPU and the crossing count used | A loop quadratic in crossings; a per-frame allocation storm; the case where the memory leaf starts to be earned | automated (not a determinism test) |

G5's figures come from the catalogue's own arithmetic — 80 words per active line × 200 lines ÷ 4 words
per group = 4 000; `DE` is one record per active line with two subscribers; Timer B counts `DE`. The
row is a *structural* count at Level 1 (the beam generates them whatever the CPU is doing), so equality
is the right shape. No absolute total is gated (§4).

**Sound is not in G5** until the sound path exists; its refill row (≤ 313/frame, ≥ 31 at the lowest
rate) is deferred with the test in §6 D2.

## 4. Reported, never gated

These go into `run.report` because a number the owner will ask for should be on the page, but a
derived figure must never turn a run red:

- **Absolute class-1 wakes per frame**, against the catalogue's derived ≈ 4 730 idle — derived, and
  #18 owns the measured figure. A gate on it would fail forever for the wrong reason.
- **Fast-path share of reads**, suspensions per frame, awaited writes per frame — #8's list of what the
  skeleton must produce, and the numbers that would reopen the pool.
- **Allocation per frame and GC pauses** (the prototype's ~64 B per suspension, zero on the fast path).
- **`_frclock`'s advance** (the OS's 200 Hz counter) beside `_nvbls`, as corroboration of G6.
- **Digest stream length** and the first appearing-difference instant when G1 or G2 fails, with the
  full record stream for the last N batches (the one-line diff ADR 0008 promises).

## 5. Manual

| Row | Check | Pass condition | Failure it catches | Status |
|---|---|---|---|---|
| **M1** | The owner boots the emulator in the UI and looks at it | The GEM desktop is on screen, drawn by the Shifter model through the host display stage | An output path that commits the right state and paints nothing — the one thing a headless probe cannot see | manual, once per session that changes the display path; never automated |

> **Ruling 2026-11-11** (owner, before #10's session): **M1 is deferred to [#12](https://github.com/laurentprudhon/the-st/issues/12)**, the host
> layer. The walking skeleton passes its gate headless — G1–G8 need no window — and the display path M1
> would check (the blit, the canvas, the host's read of Video's committed screen) is #12's to build, not
> the skeleton's. So #10 owns the machine's side of the display (Video committing a screen, frame
> delivery as records) and #12 owns the pixels reaching a window, with M1 as its own manual row. Until
> then the gate has **no pixel evidence at all**, which is exactly what §1 warns a green run does not
> prove.

## 6. Deferred, with the owner that reopens it

| Row | What it would prove | Why it is not in the gate | Reopens with |
|---|---|---|---|
| **D1** | T=1 vs T>1 produce identical digest streams | No T>1 exists: #8 ruled T=1 ships and no worker pool is on the roadmap, and #10 was told not to write one. The contract stands as written (ADR 0008 §6); it is untested, not false | The pool. The harness already **compares two stream files**, never two in-process runs, so the day a pool exists the clause is one command: `validate.sh --a --b`. No ticket owns it because writing the pool is what reopens it |
| **D2** | Sound DMA's address counter advances by exactly N × 1 000 bytes over N frames at the top rate, with no drift (ADR 0001's runtime half; the constants are G4's) | The counter is a `U300` register, the FIFO and the sample clock are Sound's, and neither subsystem has a design ticket yet | New ticket **design the sound path** (DMA address/counter, 4-word FIFO, PSG tone/noise/envelope, LMC1992), with **#20** for which slot the refill occupies. The test goes in that ticket's acceptance, not #10's |
| **D3** | The validation ladder's rungs: a TOS 1.62 run, compatibility suites, palette-mid-frame demos, MFP Timer A music, copy-protected images | These are post-destination (overview §10.4): the map's destination is a decision-complete design plus a skeleton that boots, and promising a compatibility claim inside the pass criterion is exactly what ADR 0010 forbids | The effort after the skeleton. No ticket: charting it now grows a map whose destination is a boot |
| **D4** | Absolute agreement of the timeline with real hardware | There is no oracle in this container — Hatari, Steem SSE and NeoST are source snapshots with no built binary, and a trace-diff needs a build, a trace adapter (ours has virtual-chip records, Hatari has none), a program set, and a story for the divergences ADR 0001 and Level 1 accept by design | Nothing in this map. #18 is the only Hatari contact (one number: the measured crossing rate). A fidelity question that arrives later is a new research ticket with those four costs |

## 7. What this plan does not prove

Stated so no later session mistakes the green run for more than it is:

- **Absolute timeline fidelity.** Everything here is self-consistency plus the manual's own arithmetic.
  A model that is wrong in the same way twice passes G1–G3, and a wrong-per-frame rate that the
  catalogue also mis-derives passes G5. EmuTOS reaching its desktop (G6) is the strongest external
  check available offline, and it is a *functional* check: it proves the machine is close enough to run
  an OS, not that its instants are the hardware's.
- **Audio accuracy**, in either direction (the −0.133 % divergence is accepted by ADR 0001, not
  measured by anything here).
- **Contention.** Level 1 models none; nothing in this plan can fail for a contention reason.
- **Compatibility.** The ladder is D3. "Boots to the desktop" is the acceptance test (ADR 0010), never a
  compatibility claim.

> **Correction — 2026-11-09** (ADR 0011, owner's ruling on #20). Contention **is** modelled: the machine
> is bus-aware throughout, with no Level-1 step. The "Contention" line above is kept for the audit trail.
> A run *can* now fail for a contention reason (a granted bus holding the CPU's external slots, a sound
> refill or refresh deferred out of a display window), and G5's structural counts gain a check that the
> per-line slot budget is not over-claimed. G1–G4 and G6–G8 are unchanged; **D4** remains the only row
> that would catch a wrong contention model, and no gate can catch it offline.

## 8. Traceability

| Row | Traces to |
|---|---|
| G1, G2 | ADR 0008 §6 (contract, injection allow-list); #7's second comment |
| G3 | ADR 0008 §5 and `design/scheduler.md` §4; #9's two CPU assertions (ADR 0009) |
| G4 | ADR 0001 (640 000 ticks, 32 MHz ÷ 2ⁿ, 1 000/500/250/125); #3's first comment |
| G5 · G7 | `design/crossings.md` §2–§10 (rates, two `DE` subscribers, 4 000 groups); ADR 0004 Revision 2026-11-07 |
| G6 | ADR 0010 (boots to GEM), `roms/emutos-1.4/*.sym` / `*.src` for the addresses |
| G8 | #8 (the frame budget is not the risk; 25 % is the number that reopens the pool) |
| D1 | ADR 0008 §6 second clause; #8's topology ruling |
| D2 | #3's first comment (the N-frame counter test); ADR 0001 |
| D3 | overview §10.4 (validation ladder); ADR 0010 (acceptance ≠ compatibility) |
| D4 | ticket #11's candidate list; ADR 0001's observation list |
