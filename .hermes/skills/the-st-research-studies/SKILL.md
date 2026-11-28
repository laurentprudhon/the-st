---
name: the-st-research-studies
description: "Use when writing a the-st study, audit or correction."
version: 1.0.0
author: Hermes Agent
license: MIT
metadata:
  hermes:
    tags: [the-st, research, studies, citations, campaign]
    related_skills: [research-silicon-chip-implementation, grounded-citations, the-st-68000-core]
---

# Studies, corrections and audits in `the-st`

## When to use

- Writing or extending a document under `research/` — a chip study, a standard-parts study, a protocol study, a media
  study.
- Correcting one of those studies, or landing its corrections in `design/`, `atari-st-hardware/` or the campaign index.
- Running a closing audit of a per-chip (or per-thread) campaign, or asking whether the work is finished.
- Fighting the citation gate: a study under 30 % coverage, an id that resolves to the wrong URL, `verify` refusing a
  Sources block.
- Committing and pushing `the-st` work while another session may be writing to the same remote.

Not for the 68000 core or the corpus (load `the-st-68000-core`) and not for the generic method of a chip study (load
`research-silicon-chip-implementation`).

The repository's research is a **campaign**, not a pile of documents: `docs/handoffs/silicon-research-per-chip.md` is
the index and the question set, `research/*.md` are the studies, and `design/emulator-limitations.md` is the register
of everything only a board could settle. A session that writes a study without those three is writing a fourth thing
nobody will find.

Load `research-silicon-chip-implementation` for the *method* (evidence ladders, claim labels, adjudicating
conflicts); this file is the project's plumbing — paths, gate, register rules, push protocol.

## Where a the-st lesson is stored

- **In the repository, with the code it describes.** `.hermes/skills/` (committed) holds this project's skills and a
  procedure learned while working on `the-st` is committed there in the same push — not left in the agent's profile,
  which does not travel with the clone. A fact about the *machine* is not a skill: if the lesson is "`GPIP4` is
  channel 6", it belongs in `design/` or `research/`; a skill carries procedure, gates and pitfalls.
- **Keep the two copies identical.** The profile's copy is what an agent loads with `skill_view`; the repository's is
  what a reader of the repo gets. Edit both in the same session — `.hermes/skills/README.md` carries the `rsync`
  recipe — and treat a divergence as a bug rather than as two versions.

## The gate

```sh
scripts/verify-research.sh research/<study>.md      # verify --min-coverage 0.30 against the shared ledger
scripts/verify-research.sh --all                   # every study in the tree
```

- **The ledger is shared and its ids are stable for the whole campaign** (`$HERMES_HOME/cache/citations/ledger.json`,
  103 sources, one id per URL across every study). Never guess the next number: `sources.py add` returns the id, and a re-added URL returns the
  **existing** id. A study that renumbers to look tidy breaks every other study's citations.
- **The verifier has two blind spots that cost a session**: it splits sentences on `[.!?]` + whitespace and skips
  lines beginning with `|`. A citation written *after* a sentence's last period falls into a dropped fragment, and
  table rows never count at all. Put the citation before the closing period or mid-sentence, and keep the sourced
  claim in prose even when it also appears in a table. A study at 25 % is usually one editing pass away from 32 % —
  re-run the gate after every pass, not once at the end.
- **One sentence per line.** The gate counts line fragments; hard-wrapped prose under-reports and tempts mechanical
  citation appending.
- **When the citations fight back, remap them with a script — never by eye.** A study numbering its own Sources
  (written before the shared ledger, or copied from an older template), an id that resolves to a different URL than
  the block says, a duplicated `## Sources` heading: all are repaired mechanically with assertions on the citation
  count. The recipe, and the traps in it, are `references/study-citation-gate.md`.

## The shape a study takes here

`## 1. Executive summary` (question | answer | §) · `## 2. The evidence, and what each layer can answer` (a table of
layers and what each cannot say) · the chip the record shows · **the chip in operation** (one cycle, one slot, one
sector, one scan, walked with arithmetic) · `## Conflicts adjudicated` (numbered, one per disagreement, with the
falsifier) · `## What the model needs` (one row per question in our core: the claim, its tier, its home file) ·
`## The experiments that would settle the rest` (each with a pass condition) · `## What is still unknown` · licence
position · `## Sources`.

- **Labels**: `documented` / `derived` / `inferred` / `unknown`; tiers T1 (hardware-captured), T2
  (hardware-corrected or vendor prose), T3 (emulator-derived), plus `measured-elsewhere` when someone published a
  hardware number and the study only cites it.
- **The model-comparison table is the deliverable that turns a study into work**: one row per claim, the code's file
  named beside it, "aligned" or "does X instead" — with the design document's section named per row when the brief
  asks for it.
- **The last chip's study is this project's protocol-study template** (`research/ikbd-implementation.md`): turn-taking
  walk first, then the command table with parameter counts, then the packets, the timing budget, and the drop rules.

## The book: `reference/`

**`reference/` is the reader's entry point and the only authoritative statement of the machine and the model.** A new
study does not become the truth by existing: it becomes a *source* the book cites. The book's chapter map, its source
policy and the phase's tickets are in `docs/handoffs/documentation-phase.md` (map #104).

- **A study keeps doing what it did** — evidence, labels, tiers, the register — and it gains a header line naming the
  chapters it feeds, so a reader can follow evidence → book in one hop.
- **The book's rule is stronger than the gate's**: every paragraph names its sources on its own line
  (`*Sources: `research/<study>.md` §x; ledger [n].*`), the claim's label travels with it, and every chapter is read a
  second time against those sources with a verdict per paragraph in `<chapter>.audit.md`. `scripts/verify-reference.sh`
  is the mechanical half; the second read is not optional.
