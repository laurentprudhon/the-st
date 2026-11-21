#!/usr/bin/env python3
"""Annotate the 1983 Byte magazine 68000 die photo with numbered region callouts.

The rectangles are NOT eyeballed: each one was snapped, edge by edge, onto the
yellow outline lines that Byte drew on the photograph in 1983 (detect yellow
pixels, score each candidate edge line by the fraction of its length that lies on
a yellow line, take the best line within +-16 px of the starting guess).  So the
boxes follow the original Motorola reviewer's own block outlines.

Run:  python3 annotate.py            (writes die/byte-1983-annotated.png)
Needs: pillow
"""
from PIL import Image, ImageDraw, ImageFont

SRC = "byte-1983-labelled-x3.jpg"          # 3x upscale of the 640x732 magazine photo
OUT = "byte-1983-annotated.jpg"
SCALE = 3
LEGEND_W = 1180

# group colours (r, g, b)
GROUP = {
    "io":      (120, 124, 150),   # buffers / pads
    "clock":   ( 40, 110, 175),   # clock, timer, bus control
    "store":   (118,  74, 190),   # microcode / nanocode arrays
    "decode":  (190, 120,   0),   # PLAs and decoders
    "exec":    ( 40, 140,  90),   # execution units, register file
    "special": (180,  60,  90),   # interrupt / function code
}

# n, box(x0,y0,x1,y1) in magazine pixels, short name, group, one-line explanation
BLOCKS = [
    (1,  (111,  40, 336, 144), "CLOCK GENERATORS · TIMER & E LOGIC · DECODE DRIVERS", "clock",
         "The on-chip clock conditioning and the E-clock divider; the 'decode drivers' below it fan the phases out to the rest of the chip."),
    (2,  (486,   5, 629,  96), "µP CONTROL LOGIC", "clock",
         "Bus control: the 8-state bus machine, DTACK/BERR sampling, arbitration, and the access-class decode."),
    (3,  (336,  98, 424, 143), "A0 MUX CONTROL", "clock",
         "Selects A0 / A0S over the microword's own next address — the 'take an exception now' path."),
    (4,  (423, 160, 522, 230), "A2/A3 DECODE", "decode",
         "PLA: opcode -> second and third microcode addresses (after an immediate, after an effective address)."),
    (5,  (523, 116, 628, 254), "A1 DECODE", "decode",
         "PLA: opcode -> entry microcode address. One term per opcode shape; also flags illegal and privileged."),
    (6,  ( 48, 144, 314, 224), "µROM  (microcode, 544 × 17)", "store",
         "Sequencing store: next address, branch condition, bus access class, IR load."),
    (7,  ( 47, 224, 316, 354), "NROM  (nanocode, 336 × 68)", "store",
         "Datapath store: register transfers, AU/ALU selects, flags. Many microcode words share one nanoword."),
    (8,  (423, 230, 474, 336), "BRANCH PLA", "decode",
         "Turns the 5-bit condition code plus flags into the two address bits C1/C0 that pick one of four targets."),
    (9,  (474, 230, 523, 336), "TRAP & ILL. INST. PLA", "decode",
         "Exception priority and vector selection; illegal-instruction and privilege traps."),
    (10, (474, 336, 538, 370), "IRD REG", "decode",
         "Instruction-register-delay: the opcode actually being executed, wired into the decode PLAs."),
    (11, ( 60, 354, 338, 396), "IRD BUS  (decode PLA bank)", "decode",
         "Not a bus: a bank of PLAs keyed on IRD that expands ~70 ROM control bits into ~200 datapath control lines."),
    (12, ( 60, 396, 338, 506), "CONTROL UNIT", "clock",
         "Sequencer and control distribution: the microcode address register, phase logic, and the register-write strobes."),
    (13, (436, 374, 526, 448), "ALU CONTROL", "exec",
         "The 15 × 5 ALU control matrix: row from the instruction, column from the nanoword."),
    (14, (583, 234, 629, 487), "DATA BUFFERS", "io",
         "D0-D15 pad drivers and the data bus input/output registers (DBIN / DOB)."),
    (15, ( 60, 506, 246, 676), "ADDRESS HIGH EXECUTION UNIT", "exec",
         "High 16 bits of every 32-bit register, plus the high address adder (AUH)."),
    (16, (246, 505, 413, 674), "ADDRESS LOW EXECUTION UNIT", "exec",
         "Low 16 bits of A0-A7, PC and the address temporary, plus the low adder (AUL)."),
    (17, (413, 503, 588, 674), "DATA EXECUTION UNIT", "exec",
         "Low 16 bits of D0-D7, the ALU, ALU buffer and extender, bit decoder, MOVEM encoder."),
    (18, ( 71, 676, 605, 724), "ADDRESS BUFFERS", "io",
         "A1-A23 pad drivers and the address output register (AOB)."),
    (19, (  4,  84,  48, 346), "INT LOG · FC LOG · BUFFERS", "special",
         "Interrupt priority and acknowledge, function-code generation, and the control-signal input buffers."),
]


