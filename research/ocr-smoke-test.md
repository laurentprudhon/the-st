# OCR smoke test — llama.cpp on the 8 GB box, three models, one real page

STATUS: **complete (v1)** — 2026-11-15. Purpose: prove the ruled architecture (llama.cpp only, GGUF models)
before any pipeline code exists, and measure what one magazine page actually costs.

## Environment (measured, not assumed)

| fact | value | how |
|---|---|---|
| GPU | **NVIDIA GeForce RTX 3070 Ti Laptop, 8191 MiB (7097 free)** | `nvidia-smi`, `llama-server --list-devices` → `CUDA0` |
| CUDA driver libs | `/usr/lib/wsl/lib/libcuda.so.1` present | `ldconfig -p` |
| driver / CUDA | **NVIDIA 620.70.03, KMD 621.14, CUDA UMD 13.5** | `nvidia-smi` header |
| build choice | the **cuda-12.8** llama.cpp build runs fine on a 13.5 UMD driver (backward compatible) | it loaded and ran all three models |
| llama.cpp | **v1.0.0-dev, build 12409** (nightly `b12409`, ubuntu-cuda-12.8-x64) | `llama-server --version` |
| models | OvisOCR3 Q4_K_M 505 MB + mmproj Q8 116 MB · d1-2B Q4_K_M 1.1 GB + mmproj Q8 203 MB · LFM3-VL-2B Q4_K_M 1.1 GB + mmproj Q8 203 MB · LightOnOCR-3-0.8B Q4_K_M 505 MB + mmproj Q8 116 MB | `du -h` |
| test page | `atari-st-format-issue-049` page 60 (printed p31, "ST ANSWERS HELP!") | IA per-page URL, 1200×1560 |

## Results

| model | task | wall time | tokens | output |
|---|---|---|---|---|
| **OvisOCR3** Q4 | `document_structured` | **23.5 s** | 2,172 in / 5,526 out | **58 blocks, valid JSON**, `{bbox, category, content}` — text 41, image 7, paragraph_title 3, table 1, doc_title 1, header 1, footer 1, number 1 |
| **d1-2B** Q4 | 4 gate questions (`noul`×2, `choice`, `score`) | **2.5 s** | 7,619 in / **0 out** | `is_review` 0.17 · `has_score` 0.14 · `section` = **help** (0.886) · `text_density` = 3/3 (0.59) — all four correct for this page |
| **LightOnOCR-3-0.8B** Q4 | transcription | **11.2 s** | 1,874 in / 2,690 out | 8,796 chars of Markdown + HTML table; contains `ST ANSWERS`, `72%`, `Word Finder`, `Meedmore`, `Compo` |

## Five findings that change the pipeline

1. **OvisOCR3's community GGUF works, and its output is the schema we designed for.** 58 blocks with
   normalised bboxes and categories, valid JSON, no post-processing. This is the page→blocks step, proven.
2. **LightOnOCR-3's output arrives in `reasoning_content`, not `content`.** Its GGUF chat template wraps the
   transcription in a thinking block, so a naive client reads an empty string and concludes the model failed.
   The pipeline must read `reasoning_content` (or run it with the template's thinking disabled). Recorded
   because it is exactly the kind of thing that gets a good model discarded as broken.
3. **A dense magazine page needs more than 4k output tokens.** The first OvisOCR3 run with `max_tokens=4096`
   was **truncated mid-JSON at 12,458 chars**; at 8,192 it finished cleanly at 5,526 tokens. Budget 8k, and
   size the context for image tokens + output (16k was comfortable here).
4. **The Internet Archive's per-page JPEG is 145 dpi, not 300.** `page/n3.jpg`, `_w2000.jpg` and IIIF
   `full/full` all return the same **1200×1560** image for this item — the archived scan *is* 1200 px wide.
   So the "one page per URL" convenience is at 145 dpi, **below the ruled 300 dpi floor**: for ST Format, IA
   is not a usable source at the floor, and Atarimania's image-only PDFs (46 MB/issue) are the alternative.
   This is the first concrete case where the floor rule bites, and it was invisible until measured.
5. **8 GB runs one model at a time, so the stages must be sequential** — all pages through OCR, then all
   blocks through the gate, then all reviews through the summary model. On a 32 GB card the same pipeline
   overlaps. Measured VRAM headroom at load: 7,097 MiB free before the first model.

## What the cross-check will measure (design, not yet run)

Both OCR engines run on the same page; the comparison is not "which is prettier" but three numbers on the
golden set (~770 page-located reviews from Atari Legend):

- **block agreement** — do both engines find the same regions (IoU on bboxes, category agreement);
- **score agreement** — do both read the same number for the same aspect, and does either match the printed
  page;
- **disagreement as a flag** — a page where the two engines disagree is routed to review, which is the
  cheapest quality gate available and the reason the owner asked for cross-checks in the pipeline.

## Reproduce

```sh
export LD_LIBRARY_PATH=/opt/data/models/llama/cudart-llama-b12409-bin-ubuntu-cuda-12.8-x64:$LD_LIBRARY_PATH
LL=/opt/data/models/llama/llama-b12409/llama-server
$LL -m /opt/data/models/OvisOCR3-Q4_K_M.gguf --mmproj /opt/data/models/OvisOCR3.mmproj-Q8_0.gguf \
    -ngl 99 -c 16384 --port 8081
$LL -m /opt/data/models/d1-2B-Q4_K_M.gguf --mmproj /opt/data/models/mmproj-d1-2B-Q8_0.gguf \
    -ngl 99 -b 8192 -ub 8192 -c 16384 --port 8082      # d1 needs -b/-ub to hold the question batch
$LL -m /opt/data/models/LightOnOCR3-0.8B-Q4_K_M.gguf --mmproj /opt/data/models/LightOnOCR-3-0.8B.mmproj-q8_0.gguf \
    -ngl 99 -c 16384 --port 8083
```

d1's endpoint is **`POST /v1/systemone`** with `{state, images:[data-url], questions:{…}}` — not the chat API.
llama.cpp supports `noul`, `choice` and `score` only; typed extraction (`text`/`number`/`date`), multiple
choice and boxes need transformers or mlx-vlm, which is why extraction stays OvisOCR3's job.

## Artefacts

`/opt/data/cache/scratch/smoke/` — `ovis_structured2.json` (58 blocks), `lighton_transcript.txt` (8,796 chars),
`stf49_p*.jpg` (the test pages), `prompt_structured.txt` (OvisOCR3's own prompt). Cache is disposable; the
numbers above are the durable part.