- **When the book and a study disagree, that is a bug in one of them** — fix it in place with a date and land the
  correction in both, per the landing rules above. Never leave the reader with two answers.
- **`design/` is not the book.** It is the implementation record (ADRs, the partition, the scheduler, the register);
  the book cites it per claim. Do not fold design prose into a chapter without re-sourcing it as a claim.

## The register, and what is not a ticket

**Owner's ruling (2026-11-28):** a question only a board, a scope or a meter could settle goes in
`design/emulator-limitations.md` with its state (`documented`, `measured-elsewhere`, `read`, `ours`, `open`,
`unmeasurable`), what the record already gives, what the model does instead, and the one measurement that would close
it — **never a ticket**. A ticket is a promise of work; "nobody can do this yet" is not work.

- **Search before writing `unknown`.** The STE community publishes: a forum thread, Hatari's author and the RTL's
  author have settled more than one row that looked unmeasurable.
- **Separate a reading from a measurement.** A pin name, a net, a pad's direction is a `read` — a 600 dpi crop and the
  owner's eyes — and costs an afternoon, not a session. Measurements live in the register; readings live in
  `design/board-inventory.md` §6 and the component pages.
- **A ruling is not an unknown.** "Not modelled, by decision, on this evidence" is a dated block in the study; a later
  session must not read it as a hole in the evidence.

## Landing a study's corrections

- **Dated, in place, never annotated around.** A page with a wrong frame shape is replaced, not footnoted; the study
  carries the evidence and the addresses.
- **A retired claim lives in several files.** Grep the whole tree for the dead figure, the dead premise's vocabulary
  and the dead designator before declaring the landing complete — the IKBD session found a stale `U900` line in
  `design/partition-audit.md` and an "unresolved" row in `board-inventory.md` §2 after its four named files were done.
- **A brief you execute is a document you may correct.** Tick its acceptance criteria and definition of done, and fix
  its wrong seeds in place with a dated parenthetical — the brief is read as a contract by the next session.

## Commit and push — the remote has other writers

- Work in a **worktree** (`git worktree add ../the-st-wt/<topic> -b <topic> origin/main`); the shared tree is typed
  in by other sessions and a `git commit -a` there sweeps whatever is uncommitted into a stranger's commit.
- **Write the message to a file** and `git commit -F <path>`; never chain a heredoc into `git push`.
- **Expect the push to lose the race, and rebase in a loop.** A concurrent session pushed three times during one
  documentation session; `git push origin HEAD:main` is rejected as non-fast-forward and the fix is
  `git fetch && git rebase origin/main` then retry — not a force push. Force only with `--force-with-lease` after
  `git rev-parse HEAD origin/main` agrees.
- **A rebase over a concurrent edit conflicts in the shared register** (`design/emulator-limitations.md` is the
  popular file): resolve by keeping *both* sides' new sections and renumbering headings once, then re-check
  `grep -n '^## '` for a duplicated or skipped number.
- **Fast-forward the shared tree afterwards** (`git merge --ff-only origin/main` there) and assert with
  `git rev-parse HEAD origin/main` printing the same sha, plus `git status --short` empty.
- **If you slip and write into the shared tree, move the file into the worktree — never `rm -rf` there.** A deletion
  inside a tree another session is typing in trips the approval gate and stalls the turn, and a stray file is
  harmless once the same path is tracked; `mv` loses nothing and claims nothing.

## The vendored trees are documentation archives

`hatari/`, `neost/` and `steemsse-V4.2.0_R10/` are checked in, and their `doc/` directories carry the kind of
evidence the web no longer does: Hatari's `ikbd.c` header is a ROM analysis with buffer addresses and drop rules,
Steem's `steem/doc/SSE/` holds a commented disassembly and a RAM map of the IKBD's mask ROM, and
`3rdparty/doc/` holds Atari's own protocol text. `dev-docs.atariforge.org`, the campaign's old document host, no
longer resolves. **Grep the vendored trees for the chip's name and for `doc/` before searching the web**, and cite the
file and the address rather than a summary of it.

## Running the closing audit

The audit's rule is the campaign's stopping rule: **every open question in our core has either a labelled claim or an
explicit `unknown` with the experiment that would settle it.** Walk it in three directions and write all three down:

1. **Study → home.** Every row of every study's "what the model needs" has a claim, a tier and a design file; every
   row of every "what is still unknown" is a register row (or a `read` in the inventory).
2. **Design → study.** Every `unknown`/`unverified`/TODO marker in `design/*.md` is either a claim the studies
   settled or a register row. `grep -niE "unknown|unverified|TODO|TBD|open question" design/*.md` is the whole check,
   and the answer is short when the repository is disciplined.
3. **The seven questions, per chip** (`docs/handoffs/silicon-research-per-chip.md` §2): images, clocking/counting,
   control structure, registers/latches, datapath, documented-vs-real, what the model needs. A chip whose study
   answers six of seven has a gap the audit names.

The deliverable is `docs/handoffs/silicon-closing-audit.md`: the per-chip matrix, the orphans with a disposition (register
row landed / claim landed / ruled not modelled / reading with its route), the register's state afterwards, and the
verdict on the stopping rule. **Land the orphans in the same session** — an audit that only lists them has moved the
debt, not closed it.

- **Expect the oldest studies to carry the most orphans.** A study cannot leak into a file that did not exist when it
  was written, so the register looks complete while the studies that predate it keep their residue in their own "what
  is still unknown" section — that is where the rows to land are, and the register's row count is never evidence of
  completeness.
- **A chip's section of the register can be entirely missing.** Walk the campaign index's chip list against the
  register's headings before auditing any row: a part studied before the register existed can have no section at all.
