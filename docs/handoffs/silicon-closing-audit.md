# The closing audit — every chip's open question, and where it is answered

*Written 2026-11-28, the session after the last chip. The per-chip campaign is complete: five studies (68000,
GSTMCU, GST Shifter, DMA, Blitter), one standard-parts study covering four parts, and one protocol study for the
IKBD. This is the walk the campaign's own stopping rule asks for, and it is an audit of **homes**, not of truth: it
asks whether every question our model has can be traced to a labelled claim or to an explicit `unknown` with an
experiment, not whether those claims are right.*

---

## 1. The rule, and the three directions it was walked in

The stopping rule, from `docs/handoffs/silicon-research-per-chip.md` §6:

> **Stop when every open question in our core has either a labelled claim or an explicit `unknown` with the
> experiment that would settle it.** Do not chase transistor-level detail for a part whose emulation is already
> T1-verified.

Plus the owner's ruling of 2026-11-28: a question only a board could settle goes into
`design/emulator-limitations.md`, never a ticket. The audit therefore has to show, for every part:

1. **Study → home.** Each row of each study's "what the model needs" carries a claim, a tier and a design file; each
   item of each study's "what is still unknown" is a register row, or a `read` in the board inventory.
2. **Design → study.** Every open marker in `design/*.md` is either a claim the studies settled or a register row.
   `grep -niE "unknown|unverified|TODO|TBD|open question" design/*.md` returns a list short enough to read in one
   pass — `board-inventory.md` §6 and its two "all pins unverified" symbols, the two register cross-references, and one
   line each in `video.md`, `system-control-unit.md`, `partition-audit.md`, `crossings.md` and `subsystems-spec.md` —
   and the audit read every line.
3. **The seven questions, per chip** (`silicon-research-per-chip.md` §2): images, clocking/counting, control
   structure, registers/latches, datapath, documented-vs-real behaviour, what the model needs.

The register is `design/emulator-limitations.md`. **Before this audit it held 49 rows, and eighteen of the
campaign's residual questions lived somewhere else** — sixteen in their own study's prose (three on the GSTMCU, two
on the Shifter, five on the DMA, six on the 68000, whose section was missing entirely) and three at machine level,
in `partition-audit.md`, the three studies' §8 and `board-inventory.md` §6. That is exactly the state the stopping rule
forbids: a reader of the register would have believed the residue was complete.

---

## 2. The seven questions, per chip

Every study was written against this frame, and every cell below points at a section that answers it or at the row
that says the answer does not exist.

| chip | images / decap | clocking & counting | control structure | registers & latches | datapath | documented vs real | model needs |
|---|---|---|---|---|---|---|---|
| **U100 68000** | die photographs exist (siliconpr0n, visual6502) | §6 | §7–§9: microcode + nanocode + three PLA banks | §5.2: 21 × 32, 10 × 16 | §10: two adders, one ALU, the control matrix | §11, eight conflicts adjudicated | `design/cpu-silicon-todo.md`, 12 rows |
| **U400 GSTMCU** | no decap; the recovered CAD design | §3.4, §5 | **random logic** — no ROM, PLA or RAM cell in the library (§3.2) | §5 | §3.3, §5 | §6 | §7, 10 questions with tiers |
| **U401 GST Shifter** | no decap, no drawing | §4 | random logic, by absence (§3.2) | §4: palette, scroll, mode | §4.1–§4.6 | §5 | §6, 10 questions |
| **U300 DMA** | no decap, no drawing | §4.1 | random logic, no table (§3.2) | §3: two registers, no address pins | §4.2: the FIFO, 8 words a burst | §5 | §6, 10 questions |
| **U101 Blitter** | no decap; three recovered revisions | §4.1 | 2 133 cells, no table, one adder (§3.5) | §3.2–§3.3 | §4.1: skew → HOP → LOP → merge | §5 | §6, 10 questions |
| **U203/U301/U200-U201/U202** | datasheets, not silicon | §3.1, §4.1, §5.1, §6.1 | n/a — standard parts | P1–P4 pin rows | per part | §9 | §8, 12 differences |
| **IKBD HD6301V1** | a mask ROM, no die | §7.1: `E/128` vs the ACIA's ÷64 | the firmware's state machine (§3) | the firmware's RAM map (§7.5) | the packet families (§5) | §9, nine conflicts | §8, five parts |
| **U102/U103 TOS pair** | not a study; `research/tos-rom-sourcing.md` | — | — | — | — | — | — |

`derived` **No chip is missing one of the seven.** The two absences are deliberate and documented: the TOS ROM pair
is a sourcing note rather than a study (the parts list calls it out as "a paragraph each, not a study"), and the
IKBD's "images" answer is that the ROM *is* the artefact.

---

## 3. The per-chip matrix: model questions against homes