def font(size, bold=False):
    for name in (("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf"),
                 ("DejaVuSans.ttf"), ("LiberationSans-Regular.ttf")):
        for root in ("/usr/share/fonts/truetype/dejavu/", "/usr/share/fonts/truetype/liberation/",
                     "/usr/share/fonts/", ""):
            try:
                return ImageFont.truetype(root + name, size)
            except Exception:
                continue
    return ImageFont.load_default()


def wrap(draw, text, fnt, width):
    words, lines, cur = text.split(), [], ""
    for w in words:
        t = (cur + " " + w).strip()
        if draw.textlength(t, font=fnt) <= width:
            cur = t
        else:
            lines.append(cur)
            cur = w
    if cur:
        lines.append(cur)
    return lines


def main():
    img = Image.open(SRC).convert("RGB")
    w, h = img.size
    canvas = Image.new("RGB", (w + LEGEND_W, h), (250, 249, 245))
    canvas.paste(img, (0, 0))
    d = ImageDraw.Draw(canvas, "RGBA")
    num_f = font(46, True)
    name_f = font(30, True)
    body_f = font(25)
    small_f = font(22)

    for n, (x0, y0, x1, y1), name, grp, note in BLOCKS:
        col = GROUP[grp]
        d.rectangle([x0 * SCALE, y0 * SCALE, x1 * SCALE, y1 * SCALE],
                    outline=col + (255,), width=6)
        # number badge, kept inside the image edge
        bx = min(max(x0 * SCALE, 4), w - 70)
        by = min(max(y0 * SCALE, 4), h - 70)
        d.rectangle([bx, by, bx + 62, by + 56], fill=col + (245,))
        tw = d.textlength(str(n), font=num_f)
        d.text((bx + 31 - tw / 2, by + 4), str(n), fill=(255, 255, 255), font=num_f)

    # ---- legend panel ------------------------------------------------------
    lx = w + 34
    d.rectangle([w, 0, w + LEGEND_W, h], fill=(250, 249, 245))
    y = 34
    d.text((lx, y), "MC68000 die — what is where", font=font(52, True), fill=(30, 30, 34)); y += 74
    d.text((lx, y), "Numbered boxes follow the block outlines drawn on this photograph by",
           font=small_f, fill=(90, 90, 96)); y += 30
    d.text((lx, y), "Motorola's Thomas Starnes (Byte, May 1983); box edges were snapped to",
           font=small_f, fill=(90, 90, 96)); y += 30
    d.text((lx, y), "those printed lines, not estimated; where two blocks share an edge the line",
           font=small_f, fill=(90, 90, 96)); y += 30
    d.text((lx, y), "is drawn once, so a box side can sit a few pixels off. Photograph 640×732 px, at 3×.",
           font=small_f, fill=(90, 90, 96)); y += 46

    for n, box, name, grp, note in BLOCKS:
        col = GROUP[grp]
        if y > h - 120:
            break
        d.rectangle([lx, y + 4, lx + 30, y + 30], fill=col)
        tw = d.textlength(str(n), font=small_f)
        d.text((lx + 15 - tw / 2, y + 4), str(n), fill=(255, 255, 255), font=small_f)
        d.text((lx + 44, y), name, font=name_f, fill=(30, 30, 34))
        y += 36
        for line in wrap(d, note, body_f, LEGEND_W - 80):
            d.text((lx + 44, y), line, font=body_f, fill=(70, 70, 78))
            y += 29
        y += 12

    canvas.save(OUT, quality=92, optimize=True)
    print(OUT, canvas.size)


if __name__ == "__main__":
    main()
