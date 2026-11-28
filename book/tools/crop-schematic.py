#!/usr/bin/env python3
"""Crop a schematic sheet from the STE field service manual, reproducibly.

Needs PyMuPDF (`pip install pymupdf`); the figures it draws are committed, so a reader never
has to run it.

    book/tools/crop-schematic.py            # write the figures the book uses
    book/tools/crop-schematic.py --list     # what it can cut

A figure in this book is never hand-made: the rectangle is recorded here, the script prints what
it drew and the source's hash, so a caption and its picture cannot drift apart. The source is the
vector redraw in `ste_fsm_1991.pdf` (Atari P/N C302481-001 Rev A, August 1991): sheet rectangles are
in the *rotated* page space, which is what `get_pixmap` clips in.

Sheets, from `design/board-inventory.md` §1.4 (printed page = PDF page − 5):

    1. ATARI 1040STe CPU, BLITTER, ROM and Cartridge        PDF 64
    2. Atari 1040STe Input/Output                           PDF 65
    3. ATARI 1040STe 03 DMA FDD CLK                         PDF 66
    4. Atari STe MCU, SHIFTER and VIDEO                     PDF 67
    5. ATARI 1040ST STEREO SOUND, JOYS and PADS             PDF 68
    6. ATARI 1040STe RAM                                    PDF 69
"""
from __future__ import annotations

import hashlib
import sys
from pathlib import Path

try:
    import pymupdf
except ImportError:  # older installs expose the same library as fitz
    import fitz as pymupdf  # type: ignore[no-redef]

ROOT = Path(__file__).resolve().parents[2]
PDF = ROOT / "atari-st-hardware/docs/downloads/ste_fsm_1991.pdf"

# (output, pdf page, clip rect in rotated page points, what it shows)
CROPS = [
    (
        "book/part-2/figures/2.2-sheet4-gstmcu-and-shifter.png",
        67,
        (48, 140, 338, 565),
        "sheet 4: U400 (GSTMCU) and U401 (GST Shifter) with their pins",
    ),
]


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for block in iter(lambda: fh.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def main() -> int:
    if "--list" in sys.argv:
        for out, page, rect, what in CROPS:
            print(f"p{page} {rect}  {out}\n     {what}")
        return 0

    src_hash = sha256(PDF)
    doc = pymupdf.open(PDF)
    for out, page_no, rect, what in CROPS:
        page = doc[page_no - 1]
        pix = page.get_pixmap(dpi=300, clip=pymupdf.Rect(*rect))
        target = ROOT / out
        target.parent.mkdir(parents=True, exist_ok=True)
        pix.save(target)
        print(f"drew {out}")
        print(f"  from {PDF.relative_to(ROOT)} p{page_no}, clip {rect} pt, 300 dpi")
        print(f"  {pix.width}×{pix.height} px, {target.stat().st_size} bytes")
        print(f"  source sha256 {src_hash[:16]}…")
        print(f"  shows: {what}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
