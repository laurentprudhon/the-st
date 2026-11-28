#!/usr/bin/env python3
"""Generate the educational Blitter schematics (SVG) in this directory.

Same rules as the 68000, GSTMCU and GST Shifter sets, so the four read as one series:

  * 1440-unit wide canvas, light paper, no gradients, no shadows;
  * colour carries meaning: ADDRESS blue, DATA green, CONTROL amber, CLOCK violet,
    storage grey/violet; line weight encodes bus width;
  * every block is a rounded rectangle with a bold name and a small subtitle;
  * every sheet states its own provenance, and none of them claims die geometry.

Content is transcribed from `research/blitter-implementation.md` (which cites the
sources) — Atari's BLiTTER manual of 1990, the recovered designs `TA-4021` / `ST-4051`
/ `ST-4082` and their gate census, the board's net list, and the Hatari model. Nothing
is copied from a drawing: these are redrawn block and timing diagrams, and the README
states the licence position.

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
    _audit.append((size, s, None))
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
    h = 980
    o = [head(W, h, "00 · The chip as blocks",
              "Blitter C101643 — one bus master, one datapath, one truth table, one 16×16 pattern. Functional, not to scale.")]

    # --- the three bands: bus side, datapath, control
    o.append(text(44, 112, "the bus side — driven, so it owns the cycle", 12, MUT, italic=True))
    o.append(box(44, 128, 250, 78, "A1–A23 · AS · UDS · LDS", "addresses and strobes, driven", ADDR, F_ADDR))
    o.append(box(44, 222, 250, 78, "D0–D15", "one word per access, both ways", DATA, F_DATA))
    o.append(box(44, 316, 250, 78, "R/W · FC0–FC2 · BERR", "control; BERR is an input", CTRL, F_CTRL))
    o.append(box(44, 410, 250, 78, "BR · BG · BGACK", "grant in, grant out, ack both ways", CTRL, F_CTRL))

    # --- address path
    o.append(text(360, 112, "the address path — the chip's only adder", 12, MUT, italic=True))
    o.append(box(360, 128, 300, 78, "address latches", "AI1–AI23, AO1–AO23, BLTAD1", ADDR, F_ADDR))
    o.append(box(360, 222, 300, 78, "BLTADD — the adder", "23 bits + signed 15-bit increment", ADDR, F_ADDR))
    o.append(box(360, 316, 300, 78, "increment registers", "SX · SY · DX · DY, each signed 15", STORE, F_STORE))
    o.append(box(360, 410, 300, 78, "counters", "X COUNT, Y COUNT (16 bits each)", STORE, F_STORE))

    # --- datapath
    o.append(text(736, 112, "the datapath — shift, merge, mask", 12, MUT, italic=True))
    o.append(box(736, 128, 300, 78, "skew shifter", "SI0–SI30, 31 stages, SKEW 0–15", DATA, F_DATA))
    o.append(box(736, 222, 300, 78, "HOP merge", "all ones · halftone · source · S AND H", CTRL, F_CTRL))
    o.append(box(736, 316, 300, 78, "BITBLT — the 16 LOPs", "one truth table, 50 XOR cells", CTRL, F_CTRL))
    o.append(box(736, 410, 300, 78, "end-mask merge", "ones written, zeros keep the old word", CTRL, F_CTRL))

    # --- stores and control
    o.append(text(1112, 112, "storage and control", 12, MUT, italic=True))
    o.append(box(1112, 128, 284, 78, "halftone RAM", "16 words × 16 bits = 256 BIT cells", STORE, F_STORE))
    o.append(box(1112, 222, 284, 78, "register decode", "XSEL00…XSEL1E + field strobes", ADDR, F_ADDR))
    o.append(box(1112, 316, 284, 78, "state machine", "BUSY · HOG · DMARQ · DIP · TMOUT*", CLK, F_CLK))
    o.append(box(1112, 410, 284, 78, "grant relay", "BGI → BGO, BGACKI → BGACK", CTRL, F_CTRL))

    # --- the flow, drawn as one serpentine so it reads left to right, band by band
    rows = [(128, ADDR), (222, DATA), (316, CTRL), (410, CTRL)]
    for r, col in rows:
        for x0, x1 in [(294, 360), (660, 736), (1036, 1112)]:
            o.append(line(x0 + 4, r + 39, x1 - 4, r + 39, col, 3, arrow=True))
    # the increments feed the adder, upward on the left column
    o.append(path("M360,355 L330,355 L330,261 L356,261", STORE, 2, arrow=True))
    o.append(path("M360,449 L330,449 L330,395 L356,395", STORE, 2, arrow=True))
    # count and state steer the address step
    o.append(path("M1112,449 L1076,449 L1076,498 L520,498 L520,432", CLK, 1.6, arrow=True, dashes="5 4"))
    # the halftone word goes into HOP
    o.append(path("M1112,167 L1060,167 L1060,60 L886,60 L886,124", STORE, 2, arrow=True))
    o.append(text(900, 54, "haltone word, selected by LINE NUMBER or by the source's low nibble (SMUDGE)", 10, MUT))
    # the old destination word is needed by the LOP and the mask
    o.append(path("M294,261 L320,261 L320,510 L1040,510 L1040,449 L1040,452", DATA, 1.6, arrow=True, dashes="5 4"))
    o.append(text(330, 526, "the old destination word, read only when the LOP or the mask needs it", 10, MUT))

    # --- what is not here
    o.append(f'<rect x="44" y="560" width="1352" height="112" rx="8" fill="{F_GREY}" stroke="{GREY}" stroke-width="1.4" stroke-dasharray="5 4"/>')
    o.append(text(64, 584, "What is deliberately not in this box", 13, INK, bold=True))
    o.append(text(64, 606, "no line mode — no octant bits, no accumulator; line drawing is SMUDGE with X COUNT = 1", 11.5, MUT))
    o.append(text(64, 626, "no memory timing — no RAS/CAS, no refresh: the cycles are U400's, this chip only asks and drives", 11.5, MUT))
    o.append(text(64, 646, "no FIFO and no prefetch — the source buffer is a shift register, not a queue", 11.5, MUT))
    o.append(text(760, 606, "no interrupt controller — one INT pin, and it duplicates BUSY", 11.5, MUT))
    o.append(text(760, 626, "no microcode, no PLA, no RAM cell: 2,133 cells of random logic", 11.5, MUT))
    o.append(text(760, 646, "no die photograph exists, so no floorplan is drawn here or anywhere", 11.5, MUT))
    o.append(text(64, 666, "* TMOUT appears only in ST-4082, the revision drawn for the combo chip", 10.5, GREY, italic=True))

    # --- the size line
    o.append(box(44, 700, 652, 96, "2,133 cell instances · 58 cell types", "fixed cells 15,706 transistors — the GSTMCU's size class", GREY, F_GREY, 14))
    o.append(box(744, 700, 652, 96, "15 sheets, four revisions", "TA-4021 Oct 86 · ST-4051 Mar 88 · BLT Jul 88 · ST-4082 Sep 88", GREY, F_GREY, 14))

    o.append(foot(h, ["Provenance: the sheet titles and net names of the recovered designs (TA-4021 text layer; ST-4051 and ST-4082 drawing strings)",
                      "and 4082.LST's cell census; the register semantics from Atari's BLiTTER manual. See README.md and the study's §9."]))
    return "".join(o)


# ---------------------------------------------------------------- 01 · one word
def sheet_word():
    h = 1020
    o = [head(W, h, "01 · One word, end to end",
              "Source read, skew, HOP, LOP, end mask, destination write — and why a word costs 4, 8 or 12 clocks.")]

    steps = [
        (44, "1 · source read", "$FF8A24 + $FF8A20", "1 access, 4 clocks — unless HOP names no source", DATA, F_DATA),
        (44, "2 · skew", "SI0–SI30, 31 stages", "SKEW 0–15; FXSR forces the line's first read", DATA, F_DATA),
        (44, "3 · halftone word", "RAM, not the bus", "LINE NUMBER, or the source's low nibble (SMUDGE)", STORE, F_STORE),
        (44, "4 · HOP", "$FF8A3A, 2 bits", "all ones · halftone · source · S AND H", CTRL, F_CTRL),
        (44, "5 · destination read", "$FF8A32 — only if needed", "the LOP's operand or a mask ≠ $FFFF", DATA, F_DATA),
        (44, "6 · LOP", "$FF8A3B, 4 bits", "the 16-entry table, one BITBLT cell", CTRL, F_CTRL),
        (44, "7 · end mask", "$FF8A28 / 2A / 2C", "first word / middle words / last word", CTRL, F_CTRL),
        (44, "8 · destination write", "$FF8A32, then step", "1 access, 4 clocks; Y INC on the line's last word", DATA, F_DATA),
    ]
    y = 118
    for x, name, reg, sub, col, fill in steps:
        o.append(box(44, y, 560, 72, name, f"{reg} — {sub}", col, fill, 13.5))
        if y > 118:
            o.append(line(324, y - 24, 324, y - 4, col, 2, arrow=True))
        y += 88

    # the read-count table, on the right
    o.append(text(660, 112, "what a word costs — the unit the model must carry", 13, INK, bold=True))
    o.append(f'<rect x="660" y="126" width="736" height="34" fill="{F_GREY}" stroke="{GREY}" stroke-width="1.2"/>')
    for x, s in [(672, "case"), (940, "reads"), (1050, "writes"), (1170, "clocks"), (1300, "when")]:
        o.append(text(x, 148, s, 11.5, INK, bold=True))
    rows = [("fill — OP 0 or F, HOP 0", "0", "1", "4", "no operand named"),
            ("copy — one operand", "1", "1", "8", "the usual blit"),
            ("masked, or a D-dependent LOP", "2", "1", "12", "mask ≠ $FFFF")]
    for i, r in enumerate(rows):
        yy = 160 + i * 46
        o.append(f'<rect x="660" y="{yy}" width="736" height="44" fill="#ffffff" stroke="{GREY}" stroke-width="1"/>')
        o.append(text(672, yy + 27, r[0], 11.5, INK))
        o.append(text(946, yy + 27, r[1], 11.5, DATA, bold=True))
        o.append(text(1058, yy + 27, r[2], 11.5, DATA, bold=True))
        o.append(text(1172, yy + 27, r[3], 12.5, CTRL, bold=True))
        o.append(text(1232, yy + 27, r[4], 10, MUT))

    o.append(text(660, 336, "The seven-field pipeline the manual draws as a block diagram", 13, INK, bold=True))
    o.append(text(660, 358, "source → << SKEW → HOP with halftone → LOP with destination → ENDMASK → new destination", 11.5, MUT))

    # the source buffer, drawn
    o.append(text(660, 402, "the 32-bit source buffer — two words in, sixteen bits out", 13, INK, bold=True))
    for i in range(32):
        x = 660 + i * 22
        fill = F_DATA if i < 16 else "#f6f6f2"
        o.append(f'<rect x="{x}" y="416" width="22" height="34" fill="{fill}" stroke="{DATA}" stroke-width="1"/>')
    o.append(text(660, 470, "SI16 … SI31 (the older word)", 10.5, MUT))
    o.append(text(1012, 470, "SI0 … SI15 (the word just read)", 10.5, MUT))
    o.append(line(660, 494, 1364, 494, DATA, 1.2, dashes="3 3"))
    o.append(text(660, 512, "on each new read the upper half moves down and the lower is replaced; a negative X increment reverses the direction", 10.5, MUT))

    # the masked-line worked example
    o.append(text(44, 862, "one line worked: 350 pixels from phase 3 — X COUNT 22, three masks", 13, INK, bold=True))
    for i, (lab, val, sub) in enumerate([("word 1 · ENDMASK 1", "$1FFF", "writes pixels 3–15"),
                                         ("words 2–21 · ENDMASK 2", "$FFFF", "writes the whole word"),
                                         ("word 22 · ENDMASK 3", "$F000", "writes pixels 0–3")]):
        x = 44 + i * 456
        o.append(box(x, 878, 440, 72, lab, f"{val} — {sub}", CTRL, F_CTRL, 12.5))
    o.append(text(44, 972, "X COUNT counts the words, the first one included; a mask that is not $FFFF costs the destination read; if X COUNT is 1, ENDMASK 1 is the mask for the only write.", 10.5, MUT))

    o.append(foot(h, ["Provenance: the register semantics from Atari's BLiTTER manual (25 Jan 1990); the per-access clock costs and the 4-clock read/write",
                      "constants from Hatari's measured model; the read-count rule from the manual's \"a read will only be performed if it is required\"."]))
    return "".join(o)


# ---------------------------------------------------------------- 02 · registers
def sheet_registers():
    h = 1000
    o = [head(W, h, "02 · The register seam",
              "62 bytes at $FF8A00: sixteen halftone words, eleven pointer registers, three end masks, and one byte of control.")]

    # the block, drawn as a column of rows
    o.append(text(44, 112, "the map, with the decode's own strobes beside it", 12, MUT, italic=True))
    rows = [("$FF8A00–$FF8A1E", "HALFTONE RAM · 16 words", "XSEL00 … XSEL1E — sixteen selects, 2-byte steps", STORE, F_STORE),
            ("$FF8A20 / 22", "SOURCE X · Y INCREMENT", "signed 15, LSB ignored", ADDR, F_ADDR),
            ("$FF8A24 / 26", "SOURCE ADDRESS (32-bit pair)", "23 bits used", ADDR, F_ADDR),
            ("$FF8A28 / 2A / 2C", "ENDMASK 1 · 2 · 3", "first word · middle · last word", CTRL, F_CTRL),
            ("$FF8A2E / 30", "DEST X · Y INCREMENT", "signed 15, LSB ignored", ADDR, F_ADDR),
            ("$FF8A32 / 34", "DESTINATION ADDRESS (32-bit pair)", "23 bits used", ADDR, F_ADDR),
            ("$FF8A36 / 38", "X COUNT · Y COUNT", "1 … 65536, and 65536 is written as 0", STORE, F_STORE),
            ("$FF8A3A", "HOP — 2 bits", "all ones · halftone · source · S AND H", CTRL, F_CTRL),
            ("$FF8A3B", "OP (LOP) — 4 bits", "the sixteen boolean combinations", CTRL, F_CTRL)]
    y = 126
    for a, n, d, col, fill in rows:
        o.append(f'<rect x="44" y="{y}" width="800" height="50" rx="6" fill="{fill}" stroke="{col}" stroke-width="1.3"/>')
        o.append(text(58, y + 21, a, 11, INK, bold=True))
        o.append(text(58, y + 40, n, 10.5, MUT))
        o.append(text(380, y + 31, d, 10.5, MUT))
        y += 56

    # the control byte, exploded
    o.append(text(44, 690, "the last four bytes — one word's worth of control, and the only place a model can go wrong", 13, INK, bold=True))
    ctrl = [(44, "$FF8A3A", ["HOP1", "HOP0", "—", "—", "—", "—", "—", "—"], CTRL, F_CTRL),
            (380, "$FF8A3B", ["—", "—", "—", "—", "OP3", "OP2", "OP1", "OP0"], CTRL, F_CTRL),
            (716, "$FF8A3C", ["BUSY", "HOG", "SMUDGE", "—", "LN3", "LN2", "LN1", "LN0"], CLK, F_CLK),
            (1052, "$FF8A3D", ["FXSR", "NFSR", "SKEW3", "SKEW2", "SKEW1", "SKEW0", "—", "—"], CLK, F_CLK)]
    for x, addr, bits, col, fill in ctrl:
        o.append(text(x, 714, addr, 11.5, INK, bold=True))
        for i, b in enumerate(bits):
            bx = x + i * 41
            used = b != "—"
            o.append(f'<rect x="{bx}" y="722" width="39" height="34" rx="3" fill="{fill if used else "#ffffff"}" stroke="{col}" stroke-width="1.1"'
                     f'{" stroke-dasharray=\"3 3\"" if not used else ""}/>')
            o.append(text(bx + 19.5, 744, b if used else "·", 9, INK if used else FAINT, anchor="middle", bold=used))
        o.append(text(x, 774, "bit 7", 9.5, MUT))
        o.append(text(x + 320, 774, "bit 0", 9.5, MUT, anchor="end"))

    o.append(text(44, 806, "BUSY is a read bit and a start bit at once: write 1 and it starts, write 0 and it pauses, and it reads 1 through the pause.", 11.5, INK))
    o.append(text(44, 826, "The interrupt line is a duplicate of BUSY, so completion is a falling edge — never a pulse. FXSR and NFSR change how many source reads a line spends.", 11.5, MUT))

    # the decode proof
    o.append(box(44, 850, 660, 104, "why the halftone block is first", "sixteen word selects at 2-byte steps, $00–$1E, is 32 bytes — the pattern, exactly", ADDR, F_ADDR, 13.5))
    o.append(box(724, 850, 672, 104, "why the rest is not word-aligned", "HOP, OP, LINE·SMUDGE·HOG·BUSY and SKEW·FXSR·NFSR are bytes inside one word", CTRL, F_CTRL, 13.5))

    o.append(foot(h, ["Provenance: the register map and every quoted field from Atari's BLiTTER manual; the strobe names (XSEL00…XSEL1E, XOPW, XHOPW, XSKEWW,",
                      "XLINEW, XSMUDGE, XFXSR, XRAMR/XRAMW) from sheet 9 of ST-4051, \"Address decode and regs\"."]))
    return "".join(o)


# ---------------------------------------------------------------- 03 · window
def sheet_window():
    h = 1010
    o = [head(W, h, "03 · The bus window",
              "Four clocks of CPU, four of arbitration, then 64 accesses of 4 clocks — and a frame budget that does not close.")]

    # the timeline
    o.append(text(44, 118, "the start, to scale in clocks", 12, MUT, italic=True))
    ty, th = 134, 56
    segs = [(44, 160, "the write lands", "BUSY set at its end", CTRL, F_CTRL),
            (204, 160, "4 clocks: CPU", "next instruction runs", DATA, F_DATA),
            (364, 160, "4 clocks: nobody", "arbitration (8 MegaSTE)", GREY, F_GREY),
            (524, 420, "the blitter owns the bus", "4 clocks per access, k accesses per word", CLK, F_CLK)]
    for x, w, name, sub, col, fill in segs:
        o.append(f'<rect x="{x}" y="{ty}" width="{w}" height="{th}" rx="4" fill="{fill}" stroke="{col}" stroke-width="1.4"/>')
        o.append(text(x + w / 2, ty + 24, name, 11 if w > 200 else 10.5, INK, anchor="middle", bold=True))
        o.append(text(x + w / 2, ty + 41, sub, 9.5, MUT, anchor="middle"))
    for x0, x1 in [(204, 204), (364, 364), (524, 524)]:
        o.append(line(x0, ty - 8, x0, ty + th + 8, INK, 1.2, dashes="3 3"))
    o.append(text(44, ty + th + 24, "t = 0", 10, MUT))
    o.append(text(204, ty + th + 24, "t = 4", 10, MUT))
    o.append(text(364, ty + th + 24, "t = 8", 10, MUT))
    o.append(text(944, ty + th + 24, "… until Y COUNT expires, then 4 clocks to release", 10, MUT))

    # the alternation
    o.append(text(44, 268, "the alternation — a duty cycle, not an average", 12, MUT, italic=True))
    ax, ay, ah = 44, 282, 44
    for i in range(8):
        x = ax + i * 168
        blit = i % 2 == 0
        fill = F_CLK if blit else F_DATA
        col = CLK if blit else DATA
        o.append(f'<rect x="{x}" y="{ay}" width="168" height="{ah}" fill="{fill}" stroke="{col}" stroke-width="1.2"/>')
        o.append(text(x + 84, ay + 20, "blitter" if blit else "CPU", 11, INK, anchor="middle", bold=True))
        o.append(text(x + 84, ay + 36, "64 accesses" if blit else "64 accesses", 9.5, MUT, anchor="middle"))
    o.append(text(44, ay + ah + 22, "64 accesses × 4 clocks = 256 clocks = 32 µs, and the blitter's state survives the hand-over — the release can fall inside a word's read-mix-write.", 11, MUT))

    # the 63 case
    o.append(box(44, 372, 660, 92, "63, sometimes", "a CPU access inside the 4-clock latency, counted as a blitter access", CTRL, F_CTRL, 13.5))
    o.append(box(724, 372, 672, 92, "TMOUT exists only in ST-4082", "the standalone revisions have no timeout net at all, so the count is unknown there", GREY, F_GREY, 13.5))

    # the frame budget
    o.append(text(44, 502, "what a full screen costs — 320 × 200 × 4 planes = 16,000 words", 13, INK, bold=True))
    o.append(f'<rect x="44" y="516" width="1352" height="32" fill="{F_GREY}" stroke="{GREY}" stroke-width="1.2"/>')
    for x, s in [(56, "operation"), (420, "accesses"), (580, "clocks"), (740, "HOG"), (900, "non-HOG"), (1080, "a 20 ms frame")]:
        o.append(text(x, 538, s, 11.5, INK, bold=True))
    brows = [("fill — no reads at all", "16,000", "64,000", "8 ms", "~16 ms", "40 % / 80 %"),
             ("copy — one operand read", "32,000", "128,000", "16 ms", "~32 ms", "80 % / 160 %"),
             ("masked copy — two reads", "48,000", "192,000", "24 ms", "~48 ms", "120 % / 240 %")]
    for i, r in enumerate(brows):
        yy = 548 + i * 40
        o.append(f'<rect x="44" y="{yy}" width="1352" height="38" fill="#ffffff" stroke="{GREY}" stroke-width="1"/>')
        o.append(text(56, yy + 24, r[0], 11.5, INK))
        o.append(text(420, yy + 24, r[1], 11.5, DATA))
        o.append(text(580, yy + 24, r[2], 11.5, DATA))
        o.append(text(740, yy + 24, r[3], 12, CTRL, bold=True))
        o.append(text(900, yy + 24, r[4], 12, CTRL, bold=True))
        o.append(text(1080, yy + 24, r[5], 11, MUT))
    o.append(text(44, 700, "non-HOG is the documented 64/64 alternation: ceil(accesses / 64) blitter windows plus the CPU's equal share, so a full-screen copy does not fit in a frame.", 11, MUT))

    # the interrupt path
    o.append(text(44, 744, "and the one wire that leaves the chip", 13, INK, bold=True))
    path_boxes = [(44, 760, 250, "U101 · INT", "a level: BUSY, duplicated"),
                  (342, 760, 250, "U400 pass-through", "the GSTMCU ignores it"),
                  (640, 760, 250, "U400.60 · BINT", "an output of the GSTMCU"),
                  (938, 760, 250, "MFP · GPIP3", "bit 3, low, busy/idle"),
                  (1236, 760, 160, "software", "poll or AER edge")]
    for x, y, w, name, sub in path_boxes:
        o.append(box(x, y, w, 78, name, sub, CLK, F_CLK, 13))
    for x0, x1 in [(294, 342), (592, 640), (890, 938), (1188, 1236)]:
        o.append(line(x0 + 2, 799, x1 - 2, 799, CLK, 2.2, arrow=True))
    o.append(text(44, 862, "Because the line is a level, an interrupt needs an edge: the 68901's AER bit 3 selects it, and the falling edge is the blit's completion. TOS polls instead.", 11, MUT))

    o.append(foot(h, ["Provenance: the 64-access rule and the HOG definition from Atari's BLiTTER manual; the 4-clock acquire/latency/release and the 63 case from Hatari's",
                      "measured model; BINT's direction from board-inventory §3.4 and GPIP3 from Hatari's MFP; the frame arithmetic is this study's (§4.2)."]))
    return "".join(o)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    sheets = {"00-blocks.svg": sheet_blocks,
              "01-word.svg": sheet_word,
              "02-registers.svg": sheet_registers,
              "03-window.svg": sheet_window}
    for name, fn in sheets.items():
        svg = fn()
        ET.fromstring(svg)                      # well-formed, or raise here
        with open(os.path.join(here, name), "w", encoding="utf-8") as f:
            f.write(svg.replace("><", ">\n<"))
        print(f"{name}:  {len(svg):6d} bytes")

    over = []
    for size, label, room in _audit:
        if room is None:
            continue
        need = len(label) * size * 0.60
        if need > room:
            over.append((size, label, need, room))
    print(f"{len(over)} layout warnings")
    for size, s, need, room in over:
        print(f"  overflow: {s!r} needs {need:.0f}px in {room:.0f}px (size {size})")


if __name__ == "__main__":
    main()
