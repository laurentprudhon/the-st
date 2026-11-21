#!/usr/bin/env python3
"""Generate the educational MC68000 schematics (SVG) in this directory.

Design rules, so the sheets read as one set:
  * 1440-unit wide canvas, light paper, no gradients, no shadows;
  * path colours carry meaning everywhere: ADDRESS blue, DATA green,
    CONTROL amber, CLOCK violet, storage grey/violet;
  * every block is a rounded rectangle with a bold name and a small subtitle;
  * line weight encodes bus width (1 lane / 16 bits / 32 bits).

Run:  python3 build_schematics.py      (writes 00..04 .svg, then audits them)
"""
import os
import re

W = 1440
INK = "#22232a"
MUT = "#63666f"
FAINT = "#98a0ac"
BG = "#fbfaf6"
GRID = "#e8e5db"
ADDR = "#1f5fa8"
DATA = "#2f7f5b"
CTRL = "#b4761a"
CLK = "#8a4fb0"
GREY = "#8a90a8"
STORE = "#6f5aad"
FILL_ADDR, FILL_DATA, FILL_CTRL, FILL_CLK = "#e8eefa", "#e7f2ec", "#fdf1de", "#f2eaf8"
FILL_GREY, FILL_STORE = "#eef0f5", "#ece7f7"

FONT = "'Iosevka', 'JetBrains Mono', 'SF Mono', Menlo, Consolas, monospace"
SANS = "'Inter', 'Helvetica Neue', Arial, sans-serif"

_audit = []          # (width, x, y, text) for the overflow check


def head(w, h, title, sub):
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}"
     font-family="{FONT}" role="img" aria-label="{title}">
<defs>
  <marker id="a-a" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
    <path d="M0,0 L10,5 L0,10 z" fill="{ADDR}"/></marker>
  <marker id="a-d" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
    <path d="M0,0 L10,5 L0,10 z" fill="{DATA}"/></marker>
  <marker id="a-c" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
    <path d="M0,0 L10,5 L0,10 z" fill="{CTRL}"/></marker>
  <marker id="a-k" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
    <path d="M0,0 L10,5 L0,10 z" fill="{CLK}"/></marker>
  <marker id="a-s" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
    <path d="M0,0 L10,5 L0,10 z" fill="{STORE}"/></marker>
  <pattern id="grid" width="24" height="24" patternUnits="userSpaceOnUse">
    <path d="M24 0 L0 0 0 24" fill="none" stroke="{GRID}" stroke-width="1"/></pattern>
