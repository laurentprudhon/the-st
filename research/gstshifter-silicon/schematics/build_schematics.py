#!/usr/bin/env python3
"""Generate the educational GST Shifter schematics (SVG) in this directory.

Same rules as the GSTMCU and 68000 sets, so the three read as one series:

  * 1440-unit wide canvas, light paper, no gradients, no shadows;
  * colour carries meaning: ADDRESS blue, DATA green, CONTROL amber, CLOCK violet,
    storage grey/violet; line weight encodes bus width;
  * every block is a rounded rectangle with a bold name and a small subtitle;
  * every sheet states its own provenance, and none of them claims die geometry.

Content is transcribed from `research/gst-shifter-implementation.md` (which cites the
sources) — the chip's pin symbol, ijor's reverse engineering of the earlier ST Shifter,
Atari's register tables, the board article and the reconstruction. Nothing is copied
from a drawing: these are redrawn block and timing diagrams, and the README states the
licence position.

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


def box(x, y, w, h, name, sub="", stroke=CTRL, fill=F_CTRL, tsize: float = 13, dashes=None):
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
    key = {ADDR: "a", DATA: "d", CTRL: "c", CLK: "k", STORE: "s"}.get(color, "c")
    m = f' marker-end="url(#a-{key})"' if arrow else ""
    d = f' stroke-dasharray="{dashes}"' if dashes else ""
    return f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="{width}"{m}{d}/>'


def path(d, color=CTRL, width=1.4, arrow=False, fill="none", dashes=None):
    key = {ADDR: "a", DATA: "d", CTRL: "c", CLK: "k", STORE: "s"}.get(color, "c")
    m = f' marker-end="url(#a-{key})"' if arrow else ""
    da = f' stroke-dasharray="{dashes}"' if dashes else ""
    return f'<path d="{d}" fill="{fill}" stroke="{color}" stroke-width="{width}"{m}{da}/>'


# ---------------------------------------------------------------- 00 · blocks
def sheet_blocks():
    h = 870
    o = [head(W, h, "00 · The chip as blocks",
              "GST Shifter C300588-001 — five paths through one 84-pin package. Functional, not to scale.")]

    cols = [44, 384, 744, 1104]
    cw = [300, 320, 320, 296]
    rows = [124, 244, 364, 484, 604]
    bh = 84

    bands = ["video — one word per window, four to a group",
             "colour — index to analogue, twelve bits wide",
             "sound — DMA words into two byte lanes",
             "microwire — eleven bits to the LMC1992",
             "registers and control — the other side of the wires"]
    for i, b in enumerate(bands):
        o.append(text(44, rows[i] - 12, b, 12, MUT, italic=True))

    # video
    o.append(box(cols[0], rows[0], cw[0], bh, "MD0–MD15", "one word per display window", DATA, F_DATA))
    o.append(box(cols[1], rows[0], cw[1], bh, "16-word buffer", "4 words × 16 bits, clocked by LOAD", STORE, F_STORE))
    o.append(box(cols[2], rows[0], cw[2], bh, "Shift array", "4 × 16 cells, two flip-flops each", DATA, F_DATA))
    o.append(box(cols[3], rows[0], cw[3], bh, "Pixel stream", "4-bit index, looked up below", DATA, F_DATA))
    # colour
    o.append(box(cols[0], rows[1], cw[0], bh, "Palette", "16 words × 12 bits, transparent", STORE, F_STORE))
    o.append(box(cols[1], rows[1], cw[1], bh, "R3–R0 · G3–G0 · B3–B0", "twelve digital pins, active high", DATA, F_DATA))
    o.append(box(cols[2], rows[1], cw[2], bh, "74S257 + ladder", "four weighted resistors per channel", DATA, F_DATA))
    o.append(box(cols[3], rows[1], cw[3], bh, "MC1377 → DIN-13", "the three analogue outputs", DATA, F_DATA))
    # sound
    o.append(box(cols[0], rows[2], cw[0], bh, "SREQ / *SLOAD", "one word per granted slot", CTRL, F_CTRL))
    o.append(box(cols[1], rows[2], cw[1], bh, "Word FIFO", "depth unknown; four carried (T3)", STORE, F_STORE))
    o.append(box(cols[2], rows[2], cw[2], bh, "Byte split", "SD0–SD7, high byte first", DATA, F_DATA))
    o.append(box(cols[3], rows[2], cw[3], bh, "DAC0802 ×2", "through 74F374s, on LD and HD", DATA, F_DATA))
    # microwire
    o.append(box(cols[0], rows[3], cw[0], bh, "$FF8922 / $FF8924", "data and mask registers", ADDR, F_ADDR))
    o.append(box(cols[1], rows[3], cw[1], bh, "Microwire shifter", "16 bit-times at 1 Mbit/s", CTRL, F_CTRL))
    o.append(box(cols[2], rows[3], cw[2], bh, "*MWE · MWD · MWK", "the mask decides which bits clock", CTRL, F_CTRL))
    o.append(box(cols[3], rows[3], cw[3], bh, "LMC1992", "volume · bass · treble · mixing", CTRL, F_CTRL))
    # registers and control
    o.append(box(cols[0], rows[4], cw[0], bh, "PD0–PD15 · A1–A6", "CS and R/W: a 64-byte window", ADDR, F_ADDR))
    o.append(box(cols[1], rows[4], cw[1], bh, "Transceiver + decode", "LATCH · *RDAT · *WDAT, moved in", CTRL, F_CTRL))
    o.append(box(cols[2], rows[4], cw[2], bh, "Clock mux + reload", "8 · 16 · 32 MHz, and the group rule", CLK, F_CLK))
    o.append(box(cols[3], rows[4], cw[3], bh, "DE · *BLANK · MONO", "in: the window, the edge, mono select", CLK, F_CLK))

    # arrows, one per band, coloured by the kind of thing that flows
    for r, col in zip(rows, [DATA, DATA, CTRL, CTRL, ADDR]):
        for i in range(3):
            o.append(line(cols[i] + cw[i] + 4, r + bh / 2, cols[i + 1] - 4, r + bh / 2, col, 3, arrow=True))
    # the index is what the palette is addressed by: a dashed signal path, not a data bus
    o.append(path(f"M{cols[3]+cw[3]/2},{rows[0]+bh+2} L{cols[3]+cw[3]/2},{rows[1]-26} L{cols[0]+cw[0]/2},{rows[1]-26} L{cols[0]+cw[0]/2},{rows[1]-2}", STORE, 1.6, arrow=True, dashes="4 4"))
    o.append(text(cols[1] + 10, rows[1] - 32, "the index addresses the palette", 10, MUT))

    # the absent things
    o.append(f'<rect x="44" y="{rows[4]+bh+18}" width="1356" height="94" rx="8" fill="{F_GREY}" stroke="{GREY}" stroke-width="1.4" stroke-dasharray="5 4"/>')
    o.append(text(64, rows[4] + bh + 42, "What is deliberately not in this box", 13, INK, bold=True))
    o.append(text(64, rows[4] + bh + 62, "no video address counter — U400 walks the fetch address", 11.5, MUT))
    o.append(text(560, rows[4] + bh + 62, "no DE, *BLANK, HSYNC or VSYNC generator — U400's sync block", 11.5, MUT))
    o.append(text(64, rows[4] + bh + 82, "no fetch decision — one word per display window, chosen in U400", 11.5, MUT))
    o.append(text(560, rows[4] + bh + 82, "no fourth resolution — MWE, MWD and MWK are the Microwire link", 11.5, MUT))

    o.append(foot(h, ["Provenance: the 84-pin symbol in the service manual (design/board-inventory.md §3.5); the block structure from",
                      "ijor's ST-Shifter reverse engineering and the board article; no die photograph of this part exists. See README.md."]))
    return "".join(o)


# ---------------------------------------------------------------- 01 · group
def sheet_group():
    h = 1010
    o = [head(W, h, "01 · One group — four words, 2 µs",
              "The buffer, the shift array and the reload control; why a group is 16, 32 or 64 pixels and never 500 ns.")]

    # --- the buffer
    o.append(text(44, 116, "the buffer — sixteen cells, four words deep", 13, INK, bold=True))
    for i in range(4):
        y = 132 + i * 62
        o.append(f'<rect x="64" y="{y}" width="240" height="50" rx="6" fill="{F_STORE}" stroke="{STORE}" stroke-width="1.4"/>')
        o.append(text(184, y + 22, f"word {i}", 12, INK, anchor="middle", bold=True))
        o.append(text(184, y + 40, "16 bits · one per plane cell", 10.5, MUT, anchor="middle"))
    o.append(line(44, 132, 44, 400, STORE, 2, arrow=True))
    o.append(text(50, 420, "", 10))
    o.append(text(44, 372, "LOAD", 11, STORE, bold=True))
    o.append(text(44, 388, "in at the top", 10, MUT))
    o.append(text(330, 408, "a fifth LOAD", 11, INK, anchor="start", bold=True))
    o.append(text(330, 424, "pushes the oldest", 10.5, MUT))
    o.append(text(330, 440, "word out — it is a", 10.5, MUT))
    o.append(text(330, 456, "shift array, not a FIFO", 10.5, MUT))

    # --- the shift array
    o.append(text(560, 116, "the shift path — 16 cells per plane, clocked by the pixel clock", 13, INK, bold=True))
    cw, ch = 20, 34
    for p in range(4):
        y = 132 + p * 46
        o.append(text(556, y + 22, f"plane {p}", 11, MUT, anchor="end"))
        for c in range(16):
            o.append(f'<rect x="{560+c*cw}" y="{y}" width="{cw}" height="{ch}" fill="{F_DATA}" stroke="{DATA}" stroke-width="1"/>')
        o.append(line(560 + 16 * cw + 6, y + ch / 2, 560 + 16 * cw + 58, y + ch / 2, DATA, 2, arrow=True))
    o.append(text(560, 326, "one bit leaves each plane per pixel clock", 10.5, MUT))
    o.append(text(560, 344, "four of them make the 4-bit index", 10.5, MUT))
    o.append(text(560 + 16 * cw + 62, 156, "index", 11, DATA, bold=True))
    o.append(text(560 + 16 * cw + 62, 172, "bit 3 … 0", 10, MUT))

    # reload path from buffer into the array
    o.append(path("M304,222 L360,222 L360,300 L556,300", STORE, 2, arrow=True))
    o.append(text(364, 292, "reload: 64 bits at once", 10.5, STORE))

    # --- chaining by mode
    o.append(text(44, 470, "how four words become pixels — the mode changes the chaining, not the transfer", 13, INK, bold=True))
    modes = [("low · 8 MHz", 4, "four separate 16-bit shifts", "16 pixels"),
             ("mid · 16 MHz", 2, "two chained pairs, 2 bits a pixel", "32 pixels"),
             ("mono · 32 MHz", 1, "one 64-bit shift, 1 bit a pixel", "64 pixels")]
    for i, (name, chains, note, px) in enumerate(modes):
        y = 486 + i * 58
        o.append(text(44, y + 16, name, 11.5, INK, bold=True))
        span, left = 880, 220
        w = {4: 180, 2: 380, 1: 880}[chains]
        gap = (span - chains * w) / (chains - 1) if chains > 1 else 0
        for c in range(chains):
            x = left + c * (w + gap)
            o.append(f'<rect x="{x}" y="{y}" width="{w}" height="30" rx="5" fill="{F_DATA}" stroke="{DATA}" stroke-width="1.2"/>')
            o.append(text(x + w / 2, y + 20, f"{64//chains} bits", 10.5, INK, anchor="middle"))
        o.append(text(1120, y + 12, note, 10, MUT))
        o.append(text(1120, y + 28, px, 11.5, INK, bold=True))

    # --- the reload control
    o.append(text(44, 690, "the reload control — both conditions, or no transfer", 13, INK, bold=True))
    o.append(box(44, 704, 320, 80, "4-stage LOAD register", "a 1 per LOAD, cleared after Reload", STORE, F_STORE))
    o.append(box(420, 704, 300, 80, "4-bit pixel counter", "wraps every 16 pixel clocks", CLK, F_CLK))
    o.append(box(776, 704, 260, 80, "Reload", "one pulse, then start again", CTRL, F_CTRL))
    o.append(line(364, 744, 420, 744, STORE, 2, arrow=True))
    o.append(line(720, 744, 776, 744, CLK, 2, arrow=True))
    o.append(path("M1092,744 L1160,744 L1160,700 L1180,700", CTRL, 2, arrow=True, dashes="4 4"))
    o.append(text(1096, 786, "to the buffer", 10, MUT))
    o.append(text(44, 814, "Miss a reload and sixteen pixels of palette 0 leave the chip: the border colour, for one group — the", 11.5, INK))
    o.append(text(44, 832, "\"every other 16 pixels background\" that demos live with is a transfer that did not happen.", 11.5, INK))

    # --- the 2 µs strip
    x0, wpx = 44, 1352
    o.append(text(44, 872, "one group in time — four display windows, whichever the mode", 12.5, INK, bold=True))
    o.append(f'<rect x="{x0}" y="882" width="{wpx}" height="26" rx="4" fill="{F_DATA}" stroke="{DATA}" stroke-width="1.2"/>')
    for i in range(1, 4):
        x = x0 + wpx * i / 4
        o.append(line(x, 878, x, 912, INK, 1.2, dashes="3 3"))
    for i, lab in enumerate(["LOAD", "LOAD", "LOAD", "LOAD"]):
        o.append(text(x0 + wpx * (i + 0.5) / 4, 900, lab, 10, INK, anchor="middle"))
    for i, lab in enumerate(["0", "500 ns", "1 µs", "1.5 µs", "2 µs"]):
        o.append(text(x0 + wpx * i / 4, 928, lab, 10.5, MUT, anchor="middle"))
    o.append(text(x0 + wpx, 876, "reload at 2 µs", 10.5, CTRL, anchor="end", bold=True))

    o.append(foot(h, ["Provenance: array geometry and the 4-word transfer from ijor's reverse engineering of the ST Shifter; the reload rule and",
                      "the failure mode from the same; the 2 µs grid and the pixels-per-group figures derived in study §4.1. No die geometry claimed."]))
    return "".join(o)


# ---------------------------------------------------------------- 02 · colour
def sheet_colour():
    h = 920
    o = [head(W, h, "02 · Colour — index, palette, twelve pins, three ladders",
              "Why the fourth bit is the cheap one, and why an ST program's colour $0777 is 14/15 here.")]

    # pipeline
    o.append(box(44, 120, 240, 78, "4-bit index", "one bit per plane", DATA, F_DATA))
    o.append(box(352, 120, 320, 78, "16 × 12-bit palette", "transparent latches, gated by CS", STORE, F_STORE))
    o.append(box(744, 120, 300, 78, "twelve pins", "R3–R0 · G3–G0 · B3–B0", DATA, F_DATA))
    o.append(box(1104, 120, 292, 78, "74S257", "colour / mono, selected by MONO", DATA, F_DATA))
    for a, b in [(284, 352), (672, 744), (1044, 1104)]:
        o.append(line(a + 4, 159, b - 4, 159, DATA, 3, arrow=True))

    # one channel in detail
    o.append(text(44, 250, "one channel, as the board sums it — the four bits are weighted 1-8-4-2", 13, INK, bold=True))
    weights = [("bit 11", 1), ("bit 10", 8), ("bit 9", 4), ("bit 8", 2)]
    for i, (name, wt) in enumerate(weights):
        yy = 252 + i * 36
        o.append(f'<rect x="44" y="{yy}" width="174" height="28" rx="5" fill="{F_DATA}" stroke="{DATA}" stroke-width="1.2"/>')
        o.append(text(131, yy + 19, f"{name} → weight {wt}", 10.5, INK, anchor="middle"))
        o.append(line(220, yy + 14, 326, 330 + (14 - i) * 6, DATA, 1.6, arrow=True))
    o.append(f'<circle cx="336" cy="330" r="7" fill="{DATA}"/>')
    o.append(line(344, 330, 400, 330, DATA, 2, arrow=True))
    o.append(box(404, 292, 260, 76, "MC1377 encoder", "RGB + sync → composite", DATA, F_DATA))
    o.append(box(704, 292, 240, 76, "DIN-13", "the monitor's RGB, 1 V p-p", DATA, F_DATA))
    o.append(line(664, 330, 704, 330, DATA, 2, arrow=True))
    o.append(box(974, 292, 422, 76, "nine bits would be the ST", "3 bits a channel, 512 colours — not this board", GREY, F_GREY))
    o.append(text(44, 408, "the four resistors sum into one node per channel; their values are unread at 600 dpi", 10.5, MUT))

    # weights table
    o.append(text(44, 446, "the arithmetic, which three artefacts agree on", 13, INK, bold=True))
    rows = [("register bits, most to least significant", "11 · 10 · 9 · 8"),
            ("intensity weight each bit carries", "1 · 8 · 4 · 2"),
            ("the order Atari's register table prints", "\"0 3 2 1\" encoding \"1 8 4 2\""),
            ("so $0777, full red on the ST, is here", "14/15 — one step down"),
            ("full red", "$0F00, not $0E00"),
            ("the new bit alone, $0800", "1/15 — the dimmest level, not the brightest"),
            ("a clean fade steps", "$888, $111, $999, $222, $AAA …")]
    for i, (k, v) in enumerate(rows):
        y = 466 + i * 30
        o.append(f'<rect x="44" y="{y}" width="1356" height="26" rx="4" fill="{F_CTRL if i % 2 == 0 else "#ffffff"}" stroke="{GRID}" stroke-width="1"/>')
        o.append(text(58, y + 18, k, 11, MUT))
        o.append(text(700, y + 18, v, 11.5, INK, bold=True))

    # the two latches
    o.append(box(44, 700, 430, 84, "monocolor", "a second copy of bit 0 of colour 0, written together", STORE, F_STORE))
    o.append(box(504, 700, 430, 84, "transparent, not clocked", "a write is visible while CS is still low", STORE, F_STORE))
    o.append(box(964, 700, 432, 84, "a byte write lands twice", "the 68000 puts it on both halves of the bus", STORE, F_STORE))
    o.append(text(44, 806, "The three notes above are what a model must keep: the mono/inverse select is not a mode register, the palette has no capture edge,", 11.5, MUT))
    o.append(text(44, 824, "and a byte store to $FF8240 leaves $0707 rather than $0007. All three are testable with a read-back (§7 of the study).", 11.5, MUT))

    o.append(foot(h, ["Provenance: the bit weights are Atari's own register table (STE FAQ, worked fade sequence); the twelve pins are the service manual's",
                      "symbol; the ladder's existence and four-resistors-per-channel reading are from the STE video schematic. Values are not read at 600 dpi."]))
    return "".join(o)


# ---------------------------------------------------------------- 03 · line
def sheet_line():
    h = 940
    o = [head(W, h, "03 · One line — 512 clocks, three modes, one window",
              "What arrives from U400 as DE and what this chip does with it: 80 words in colour, 40 in mono.")]

    x0, wpx = 44, 1352
    clk = wpx / 512.0

    def X(c):
        return x0 + c * clk

    # ruler
    o.append(text(44, 112, "the clock ruler — one tick a clock (125 ns), the numbers are the ones the GLUE model prints", 12.5, INK, bold=True))
    o.append(line(x0, 132, x0 + wpx, 132, INK, 1.4))
    for c in [0, 4, 28, 56, 164, 184, 376, 448, 512]:
        o.append(line(X(c), 126, X(c), 138, INK, 1.2))
        o.append(text(X(c), 150, str(c), 10, MUT, anchor="middle"))

    # DE bars
    o.append(text(44, 186, "DE — the display window, from U400", 12, INK, bold=True))
    o.append(f'<rect x="{X(56)}" y="166" width="{X(376)-X(56)}" height="20" rx="3" fill="{F_DATA}" stroke="{DATA}" stroke-width="1.3"/>')
    o.append(text((X(56) + X(376)) / 2, 180, "colour: 56 → 376 = 320 clocks = 80 words", 10, INK, anchor="middle"))
    o.append(f'<rect x="{X(4)}" y="196" width="{X(164)-X(4)}" height="20" rx="3" fill="{F_DATA}" stroke="{DATA}" stroke-width="1.3"/>')
    o.append(text((X(4) + X(164)) / 2, 210, "mono: 4 → 164 = 160 clocks = 40 words", 10, INK, anchor="middle"))

    # blank
    o.append(text(44, 264, "*BLANK — the picture area, 840 columns wide at 16 MHz", 12, INK, bold=True))
    o.append(f'<rect x="{X(28)}" y="244" width="{X(448)-X(28)}" height="20" rx="3" fill="{F_CLK}" stroke="{CLK}" stroke-width="1.3"/>')
    o.append(text((X(28) + X(448)) / 2, 258, "picture: 28 → 448 = 420 clocks = 52.5 µs", 10, INK, anchor="middle"))
    o.append(line(X(448), 240, X(448), 268, CTRL, 1.6))
    o.append(text(X(448) + 6, 236, "blank on", 9.5, CTRL))
    o.append(text(X(28) - 6, 236, "blank off", 9.5, CLK, anchor="end"))

    # windows strip
    o.append(text(44, 320, "the memory grid — 128 windows of 500 ns, one internal slot each (the colour line is drawn)", 12.5, INK, bold=True))
    wy, wh = 330, 40
    o.append(f'<rect x="{x0}" y="{wy}" width="{wpx}" height="{wh}" fill="#ffffff" stroke="{INK}" stroke-width="1.2"/>')
    o.append(f'<rect x="{X(56)}" y="{wy}" width="{X(376)-X(56)}" height="{wh}" fill="{F_DATA}" stroke="{DATA}" stroke-width="1.2"/>')
    o.append(text((X(56) + X(376)) / 2, wy + 25, "80 windows to video — one word each, four to a group", 10.5, INK, anchor="middle"))
    o.append(text(x0 + (X(56) - x0) / 2, wy + 25, "14", 10.5, MUT, anchor="middle"))
    o.append(text((X(376) + x0 + wpx) / 2, wy + 25, "48 windows: sound DMA, else RAS-only refresh", 10.5, MUT, anchor="middle"))
    o.append(text(x0, wy + 58, "0", 10, MUT, anchor="middle"))
    o.append(text(X(56), wy + 58, "window 14", 10, MUT, anchor="middle"))
    o.append(text(X(376), wy + 58, "window 94", 10, MUT, anchor="middle"))
    o.append(text(x0 + wpx, wy + 58, "128", 10, MUT, anchor="middle"))

    # the table
    o.append(text(44, 436, "the three modes, closed against U400's budget", 13, INK, bold=True))
    head_cols = [("mode", 44), ("pixels", 200), ("pixel time", 330), ("words", 470), ("groups", 580), ("DE in clocks", 720), ("pixels per group", 900)]
    for lab, x in head_cols:
        o.append(text(x, 460, lab, 10.5, MUT, bold=True))
    rows = [("low", "320", "125 ns", "80", "20", "56 → 376", "16"),
            ("mid", "640", "62.5 ns", "80", "20", "56 → 376", "32"),
            ("mono", "640", "31.25 ns", "40", "10", "4 → 164", "64")]
    for i, r in enumerate(rows):
        y = 470 + i * 34
        o.append(f'<rect x="44" y="{y}" width="1300" height="30" rx="4" fill="{F_DATA if i % 2 == 0 else "#ffffff"}" stroke="{GRID}" stroke-width="1"/>')
        for (lab, x), v in zip(head_cols, r):
            o.append(text(x, y + 20, v, 11.5, INK, bold=(lab == "mode")))

    # lower notes
    o.append(text(44, 620, "The frame closes at 4 000 groups whichever mode is on: 200 colour lines × 20, 400 mono lines × 10.", 12, INK))
    o.append(text(44, 642, "A group is always four words, so the fetch schedule is identical in low and mid and halves in mono.", 12, INK))
    o.append(text(44, 664, "Outside DE the internal slot belongs to sound when SREQ is pending and to a RAS-only refresh otherwise —", 12, INK))
    o.append(text(44, 686, "the memo's rule seen from this side, and the reason a line has 48 spare windows rather than none.", 12, INK))

    o.append(box(44, 712, 640, 80, "PRELOAD — the STE's addition", "LOADs before DE, so an offset can start the line early", CLK, F_CLK))
    o.append(box(716, 712, 680, 80, "hard scroll does not move the group", "the offset acts after the group, in the shift path", CTRL, F_CTRL))
    o.append(text(44, 828, "The picture area and the DE window are different facts: DE says which words the video client fetches, *BLANK says which pixels reach", 11.5, MUT))
    o.append(text(44, 846, "the monitor. The 840-column buffer is the second, and the 80 words are the first.", 11.5, MUT))

    o.append(foot(h, ["Provenance: DE and *BLANK edges from the GLUE/Shifter state-machine model (AtariForumWiki); window counts, pixel times and the frame",
                      "total derived in study §4.1–§4.2 against U400's own line budget. The mono blank release is not stated by any source."]))
    return "".join(o)


# ---------------------------------------------------------------- 04 · registers
def sheet_registers():
    h = 940
    o = [head(W, h, "04 · The registers and the seam",
              "Two chips answer at $FF82xx: what is stored here, what is stored in U400, and what the Microwire frame looks like.")]

    o.append(text(44, 116, "in U400 — the System Control Unit", 13, ADDR, bold=True))
    o.append(text(744, 116, "in U401 — this chip", 13, STORE, bold=True))
    o.append(line(720, 104, 720, 560, GREY, 1.4, dashes="6 5"))
    o.append(text(712, 512, "the seam", 10.5, GREY, anchor="middle", italic=True))

    left = [("$FF8201 / 03 / 0D", "video base", "the address a line starts from"),
            ("$FF8205 / 07 / 09", "video counter", "the running fetch address, readable"),
            ("$FF820F", "line width", "words skipped after each line"),
            ("$FF820A", "sync control", "50/60 Hz, internal or genlock sync"),
            ("$FF8260 (copy)", "timing mode", "the second storage site of one write"),
            ("$FF8901 … $FF8913", "sound DMA", "control, frame base, top, count")]
    for i, (a, n, d) in enumerate(left):
        y = 132 + i * 62
        o.append(f'<rect x="44" y="{y}" width="640" height="52" rx="6" fill="{F_ADDR}" stroke="{ADDR}" stroke-width="1.3"/>')
        o.append(text(60, y + 21, a, 11.5, INK, bold=True))
        o.append(text(60, y + 40, n, 10.5, MUT))
        o.append(text(400, y + 32, d, 10.5, MUT))

    right = [("$FF8240 – $FF825E", "palette", "16 words × 12 bits, in latches"),
             ("$FF8260 (copy)", "plane mode", "the clock mux reads these two bits"),
             ("$FF8261", "plane mode only", "the documented way to disagree"),
             ("$FF8264 / $FF8265", "pixel offset", "0–15, and the prefetch request"),
             ("$FF8920 / $FF8921", "SAMPMOD", "sample rate; Sound's business"),
             ("$FF8922 / $FF8924", "Microwire", "data register and mask register")]
    for i, (a, n, d) in enumerate(right):
        y = 132 + i * 62
        o.append(f'<rect x="744" y="{y}" width="652" height="52" rx="6" fill="{F_STORE}" stroke="{STORE}" stroke-width="1.3"/>')
        o.append(text(760, y + 21, a, 11.5, INK, bold=True))
        o.append(text(760, y + 40, n, 10.5, MUT))
        o.append(text(1092, y + 32, d, 10.5, MUT))

    # microwire frame
    o.append(text(44, 540, "one Microwire transfer — eleven bits at 1 Mbit/s, and the mask decides which ones clock out", 13, INK, bold=True))
    fy, fh = 556, 34
    segs = [(44, 160, "1 0", "2 bits: selects the chip"), (204, 240, "register", "3 bits: the command"),
            (444, 480, "data", "6 bits"), (924, 400, "five unused", "no clock while the mask is 0")]
    for x, w, lab, sub in segs:
        fill = F_CTRL if lab != "five unused" else "#ffffff"
        o.append(f'<rect x="{x}" y="{fy}" width="{w}" height="{fh}" rx="4" fill="{fill}" stroke="{CTRL}" stroke-width="1.3"{" stroke-dasharray=\"4 3\"" if lab=="five unused" else ""}/>')
        o.append(text(x + w / 2, fy + 16, lab, 11.5, INK, anchor="middle", bold=True))
        o.append(text(x + w / 2, fy + 30, sub, 9.5, MUT, anchor="middle"))
    o.append(text(44, 614, "$FF8922 write starts it; the mask written at $FF8924 rotates and gates MWK; a write to the mask during a transfer aborts it.", 11.5, INK))
    o.append(text(44, 632, "Atari's own example uses $07FF, which is eleven clocks — about 11 µs. The shift itself always runs sixteen bit-times.", 11.5, MUT))

    # sound path
    o.append(text(44, 674, "one sound word — the two ends of the same wire", 13, INK, bold=True))
    o.append(box(44, 688, 250, 74, "SREQ (out)", "not full, sampled by U400", CTRL, F_CTRL))
    o.append(box(334, 688, 250, 74, "*SLOAD (in)", "phase 1 of the granted slot", CTRL, F_CTRL))
    o.append(box(624, 688, 250, 74, "latch on rising edge", "the memo's own wording", STORE, F_STORE))
    o.append(box(914, 688, 240, 74, "FIFO", "depth unknown — T3", STORE, F_STORE))
    o.append(box(1194, 688, 202, 74, "byte split", "two lanes, one sample apart", DATA, F_DATA))
    for a, b in [(294, 334), (584, 624), (874, 914), (1154, 1194)]:
        o.append(line(a + 3, 725, b - 3, 725, CTRL if a < 874 else DATA, 2, arrow=True))

    o.append(box(44, 786, 1352, 74, "counted in U400, spent here", "FBASE · FCOUNT · FEND and the address counter are the other chip's; the requester, FIFO and byte split are this one's", GREY, F_GREY))

    o.append(foot(h, ["Provenance: register addresses and semantics from Atari's STE documentation (and Hatari for the readback behaviour); the $FF8260",
                      "duplication from the board article and the GSTMCU study §6.7; the Microwire frame from the board article and the reconstruction."]))
    return "".join(o)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    sheets = {"00-blocks.svg": sheet_blocks,
              "01-group.svg": sheet_group,
              "02-colour.svg": sheet_colour,
              "03-line.svg": sheet_line,
              "04-registers.svg": sheet_registers}
    for name, fn in sheets.items():
        svg = fn()
        ET.fromstring(svg)                      # well-formed, or raise here
        with open(os.path.join(here, name), "w", encoding="utf-8") as f:
            f.write(svg.replace("><", ">\n<"))
        print(f"{name}:  {len(svg):6d} bytes")

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
