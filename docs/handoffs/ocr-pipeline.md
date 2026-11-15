# Handoff — the magazine OCR pipeline (map #51, tickets #52 / #60 / #61 / #64)

> **Moved 2026-11-15 (ticket #60).** The corpus now has its own repository:
> [`laurentprudhon/the-st-software`](https://github.com/laurentprudhon/the-st-software) — private,
> text and data only. The schema, the tooling (`tools/corpus/**`) and the data left this repository;
> `design/corpus-*.md` and `tools/corpus/README.md` here are pointers. Read any path below as being
> relative to that repository. The tracker, the maps and these handoffs stay in `the-st`.

Written 2026-11-15 at the end of the session that built the corpus fact base and proved the OCR stack. Read
this first; it is self-contained. Everything it cites is on `main`.

## Where the work stands

| ticket | state | what it holds |
|---|---|---|
| **#52** schema | open, claimed | `design/corpus-schema.md` — file model (5 levels), **article** and **review** as separate entities, review = list of aspect scores `{aspect, value, scale_min, scale_max, direction, raw}` + our English summary. Q1 (file-model identity) still parked. |
| **#57** host registry | **closed** | `research/st-host-registry.md` — ~330 domains probed, ~45 serve ST bytes, 22 countable, 10 with protected formats, 5 publishing checksums, 1 with a rights-holder permission (`lankhor.net`). |
| **#59** magazines | **closed** | `research/st-magazines.md` + `research/magazines/` — per-country titles, six incompatible score scales, scan sources, page arithmetic, DPI distribution. |
| **#60** artefact store | open, unstarted | the private repo that will hold formatted article text, page crops, box/disk/manual scans + OCR. **Its id scheme must be settled before the pipeline stores a crop.** |
| **#61** test corpus | open, claimed | `runs` enum + `is-what-it-claims` + `run-status` ruled. **`runs` is gated on the emulator's first beta (~2 weeks).** |
| **#64** OCR pipeline | open, claimed | `stage → extract → verify` with a golden set as acceptance. **This is the active work.** |

## What is proven, with numbers

- **Engines, all on llama.cpp, all GGUF** (`research/ocr-smoke-test.md`): OvisOCR3 Q4 → 58 blocks of valid
  `{bbox, category, content}` JSON in 23.5 s/page; d1-2B → 4 gate questions in 2.5 s with **0 output tokens**
  and calibrated probabilities; LFM3-VL-2B → correct 3-sentence summary in ~1,100 tokens; LightOnOCR-3 →
  8,796 chars as a cross-check.
- **Sizing** (`research/ocr-batching-and-sizing.md`): KV **13.0 KiB/token** (measured, not derived — the
  architecture formula is 4× too high), d1 flat at 1.5 GiB, image tokens ≈ 1 per 975 px. On a 32 GB card three
  servers at 128k context use **30 % of VRAM**: memory is not the constraint, context and compute are.
- **The DPI budget line**: a 1200×1560 page is 1,920 image tokens; a 300 dpi A4 page is **8,923** — 4.6× the
  compute for the same page. The Internet Archive's per-page JPEG is **145 dpi**, below the ruled 300 dpi
  floor, so for ST Format the page must come from the JP2 zip or the archived PDF.

## The frontier, in order

1. **#64a — the golden set** (start here, no GPU needed). Atari Legend's magazine index: 69 issues, 1,252 rows,
   **770 `Review`** with printed folios; the French index adds 2,832 page-located rows. The hard part is the
   **folio → leaf mapping**: the printed page number is not the page index, and only 25 of 55 sampled IA items
   carried a leaf→folio map. Measure it per issue (OCR the folio from the page corner and match — the pipeline
   that reads folios is the one that needs them).
2. **#64b — `stage`**: per-page fetch, content-addressed cache, provenance (URL, date, status, size, **DPI**,
   provenance class `own-scan` / `archived-scan-degraded` / `third-party-ocr-fallback`).
3. **#64c — `extract` + `verify`**: OvisOCR3 blocks → d1 gates → KIE with the review template → LFM3 summary;
   LightOnOCR-3 cross-check with disagreement routed to review. `verify` prints **located reviews** and
   **correct scores** against the golden set. Cheapest calibration run: **ST Review 35/35**, complete and
   already OCR'd.
4. **#60** — the artefact store id scheme (a design session, the owner's call).
5. **#61** — `runs`, when the emulator beta ships.

## Environment (measured on the 8 GB box; re-derive on the 5090)

| fact | value |
|---|---|
| GPU here | RTX 3070 Ti Laptop, 8191 MiB; driver 620.70.03, CUDA UMD 13.5 |
| llama.cpp | nightly **b12409**, `llama-b12409-bin-ubuntu-cuda-12.8-x64` + its `cudart` tarball (the tagged v1.0.0 release has **no binaries**) |
| models | OvisOCR3 Q4_K_M + mmproj Q8 · d1-2B Q4_K_M + mmproj Q8 · LFM3-VL-2B Q4_K_M + mmproj Q8 · LightOnOCR-3-0.8B Q4_K_M + mmproj Q8 (~3.5 GB total) |
| repo | `laurentprudhon/the-st`, worktree branch `51-corpus`, `main` is canonical |

`tools/corpus/setup.sh` installs llama.cpp and the models and verifies the GPU; `tools/corpus/sizing/` holds
the calculator and the VRAM measurement scripts.

## Running on wslc (the native WSL container engine) — no GPU flags needed

The engine passes the GPU through a hook: a wslc container gets `/.wslc-gpu-hook`, `/dev/dxg` and an
`/usr/lib/wsl/lib` overlay automatically, so `nvidia-smi` works inside with **no `--gpus` flag and no
nvidia-container-toolkit** (that is docker's mechanism, not wslc's). Verified in the container this handoff
was written from.

```powershell
# Windows PowerShell — wslc.exe is a Windows binary
wslc container run -d --name hermes-5090 `
  -p 127.0.0.1:8642:8642 -p 127.0.0.1:9119:9119 `
  -v hermes-data-5090:/opt/data `
  -e API_SERVER_HOST=0.0.0.0 `
  nousresearch/hermes-agent:latest gateway run
wslc container exec hermes-5090 nvidia-smi        # the one check that matters
```

wslc quirks that bite: **no `--restart` policy** (restart with `wslc container start`), **bridge networking
only** (explicit `-p`), and its output carries NUL bytes through pipes (`| tr -d '\000'`). Models live under
`/opt/data/models` so they persist in the data volume — no second volume needed.

If a future engine build ever stops passing the GPU, the fallback is to run `llama-server` in the WSL distro
and have the container call it over HTTP; the pipeline only needs the four endpoints, not the device.

## Traps that look like model failures

1. LightOnOCR-3's output arrives in **`reasoning_content`**, not `content` — a naive client reads empty.
2. LFM3-VL-2B needs a **2k thinking budget**; at 600 tokens it narrates and never answers.
3. OvisOCR3's structured JSON for a dense page is **~5,500 tokens**; 4k truncates mid-JSON. Budget 8k, ctx 16k.
4. `--image-min-tokens 1024` — Qwen-VL-family models lose grounding accuracy below it.
5. d1 needs **`-b 8192 -ub 8192`** and its endpoint is **`POST /v1/systemone`**, not the chat API; llama.cpp
   supports `noul`/`choice`/`score` only.
6. Size from **measurement**, not the architecture formula.

## How to work this stream

- Claim a ticket before touching it (`gh issue edit <n> --add-assignee @me`).
- Docs land on `main` by **rebase + fast-forward-only ref update** (`force=false`), never a merge commit.
- **Never touch** `src/TheST.Machine/**`, `tools/validation-harness/**`, `tools/suspension-bench/**`,
  `scripts/validate.sh` — the emulator's perf chain owns them.
- Corpus code lives under `tools/corpus/`.
- Licence stance: facts and our summaries in this repo; the magazines' expression (article text, page crops)
  only in the artefact store (#60), which is private for now.
