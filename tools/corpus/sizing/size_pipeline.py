#!/usr/bin/env python3
"""Size a llama.cpp OCR pipeline: how many pages can be in flight, and what does it cost in VRAM.

Constants are MEASURED on the 8 GB box (2026-11-15, llama.cpp b12409), not assumed:
  - KV cache: 13.0 KiB per token for both VL models (OvisOCR3 and LFM3-VL-2B, same architecture family),
    derived from the 8k -> 32k context delta: 312 MiB / 24,576 tokens.
  - d1-2B: flat ~1.5 GiB, no measurable KV growth with context (single forward pass, no generation).
  - image tokens: ~1 token per 975 px of page image (measured: a 1200x1560 page = 1,920 image tokens,
    from prompt_tokens 2,172 minus ~250 tokens of prompt text).
  - output tokens: OvisOCR3 structured ~5,500 for a dense page; LFM3-VL summary ~1,100; d1 0.

Usage:
  python3 size_pipeline.py --vram 32 --page 1200x1560 --dpi 145
  python3 size_pipeline.py --vram 32 --page 2480x3508 --dpi 300 --sweep
"""
import argparse, math

KV_KIB_PER_TOKEN = 13.0          # measured
PX_PER_TOKEN = 975.0             # measured
BASE_MIB = {                     # measured at 8k ctx, minus KV
    "ocr":   1077,               # OvisOCR3 Q4_K_M + mmproj Q8
    "gate":  1523,               # d1-2B Q4_K_M + mmproj Q8 (flat)
    "summ":  1879,               # LFM3-VL-2B Q4_K_M + mmproj Q8
}
OUT_TOKENS = {"ocr": 5500, "gate": 0, "summ": 1100}
TEXT_TOKENS = 250                # prompt text per request


def image_tokens(w, h):
    return int(round((w * h) / PX_PER_TOKEN))


def per_request_tokens(role, w, h):
    return image_tokens(w, h) + TEXT_TOKENS + OUT_TOKENS[role]


def kv_mib(tokens):
    return tokens * KV_KIB_PER_TOKEN / 1024.0


def plan(vram_gb, w, h, ctx, np_slots, reserve_gb=1.5):
    """Return per-server VRAM and the concurrency each server can actually hold."""
    usable = vram_gb * 1024 - reserve_gb * 1024
    rows = []
    for role in ("ocr", "gate", "summ"):
        need = per_request_tokens(role, w, h)
        kv = kv_mib(ctx)
        total = BASE_MIB[role] + kv
        by_ctx = ctx // need if need else np_slots
        by_vram = int((usable - BASE_MIB[role]) // kv_mib(need)) if need else np_slots
        rows.append(dict(role=role, base=BASE_MIB[role], kv=kv, total=total,
                         need=need, by_ctx=by_ctx, by_vram=by_vram,
                         slots=min(np_slots, by_ctx, by_vram)))
    return rows, usable


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--vram", type=float, default=32.0, help="GPU VRAM in GiB")
    ap.add_argument("--page", default="1200x1560", help="page image WxH in pixels")
    ap.add_argument("--dpi", type=int, default=0, help="informational: printed DPI of that page")
    ap.add_argument("--ctx", type=int, default=131072, help="context per server")
    ap.add_argument("--np", type=int, default=8, help="requested --parallel slots per server")
    ap.add_argument("--sweep", action="store_true", help="sweep ctx and np")
    a = ap.parse_args()
    w, h = (int(x) for x in a.page.lower().split("x"))

    print(f"page {w}x{h} px" + (f" (~{a.dpi} dpi)" if a.dpi else "") +
          f" -> {image_tokens(w,h)} image tokens")
    print(f"GPU {a.vram} GiB, ctx {a.ctx}, --parallel {a.np}\n")

    rows, usable = plan(a.vram, w, h, a.ctx, a.np)
    print(f"{'server':6} {'base MiB':>9} {'KV MiB':>8} {'total MiB':>10} {'tok/req':>8} "
          f"{'by ctx':>7} {'by VRAM':>8} {'slots':>6}")
    tot = 0
    for r in rows:
        tot += r["total"]
        print(f"{r['role']:6} {r['base']:9.0f} {r['kv']:8.0f} {r['total']:10.0f} {r['need']:8d} "
              f"{r['by_ctx']:7d} {r['by_vram']:8d} {r['slots']:6d}")
    print(f"\nthree servers resident: {tot:.0f} MiB of {usable:.0f} MiB usable "
          f"({100*tot/usable:.0f} %)")
    print(f"pages in flight (sum of slots): {sum(r['slots'] for r in rows)}")

    if a.sweep:
        print("\n--- sweep: pages in flight vs context, at --parallel 32 ---")
        print(f"{'ctx':>8} {'ocr':>5} {'gate':>5} {'summ':>5} {'total MiB':>10} {'%VRAM':>6}")
        for ctx in (16384, 32768, 65536, 131072, 262144):
            rows, usable = plan(a.vram, w, h, ctx, 32)
            tot = sum(r["total"] for r in rows)
            print(f"{ctx:8d} {rows[0]['slots']:5d} {rows[1]['slots']:5d} {rows[2]['slots']:5d} "
                  f"{tot:10.0f} {100*tot/usable:5.0f}%")


if __name__ == "__main__":
    main()
