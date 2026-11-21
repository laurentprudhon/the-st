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
/// placements is <c>#74</c>'s and unchanged. **Where CLK and MAME disagree, MAME wins** (the owner's
/// ruling of 2026-11-19): its microcoded core is the source of truth for an instruction's clocks, so
/// <c>MUL</c>/<c>MULS</c> charge what MAME charges per source EA, <c>DIV</c>/<c>DIVS</c> its longer path,
/// and <c>MOVEM</c> its per-register count — <c>PLACEMENT.md</c> keeps CLK's numbers beside them so the
/// disagreement stays visible. CLK remains the oracle for an instruction's final state.
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
                // the long immediates are scripts (M68000.Scripts.cs): o#l1/o#w1 and the family's tail
                if (IsScriptedImmediateLong(op))
                {
                    return 0;
                }

                // …and so are the word forms: o#w1 and the word tails are their clocks
                if (IsScriptedImmediateWord(op))
                {
                    return 0;
                }

                // ORI/ANDI/EORI to SR: the status register write is its own work
                if ((op & 0xFF00) is 0x0000 or 0x0200 or 0x0A00 && (op & 0x00FF) == 0x7C)
                {
                    return 8;
                }

                // to *CCR* is 12 clocks, and uniformly so (7 500 records: every one of the three files
                // sat at exactly −12 when the charge was 0): MAME's `andi_imm8_ccr` fetches the
                // immediate word with a bus state of its own (`o#w1`, 4), rolls the counter back
                // (`stiw1`, 2) and then runs the two status states (`stiw2`, `stiw3`, 2 each) before
                // its closing prefetch — where this core pops both words out of the queue
                if ((op & 0xFF00) is 0x0000 or 0x0200 or 0x0A00 && (op & 0x00FF) == 0x3C)
                {
                    return 12;
                }

                // BTST/BCHG/BCLR/BSET with a data-register destination. The charge is the bit
                // operation's own: BTST 2, BCHG/BSET 2, BCLR 4 — and **2 more when the bit position
                // is above 15**, which is the 6-or-8 split LENGTH.md §6 could not settle from the
                // records: CLK's `did_bit_op` sets `dynamic_instruction_length_ = int(bit > 15)` and
                // `BCHG_BSET_Dn` spends `IdleBus(1 + dynamic_instruction_length_)` (`BCLR_Dn`:
                // `2 + …`). Checked against every record of the three files: 410/410 BCHG, 413/413
                // BCLR, 389/389 BSET. The handler charges it (the bit number is its own read: a
                // register form's `Dn & 31`, a static form's extension word), so the table adds nothing
                if ((op & 0x0100) != 0 || (op & 0x0F00) == 0x0800)
                {
                    return 0;
                }

                return 0;

            case 0x4:
                // JMP and JSR are scripted (M68000.Scripts.cs): their own clocks lead inside the script,
                // because a transfer never runs the closing fetch that spends `_own`
                if (IsScriptedJump(op) || IsScriptedJsr(op))
                {
                    return 0;
                }

                // NOT/CLR/NEG/NEGX/TST .l are scripted (M68000.Scripts.cs): their RMW block's states are
                // their clocks, and the row below never charged the memory shapes any
                if (IsScriptedSingleOperandLong(op))
                {
                    return 0;
                }

                // …and so are the .w forms: the word block (adrw1/adrw2 … nnmw1 + morw2) is its clocks
                if (IsScriptedSingleOperandWord(op))
                {
                    return 0;
                }

                // …and the .b forms, over the same block (this row never charged a byte anything —
                // the byte clock column was the scripts' all along; the CLR read was the one gap)
                if (IsScriptedSingleOperandByte(op))
                {
                    return 0;
                }

                // the three status moves are scripts: their idle states and the queue's refill after
                // the status write are theirs (the rows below carried the Dn form's 2 only)
                if (IsScriptedMoveToStatus(op))
                {
                    return 0;
                }

                if ((op & 0xFFF8) == 0x4840 || (op & 0xFFF8) == 0x4880 || (op & 0xFFF8) == 0x48C0)
                {
                    return 0;                                       // SWAP, EXT.W, EXT.L
                }

                // TAS: its own 2 clocks are the memory form's `tasm1`, and the handler charges them
                // between the read and the write. This row was `return 10` behind a mask that can never
                // match (`(op & 0xFF00) == 0x4AC0` masks off the byte it tests for), so it fired on
                // nothing — and the value was wrong anyway: the register form is 4 clocks and needs no
                // charge at all, so the table keeps nothing for TAS — and the row that stood here could
                // never fire anyway.

                if ((op & 0xFFF8) == 0x4180 || (op & 0xFFF8) == 0x4100)
                {
                    // CHK is scripted (M68000.Scripts.cs): chkm1/chkr1, chkr2 and chkr3 are its own
                    // six clocks — the row below was the measured shape, and it charged the trap path
                    // its success path's state
                    return IsScriptedChk(op) ? 0 : 6;
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
                // the long quicks are scripts (M68000.Scripts.cs): raql1 and the family's tail
                if (IsScriptedQuickLong(op))
                {
                    return 0;
                }

                // …and the word quicks: raqw1/maqw1 and roaw2/morw2 are theirs
                if (IsScriptedQuickWord(op))
                {
                    return 0;
                }

                if ((op & 0x00C0) != 0x00C0)
                {
                    // ADDQ/SUBQ: an address register is 4 clocks; long to memory 2, long to a register 4
                    return mode == 1 ? 4 : size == 2 ? (mode == 0 ? 4 : 2) : 0;
                }

                return 0;                                           // Scc (2 when true, set by the family), DBcc leads

            case 0x6:
                return 0;                                           // Bcc/BSR: leading, set by the family

            case 0x7:
                // MOVEQ: rlql1 and the one closing read, no idle at all (2 500 records). The line
                // falls through to AluOwnClocks otherwise, where the plain two-operand long row
                // claims it by accident — its "size" field is the MOVEQ value's top two bits and its
                // "mode" field is part of the value, so 1 081 records read 2 clocks long and 123 4
                return 0;

            case 0x1:
                // MOVE.b is the one MOVE size the port left on the table's side (IsScriptedMove is
                // lines 2 and 3), and the table never measured it: its "size" field is 3 (MOVE's
                // byte code), so the CMPA/ADDA row's `(op & 0x00C0) == 0x00C0` claims every one and
                // charges 2 (1 259 records) — and 4 where the *source* field reads as mode ≤ 1 (124).
                // MAME's move_bw blocks charge nothing of their own: the record's internal clocks are
                // the EA's 2 and nothing else (`--clocks`: L − 4 × bus is 0 or the EA's 2 on all
                // 2 500 records), so the byte form adds nothing here
                return 0;

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

    /// <summary>
    /// The two-operand ALU families: the long operations pay 2, a register/immediate source 2 more.
    /// </summary>
    int AluOwnClocks(ushort op, int line, int size, int mode)
    {
        // MOVE/MOVEA in size 1 and 2 are scripts (M68000.Scripts.cs): the source block and the
        // destination class's tail are their clocks, and the shape rows below never charged the
        // `(An)+` destinations correctly (they read size 3 off the opcode and answered 4)
        if (IsScriptedMove(op))
        {
            return 0;
        }

        // CMPM is a script of its own (M68000.Scripts.cs): its four states carry no idle clock at
        // all (the listing's footer: 6 accesses, 0 idle states), and its long form's opmode is the one
        // the two-operand long's row below reads — 82 records of CMP.l were charge 4 by it
        if (IsScriptedCmpm(op))
        {
            return 0;
        }

        // MULU/MULS/DIVU/DIVS are scripts (M68000.Scripts.cs): their loops carry both the value and its
        // clocks, so the table must not charge them at all — 2 x 16 rotates plus one add per Booth digit
        // or set bit is the multiply, one subtract per quotient bit plus a restore is the divide, and no
        // shape can state either. The test has to come *first*, because two masked rows below claim these
        // families by accident: `(op & 0xF130) == 0x8100` is meant for ADDX/SUBX and matches
        // `DIVS Dn,<any>` (0x81C5, 0x87C5 …), and `(op & 0x00C0) == 0x00C0` is meant for ADDA/SUBA/CMPA
        // and matches the word forms of the whole ALU block. Charged through them, every register form
        // came out 2 clocks long. A masked row is a claim on a *neighbour* until the port proves
        // otherwise, so a scripted family's own-clocks test belongs above all of them.
        if (IsScriptedMultiply(op) || IsScriptedDivide(op))
        {
            return 0;
        }

        // ADDX.l/SUBX.l are their own block (`asxl`/`rorl`), not the ALU tails — and they are the
        // reason the row below needed a predicate of its own: their size field at 2 lands them on the
        // ALU opmodes 2 and 6, so IsScriptedAluLong answered 0 for them by accident, and their clock
        // column was the scripts' while no script existed (the memory forms were 2 short, the
        // register forms 4)
        if (IsScriptedExchangeLong(op))
        {
            return 0;
        }

        // the ported long families are scripted (M68000.Scripts.cs): the script's own states are
        // their clocks, so the measure-by-shape table must not charge them at all
        if (IsScriptedAluLong(op))
        {
            return 0;
        }

        // …and so are their word forms, including ADDA.w/SUBA.w/CMPA.w — the rows below charge them 4
        // and 2, which is the register form's number and not the shape's
        if (IsScriptedAluWord(op))
        {
            return 0;
        }

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
            // JMP/JSR are scripted: their leading run is the script's own (and `_own` is 0), so this
            // answers true only to keep the meaning of the predicate — a transfer's clocks lead
            return IsScriptedJsr(op) || IsScriptedJump(op);
        }

        return false;
    }
}
