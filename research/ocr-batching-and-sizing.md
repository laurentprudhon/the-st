# Batching and sizing the OCR pipeline — measured constants, and what a 5090 can hold

STATUS: **complete (v1)** — 2026-11-15. Answers one question: with three llama.cpp servers running
concurrently, how many magazine pages can be in flight, and what does each one cost?

Everything here is **measured on the 8 GB box** (llama.cpp b12409, RTX 3070 Ti Laptop) and then applied to a
32 GB card by arithmetic. The calculator is `tools/corpus/sizing/size_pipeline.py`; the raw measurements are
`tools/corpus/sizing/vram-measured.csv`. To re-derive the constants on another card (the 5090 will have a
different driver and possibly a different llama.cpp build), run
`tools/corpus/sizing/measure_vram.sh` — it starts each model at 8k and 32k context, reads `nvidia-smi`, and
prints the CSV rows; `--quick` does one model as a sanity check. Verified on this box: it reproduces
1,181 MiB and 1,493 MiB exactly.

## 1. The measured constants

| constant | value | how it was measured |
|---|---|---|
| **KV cache** | **13.0 KiB per token** | same model at `-c 8192` and `-c 32768`: 1,181 → 1,493 MiB, i.e. 312 MiB per 24,576 tokens. Identical for LFM3-VL-2B (1,983 → 2,295). |
| **d1-2B VRAM** | **flat ~1.5 GiB, no KV growth** | 1,523 MiB at both 8k and 32k — a decision model answers in one forward pass, so context costs it nothing measurable. |
| **base VRAM** (weights + mmproj + compute) | OCR **1,077 MiB** · gate **1,523 MiB** · summary **1,879 MiB** | measured total at 8k minus the KV share |
| **image tokens** | **~1 token per 975 px** of page image | a 1200×1560 page = 1,920 image tokens (prompt_tokens 2,172 minus ~250 tokens of prompt text) |
| **output tokens** | OCR **~5,500** (dense page, structured JSON) · summary **~1,100** · gate **0** | measured in the smoke test |

The KV figure is the surprise: 13 KiB/token is a quarter of the naive f16 estimate (48 KiB/token for
`n_layer 24 × n_embd_kv 1024 × 2 B`), so this build is already running a quantised KV cache. **Do not size
from the architecture formula — size from the measurement**, or you will reserve four times the memory you
need and conclude the card is too small.

## 2. The model

```
tokens_per_request(role) = image_tokens + prompt_text + output_tokens(role)
image_tokens             = (W_px × H_px) / 975
vram_per_server(role)    = base(role) + ctx × 13 KiB
concurrency_by_ctx       = ctx / tokens_per_request
concurrency_by_vram      = (usable_vram − base) / (tokens_per_request × 13 KiB)
slots                    = min(--parallel, concurrency_by_ctx, concurrency_by_vram)
```

## 3. What a 32 GB card holds (three servers resident)

At `--ctx 131072` and a 145 dpi page (what the Internet Archive actually serves):

| server | base | KV | total | tokens/request | by ctx | by VRAM | slots at `-np 8` |
|---|---|---|---|---|---|---|---|
| OCR (OvisOCR3) | 1,077 | 1,664 | 2,741 | 7,670 | 17 | 309 | 8 |
| gate (d1-2B) | 1,523 | 1,664 | 3,187 | 2,170 | 60 | 1,078 | 8 |
| summary (LFM3-VL) | 1,879 | 1,664 | 3,543 | 3,270 | 40 | 707 | 8 |
| **three resident** | | | **9,471 MiB of 31,232 usable (30 %)** | | | | **24 pages in flight** |

**The conclusion that matters: on a 5090, memory is not the constraint — context and compute are.** Three
servers at 128k context use 30 % of the card; at 256k they use 46 %. You could give each server a 256k context
and still have half the card free. So the sizing question is not "will it fit" but "how many pages in flight
does the GPU actually process faster", which is a benchmark, not an arithmetic.

## 4. The DPI cost, which is the real budget line

| page | pixels | image tokens | tokens/request (OCR) | pages in flight at 128k ctx |
|---|---|---|---|---|
| 1200×1560 (145 dpi, IA's derivative) | 1.87 M | **1,920** | 7,670 | **17** |
| 2480×3508 (300 dpi, the ruled floor) | 8.70 M | **8,923** | 14,673 | **8** |

**A 300 dpi page costs 4.6× the tokens of a 145 dpi page**, because tokens scale with area. That is the price
of the floor rule, and it is the number to quote when someone asks why the corpus is expensive: the floor
does not just exclude bad scans, it multiplies the compute of every page that passes it.

`--image-max-tokens N` is the lever if that price is too high: it caps the tokens an image may take, trading
resolution for throughput. It should be set deliberately and recorded per artefact, never left to the default.

## 5. Recommended 5090 configuration

```sh
LL=/path/to/llama-server
# OCR — the throughput-critical server
$LL -m OvisOCR3-Q4_K_M.gguf --mmproj OvisOCR3.mmproj-Q8_0.gguf \
    -ngl 99 -c 131072 -np 16 -b 4096 -ub 2048 --image-min-tokens 1024 --port 8081
# gate — cheap, latency-critical, no KV growth
$LL -m d1-2B-Q4_K_M.gguf --mmproj mmproj-d1-2B-Q8_0.gguf \
    -ngl 99 -b 8192 -ub 8192 -c 32768 -np 8 --port 8082
# summary — reasoning model, needs output room
$LL -m LFM3-VL-2B-Q4_K_M.gguf --mmproj mmproj-LFM3-VL-2B-Q8_0.gguf \
    -ngl 99 -c 65536 -np 8 -b 4096 -ub 2048 --port 8083
```

Why these numbers: `-np 16` on OCR because context allows 17 at 145 dpi and 8 at 300 dpi — start at 8 if the
corpus is 300 dpi. `-ub 2048` because a vision request's image tokens must fit the physical batch, and
`--image-min-tokens 1024` because llama.cpp warns that Qwen-VL-family models lose grounding accuracy below
that. d1 gets `-b/-ub 8192` because its whole question batch is read in one pass. The summary server gets a
smaller context because its requests are short and its output is ~1.1k tokens.

## 6. The benchmark that decides the batch size (not yet run)

Memory gives the ceiling; only a sweep gives the knee. On the 5090, with a fixed set of ~200 real pages:

1. For `-np ∈ {1, 2, 4, 8, 16, 32}`, run the OCR server alone and record **pages/minute** and **VRAM**.
2. Repeat with all three servers resident, to measure the interference (the gate and summary servers steal
   SM time even when idle-ish).
3. Pick the smallest `-np` within 5 % of the best pages/minute — beyond the knee you pay VRAM and latency for
   nothing.
4. Record the result as a row in this note, with the page resolution and the model quantisation, because the
   number is meaningless without them.

On this 8 GB box the same sweep is possible but only at `-np ≤ 2` for OCR (context allows 2 concurrent pages
at 16k), which is why the real numbers must come from the 5090.

## 7. What this means for the pipeline's shape

- **Stages must be separate processes, not one big model**: the gate is 2.5 s and needs no KV, the OCR is
  23.5 s and needs a lot — running them as one queue would idle the GPU on the cheap stage.
- **The gate should run on the OCR output, not on the page image**, wherever possible: a `noul` question over
  the extracted blocks costs ~2k tokens instead of ~7.7k, and it is the same answer.
- **Batch by page, not by issue**: a page is the unit whose tokens are known in advance, so the queue can be
  sized exactly; an issue is a variable-size blob that would make the batch unpredictable.
