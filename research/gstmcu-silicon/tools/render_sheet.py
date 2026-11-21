#!/usr/bin/env python3
"""Render one sheet of ST4081S.PDF to PNG, for the owner's eyes.

The text layer gives titles and net names but not connections; where a claim needs
the drawing itself, render the sheet and let the owner read it (a vision model
fabricates geometry — see the repo's note in research/recovered-asic-designs.md).

    python3 render_sheet.py SHEET --page 23 --dpi 200
    python3 render_sheet.py SHEET --page 23 --dpi 400 --clip 0,0,0.5,0.5

--clip is a fraction of the page (x0,y0,x1,y1), so a zone can be zoomed without
guessing pixel coordinates. Output goes beside the PDF unless --out is given.
"""
import argparse, os, sys


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('pdf')
    ap.add_argument('--page', type=int, required=True, help='1-based printed sheet number')
    ap.add_argument('--dpi', type=int, default=200)
    ap.add_argument('--clip', help='x0,y0,x1,y1 as fractions of the page')
    ap.add_argument('--out')
    a = ap.parse_args()

    try:
        import pymupdf
    except ImportError:
        sys.exit('pymupdf missing: pip install pymupdf')

    doc = pymupdf.open(a.pdf)
    page = doc[a.page - 1]
    rect = page.rect
    clip = None
    if a.clip:
        fx0, fy0, fx1, fy1 = (float(v) for v in a.clip.split(','))
        clip = pymupdf.Rect(rect.x0 + fx0 * rect.width, rect.y0 + fy0 * rect.height,
                            rect.x0 + fx1 * rect.width, rect.y0 + fy1 * rect.height)
    pix = page.get_pixmap(dpi=a.dpi, clip=clip)
    out = a.out or os.path.join(os.path.dirname(a.pdf), f'sheet{a.page:02d}-{a.dpi}dpi.png')
    pix.save(out)
    print(f'{out}  {pix.width}x{pix.height}')


if __name__ == '__main__':
    main()
