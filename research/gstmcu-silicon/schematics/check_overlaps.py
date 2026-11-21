"""Estimate text bounding boxes in the generated SVGs and report collisions."""
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


def main():
    for f in sorted(glob.glob("*.svg")):
        items = boxes(f)
        hits = []
        for i in range(len(items)):
            for j in range(i + 1, len(items)):
                a, b = items[i], items[j]
                ox = min(a[2], b[2]) - max(a[0], b[0])
                oy = min(a[3], b[3]) - max(a[1], b[1])
                if ox > 1.5 and oy > 1.5:
                    hits.append((round(ox * oy), a[4][:44], b[4][:44]))
        print(f, "overlaps:", len(hits))
        for h in sorted(hits, reverse=True)[:8]:
            print("   ", h)


if __name__ == "__main__":
    main()
