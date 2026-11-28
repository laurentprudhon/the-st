# Handoff — the documentation phase: one authoritative reference, the machine and the emulator

**Goal of the phase.** Replace a decade-shaped pile of documents with **one reader's book and one archive**. A new
`book/` directory becomes the single entry point and the only authoritative statement of what this machine is
and what this emulator does; `research/` becomes the archive of evidence everything in it is derived from, including
the older documents that were written before there was a book to fold them into.

The book is six parts and they are deliberately different in kind. **Parts 1 to 3 are the real machine** — a 1040STE
as a product a person uses, then as a board, then as a set of parts, then as a set of timelines. **Part 4 is our
emulator of it** — how time is simulated, how each part is modelled, what is *not* modelled, what nobody knows about
the hardware, and where the code is. **Part 5 is the 68000**, presented as a programming manual we can write better
than Motorola's, because we have the microcode listing and the corpus. **Part 6 is TOS as code** — every service a
program can call, its implementation in EmuTOS and in real TOS, and the table that ties each service back to the
hardware of Parts 2–3 and forward to the model of Part 4.

**Why now.** The per-chip campaign and the closing audit (`docs/handoffs/silicon-closing-audit.md`) have just
finished: every chip has a study, every open question has a labelled claim or a register row, and the register is
complete at 68 rows. That is the moment a book can be written from evidence rather than from memory — and the moment
before the evidence starts to rot in six directories with three vocabularies.

**The rule that decides every argument in this phase:** *a chapter of the book may not say anything its sources do
not say, and it must name them.* Every paragraph carries its provenance, every label travels with the claim
(`documented`, `derived`, `inferred`, `unknown`), and every chapter is read a second time against its sources before
it is called done. §5 is the mechanism; it is the most important section of this brief.

---

## 0. The standing rule — **the owner validates a detailed plan before anything is written**

**No chapter, no section and no document of this phase is written before the owner has validated its plan.** The
plan is *detailed*: the chapter's sections in order, what each section will claim, the sources it will name, the
figures it will draw. It is posted as a comment on the ticket that owns the chapter, and the owner answers there —
approval, corrections, or a different shape. A chapter written on an unvalidated plan is a chapter that gets
rewritten, and the phase has forty-three chapters: this is the cheapest rule in it.

The same rule governs everything else in the repository that is expensive to undo: a study's outline, a move of
files, a register of rows, a design document. **Ask first, in writing, with the plan attached.** Silence is not
approval, and a plan that is "obviously right" is still posted — the owner's corrections are the point, and they
have been right about the chapter order, the vocabulary and the sources more than once.

---

## 1. The shape of the repository after the phase

| directory | role | what changes |
|---|---|---|
| **`book/`** | **The book**, at the repository root: `book/README.md` is the single entry point, `book/part-1/` … `book/part-6/` the chapters, `book/appendix/` the appendices. The only authoritative statement of the machine and the model. | created here |
| `docs/` | the project's **records**, which it already held: `docs/adr/` (decisions), `docs/handoffs/` (session briefs), `docs/agents/` (tracker conventions). Unchanged in place. | unchanged |
| `research/` | The **archive of evidence**: the studies, the vendored sources, the recovered designs, the magazines, and — new — `research/archive/` holding the older documents that were never studies. | gains `archive/`, and a `README.md` that indexes what is where |
| `design/` | The **implementation record**: ADRs, the virtual-chip partition, the scheduler, the subsystem specs. Not rewritten; cited by the book's second half as a source. | unchanged in place; some files gain a pointer line |
| `atari-st-hardware/`, `atari-st-software/`, `atari-st-emulation/` | **moved** into `research/archive/` — see §7. | moved, with redirects |