| chip | questions in our core | answered with a labelled claim | register rows before | after | orphans found |
|---|---|---|---|---|---|
| U100 68000 | 12 (`cpu-silicon-todo.md`) | 12 (each with a blocker or a ruling) | **0 — no section at all** | 6 | **6** |
| U400 GSTMCU | 10 (§7) | 10 | 6 | 9 | **3** |
| U401 Shifter | 10 (§6) | 10 | 8 | 10 | **2** |
| U300 DMA | 10 (§6) | 10 | 7 | 12 | **5** |
| U101 Blitter | 10 (§6) | 10 | 10 | 10 | 0 |
| four standard parts | 12 (§8) | 12 | 10 | 10 | 0 |
| IKBD | 5 (§8) | 5 | 4 | 4 | 0 |
| machine level | 4 (`M1`–`M4`) | 4 | 4 | 7 | **3** |

`derived` **Nineteen rows and one whole section were missing.** The pattern is legible: the chips whose studies
were written *against* the register (Blitter, standard parts, IKBD) have no orphans, and the three whose studies
predate it (GSTMCU, Shifter, DMA) each left their residue in prose. That is not a fault in the studies — they were
written to the older brief — but it is exactly what an audit is for.

---

## 4. The orphans, and what each one became

Every row below was landed in `design/emulator-limitations.md` in this session. "Read" means the route is a drawing
and the owner's eyes, not a scope; "open" means a measurement.

| chip | question | was | now | row |
|---|---|---|---|---|
| 68000 | a complete nanocode bit map | prose in the study §12 | `ours` — FX68K's public RTL decodes all control lines, so it is a reading of a source, not a measurement | C1 |
| 68000 | the `IRD` PLA bank's truth tables | prose §12 | `read` — the die is photographed and delayered; a PLA plane at the archive's resolution | C2 |
| 68000 | the internal clock generator beyond `E` | prose §12 | `unmeasurable` — inside the clock block; `Pins.E` is undriven by ruling | C3 |
| 68000 | die-revision differences (R9M, HC000, 68010) | prose §12 | `unmeasurable` — one die per revision | C4 |
| 68000 | why the ROM rows are ordered as they are | prose §12 | `unmeasurable`, academic — the model never uses the row order | C5 |
| 68000 | the test mode, exercised | prose §12 | `open` — a tester that drives the stimulus | C6 |
| GSTMCU | the sound counter's increment instant (grant or `SLOAD`) | §7 row 2, "unresolved ordering", T3, no row | `open`, with the poll-and-correlate probe | G7 |
| GSTMCU | how many blanking slots a refresh takes | §9 prose | `open` — `RAS0` under `DE` | G8 |
| GSTMCU | `LATCH`'s payload and the ring's wrap | §9 prose | `open` — a scope on pin 131 plus one `.DWG` read | G9 |
| Shifter | which pins 35, 52, 55, 72 and 76 carry | §8 prose | `read` — a 600 dpi crop each | S9 |
| Shifter | is the `$FF8265` non-zero-to-zero bug this chip's or U400's | §8 prose | `open` — experiment 4 with `$FF8265` written 8 → 0 mid-line | S10 |
| DMA | how the direction reaches U400 | §4.4, §8 prose | `read` — the two symbols and the net column | D8 |
| DMA | `FDRQ` against `HDRQ` when both assert | §8 prose | `open` — both request lines on a scope, mode rewritten mid-sector | D9 |
| DMA | the silicon FIFO's depth and the write prefill's shape | §8 prose | `open` — `DRQ` against `CA1`, one-word transfer | D10 |
| DMA | the reserved mode bits (0, 5, 6) | §8 prose | `open` — a write-and-read | D11 |
| DMA | why `DRQ` shortens on a sector's last two bytes | §8 prose | `open` — `DRQ` across a sector boundary | D12 |
| machine | which slot each client occupies, and the STE's internal share | `partition-audit.md`'s estimate | `open` — a cycle counter with `BGACK` and `AS` on a scope | M5 |
| machine | the shipped mask of the three customs with no decap | three studies' §8, prose | `unmeasurable` — no STE custom has been decapped | M6 |
| machine | the readings still owed on the board | `board-inventory.md` §6 only | `read` — a pointer row, so the register's reader knows where they live | M7 |

And two findings that are not rows:

- **The register had no section for the campaign's first chip.** The 68000 study's residue was in its §12 and its
  model comparison in `design/cpu-silicon-todo.md`, and neither was reachable from the register. §8 is that section
  now, with the six evidence rows above and a pointer to the twelve code rows.
- **The 68000 study's citations were study-local and colliding.** Its Sources block numbered 1–31 while the shared
  ledger uses those ids for other URLs, so every citation in the reference case pointed somewhere else. Its 31
  sources are registered as ids 73–103 and the study is renumbered to them (194 inline citations, unchanged in
  count; `verify` green at 32 %). The campaign's claim that "the ledger's ids are stable across the studies" is now
  true of every study rather than of all but one.

`derived` **Two rules were written down because the audit needed them.** A `read` row (a pin name, a net, a pad's
direction) is not a measurement: its route is a crop, and the reading residue belongs to
`design/board-inventory.md` §6 — so the register now says which half of a mixed row is which, and M7 is the
pointer that makes the split visible. And the gate's two blind spots (table rows never count; a citation after a
sentence's final period is dropped) are now in the repository's own tool, `scripts/verify-research.sh`, because they
cost this campaign a session.

