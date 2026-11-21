#!/usr/bin/env python3
"""Generate the educational GSTMCU schematics (SVG) in this directory.

Same rules as the 68000 set (`research/68000-silicon/schematics/`), so the two read as
one series:

  * 1440-unit wide canvas, light paper, no gradients, no shadows;
  * colour carries meaning: ADDRESS blue, DATA green, CONTROL amber, CLOCK violet,
    storage grey/violet; line weight encodes bus width;
  * every block is a rounded rectangle with a bold name and a small subtitle;
  * every sheet states its own provenance, and none of them claims die geometry.

Content is transcribed from the recovered design (`TA-4081` / `ST-4081` netlists and
sheets 1-33) and from `research/gstmcu-silicon-implementation.md`; see README.md for the
licence position. Nothing here is copied from the archive — the drawings are redrawn.

Run:  python3 build_schematics.py       (writes the SVGs, then audits label overflow)
"""
import os
import xml.etree.ElementTree as ET

W = 1440
INK, MUT, FAINT, BG, GRID = "#22232a", "#63666f", "#98a0ac", "#fbfaf6", "#e8e5db"
ADDR, DATA, CTRL, CLK, GREY, STORE = "#1f5fa8", "#2f7f5b", "#b4761a", "#8a4fb0", "#8a90a8", "#6f5aad"
F_ADDR, F_DATA, F_CTRL, F_CLK = "#e8eefa", "#e7f2ec", "#fdf1de", "#f2eaf8"
F_GREY, F_STORE = "#eef0f5", "#ece7f7"
FONT = "'Iosevka', 'JetBrains Mono', 'SF Mono', Menlo, Consolas, monospace"
SANS = "'Inter', 'Helvetica Neue', Arial, sans-serif"

_audit = []


def esc(s):
    return str(s).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def head(w, h, title, sub):
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}"
     font-family="{FONT}" role="img" aria-label="{esc(title)}">
<defs>
  <marker id="a-a" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="{ADDR}"/></marker>
  <marker id="a-d" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="{DATA}"/></marker>
  <marker id="a-c" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="{CTRL}"/></marker>
  <marker id="a-k" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="{CLK}"/></marker>
  <marker id="a-s" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="{STORE}"/></marker>
  <pattern id="grid" width="24" height="24" patternUnits="userSpaceOnUse"><path d="M24 0 L0 0 0 24" fill="none" stroke="{GRID}" stroke-width="1"/></pattern>
  <pattern id="hatch" width="8" height="8" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><path d="M0 0 L0 8" stroke="{CTRL}" stroke-width="1.4" opacity=".55"/></pattern>