**Why `design/` does not move.** It is not a document *about the machine*; it is the record of how we decided to build
the model (ADRs 0001–0015, the partition, the scheduler's rules, the register of limitations). The book's Part 4
distils it for a reader and cites it per claim; a reader who wants the decisions rather than the description follows
the citations. `design/emulator-limitations.md` stays there too, and the book carries a reader-facing summary of it
(Part 4.7) that points back to the rows.

**Why the older directories do move.** `atari-st-emulation/02-atari-ste-hardware-overview-for-emulation.md` (1 198
lines), `atari-st-hardware/components/*` and `atari-st-software/*` (13 files) are epoch-1 documents: plausible, useful
in their time, and now partly wrong in the ways the campaign has spent two months correcting. They are *evidence of
what was believed*, and they are also where a lot of good prose lives that the book will reuse (chapter by chapter,
claim by claim, re-sourced). Keeping them at the root makes them look authoritative. They belong in
`research/archive/`, with a header line saying what they were and which book chapter superseded them.

---

## 2. The reader, and how the book discloses itself

The book is written for four readers who arrive at the same page with different questions. `book/README.md` is
the single entry point and it says, in one screen, what the book is and which path to take:

| reader | arrives asking | reading path |
|---|---|---|
| **the user** | "what is this machine, what can I do with it?" | Part 1 → the desktop chapter → Part 2.2 (the chips, as a curiosity) |
| **the hardware reader** | "how is it built, what talks to what, how fast?" | Part 1.1 → Part 2 completely → Part 3 completely |
| **the emulator implementer** | "how is it modelled, what is missing, where is the code?" | Part 2.3 (buses) → Part 3 → Part 4 completely |
| **the 68000 programmer** | "what does this instruction do, how long does it take, what does the bus do?" | Part 5, then Part 3.3 for the bus it sits on |
| **the TOS programmer** | "how do I call the system, and what does it do to the hardware?" | Part 6, then the Part 2/3 chapter each service's bridge row points at |

**Progressive disclosure is a mechanism, not a tone.** Every chapter file has the same four levels:

1. **In one page** — a boxed 3–6 line answer at the top: what this chapter covers and the one thing to remember. A
   reader who stops here is not misled.
2. **Numbered sections** — the body, which may be read top to bottom in one sitting (a chapter is 150–600 lines).
3. **Deep dives** — blocks marked `> **Deep dive — <topic>.**` that a casual reader skips and an implementer does
   not: pin tables, micro-state listings, the arithmetic of a timing, the exact register bits.
4. **Where this comes from** — the chapter's source list, plus the audit table of §5.4 when the chapter is signed off.

Cross-links point *down* the book (a Part 1 chapter never links forward into Part 4's code; a Part 4 chapter always
links back to the machine's chapter it models) and every term in `GLOSSARY.md` is used exactly as the glossary defines
it. A chapter that needs a term the glossary lacks adds it to the glossary in the same commit.

---

## 3. The book: chapter map

`book/` holds **43 chapters in six parts** (4 + 9 + 5 + 8 + 7 + 10) and four appendices. The layout is
`book/part-1/…` … `book/part-6/…` and `book/appendix/…`, with `book/README.md` as the entry point; `docs/` keeps the
records (`adr/`, `handoffs/`, `agents/`) and is not part of the book. Each row below is a file, its job, and its
principal sources today — the evidence exists for every one of them or the row says what is missing.

### Part 1 — the machine as a product (4 chapters)

| file | answers | principal sources |
|---|---|---|
| `1.1-the-1040ste.md` | what the 1040STE is, the family it belongs to, what shipped in the box, its specifications | `atari-st-emulation/02` §1, the service manual's parts list, `design/board-inventory.md` §1–§2, the STE-era articles in `research/st-magazines.md` |
| `1.2-ports-and-controls.md` | every connector on the machine, what plugs into it, what the machine does with it, and what it cannot do | `design/board-inventory.md` §2.4, the component pages, `research/ste-analog-pads.md` |
| `1.3-switching-on.md` | power-on, the boot sequence as a user sees it, the GEM desktop, disks, what a working machine looks like | `research/tos-rom-sourcing.md`, `atari-st-software/boot-os/01`, EmuTOS's `kbd_init`/boot path, the ADR 0010 on the shipped TOS |
| `1.4-media-and-formats.md` | disks, images and what a user can load — the *user's* view of the media study | `research/floppy-media-and-images.md` (the whole study, distilled to what a reader needs) |

### Part 2 — the machine as hardware (9 chapters)

| file | answers | principal sources |
|---|---|---|
| `2.1-board-generations.md` | the CA4003290 board, the with/without-blitter populations, and the ST-versus-STE differences that change behaviour | `design/board-inventory.md` §1, §6, `design/st-ste-equivalence.md`, `research/ste-super-hires.md` |
| `2.2-the-chips.md` | the inventory: every part, its designator, its role, its package, and the study or datasheet behind it | `design/board-inventory.md` §2–§3, `design/partition-audit.md` |
| `2.3-buses-and-signals.md` | the CPU bus, its control signals, `DTACK`/`BERR`, the grant chain, and which chip drives what | `design/board-inventory.md` §4, `research/bus-arbitration.md`, `design/crossings.md` |
| `2.4-memory-map.md` | every window in the map, what decodes it, and the aliases and mirrors a program can see | ADRs 0005/0011, `design/system-control-unit.md`, `atari-st-emulation/02` §3, Hatari's dispatch tables as a witness |
| `2.5-subsystem-video.md` | the video path: U400's counters and fetch, U401's shift and palette, DE/BLANK, mono/colour | `research/gstmcu-silicon-implementation.md` §5, `research/gst-shifter-implementation.md` §4, `design/video.md` |
| `2.6-subsystem-sound.md` | the PSG, the Microwire volume chip, the sound-DMA path, and where the sample clock lives | `research/standard-parts-implementation.md` §6, `design/crossings.md` §2, `design/video.md` §6 |
| `2.7-subsystem-disk.md` | the FDC, the DMA controller, the drives and the ACSI port, from sector to byte clock | `research/standard-parts-implementation.md` §4, `research/dma-controller-implementation.md`, `design/disk-path.md` |
| `2.8-subsystem-controllers.md` | the IKBD link, the mouse and joystick ports, the ACIAs, the MFP's port lines, the paddles | `research/ikbd-implementation.md`, `research/standard-parts-implementation.md` §3, §5, `research/ste-analog-pads.md` |
| `2.9-subsystem-interrupts.md` | the interrupt chain: MFP sources, `GPIP`, the `IPL` encoder, `IACK`, and what software sees | `design/timers-and-events.md`, ADR 0013, `research/standard-parts-implementation.md` §3 |

### Part 3 — time on the real machine (5 chapters)

| file | answers | principal sources |
|---|---|---|
| `3.1-clocks.md` | the two oscillators, every clock net, and the exact ratios between them | ADR 0001, `design/board-inventory.md` §2.3, `design/virtual-chips.md` (the clock outputs), the PSG/FDC clock corrections |
| `3.2-video-timing.md` | lines, frames, slots, `DE`/`BLANK`, the fetch cadence and the mono/colour line counts, with the arithmetic | `research/gstmcu-silicon-implementation.md` §5, `research/memory-bus-slots.md`, `design/video.md` |
| `3.3-bus-slots-and-arbitration.md` | the slot schedule, who owns the bus when, the 4-clock grant, HOG/non-HOG, and the blitter's window | `research/bus-arbitration.md`, `research/blitter-implementation.md` §4.2, ADR 0009 |
| `3.4-refresh-and-dma.md` | refresh as filler, the sound and video fetch streams, and the disk burst's 16-byte rhythm | `research/gstmcu-silicon-implementation.md` §4–§5, `research/dma-controller-implementation.md` §4 |
| `3.5-one-frame-end-to-end.md` | a whole frame walked slot by slot: what is fetched, what interrupts fire, what the CPU gets | the four studies above plus `research/pin-event-rate.md` |

### Part 4 — the emulator (8 chapters)

| file | answers | principal sources |
|---|---|---|
| `4.1-architecture.md` | the eight virtual chips, the boundary rules, the stores, and what the machine is made of *in the model* | `design/virtual-chips.md`, `design/subsystems-spec.md`, ADRs 0003/0005/0011/0014 |
| `4.2-time-and-ordering.md` | machine time, the five verbs, the scheduler's rules, same-instant ordering, and why it is deterministic | `design/scheduler.md`, ADRs 0001/0006/0007/0008/0012, `research/deterministic-threaded-simulation.md` |
| `4.3-how-a-chip-is-modelled.md` | the general recipe: pins as records, crossings, subscriptions, deadlines, and what a chip *is* in C# | `design/walking-skeleton.md`, `design/crossings.md`, `design/scheduler.md` |
| `4.4-what-is-not-modelled.md` | the declared scope: MIDI, serial, printing, cartridge, hard disk — and the stubs' contracts | the map #1 scope, ADR 0010, `src/TheST.Machine/Chips/Stubs.cs` |
| `4.5-the-customs-modelled.md` | GSTMCU, Shifter, Blitter and DMA as the model has them: what is exact, what is approximated, what is absent | the four studies' "what the model needs" tables, `design/*.md`, `src/TheST.Machine/Chips/` |
| `4.6-the-peripherals-modelled.md` | MFP, FDC, the two ACIAs, the PSG, the IKBD and the paddles, same three questions each | `research/standard-parts-implementation.md` §8, `research/ikbd-implementation.md` §8, the `Controllers` stub's contract |
| `4.7-what-we-do-not-know.md` | the register, for a reader: every open question, what it changes, and what would settle it | `design/emulator-limitations.md` (the 68 rows, 24 open) |
| `4.8-the-code.md` | the solution's projects, the file map, the instruments, the harness, and how to find the code for a behaviour | `src/`, `tools/`, `scripts/validate.sh`, `design/walking-skeleton.md` §1, `design/validation.md` |

### Part 5 — the 68000 (7 chapters)

| file | answers | principal sources |
|---|---|---|
| `5.1-the-programming-model.md` | registers, data types, addressing modes, supervisor/user, the reset state — as a programmer meets them | `research/m68000-silicon-implementation.md` §5.2, the M68000 user's manual (vendor), `design/cpu-core.md` |
| `5.2-the-instruction-set.md` | every instruction and variant: syntax, operation, flags, and **our** clock count per variant | the corpus (`tests/680x0`, MAME's 317 500 records), `research/m68000-silicon-implementation.md` §6–§10, the C# timing ledger |
| `5.3-instruction-scripts.md` | per variant, the micro-state walk the die performs — the thing no manual prints | `scripts/microcode-walk.py` over `research/vendor/m68000/m68000.lst`, the C# scripts' per-state comments, `design/cpu-scripts.md` |
| `5.4-exceptions-and-interrupts.md` | the vector table, the frame formats and their order, the interrupt sampling rule, the timings | `research/m68000-silicon-implementation.md` §5.3, §11, `design/cpu-silicon.md` §5, the corpus's exception records |
| `5.5-bus-cycles-and-signals.md` | one read, one write, one read-modify-write, one interrupt acknowledge: the signal sequence with clocks | `research/m68000-silicon-implementation.md` §6.1, ADR 0009, `design/cpu-silicon.md` §5 |
| `5.6-timing-diagrams.md` | the same four cycles as **diagrams**, and the instruction-timing chart by family | drawn from §5.5 (see §6.3 on drawings) |
| `5.7-cycle-counting.md` | how to count a routine's clocks: the tables, the prefetch's effect, the corpus's own conventions | §5.2, `design/cpu-silicon-todo.md`, `tests/680x0/CONFORMANCE.md`, `FINDINGS.md` |

### Part 6 — the operating system: TOS as code (10 chapters)

The part the other five cannot replace: **what a programmer can call, and how it is implemented.** It is written for
somebody who wants to *use* the machine's software rather than its hardware — and its last chapter is the bridge back
to the hardware, because every TOS service ends on a register or a pin that Parts 2 and 3 describe.

**The evidence, stated once here because every chapter inherits it — and it is better than we planned for.**
There are **two readable source trees**, and both are citable:

| | what it is | how a claim names it |
|---|---|---|
| **real TOS 1.62** | `th-otto/tos1x` — Atari's own sources, with a build that reproduces the shipped ROM **byte for byte**: the 262 144-byte `tos162uk.img` it builds has the same SHA-256 as the retail dump (`research/tos-rom-sourcing.md` §2), which is what makes the source authoritative for the image a user actually runs. Pinned at commit `7c6680d`. | `bios/ikbdclk.S:412`, followed by the symbol map's address (`glue/tos162uk.map`) so a reader can find it in a debugger |
| **EmuTOS 1.4** | in our tree as source (GPLv2), the free replacement the project ships | `src/…:line` |

The module map, because it is how Part 6 navigates: **`bios/`** is BIOS and XBIOS (`biosext.S` for the trap-14 calls,
`ikbdclk.S` for the IKBD and the clock, `floppy.S` and `blkdev.c` for the disk, `sounds.c`, `prt*.c`, `memtest.S`,
`startup.S`); **`bdos/`** is GEMDOS (`bdosmain.c` for the trap dispatcher, `fs*.c` for the filesystem, `osmem.c`,
`proc.c`, `time.c`); **`vdi/`** is the screen (`opnwk.c` for the workstation, `line1010.S` for the line-drawing A-line,
and the blit families — `sbblit.S`, `hbbitblt.S`, `sbtxtblt.S`, `blaster.S` — which is where the "does TOS use the
blitter" question is actually answered); **`aes/`** is the desktop API (`gemevlib.c` for the event loop, `geminput.c`,
`gemdisp.c`); **`common/`**, **`desk/`** and **`system/`** hold the shared definitions, the desktop and the post-release
patches.

So each function gets three columns: *what the interface promises* (documented), *what TOS 1.62 does* (a `file:line` in
`tos1x`), *what EmuTOS does* (`src/…:line`). Where the two implementations diverge, that divergence is itself a claim
worth a paragraph — it tells a reader which behaviour is the contract and which is one implementation's accident.
**Licence discipline**: `tos1x` carries no licence and is Atari-derived, so the book **cites** it by path and pinned
commit and **never vendors it**; the ROM stays user-supplied and the project ships EmuTOS (ADR 0010). An `inferred`
label is now needed only where neither tree says anything — not as a default.

| file | answers | principal sources |
|---|---|---|
| `6.1-tos-as-a-product.md` | what TOS is: the ROMs, the five layers (BIOS, XBIOS, GEMDOS, VDI, AES), the traps, the cookie jar, the system variables, and which version this book describes | `research/tos-rom-sourcing.md`, ADR 0010, `atari-st-software/rom-system/01`, EmuTOS's `doc/` |
| `6.2-the-trap-interface.md` | how a program calls the system: trap 13/14/1/2, the parameter conventions per layer, the error returns, and the dispatcher | `atari-st-software/rom-system/02`, the M68000 user's manual (trap/exception model), EmuTOS's trap tables |
| `6.3-the-boot-in-code.md` | the ROM's start to the desktop, in code: hardware init, memory detection, GEMDOS init, the AES/desktop start, the AUTO folder | `research/tos-rom-sourcing.md`, EmuTOS `bios/`, `atari-st-software/boot-os/01`, `design/disk-path.md` (the boot sector) |
| `6.4-bios.md` | every BIOS function, one row each: what it promises, what EmuTOS does, what TOS 1.62 does, the hardware it touches | `atari-st-software/apis/01`, `atari-st-software/rom-system/02`, EmuTOS `bios/`, the trap table |
| `6.5-xbios.md` | the same for XBIOS — screen base and palette, `Initmous`, `Vsetmode`, `Kbdvbase`, `Mfpint`, `Giaccess`, `Dosound`, the floppy calls | `atari-st-software/apis/01`, `atari-st-software/rom-system/02`, EmuTOS `bios/`, Parts 2.5–2.8 of this book |
| `6.6-gemdos.md` | the file, memory and process services, the descriptor table, the BIOS device layer, and the disk path to the FDC | `atari-st-software/boot-os/*`, EmuTOS `gemdos/`, `design/disk-path.md`, Part 2.7 |
| `6.7-vdi.md` | the workstation, the screen as a bitmap, and the drawing primitives — which are TOS's own code and which reach the Shifter or the blitter | `atari-st-software/games-demos/04` (the VDI reference), EmuTOS `vdi/`, Parts 2.5 and 3.2 |
| `6.8-aes-and-the-desktop.md` | the AES calls, the resource files, and the event loop (mouse, keyboard, timed events) that the desktop runs | `atari-st-software/rom-system/02`, EmuTOS `aes/`, `research/ikbd-implementation.md` §5 |
| `6.9-interrupts-and-timers.md` | `VBLQueue`, the 200 Hz clock, the MFP's role, and the path a keypress takes from the IKBD to the desktop | `atari-st-software/boot-os/03`, `design/timers-and-events.md`, `research/ikbd-implementation.md` |
| `6.10-where-tos-meets-the-hardware.md` | **the bridge**: one table, service → register or pin → the Part 2/3 chapter that explains it → the Part 4 section that models it | all of the above, plus Parts 2, 3 and 4 |

**The 6.10 table is this part's reason for existing** and its acceptance criterion: a reader who has read Parts 2–5
can be told, for any TOS service, exactly which piece of hardware it drives and which chapter describes that
hardware — and an implementer can be told which part of the model it exercises.

### Appendices (4 files)

| file | holds |
|---|---|
| `A-pin-tables.md` | every part's pin table, generated from `design/board-inventory.md` and kept identical to it by the checker |
| `B-the-unknowns.md` | the reader-facing copy of the register's open rows with their experiments |
| `C-glossary.md` | the glossary, extended with the book's own terms; `GLOSSARY.md` at the root becomes a stub pointing here |
| `D-sources.md` | the campaign ledger rendered (103 ids) plus the repo-path sources the book cites |

---

## 4. The order of work, and why it is that order

1. **Scaffolding first (#105).** The directory, the chapter template, the checker, the manifest. A chapter written
   before the checker exists gets rewritten.
2. **Parts 1 and 2 next (#106, #107, #108, #109).** The machine, from the outside in: product, board, chips, buses, then each
   subsystem, then each part's internals. Everything later cites these chapters instead of re-deriving a pin.
3. **Part 3 (#110).** Time, once the parts are described: clocks, video timing, slots, refresh, one frame.
4. **Part 4 (#111, #112, #113).** The emulator, in three passes: architecture/time, then the per-part modelling, then the
   unknowns and the code. It can be written only after Parts 2 and 3 exist, because every section is "the model of
   *that* chapter".
5. **Part 5 (#114, #115).** The 68000 can start in parallel with Part 1 — it shares no source with the board — but #115's
   diagrams reuse §5.5, so it comes after #114.
5a. **Part 6 (#118, #119).** TOS needs the hardware described (Part 3) and the 68000's trap model (Part 5.4), and its
   last chapter is the bridge back into Parts 2–4: it is the second-to-last thing written, and the one that proves the
   book hangs together.
6. **Appendices and the archive move (#116) last**, except the glossary, which grows with every ticket. The
   `atari-st-software/` documents move into the archive *after* Part 6 has absorbed them, not before.
7. **The verification sweep (#117) closes the phase**: the second pass over every chapter, the redirect sweep over the
   old documents, and the phase's own audit.

---

## 5. The source policy — **every paragraph names its sources, and every chapter is checked twice**

This is the requirement the phase exists to satisfy, so it is mechanical rather than a matter of care.

### 5.1 The provenance line

Every paragraph of prose, and every table, ends with a visible provenance line:

```markdown
*Sources: `research/gstmcu-silicon-implementation.md` §5.2; STe schematic sheet 4 (crop `gstmcu-silicon/die/sheet4-line.png`); ledger [13].*
```

Rules for it:

- **A repo document is named with its path and section** — not "the GSTMCU study".
- **An external source carries its ledger id** — the same one every study uses.
- **A drawing is named with its sheet and, where a crop was read, the crop's path.**
- **A code fact is named with `path:line`** — and in Part 6 a TOS claim names *which* code: EmuTOS by
  `src/…:line`, real TOS by the function and the disassembly address, or `inferred from behaviour` when neither
  is readable. A paragraph that presents one as the other is the part's one fatal error.
- **The claim's label travels**: a paragraph whose source is `inferred` in the study says so ("`inferred` from the
  RTL's comment"), and an `unknown` says that too, with the register row (`B9`).
- **A paragraph with no source is not allowed.** If the only source is the book's own arithmetic, the line says
  `*Sources: arithmetic over §2.3's pin table (no external source).*` — that is a source for this purpose, and it
  tells the reader exactly how much to trust the paragraph.

### 5.2 The checker

`scripts/check-book-sources.py` runs over `book/` and fails on:

1. **an unsourced paragraph** — any block of prose between blank lines that does not end with a `*Sources:` line;
2. **a broken reference** — a `` `research/...` `` or `` `design/...` `` path that does not exist, or a `` § `` that the
   file does not have;
3. **an unknown id** — a `[n]` that is not in the ledger, or an id cited in a chapter but absent from that chapter's
   "Where this comes from" list (and the reverse: a listed id never cited);
4. **a label-less extrapolation** — a paragraph that uses a word from the reserved set (`measured`, `exactly`,
   `always`, `never`, `the hardware does`) while its sources are all `inferred`/`#107`. This one is a warning, not a
   failure: it flags the paragraphs a second reader should look at first;
5. **a manifest mismatch** — a chapter file with no entry in `book/manifest.json` (title, part, order, sources
   count, audit state), or an entry with no file.

It runs from `scripts/verify-book.sh` (the wrapper, next to `verify-research.sh`) and its exit code is the
chapter's gate: a chapter is not done while the checker fails on it.

### 5.3 The first pass — how a chapter is written

A chapter session works in this order, and the order is the discipline:

0. **Post the detailed plan and wait for the owner's go** (§0): the chapter's sections, the claims each will make,
   the sources each will name, the figures it will draw. The plan is the ticket's comment, not a chat message.
1. **Collect the claims** into a scratch list with the source each one will carry, *before* writing prose. The list is
   the audit table of §5.4, started empty-of-verdicts.
2. **Write the chapter** from the list, one paragraph at a time, each ending with its provenance line. A claim whose
   source cannot be found does not get written: it becomes a question for the session (and, if it is a hardware
   question, a register row — never a ticket).
3. **Run the checker** until it is green.
4. **Draw** whatever §6.3 says the chapter needs.

### 5.4 The second pass — the check that makes the first one mean something

**Written from the sources, read against the sources, by two different passes.** The second pass is a separate read
of every paragraph of the finished chapter with the named source open beside it, and it records a verdict per
paragraph in the chapter's audit table:

| # | paragraph (§) | claim, in one line | source named | verdict | note |
|---|---|---|---|---|---|
| 1 | §2.1 | the DE edge moves 8 pixels with the offset | `research/gst-shifter-implementation.md` §4.2 | **found** | — |
| 2 | §2.3 | "the fetch never idles in low res" | `research/gstmcu-silicon-implementation.md` §5.4 | **overstated** | the study says *seldom*, and names the mono counter-example — corrected |
| 3 | §3.1 | the PSG's divider is `CLK2/4` | `research/standard-parts-implementation.md` §6.1 | **wrong source** | the sentence is `derived` in §6.3 — re-pointed |

Rules:

- **Every paragraph gets a row.** A 400-line chapter has 40–80 rows; the table lives beside the chapter as
  `<chapter>.audit.md` and is committed with it.
- **`overstated`, `wrong source`, `not found` are the working verdicts.** A `not found` means the paragraph is cut or
  re-sourced before the chapter is called done — not softened.
- **The second pass is done by a different session or a delegated reader, not by the writer in the same sitting.**
  The writer's own re-read finds typos, not overstatements; the point of the second pass is that the reader has not
  already committed to the sentence.
- **The phase's last ticket (#119) samples nothing: it re-runs the protocol's own evidence** — one row per chapter at
  minimum, every chapter's `not found` count at zero, and the checker green across the whole directory.

### 5.5 Facts, decisions and opinions in one book

The book has three kinds of sentence and they must not wear each other's clothes:

- **a fact about the machine** — sourced, labelled, and (where the record is silent) marked `unknown`;
- **a decision of ours** — sourced to the ADR or the design document, and phrased as a decision ("the model does X,
  by ruling") rather than as a property of the hardware;
- **an explanation** — the book's own arithmetic or analogy, sourced as §5.1's "arithmetic over …" and never used to
  settle a question the evidence leaves open.

---

## 6. What the book owes each part, beyond prose

### 6.1 Part 5's instruction scripts — the thing no manual prints

For every instruction variant (family × size × EA class), `5.3` carries: the microcode state walk (name, address,
statements, clocks — from `scripts/microcode-walk.py`), the die's own clock total, and the corpus's measured total
with the file it came from. That is a table per family, 60–120 rows in total, and it is the book's unique asset: a
programmer can read what the chip *does*, not only what it promises.

### 6.2 Part 5's clock tables — measured, not quoted

`5.2`'s clock column comes from the corpus (MAME's 317 500 records are the gate), not from the manual's table; where
the two disagree the book prints both and says which it uses and why. `5.7` explains the counting rules (prefetch,
the +2 on a taken branch, the read-modify-write's five states) with a worked routine.

### 6.3 Drawings — timing diagrams, and only where they earn their place

- **Timing diagrams live in `5.6` and in `3.2`/`3.3`** (the video fetch cadence and the arbitration handshake).
- They are **generated by a script** (`book/tools/draw-timing.py`), from a table of signals and edges in the
  chapter itself, so a diagram cannot drift from the text that describes it, and the script prints the table it drew.
- **Every diagram declares its evidence**: measured (with the ledger id), derived (with the arithmetic), or drawn from
  a datasheet's figure (with the page).
- A diagram that would only repeat a table is not drawn. The bar is: does the reader see an *order* or an *overlap*
  that a table hides?

### 6.4 The book's own vocabulary

`GLOSSARY.md` is the machine's vocabulary, and the book must not invent synonyms. Where a chapter needs a word the
glossary lacks (e.g. "provenance line", "audit table", "reading path"), the word is added to the glossary in the same
commit, under a "the book's own terms" heading. `book/appendix/C-glossary.md` is the rendered glossary.

---

## 7. The archive: what moves, and what a moved file says

**Moves (#118, one commit, with redirects):**

| from | to | header added |
|---|---|---|
| `atari-st-emulation/01-emulation-technical-solutions.md` | `research/archive/emulation-technical-solutions.md` | "An epoch-1 survey (2026-11 era); superseded by `docs/part-4` — kept as evidence of what was believed." |
| `atari-st-emulation/02-atari-ste-hardware-overview-for-emulation.md` | `research/archive/ste-hardware-overview.md` | same, pointing at Parts 2 and 3 |
| `atari-st-emulation/sources/` | `research/archive/ste-study-transcript.md` | same |
| `atari-st-hardware/components/*.md` | `research/archive/hardware/*.md` | "Superseded by `book/part-2`; the pin tables that survive are in `design/board-inventory.md`." |
| `atari-st-software/**/*.md` | `research/archive/software/*.md` | "Superseded by `book/part-6` (the TOS API by function) and `book/part-1/1.3`–`1.4`; kept as the epoch-1 reading the TOS chapters re-sourced claim by claim." |
| `atari-st-hardware/docs/downloads/` (93 MB of PDFs) | **stays where it is**, with `research/archive/hardware/README.md` pointing at it | archive of primary documents, not prose |

**Every moved file carries the header, and every reference to its old path is updated** — `grep` for the old path
across `research/`, `design/`, `docs/`, `book/` and the code comments, and fix each hit. The redirects are part
of the same ticket, not a follow-up: a book that supersedes documents nobody can find is worse than no book.

**`research/README.md`** (new) is the archive's index: what each subdirectory holds, which study answers which
question, and which chapters of the book each study now feeds.

---

## 8. Acceptance criteria for the phase

A reader of the finished book can do all of these without opening a document outside `book/`:

1. **Use the machine** — say what a 1040STE is, plug it up, boot it, load a disk, and explain each port (Part 1).
2. **Draw the board** — name every chip, its role, its package, its clock, and the buses it sits on (Part 2).
3. **Walk one frame** — from the oscillator to the pixels, the interrupts and the bus grants, with the arithmetic
   (Part 3).
4. **Explain the emulator** — what a virtual chip is, how an instant is ordered, why the machine is deterministic,
   and which parts are stubs (Part 4).
5. **Answer "is this modelled?"** for any behaviour, with the chapter, the code file and the register row.
6. **Program the 68000** — the instruction set's variants with clocks and micro-states, the exception model, and the
   bus cycle's signals, from the book alone (Part 5).
6a. **Call TOS** — look up any BIOS, XBIOS, GEMDOS, VDI or AES function, see what it promises, how EmuTOS and TOS
   1.62 each implement it, and which register or pin it ends on (Part 6).
7. **Trust it** — every paragraph names its sources, every chapter has an audit table with no `not found` rows, and
   the checker is green.

---

## 9. What is *not* in this phase

- **No new hardware research.** The register is closed at 68 rows and its 24 open rows stay open; the book reports
  them (Part 4.7 and Appendix B) rather than chasing them. A chapter that finds a gap files a register row, per the
  owner's ruling — never a ticket.
- **No emulator changes.** The book describes the model as it is; where the model differs from the study, Part 4.5/4.6
  says so and names the ticket that owns the fix (if any) — it does not fix it.
- **No reverse-engineering of TOS beyond what the book needs.** Part 6 documents the *documented* API and reads the
  implementation where a disassembly or EmuTOS's source is legible; it does not disassemble all of TOS, rename its
  internal routines, or reconstruct its data structures beyond the system variables a programmer may touch.
- **No translation, no website, no PDF build.** One markdown book in the repo, rendered by whatever the reader uses.
- **No rewriting of `design/`.** It is cited, not edited, except for a pointer line at the top of a file the book
  supersedes as the reader's entry.

---

## 10. Definition of done for the phase

1. `book/` holds the 43 chapters and 4 appendices of §3, each with its `In one page` box, its provenance lines,
   its `Where this comes from`, and its `.audit.md` with zero `not found` rows.
2. `scripts/check-book-sources.py` and `scripts/verify-book.sh` are green over the whole directory, and the
   manifest lists every chapter with its audit state.
3. `book/README.md` is a real entry point: the four reading paths, the map of the machine in one page, and the
   chapter index with one line each.
4. The archive move is done, every moved file carries its header, and `research/README.md` indexes the archive.
5. `GLOSSARY.md` and Appendix C agree; `GLOSSARY.md` is the stub that points at the appendix.
6. The phase's audit (#119) is written into `docs/handoffs/documentation-audit.md`: one row per chapter, the checker's
   output, the `not found` counts, and the residue.

---

## 11. Frontier and ticket order

The tickets are children of the map issue [#104](https://github.com/laurentprudhon/the-st/issues/104)
(*Documentation: one authoritative reference — the machine and the emulator*), created with this brief; its body is
§1–§3 of this file. The fifteen tickets, by the numbers the tracker returned (never the ones a plan predicts). The TOS part added two
after the first charting, which is why their numbers (#118, #119) sit out of order:

| # | ticket | chapters | blocked by |
|---|---|---|---|
| [#105](https://github.com/laurentprudhon/the-st/issues/105) | the scaffolding — tree, template, manifest, source checker | — | — |
| [#106](https://github.com/laurentprudhon/the-st/issues/106) | Part 1 — the machine as a product | 1.1–1.4 | #105 |
| [#107](https://github.com/laurentprudhon/the-st/issues/107) | Part 2A — the board, the chips, the buses, the memory map | 2.1–2.4 | #105 |
| [#108](https://github.com/laurentprudhon/the-st/issues/108) | Part 2B — video and sound | 2.5–2.6 | #105, #107 |
| [#109](https://github.com/laurentprudhon/the-st/issues/109) | Part 2C — disk, controllers, interrupts | 2.7–2.9 | #105, #108 |
| [#110](https://github.com/laurentprudhon/the-st/issues/110) | Part 3 — time on the real machine | 3.1–3.5 | #105, #109 |
| [#111](https://github.com/laurentprudhon/the-st/issues/111) | Part 4A — the emulator's architecture and time | 4.1–4.3 | #105, #110 |
| [#112](https://github.com/laurentprudhon/the-st/issues/112) | Part 4B — the parts as the model has them | 4.4–4.6 | #105, #111 |
| [#113](https://github.com/laurentprudhon/the-st/issues/113) | Part 4C — what we do not know, and the code | 4.7–4.8 | #105, #112 |
| [#114](https://github.com/laurentprudhon/the-st/issues/114) | Part 5A — the 68000 as a programming manual | 5.1–5.4 | #105 |
| [#115](https://github.com/laurentprudhon/the-st/issues/115) | Part 5B — bus cycles, timing diagrams, cycle counting | 5.5–5.7 | #105, #114 |
| [#118](https://github.com/laurentprudhon/the-st/issues/118) | Part 6A — TOS: the system, the trap interface, the boot in code | 6.1–6.3 | #105, #110, #114 |
| [#119](https://github.com/laurentprudhon/the-st/issues/119) | Part 6B — TOS: the API by function, the desktop, the hardware bridge | 6.4–6.10 | #105, #118 |
| [#116](https://github.com/laurentprudhon/the-st/issues/116) | appendices, the archive move and the redirect sweep | A–D | #105, #106, #113, #115, #119 |
| [#117](https://github.com/laurentprudhon/the-st/issues/117) | the phase's verification sweep and audit | — | #105, #116 |

The blocking order is:

```
#105  scaffolding
  →  #106 Part 1                                     (parallel with 2A and 5A)
  →  #107 Part 2A → #108 Part 2B → #109 Part 2C → #110 Part 3
  →  #111 Part 4A → #112 Part 4B → #113 Part 4C       (needs #110)
  →  #114 Part 5A → #115 Part 5B
  →  #118 Part 6A → #119 Part 6B                      (needs #110 and #114)
  →  #116 appendices + archive → #117 verification    (needs #113, #115, #119)
```

#106–#109 and #114 may run in any order once #105 is done; #110 needs the parts described; #111 needs Part 3; #118 needs Part 3 and the 68000's trap model (#114); #116 needs every chapter it indexes; #117 is last and is the phase's own audit.

Each ticket's body carries its chapters, their sources, and their acceptance criteria; this brief carries the rules
every ticket obeys — §5 above all.

---

*Written 2026-11-28, the session after the closing audit, which is what makes this phase possible: the evidence is
complete enough to be a book, and the register is complete enough to be honest about what the book does not know.*
