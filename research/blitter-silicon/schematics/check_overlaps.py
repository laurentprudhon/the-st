"""Estimate text bounding boxes in the generated SVGs and report collisions.

Two passes, because each has caught a real defect in this repo:
  * text vs text — estimated bounding boxes (0.60 em per character);
  * rect vs rect — the blocks themselves, which the text pass cannot see;
plus anything that leaves the canvas, which a browser happily clips and nobody notices.
"""
import glob
import xml.etree.ElementTree as ET

NS = "{http://www.w3.org/2000/svg}"


def boxes(f):
    root = ET.parse(f).getroot()
    out = []
    for el in root.iter(NS + "text"):
        s = "".join(el.itertext()).strip()
        if not s:
            continue
        size = float(el.get("font-size", 12))
        x = float(el.get("x", 0))
        y = float(el.get("y", 0))
        w = len(s) * size * 0.60
        anchor = el.get("text-anchor", "start")
        x0 = x - (w / 2 if anchor == "middle" else w if anchor == "end" else 0)
        out.append((x0, y - size * 0.78, x0 + w, y + size * 0.22, s))
    return out


def num(v):
    try:
        return float(str(v))
    except (TypeError, ValueError):
        return None


def rects(f):
    root = ET.parse(f).getroot()
    out = []
    for el in root.iter(NS + "rect"):
        x, y = num(el.get("x")), num(el.get("y"))
        w, h = num(el.get("width")), num(el.get("height"))
        if None in (x, y, w, h):
            continue          # the full-bleed background and the grid
        x, y, w, h = float(x), float(y), float(w), float(h)
        if w > 1300 or h > 400:
            continue          # frames and bands, intentionally full width
        out.append((x, y, x + w, y + h, el.get("fill", "")))
    return out


def overlap(a, b):
    ox = min(a[2], b[2]) - max(a[0], b[0])
    oy = min(a[3], b[3]) - max(a[1], b[1])
    return ox * oy if ox > 1.5 and oy > 1.5 else 0


def main():
    for f in sorted(glob.glob("*.svg")):
        root = ET.parse(f).getroot()
        W, H = float(root.get("width") or 0), float(root.get("height") or 0)
        t = boxes(f)
        hits = [h for i, a in enumerate(t) for h in [(overlap(a, b), a[4][:40], b[4][:40]) for b in t[i + 1:]] if h[0]]
        r = rects(f)
        rhits = [(overlap(a, b), f"rect {int(a[0])},{int(a[1])}", f"rect {int(b[0])},{int(b[1])}")
                 for i, a in enumerate(r) for b in r[i + 1:] if overlap(a, b)]
        offc = [x for x in t if x[0] < -1 or x[2] > W + 1] + [x for x in r if x[2] > W + 1 or x[3] > H + 1]
        print(f"{f}  text {len(hits)}  rect {len(rhits)}  off-canvas {len(offc)}")
        for h in sorted(hits + rhits, reverse=True)[:6]:
            print("    overlap", h)
        for x in offc[:6]:
            print("    off-canvas", x[4][:50] if len(x) > 4 else x)


if __name__ == "__main__":
    main()
