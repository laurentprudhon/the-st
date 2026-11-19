namespace TheST.Machine.Cpu;

/// <summary>
/// The internal clock — ADR 0009 §3's residue, measured rather than assumed.
///
/// <para>
/// A 68000 instruction costs its bus cycles (4 clocks each) <b>plus</b> the clocks it spends on no
/// bus: the effective address's own work, and the family's own execution. Those clocks are what a
/// bus-cycle-only core leaves out, and they are visible to software in exactly one way — <i>when the
/// next bus cycle starts</i>. So they are charged to the CPU's own clock here, which is choice 3 of
/// <c>tests/680x0/LENGTH.md</c> §7 (the residue on the bus timeline), not a counter.
/// </para>
///
/// <para>
/// The numbers are measured from the corpus's own cycle lists (<c>tests/680x0/LENGTH.md</c> and
/// <c>tests/680x0/PLACEMENT.md</c>): the residue <c>length − 4 × bus</c> per instruction shape, split
/// into the part the effective address owns and the part the family owns. Two independent T3 models
/// (CLK and MAME's microcoded core) agree on where inside the instruction each run of clocks sits —
/// 375 of 423 shapes — so the <i>placement</i> is not a guess either:
/// </para>
///
/// <list type="bullet">
/// <item><b>the effective address</b> charges 2 clocks, once per instruction, before the access it
/// serves, when the mode is <c>-(An)</c> or indexed (the decrement, the index arithmetic);</item>
/// <item><b>the family's own work</b> trails — charged before the instruction's closing fetch
/// (the shifts' <c>2 + 2 × count</c>, a divide's 76–150, <c>CHK</c>'s 6, <c>Scc</c>'s 2, the long
/// ALU's 2/4);</item>
/// <item><b>a control transfer's</b> clocks lead — charged before its refill fetches (a branch's 4
/// not taken and 2 taken, <c>JMP</c>/<c>JSR</c>'s 2 or 4, <c>Bcc</c>/<c>DBcc</c>);</item>
/// <item><b>an exception entry</b> charges 4 before the frame's writes and 2 between the handler's
/// two fetches (the <c>n 4 … n 2</c> every entry's trace shows).</item>
/// </list>
///
/// <para>
/// What is <b>not</b> here: the exception entry's own bus cycles (the frame, the vector) — those are
/// cycles the core already performs — and the <c>MOVE</c> into an odd <c>(xxx).l</c> destination, whose
/// placement is <c>#74</c>'s and unchanged. Where CLK and MAME disagree the corpus is CLK, and the
/// disagreement is named in <c>PLACEMENT.md</c> §4 (<c>MUL</c>/<c>DIV</c> differ by 2, <c>MOVEM</c>
/// differs in its register count).
/// </para>
/// </summary>
public sealed partial class M68000
{
    /// <summary>
    /// The clocks an instruction spends off the bus, from its own encoding — the family's share with
    /// the effective address's 2 clocks excluded (the EA charges its own, once, in
    /// <see cref="EaAddressAsync"/>). Data-dependent families (<c>MUL</c>, <c>DIV</c>) fill the value
    /// in when they have read their operand.
    /// </summary>
    int InstructionOwnClocks(ushort op)
    {
        int line = op >> 12;
        int size = (op >> 6) & 0x3;
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;

        switch (line)
        {
            case 0x0:
                // ORI/ANDI/EORI to CCR or SR: the status register write is its own work
                if ((op & 0xFF00) is 0x0000 or 0x0200 or 0x0A00 && (op & 0x00C0) == 0x00C0)
                {
                    return 8;
                }

                // BTST/BCHG/BCLR/BSET with a data-register destination: BTST 2, BCHG/BSET 2, BCLR 4.
                // The corpus splits BCHG/BSET/BCLR 6-or-8 on that shape with nothing in the record to
                // separate the two (LENGTH.md §6); the majority is charged here.
                if ((op & 0x0100) != 0 || (mode == 0))
                {
                    if (mode == 0)
                    {
                        int operation = (op >> 6) & 0x3;
                        return operation == 2 ? 4 : 2;
                    }
                }

                return 0;

            case 0x4:
                // JMP/JSR: the control transfer's clocks lead (see LeadsOwnClocks)
                if ((op & 0xFFC0) == 0x4E80 || (op & 0xFFC0) == 0x4E90)
                {
                    return mode switch
                    {
                        6 => 4,                                     // indexed: 2 of it is the EA's
                        5 or 7 => 2,                                // (d16,An), (xxx).w/.l, (d16,PC)
                        _ => 0,                                     // (An)
                    };
                }

                if ((op & 0xFFF8) == 0x4840 || (op & 0xFFF8) == 0x4880 || (op & 0xFFF8) == 0x48C0)
                {
                    return 0;                                       // SWAP, EXT.W, EXT.L
                }

                if ((op & 0xFF00) == 0x4AC0)
                {
                    return 10;                                      // TAS: the reference's `t` cycle
                }

                if ((op & 0xFFF8) == 0x4180 || (op & 0xFFF8) == 0x4100)
                {
                    return 6;                                       // CHK
                }

                if (op == 0x4E70)
                {
                    return 128;                                     // RESET: the pin's asserted clocks
                }

                if ((op & 0xFFC0) == 0x4E40)
                {
                    return 0;                                       // TRAP: the entry charges its own 4
                }

                if ((op & 0xFFC0) == 0x4000)
                {
                    return size == 2 && mode == 0 ? 2 : 0;           // MOVE from SR, long to a register
                }

                // CLR/NEG/NEGX/NOT/TST and the single-operand family: a long operation's own 2
                if ((op & 0xFF00) is 0x4200 or 0x4400 or 0x4600 or 0x4000 or 0x4800 or 0x4A00)
                {
                    return size == 2 && mode == 0 ? 2 : 0;
                }

                return 0;                                           // MOVEM, LINK, UNLK, RTS, RTR, RTE

            case 0x5:
                if ((op & 0x00C0) != 0x00C0)
                {
                    // ADDQ/SUBQ: an address register is 4 clocks; long to memory 2, long to a register 4
                    return mode == 1 ? 4 : size == 2 ? (mode == 0 ? 4 : 2) : 0;
                }

                return 0;                                           // Scc (2 when true, set by the family), DBcc leads

            case 0x6:
                return 0;                                           // Bcc/BSR: leading, set by the family

            case 0xE:
                // shifts and rotates: 2 + 2 × count (4 + 2 × count for long); the memory forms charge
                // nothing of their own
                if ((op & 0x00C0) == 0x00C0)
                {
                    return 0;
                }

                bool fromRegister = (op & 0x0020) != 0;
                int count = fromRegister ? (int)(_d[(op >> 9) & 0x7] & 63) : (op >> 9) & 0x7;
                if (!fromRegister && count == 0)
                {
                    count = 8;                                       // an immediate of 0 means 8
                }

                if (!IsShift(op))
                {
                    return 0;
                }

                return (size == 2 ? 4 : 2) + 2 * count;

            default:
                return AluOwnClocks(op, line, size, mode);
        }
    }