</defs>
<rect width="100%" height="100%" fill="{BG}"/>
<rect width="100%" height="100%" fill="url(#grid)" opacity=".6"/>
<text x="44" y="44" font-size="25" font-weight="700" fill="{INK}" font-family="{SANS}">{esc(title)}</text>
<text x="44" y="71" font-size="14" fill="{MUT}" font-family="{SANS}">{esc(sub)}</text>
"""


def foot(h, lines):
    out = [f'<rect x="0" y="{h-46}" width="{W}" height="46" fill="#f4f1e8"/>']
    for i, line in enumerate(lines):
        out.append(f'<text x="44" y="{h-28+i*15}" font-size="11" fill="{MUT}" font-family="{SANS}">{esc(line)}</text>')
    return "\n".join(out) + "\n</svg>\n"


def box(x, y, w, h, name, sub="", stroke=CTRL, fill=F_CTRL, kind=None, tsize: float = 13, dashes=None):
    d = f' stroke-dasharray="{dashes}"' if dashes else ""
    o = [f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="{fill}" stroke="{stroke}" stroke-width="1.6"{d}/>']
    if sub:
        o.append(f'<text x="{x+w/2}" y="{y+h/2-4}" text-anchor="middle" font-size="{tsize}" font-weight="700" fill="{INK}">{esc(name)}</text>')
        o.append(f'<text x="{x+w/2}" y="{y+h/2+15}" text-anchor="middle" font-size="11" fill="{MUT}">{esc(sub)}</text>')
        _audit.append((tsize, name, w - 16))
        _audit.append((11, sub, w - 16))
    else:
        o.append(f'<text x="{x+w/2}" y="{y+h/2+5}" text-anchor="middle" font-size="{tsize}" font-weight="700" fill="{INK}">{esc(name)}</text>')
        _audit.append((tsize, name, w - 16))
    return "\n".join(o)


def text(x, y, s, size: float = 12, fill=INK, anchor="start", bold=False, italic=False, sans=False):
    fam = SANS if sans else FONT
    fw = ' font-weight="700"' if bold else ""
    fi = ' font-style="italic"' if italic else ""
    return f'<text x="{x}" y="{y}" font-size="{size}" fill="{fill}" text-anchor="{anchor}"{fw}{fi} font-family="{fam}">{esc(s)}</text>'


def line(x1, y1, x2, y2, color=CTRL, width=1.4, arrow=False, dashes=None):
    m = ' marker-end="url(#a-%s)"' % {"a": "a", "d": "d", "c": "c", "k": "k", "s": "s"}.get({ADDR: "a", DATA: "d", CTRL: "c", CLK: "k", STORE: "s"}.get(color, "c"), "c") if arrow else ""
    d = f' stroke-dasharray="{dashes}"' if dashes else ""
    return f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="{width}"{m}{d}/>'


def path(d, color=CTRL, width=1.4, arrow=False, fill="none", dashes=None):
    key = {ADDR: "a", DATA: "d", CTRL: "c", CLK: "k", STORE: "s"}.get(color, "c")
    m = f' marker-end="url(#a-{key})"' if arrow else ""
    da = f' stroke-dasharray="{dashes}"' if dashes else ""
    return f'<path d="{d}" fill="{fill}" stroke="{color}" stroke-width="{width}"{m}{da}/>'


def busbar(x, y, w, color, width=4.5, label=None, size: float = 10, anchor="middle"):
    o = [f'<line x1="{x}" y1="{y}" x2="{x+w}" y2="{y}" stroke="{color}" stroke-width="{width}"/>']
    if label:
        o.append(text(x + w / 2, y - 8, label, size, color, anchor="middle"))
    return "\n".join(o)


# ---------------------------------------------------------------- 00 · blocks
def sheet_blocks():
    h = 1010
    o = [head(W, h, "00 · The chip as blocks",
              "GSTMCU C300589-001 — what feeds what, from the design's own netlists and sheets. Functional, not to scale.")]

    # CPU bus side
    o.append(box(44, 120, 250, 92, "CPU / DMA bus interface", "A1–A23 · D0–D15 · AS · R/W · FC", ADDR, F_ADDR))
    o.append(box(44, 232, 250, 78, "Bus answer", "DTACK · BERR · VMA/VPA · BR/BGACK", CTRL, F_CTRL))
    o.append(box(44, 330, 250, 70, "Address decode", "sheet 3 · FFFXXX / FF8XXX / FF9XXX", CTRL, F_CTRL))
    o.append(box(44, 420, 250, 86, "Chip + register selects", "CARTSEL · JOYSEL · SNDCS · MFPCS …", CTRL, F_CTRL))

    # core column
    o.append(box(330, 120, 300, 92, "Phase generator · TGEN", "16 phases a window · TIME0–TIME7", CLK, F_CLK))
    o.append(box(330, 232, 300, 110, "Register bank", "RLC cells read through SAB sense bits", STORE, F_STORE))
    o.append(box(330, 362, 300, 96, "Address multiplexer", "ten MX41, one per MAD line", ADDR, F_ADDR))
    o.append(box(330, 478, 300, 96, "DRAM strobe generator", "RAS0/1 · CAS0/1 · WE · ROWB · LATCH", CTRL, F_CTRL))

    # owner equation
    o.append(f'<rect x="666" y="120" width="340" height="150" rx="8" fill="{F_CTRL}" stroke="{CTRL}" stroke-width="1.6"/>')
    o.append(text(686, 148, "Internal slot owner", 14, INK, bold=True))
    o.append(text(686, 172, "one rule, no ranking", 11, MUT))
    o.append(text(686, 196, "video    if DE is high", 12, INK))
    o.append(text(686, 216, "sound    else if SREQ", 12, INK))
    o.append(text(686, 236, "refresh  else, RAS only", 12, INK))
    o.append(text(686, 258, "the external slot: CPU or granted master", 10.5, MUT, italic=True))

    # DRAM side
    o.append(box(666, 300, 340, 80, "MAD0–MAD9 + RAS/CAS/WE", "multiplexed address to the four SIMMs", ADDR, F_ADDR))
    o.append(box(666, 400, 340, 88, "Refresh address chain", "ten stages · one row per 2 µs", CLK, F_CLK))
    o.append(box(666, 508, 340, 88, "Video fetch buffer", "one word per cycle · group of four", DATA, F_DATA))

    # right column: counters and the rest
    o.append(box(1030, 120, 366, 74, "Video address counter", "24 bits · VIDCLK, one step per cycle", DATA, F_DATA))
    o.append(box(1030, 204, 366, 74, "Sound address counter", "21 bits · SNDCLK on the granted slot", DATA, F_DATA))
    o.append(box(1030, 288, 366, 74, "Disk DMA address counter", "24 bits · RDY / DMADIR handshake", DATA, F_DATA))
    o.append(box(1030, 372, 366, 74, "Paddle divider", "eight taps → PADC at 500 kHz/8", CLK, F_CLK))
    o.append(box(1030, 456, 366, 74, "Pen position counters", "H from M2CLOCK · V from IHSYNC+VDE", CLK, F_CLK))
    o.append(box(1030, 540, 366, 74, "Sync + interrupt combiner", "HINT · VINT · IPL0–2 (74LS148 on chip)", CTRL, F_CTRL))

    # pads row
    o.append(box(44, 600, 250, 74, "Pad / pen / joypad port", "sheet 32: 32-bit select-and-buffer", DATA, F_DATA))
    o.append(box(44, 694, 250, 74, "Clocks out", "CLK8 · CLK4 · CLK2 · KHZ500", CLK, F_CLK))
    o.append(box(44, 788, 250, 74, "Reset + test", "PORB · LRESB · TEST tied inactive", GREY, F_GREY))

    # arrows
    o.append(line(294, 166, 330, 166, ADDR, 4, arrow=True))
    o.append(line(294, 367, 330, 367, CTRL, 1.6, arrow=True))
    o.append(line(294, 280, 330, 280, CTRL, 1.6, arrow=True))
    o.append(line(630, 260, 666, 260, STORE, 1.6, arrow=True))
    o.append(line(630, 400, 666, 340, ADDR, 4, arrow=True))
    o.append(line(630, 520, 666, 465, CLK, 1.6, arrow=True))
    o.append(line(1006, 340, 1030, 160, DATA, 1.6, arrow=True, dashes="5 4"))
    o.append(line(1006, 526, 1030, 495, CLK, 1.6, arrow=True, dashes="5 4"))
    o.append(line(480, 212, 480, 232, CLK, 1.6, arrow=True))
    o.append(line(480, 342, 480, 362, STORE, 1.6, arrow=True))

    o.append(text(44, 900, "Reading order: the phase generator makes the window; the decode makes an address; the mux picks an owner;",
                  12, MUT, italic=True))
    o.append(text(44, 920, "the strobe generator turns that into RAS/CAS; everything on the right is a counter that decides who asks next.",
                  12, MUT, italic=True))
    o.append(foot(h, ["Provenance: redrawn from the TA-4081 / ST-4081 netlists and sheets 1–33, and from research/gstmcu-silicon-implementation.md.",
                      "No die geometry is implied or used: this is the design's structure, not its layout."]))
    return "".join(o)


# ---------------------------------------------------------------- 01 · cycle
PHASES = 16
X0, X1 = 300, 1380
PW = (X1 - X0) / PHASES


def wave(y, runs, color=CTRL, width=2.2, dashes=None):
    """runs: list of (level, from_phase, to_phase); level 1 high, 0 low. Phases are integers."""
    pts = []
    for lvl, a, b in runs:
        yy = y - 14 if lvl else y
        if pts:
            pts.append((X0 + a * PW, yy))
        else:
            pts.append((X0 + a * PW, yy))
        pts.append((X0 + b * PW, yy))
    d = "M " + " L ".join(f"{x:.1f},{yy:.1f}" for x, yy in pts)
    return path(d, color, width, dashes=dashes)


def sheet_cycle():
    h = 820
    o = [head(W, h, "01 · One cycle — 500 ns, sixteen phases",
              "The slot grid as sheet 48 draws it, with the strobes the netlist names. One phase = 31.25 ns.")]

    # slot bands
    o.append(f'<rect x="{X0}" y="104" width="{8*PW}" height="44" fill="{F_CLK}" stroke="{CLK}" stroke-width="1.2"/>')
    o.append(f'<rect x="{X0+8*PW}" y="104" width="{8*PW}" height="44" fill="{F_CTRL}" stroke="{CTRL}" stroke-width="1.2"/>')
    o.append(text(X0 + 4 * PW, 126, "internal slot — video / sound / refresh", 12, INK, anchor="middle", bold=True))
    o.append(text(X0 + 12 * PW, 126, "external slot — CPU, or the granted master", 12, INK, anchor="middle", bold=True))
    o.append(text(X0 + 4 * PW, 142, "ADDRSELB high · REF decides video vs sound vs refresh", 10.5, MUT, anchor="middle", italic=True))
    o.append(text(X0 + 12 * PW, 142, "ADDRSEL low · IXDMAB picks CPU or DMA", 10.5, MUT, anchor="middle", italic=True))

    # phase grid
    for i in range(PHASES + 1):
        x = X0 + i * PW
        o.append(line(x, 168, x, 560, GRID, 1))
        if i < PHASES:
            o.append(text(x + PW / 2, 184, str(i), 11, MUT, anchor="middle"))

    rows = [
        ("RAS0", [(0, 0, 5), (1, 5, 8), (0, 8, 13), (1, 13, 16)], CTRL, 226),
        ("CAS0", [(1, 0, 2), (0, 2, 7), (1, 7, 10), (0, 10, 15), (1, 15, 16)], CTRL, 286),
        ("COL/ROW", [(0, 0, 1), (1, 1, 5), (0, 5, 9), (1, 9, 13), (0, 13, 16)], ADDR, 346),
        ("LATCH", [(0, 0, 10), (1, 10, 14), (0, 14, 16)], STORE, 406),
        ("DCYC", [(1, 0, 2), (0, 2, 6), (1, 6, 8)], DATA, 466),
        ("SLOAD", [(1, 0, 2), (0, 2, 6), (1, 6, 8)], GREY, 526),
    ]
    for name, runs, color, y in rows:
        o.append(text(X0 - 16, y - 2, name, 12.5, INK, anchor="end", bold=True))
        o.append(line(X0, y + 6, X1, y + 6, GRID, 1, dashes="3 5"))
        o.append(wave(y, runs, color, dashes="5 4") if color == GREY else wave(y, runs, color))
    o.append(text(X0 + 12 * PW, 470, "neither strobe belongs to the external half", 10.5, MUT, anchor="middle", italic=True))
    o.append(text(X0 + 12 * PW, 530, "SLOAD is the sound lane's: this window is video's", 10.5, MUT, anchor="middle", italic=True))

    # annotations
    o.append(line(X0 + 5 * PW, 600, X0 + 7 * PW, 600, INK, 1.2, arrow=True))
    o.append(line(X0 + 7 * PW, 600, X0 + 5 * PW, 600, INK, 1.2, arrow=True))
    o.append(text(X0 + 6 * PW, 594, "tRCD 62.5 ns", 11, INK, anchor="middle", bold=True))
    o.append(line(X0, 630, X0 + 8 * PW, 630, CLK, 3))
    o.append(text(X0 + 4 * PW, 648, "slot 250 ns · 8 phases", 11, CLK, anchor="middle", bold=True))
    o.append(line(X0 + 8 * PW, 630, X1, 630, CTRL, 3))
    o.append(text(X0 + 12 * PW, 648, "slot 250 ns · 8 phases", 11, CTRL, anchor="middle", bold=True))

    # side notes
    o.append(f'<rect x="44" y="686" width="1340" height="76" rx="8" fill="{F_CTRL}" stroke="{CTRL}" stroke-width="1.4"/>')
    o.append(text(64, 712, "What each strobe belongs to", 12.5, INK, bold=True))
    o.append(text(64, 732, "CAS is per bank and VOS-qualified — which is why a refresh cycle has no CAS at all.  LATCH fires once, in the external half: the read/write data latch.", 11.5, MUT))
    o.append(text(64, 750, "DCYC and SLOAD are transparent-latch sample-and-holds of their decode, not resynchronisers — their edge is the decode's edge, one open window later.", 11.5, MUT))
    o.append(text(44, 600, "RAS falls at phase 0 of each slot; CAS two phases later;", 11.5, MUT, italic=True))
    o.append(text(44, 616, "COL/ROW is low while RAS falls, so low = row address.", 11.5, MUT, italic=True))
    o.append(foot(h, ["Provenance: levels transcribed from sheet 48 (MHZ16 numbers 0–15) and the strobe equations of sheets 21 and 22.",
                      "The phase width (31.25 ns) follows from CAS being a pure decode of TIME2 meeting the chart's two-number gap."]))
    return "".join(o)


# ---------------------------------------------------------------- 02 · line
def sheet_line():
    h = 640
    o = [head(W, h, "02 · One line — 512 clocks, 128 internal slots",
              "The line's slot budget in low resolution: 80 internal slots to video, 48 to blanking, and what fits in the 48.")]
    bx0, bx1 = 120, 1320
    bw = (bx1 - bx0) / 128
    # bar
    o.append(f'<rect x="{bx0}" y="180" width="{80*bw}" height="72" fill="{F_DATA}" stroke="{DATA}" stroke-width="1.6"/>')
    o.append(f'<rect x="{bx0+80*bw}" y="180" width="{48*bw}" height="72" fill="{F_GREY}" stroke="{GREY}" stroke-width="1.6"/>')
    o.append(text(bx0 + 40 * bw, 212, "video — one word per cycle, 80 fetches", 13, INK, anchor="middle", bold=True))
    o.append(text(bx0 + 40 * bw, 234, "a group of four words delivered on every fourth DCYC", 11, MUT, anchor="middle"))
    o.append(text(bx0 + 104 * bw, 212, "blanking — 48 slots", 13, INK, anchor="middle", bold=True))
    o.append(text(bx0 + 104 * bw, 234, "sound ≤ 3.2 · refresh ≤ 32", 11, MUT, anchor="middle"))

    # refresh hatch inside blanking (first 32 slots)
    o.append(f'<rect x="{bx0+80*bw}" y="252" width="{32*bw}" height="26" fill="url(#hatch)" stroke="{CTRL}" stroke-width="1.2"/>')
    o.append(text(bx0 + 96 * bw, 270, "refresh — the address advances every 2 µs", 11, INK, anchor="middle"))

    # DE and HBLANK brackets
    o.append(line(bx0, 164, bx0 + 80 * bw, 164, DATA, 2.4))
    o.append(text(bx0 + 40 * bw, 156, "DE high — the Shifter's window", 11, DATA, anchor="middle", bold=True))
    o.append(line(bx0 + 80 * bw, 164, bx1, 164, GREY, 2.4))
    o.append(text(bx0 + 104 * bw, 156, "HBLANK", 11, MUT, anchor="middle", bold=True))

    # scale
    o.append(line(bx0, 320, bx1, 320, INK, 1.2))
    for i in range(0, 129, 16):
        x = bx0 + i * bw
        o.append(line(x, 314, x, 326, INK, 1.2))
        o.append(text(x, 342, str(i), 10, MUT, anchor="middle"))
    o.append(text(bx0 + 64 * bw, 362, "internal slot index · one slot per 500 ns cycle", 11, MUT, anchor="middle", italic=True))

    # the three numbers
    o.append(box(120, 390, 380, 96, "64 µs a line", "512 CPU clocks = 256 slots, half internal", CLK, F_CLK, tsize=14))
    o.append(box(530, 390, 380, 96, "80 of 128 slots are video's", "20 words × 4 planes = 80 · 40 × 2 mid · 40 mono", DATA, F_DATA, tsize=14))
    o.append(box(940, 390, 380, 96, "the 48 left are blanking's", "≈2 refreshes and ≤3.2 sound words used, the rest idle", CTRL, F_CTRL, tsize=14))

    o.append(text(120, 522, "A frame is 313 such lines: 313 × 512 clocks × 125 ns = 20.03 ms, which is 49.9 Hz.", 12.5, INK))
    o.append(text(120, 544, "The vertical blanking interval is not a special case: the window closes and its slots become blanket refresh and sound.", 12, MUT, italic=True))
    o.append(foot(h, ["Provenance: arithmetic from the design's counters and the sound memo; slot counts from research/gstmcu-silicon-implementation.md §5.2.",
                      "Refresh's ≤32 is the 2 µs address clock meeting a 64 µs line, not a scheduled budget."]))
    return "".join(o)


# ---------------------------------------------------------------- 03 · address
def sheet_address():
    h = 860
    o = [head(W, h, "03 · The address path — four owners, ten muxes",
              "Every MAD line is a 4:1 choice between the CPU/DMA bus, the video address, the sound address and the refresh row.")]

    o.append(box(44, 130, 250, 84, "A0–A23 · the bus", "CPU, or the DMA master holding it", ADDR, F_ADDR))
    o.append(box(44, 232, 250, 84, "VA — video address", "after the WLOC line-offset muxes", DATA, F_DATA))
    o.append(box(44, 334, 250, 84, "MA — sound address", "sheet 17 · 21-bit counter", DATA, F_DATA))
    o.append(box(44, 436, 250, 84, "RA0–RA9 — refresh row", "sheet 11 · ten stages on REFCLKB", CLK, F_CLK))

    # mux column
    o.append(f'<rect x="360" y="130" width="250" height="390" rx="8" fill="{F_ADDR}" stroke="{ADDR}" stroke-width="1.6"/>')
    o.append(text(485, 158, "ten MX41 muxes", 15, INK, anchor="middle", bold=True))
    o.append(text(485, 176, "one per MAD line", 11, MUT, anchor="middle"))
    o.append(text(485, 300, "A  ▸  MADn", 12, INK, anchor="middle"))
    o.append(text(485, 324, "VA ▸", 12, INK, anchor="middle"))
    o.append(text(485, 348, "MA ▸", 12, INK, anchor="middle"))
    o.append(text(485, 372, "RA ▸", 12, INK, anchor="middle"))
    o.append(text(485, 410, "selects: REF · SADSEL", 10.5, MUT, anchor="middle"))
    o.append(text(485, 426, "IXDMAB · ADDRSELB", 10.5, MUT, anchor="middle"))

    o.append(box(660, 130, 250, 84, "MAD0–MAD9 to the SIMMs", "row address on RAS, column on CAS", ADDR, F_ADDR))
    o.append(box(660, 232, 250, 84, "RAS0 / RAS1", "one per bank, one per slot", CTRL, F_CTRL))
    o.append(box(660, 334, 250, 84, "CAS0 / CAS1", "= NAND(TIME2, LCYCSELB, RAMxA, VOS)", CTRL, F_CTRL))
    o.append(box(660, 436, 250, 84, "WE · ROWB · LATCH", "write · row/column · data latch", CTRL, F_CTRL))

    for y in (172, 274, 376, 478):
        o.append(line(294, y, 360, y, ADDR, 2, arrow=True))
    o.append(line(610, 172, 660, 172, ADDR, 4, arrow=True))
    o.append(line(610, 250, 660, 274, CTRL, 1.6, arrow=True, dashes="4 4"))
    o.append(line(610, 300, 660, 376, CTRL, 1.6, arrow=True, dashes="4 4"))

    # the printed table, redrawn
    o.append(f'<rect x="946" y="130" width="450" height="196" rx="8" fill="{F_CTRL}" stroke="{CTRL}" stroke-width="1.6"/>')
    o.append(text(966, 154, "the designer's table, sheet 23", 13, INK, bold=True))
    o.append(text(966, 172, "read: ADDRSELB = 1 is the internal slot", 10.5, MUT))
    tbl = [("IXDMAB", "SADSEL", "REF", "ADDRSELB", "owner"),
           ("X", "X", "1", "1", "REFRESH"),
           ("X", "1", "0", "1", "SOUND"),
           ("X", "0", "0", "1", "VIDEO"),
           ("1", "X", "X", "0", "CPU"),
           ("0", "X", "X", "0", "DMA")]
    for i, row in enumerate(tbl):
        y = 198 + i * 22
        for j, cell in enumerate(row):
            o.append(text(970 + j * 74, y, cell, 11.5 if i else 10.5, INK if i else MUT,
                          anchor="start", bold=(i == 0)))
        if i:
            o.append(line(966, y + 5, 1376, y + 5, GRID, 1))

    o.append(f'<rect x="946" y="346" width="450" height="182" rx="8" fill="{F_DATA}" stroke="{DATA}" stroke-width="1.6"/>')
    o.append(text(966, 370, "where each input comes from", 13, INK, bold=True))
    o.append(text(966, 388, "the cross-reference tails on sheet 25", 10.5, MUT))
    for i, row in enumerate(["RA9  ← sheet 11   the refresh chain's taps",
                             "VA9  ← sheet 11   after the WLOC offset muxes",
                             "MA9  ← sheet 17   the sound address counter",
                             "A9   ← sheet 19   the CPU/DMA bus"]):
        o.append(text(966, 416 + i * 22, row, 11.5, INK))
    o.append(text(966, 508, "four inputs, because two lanes share one: on an external", 10.5, MUT, italic=True))
    o.append(text(966, 522, "slot the granted master is already driving the bus.", 10.5, MUT, italic=True))

    o.append(text(44, 580, "The table is a decode, not a priority: REFB and SNDEN are produced by one gate group, so the first two arms",
                  12, INK))
    o.append(text(44, 600, "cannot both be live — the internal slot is video's, else sound's, else the RAS-only refresh.",
                  12, INK))
    o.append(text(44, 636, "Ten taps = 1 024 states, and 512 rows per bank is half of that.  Tap 1 is RA9 and tap 10 is RA0,", 12, MUT, italic=True))
    o.append(text(44, 656, "so the walk starts at the last stage — the order a refresh row sequence follows.", 12, MUT, italic=True))
    o.append(foot(h, ["Provenance: sheet 25 read at the MUX inputs, sheet 23's printed table, sheet 11's chain; the CAS form from sheets 21–22.",
                      "No archive drawing is reproduced: the table and the arrows are redrawn from the netlist's connectivity."]))
    return "".join(o)


# ---------------------------------------------------------------- 04 · counters
def sheet_counters():
    h = 880
    o = [head(W, h, "04 · Clocks and counters — what counts, and on which edge",
              "One oscillator, one ÷2 ladder, one phase generator; then seven counting structures, each with its own clock.")]

    # divider ladder
    o.append(box(44, 120, 210, 62, "16 MHz", "the master oscillator", CLK, F_CLK, tsize=14))
    o.append(box(300, 120, 210, 62, "MHZ8", "÷2 · the CPU clock", CLK, F_CLK, tsize=14))
    o.append(box(556, 120, 210, 62, "MHZ4 = M2CLOCK", "÷2 · the peripheral clock", CLK, F_CLK, tsize=13))
    o.append(box(812, 120, 210, 62, "MHZ2", "÷2", CLK, F_CLK, tsize=14))
    o.append(box(1068, 120, 328, 62, "K500CLK · KHZ500", "÷2 ÷2 · 500 kHz, and the clocks out", CLK, F_CLK, tsize=13))
    for x in (254, 510, 766, 1022):
        o.append(line(x, 151, x + 46, 151, CLK, 2, arrow=True))

    # TGEN
    o.append(box(300, 224, 300, 96, "TGEN · the phase ring", "hand-designed macro cell, not library gates", CLK, F_CLK, tsize=14))
    o.append(line(405, 182, 405, 224, CLK, 2, arrow=True))
    o.append(box(660, 224, 340, 96, "TIME0 … TIME7", "eight outputs, one phase apart, a slot wide", CLK, F_CLK, tsize=14))
    o.append(line(600, 272, 660, 272, CLK, 2, arrow=True))
    o.append(text(660, 336, "One phase = 31.25 ns = one half of a 16 MHz clock; sixteen to a 500 ns window.", 11.5, MUT, italic=True))

    # counters table-as-diagram
    rows = [
        ("refresh row", "REFCLKB · one row per 2 µs", "ten stages · RA9 … RA0 into the muxes", CLK, F_CLK),
        ("video address", "VIDCLK · one per 500 ns cycle while DE is high", "24 bits · VCOUNT plus VIDBH/M/L", DATA, F_DATA),
        ("sound address", "SNDCLK · the granted internal slot", "21 bits · SFTH/M/L, compared against frame top", DATA, F_DATA),
        ("disk DMA address", "RDY / DMADIR from the DMA controller", "24 bits · DMAH/M/L", DATA, F_DATA),
        ("paddle", "PADC · 500 kHz through an eight-tap divider", "four 8-bit results, 0–255, read via the port", CTRL, F_CTRL),
        ("pen — horizontal", "M2CLOCK, gated by PX033/031/032; cleared by IHSYNC", "captured into MF2 cells on the pen's edge", CTRL, F_CTRL),
        ("pen — vertical", "IHSYNC + VDE logic; cleared by IVSYNC", "captured into MF2 cells on the pen's edge", CTRL, F_CTRL),
    ]
    for i, (name, clk, width, color, fill) in enumerate(rows):
        y = 368 + i * 56
        o.append(f'<rect x="44" y="{y}" width="700" height="46" rx="7" fill="{fill}" stroke="{color}" stroke-width="1.4"/>')
        o.append(text(60, y + 20, name, 12.5, INK, bold=True))
        o.append(text(60, y + 37, clk, 10.5, MUT))
        o.append(text(520, y + 27, width, 10.5, MUT))

    # refresh clock chain
    o.append(f'<rect x="800" y="368" width="596" height="124" rx="8" fill="{F_CLK}" stroke="{CLK}" stroke-width="1.6"/>')
    o.append(text(820, 390, "REFCLK — how the refresh clock is made", 13, INK, bold=True))
    o.append(text(820, 406, "sheet 27, with K500CLK buffered locally", 10.5, MUT))
    o.append(text(820, 426, "NAND(HCTR, LC) → XY2, mixed with K500CLK → a flop whose output is REFCLK", 11.5, INK))
    o.append(text(820, 444, "HCTR = M2CLOCK ÷ 64 (16 µs); LC is a one-clock marker from the TIME pipeline", 11.5, INK))
    o.append(text(820, 462, "REFCLKB = NAND(SNDEN, REFCLK) on sheet 20 — parked high while sound fetches", 11.5, INK))
    o.append(text(820, 480, "Result: one row address per 2 µs; 1 024 states in 2.05 ms; a bank in 1.02 ms", 11.5, INK, bold=True))

    # not modelled
    o.append(f'<rect x="800" y="504" width="596" height="120" rx="8" fill="{F_GREY}" stroke="{GREY}" stroke-width="1.4"/>')
    o.append(text(820, 530, "Not modelled (ruling, 2026-11-21)", 13, INK, bold=True))
    o.append(text(820, 556, "interlace — the genlock sync parity behind $FF820A bit 0", 11.5, MUT))
    o.append(text(820, 576, "TEST — tied inactive on the board, no behaviour to model", 11.5, MUT))
    o.append(text(820, 596, "BINT — purpose unknown to two independent readings", 11.5, MUT))

    o.append(text(44, 800, "Reading this sheet: every counter above is a chain of gates clocked by the net named beside it; none of them is a datapath that can",
                  12, MUT, italic=True))
    o.append(text(44, 818, "be made to count, which is the structural difference from the 68000 whose counters live under microcode.",
                  12, MUT, italic=True))
    o.append(foot(h, ["Provenance: sheet 7's ÷2 ladder, sheet 27's REFCLK cluster, sheets 11/15/16/17/31/33's counters.",
                      "Widths and clocks are read from the netlists; the pen's capture is from sheet 33 read cell by cell."]))
    return "".join(o)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    sheets = {"00-blocks.svg": sheet_blocks,
              "01-cycle.svg": sheet_cycle,
              "02-line.svg": sheet_line,
              "03-address.svg": sheet_address,
              "04-counters.svg": sheet_counters}
    for name, fn in sheets.items():
        svg = fn()
        ET.fromstring(svg)                      # well-formed, or raise here
        with open(os.path.join(here, name), "w", encoding="utf-8") as f:
            f.write(svg)
        print(f"{name}:  {len(svg):6d} bytes")

    # label audit: conservative 0.60 em per character
    over = []
    for size, label, room in _audit:
        need = len(label) * size * 0.60
        if need > room:
            over.append((size, label, need, room))
    print(f"{len(over)} layout warnings")
    for size, s, need, room in over:
        print(f"  overflow: {s!r} needs {need:.0f}px in {room:.0f}px (size {size})")


if __name__ == "__main__":
    main()