---

## 5. The register after the audit

| section | rows | open | read | unmeasurable | the rest (documented, `measured-elsewhere`, `ours`) |
|---|---|---|---|---|---|
| 1 Blitter | 10 | 3 | — | 1 | 6 |
| 2 GSTMCU | 9 | 6 | — | — | 3 |
| 3 Shifter | 10 | 3 | 1 | — | 6 |
| 4 DMA | 12 | 6 | 1 | — | 5 |
| 5 machine | 7 | 1 | 2 | 1 | 3 |
| 6 standard parts (incl. the medium) | 10 | 3 | — | 3 | 4 |
| 7 IKBD | 4 | 3 | — | — | 1 |
| 8 68000 | 6 | 1 | 1 | 3 | 1 |
| **total** | **68** | **26** | **5** | **8** | **29** |

`derived` **Twenty-four rows are open outright and two more are open inside a row whose main state is
`measured-elsewhere`** — B7's *coverage* and K4's general *rule*. The eight `unmeasurable` rows are the campaign's
honest floor: five are revisions or masks nobody has decapped (B10, M6 and three of the CPU's), and three are
inside a die or a clock block. Everything else has a named route.

---

## 6. What this session changed

| file | change |
|---|---|
| `design/emulator-limitations.md` | the readings/measurements rule; **19 rows** (G7–G9, S9–S10, D8–D12, M5–M7, C1–C6); the new §8 for the 68000; §10 rewritten to the complete open list |
| `research/m68000-silicon-implementation.md` | citations renumbered from its local 1–31 to the ledger's 73–103; the duplicated `## Sources` heading removed |
| `.hermes/skills/` | the project's own skills, committed with the repo: `the-st-68000-core/` (copied) and `the-st-research-studies/` (new — the study shape, the gate, the register's rules, the push protocol, this audit's procedure) |
| `scripts/verify-research.sh` | the citation gate for one study or for every study, with the ledger and the 30 % floor baked in, and the verifier's two blind spots documented in the header |
| `docs/handoffs/silicon-research-per-chip.md` | §6 records the campaign closed and points here |

---

## 7. The verdict

`derived` **The stopping rule is met, with one honest caveat about what an audit can mean.** Every question in our
core now has a home: a labelled claim with a tier and a design file, or a register row with its state and its
experiment. There is no third category left — nothing in the model rests on an unlabelled guess that this walk could
find, and the code-level differences that remain are enumerated in the design documents rather than hidden.

`derived` **What the audit did not do, and cannot:** it did not re-check the claims themselves. A row that says
`documented` and quotes a memo is only as good as the memo; the tiers are the mechanism that keeps that honest, and a
later session that finds a claim wrong corrects it in place with a date, as the campaign has done throughout. The
audit is a completeness check over the *question space*, and the question space is now written down in one file.

`derived` **What it says about the campaign's method:** the three studies whose residue leaked into prose were the
three written before the register existed, and the three written after it have no orphans. Recording a question is a
habit that has to be given a place, and the place was created mid-campaign.

---

## 8. What the disk-media thread inherits

`documented` The media study is done (`research/floppy-media-and-images.md`, 2026-11-28) and it re-priced the flux
defer rather than reopening it: 2 129 of 14 001 census entries are lost to a sector-only path, every preserved-original
title in its ten-title sample is STX-reachable, and the trigger for the bit-cell reader is a ladder title whose only
master is `.ipf`/`.scp`/`.hfe`. **This audit does not reopen that decision**, and the silicon register gives it no
reason to: the disk side's residue is the seven D-rows and the three A-rows, and none of them is a *format*
capability — they are a FIFO's depth, a request line's priority, a splice and a spindle.

`derived` What the audit does hand that thread is the split it wrote down: the loader's `provenance` flag and the
A-rows are the same distinction one level up — what the *file* says against what the *drive* did — and the
register's D12 (a two-byte `DRQ` tail at a sector boundary) is a timing detail inside a sector, not a class of
protection. The one thing the media study and this audit agree on is that the campaign's remaining board questions
are cheap and separable, and that a single scope session would settle several rows of each.

---

## 9. The shopping list, in the order that buys the most

`derived` If the owner ever has a board and a scope for one evening, this is the order the register's own rows
support — each entry names the rows one session would close.

1. **One scope on the disk path**: `DRQ`, `CA1`, `BGACK` and `RDY` across a sector, with a known image — closes
   D12, D10, D9 and confirms D4's timing.
2. **One scope on the display window**: `RAS0` and `CAS0L` while `DE` is high — closes G8 and confirms G1.
3. **Two polls**: the sound counter between refills (G7) and the DMA counter against `RDY` (D5).
4. **One keyboard cable pull and one scope on `DR1`** — K3, K1.
5. Everything else is a reading (`M7`, S9, D8) and needs the owner's eyes on a crop, not an iron.

---

*Sources: the studies and the register cited above are all in the tree; this audit adds no new external source and
therefore no new ledger id.*