</defs>
<rect width="100%" height="100%" fill="{BG}"/>
<rect width="100%" height="100%" fill="url(#grid)" opacity=".6"/>
<text x="44" y="44" font-size="25" font-weight="700" fill="{INK}" font-family="{SANS}">{title}</text>
<text x="44" y="71" font-size="14" fill="{MUT}" font-family="{SANS}">{sub}</text>
"""


def tw(s, size: float) -> float:
    return len(s) * size * 0.60


def esc(s: str) -> str:
    """XML-escape text content (a bare & or < in a label is fatal to an SVG parser)."""
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def box(x, y, w, h, name, sub="", kind="unit", fill=None, stroke=None, dashed=False,
        rx=8, name_size: float = 15, sub_size: float = 11.5):
    fills = {"unit": FILL_GREY, "addr": FILL_ADDR, "data": FILL_DATA, "ctrl": FILL_CTRL,
             "clk": FILL_CLK, "store": FILL_STORE}
    strokes = {"unit": GREY, "addr": ADDR, "data": DATA, "ctrl": CTRL, "clk": CLK, "store": STORE}
    f = fill or fills.get(kind, FILL_GREY)
    s = stroke or strokes.get(kind, GREY)
    dash = ' stroke-dasharray="7 5"' if dashed else ""
    out = (f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{f}" stroke="{s}" '
           f'stroke-width="1.6"{dash}/>')
    lines = name.split("\n")
    n = len(lines)
    total = n * (name_size + 4) + (sub_size + 6 if sub else 0)
    cy = y + h / 2 - total / 2 + name_size
    for i, ln in enumerate(lines):
        out += (f'<text x="{x + w / 2}" y="{cy + i * (name_size + 4)}" text-anchor="middle" '
                f'font-size="{name_size}" font-weight="700" fill="{INK}">{esc(ln)}</text>')
        _audit.append((tw(ln, name_size), x + w / 2, cy + i * (name_size + 4), ln, w))
    if sub:
        sy = cy + (n - 1) * (name_size + 4) + sub_size + 8
        out += (f'<text x="{x + w / 2}" y="{sy}" text-anchor="middle" font-size="{sub_size}" '
                f'fill="{MUT}">{esc(sub)}</text>')
        _audit.append((tw(sub, sub_size), x + w / 2, sy, sub, w))
    return out


def txt(x, y, s, size: float = 13, fill=MUT, anchor="start", weight="400", family=None, style="",
        rotate=None):
    fam = f' font-family="{family}"' if family else ""
    rot = f' transform="rotate(-90 {x} {y})"' if rotate else ""
    if anchor == "start":
        _audit.append((tw(s, size), x + tw(s, size) / 2, y, s, None))
    return (f'<text x="{x}" y="{y}" text-anchor="{anchor}" font-size="{size}" fill="{fill}" '
            f'font-weight="{weight}"{fam}{style}{rot}>{esc(s)}</text>')


def line(x1, y1, x2, y2, color=ADDR, width: float = 2.0, marker=None, dash=None):
    m = f' marker-end="url(#{marker})"' if marker else ""
    d = f' stroke-dasharray="{dash}"' if dash else ""
    return (f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="{width}" '
            f'stroke-linecap="round"{m}{d}/>')


def path(d, color=ADDR, width: float = 2.0, marker=None, dash=None, fill="none"):
    m = f' marker-end="url(#{marker})"' if marker else ""
    da = f' stroke-dasharray="{dash}"' if dash else ""
    return (f'<path d="{d}" fill="{fill}" stroke="{color}" stroke-width="{width}" stroke-linecap="round" '
            f'stroke-linejoin="round"{m}{da}/>')


def bus(x1, y1, x2, y2, color, bits="16", label=None, lx=None, ly=None, dash=None, marker=None):
    w = {"32": 11, "16": 8, "10": 6, "1": 4}.get(bits, 6)
    out = line(x1, y1, x2, y2, color, w, dash=dash)
    out += line(x1, y1, x2, y2, BG, 2.4 if bits != "1" else 1, dash=dash)
    if marker:
        out += line(x1, y1, x2, y2, color, 1.6, marker=marker)
    if label:
        out += txt(lx if lx is not None else (x1 + x2) / 2,
                   ly if ly is not None else (y1 + y2) / 2 - 12,
                   label, 11, color, "middle", "700")
    return out


def legend(x, y, items, title="LEGEND"):
    out = txt(x, y, title, 11, MUT, weight="700")
    yy = y + 20
    for color, label in items:
        out += line(x, yy - 4, x + 28, yy - 4, color, 6)
        out += txt(x + 38, yy, label, 11.5, MUT)
        yy += 18
    return out


def footer(y, s):
    return txt(44, y, s, 11.5, FAINT, family=SANS, style=' font-style="italic"')


# ------------------------------------------------------------------ sheet 00
def floorplan():
    w, h = W, 980
    s = head(w, h, "00 · The die at a glance",
             "Where the parts sit on the silicon — drawn in the orientation of the 1983 labelled die photograph, not to scale.")
    s += f'<rect x="26" y="96" width="{w - 52}" height="{h - 150}" rx="12" fill="none" stroke="{FAINT}" stroke-width="1.8" stroke-dasharray="10 7"/>'
    s += txt(36, h - 38, "bond pads (64 pins) around the whole perimeter", 11.5, FAINT)

    s += box(44, 120, 66, 300, "INT\nLOG", "interrupt", "ctrl", name_size=12, sub_size=10)
    s += box(44, 436, 66, 140, "FC\nLOG", "func code", "ctrl", name_size=12, sub_size=10)
    s += box(44, 592, 66, 110, "BUF", "inputs", "unit", name_size=12, sub_size=10)

    s += box(128, 120, 320, 66, "CLOCK GENERATORS · TIMER & E", "φ1 / φ2 and the E divider", "clk", name_size=13)
    s += box(128, 196, 320, 50, "DECODE DRIVERS", "phase fan-out to every block", "clk", name_size=13)

    s += box(128, 268, 320, 168, "µROM", "microcode · 544 × 17", "store", name_size=19)
    s += box(128, 452, 320, 236, "NROM", "nanocode · 336 × 68", "store", name_size=19)

    s += box(476, 120, 290, 62, "µP CONTROL LOGIC", "bus state machine S0–S7", "clk", name_size=13)
    s += box(790, 120, 250, 62, "BUS CONTROL", "DTACK · BERR · arbitration", "clk", name_size=13)
    s += box(476, 196, 290, 52, "A0 MUX CONTROL", "exception address override", "ctrl", name_size=13)
    s += box(790, 196, 250, 52, "A1 DECODE", "opcode → entry point", "ctrl", name_size=13)
    s += box(790, 260, 250, 52, "A2 / A3 DECODE", "2nd and 3rd entry points", "ctrl", name_size=13)
    s += box(790, 324, 250, 50, "BRANCH PLA", "C1/C0 from the condition", "ctrl", name_size=13)
    s += box(790, 386, 250, 50, "TRAP & ILL. PLA", "vectors · illegal · priv", "ctrl", name_size=13)
    s += box(790, 448, 250, 50, "IRD REG", "the opcode in execution", "ctrl", name_size=13)

    s += box(476, 540, 564, 76, "IRD BUS — the decode PLA bank (not a bus)",
             "~70 control bits from the two ROMs  →  ~200 datapath control lines", "ctrl", name_size=15)
    s += box(476, 632, 564, 46, "ALU CONTROL", "15 rows × 5 columns matrix", "ctrl", name_size=14)

    s += box(128, 724, 306, 128, "ADDRESS HIGH", "high words + AUH", "data", name_size=14)
    s += box(444, 724, 306, 128, "ADDRESS LOW", "low words + AUL", "data", name_size=14)
    s += box(760, 724, 306, 128, "DATA", "low words + the ALU", "data", name_size=14)
    s += box(128, 872, 306, 34, "ADDRESS BUFFERS → A1–A23", "", "addr", name_size=12)
    s += box(1236, 500, 92, 380, "DATA\nBUFFER\nD0–D15", "", "data", name_size=11.5)

    s += bus(448, 352, 476, 352, CTRL, "10", "10-bit address", 462, 340, marker="a-c")
    s += bus(448, 570, 476, 570, CTRL, "16", "68 + 17 bits", 462, 558, marker="a-c")
    s += bus(300, 688, 300, 720, CTRL, "16", "", dash="4 4", marker="a-c")
    s += txt(308, 706, "control lines down", 11, CTRL)
    s += bus(900, 696, 900, 720, CTRL, "16", "", dash="4 4", marker="a-c")
    s += bus(1066, 788, 1234, 788, DATA, "16", "16", 1150, 776, marker="a-d")
    s += bus(128, 788, 96, 788, ADDR, "32", "", marker="a-a")

    s += legend(1070, 120, [(CLK, "clock / sequencing"), (CTRL, "control and decode"),
                            (STORE, "control store (ROM)"), (DATA, "data path"),
                            (ADDR, "address path"), (GREY, "buffers / pads")])
    s += txt(1070, 256, "Three bands to remember", 12.5, INK, weight="700")
    for i, t in enumerate(["top — clocks, bus control, opcode decode",
                           "middle — the two control stores",
                           "bottom — the execution unit"]):
        s += txt(1070, 280 + i * 19, "· " + t, 11.5, MUT)
    s += txt(1070, 372, "The photograph also names separate", 11.5, MUT)
    s += txt(1070, 390, "A0 / A1 / A2-A3 decoders and a", 11.5, MUT)
    s += txt(1070, 408, "TIMER & E LOGIC block; modern work", 11.5, MUT)
    s += txt(1070, 426, "confirms the roles, not the exact", 11.5, MUT)
    s += txt(1070, 444, "boundaries between them.", 11.5, MUT)
    s += footer(h - 12, "MC68000 · educational block schematic · not a circuit diagram")
    return s + "</svg>\n"


# ------------------------------------------------------------------ sheet 01
def datapath():
    w, h = W, 900
    s = head(w, h, "01 · The execution unit — three sections, two buses",
             "Every 32-bit register is split across sections. Each section carries an address lane and a data lane, so six 16-bit transfers can happen at once.")
    top, bot = 148, 700
    for x, name, sub in [(80, "HIGH", "upper 16 bits of every 32-bit register · AUH"),
                         (490, "LOW", "lower 16 bits of A0–A7, PC, AT · AUL"),
                         (900, "DATA", "lower 16 bits of D0–D7 · the ALU")]:
        s += (f'<rect x="{x}" y="{top - 46}" width="390" height="{bot - top + 40}" rx="14" fill="#ffffff" '
              f'fill-opacity=".5" stroke="{FAINT}" stroke-width="1.4"/>')
        s += txt(x + 195, top - 20, name + " SECTION", 13.5, INK, "middle", "700")
        s += txt(x + 195, top - 4, sub, 10.5, MUT, "middle")

    yA, yD = 250, 336
    s += bus(80, yA, 1290, yA, ADDR, "32", "", marker=None)
    s += bus(80, yD, 1290, yD, DATA, "32", "")
    s += txt(700, yA - 16, "ADDRESS BUS  AB(h) · AB(l) · AB(d)", 11.5, ADDR, "middle", "700")
    s += txt(700, yD + 30, "DATA BUS  DB(h) · DB(l) · DB(d)", 11.5, DATA, "middle", "700")
    for x in (470, 880):
        for y in (yA, yD):
            s += f'<rect x="{x - 7}" y="{y - 15}" width="14" height="30" rx="3" fill="{BG}" stroke="{GREY}" stroke-width="1.6"/>'
    s += txt(470, 214, "couplers join or split the two buses at a section boundary", 10.5, MUT, "middle")

    for name, x, y in [("D0–D7 (h)", 96, 384), ("A0–A6 (h)", 182, 384), ("USP/SSP (h)", 268, 384),
                       ("PC (h)", 96, 452), ("AT (h)", 182, 452), ("AOB (h)", 268, 452)]:
        s += box(x, y, 78, 50, name, "", "unit", name_size=10)
        s += line(x + 38, yA + 5, x + 38, y, ADDR, 1.3)
        s += line(x + 38, y + 50, x + 38, yD - 5, DATA, 1.3, "a-d")
    s += box(96, 540, 248, 58, "AUH · 16-bit adder", "constants 0 ±1 ±2 +4 +size", "data", name_size=12.5)
    s += line(220, 502, 220, 540, ADDR, 5, "a-a")

    for name, x in [("A0–A6 (l)", 506), ("PC (l)", 592), ("AT (l)", 678), ("AOB (l)", 764)]:
        s += box(x, 384, 76, 50, name, "", "unit", name_size=10.5)
        s += line(x + 38, yA + 5, x + 38, 384, ADDR, 1.3)
        s += line(x + 38, 434, x + 38, yD - 5, DATA, 1.3, "a-d")
    s += box(506, 540, 248, 58, "AUL · 16-bit adder", "with AUH forms a 32-bit adder", "data", name_size=12.5)
    s += line(630, 502, 630, 540, ADDR, 5, "a-a")

    for name, x, sub in [("D0–D7 (l)", 916, ""), ("ALUB", 1002, "2nd operand"),
                         ("ALUE", 1088, "shift")]:
        s += box(x, 384, 76, 50, name, sub, "unit", name_size=10.5, sub_size=9.5)
        s += line(x + 38, yD + 5, x + 38, 384, DATA, 1.3)
    s += box(916, 452, 76, 50, "DCR", "bit 4→16", "unit", name_size=10.5, sub_size=9.5)
    s += box(1002, 452, 76, 50, "PREN", "MOVEM", "unit", name_size=10.5, sub_size=9.5)
    s += box(1084, 452, 82, 50, "FTU", "SR/IR/vec", "unit", name_size=10.5, sub_size=9.5)
    s += box(916, 540, 248, 58, "ALU · 16-bit", "15 rows × 5 columns · X N Z V C", "data", name_size=13)
    s += line(1040, yD + 5, 1040, 540, DATA, 5, "a-d")
    s += line(1126, 434, 1126, 540, DATA, 2, "a-d")
    s += line(1040, 502, 1040, 540, DATA, 1.5)

    s += box(80, 640, 120, 36, "DBIN", "data in", "data", name_size=10.5, sub_size=9.5)
    s += box(214, 640, 120, 36, "DOB", "data out", "data", name_size=10.5, sub_size=9.5)
    s += box(80, 692, 120, 36, "AOB", "addr out", "addr", name_size=10.5, sub_size=9.5)
    s += line(140, 676, 140, 692, ADDR, 8, "a-a")

    s += txt(470, 780, "→  D0–D15   sixteen pins, read and written", 12.5, DATA, weight="700")
    s += txt(470, 804, "→  A1–A23   twenty-three pins, output only   (A0 is derived from UDS/LDS)", 12.5, ADDR, weight="700")
    s += txt(80, 764, "The pads sit outside the unit: AOB, DOB and DBIN are its edge registers.", 11, MUT)
    s += legend(1000, 750, [(ADDR, "address path"), (DATA, "data path")])
    s += footer(h - 12, "MC68000 · execution unit · after US 4,296,469 fig. 2–3 and the die analysis")
    return s + "</svg>\n"


# ------------------------------------------------------------------ sheet 02
def control():
    w, h = W, 1010
    s = head(w, h, "02 · From opcode to control line",
             "Three decoders turn an instruction into three microcode addresses; both ROMs read at once; a PLA bank keyed on the opcode expands their bits into the datapath controls.")
    # --- row A: the instruction queue -----------------------------------
    s += box(60, 108, 150, 54, "D0–D15 pads", "instruction word", "data", name_size=11.5)
    s += box(250, 108, 140, 54, "IRC", "next word", "unit", name_size=14)
    s += box(430, 108, 140, 54, "IR", "being decoded", "unit", name_size=14)
    s += box(610, 108, 140, 54, "IRD", "being executed", "unit", name_size=14)
    s += bus(210, 135, 248, 135, DATA, "16", "", marker="a-d")
    s += bus(390, 135, 428, 135, DATA, "16", "load IR", 409, 123, marker="a-d")
    s += bus(570, 135, 608, 135, DATA, "16", "load IRD", 589, 123, marker="a-d")
    s += txt(780, 122, "IRD drives the decode PLAs, the ALU row decoder,", 11, MUT)
    s += txt(780, 138, "the branch conditions and the field translation unit.", 11, MUT)
    s += txt(780, 158, "IR is what the PLAs see; IRD is what the datapath obeys.", 11, MUT)

    # --- row B: the three decoders --------------------------------------
    s += txt(60, 214, "16 bits of opcode (and its complement) fan out to three PLAs", 10.5, MUT)
    s += bus(500, 162, 500, 240, CTRL, "16", "", marker="a-c")
    s += line(230, 248, 700, 248, CTRL, 2)
    for x in (230, 415, 600):
        s += line(x, 248, x, 262, CTRL, 2, "a-c")
    s += box(140, 264, 200, 76, "A1 DECODE", "PLA · 149 terms", "ctrl", name_size=13)
    s += box(356, 264, 170, 76, "A2 / A3 DECODE", "shared AND plane", "ctrl", name_size=12.5)
    s += box(540, 264, 160, 76, "ILLEGAL · PRIV", "from the A2/A3 PLA", "ctrl", name_size=11.5)

    # --- row C: address selector ---------------------------------------
    s += box(60, 386, 640, 58, "MICROCODE ADDRESS SELECTOR",
             "{ NMA · A1 · A2 · A3 · A0 · A0S }  →  10-bit registered address", "ctrl", name_size=12.5)
    for x in (270, 430, 620):
        s += bus(x, 340, x, 384, CTRL, "10", "", marker="a-c")
    s += box(760, 386, 260, 58, "EXCEPTION LOGIC", "A0 at a boundary · A0S now", "clk", name_size=12)
    s += bus(758, 415, 702, 415, CLK, "10", "", marker="a-k")
    # feedback of the next address into the selector
    s += path("M 60 648 L 28 648 L 28 415 L 58 415", ADDR, 2.2, "a-a")
    s += txt(34, 470, "next address, next cycle", 10, ADDR, rotate=True)

    # --- row D: the two stores ------------------------------------------
    s += path("M 380 444 L 380 462 L 250 462 L 250 480", CTRL, 3.4, "a-c")
    s += path("M 380 462 L 700 462 L 700 480", CTRL, 3.4, "a-c")
    s += box(60, 480, 380, 92, "µROM  544 × 17", "next address · condition · access class", "store", name_size=15)
    s += box(520, 480, 380, 92, "NROM  336 × 68", "transfers · AU/ALU selects · flags", "store", name_size=15)
    s += txt(910, 500, "one address, both ROMs,", 10.5, MUT)
    s += txt(910, 516, "read in the same cycle", 10.5, MUT)
    s += txt(910, 542, "many-to-one: 544 addresses", 10.5, STORE)
    s += txt(910, 558, "share 336 nanowords", 10.5, STORE)

    # --- row E: what the words mean -------------------------------------
    s += box(60, 620, 190, 52, "next address", "10 bits", "addr", name_size=11.5)
    s += box(60, 686, 190, 52, "branch control", "CBC, 5 bits", "addr", name_size=11.5)
    s += box(60, 752, 190, 52, "access class", "i/f/n/t · p/r/w · type", "ctrl", name_size=11.5)
    s += bus(155, 572, 155, 618, ADDR, "10", "", marker="a-a")
    s += bus(250, 546, 300, 546, CTRL, "16", "", marker="a-c")
    s += bus(300, 546, 300, 686, CTRL, "2", "", marker="a-c")
    s += line(250, 686, 300, 686, CTRL, 1.2)

    s += box(330, 686, 210, 52, "BRANCH UNIT", "condition → C1/C0", "addr", name_size=11.5)
    s += txt(330, 754, "inputs: flags, IRD, AU = 0", 10, MUT)
    s += txt(330, 770, "C1/C0 replace address bits 7:6", 10, MUT)
    s += txt(330, 786, "→ four targets in one row", 10, MUT)
    s += bus(300, 712, 328, 712, ADDR, "2", "", marker="a-a")

    s += box(600, 620, 380, 84, "IRD DECODE PLA BANK", "keyed on the instruction register", "ctrl", name_size=13)
    s += bus(710, 572, 710, 618, CTRL, "32", "68 bits", 710, 598, marker="a-c")
    s += box(600, 740, 380, 56, "≈ 200 CONTROL LINES", "register strobes · bus selects · flag masks", "ctrl", name_size=12.5)
    s += bus(790, 704, 790, 738, CTRL, "16", "", marker="a-c")
    s += txt(600, 826, "The ROMs supply only ~70 of the ~200 signals;", 11, MUT)
    s += txt(600, 842, "the rest is hard-wired decode of the opcode.", 11, MUT)

    # --- row F: consumers ------------------------------------------------
    s += box(1060, 620, 220, 84, "BUS STATE\nMACHINE", "S0–S7 · waits on DTACK", "clk", name_size=12.5, sub_size=10.5)
    s += box(1060, 740, 220, 84, "EXECUTION\nUNIT", "three sections", "data", name_size=12.5, sub_size=10.5)
    s += path("M 250 778 L 1040 778 L 1040 662 L 1058 662", CTRL, 2.2, "a-c")
    s += bus(980, 768, 1058, 768, CTRL, "16", "", marker="a-c")
    s += txt(250, 894, "Every microcycle: one microword fixes the next address and, at most, one bus access.", 11, MUT)
    s += txt(250, 920, "A microword that must finish a bus access holds the sequencer until the cycle ends.", 11, MUT)
    s += legend(1060, 120, [(ADDR, "microcode address"), (CTRL, "control"),
                            (DATA, "data"), (CLK, "clock · exceptions"), (STORE, "ROM")])
    s += footer(h - 12, "MC68000 · control path · after US 4,325,121 and the die-derived PLA listings")
    return s + "</svg>\n"


# ------------------------------------------------------------------ sheet 03
def timing():
    w, h = W, 900
    s = head(w, h, "03 · Time — one clock, two phases, four clocks per bus cycle",
             "A microword costs two clock periods; a bus access costs four; wait states stretch the microword, never the instruction's script.")
    x0, x1 = 120, 1330
    base = 150
    half = 70

    # CLK waveform: 8 periods
    d = f"M {x0} {base}"
    for i in range(8):
        x = x0 + i * half
        d += f" L {x} {base - 34} L {x + half / 2} {base - 34} L {x + half / 2} {base}"
        d += f" L {x + half} {base}"
    s += path(d, CLK, 2.4)
    s += txt(x0 - 12, base - 20, "CLK", 12, CLK, "end", "700")
    s += txt(x1 + 8, base - 8, "pin", 10.5, MUT)
    for i in range(9):
        x = x0 + i * half
        s += line(x, base + 4, x, base + 12, FAINT, 1)
    s += txt(x0, base + 30, "every microword is two clock periods wide (four phase edges: φ1 φ2 φ1 φ2)", 11, MUT)

    # microcycle band
    my = 250
    for i in range(4):
        x = x0 + i * half * 2
        s += f'<rect x="{x}" y="{my}" width="{half * 2}" height="44" rx="6" fill="{FILL_CLK}" stroke="{CLK}" stroke-width="1.3"/>'
        s += txt(x + half, my + 27, f"microword {i + 1}", 11.5, INK, "middle", "700")
    s += txt(x0, my - 10, "one microword per microcycle", 11, MUT)

    # bus cycle: 8 states over 2 microcycles (two states per clock period)
    by = 360
    sh = half / 2
    states = ["S0", "S1", "S2", "S3", "S4", "S5", "S6", "S7"]
    for i, st in enumerate(states):
        x = x0 + i * sh
        fill = "#ffffff" if i % 2 == 0 else "#f4f2ea"
        s += f'<rect x="{x}" y="{by}" width="{sh}" height="52" fill="{fill}" stroke="{GREY}" stroke-width="1.1"/>'
        s += txt(x + sh / 2, by + 32, st, 11, INK, "middle", "700")
    s += txt(x0, by - 12, "a bus cycle is eight states over four clock periods — two states per clock, two microwords", 11, MUT)
    s += txt(x0 + half + 6, by + 70, "address out · AS low", 10.5, ADDR)
    s += txt(x0 + 2 * half + 6, by + 70, "DTACK sampled at the end of S4", 10.5, CTRL)

    # wait states
    wy = 470
    s += txt(x0, wy - 12, "if DTACK is late, whole clocks are inserted and the current microword is held", 11, MUT)
    xx = x0
    seq = ["S0", "S1", "S2", "S3", "S4", "S5", "S5", "S5", "S6", "S7"]
    for st in seq:
        wd = sh * (0.75 if st == "S5" and xx > x0 + 4 * sh else 1)
        s += f'<rect x="{xx}" y="{wy}" width="{wd}" height="46" fill="#fff8ea" stroke="{CTRL}" stroke-width="1.2"/>'
        s += txt(xx + wd / 2, wy + 29, st, 10, INK, "middle", "700")
        xx += wd
    s += txt(xx + 10, wy + 29, "…", 12, MUT)

    # E clock
    ey = 600
    s += txt(x0, ey - 14, "E clock: one period every ten CLK periods — six low, four high", 11, MUT)
    d = f"M {x0} {ey}"
    for rep in range(2):
        bx = x0 + rep * half * 10
        d += f" L {bx} {ey - 30} L {bx + half * 6} {ey - 30} L {bx + half * 6} {ey} L {bx + half * 10} {ey}"
    s += path(d, CLK, 2.2)
    s += txt(x0 + half * 3, ey - 40, "6 low", 10.5, CLK, "middle")
    s += txt(x0 + half * 8, ey - 40, "4 high", 10.5, CLK, "middle")

    # cost table
    ty = 700
    s += f'<rect x="{x0}" y="{ty - 26}" width="470" height="150" rx="8" fill="#ffffff" stroke="{FAINT}" stroke-width="1.2"/>'
    s += txt(x0 + 16, ty - 6, "cost = number of microwords", 12.5, INK, weight="700")
    rows = [("NOP", "2 words, one bus read", "4 clocks"),
            ("MOVE.W Dn,Dn", "one read, one write", "4 clocks"),
            ("DIVU", "a loop of words", "76–130 clocks")]
    for i, (a, b, c) in enumerate(rows):
        s += txt(x0 + 16, ty + 22 + i * 26, a, 11.5, INK, weight="700")
        s += txt(x0 + 140, ty + 22 + i * 26, b, 11, MUT)
        s += txt(x0 + 400, ty + 22 + i * 26, c, 11, DATA, "end", "700")

    s += txt(640, ty - 6, "Consequences worth remembering", 12.5, INK, weight="700")
    for i, t in enumerate(["a wait state lengthens the current microword, not the next one;",
                           "timing therefore depends on the script, and the script is data;",
                           "the E clock is the only slow clock in the machine.",
                           "there is no cycle counter — the microcode is the counter."]):
        s += txt(640, ty + 20 + i * 24, "· " + t, 11.5, MUT)
    s += legend(980, 690, [(CLK, "clocking"), (ADDR, "address"), (CTRL, "bus control"), (DATA, "timing result")])
    s += footer(h - 12, "MC68000 · timing · after the user's manual bus-state diagrams and the die-derived sequencer")
    return s + "</svg>\n"


# ------------------------------------------------------------------ sheet 04
def wordformats():
    w, h = W, 860
    s = head(w, h, "04 · The two control words, bit by bit",
             "17 bits of sequencing and 68 bits of datapath. Field names are the patent's; bit numbers are the production part's.")

    def bits(y, fields, cellw, x0=90, label=""):
        out = ""
        total = sum(n for _, n, _, _ in fields)
        for i, (name, n, color, note) in enumerate(fields):
            x = x0 + sum(n for _, k, _, _ in fields[:i]) * cellw
            wd = n * cellw
            out += f'<rect x="{x}" y="{y}" width="{wd}" height="54" fill="{color}" stroke="{FAINT}" stroke-width="1"/>'
            out += txt(x + wd / 2, y + 24, name, 11.5 if wd > 46 else 10, INK, "middle", "700")
            if wd > 40:
                out += txt(x + wd / 2, y + 42, note, 9.5, MUT, "middle")
            if label:
                out += ""
        # bit numbers
        bit = total - 1
        for name, n, color, note in fields:
            x = x0 + (total - bit - 1) * cellw
            for k in range(n):
                cx = x0 + (total - bit - 1 + k) * cellw
                out += txt(cx + cellw / 2, y - 8, str(bit - k), 8.5, FAINT, "middle")
            bit -= n
        return out

    cell = 44
    s += txt(90, 130, "FORMAT I — direct next address (bit 1 = 0)", 12.5, INK, weight="700")
    s += bits(160, [("FC", 2, FILL_ADDR, "space"), ("NMA · next address", 10, FILL_ADDR, "10 bits"),
                    ("C", 1, FILL_CTRL, ""), ("TY", 2, FILL_CTRL, "source"), ("0", 1, "#f2f2ee", ""),
                    ("I", 1, FILL_DATA, "load IR")], cell)
    s += txt(90, 250, "TY: 00 = NMA · 01 = A1 · 10 = A2 · 11 = A3        FC: n none · u unknown · d data · i instruction · a interrupt ack",
             11, MUT)
    s += txt(90, 272, "C+I: db neither · dbi IRC→IR + trap vector · dbc IRC→IR only · dbl partial vector",
             11, MUT)

    s += txt(90, 340, "FORMAT II — conditional (bit 1 = 1): the condition supplies address bits 7 and 6", 12.5, INK, weight="700")
    s += bits(370, [("FC", 2, FILL_ADDR, "space"), ("NMBA · base address", 8, FILL_ADDR, "8 bits"),
                    ("CBC · condition", 5, FILL_CTRL, "32 codes"), ("1", 1, "#f2f2ee", ""),
                    ("I", 1, FILL_DATA, "load IR")], cell)
    s += txt(90, 460, "target = { NMA[9:8], C1 C0, NMA[5:0] }   → four destinations in one logical row of both ROMs", 11, MUT)
    s += txt(90, 482, "conditions: auz (AU = 0) · c · z · n · v · nz1 · nz2 · ms0 · m01 · ze · nv · d4 · enl · cc · i11  (+ inverted)",
             11, MUT)

    # nanocode map
    s += txt(90, 545, "NANOWORD — 68 bits, 66 driven", 12.5, INK, weight="700")
    nx, ny, cw = 90, 570, 14
    groups = [(4, FILL_CLK, "AU const"), (32, FILL_DATA, "transfers and destinations"),
              (32, FILL_CTRL, "mid-state, ALU mode, flags")]
    x = nx
    for n, col, name in groups:
        wd = n * cw
        s += f'<rect x="{x}" y="{ny}" width="{wd}" height="46" fill="{col}" stroke="{FAINT}" stroke-width="1"/>'
        s += txt(x + wd / 2, ny + 28, name, 10.5 if n > 8 else 8.5, INK, "middle", "700")
        x += wd
    s += txt(nx, ny + 66, "bits 67 and 66 are unused on the die: two metal traces leave the array and stop", 10.5, MUT)
    s += txt(nx, ny + 86, "fields: register ↔ bus transfers · sign extends · AU input constant · au = alu · d/a/pc destination ·",
             10.5, MUT)
    s += txt(nx, ny + 102, "4-bit mid-state (trace, SR, ibuf, MOVEM) · ALU column 1–6 with initiate/finish · flag masks",
             10.5, MUT)

    # storage organisation
    ox, oy = 980, 520
    s += txt(ox, oy - 14, "How the bits are stored", 12.5, INK, weight="700")
    s += f'<rect x="{ox}" y="{oy}" width="120" height="34" fill="{FILL_STORE}" stroke="{STORE}"/>'
    s += txt(ox + 60, oy + 22, "µROM 34 × 16", 10, INK, "middle", "700")
    s += f'<rect x="{ox}" y="{oy + 42}" width="120" height="86" fill="{FILL_STORE}" stroke="{STORE}"/>'
    s += txt(ox + 60, oy + 90, "NROM 84 × 4", 10, INK, "middle", "700")
    s += txt(ox + 134, oy + 16, "= 544 addresses", 10.5, MUT)
    s += txt(ox + 134, oy + 92, "= 336 addresses", 10.5, MUT)
    s += txt(ox, oy + 150, "row selected by an address PLA;", 10.5, MUT)
    s += txt(ox, oy + 166, "the 10-bit address is permuted on the die,", 10.5, MUT)
    s += txt(ox, oy + 182, "so field slicing needs a scramble.", 10.5, MUT)

    s += legend(90, 700, [(FILL_ADDR, "address / space"), (FILL_CTRL, "sequencing"),
                          (FILL_DATA, "datapath / IR"), (FILL_CLK, "constants")])
    s += footer(h - 12, "MC68000 · microcode and nanocode formats · after US 4,325,121 fig. 8 and the die dump")
    return s + "</svg>\n"


SHEETS = {
    "00-floorplan.svg": floorplan,
    "01-datapath.svg": datapath,
    "02-control.svg": control,
    "03-timing.svg": timing,
    "04-word-formats.svg": wordformats,
}


def audit(path, svg):
    global _audit
    bad = []
    for width, cx, y, text, boxw in _audit:
        if boxw is not None and width > boxw - 8:
            bad.append(f"  overflow: {text!r} needs {width:.0f}px in a {boxw - 8:.0f}px box")
        if cx + width / 2 > W - 8:
            bad.append(f"  off-canvas: {text!r} ends at {cx + width / 2:.0f}")
    _audit = []
    return bad


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    problems = {}
    for name, fn in SHEETS.items():
        svg = fn()
        # one element per line: readable diffs, and the file survives review
        svg = svg.replace("><", ">\n<") + "\n"
        p = os.path.join(here, name)
        open(p, "w").write(svg)
        problems[name] = audit(p, svg)
        print(f"{name}: {len(svg)} bytes, {len(problems[name])} layout warnings")
    for name, bad in problems.items():
        for b in bad:
            print(name + b)


if __name__ == "__main__":
    main()
