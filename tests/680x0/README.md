# The 680x0 corpus (vendored)

The 68000 core's oracle: Thomas Harte's single-step test set for the 68000, from
[`SingleStepTests/680x0`](https://github.com/SingleStepTests/680x0), pinned at commit
`e0d5ece9670205cc84a0101081837deb446f86a3` ("Import initial tests", 2024-05-13).

It is what makes "the core implements the instruction set" a measurable claim instead of a reading of
the manual: 1 000 060 tests over 124 opcode files, each one *one* instruction in isolation, with the
registers, both stack pointers, the status register, the program counter and a sparse byte map given
before and after. The generator is the 68000 in [Tom Harte's CLK](https://github.com/TomHarte/CLK)
(`Processors/68000`), chosen upstream because it conforms to the documented behaviour and to every
other published test set.

`validation-harness cpu-tests` runs it (see below).

## The second corpus: MAME's microcoded core — the **gate**, since 2026-11-19

`tests/680x0/mame/` (git-ignored, fetched by `scripts/fetch-m68000-tests.sh`) holds
[`SingleStepTests/m68000`](https://github.com/SingleStepTests/m68000) at pin
`64b253116a3de04aaac4346c43680960dc9b67e5`: **127 files × 2 500 = 317 500** records generated from
MAME's microcoded 68000, MIT-licensed, shipped as `.json.bin` and converted by upstream's own `decode.py`.

By the owner's ruling it is the **source of truth and the gate for clocks *and* state** — it is a strict
superset of CLK's opcode files (127 against 124: it adds `ILLEGAL_LINEA`, `ILLEGAL_LINEF`, `STOP`) and
covers the same 53 of 53 instruction forms on the files both carry, while carrying 3.1× fewer data vectors
a file. CLK stays vendored as the denser suite, **reported beside it and never subtracted from it**.

```
scripts/fetch-m68000-tests.sh
tools/validation-harness/bin/Debug/net11.0/validation-harness cpu-tests --corpus mame
tools/validation-harness/bin/Debug/net11.0/validation-harness cpu-tests --corpus clk --dir tests/680x0/full
```

Its format is the same shape with three conventions of its own, all handled by `--corpus mame`: `pc` is
`m_au` — the **next prefetch address**, so the opcode sits at `pc − 4` — `ram` pieces are 16-bit (already
split into bytes by `decode.py`), and a byte's lane on the bus is `UDS && !LDS ? data >> 8 : data & 0xFF`
where CLK always puts it low. `re`/`we` in `transactions` are the **aborted** address-error cycles: 4
clocks of `length`, no bus cycle performed. `TAS` and `TRAPV` are outside any 100 % claim on this corpus —
MAME's own README disowns its `TAS` read-modify-write timing and its `TRAPV` tests.

## What is in the tree, and why not all of it

The upstream corpus is **194 MB of gzip**. That is more than this repository's whole history, so the
tree carries a deterministic sample and the script that produced it:

| Path | What it is |
|---|---|
| `v1/<mnemonic>.json.gz` | 24 tests per opcode file — 124 files, 2 976 tests — evenly spaced through the file, first and last included |
| `68000.official.json` | upstream's full decoding of the officially-defined opcodes (the map, not tests) |
| `full/` | the whole corpus, when fetched — **git-ignored**, never committed |

The sample is not hand-picked: `scripts/fetch-680x0-tests.sh` regenerates it from the pinned commit by
taking `round(i × (n−1) / 23)` for `i` in `0…23` out of each file, and the corpus is randomly ordered so
a stride samples every addressing mode the file covers. Bump `sample_size` in that script if a wider
net is wanted.

**A claim about the core must be measured against `--full`**, not against the sample:

```
scripts/fetch-680x0-tests.sh --full          # writes tests/680x0/full/ from the pinned commit
tools/validation-harness/bin/Debug/net11.0/validation-harness cpu-tests --dir tests/680x0/full
```

## The test format (`v1`)

```json
{
  "name": "5e4a [ADD.w Q, A2] 1",
  "initial": { "d0"…"d7", "a0"…"a6", "usp", "ssp", "sr", "pc",
               "prefetch": [w0, w1], "ram": [[address, byte], …] },
  "final":   { … same shape, plus the bytes the instruction wrote … },
  "length":  8,
  "transactions": [["r", 4, 6, 3076, ".w", 467], ["n", 4]]
}
```

- `a7` is not a field: the corpus keeps `usp` and `ssp` and the status register's S bit decides which
  one is live. The harness compares all three, and A7 against whichever is live.
- `ram` holds *only* the bytes that differ from zero, so the base memory is zero-filled, with the two
  `prefetch` words at `pc`. That is exactly the machine the harness runs: 24-bit, big-endian, no banks.
- `length` is in **CPU clock cycles** and is *graded* as `exact-clock` (`4 × our bus cycles + our
  internal clocks == length`), which is what the core's internal-clock table is measured against
  (`LENGTH.md`, `PLACEMENT.md`); the raw bus-cycle count alone never matches it, by construction.
- `transactions` lists the bus activity; the harness does not gate it, because the prefetch model
  differs (fetch-on-demand, ADR 0009 §4's gap).

`--out FILE` writes the per-opcode table; `--failures FILE` writes detail lines per opcode file
(opcode, test name, first difference), which is what triage wants (`--failures` keeps *every* failure;
the printed list stops at 20 a file); `--file NAME` runs one opcode, `--limit N` caps the tests per file,
`--show N` sets how many full failures are printed, `--corpus clk|mame` picks the corpus (and its default
directory) and `--clocks FILE` dumps one line a record for the clock table.

## What a failure means

The harness runs each test against `SingleStepBus` from `TheST.Machine.Corpus` — plain RAM, one answer
four ticks after the assertion, no beam, no counters, no interrupts — so a failure is the core's and
never a chip's. It compares, in this order, and reports the **first** difference:

`d0…d7`, `a0…a6`, `usp`, `ssp`, the defined bits of `sr` (`0xA71F`: T, S, I2–I0, CCR), `pc`, then every
byte of `final.ram`.

## Upstream licence

The corpus declares **no licence** upstream (the repository has none). It is vendored here as test data
with attribution, for local verification only, and is not built into, shipped with or licensed as part
of The ST. See `NOTICE`.