    /// <summary>The two-operand ALU families: the long operations pay 2, a register/immediate source 2 more.</summary>
    int AluOwnClocks(ushort op, int line, int size, int mode)
    {
        // EXG: 2 clocks on every form
        if ((op & 0xF1F8) == 0xC140 || (op & 0xF1F8) == 0xC148 || (op & 0xF1F8) == 0xC188)
        {
            return 2;
        }

        // ABCD/SBCD: 2 clocks, both forms (the corpus's own run is 2 for Dn,Dn and for -(An),-(An))
        if ((op & 0xF1F0) == 0xC100 || (op & 0xF1F0) == 0x8100)
        {
            return 2;
        }

        // ADDX/SUBX: 2 on a data register (4 when long), nothing on memory
        if ((op & 0xF130) == 0x0100 || (op & 0xF130) == 0x8100)
        {
            return mode == 0 ? (size == 2 ? 4 : 2) : 0;
        }

        // ADDA/SUBA/CMPA: the word forms pay 4, the long ones 2 for memory and 4 for a register
        if ((op & 0x00C0) == 0x00C0)
        {
            int opmode = (op >> 6) & 0x7;
            if (opmode == 3 || opmode == 7)
            {
                return 2;                                       // CMPA.w/l
            }

            return size == 2 ? (mode <= 1 ? 4 : 2) : 4;          // ADDA/SUBA: 4 on .w, 2/4 on .l
        }

        // MULU/MULS/DIVU/DIVS: data-dependent, filled in by the family once the operand is read
        if ((op & 0x01C0) == 0x01C0)
        {
            return 0;
        }

        // the plain two-operand families: 0 for .b/.w, 2 for a long memory source and 4 for a long
        // register or immediate one
        if (size != 2)
        {
            return 0;
        }

        return mode <= 1 ? 4 : 2;
    }

    static bool IsShift(ushort op)
    {
        return true;                                            // every kind: AS, LS, ROX and RO
    }

    /// <summary>
    /// True for the families whose clocks <b>lead</b>: a branch, a jump and a status-register branch
    /// charge before their refill fetches, because that is where both corpora's traces put them.
    /// </summary>
    static bool LeadsOwnClocks(ushort op)
    {
        int line = op >> 12;
        if (line == 0x6)
        {
            return true;                                        // Bcc, BSR
        }

        if (line == 0x5)
        {
            return (op & 0x00C0) == 0x00C0 && (op & 0xFF00) == 0x5000;   // DBcc only, not Scc
        }

        if (line == 0x4)
        {
            return (op & 0xFFC0) == 0x4E80 || (op & 0xFFC0) == 0x4E90;   // JMP, JSR
        }

        return false;
    }
}
