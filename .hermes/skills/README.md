# Project skills — the-st's own procedural knowledge

These are **the project's** skills: the rules a session needs *because it is working on this repository*, versioned
with the code they describe. The generic method skills (`research-silicon-chip-implementation`,
`technical-schematics`, `grounded-citations`, `code-review`, …) live in the agent's profile, not here — they apply to
any project. What belongs here is everything that would be wrong, stale or meaningless somewhere else: the corpus
gate, the register's rules, the push protocol for a remote another session also writes to.

| skill | when it loads |
|---|---|
| `the-st-68000-core/` | any work on `src/TheST.Machine/Cpu/`, the MAME/CLK corpus, or the comments pass — the gate, the measured rules, the pitfalls |
| `the-st-research-studies/` | writing or correcting a study under `research/`, running a chip's closing audit, or landing a study's corrections in `design/` |

## The rule for adding one

**When a session learns something about *this repo* that the next session needs, it goes in a skill here and is
committed with the work that taught it.** A lesson that lives only in a chat, or only in the agent's profile, is lost
to the next reader of the repository — and this repository is read by several agents and by one human who does not
want to re-derive a gate.

- **A procedure goes in a skill; a fact about the machine goes in `design/` or `research/`.** If the lesson is "the
  `GPIP4` bit is channel 6", it belongs in `design/timers-and-events.md`. If it is "run the gate on the uncommitted
  tree with the parent commit as its baseline", it belongs here.
- **One rule per bullet, with the reason.** Every rule in these files exists because a session lost time without it;
  write the loss, not the moral.
- **No dates, no PR numbers, no incident narration** in the body — the git history carries those.

## Keeping the two copies honest

The agent profile holds a live copy under `~/.hermes/skills/` (synced separately to `hermes-sync`), and these
repository copies are the ones a reader of the repo gets. They are copies of one text, and a silent divergence is a
bug: when you edit one, edit the other in the same session, and treat the **profile copy as the one agents load**
(`skill_view`) while this one is authoritative for what the project says it does.

```sh
# refresh the repository copies from the profile after editing a skill there
rsync -a --delete ~/.hermes/skills/the-st-68000-core/        .hermes/skills/the-st-68000-core/
rsync -a --delete ~/.hermes/skills/the-st-research-studies/  .hermes/skills/the-st-research-studies/
```

## Tools

`scripts/verify-research.sh` is the citation gate for a study, with the ledger's ids and the 30 % floor baked in:

```sh
scripts/verify-research.sh research/ikbd-implementation.md
scripts/verify-research.sh --all          # every study under research/
```

The gate is a *publishing* check, not a correctness one: it says every third sentence carries a source, and that no
id exists that the ledger does not hold. What the study claims is still the study's problem.
