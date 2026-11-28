namespace TheST.Machine.Cpu;

/// <summary>
/// The instruction scripts: one method per microcode script, written in the elementary operations of
/// <c>M68000.Micro.cs</c> and in the microcode's own order (ticket #78).
///
/// <para>
/// The expansion source is the generated C++ for the same opcode shape —
/// <c>python3 research/vendor/m68000/m68000gen.py sdf research/vendor/m68000/m68000.lst out.cpp</c>
/// then <c>void m68000_device::or_l_dd_ais_df()</c> — and the verdict is the corpus record's own
/// transaction list. Each method's comment names the states it expands in order, so the three things
/// this file exists to get right can be read off it: <b>how many reads and writes</b>, <b>where the
/// one idle run sits</b>, and <b>when a register write commits</b>.
/// </para>
///
/// <para>
/// The families are ported one at a time and the predicate beside them is the port's own ledger: a
/// family is scripted when the decode routes it here and its entry in <c>M68000.Timing.cs</c> is
/// gone, because a script carries its own clocks. <b>OR.l was the first</b>; the two-operand longs
/// followed (AND, EOR, ADD, SUB, CMP) and their address-register forms (ADDA, SUBA, CMPA). All of
/// them are the <em>same</em> state blocks: the operand's two words, three destination tails, and the
/// shape's closing idle run — so a family is a dispatch (<see cref="AluLongShapeAsync"/>) and an
/// <see cref="AluLong"/> value, not a copy.
/// </para>
/// </summary>
public sealed partial class M68000
{
    /// <summary>
    /// The families whose clocks are carried by a script instead of <c>M68000.Timing.cs</c>'s table.
    /// This predicate is the ledger: a family is added here when its script lands, and its row in the
    /// table is deleted — one place to read what is ported and what is still measured by shape.
    /// </summary>
    /// <remarks>
    /// The encoding, not a list of mnemonics: the two-operand longs are opmode 2 (<c>&lt;ea&gt;,Dn</c>)
    /// and opmode 6 (<c>Dn,&lt;ea&gt;</c>) on lines 8, 9, B, C and D, and the address-register forms are
    /// opmode 7 on lines 9, B and D. Opmode 7 on line 8 is <c>DIVS</c> and on line C is <c>MULS</c>,
    /// which are not ALU scripts; <c>EXG Dn,An</c> shares line C's opmode 6, and <c>CMPM.l</c> shares
    /// line B's (<c>CMPM</c>'s size field is two bits wide, so its long form lands on the opmode
    /// <c>EOR</c> uses).
    /// </remarks>
    static bool IsScriptedAluLong(ushort op)
    {
        int line = op >> 12;
        int opmode = (op >> 6) & 0x7;

        if (line is not (0x8 or 0x9 or 0xB or 0xC or 0xD)
            || (op & 0xF1F8) == 0xC188                          // EXG Dn,An
            || IsScriptedCmpm(op))                              // CMPM — *not* CMPA.l with a mode-1 source
        {
            return false;
        }

        return opmode is 2 or 6 || (opmode == 7 && line is 0x9 or 0xB or 0xD);
    }

    /// <summary>
    /// Line 0's <b>long immediates</b> — `ori`/`andi`/`eori`/`addi`/`subi`/`cmpi` in size 2, whose
    /// operand block is the two program reads `o#l1`/`o#w1` and whose destination is the `mode.reg`
    /// field. Not the bit operations (`0x08xx` static, `0x01xx` with a register bit number), not
    /// `MOVEP`, and not the to-CCR/to-SR forms (`mode 7, reg 4`), which are their own block — and not
    /// the byte and word sizes, which still measure by shape.
    /// </summary>
    static bool IsScriptedImmediateLong(ushort op)
    {
        int line = op >> 12;
        int size = (op >> 6) & 0x3;
        int operation = (op >> 9) & 0x7;
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;

        // line 0, long, and bit 8 clear: 0x01xx is the register bit-operation block, 0x08xx the static
        // one (whose mnemonic field reads 4), and MOVEP lives in 0x0108-0x010F
        if (line != 0x0 || size != 2 || (op & 0x0100) != 0 || operation is 4 or 7)
        {
            return false;
        }

        return mode != 7 || reg != 4;                      // (mode 7, reg 4) is to-CCR / to-SR
    }

    /// <summary>
    /// Line 5's <b>long quicks</b> — `addq.l`/`subq.l`, a value in the opcode's own field and no
    /// extension word. Size 3 on line 5 is `Scc`/`DBcc`, which is a different family with its own
    /// leading charge.
    /// </summary>
    static bool IsScriptedQuickLong(ushort op) =>
        (op >> 12) == 0x5 && ((op >> 6) & 0x3) == 2;

    /// <summary>
    /// The two-operand families in <b>size 1</b> — `or/and/eor/add/sub .w` in both directions plus
    /// `ADDA`/`SUBA`/`CMPA` — and the two line-level families that share their tails, line 0's word
    /// immediates and line 5's word quicks (see <see cref="IsScriptedImmediateWord"/> and
    /// <see cref="IsScriptedQuickWord"/>).
    /// </summary>
    /// <remarks>
    /// The opmode field is <b>not</b> the long forms' one: it carries the size bits, so the word forms
    /// are opmode 1 (`&lt;ea&gt;,Dn`), 3 (`&lt;ea&gt;,An`) and 5 (`Dn,&lt;ea&gt;`), and opmode 7 is the
    /// long address-register form. Three neighbours hide inside those opmodes and each is another
    /// instruction: <c>DIVU</c> (line 8 opmode 3), <c>MULU</c> (line C opmode 3), <c>DIVS</c>/
    /// <c>MULS</c> (opmode 7) and <c>CMPM.w</c> (line B opmode 5 with mode 1, because <c>CMPM</c>'s size
    /// field is two bits wide). `An` direct is a source only for ADD/SUB/CMP.
    /// </remarks>
    static bool IsScriptedAluWord(ushort op)
    {
        int line = op >> 12;
        int opmode = (op >> 6) & 0x7;
        int mode = (op >> 3) & 0x7;

        if (line is not (0x8 or 0x9 or 0xB or 0xC or 0xD))
        {
            return false;
        }

        // Three more neighbours, all of them in opmode 5 with mode 0 and all of them already green:
        // `EXG Dn,Dn` shares line C's, `ADDX.w`/`SUBX.w` share lines D's and 9's, and `CMPM.w` (whose
        // size field is two bits wide) shares line B's
        if ((op & 0xF1F8) is 0xC140 or 0xC148 or 0xC188 || (op & 0xF130) is 0xD100 or 0x9100)
        {
            return false;
        }

        if (opmode == 3 || opmode == 7)
        {
            return opmode == 3 && line is 0x9 or 0xB or 0xD;    // ADDA.w/SUBA.w/CMPA.w; 8 and C are DIVU/MULU
        }

        if (opmode == 5)
        {
            return (op & 0xF138) != 0xB108 && mode != 1;        // CMPM.w shares EOR's opmode
        }

        return opmode == 1 && (mode != 1 || line is 0x9 or 0xB or 0xD);
    }

    /// <summary>
    /// Line 0's <b>word immediates</b> — `ori`/`andi`/`eori`/`addi`/`subi`/`cmpi` in size 1: `o#w1`
    /// then the destination's word tail. Same exclusions as the long forms (the bit operations, and the
    /// to-CCR/to-SR block, which is `mode 7, reg 4` and its own script).
    /// </summary>
    static bool IsScriptedImmediateWord(ushort op)
    {
        if ((op >> 12) != 0x0 || ((op >> 6) & 0x3) != 1 || (op & 0x0100) != 0)
        {
            return false;
        }

        if (((op >> 9) & 0x7) is 4 or 7 || ((op >> 3) & 0x7) == 1)
        {
            return false;                                        // the bit operations, and `An` direct
        }

        return (op & 0x00FF) is not (0x3C or 0x7C);              // to CCR / to SR
    }

    /// <summary>
    /// Line 5's <b>word quicks</b> — `addq.w`/`subq.w`, the value in the opcode and no extension word.
    /// Size 3 on line 5 is `Scc`/`DBcc`, which is another family with a leading charge.
    /// </summary>
    static bool IsScriptedQuickWord(ushort op) =>
        (op >> 12) == 0x5 && ((op >> 6) & 0x3) == 1;

    /// <summary>
    /// The <b>control transfers</b> — `BSR` (line 6's condition field 1, not the conditional branches),
    /// `JMP` and `JSR` (line 4's two opcode ranges) and `UNLK`. Their scripts are in this file;
    /// `M68000.Timing.cs` charges nothing for them.
    /// </summary>
    static bool IsScriptedBranch(ushort op) => (op >> 12) == 0x6 && ((op >> 8) & 0xF) == 1;

    static bool IsScriptedJump(ushort op) => (op & 0xFFC0) is 0x4EC0 or 0x4ED0;

    static bool IsScriptedJsr(ushort op) => (op & 0xFFC0) == 0x4E80;

    static bool IsScriptedUnlink(ushort op) => (op & 0xFFF8) == 0x4E58;

    /// <summary>
    /// <b>MOVE</b> in size 1 and 2, and <b>MOVEA</b> (which is the same instruction with destination
    /// mode 1). Lines 3 and 2 carry nothing else, so the test is the line; size 0 (line 1) stays
    /// measured by shape.
    /// </summary>
    /// <remarks>
    /// The family's whole difficulty is that its destination tail is <em>not</em> the ALU families'
    /// tail for the same effective address: a MOVE writes and then issues its closing read, where
    /// `morw1`/`morl1` fetch first, so the read's position inside the instruction is per destination
    /// class and set by the script. The source block is the ALU families' own, unchanged
    /// (<see cref="ReadWordSourceAsync"/>, <see cref="ReadLongSourceAsync"/>).
    /// </remarks>
    static bool IsScriptedMove(ushort op) => (op >> 12) is 0x2 or 0x3;

    // =========================================================================================
    // The two-operand longs — `or/and/eor/add/sub .l` in both directions, and ADDA/SUBA/CMPA
    // =========================================================================================

    /// <summary>
    /// The dispatch of one line's family: the line picks the ALU's operation, the opmode picks where
    /// the result goes — a data register (<c>&lt;ea&gt;,Dn</c>), memory (<c>Dn,&lt;ea&gt;</c>) or an
    /// address register (<c>ADDA</c>/<c>SUBA</c>). Line B carries three scripts and is the one line
    /// whose opmode is not enough: CMP reads and sets flags, CMPA does the same against <c>An</c>, and
    /// EOR writes.
    /// </summary>
    async ValueTask AluLongAsync(ushort op)
    {
        switch (op >> 12)
        {
            case 0x8:
                await AluLongShapeAsync(op, AluLong.Or);
                return;

            case 0x9:
                await AluLongShapeAsync(op, AluLong.Sub);
                return;

            case 0xC:
                await AluLongShapeAsync(op, AluLong.And);
                return;

            case 0xD:
                await AluLongShapeAsync(op, AluLong.Add);
                return;

            case 0xB:
                if (((op >> 6) & 0x7) == 6)
                {
                    await AluLongShapeAsync(op, AluLong.Eor);     // eor_l_dd_<shape>
                }
                else
                {
                    await CompareLongAsync(op);                   // cmp_l_<shape>_dd / cmpa_l_<shape>_ad
                }

                return;
        }
    }

    /// <summary>
    /// One family in its three directions. The operand's read block is shared by all of them; only the
    /// tail differs, and `ADD.l An,Dn` (mode 1) is the shape OR.l does not have.
    /// </summary>
    async ValueTask AluLongShapeAsync(ushort op, AluLong kind)
    {
        int opmode = (op >> 6) & 0x7;
        int rx = (op >> 9) & 0x7;
        int mode = FtuMode;
        int reg = FtuRegister;

        if (opmode == 2)                                   // <ea>, Dn — the destination is a data register
        {
            uint? source = await ReadLongSourceAsync(mode, reg);
            if (source is null)
            {
                return;
            }

            await AluIntoDataRegisterAsync(rx, kind, source.Value, clocks: ClosingIdleClocks(mode, reg));
            return;
        }

        if (opmode == 6)                                   // Dn, <ea>
        {
            if (mode == 0)                                 // eor_l_dd_ds — the destination is a *register*
            {
                if (kind != AluLong.Eor)
                {
                    return;                                // OR/AND/ADD/SUB have no Dn,Dn form here
                }

                await AluIntoDataRegisterAsync(reg, kind, _d[rx], clocks: 4);   // roal1..roal4
                return;
            }

            if (mode == 1 || (mode == 7 && reg > 1))
            {
                return;                                    // nothing alterable in memory here
            }

            uint? address = await ReadLongEaAsync(mode, reg);
            if (address is null)
            {
                return;
            }

            await AluIntoMemoryLongAsync(_d[rx], address.Value, kind);
            return;
        }

        // opmode 7 — ADDA.l / SUBA.l: the destination is an address register and the flags do not move
        uint? operand = await ReadLongSourceAsync(mode, reg);
        if (operand is null)
        {
            return;
        }

        await AluIntoAddressRegisterAsync(rx, kind, operand.Value, clocks: ClosingIdleClocks(mode, reg));
    }

    /// <summary>
    /// The shape's closing idle run: two micro-states (4 clocks) for a source that misses the bus —
    /// a data or address register direct, an immediate — and one (2 clocks) for a memory operand.
    /// </summary>
    static int ClosingIdleClocks(int mode, int reg) => mode <= 1 || (mode == 7 && reg == 4) ? 4 : 2;

    // ---- the operand's two words, one block per EA shape ------------------------------------

    /// <summary>
    /// adrl1, adrl2 — a long operand at an address: the high word, then the low one. The high word is
    /// what the script latches into <c>m_alub</c>/<c>m_alue</c>; the low one stays in <c>m_dbin</c>.
    /// </summary>
    /// <remarks>
    /// The PC-relative shapes use <see cref="ReadLongOperandFromStreamAsync"/> instead: their first
    /// word carries the instruction stream's bus status.
    /// </remarks>
    async ValueTask ReadLongOperandAsync(uint address)
    {
        // adrl1, adrl2 — the operand's two words, each state one read (`microcode-walk.py --op 8090`).
        //
        //   ours: adrl2's latch (`m_alub = m_dbin; m_alue = m_dbin`) is bound to the *first* read's
        //   result here, because that read is what fills `m_dbin`; the adder's moves are the derivation's.
        //
        // adrl1 (00b)  m_aob = m_da[ry]; m_au = m_da[ry] + 2; alu_and(m_dbin, 0xffff); read at m_aob  4
        _alub = await ReadWordAsync(address);              // adrl1 — m_dbin -> m_alub, m_alue
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        // adrl2 (005)  m_aob = m_au; m_alub = m_dbin; m_alue = m_dbin; m_at = m_au; m_au = m_pc + 2;
        //              read at m_aob                                                                  4
        _dbin = await ReadWordAsync(address + 2);          // adrl2 — m_dbin
    }

    /// <summary>
    /// adsl2, adsl3 as the PC-relative shapes read them — the high word with the instruction stream's
    /// bus status (<see cref="ReadOperandProgramAsync"/>), the low one as a data read.
    /// </summary>
    async ValueTask ReadLongOperandFromStreamAsync(uint address)
    {
        // adsl2, adsl3 — the PC-relative shapes' pair: the same two reads, the first one a program read
        // so a fault frames an access class (`--op 80b1`).
        //
        // adsl2 (00c)  m_aob = m_au; m_au = m_au + 2; alu_and(m_dbin, 0xffff); read at m_aob          4
        _alub = await ReadOperandProgramAsync(address);    // adsl2 — SSW_PROGRAM | SSW_R
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        // adsl3 (026)  m_aob = m_au; m_alub = m_dbin; m_alue = m_dbin; m_at = m_au; m_au = m_pc + 4;
        //              read at m_aob                                                                  4
        _dbin = await ReadWordAsync(address + 2);          // adsl3 — SSW_DATA | SSW_R
    }

    /// <summary>
    /// The source operand of a script, its two halves left in <c>m_alub</c>/<c>m_dbin</c>: a register
    /// direct (no bus cycle at all), an immediate or a PC-relative shape (both out of the queue), or
    /// anything with an address. <c>null</c> when a bus cycle faulted, which ends the instruction.
    /// </summary>
    async ValueTask<uint?> ReadLongSourceAsync(int mode, int reg)
    {
        switch (mode)
        {
            case 0:                                        // ds — rorl1's operand is the register
                _alub = High16(_d[reg]);
                _dbin = (ushort)Low16(_d[reg]);
                return _d[reg];

            case 1:                                        // as — ADD.l An,Dn / SUB.l An,Dn
                _alub = High16(GetA(reg));
                _dbin = (ushort)Low16(GetA(reg));
                return GetA(reg);

            case 7 when reg >= 2:
                return await ReadLongStreamOrImmediateAsync(reg) ? Merge16_32(_alub, _dbin) : null;

            default:
                return await ReadLongEaAsync(mode, reg) is null ? null : Merge16_32(_alub, _dbin);
        }
    }

    /// <summary>
    /// The two shapes whose operand is not at an address the EA machine builds: <c>(d16,PC)</c>,
    /// <c>(d8,PC,Xn)</c> — read out of the instruction stream, and their first word carries the
    /// stream's bus status — and an immediate, whose two words are the queue's own.
    /// </summary>
    async ValueTask<bool> ReadLongStreamOrImmediateAsync(int reg)
    {
        if (reg == 4)                                      // imm32 — e#l1, e#w1
        {
            _alub = await FetchAsync();
            if (_busFaulted) return false;
            _dbin = await FetchAsync();
            return !_busFaulted;
        }

        if (reg == 2)                                      // dpc — adsl1
        {
            ushort displacement = await FetchAsync();
            if (_busFaulted) return false;
            await ReadLongOperandFromStreamAsync(ConsumedAddress + Ext32(displacement));
            return !_busFaulted;
        }

        // dpci — aixw0 (2 clocks), then the refill, adsl2, adsl3
        await IdleAsync();
        ushort extension = await FetchAsync();
        if (_busFaulted) return false;
        await ReadLongOperandFromStreamAsync(
            ConsumedAddress + (uint)ExtensionDisplacement(extension) + IndexValue(extension));
        return !_busFaulted;
    }

    /// <summary>
    /// The operand's address for the shapes that have one — the same states the source side uses —
    /// the two words read, and the post-increment's commit. <c>null</c> when a bus cycle faulted.
    /// </summary>
    /// <remarks>
    /// The increment commits <em>after</em> both words (<c>pinl3</c>), so a word pair that faults
    /// leaves the register where it was; the predecrement's own decrement commits before the read
    /// (<c>pdcl2</c>), because the CPU has already moved the register when the cycle is issued.
    /// </remarks>
    async ValueTask<uint?> ReadLongEaAsync(int mode, int reg)
    {
        uint address;

        switch (mode)
        {
            case 2:                                        // ais — adrl1's address
                address = GetA(reg);
                break;

            case 3:                                        // aips — pinl1's address
                address = GetA(reg);
                break;

            case 4:                                        // pais — pdcl1 (2 clocks), pdcl2 (the decimal)
                address = GetA(reg) - 4;
                await IdleAsync();
                SetA(reg, address);
                break;

            case 5:                                        // das — adsl1, out of the queue
            {
                ushort displacement = await FetchAsync();
                if (_busFaulted) return null;
                address = GetA(reg) + Ext32(displacement);
                break;
            }

            case 6:                                        // dais — aixw0 (2 clocks), then the refill
                await IdleAsync();
                {
                    ushort extension = await FetchAsync();
                    if (_busFaulted) return null;
                    address = GetA(reg) + (uint)ExtensionDisplacement(extension) + IndexValue(extension);
                }

                break;

            default:                                       // mode 7 — the absolute shapes only
                if (reg == 0)
                {
                    BeginFetchState();                     // abww1 — m_pc = m_au
                    ushort absolute = await FetchAsync();
                    if (_busFaulted) return null;
                    address = Ext32(absolute);
                }
                else
                {
                    ushort high = await FetchAsync();      // ablw1 — no m_pc of its own
                    if (_busFaulted) return null;
                    BeginFetchState();                     // ablw2 — m_pc = m_au
                    ushort low = await FetchAsync();
                    if (_busFaulted) return null;
                    address = Merge16_32(high, low);
                }

                break;
        }

        await ReadLongOperandAsync(address);
        if (_busFaulted) return null;

        if (mode == 3)
        {
            SetA(reg, address + 4);                        // pinl3 — the increment commits, after the words
        }

        return address;
    }

    // ---- the destination tails, one per place a result goes --------------------------------

    /// <summary>
    /// roml1, roml2, 25e, 25d (rorl/roal/cprl for the other families) — the operand ALUed into Dn,
    /// word by word: the low half and the instruction's own prefetch between the halves, then the high
    /// half. <paramref name="clocks"/> is the shape's closing idle run (see
    /// <see cref="ClosingIdleClocks"/>).
    /// </summary>
    async ValueTask AluIntoDataRegisterAsync(int rx, AluLong kind, uint source, int clocks)
    {
        uint destination = _d[rx];

        AluLongOp(source, destination, kind);              // roml1 — alu_or/alu_and/…(source, Dn)
        SetLongFlags(kind, source, destination, _aluo, extend: kind is AluLong.Add or AluLong.Sub);
        uint result = _aluo;

        Set16L(rx, Low16(result));                         // 25c — set_16l

        await TrailingFetchAsync();                        // roml2 — the one read of the instruction
        if (_busFaulted) return;

        Set16H(rx, High16(result));                        // 25d — set_16h

        await IdleAsync(clocks);                           // the shape's closing idle clocks
    }

    /// <summary>
    /// The same word-by-word ALU into an <b>address</b> register (ADDA/SUBA): the two
    /// <c>set_16l</c>/<c>set_16h</c> writes are the destination's, and the flags do not move at all —
    /// MAME's nanocode says <c>i=.......</c> on every state of these shapes.
    /// </summary>
    async ValueTask AluIntoAddressRegisterAsync(int rx, AluLong kind, uint source, int clocks)
    {
        uint destination = GetA(rx);

        AluLongOp(source, destination, kind);              // roml1 — alu_add(m_dbin, m_da[rx])
        uint result = _aluo;

        SetA(rx, (destination & 0xFFFF0000) | Low16(result));    // 25c — set_16l on An

        await TrailingFetchAsync();                        // roml2
        if (_busFaulted) return;

        SetA(rx, (GetA(rx) & 0xFFFF) | (High16(result) << 16));  // 25d — set_16h on An

        await IdleAsync(clocks);
    }

    /// <summary>
    /// morl1, morl2, morw2 — the destination's words ALUed with Dn and written back. The prefetch is
    /// <b>before</b> the writes (that is what a faulting or an interrupted instruction's frame saves),
    /// and the low word goes out first: the chip's ALU is sixteen bits wide, so a long write is two
    /// word cycles, `m_au - 2` in between.
    /// </summary>
    async ValueTask AluIntoMemoryLongAsync(uint source, uint address, AluLong kind)
    {
        // morl1 (29d)  m_aob = m_au; m_ir = m_irc; m_pc = m_au; m_au = m_at; alu_or(m_da[rx], m_dbin);
        //              sr_nzvc(); read at m_aob                                                       4
        await TrailingFetchAsync();                        // morl1 — m_aob = m_au, the one read
        if (_busFaulted) return;

        uint destination = Merge16_32(_alub, _dbin);

        AluLongOp(source, destination, kind);              // morl1/morl2 — the low half, then the carry
        SetLongFlags(kind, source, destination, _aluo, extend: kind is AluLong.Add or AluLong.Sub);
        uint result = _aluo;

        // morl2 (34d)  m_aob = m_au; m_dbout = m_aluo; m_au = m_au - 2;
        //              alu_or(high16(m_da[rx]), m_alue); sr_nzvc_u(); write m_dbout at m_aob          4
        await WriteWordAsync(address + 2, (ushort)Low16(result));   // morl2 — m_aob = m_at + 2
        if (_busFaulted) return;
        // morw2 (38d)  m_aob = m_au; m_ird = m_ir; m_dbout = m_aluo; m_au = m_pc + 2;
        //              write m_dbout at m_aob                                                         4
        await WriteWordAsync(address, (ushort)High16(result));      // morw2 — m_au -= 2, the high word
    }

    /// <summary>
    /// CMP.l / CMPA.l — cpml1, cpml2, rcal3. Nothing is written back, so both halves are subtracted
    /// <em>before</em> the closing prefetch (the destination's words in the OR tail are what put a
    /// fetch between the halves there), and the flags are all the instruction leaves. X is untouched:
    /// a comparison is not an arithmetic operation.
    /// </summary>
    async ValueTask CompareLongAsync(ushort op)
    {
        int rx = (op >> 9) & 0x7;
        int mode = FtuMode;
        int reg = FtuRegister;

        // CMPA.l compares against an address register: its opmode 7 is the operation, not a mode
        bool toAddressRegister = ((op >> 6) & 0x7) == 7;

        uint? source = await ReadLongSourceAsync(mode, reg);
        if (source is null)
        {
            return;
        }

        uint destination = toAddressRegister ? GetA(rx) : _d[rx];
        await CompareLongTailAsync(source.Value, destination, clocks: 2);
    }

    /// <summary>
    /// The comparison's tail, shared by CMP.l, CMPA.l and CMPI.l: cpml1, cpml2 (rcal1, rcal2 for the
    /// CMPI shapes), the closing prefetch, then rcal3's idle. Both ALU halves run <em>before</em> the
    /// fetch — a comparison writes nothing, so nothing else puts a read between them. X is untouched:
    /// a comparison is not an arithmetic operation.
    /// </summary>
    /// <param name="clocks">
    /// rcal3's run: 2 for a shape whose script ends on it (`cmp.l <ea>,Dn`, `cmpi.l #,Dn`), 0 for one
    /// that ends on the prefetch (`cpdl2` — the CMPI shapes with a memory destination).
    /// </param>
    async ValueTask CompareLongTailAsync(uint source, uint destination, int clocks)
    {
        AluLongOp(source, destination, AluLong.Sub);       // cpml1/rcal1 — alu_sub
        SetLongFlags(AluLong.Sub, source, destination, _aluo, extend: false);

        await TrailingFetchAsync();                        // cpml2/rcal2 — the one read
        if (_busFaulted) return;

        await IdleAsync(clocks);                           // 08d rcal3
    }

    // =========================================================================================
    // CMPM — the one family whose two operands are both `(Ay)+`, and whose register writes are
    // what its corpus records actually disagree about
    // =========================================================================================

    /// <summary>
    /// <b>CMPM</b> — <c>cmpm.b/.w/.l (Ay)+,(Ax)+</c>, line B opmode 5 with the source mode 1. The
    /// size field is two bits wide (a byte form of a line-B instruction is size 0, but MAME's
    /// <c>cmpm</c> block is one block for 0 and 1), so the long form lands on <c>EOR</c>'s opmode and
    /// the word form on the word ALU's: the exclusion in <see cref="IsScriptedAluLong"/> and
    /// <see cref="IsScriptedAluWord"/> is this family, and size 3 (<c>CMPM</c> takes no size 3) stays
    /// out of the script.
    /// </summary>
    static bool IsScriptedCmpm(ushort op) =>
        (op & 0xF138) == 0xB108 && ((op >> 6) & 0x3) != 3;

    /// <summary>
    /// <c>cmpm</c> — <c>cmpm_b/w/l_aips_aipd</c>, one state list for sizes 0 and 1 (<c>cmmw1</c>…
    /// <c>cmmw4</c>) and a four-read one for size 2 (<c>cmml1</c>…<c>cmml7</c>). Three facts the
    /// generic <c>CMP</c> handler could not have, each measured on the last 162/165 records of
    /// <c>CMP.w</c>/<c>CMP.l</c>:
    /// <list type="number">
    /// <item><b>the frame's counter moves before the first read</b> — <c>cmmw1</c> is
    /// <c>m_pc = m_au</c>, so a source fault frames the queue's word (<see cref="BeginFetchState"/>);
    /// 115 records of <c>CMP.w</c> saved 2 too low without it;</item>
    /// <item><b>the destination's increment lands after its read</b> — <c>cmmw4</c>/<c>cmml6</c> is
    /// <c>m_da[rx] = m_au</c>, with the read at <c>cmmw3</c>/<c>cmml4</c>…<c>cmml5</c> before it, so a
    /// destination fault leaves <c>Ax</c> where it stood (47 <c>CMP.w</c> and 165 <c>CMP.l</c> records
    /// were 2 high, the long ones 2 high by the first word's step: <c>cmml2</c> commits
    /// <c>A + 2</c> and <c>cmml3</c> <c>A + 4</c>, so a long source fault is a 2-clock step and not
    /// a 4);</item>
    /// <item><b>no idle states</b> — the listing's own footer (<c># bus accesses … idle states: 0</c>),
    /// which is why the family's entry in <c>M68000.Timing.cs</c> has to be zero: the long form's
    /// opmode was charged the two-operand long's 4 on 82 records of <c>CMP.l</c>.</item>
    /// </list>
    /// A faulting record's clocks are the aborted read's own 4 (<see cref="ChargeAbortedReadAsync"/>)
    /// on top of the cycle's, at every read and in both sizes.
    /// </summary>
    async ValueTask CompareMemoryAsync(ushort op)
    {
        int rx = (op >> 9) & 0x7;                          // the destination: the second An
        int ry = op & 0x7;                                 // the source
        int size = FtuSize;

        // Two listings, one per width (`microcode-walk.py --op b108` for the byte and the word,
        // `--op b188` for the long) and the same shape: the source's word(s), stepped before the read,
        // then the destination's, then the flags with the closing read.
        //
        //   ours: the adder's moves (`m_au = m_da[ry] + 1|2`, `m_au = m_au + 2`, `m_au = m_pc + 2`) are
        //   the derivation's here — `FetchPosition` — because each access is passed its own address; what
        //   the states decide and this code keeps is each *commit* (`m_da[ry]`, `m_da[rx]`) and where it
        //   sits. The instruction register's shift (`m_ird = m_ir`) is the closing pair's, which the loop
        //   runs after this read, and the long form's two ALU halves (`alu_sub`, then `alu_subc` in
        //   cmml7) are one `AluLongOp`: no bus cycle separates them in the listing either.
        //
        // cmmw1 (06b)  m_aob = m_da[ry]; m_pc = m_au; m_au = m_da[ry] + (ry < 15 ? 1 : 2)             0
        // cmml1 (06f)  m_aob = m_da[ry]; m_pc = m_au; m_au = m_da[ry] + 2                             0
        BeginFetchState();                                 // cmmw1 / cmml1 — `m_pc = m_au`

        // a byte steps by one, except on A7, whose byte forms move a whole word (row 48)
        uint sourceStep = (uint)(size == 0 && ry == 7 ? 2 : SizeBytes(size));
        uint destinationStep = (uint)(size == 0 && rx == 7 ? 2 : SizeBytes(size));

        uint sourceAddress = GetA(ry);

        // the destination's address is read at its own state, *after* the source has committed: when
        // both operands name the same register (`(A4)+,(A4)+`) the second address is the first one's
        // stepped value, not its own old one (18 + 21 records of the two files)
        uint source;
        uint destination;

        if (size == 2)
        {
        // cmml2 (08e)  m_at = m_au; m_da[ry] = m_au; m_au = m_au + 2; read at m_aob                   4
            SetA(ry, sourceAddress + 2);                   // cmml2 — the step lands before the read
            ushort sourceHigh = await ReadWordAsync(sourceAddress);
            if (_busFaulted)
            {
                await ChargeAbortedReadAsync();
                return;
            }

        // cmml3 (154)  m_aob = m_at; m_alub = m_dbin; m_da[ry] = m_au; read at m_aob                  4
            SetA(ry, sourceAddress + 4);                   // cmml3 — the second word's commit
            ushort sourceLow = await ReadWordAsync(sourceAddress + 2);
            if (_busFaulted)
            {
                await ChargeAbortedReadAsync();
                return;
            }

            source = Merge16_32(sourceHigh, sourceLow);

        // cmml4 (12e)  m_aob = m_da[rx]; m_ir = m_irc; m_au = m_da[rx] + 2; alu_and(m_dbin, 0xffff);
        //              read at m_aob                                                                  4
            uint destAddress = GetA(rx);                                          // cmml4 — m_da[rx]
            ushort destinationHigh = await ReadWordAsync(destAddress);            // cmml4
            if (_busFaulted)
            {
                await ChargeAbortedReadAsync();
                return;
            }

        // cmml5 (155)  m_aob = m_au; m_alue = m_dbin; m_au = m_au + 2; read at m_aob                  4
            ushort destinationLow = await ReadWordAsync(destAddress + 2);         // cmml5
            if (_busFaulted)
            {
                await ChargeAbortedReadAsync();
                return;
            }

            destination = Merge16_32(destinationHigh, destinationLow);
        // cmml6 (156)  m_aob = m_pc; m_da[rx] = m_au; m_au = m_pc + 2; alu_sub(m_aluo, m_dbin);
        //              sr_nzvc                                                                        0
            SetA(rx, destAddress + destinationStep);             // cmml6 — after both words

            AluLongOp(source, destination, AluLong.Sub);         // cmml6 — alu_sub, sr_nzvc
            SetLongFlags(AluLong.Sub, source, destination, _aluo, extend: false);

        // cmml7 (157)  m_ird = m_ir; alu_subc(m_alub, m_alue); sr_nzvc_u; read at m_aob               4
            await TrailingFetchAsync();                          // cmml7 — the closing prefetch
            return;
        }

        // cmmw2 (086)  m_at = m_au; m_da[ry] = m_au; m_au = m_au + 2; read at m_aob                   4
        SetA(ry, sourceAddress + sourceStep);              // cmmw2 — the step lands before the read
        source = await ReadAtAsync(sourceAddress, size);
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        // cmmw3 (122)  m_aob = m_da[rx]; m_ir = m_irc; m_au = m_da[rx] + (rx < 15 ? 1 : 2);
        //              alu_and8(m_dbin, 0xffff); read at m_aob                                        4
        uint destinationAddress = GetA(rx);                         // cmmw3 — after the source's step
        destination = await ReadAtAsync(destinationAddress, size);   // cmmw3
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        // cmmw4 (170)  m_aob = m_pc; m_ird = m_ir; m_da[rx] = m_au; m_au = m_pc + 2;
        //              alu_sub8(m_aluo, m_dbin); sr_nzvc; read at m_aob                               4
        SetA(rx, destinationAddress + destinationStep);    // cmmw4 — after the read, before the fetch
        SetCompareFlags(source, destination, (destination - source) & SizeMask(size), size);

        await TrailingFetchAsync();                        // cmmw4's read at `m_pc`
    }

    // =========================================================================================
    // The status-register moves — MOVE from SR, MOVE to CCR, MOVE to SR
    // =========================================================================================

    /// <summary>
    /// The three status moves: <c>move.w sr,&lt;ea&gt;</c> (<c>0x40C0</c>), <c>move.w &lt;ea&gt;,ccr</c>
    /// (<c>0x44C0</c>) and <c>move.w &lt;ea&gt;,sr</c> (<c>0x46C0</c>) — one block each, every mode and
    /// register inside it. <c>0x42C0</c> (<c>MOVE from CCR</c>) is <b>not</b> here: it is 68010+ and
    /// traps as an illegal instruction, which is what EmuTOS's CPU probe reads.
    /// </summary>
    static bool IsScriptedMoveToStatus(ushort op) =>
        (op & 0xFFC0) is 0x40C0 or 0x44C0 or 0x46C0;

    /// <summary>
    /// <c>move_sr_*</c>, <c>move_*_ccr</c> and <c>move_*_sr</c>. Two scripts, not three: the to-CCR and
    /// to-SR blocks are the same states (<c>mstw1</c>/<c>stiw4</c> for one, <c>stiw1</c>…<c>stiw3</c>
    /// for the other) and differ only in which bits of the status register they write.
    /// <list type="bullet">
    /// <item><b>the write direction reads its operand first.</b> <c>move_sr_ais</c> is
    /// <c>r w</c> — a read at the destination, the instruction's program read, then the store — so
    /// every memory record of <c>MOVEfromSR</c> was 4 clocks short while the handler only stored
    /// (1 118 records), and a fault on it is a <em>read</em> fault, which is also what its frame's
    /// status word records;</item>
    /// <item><b>the register destination ends on one idle</b> (<c>move_sr_ds</c>'s last
    /// <c>m_icount -= 2</c>): 6 clocks, not 4;</item>
    /// <item><b>the read direction refills its queue after the status write</b> — <c>move_dais_sr</c>
    /// ends <c>n 4</c> then two program reads at the next opcode and the word after it (a status write
    /// can change which stack and bus the CPU runs on, so MAME re-fetches the stream), which is why
    /// every supervisor record was exactly 8 short: 4 of idle and 4 of read. <see cref="RefillAsync"/>
    /// is that pair, at <c>_au</c> — the measured addresses are the instruction's own next word
    /// (<c>5722028</c> for a <c>(d16,An)</c> shape, i.e. the extension word read again, then
    /// <c>+2</c>);</item>
    /// <item><b>its clock charge is nothing of its own</b>: the whole difference is that pair plus the
    /// idle, and a privilege violation (<c>move_dais_sr</c>'s first state) frames before any of it —
    /// which is why the 354 user-mode records were already exact.</item>
    /// </list>
    /// A fault at the operand's read costs the aborting state's own 4 on every shape but the
    /// predecrement block, exactly as in the memory shifts (the same measure: 242 + 444 records).
    /// </summary>
    async ValueTask MoveToStatusAsync(ushort op)
    {
        int mode = FtuMode;
        int reg = FtuRegister;
        bool fromStatus = (op & 0xFFC0) == 0x40C0;
        bool toStatus = (op & 0xFFC0) == 0x46C0;

        // the privilege check is the script's first state, before any effective address is built
        if (toStatus && !Supervisor)
        {
            await EnterExceptionAsync(8, 0);
            return;
        }

        if (mode == 0)                                     // move_sr_ds / move_ds_ccr / move_ds_sr
        {
            if (fromStatus)
            {
                _d[reg] = (_d[reg] & 0xFFFF0000u) | (uint)(_sr & DefinedSr);
                _own = 2;                                  // the one idle state
                return;
            }

            WriteStatusRegister(toStatus, _d[reg] & 0xFFFF);
            await StatusWriteRefillAsync();                 // `n 4`, then the queue's two reads
            return;
        }

        if (mode == 7 && reg == 4)                         // `move.w #,ccr` / `,sr`: the immediate word
        {
            uint immediate = await FetchAsync();
            if (_busFaulted)
            {
                return;
            }

            WriteStatusRegister(toStatus, immediate);
            await StatusWriteRefillAsync();
            return;
        }

        // the predecrement's 2 clocks carry no read in these three scripts — the queue is refilled
        // after the operand, not behind the decrement (`move_pais_sr` is `n 2`, `r`, `n 4`, `r`, `r`),
        // so the idle is spent here and the EA block must not spend its own (`EaAddressAsync` would
        // issue a prefetch inside it: 200 of MOVEtoCCR and 109 of MOVEtoSR were 4 over)
        if (mode == 4)
        {
            _eaCharged = true;
            await ClocksAsync(2);
        }

        uint address = await EaAddressAsync(mode, reg, 1);
        if (_busFaulted)
        {
            return;
        }

        // the same frame rule the memory shifts carry: `asr_adr16`/`adr32` move `m_pc`, `aixl1`
        // does not (83 records of ASR.w and 412 of these three files measured it)
        FrameCounterForExtensionShape(mode, reg);

        // a PC-relative shape's operand read is still the instruction stream: `move_dpc_sr`'s adsw1
        // and adrw2 both set `SSW_PROGRAM`, so a fault there frames a program access (0xF6, not the
        // data class's 0xF5) — 25 records of MOVEtoSR and 49 of MOVEtoCCR
        bool pcRelative = mode == 7 && reg is 2 or 3;
        uint operand = pcRelative ? await ReadOperandProgramAsync(address) : await ReadWordAsync(address);
        if (_busFaulted)
        {
            // every shape pays the aborting state's own 4 here, the predecrement included — its idle
            // is spent above and carries no read, so there is no other state to pay it (444 records)
            await ChargeAbortedReadAsync();
            return;
        }

        if (fromStatus)
        {
            // `r w`: the instruction's own program read sits between the destination's read and its
            // store, so the store goes out after the trailing fetch rather than before it
            await TrailingFetchAsync();
            if (_busFaulted)
            {
                return;
            }

            await WriteWordAsync(address, (ushort)(_sr & DefinedSr));
            return;
        }

        WriteStatusRegister(toStatus, operand);
        await StatusWriteRefillAsync();
    }

    /// <summary>
    /// A status write's tail: <c>n 4</c>, then the queue's two reads at the instruction's own next
    /// word. <see cref="RefillAsync"/> is the pair and it marks the flow transferred, so the closing
    /// steps do not fetch a third word behind them.
    /// </summary>
    async ValueTask StatusWriteRefillAsync()
    {
        await ClocksAsync(4);                              // `mstw1`/`stiw*`: two idle states
        await RefillAsync(_au);
    }

    void WriteStatusRegister(bool toStatus, uint value)
    {
        // the undefined bits (7–5) read back as zero — the corpus compares the register, not the wire
        _sr = toStatus
            ? (ushort)(value & DefinedSr)
            : (ushort)((_sr & 0xFF00u) | (value & 0x00FFu));
    }

    /// <summary>
    /// Where MAME's <c>m_pc</c> stands when an operand's read faults in an extension-addressed shape:
    /// <c>asr_adr16</c>'s first state and <c>asr_adr32</c>'s second are <c>m_pc = m_au</c> (the word
    /// past the extension words, one behind the queue — <c>_fetchPointer - 2</c>), while
    /// <c>aixl1</c> never moves it (the first extension word's own address, <c>opcode + 2</c>), and
    /// <c>(d16,An)</c>, <c>(An)</c>, <c>(An)+</c> and <c>-(An)</c> already frame what the core's own
    /// state leaves. Measured over <c>ASR.w</c> (83) and the three status files (412).
    /// </summary>
    void FrameCounterForExtensionShape(int mode, int reg)
    {
        switch (mode)
        {
            case 4:                                            // the predecrement block's own `m_pc = m_au`
                _pc = _opcodeAddress + 4;
                break;
            case 6:                                            // aixl1 — never moves `m_pc`
                _pc = _opcodeAddress + 2;
                break;
            case 7 when reg <= 1:                              // adr16/adr32 — `m_pc = m_au` twice
                _pc = _au;                                     // the position the shape's own reads left
                break;
            case 7:                                            // dpc/axpc — `m_pc` is the operand's own base
                _pc = _opcodeAddress + 2;
                break;
        }
    }

    // =========================================================================================
    // Line 0's immediates and line 5's quicks — the same tails, a different operand block
    // =========================================================================================

    /// <summary>
    /// o#l1, o#w1 — a line-0 immediate's two words. They are the instruction's first two reads, in
    /// program space, and the second is the state that begins a prefetch (`m_pc = m_au`), so the
    /// frame's counter moves at it. `m_dt` is the immediate in MAME's own register; here it is the
    /// returned value, because nothing else in a long immediate shape reads it.
    /// </summary>
    async ValueTask<uint?> ReadLongImmediateAsync()
    {
        ushort high = await FetchAsync();                  // 3e0 o#l1 — set_16h(m_dt, m_dbin)
        if (_busFaulted) return null;

        BeginFetchState();                                 // 2b9 o#w1 — m_pc = m_au
        ushort low = await FetchAsync();                   // 2b9 o#w1 — set_16l(m_dt, m_dbin)
        return _busFaulted ? null : Merge16_32(high, low);
    }

    /// <summary>
    /// Line 0's long immediates — `ori/andi/eori/addi/subi/cmpi .l`, destination in a register or in
    /// memory. The script is the immediate's two reads and then #79's tail for the same destination;
    /// what the table could not say is <em>where</em> those two reads sit and that the register tail
    /// ends on two idle states (roal3, roal4) rather than the closing fetch.
    /// </summary>
    async ValueTask ImmediateLongAsync(ushort op)
    {
        int operation = (op >> 9) & 0x7;
        int mode = FtuMode;
        int reg = FtuRegister;

        uint? immediate = await ReadLongImmediateAsync();
        if (immediate is null)
        {
            return;
        }

        uint value = immediate.Value;

        if (operation == 6)                                // CMPI — a comparison, nothing is written
        {
            if (mode == 0)
            {
                await CompareLongTailAsync(value, _d[reg], clocks: 2);    // rcal1, rcal2, rcal3
                return;
            }

            uint? address = await ReadLongEaAsync(mode, reg);
            if (address is null)
            {
                return;
            }

            await CompareLongTailAsync(value, Merge16_32(_alub, _dbin), clocks: 0);   // cpdl1, cpdl2
            return;
        }

        AluLong kind = operation switch
        {
            0 => AluLong.Or,
            1 => AluLong.And,
            2 => AluLong.Sub,
            3 => AluLong.Add,
            _ => AluLong.Eor,
        };

        if (mode == 0)                                     // roal1 (free), roal2, roal3, roal4
        {
            await AluIntoDataRegisterAsync(reg, kind, value, clocks: 4);
            return;
        }

        uint? destination = await ReadLongEaAsync(mode, reg);
        if (destination is null)
        {
            return;
        }

        await AluIntoMemoryLongAsync(value, destination.Value, kind);   // morl1, morl2, morw2
    }

    /// <summary>
    /// Line 5's long quicks — `addq.l`/`subq.l`. There is no extension word: the value is the opcode's
    /// own field (`m_ftu`, bits 11–9, a zero meaning 8), so the script starts at the destination and
    /// runs the same tails as the immediates. An address register takes the whole 32 bits and moves no
    /// flags (MAME's raql1 says `m=.....`, the empty flag mask).
    /// </summary>
    async ValueTask QuickLongAsync(ushort op)
    {
        int data = (op >> 9) & 0x7;
        if (data == 0)
        {
            data = 8;
        }

        int mode = FtuMode;
        int reg = FtuRegister;
        AluLong kind = (op & 0x0100) != 0 ? AluLong.Sub : AluLong.Add;

        if (mode == 1)                                     // raql1 — An, no flags, two set_16 writes
        {
            await AluIntoAddressRegisterAsync(reg, kind, (uint)data, clocks: 4);
            return;
        }

        if (mode == 0)                                     // raql1 (free), roal2, roal3, roal4
        {
            await AluIntoDataRegisterAsync(reg, kind, (uint)data, clocks: 4);
            return;
        }

        uint? address = await ReadLongEaAsync(mode, reg);  // adrl1, adrl2
        if (address is null)
        {
            return;
        }

        await AluIntoMemoryLongAsync((uint)data, address.Value, kind);  // maql1, morl2, morw2
    }

    // =========================================================================================
    // The single-operand families — NOT, NEG, NEGX, CLR, TST, and their one read-modify-write block
    // =========================================================================================
    //
    // Five mnemonics, one script shape: the operand's two words (`adrl1`/`adrl2`, `pinl1`..`pinl3`,
    // `pdcl1`/`pdcl2`, `adsl1`..`adsl3`, `aixw*`, `abww1`/`ablw3`), then `nnml1`/`nnml2`/`morw2` for the
    // four that write back and `tsml1`/`tsml2` for the one that does not. `nnml1` is the state that both
    // runs the ALU's low half and issues the instruction's closing prefetch (`m_pc = m_au`); `nnml2`
    // writes the low word at `address + 2`; `morw2` writes the high one at `address`.
    //
    // `CLR` reads the operand it is about to zero (the corpus's frames record the read) — so it is the
    // same block, with `alu_and(word, 0)`, and not a store.

    /// <summary>
    /// The five single-operand members, keyed by the line-4 operation field (<c>op &gt;&gt; 8</c> is 0, 2, 4,
    /// 6 and 10 — the field read as four bits, so `TST`'s 10 is its own value).
    /// </summary>
    enum SingleOperand
    {
        Negx,
        Clr,
        Neg,
        Not,
        Tst
    }

    /// <summary>
    /// The five families over one size field: <b>NOT</b>, <b>CLR</b>, <b>NEG</b>, <b>NEGX</b> and
    /// <b>TST</b> — one RMW block per size, so one predicate per size. Everything the size field excludes
    /// from these five operation fields is another instruction and stays where it is: `MOVE from SR`
    /// (<c>0x40C0</c>), `MOVE from CCR` (<c>0x42C0</c>), `MOVE to CCR` (<c>0x44C0</c>), `MOVE to SR`
    /// (<c>0x46C0</c>), `TAS` (<c>0x4AC0</c>) — all size 3 — and `MOVEM`/`LEA`/`CHK`, which are size 3 as
    /// well. `An` direct is not an alterable EA, and the corpus carries no PC-relative or immediate shape
    /// for these five (`0x4AFC` is `ILLEGAL`).
    /// </summary>
    static bool IsScriptedSingleOperand(ushort op, int size)
    {
        if ((op >> 12) != 0x4 || ((op >> 6) & 0x3) != size)
        {
            return false;
        }

        int operation = (op >> 8) & 0xF;
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;

        if (mode == 1 || (mode == 7 && reg > 1))
        {
            return false;
        }

        // the operation field is bits 11-8 (four bits: the five families are 0x0 NEGX, 0x2 CLR, 0x4 NEG,
        // 0x6 NOT, 0xA TST). Reading it as bits 11-9 makes 0x4180 — `CHK` — look like NEGX.l, and that
        // costs CHK 896 records: 0x4180 is 0100 0001 10 mmm rrr, so its "size" field really is 10
        return operation is 0x0 or 0x2 or 0x4 or 0x6 or 0xA;
    }

    static bool IsScriptedSingleOperandLong(ushort op) => IsScriptedSingleOperand(op, 2);

    static bool IsScriptedSingleOperandWord(ushort op) => IsScriptedSingleOperand(op, 1);

    static bool IsScriptedSingleOperandByte(ushort op) => IsScriptedSingleOperand(op, 0);

    static SingleOperand SingleOperandKindOf(ushort op) => ((op >> 8) & 0xF) switch
    {
        0x0 => SingleOperand.Negx,
        0x2 => SingleOperand.Clr,
        0x4 => SingleOperand.Neg,
        0x6 => SingleOperand.Not,
        _ => SingleOperand.Tst                                  // 0xA
    };

    /// <summary>
    /// One long single-operand instruction: the operand block, the ALU, and either the two writes
    /// (`nnml2`, `morw2`) or none (`tsml2`).
    ///
    /// <para>
    /// The register form is the same block with no bus cycle of its own: `nnrl1` latches the prefetch's
    /// start and runs the ALU on the low half, `nnrl2` writes it back (`set_16l`) and finishes the flags,
    /// and `roal4` — the shape's closing 2 clocks — writes the high half after the prefetch read. That is
    /// why a fault at the prefetch leaves the destination half-updated, and why the idle state is `TST`'s
    /// to omit (`tsrl2` ends the script).
    /// </para>
    /// </summary>
    async ValueTask SingleOperandLongAsync(ushort op)
    {
        SingleOperand kind = SingleOperandKindOf(op);

        int mode = FtuMode;
        int reg = FtuRegister;

        if (mode == 0)                                     // nnrl1, nnrl2, roal4 / tsrl1, tsrl2
        {
            uint register = _d[reg];
            uint result = SingleOperandResult(kind, register);
            SetSingleOperandFlags(kind, register, result, 2);

            if (kind == SingleOperand.Tst)
            {
                await TrailingFetchAsync();                // tsrl2 — the instruction's one read
                return;
            }

            Set16L(reg, Low16(result));                    // nnrl2 — set_16l, before the fetch
            await TrailingFetchAsync();                    // nnrl2 — the instruction's one read
            if (_busFaulted) return;

            Set16H(reg, High16(result));                   // roal4 — set_16h
            await IdleAsync();                             // roal4 — the shape's closing 2 clocks
            return;
        }

        uint? address = await ReadLongEaAsync(mode, reg);
        if (address is null)
        {
            return;
        }

        await TrailingFetchAsync();                        // nnml1/tsml1 — m_pc = m_au, the one read
        if (_busFaulted) return;

        uint operand = Merge16_32(_alub, _dbin);
        uint outcome = SingleOperandResult(kind, operand);
        SetSingleOperandFlags(kind, operand, outcome, 2);

        if (kind == SingleOperand.Tst)
        {
            return;                                        // tsml2 — nothing is written back
        }

        await WriteWordAsync(address.Value + 2, (ushort)Low16(outcome));   // nnml2 — m_aob = m_at
        if (_busFaulted) return;
        await WriteWordAsync(address.Value, (ushort)High16(outcome));      // morw2 — m_au = m_at - 2
    }

    /// <summary>
    /// One <b>word</b> or <b>byte</b> single-operand instruction — the same five families, the same ALU
    /// and flags, a different block: `adrw1`/`adrw2`, `pinw1`/`pinw2`, `pdcw1`/`pdcw2`,
    /// `adsw1`/`adsw2`, `aixw*`, `abwl1`/`abll3`, then `nnmw1` + `morw2` (`TST`: `tsmw1` + `mmrw3`,
    /// and no write).
    ///
    /// <para>
    /// The byte forms are the <em>same</em> block, not a third one: `adrw1`/`adrw2` are one script for
    /// both sizes, and only the access's width (and the EA's unit step, one byte instead of two) tells
    /// them apart — the reference's byte read is the word read with a lane mask, `m_edb >>= 8` on an
    /// even address. So <paramref name="size"/> is 1 (word) or 0 (byte) and nothing else in the state
    /// list moves.
    /// </para>
    ///
    /// <para>
    /// Three ways the word block is not the long one shrunk. (1) The register form is `nnrw1` + `roaw2`
    /// with <b>no closing idle</b>: `NOT.w Dn` is 4 clocks (`NOT.l Dn` is 6, `roal4`'s 2 being the
    /// difference). (2) `pdcw1` is a state that writes `m_pc = m_au` before it steps `An` down — the
    /// long form's `pdcl1` does not — so a `-(An)` word operand's fault saves the counter from there.
    /// (3) `pinw2` commits the post-increment <em>before</em> its read (the long form's `pinl3` commits
    /// after both words), so a faulting `(An)+` word read leaves `An` incremented.
    /// </para>
    ///
    /// <para>
    /// `CLR` is in here with a read: the microcode's `clr_bw_*` reads the operand it zeroes (`nnmw1`
    /// runs `alu_and8/alu_and(word, 0)` on `m_dbin`, not on a constant), so a byte `CLR` that only
    /// wrote was 4 clocks short on its 2 099 memory records — the read is the missing access, and it
    /// costs the same 4 as the write. That is the whole reason the byte batch is a port and not a
    /// timing-table edit.
    /// </para>
    /// </summary>
    async ValueTask SingleOperandWordOrByteAsync(ushort op, int size)
    {
        SingleOperand kind = SingleOperandKindOf(op);

        int mode = FtuMode;
        int reg = FtuRegister;

        if (mode == 0)                                     // nnrw1, roaw2 / tsrw1, mmrw3
        {
            uint register = _d[reg] & SizeMask(size);
            uint result = SingleOperandResult(kind, register);
            SetSingleOperandFlags(kind, register, result, size);

            if (kind == SingleOperand.Tst)
            {
                await TrailingFetchAsync();                // mmrw3 — the instruction's one read
                return;
            }

            if (size == 0)
            {
                _d[reg] = (_d[reg] & 0xFFFF_FF00) | (result & 0xFF);   // roaw2 — set_8, before the fetch
            }
            else
            {
                Set16L(reg, result);                       // roaw2 — set_16l, before the fetch
            }

            await TrailingFetchAsync();                    // roaw2 — the instruction's one read
            return;                                        // no closing idle: the word form ends here
        }

        uint? address = await ReadSmallEaAsync(mode, reg, size);
        if (address is null)
        {
            return;
        }

        uint operand = _dbin;                              // a byte arrives in the low lane (adrw2's shift)
        uint outcome = SingleOperandResult(kind, operand);
        SetSingleOperandFlags(kind, operand, outcome, size);   // nnmw1/tsmw1 — the ALU and its flags

        await TrailingFetchAsync();                        // nnmw1/tsmw1 — m_pc = m_au, the one read
        if (_busFaulted) return;

        if (kind == SingleOperand.Tst)
        {
            return;                                        // mmrw3 closes; nothing is written back
        }

        if (size == 0)
        {
            await WriteByteAsync(address.Value, (byte)outcome);   // morw2 — the byte, in its lane
            return;
        }

        await WriteWordAsync(address.Value, (ushort)outcome);     // morw2 — the word at m_at
    }

    /// <summary>
    /// The word or byte operand's address and its read — the same EA states the long script uses, with
    /// the word block's own three differences (`adrw1` is a latch with no bus cycle, `pdcw1` begins a
    /// fetch state, `pinw2` commits the increment before the read) and one size-dependent fact: the
    /// unit step is a byte for the byte forms, except on <c>A7</c>, whose byte forms step by two —
    /// MAME's `m_au = m_da[ry] + (ry &lt; 15 ? 1 : 2)` in `pinw1`/`pdcw1`.
    /// </summary>
    async ValueTask<uint?> ReadSmallEaAsync(int mode, int reg, int size)
    {
        int step = size == 1 || reg == 7 ? 2 : 1;
        uint address;

        switch (mode)
        {
            case 2:                                        // ais — adrw1, the address and nothing else
                address = GetA(reg);
                break;

            case 3:                                        // aips — pinw1
                address = GetA(reg);
                break;

            case 4:                                        // pais — pdcw1 (2 clocks), pdcw2 (the commit)
                BeginFetchState();                         // pdcw1 — m_pc = m_au
                address = GetA(reg) - (uint)step;
                await IdleAsync();
                SetA(reg, address);
                break;

            case 5:                                        // das — adsw1, out of the queue
            {
                ushort displacement = await FetchAsync();
                if (_busFaulted) return null;
                address = GetA(reg) + Ext32(displacement);
                break;
            }

            case 6:                                        // dais — aixw0 (2 clocks), then the refill
                await IdleAsync();
                {
                    ushort extension = await FetchAsync();
                    if (_busFaulted) return null;
                    address = GetA(reg) + (uint)ExtensionDisplacement(extension) + IndexValue(extension);
                }

                break;

            default:                                       // mode 7 — the absolute shapes only
                if (reg == 0)
                {
                    BeginFetchState();                     // abwl1 — m_pc = m_au
                    ushort absolute = await FetchAsync();
                    if (_busFaulted) return null;
                    address = Ext32(absolute);
                }
                else
                {
                    ushort high = await FetchAsync();      // abll1 — no m_pc of its own
                    if (_busFaulted) return null;
                    BeginFetchState();                     // abll2 — m_pc = m_au
                    ushort low = await FetchAsync();
                    if (_busFaulted) return null;
                    address = Merge16_32(high, low);
                }

                break;
        }

        if (mode == 3)
        {
            SetA(reg, address + (uint)step);               // pinw2 — the increment, before the read
        }

        _dbin = size == 0
            ? await ReadByteAsync(address)                 // adrw2 / abll3, in the byte's own lane
            : await ReadWordAsync(address);

        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return null;
        }

        return address;
    }

    /// <summary>
    /// <c>alu_not</c>, <c>alu_and(word, 0)</c> (CLR), <c>alu_sub(word, 0)</c> (NEG), <c>alu_subx</c> (NEGX)
    /// and <c>alu_and(word, 0xFFFF)</c> (TST) — thirty-two bits wide, once, as the two-operand longs do:
    /// the low word's carry out is the high word's carry in, so one 32-bit operation gives the same final
    /// result and the same final flags. NEGX's extend is the current <c>X</c>.
    /// </summary>
    uint SingleOperandResult(SingleOperand kind, uint operand) => kind switch
    {
        SingleOperand.Clr => 0,
        SingleOperand.Not => ~operand,
        SingleOperand.Neg => 0 - operand,
        SingleOperand.Negx => 0 - operand - (uint)((_sr & FlagX) != 0 ? 1 : 0),
        _ => operand                                      // TST
    };

    /// <summary>
    /// The five flag rules, each its own: `NOT`/`CLR`/`TST` are logic operations (`m=.nzvc`, `X` untouched,
    /// `V` and `C` cleared), `NEG` is a subtract from zero (`X` follows `C`, which is set for every non-zero
    /// operand), and `NEGX` is the exchange form — a subtract from zero with the extend bit, whose `Z` is
    /// only ever <em>cleared</em> so a chain reports the whole result. <paramref name="size"/> is the
    /// core's own size code (1 = word, 2 = long), which is all the width the rules need.
    /// </summary>
    void SetSingleOperandFlags(SingleOperand kind, uint operand, uint result, int size)
    {
        switch (kind)
        {
            case SingleOperand.Neg:
                SetSubFlags(operand, 0, result, size);     // sr_xnzvc + sr_xnzvc_u
                return;

            case SingleOperand.Negx:
                SetExchangeFlags(true, operand, 0, result, size);
                return;

            default:                                       // NOT, CLR, TST — sr_nzvc + sr_nzvc_u
            {
                uint masked = result & SizeMask(size);
                _sr = (ushort)(_sr & ~(FlagN | FlagZ | FlagV | FlagC));
                if (masked == 0) _sr |= FlagZ;
                if ((masked & (uint)SignBit(size)) != 0) _sr |= FlagN;
                return;
            }
        }
    }

    // =========================================================================================
    // The two-operand families in size 1 — the word block and three tails
    // =========================================================================================
    //
    // The word forms are **not** the long ones with a mask: the microcode gives them their own state
    // list, and three facts separate it (each one read off the generated handlers and then measured on
    // the corpus's own cycle lists).
    //
    //  (1) The destination's tail is `roaw1`/`roaw2` (`rorw1`/`rrgw2` for `<ea>,Dn`): the ALU and its
    //      flags in a state that has no bus cycle and no idle charge, then `set_16l` and the
    //      instruction's one prefetch read — and **no closing idle**, because `roaw2` ends the script.
    //      So `OR.w Dn,Dn` is 4 clocks where `OR.l Dn,Dn` is 6 (the long form's `roal4`).
    //  (2) The memory tail is `morw1` + `morw2`, and the ALU runs **before** the closing prefetch —
    //      where the long form's `morl1` fetches first and ALUs after. The one write follows the read.
    //  (3) `ADDA`/`SUBA`/`CMPA` in size 1 are the **long** tails. A word source is sign-extended
    //      (`m_at = ext32(m_dbin)` in `romm1`/`cpmm1`) and the ALU runs twice — `alu_add`/`alu_sub` on
    //      the low half, `alu_addc`/`alu_subc` on the high — which is exactly what
    //      `AluIntoAddressRegisterAsync` and the long compare tail already expand. That reuse is where
    //      the timing table's flat 4 was measured short: `ADDA.w` was 144 exact of 2 500.
    //
    // The register-destination and memory-destination word tails are shared by three families that are
    // not ALU families at all — line 0's word immediates (`o#w1` first) and line 5's word quicks (no
    // source read at all) — which is why they are one block and not three.

    /// <summary>
    /// One word instruction of the two-operand families: `<ea>,Dn` (opmode 1), `<ea>,An` (opmode 3 —
    /// ADDA/SUBA/CMPA, which are the long tails) or `Dn,<ea>` (opmode 5, where mode 0 is `EOR.w Dn,Dn`
    /// and the only one of these shapes that writes a register).
    /// </summary>
    async ValueTask AluWordAsync(ushort op)
    {
        int line = op >> 12;
        int opmode = (op >> 6) & 0x7;
        int rx = (op >> 9) & 0x7;
        int mode = FtuMode;
        int reg = FtuRegister;

        bool comparison = line == 0xB;
        AluLong kind = line switch
        {
            0x8 => AluLong.Or,
            0x9 => AluLong.Sub,
            0xC => AluLong.And,
            0xD => AluLong.Add,
            _ => opmode == 5 ? AluLong.Eor : AluLong.Sub,          // line B: EOR writes, CMP does not
        };

        if (opmode == 1)                                          // <ea>,Dn — rorw1/rrgw2, cpmw1/rcaw2
        {
            uint? source = await ReadWordSourceAsync(mode, reg);
            if (source is null)
            {
                return;
            }

            if (comparison)
            {
                await CompareWordTailAsync(source.Value, _d[rx] & 0xFFFF);
                return;
            }

            await AluWordIntoDataRegisterAsync(rx, kind, source.Value);
            return;
        }

        if (opmode == 3)                                          // <ea>,An — the long tails
        {
            uint? operand = await ReadWordSourceAsync(mode, reg);
            if (operand is null)
            {
                return;
            }

            uint extended = Ext32((ushort)operand.Value);          // romm1/cpmm1 — a word, sign-extended

            if (comparison)
            {
                await CompareLongTailAsync(extended, GetA(rx), clocks: 2);    // cpmm1, cprm2, rcal3
                return;
            }

            await AluIntoAddressRegisterAsync(rx, kind, extended, clocks: 4); // romm1, rorm2, rorm3, roml3
            return;
        }

        if (mode == 0)                                            // eor_w_dd_ds_df — a register destination
        {
            await AluWordIntoDataRegisterAsync(reg, kind, _d[rx] & 0xFFFF);
            return;
        }

        if (mode == 1)
        {
            return;                                                // nothing alterable in memory here
        }

        uint? address = await ReadSmallEaAsync(mode, reg, 1);
        if (address is null)
        {
            return;
        }

        await AluWordIntoMemoryAsync(_d[rx] & 0xFFFF, address.Value, kind);
    }

    /// <summary>
    /// Line 0's word immediates — `ori/andi/eori/addi/subi/cmpi .w`: `o#w1` (one program read, and the
    /// state that moves the frame's counter) and then the destination's word tail. The immediate comes
    /// out of the stream <b>before</b> the destination's extension word, which is the whole reason the
    /// old handlers read them the other way round on every indexed and absolute destination.
    /// </summary>
    async ValueTask ImmediateWordAsync(ushort op)
    {
        int operation = (op >> 9) & 0x7;
        int mode = FtuMode;
        int reg = FtuRegister;

        BeginFetchState();                                         // o#w1 — m_pc = m_au
        ushort immediate = await FetchAsync();                     // o#w1 — the one read
        if (_busFaulted)
        {
            return;
        }

        AluLong kind = operation switch
        {
            0 => AluLong.Or,
            1 => AluLong.And,
            2 => AluLong.Sub,
            3 => AluLong.Add,
            _ => AluLong.Eor,
        };

        if (mode == 0)                                             // roaw1/roaw2, rcaw1/rcaw2
        {
            if (operation == 6)
            {
                await CompareWordTailAsync(immediate, _d[reg] & 0xFFFF);
                return;
            }

            await AluWordIntoDataRegisterAsync(reg, kind, immediate);
            return;
        }

        uint? address = await ReadSmallEaAsync(mode, reg, 1);
        if (address is null)
        {
            return;
        }

        if (operation == 6)                                        // cpdw1 + mmrw3 — nothing is written
        {
            await CompareWordTailAsync(immediate, _dbin);
            return;
        }

        await AluWordIntoMemoryAsync(immediate, address.Value, kind);
    }

    /// <summary>
    /// Line 5's word quicks — `addq.w`/`subq.w`: no extension word (the value is the opcode's own
    /// field, a zero meaning 8), so the script starts at the destination and runs the same three tails.
    /// An address register takes the whole 32 bits and moves no flags (`raql1`'s `m=.....`).
    /// </summary>
    async ValueTask QuickWordAsync(ushort op)
    {
        int data = (op >> 9) & 0x7;
        if (data == 0)
        {
            data = 8;
        }

        int mode = FtuMode;
        int reg = FtuRegister;
        AluLong kind = (op & 0x0100) != 0 ? AluLong.Sub : AluLong.Add;

        if (mode == 1)                                             // raql1, roal2, roal3, roal4
        {
            await AluIntoAddressRegisterAsync(reg, kind, (uint)data, clocks: 4);
            return;
        }

        if (mode == 0)                                             // raqw1/roaw1 + roaw2
        {
            await AluWordIntoDataRegisterAsync(reg, kind, (uint)data);
            return;
        }

        uint? address = await ReadSmallEaAsync(mode, reg, 1);
        if (address is null)
        {
            return;
        }

        await AluWordIntoMemoryAsync((uint)data, address.Value, kind);   // maqw1 + morw2
    }

    // ---- the three word tails ----------------------------------------------------------------

    /// <summary>
    /// rorw1/roaw1 + rrgw2/roaw2 — the word result into a data register: the ALU and its flags (a state
    /// with no bus cycle and no idle), `set_16l` <em>before</em> the instruction's one read, and no
    /// closing idle. `X` follows `C` for ADD/SUB and stays put for OR/AND/EOR.
    /// </summary>
    async ValueTask AluWordIntoDataRegisterAsync(int rx, AluLong kind, uint source)
    {
        uint destination = _d[rx] & 0xFFFF;
        uint result = WordAlu(kind, source, destination);
        SetWordFlags(kind, source, destination, result, extend: kind is AluLong.Add or AluLong.Sub);

        Set16L(rx, result);                                        // roaw2 — set_16l, before the read
        await TrailingFetchAsync();                                // roaw2 — the instruction's one read
    }

    /// <summary>
    /// cpmw1 + rcaw2 (`cmpi`'s rcaw1/rcaw2, `cpdw1`/`mmrw3` for a memory destination) — a word
    /// comparison: the subtract and its flags, the one read, nothing written and no closing idle. The
    /// long compare's `rcal3` is a long-shape state and the word form does not have it.
    /// </summary>
    async ValueTask CompareWordTailAsync(uint source, uint destination)
    {
        uint result = WordAlu(AluLong.Sub, source, destination);
        SetWordFlags(AluLong.Sub, source, destination, result, extend: false);   // cpmw1 — X untouched

        await TrailingFetchAsync();                                // rcaw2
    }

    /// <summary>
    /// morw1 + morw2 — the word written back to memory. The ALU and its flags run <b>before</b> the
    /// closing prefetch (the long tail fetches first), and the store follows it, so an odd destination
    /// still leaves the flags of the operation that was made.
    /// </summary>
    async ValueTask AluWordIntoMemoryAsync(uint source, uint address, AluLong kind)
    {
        uint destination = _dbin;
        uint result = WordAlu(kind, source, destination);
        SetWordFlags(kind, source, destination, result, extend: kind is AluLong.Add or AluLong.Sub);

        await TrailingFetchAsync();                                // morw1 — m_pc = m_au, the one read
        if (_busFaulted)
        {
            return;
        }

        await WriteWordAsync(address, (ushort)result);              // morw2
    }

    /// <summary>
    /// <c>alu_or</c>/<c>alu_and</c>/<c>alu_eor</c>/<c>alu_add</c>/<c>alu_sub</c> at the word width —
    /// the chip's ALU is sixteen bits, so these are one operation and not half of a long one.
    /// </summary>
    uint WordAlu(AluLong kind, uint source, uint destination)
    {
        uint a = source & 0xFFFF;
        uint b = destination & 0xFFFF;
        _aluWidth = 1;
        _aluo = kind switch
        {
            AluLong.Or => b | a,
            AluLong.And => b & a,
            AluLong.Eor => b ^ a,
            AluLong.Sub => (b - a) & 0xFFFF,
            _ => (b + a) & 0xFFFF,
        };

        return _aluo;
    }

    /// <summary>
    /// The word forms' flag update: `sr_nzvc` for OR/AND/EOR, `sr_xnzvc` for ADD/SUB, and
    /// "<paramref name="extend"/> false" for a comparison, whose `X` is not the operation's.
    /// </summary>
    void SetWordFlags(AluLong kind, uint source, uint destination, uint result, bool extend)
    {
        ushort x = (ushort)(_sr & FlagX);

        switch (kind)
        {
            case AluLong.Or or AluLong.And or AluLong.Eor:
                SetLogicFlags(result, 1);
                break;

            case AluLong.Add:
                SetAddFlags(source, destination, result, 1);
                break;

            default:
                SetSubFlags(source, destination, result, 1);
                break;
        }

        if (!extend)
        {
            _sr = (ushort)((_sr & ~FlagX) | x);                     // a comparison leaves X alone
        }
    }

    // =========================================================================================
    // MOVE — lines 3 (word) and 2 (long), and MOVEA
    // =========================================================================================
    //
    // `MOVE` is the family whose *destination* tail is not the ALU families' tail for the same
    // effective address, and the difference is where the instruction's one closing read sits. The
    // microcode's own handlers say it per class, and the corpus's transactions confirm every one
    // (`R` a program read, `W` the store, `n2` an idle state):
    //
    //   dest         register/immediate source        memory source
    //   Dn           R                                (source reads) R
    //   An (MOVEA)   R                                R
    //   (An)         W R
    //   (An)+        W R                              (source reads) W R
    //   -(An)        R W
    //   (d16,An)     R W R                            R W R
    //   (d8,An,Xn)   n2 R W R
    //   (xxx).w      R W R
    //   (xxx).l      R R W R                          R W R R
    //
    // Two of those are the classes #74 and row 74 already pinned, and they are why this family was
    // left for last: a plain `MOVE` into `-(An)` issues its closing read **before** the store (an
    // odd destination keeps the read and loses the write), where every other instruction stores
    // first; and an `(xxx).l` destination takes the address's low word **out of the queue** rather
    // than reading it, which is `FetchMoveAbsoluteLongAsync`/`_queueShort` and the two closing reads
    // `CloseAfterLongDestinationAsync` already performs. With a source that cost no bus cycle the
    // class prefetches the word after the address *before* the store — the R R W R row above.
    //
    // The register commits are the other half: `(An)+` steps its register **after** the store
    // (`mmiw2`/`rmil3`) and `-(An)` steps it **with** the store (`mmmw2`), so a faulting store
    // leaves `An` where it stood in both — where the old handlers committed the step while they
    // addressed the operand. MOVEA takes the whole 32-bit value and calls **no flag update at all**
    // (`i=......f` on every one of its states).

    /// <summary>
    /// One <c>MOVE</c>/<c>MOVEA</c>: the source block the ALU families use, the flags the move sets
    /// (not the store), then the destination class's own tail.
    /// </summary>
    async ValueTask MoveScriptAsync(ushort op, int size)
    {
        int sourceMode = (op >> 3) & 0x7;
        int sourceReg = op & 0x7;
        int destinationMode = (op >> 6) & 0x7;
        int destinationReg = (op >> 9) & 0x7;

        uint? source = size == 2
            ? await ReadLongSourceAsync(sourceMode, sourceReg)
            : await ReadWordSourceAsync(sourceMode, sourceReg);
        if (source is null)
        {
            return;
        }

        uint value = size == 2 ? source.Value : source.Value & 0xFFFF;
        bool noBusCycle = MoveSourceTakesNoBusCycle(sourceMode, sourceReg);

        // the flags are the *move's*, and where inside the store they land is per class (they are
        // `sr_nzvc` on the low word and `sr_nz_u` on the high one, in the state the class puts them).
        // A word destination latches the whole value in one state, so its flags are always up before
        // the store and this sets them once; a long memory store's two updates are the block's own
        // business (see <see cref="WriteMoveLongAsync"/>), because a fault can fall between them.
        if (destinationMode != 1 && (size == 1 || destinationMode == 0))
        {
            SetLogicFlags(value, size);
        }

        await MoveStoreAsync(destinationMode, destinationReg, size, value, noBusCycle);
    }

    /// <summary>
    /// Whether the <c>MOVE</c>'s source cost a bus cycle of its own — a data or address register
    /// direct, or an immediate. Only the absolute-long destination reads it (the reference's
    /// `MOVE_bw_AbsoluteLong_prefetch_first` class), and it is the source field of the *opcode*, not
    /// a property of the tail.
    /// </summary>
    static bool MoveSourceTakesNoBusCycle(int mode, int reg) =>
        mode is 0 or 1 || (mode == 7 && reg == 4);

    /// <summary>
    /// The destination class's tail — one block per row of the table above, the states in the
    /// microcode's order.
    /// </summary>
    async ValueTask MoveStoreAsync(int mode, int reg, int size, uint value, bool sourceTakesNoBusCycle)
    {
        switch (mode)
        {
            case 0:                                            // mrgw1 + mmrw3 / mrgl1 + mrgl2
                if (size == 2)
                {
                    _d[reg] = value;
                }
                else
                {
                    Set16L(reg, value);
                }

                BeginFetchState();                             // mrgw1 — m_pc = m_au
                await TrailingFetchAsync();                    // mmrw3
                return;

            case 1:                                            // MOVEA — rrgm1/rrgl2, no flag call at all
                SetA(reg, size == 2 ? value : Ext32((ushort)value));
                BeginFetchState();
                await TrailingFetchAsync();
                return;

            case 2:                                            // (An) — mmrw1/mmrw2/mmrw3, rmrl1..rmrl3
            {
                uint address = GetA(reg);
                BeginFetchState();                             // mmrw1 — m_pc = m_au
                await WriteMoveAsync(address, size, value,
                    sourceTakesNoBusCycle ? MoveLongFlags.AfterFirstWord : MoveLongFlags.BetweenWords);
                if (_busFaulted)
                {
                    return;                                    // a faulted store cancels the closing read
                }

                await TrailingFetchAsync();                    // mmrw3 — after the store
                return;
            }

            case 3:                                            // (An)+ — mmiw1/mm iw2, rmil1..rmil3
            {
                uint address = GetA(reg);
                BeginFetchState();                             // mmiw1 — m_pc = m_au
                await WriteMoveAsync(address, size, value,
                    sourceTakesNoBusCycle ? MoveLongFlags.AfterFirstWord : MoveLongFlags.BetweenWords);
                if (_busFaulted)
                {
                    return;
                }

                SetA(reg, address + (uint)SizeBytes(size));    // mmiw2 — the step lands with the store
                await TrailingFetchAsync();                    // mmiw2 — the closing read
                return;
            }

            case 4:                                            // -(An) — rmmw1/mmmw2, rmml1..mmmw2
            {
                BeginFetchState();                             // rmmw1 — m_pc = m_au
                await TrailingFetchAsync();                    // rmmw1 — the read comes *before* the store
                if (_busFaulted)
                {
                    return;
                }

                uint start = GetA(reg);
                if (size == 1)
                {
                    // rmmw1 does `m_ir = m_irc` and mmmw2 `m_ird = m_ir`, so a group-0 frame taken by
                    // this store records the queue head — the instruction's next word — and not the
                    // opcode. The long form's first write is rmml2, which reassigns nothing, so its
                    // frame still records the opcode: the difference is measured (115 records).
                    FrameSavesInstructionWord(_ir);
                    SetA(reg, start - 2);                      // mmmw2 — the step lands with the store
                    await WriteMoveWordAsync(start - 2, (ushort)value);
                    return;
                }

                // rmml2 writes the **low** word at A − 2 (m_aluo is rmml1's `alu_and(src)`), then
                // mmmw2 steps the register to A − 4 and writes the high word there — so the step is
                // committed after the first word and an odd register keeps it
                MoveLongLowFlags(value);                     // rmml1 — sr_nzvc on the low word
                MoveLongHighFlags(value);                    // rmml2 — sr_nz_u on the high one
                await WriteMoveWordAsync(start - 2, (ushort)Low16(value));
                if (_busFaulted)
                {
                    return;
                }

                SetA(reg, start - 4);
                await WriteMoveWordAsync(start - 4, (ushort)High16(value));
                return;
            }

            case 5:                                            // (d16,An) — rmdw1/rmdw2/b/mmrw3
            {
                BeginFetchState();                             // rmdw1 — m_pc = m_au
                ushort displacement = await FetchAsync();
                if (_busFaulted)
                {
                    return;
                }

                uint address = GetA(reg) + Ext32(displacement);
                await WriteMoveAsync(address, size, value,
                    sourceTakesNoBusCycle ? MoveLongFlags.HighBeforeFirstWord : MoveLongFlags.BeforeBoth);
                if (_busFaulted)
                {
                    return;
                }

                BeginFetchState();                             // b — m_pc = m_au
                await TrailingFetchAsync();                    // mmrw3
                return;
            }

            case 6:                                            // (d8,An,Xn) — rmxw0..rmxw3/rmdw2/b/mmrw3
            {
                await IdleAsync();                             // rmxw0 — the index's own state
                BeginFetchState();                             // rmxw1 — m_pc = m_au, the read's address
                ushort extension = await FetchAsync();
                if (_busFaulted)
                {
                    return;
                }

                uint address = GetA(reg) + (uint)ExtensionDisplacement(extension) + IndexValue(extension);
                await WriteMoveAsync(address, size, value,
                    sourceTakesNoBusCycle ? MoveLongFlags.HighBeforeFirstWord : MoveLongFlags.BeforeBoth);
                if (_busFaulted)
                {
                    return;
                }

                BeginFetchState();                             // b
                await TrailingFetchAsync();                    // mmrw3
                return;
            }

            default:
                switch (reg)
                {
                    case 0:                                    // (xxx).w — raww1/maww2/b/mmrw3
                    {
                        BeginFetchState();                     // raww1 — m_pc = m_au
                        ushort absolute = await FetchAsync();
                        if (_busFaulted)
                        {
                            return;
                        }

                        await WriteMoveAsync(Ext32(absolute), size, value, MoveLongFlags.BeforeBoth);
                        if (_busFaulted)
                        {
                            return;
                        }

                        BeginFetchState();                     // b
                        await TrailingFetchAsync();            // mmrw3
                        return;
                    }

                    case 1:                                    // (xxx).l — the queue-short class
                    {
                        // mall1 sets `m_pc = m_au` for the pair's one read when the source took a bus
                        // cycle; when it did not, the class's second read (rall2) is the one that sets
                        // it, which is the prefetch-first `TrailingFetchAsync` below
                        if (!sourceTakesNoBusCycle)
                        {
                            BeginFetchState();
                        }

                        uint address = await FetchMoveAbsoluteLongAsync();   // mall1/mall2, or ralw1/ralw2
                        if (_busFaulted)
                        {
                            return;
                        }

                        // the prefetch-first half: with the queue a word short, a source that took no
                        // bus cycle leaves the bus free, so the word after the address is read before
                        // the store (records `33fc` keep that read on a fault, `33ea` lose it)
                        if (sourceTakesNoBusCycle)
                        {
                            await TrailingFetchAsync();
                            if (_busFaulted)
                            {
                                return;
                            }
                        }

                        await WriteMoveAsync(address, size, value,             // malw2/maww2
                            sourceTakesNoBusCycle ? MoveLongFlags.BeforeBoth : MoveLongFlags.BetweenWords);
                        return;                                             // the close pays the two reads
                    }
                }

                return;
        }
    }

    /// <summary>
    /// Where a long <c>MOVE</c>'s two flag updates sit relative to its two writes. The microcode gives
    /// the family two, `sr_nzvc` on the low word and `sr_nz_u` on the high one, and the *state* each
    /// runs in differs by destination class — which is visible only when a store faults between them.
    /// </summary>
    enum MoveLongFlags
    {
        /// <summary>`rmml1`/`rmml2`, `raw l1`/`rall3`, `mmdl1`/`mawl2`: both updates before either word.</summary>
        BeforeBoth,

        /// <summary>`rmrl1`…`rmrl3`, `rmil1`…`rmil3`: the high word, then `sr_nzvc`, then the low word, then `sr_nz_u`.</summary>
        AfterFirstWord,

        /// <summary>`mmrl1`/`mmrl2`, `mall2`/`mall3`: `sr_nzvc`, the high word, `sr_nz_u`, the low word.</summary>
        BetweenWords,

        /// <summary>
        /// `rmdl2`/`rmdl3` (a displacement or indexed destination with a source that took no bus cycle):
        /// `sr_nz_u`, the high word, `sr_nzvc`, the low word, `sr_nz_u` — so a fault on the first word
        /// leaves N and Z from the high half and V and C exactly as the instruction found them.
        /// </summary>
        HighBeforeFirstWord,
    }

    /// <summary><c>alu_and(m_dbin, 0xffff)</c> + <c>sr_nzvc</c> — the low word's N, Z, V, C; X untouched.</summary>
    void MoveLongLowFlags(uint value)
    {
        AluAnd16(Low16(value), 0xFFFF);
        SrNzvc();
    }

    /// <summary>
    /// <c>alu_and(high16(...), 0xffff)</c> + <c>sr_nz_u</c> — the high word's N, its Z (which can only
    /// be <em>cleared</em>), and V and C left as they were. That pair, run in this order, is exactly a
    /// thirty-two-bit logic flag update; run separately they are what a fault in between leaves behind.
    /// </summary>
    void MoveLongHighFlags(uint value)
    {
        AluAnd16(High16(value), 0xFFFF);
        SrNzU();
    }

    /// <summary>
    /// A word store of the <c>MOVE</c> block, plus the aborting state's own 4 clocks when the address is
    /// odd. A word store at an odd address never reaches the wire, but the microcode still charges the
    /// state that issued it and then 4 more for the abandoned cycle — the corpus's record shows both
    /// (`we` for 4, then the entry's `n 8`), so an aborted store is 8 clocks and every faulting record of
    /// the family was exactly 4 short without the second half (1 042 records).
    /// </summary>
    async ValueTask WriteMoveWordAsync(uint address, ushort data)
    {
        await WriteWordAsync(address, data);
        if (_busFaulted && _addressError)
        {
            await ClocksAsync(4);
        }
    }

    /// <summary>
    /// The store itself: <c>maww2</c>/<c>malw2</c> and their siblings. A long value goes out **high word
    /// first**, then the low one at <c>address + 2</c> (the <c>-(An)</c> form is the exception and writes
    /// it in the block above), with the class's two flag updates where the microcode puts them.
    /// </summary>
    async ValueTask WriteMoveAsync(uint address, int size, uint value, MoveLongFlags flags)
    {
        if (size == 1)
        {
            await WriteMoveWordAsync(address, (ushort)value);
            return;
        }

        switch (flags)
        {
            case MoveLongFlags.BeforeBoth:
                MoveLongLowFlags(value);                       // rawl1 / mmdl1
                MoveLongHighFlags(value);                      // rall3 / mawl2
                await WriteMoveWordAsync(address, (ushort)High16(value));
                if (_busFaulted)
                {
                    return;
                }

                await WriteMoveWordAsync(address + 2, (ushort)Low16(value));
                return;

            case MoveLongFlags.AfterFirstWord:
                await WriteMoveWordAsync(address, (ushort)High16(value));   // rmrl1 / rmil1
                if (_busFaulted)
                {
                    return;
                }

                MoveLongLowFlags(value);                       // rmrl2 / rmil2
                await WriteMoveWordAsync(address + 2, (ushort)Low16(value));
                if (_busFaulted)
                {
                    return;
                }

                MoveLongHighFlags(value);                      // rmrl3 / rmil3
                return;

            case MoveLongFlags.HighBeforeFirstWord:
                MoveLongHighFlags(value);                      // rmdl2
                await WriteMoveWordAsync(address, (ushort)High16(value));
                if (_busFaulted)
                {
                    return;
                }

                MoveLongLowFlags(value);                       // rmdl3
                await WriteMoveWordAsync(address + 2, (ushort)Low16(value));
                if (_busFaulted)
                {
                    return;
                }

                MoveLongHighFlags(value);                      // rmrl3
                return;

            default:                                           // BetweenWords
                MoveLongLowFlags(value);                       // mmrl1 / mall2
                await WriteMoveWordAsync(address, (ushort)High16(value));
                if (_busFaulted)
                {
                    return;
                }

                MoveLongHighFlags(value);                      // mmrl2 / mall3
                await WriteMoveWordAsync(address + 2, (ushort)Low16(value));
                return;
        }
    }

    // =========================================================================================
    // The control transfers — BSR, JMP, UNLK
    // =========================================================================================
    //
    // Three measured facts separate these from the families above. (1) A transfer **abandons the stream**
    // it would refill, so its extension word comes out of the queue free — `JumpWordAsync(0)` — and the
    // reads it costs are the target's (`n2 r r` for `JMP (d16,An)`, 18 clocks for `BSR` in both sizes).
    // (2) Its own clocks **lead** (a branch's 2, a jump's shape-dependent 0/2/6), because a transfer never
    // runs the closing fetch that spends `_own`. (3) The frame's saved counter is the script's: `BSR`
    // puts the target in `m_pc` before its fetch, `JMP` never does, and `UNLK` takes the boundary's word.

    /// <summary>
    /// `BSR` — bsrw1, bsrw2, bsrw3, then `malw3`, `b`, `mmrw3` at the target.
    ///
    /// <para>
    /// The return address is the word past the opcode, and it is latched at bsrw1 — after the word form's
    /// displacement has been taken out of the queue, which is why it is `opcode + 2` on `BSR.b` and
    /// `opcode + 4` on `BSR.w` (the corpus's push says both). The high word goes out first (`bsrw2` at
    /// `sp − 4`, then `bsrw3` at `sp − 2`, where the low word is `m_aluo` — the latched address, not a
    /// re-read of `m_au`). And the fetch at the target <em>is</em> a fetch state (`malw3`: `m_pc = m_au`),
    /// so an odd target's frame saves the target itself and not the word before it, which is what #76's
    /// default rule would give: 1 271 records of the file, `saved − opcode` = the displacement.
    /// </para>
    /// </summary>
    async ValueTask BranchSubroutineAsync(ushort op)
    {
        int displacement = op & 0xFF;

        if (displacement == 0)
        {
            // the word form: the displacement is the queue's own word, and a branch does not refill the slot
            // behind it — the fetch that would is the one the branch abandons (`BSR.w` is 18 clocks, like
            // `BSR.b`; pop it with `FetchAsync` and the six word-form records are 4 clocks long)
            displacement = (short)await JumpWordAsync(0);
            if (_busFaulted) return;
        }
        else if (displacement >= 0x80)
        {
            displacement -= 0x100;      // the byte form's displacement is signed (0xEC is −20, not 236)
        }

        // The script, in the listing's order (`microcode-walk.py --op 6100`; `61ec` is the same six states
        // for the byte form). The two writes push the return address high word first, the target's read is
        // `malw3` — a fetch state, so an odd target's frame saves itself — and the loop's pair follows.
        //
        //   ours: the word form's displacement is a free pop (`JumpWordAsync(0)`): the listing's `bsrw3`
        //   takes `ext32(m_dbin)`, the word the previous instruction's closing fetch already landed, and a
        //   branch never refills the slot behind it. `alu_and(m_au, 0xffff)` is the constants ROM's route to
        //   `m_aluo` (bsrw3's `m_dbout`), so the low word here is `Low16` of the latched address. And the
        //   two reads after `malw3`, with the `b`/`mmrw3` behind them, are `RefillAsync`'s — named once at
        //   `CloseInstructionAsync` — where `b`'s `m_pc = m_au` is the frame's counter, declared before
        //   either read lands.
        //
        // bsrw1 (0a9)  m_at = m_au; alu_and(m_au, 0xffff); m_au = m_da[m_sp] - 4                      2
        await IdleAsync();                                 // bsrw1 — `alu r=0 c=1`, the transfer's own 2

        uint returnAddress = _au;                          // the latch: bsrw1's `m_at = m_au`
        uint stack = A7 - 4;
        A7 = stack;

        // `A7 = stack` above is bsrw2's `m_da[m_sp] = m_au`: the stack pointer is a register here, so the
        // commit is a plain write and it lands before the first store, as the listing has it.
        //
        // bsrw2 (10e)  m_aob = m_au; m_dbout = high16(m_at); m_da[m_sp] = m_au; m_au = m_au + 2;
        //              write m_dbout at m_aob                                                         4
        await WriteWordAsync(stack, (ushort)High16(returnAddress));      // bsrw2 — high word first
        if (_busFaulted) return;
        // bsrw3 (0aa)  m_aob = m_au; m_dbout = m_aluo; m_au = ext32(m_dbin) + m_pc;
        //              write m_dbout at m_aob                                                         4
        await WriteWordAsync(stack + 2, (ushort)Low16(returnAddress));   // bsrw3
        if (_busFaulted) return;

        long target = (long)_opcodeAddress + 2 + displacement;
        FrameSavesOnFetch((uint)target);                   // `b`: `m_pc = m_au` = the target
        // malw3 (1e1)  m_aob = m_au; m_movemr = m_dbin; m_pc = m_au; m_ftu = m_dbin; m_au = m_au + 2;
        //              read at m_aob                                                                  4
        await RefillAsync((uint)target);                   // malw3 then `b`/`mmrw3`: the target's pair

        // an odd target aborts on that fetch, and this family is scripted: the aborting state's own 4 is the
        // port's to charge (the shape table carried it in BSR's row, and that row is gone)
        await ChargeAbortedReadAsync();
    }

    /// <summary>
    /// `JMP &lt;ea&gt;` — MAME's seven scripts, one per addressing mode: their address states' idle run
    /// (`jmpd1` 2, `jmaw1` 2, `jmaw1`+`jmal2` for the long word, `jmpx0`/`jmpx1`/`jmpx2|3` 6), then
    /// `bbci3`'s read at the target, then `b`/`mmrw3`'s fetch at its successor.
    ///
    /// <para>
    /// The target read is <b>not</b> a fetch state: `m_pc` keeps the boundary's word until `b` sets it, so
    /// a `JMP` frame saves `opcode + 2` on <em>every</em> shape — whatever its address cost (911 records,
    /// the same number for `(An)`, `(d16,An)`, `(d8,An,Xn)` and the absolutes). The idle runs are charged
    /// here for the same reason the saved counter is: `_own` is only spent at the closing fetch, and a jump
    /// never runs it — which is why the jump's clock column sat at −2/−4/+6/−8 per shape before this port.
    /// </para>
    /// </summary>
    async ValueTask JumpAsync(ushort op)
    {
        int mode = FtuMode;
        int reg = FtuRegister;
        long target;

                // The seven scripts, one per addressing mode (`microcode-walk.py --op 4ed0`, `4ee8`, `4ef0`,
        // `4ef8`, `4ef9`, `4efa`, `4efb`; `4efa` is `4ee8`'s listing and `4efb` is `4ef0`'s), and one shape: the
        // target's address, its read, then the pair at its successor.
        //
        //   ours: the target's read is one call (`ReadTargetAsync`, below) where the listing gives it to `bbci3`
        //   on every shape but the two absolutes — `(xxx).w` ends on `jmaw1`'s latch and `(xxx).l` on `jmal2`,
        //   the state that builds the address and reads it, so that read is the tail's as well. The adder's
        //   moves are the derivation's, and the pair is written inline here, not through `RefillAsync` — the
        //   second site that expands it, named once at `CloseInstructionAsync`.
        //
        switch (mode)
        {
            case 2:                                            // jmp_an — jmpa1: a read at the register
                // jmpa1 (255)  m_aob = m_da[ry]; m_au = m_da[ry] + 2; read at m_aob                   4
                target = GetA(reg);
                break;

            case 5:                                            // jmp_das — jmpd1
                // jmpd1 (2b4)  m_au = ext32(m_dbin) + m_da[ry]                                        2
                await IdleAsync();
                target = GetA(reg) + (uint)(short)await JumpWordAsync(0);
                if (_busFaulted) return;
                break;

            case 6:                                            // jmp_dais — jmpx0, jmpx1, jmpx2/jmpx3
            {
                // jmpx0 (1f7)  alu_ext(m_dbin)                                                        2
                await IdleAsync(2);                            // jmpx0 — `alu_ext` of the brief extension word
                ushort extension = await JumpWordAsync(0);
        //   jmpx1's `m_t` is the branch: jmpx2 is the word index (`ext32`), jmpx3 the long one, decided
        //   inside `IndexValue`.
                // jmpx1 (2b6)  m_t = m_irc & 0x0800; m_au = ext32(m_aluo) + m_da[ry]                  2
                // jmpx2 (02d)  m_au = m_au + ext32(m_da[map_sp(m_irc >> 12)])                         2
                // jmpx3 (0ed)  m_au = m_au + m_da[map_sp(m_irc >> 12)]                                2
                await IdleAsync(4);                            // jmpx1 and the index state
                target = GetA(reg) + (uint)ExtensionDisplacement(extension) + IndexValue(extension);
                break;
            }

            case 7 when reg == 0:                              // jmp_adr16 — jmaw1
                // jmaw1 (297)  m_at = ext32(m_dbin)                                                   2
                await IdleAsync();
                target = (uint)(short)await JumpWordAsync(0);
                break;

            case 7 when reg == 1:                              // jmp_adr32 — jmal1, jmal2
            {
                // no idle run at all: `jmal1` merges the queue's word with the one read at the fetch pointer,
                // and `jmal2` reads at the address it built (12 clocks, three reads, nothing between them)
                // jmal1 (1f6)  m_aob = m_au; set_16h(m_at, m_dbin); m_au = m_au + 2; read at m_aob    4
                uint high = await JumpWordAsync(0);             // jmal1 — the word the queue holds
                uint low = await JumpWordAsync(1);              // jmal2 — one read at the fetch pointer
                if (_busFaulted) return;
                target = (high << 16) | low;
                break;
            }

            case 7 when reg == 2:                              // jmp_dpc — jmpd1, then the same bbci3
                await IdleAsync();
                target = (long)_opcodeAddress + 2 + (short)await JumpWordAsync(0);
                break;

            case 7 when reg == 3:                              // jmp_dpci — jmpx0..jmpx3 off the counter
            {
                await IdleAsync(2);
                ushort extension = await JumpWordAsync(0);
                await IdleAsync(4);
                target = (long)_opcodeAddress + 2
                         + ExtensionDisplacement(extension) + IndexValue(extension);
                break;
            }

            default:                                           // (An)+, -(An), Dn… : not a JMP encoding
                await EnterExceptionAsync(4, 0);
                return;
        }

        uint address = (uint)target;

        // bbci3 — the target's read, in program space and outside any fetch state. An odd target aborts
        // here: the frame saves the word the boundary left, and the state's own 4 is the port's to charge
        // bbci3 (0c5)  m_aob = m_au; m_au = m_au + 2; read at m_aob                                   4
        // jmal2 (2b3)  m_aob = merge_16_32(high16(m_at), m_dbin);
        //              m_au = merge_16_32(high16(m_at), m_dbin) + 2; read at m_aob                    4
        FrameSavesOnFetch(_opcodeAddress + 2);
        ushort first = await ReadTargetAsync(address);
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        // b + mmrw3 — `m_pc = m_au`, then the one fetch that lands the queue at the target
        _flowTransferred = true;
        _ird = first;
        _pc = address + 2;
        _ir = await ReadProgramAsync(address + 2);
        if (_busFaulted) return;
        _ircValid = false;
        _au = address;
    }

    /// <summary>
    /// `JSR &lt;ea&gt;` — `jsra1`/`jsrd2`+`jsrd3` (the target's read, which comes <b>before</b> the push),
    /// `jsaw2`/`jsaw3` (the return address, high word first), then `b`/`mmrw3` at the target.
    ///
    /// <para>
    /// The same two facts as `JMP` (the address states' idle run is the shape's own, the extension word is
    /// free), plus the one that makes the pushes land where the corpus puts them: <c>m_pc</c> is advanced
    /// past each extension word <em>as it is consumed</em> (`m_au = m_pc + 2; m_pc = m_au`), so the return
    /// address is the counter past the last one — `opcode + 2` for `(An)`, `+4` for one extension word,
    /// `+6` for `(xxx).l` — and an odd <em>target</em> faults on that read with the same counter saved
    /// (`FrameSavesOnFetch(_au)`), the stack untouched and nothing pushed (FINDINGS row 43). The pushes
    /// are the stacked copy of that counter, so both are `_au` at one instant.
    /// </para>
    /// </summary>
    async ValueTask JumpSubroutineAsync(ushort op)
    {
        int mode = FtuMode;
        int reg = FtuRegister;
        long target;

                // The seven scripts, one per addressing mode (`microcode-walk.py --op 4e90`, `4ea8`, `4eb0`,
        // `4eb8`, `4eb9`, `4eba`, `4ebb`; `4eba` is `4ea8`'s listing and `4ebb` is `4eb0`'s, off the counter),
        // and one shape throughout: the target's address, the target's read, the return address's two words
        // high word first, then the pair at the target.
        //
        //   ours: the read is one call for every mode (`ReadTargetAsync`, below) where the listing gives it to
        //   the mode's last address state — `jsra1`, `jsrd3`, `jsaw1`, `jsal2` — so each block below is that
        //   state's address half and the tail is its read. The adder's moves are the derivation's
        //   (`FetchPosition`) except the one the code pays for explicitly: `jsal2`'s queue read moves the
        //   counter by 2. And the pair at the target is written inline here, not through `RefillAsync` — the
        //   second site that expands it, named once at `CloseInstructionAsync`.
        //
        switch (mode)
        {
            case 2:                                            // jsr_ais — jsra1, no idle run
                // jsra1 (273)  m_aob = m_da[ry]; m_at = m_da[ry]; m_au = m_da[m_sp] - 4; read at m_aob;
                //                                                                                     4
                target = GetA(reg);
                break;

            case 5:                                            // jsr_das — jsrd1
                // jsrd1 (2b0)  m_au = ext32(m_dbin) + m_da[ry]                                        2
                await IdleAsync();
                target = GetA(reg) + (uint)(short)await JumpWordAsync(0);
                if (_busFaulted) return;
                break;

            case 6:                                            // jsr_dais — jsrx0, jsrx1, jsrx2/jsrx3
            {
                // jsrx0 (1f3)  alu_ext(m_dbin)                                                        2
                await IdleAsync(2);                            // jsrx0 — `alu_ext` of the brief extension word
                ushort extension = await JumpWordAsync(0);
                //   jsrx1's `m_t` is the branch: jsrx2 is the word index (`ext32`), jsrx3 the long one,
                //   decided inside `IndexValue`; one of the two, with jsrx1's 2, is the clocks below.
                //   (Prose above the block: a paragraph after a state line would take its charge.)
                //
                // jsrx1 (2b2)  m_t = m_irc & 0x0800; m_au = ext32(m_aluo) + m_da[ry]                  2
                // jsrx2 (029)  m_au = m_au + ext32(m_da[map_sp(m_irc >> 12)])                         2
                // jsrx3 (0e9)  m_au = m_au + m_da[map_sp(m_irc >> 12)]                                2
                await IdleAsync(4);                            // jsrx1 and the index state
                target = GetA(reg) + (uint)ExtensionDisplacement(extension) + IndexValue(extension);
                break;
            }

            case 7 when reg == 0:                              // jsr_adr16 — one idle state
                // jsaw0 (293)  m_at = ext32(m_dbin)                                                   2
                // jsaw1 (270)  m_aob = m_at; m_pc = m_au; m_au = m_da[m_sp] - 4; read at m_aob        4
                await IdleAsync();
                target = (uint)(short)await JumpWordAsync(0);
                break;

            case 7 when reg == 1:                              // jsr_adr32 — jsal1, jsal2, no idle run
            {
                // jsal1 (1f2)  m_aob = m_au; set_16h(m_at, m_dbin); m_au = m_au + 2; read at m_aob    4
                uint high = await JumpWordAsync(0);            // jsal1 — the word the queue holds
                // jsal2 (256)  m_aob = merge_16_32(high16(m_at), m_dbin); m_pc = m_au;
                //              set_16l(m_at, m_dbin); m_au = m_da[m_sp] - 4; read at m_aob            4
                uint low = await JumpWordAsync(1);             // jsal2 — one read at the fetch pointer
                if (_busFaulted) return;

                // jsal2 also commits `m_pc = m_au`, and that read moved `m_au` by 2 — so this shape's
                // counter is the only one that a queue read *does* advance (or the return address and
                // the frame are both a word low: 90 records)
                AuPlusTwo();
                target = (high << 16) | low;
                break;
            }

            case 7 when reg == 2:                              // jsr_dpc — jsrd1, then jsrd2
                await IdleAsync();
                target = (long)_opcodeAddress + 2 + (short)await JumpWordAsync(0);
                break;

            case 7 when reg == 3:                              // jsr_dpci — jsrx0..jsrx3 off the counter
            {
                await IdleAsync(2);
                ushort extension = await JumpWordAsync(0);
                await IdleAsync(4);
                target = (long)_opcodeAddress + 2
                         + ExtensionDisplacement(extension) + IndexValue(extension);
                break;
            }

            default:                                           // Dn, (An)+, -(An), #imm : not a JSR encoding
                await EnterExceptionAsync(4, 0);
                return;
        }

        uint address = (uint)target;

        // jsra1/jsrd3 — the target's read, before the push, and the state that advanced `m_pc` past the
        // last extension word is also the one an odd target's frame saves
        //   `jsr_dpc`'s two states are `jsrd1`'s listing again, named once in `case 5`; the indexed mode's
        //   four are `case 6`'s.
        // jsrd2 (274)  m_aob = m_au; m_at = m_au; m_au = m_pc + 2                                     0
        // jsrd3 (2b1)  m_pc = m_au; m_au = m_da[m_sp] - 4; read at m_aob                              4
        FrameSavesOnFetch(_au);
        ushort first = await ReadTargetAsync(address);
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        // jsaw2, jsaw3 — the return address's two words, high first, the stack pointer committed with
        // the first one (as `m_da[m_sp] = m_au` does)
        // jsaw2 (271)  m_aob = m_au; m_dbout = high16(m_pc); m_da[m_sp] = m_au; m_au = m_au + 2;
        //              write m_dbout at m_aob                                                         4
        uint returnAddress = _au;
        uint stack = A7 - 4;
        A7 = stack;
        await WriteWordAsync(stack, (ushort)High16(returnAddress));
        if (_busFaulted) return;
        // jsaw3 (272)  m_aob = m_au; m_ir = m_irc; m_dbout = m_pc; m_au = m_at + 2;
        //              write m_dbout at m_aob                                                         4
        await WriteWordAsync(stack + 2, (ushort)Low16(returnAddress));
        if (_busFaulted) return;

        // b + mmrw3 — `m_pc = m_au` at the target's successor, `m_ird = m_ir` (the target's own word)
        _flowTransferred = true;
        _ird = first;
        _pc = address + 2;
        _ir = await ReadProgramAsync(address + 2);
        if (_busFaulted) return;
        _ircValid = false;
        _au = address;
    }

    /// <summary>
    /// `UNLK An` — "tears the frame down: `SP` = `An`, then pops `An`" (§5.1). The die's script is
    /// `unlk1`..`unlk4`: the two words are read at the frame pointer the register names, the new stack
    /// pointer is committed to `m_da[m_sp]` **after** both are in (an odd `An` therefore enters the
    /// address error with the stack pointer untouched — 1 115 records, whose first differing field was
    /// `ssp`/`usp`), and the instruction's one trailing fetch is `unlk4`'s read at `m_pc`. That
    /// `m_pc = m_au` of unlk1 is also the frame's saved counter for an odd `An`: `opcode + 4`, the
    /// boundary's word, not the word before it.
    /// </summary>
    async ValueTask UnlinkAsync(ushort op)
    {
        int reg = FtuRegister;

        // unlk_as (the prologue: `int ry = map_sp((m_irdi & 7) | 8)` — the FTU's register field)
        //
        //   ours: the two states' accesses are one helper — `ReadLongAsync` reads at An and at An + 2
        //   and merges them, so unlk1's `m_aob`/`m_au` bookkeeping and unlk2's are the call's halves.
        //
        // unlk1 (119)  m_aob = m_da[ry]; m_pc = m_au; m_at = m_da[ry]; m_au = m_da[ry] + 2;
        //              read at m_aob                                                                  4
        // unlk2 (1fe)  m_aob = m_au; m_ir = m_irc; set_16h(m_at, m_dbin); m_au = m_au + 2;
        //              read at m_aob                                                                  4
        BeginFetchState();                                 // unlk1 — `m_pc = m_au`
        uint address = GetA(reg);                          // m_at = m_da[ry]; m_aob = m_da[ry]
        uint value = await ReadLongAsync(address);         // unlk1 then unlk2, at An and An + 2
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        //   ours: unlk3's `m_aob`/`m_au` pair and unlk4's read are the loop's closing fetch
        //   (`CloseInstructionAsync`'s trailing fetch, which the script lets fall through), so a
        //   fault above cancels it, as the die's own abort does.
        //
        // unlk3 (27c)  m_aob = m_pc; m_da[m_sp] = m_au; m_au = m_pc + 2                               0
        A7 = address + 4;                                  // unlk3 — m_da[m_sp] = m_au
        // unlk4 (27d)  m_ird = m_ir; m_da[ry] = merge_16_32(high16(m_at), m_dbin);
        //              read at m_aob                                                                  4
        SetA(reg, value);                                  // unlk4 — merge_16_32 of the two words
    }

    // =========================================================================================
    // ADDX.l / SUBX.l — the long exchange arithmetic, `<ea>,Dn` and `Dn,<ea>`
    // =========================================================================================

    /// <summary>
    /// The long <c>ADDX</c>/<c>SUBX</c>: opmode 2 (<c>&lt;ea&gt;,Dn</c>) and opmode 6
    /// (<c>Dn,&lt;ea&gt;</c>) on lines 9 and D with the size field at 2. Those are the two opmodes the
    /// two-operand longs use, so <see cref="IsScriptedAluLong"/> already answers 0 clocks for them —
    /// this predicate is the one that says <em>why</em>: they are the <c>asxl</c>/<c>rorl</c> block,
    /// not the ALU tails. A byte or a word is not here: those are still measured by shape.
    /// </summary>
    static bool IsScriptedExchangeLong(ushort op) =>
        ((op & 0xF130) is 0x9100 or 0xD100) && ((op >> 6) & 0x3) == 2;

    /// <summary>
    /// ADDX.l / SUBX.l — two scripts, and both step their operands <b>a word at a time</b>, which is
    /// the one place they differ from the other long forms.
    ///
    /// <para>
    /// The register form is <c>rorl1</c> (the whole 32-bit operation and its flags, no bus cycle),
    /// <c>rorl2</c> (<c>set_16l</c> then the instruction's one read), <c>rorl3</c> + <c>roml3</c>
    /// (<c>set_16h</c> between two 2-clock idles) — 8 clocks, 4 of them off the bus.
    /// </para>
    /// <para>
    /// The memory form is <c>asxl1</c>…<c>asxl8</c> + <c>morw2</c>: one idle state, the source's low
    /// word at <c>A − 2</c>, <b>the register's commit</b>, the source's high word at <c>A − 4</c>, the
    /// destination's low word at <c>A − 2</c>, its commit, its high word at <c>A − 4</c>, the low
    /// result written at <c>rx − 2</c>, the instruction's one read, and the high result at
    /// <c>rx − 4</c>. Three facts the corpus pins and the word form does not share: the commit comes
    /// <em>after</em> each pair's first read (<c>asxl3</c>/<c>asxl6</c> — an odd register is left
    /// where it was, 797 records), the low word goes out <em>before</em> the closing fetch
    /// (<c>asxl7</c>), and the two halves are ALUed word by word.
    /// </para>
    /// </summary>
    async ValueTask ExchangeLongAsync(ushort op, bool subtract)
    {
        int rx = (op >> 9) & 0x7;
        int ry = op & 0x7;
        uint extended = (uint)((_sr & FlagX) != 0 ? 1 : 0);

        if ((op & 0x0008) == 0)                            // rorl1 … roml3, the register form
        {
            uint registerSource = _d[ry];
            uint registerDestination = _d[rx];
            uint registerResult = subtract
                ? registerDestination - registerSource - extended
                : registerDestination + registerSource + extended;

            SetExchangeFlags(subtract, registerSource, registerDestination, registerResult, 2);  // rorl1

            Set16L(rx, Low16(registerResult));             // rorl2 — set_16l, before the read
            await TrailingFetchAsync();                    // rorl2 — the instruction's one read
            if (_busFaulted)
            {
                return;
            }

            await IdleAsync();                             // rorl3
            Set16H(rx, High16(registerResult));            // roml3 — set_16h, after the read
            await IdleAsync();                             // roml3
            return;
        }

        // asxl1 — `m_pc = m_au` and the memory form's one idle state (2 clocks)
        BeginFetchState();
        await IdleAsync();
        _alub = _dbin;

        _dbin = await ReadWordAsync(GetA(ry) - 2);         // asxl2 — the source's low word, at A − 2
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        // asxl3 — the source's register commits <b>after</b> the low word came back, so a first read
        // that faults leaves it where it stood (the word form's asxw2 commits before its read)
        SetA(ry, GetA(ry) - 4);
        uint sourceLow = _dbin;                            // asxl3 — alu_and(m_dbin, 0xffff)

        _dbin = await ReadWordAsync(GetA(ry));             // asxl4 — the source's high word, at A − 4
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        uint sourceHigh = _dbin;
        _alub = _dbin;                                     // asxl5 — m_alub = the source's high word

        uint destinationAddress = GetA(rx) - 2;            // asxl4/asxl5
        _dbin = await ReadWordAsync(destinationAddress);   // asxl5 — the destination's low word
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        uint destinationLow = _dbin;
        SetA(rx, GetA(rx) - 4);                            // asxl6 — the destination commits

        _dbin = await ReadWordAsync(GetA(rx));             // asxl6 — the destination's high word
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        uint source = Merge16_32(sourceHigh, (ushort)sourceLow);
        uint destination = Merge16_32(_dbin, (ushort)destinationLow);
        uint result = subtract
            ? destination - source - extended
            : destination + source + extended;

        // asxl6/asxl8 — MAME ALUs the halves and calls sr_xnzvc_u on each; the thirty-two-bit
        // operation gives the same result and the same final flags, because the low half's carry
        // out is the high half's carry in and `_u` only ever clears Z. No fault can land between
        // the halves here (a long predecrement's two words have the same parity), so the two calls
        // have one observable value.
        SetExchangeFlags(subtract, source, destination, result, 2);

        await WriteWordAsync(destinationAddress, (ushort)Low16(result));   // asxl7 — low word, before the read
        if (_busFaulted)
        {
            return;
        }

        await TrailingFetchAsync();                        // asxl8 — the instruction's one read
        if (_busFaulted)
        {
            return;
        }

        await WriteWordAsync(GetA(rx), (ushort)High16(result));    // morw2 — the high word, at m_at
    }

    // =========================================================================================
    // The variable-time families — MULU/MULS, DIVU/DIVS, CHK
    // =========================================================================================
    // Their clocks are a counter on a value the core holds: a multiply's loop shifts the multiplier one
    // bit (MULU) or one Booth digit (MULS) at a time, a divide restores its remainder sixteen times, and
    // CHK is the six clocks of its three comparison states. So a wrong value is a wrong clock, and the
    // only transcription that can be right is the microcode's own loop, state for state. The states are
    // M68000.Micro.cs's sixteen-bit ALU: `AluAdd16`/`AluSub16`/`AluAnd16` write the flag latch and each
    // loop state ends with `SrNzvc`/`SrNzvcU`, exactly as the listing's `sr_nzvc` calls do.
    //
    // **The operand read is one block for all five**, because the microcode's word-EA states (`adrw1`,
    // `adsw1`, `aixl0`, `abwl1` …) are the word block the single-operand families already use — with one
    // difference the corpus measures: a PC-relative shape reads its operand through the *instruction
    // stream* (`SSW_PROGRAM`), so a fault there records a program access in the frame's status word.
    //
    // The counter is MAME's `m_au`, loaded from the instruction's own timing constant (`m_ftu`): **15**
    // for all five (the loop tests `au & 0x3f` and the MUL loops run sixteen bit-positions, the divide
    // sixteen bits of quotient). Measured, not chosen: with 15 every non-faulting record of the four
    // loops reproduces the corpus's own internal clocks, its destination register and its flags.

    /// <summary>MULU (bit 8 clear) and MULS — line C, opmode 3.</summary>
    static bool IsScriptedMultiply(ushort op) => (op & 0xF1C0) is 0xC0C0 or 0xC1C0;

    /// <summary>DIVU (bit 8 clear) and DIVS — line 8, opmode 3.</summary>
    static bool IsScriptedDivide(ushort op) => (op & 0xF1C0) is 0x80C0 or 0x81C0;

    /// <summary>CHK — line 4, bits 8-6 = 110 (LEA's 111 is a different family).</summary>
    static bool IsScriptedChk(ushort op) => (op & 0xF1C0) == 0x4180;

    /// <summary>
    /// MAME's <c>m_pc</c> while one of these families reads its operand: the boundary leaves it at the
    /// instruction's <b>first extension word</b> (`opcode + 2`), and only an absolute address's fetch
    /// state moves it (<see cref="BeginFetchState"/>). Measured on the group-0 frames of all five
    /// families: `opcode + 2` when the extension words are a displacement or an index, `opcode + 4` for
    /// `(xxx).w`, `opcode + 6` for `(xxx).l` — the same +2 the old handlers were high by on all 1 459
    /// faulting records.
    /// </summary>
    void FrameSavesFirstExtensionWord() => _pc = _opcodeAddress + 2;

    /// <summary>
    /// The source operand of MULU/MULS/DIVU/DIVS/CHK: MAME's word-EA blocks, in the states' own order,
    /// with their own idles. <c>null</c> when a bus cycle faulted, which ends the instruction.
    /// </summary>
    /// <remarks>
    /// The two shapes with their own 2-clock state are `-(An)` (`pdcw1`, before the read) and the indexed
    /// one (`aixl0`); everything else is the read alone. A PC-relative shape's read carries the
    /// instruction stream's bus status (<see cref="ReadOperandProgramAsync"/>) — that is what makes its
    /// fault frame a *program* access, and it is the one field the old handlers had wrong on every
    /// PC-relative record.
    /// </remarks>
    async ValueTask<uint?> ReadWordSourceAsync(int mode, int reg)
    {
        uint value;

        switch (mode)
        {
            case 0:                                            // ds — the register itself
                return _d[reg] & 0xFFFF;

            case 1:                                            // as
                return GetA(reg) & 0xFFFF;

            case 2:                                            // ais — adrw1, adrw2
                value = await ReadWordAsync(GetA(reg));
                break;

            case 3:                                            // aips — pinw1, pinw2
            {
                uint address = GetA(reg);
                SetA(reg, address + 2);                        // pinw2 commits *before* its read
                value = await ReadWordAsync(address);
                break;
            }

            case 4:                                            // pais — pdcw1 (2 clocks), pdcw2
            {
                uint address = GetA(reg) - 2;
                BeginFetchState();                             // pdcw1 moves m_pc, unlike pinw1
                await IdleAsync();
                SetA(reg, address);
                value = await ReadWordAsync(address);
                break;
            }

            case 5:                                            // das — adsw1, adsw2, adrw2
            {
                ushort displacement = await FetchAsync();
                if (_busFaulted) return null;
                value = await ReadWordAsync(GetA(reg) + Ext32(displacement));
                break;
            }

            case 6:                                            // dais — aixl0, aixl1, adrw2
            {
                await IdleAsync();
                ushort extension = await FetchAsync();
                if (_busFaulted) return null;
                value = await ReadWordAsync(
                    GetA(reg) + (uint)ExtensionDisplacement(extension) + IndexValue(extension));
                break;
            }

            default:                                           // mode 7
                switch (reg)
                {
                    case 0:                                    // adr16 — abwl1 (a fetch state), abwl2
                        BeginFetchState();
                        {
                            ushort absolute = await FetchAsync();
                            if (_busFaulted) return null;
                            value = await ReadWordAsync(Ext32(absolute));
                        }

                        break;

                    case 1:                                    // adr32 — abll1, abll2 (a fetch state), abll3
                    {
                        ushort high = await FetchAsync();
                        if (_busFaulted) return null;
                        BeginFetchState();
                        ushort low = await FetchAsync();
                        if (_busFaulted) return null;
                        value = await ReadWordAsync(Merge16_32(high, low));
                        break;
                    }

                    case 2:                                    // dpc — adsw1, adsw2, adrw2
                    {
                        ushort displacement = await FetchAsync();
                        if (_busFaulted) return null;
                        value = await ReadOperandProgramAsync(ConsumedAddress + Ext32(displacement));
                        break;
                    }

                    case 3:                                    // dpci — aixl0, aixl1, adrw2
                    {
                        await IdleAsync();
                        ushort extension = await FetchAsync();
                        if (_busFaulted) return null;
                        value = await ReadOperandProgramAsync(
                            ConsumedAddress + (uint)ExtensionDisplacement(extension) + IndexValue(extension));
                        break;
                    }

                    default:                                   // imm16 — e#w1, one read in the stream
                        value = await FetchAsync();
                        break;
                }

                break;
        }

        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return null;
        }

        return value;
    }

    /// <summary>
    /// MULU / MULS — <c>mulr1</c>/<c>mulm1</c>, <c>mulm2</c>, the loop (<c>mulm3</c>/<c>mulm4</c>/<c>mulm5</c>),
    /// then <c>mulm6</c>. The product is M x sum(b_i · 2^i) over the multiplier's sixteen bits — a
    /// shift-and-add, run as sixteen states because the ALU is sixteen bits wide and the chip has no
    /// multiplier to call.
    /// </summary>
    /// <remarks>
    /// <para>M = <c>m_alub</c> = the destination's low word, Q = <c>m_alue</c> = the source word. The pair
    /// (<c>m_aluo</c> : <c>m_alue</c>) is the whole state — the accumulator in the high half, the
    /// multiplier in the low half — and each rotate is the recurrence
    /// <c>P = floor((P + b·M·2^16) / 2)</c> with <c>P0 = Q</c>. Each step halves the register, so the
    /// weight of the add shrinks to the bit's own place, and after sixteen rotates the accumulator has
    /// absorbed exactly <c>sum(b_i·M·2^i) = M·Q</c>. The multiplier's bits leave through the bottom, one
    /// per rotate — which is why the branch tests bit 1 and not bit 0: bit 0 was consumed by the previous
    /// rotate — while the partial product arrives at the top through the add's carry. The pair then *is*
    /// the product, and <c>mulm6</c> stores it whole.</para>
    /// <para>MULS is the same register with Booth's recoding. A negative multiplier is a two's-complement
    /// bit string, so its raw bits do not sum to the right thing; the digits are the bit *differences*
    /// (<c>d_i = b_i − b_(i−1)</c>, so -1, 0 or +1), which is why one state adds (<c>mulm3</c>), one
    /// subtracts (<c>mulm5</c>) and one does neither, and why the rotate shifts in the accumulator's sign
    /// instead of the carry: the multiplier is sign-extended into the register, so a negative
    /// multiplier's run of leading 1s is digit 0 and costs nothing. Its clock is therefore 38 + 2k with k
    /// the number of signed digits — *not* the source's popcount, which is what the old timing table
    /// guessed (<c>34 + 2 × popcount</c>) and got wrong on 60 % of the records.</para>
    /// <para>Flags: N from bit 31 of the product, Z from "the whole product is zero" (Z is *set* before
    /// the loop and the loop's flag update can only clear it), V and C cleared by <c>mulm6</c>, X
    /// untouched. Clocks: 4 for the read + 2 × 16 for the rotates + 2n for the adds + 2 for the closing
    /// state = 38 + 2n, the manual's own formula, emerging from the loop instead of from a table.</para>
    /// </remarks>
    async ValueTask MultiplyAsync(ushort op)
    {
        bool signed = (op & 0x0100) != 0;
        int rx = (op >> 9) & 0x7;
        int mode = FtuMode;
        int reg = FtuRegister;

        FrameSavesFirstExtensionWord();

        uint? operand = await ReadWordSourceAsync(mode, reg);
        if (operand is null)
        {
            return;
        }

        // mulr1 / mulm1 — the chip's operands: m_alub takes the multiplicand (the destination register's
        // low word) and m_alue the multiplier (the source word), which makes the pair
        // (accumulator : multiplier) the multiply's entire state. The flags start at *Z set* —
        // alu_and(Dn, 0) — because the loop updates them with sr_nzvc_u, which can only clear Z: a Z left
        // standing after sixteen states therefore means every partial product was zero, i.e. the product
        // is zero. That is how a clear-only flag ends up carrying the instruction's Z.
        uint multiplicand = _d[rx] & 0xFFFF;
        _alue = operand.Value & 0xFFFF;
        AluAnd16(_d[rx], 0x0000);
        SrNzvc();

        // mulm2 — the first multiplier bit (bit 0, tested here because the loop's test reads bit 1: the
        // register shifts between the two tests), the latch — still the zero mulm1 left — into Dn's low
        // word, and the instruction's one bus read. The read sits *inside* the instruction, after the
        // operands are already latched, which is why a fault here saves the pc the later states leave.
        bool bit = (_alue & 1) != 0;
        Set16L(rx, _aluo);
        await TrailingFetchAsync();
        if (_busFaulted)
        {
            return;
        }

        if (bit)
        {
            // mulm2's branch — the first digit. MULU adds the multiplicand for a 1 bit; MULS *subtracts*
            // it, because Booth's first digit looks at bit 0 against an implied bit -1 of zero, so a 1 at
            // bit 0 is the digit -1. Getting this one branch the wrong way round still multiplies, but by
            // the complement — it was the first bug this port had.
            if (signed)
            {
                AluSub16(multiplicand, _aluo);                 // mulm5
            }
            else
            {
                AluAdd16(multiplicand, _aluo);                 // mulm3
            }

            SrNzvcU();
            await IdleAsync();
        }

        // m_au = ext32(m_ftu) — **the loop counts in the address adder**, loaded from the field register by
        // the constants ROM's select (`const_to_ftu`), because the trip count is the instruction's timing
        // constant and not a literal in the microcode: 15 for all five variable-time families, and the
        // sixteen rotates it gives are the multiply's sixteen bits. (Found by brute force against the
        // corpus's clocks: no vendored file sets it, and 15 is the value that reproduces every
        // non-faulting record.) The adder is free here because a loop forms no address and issues no
        // fetch; `AuEqualsConsumed()` at the exit puts the instruction's own position back.
        ConstToFtu(15);
        AuEqualsFtu();
        while (true)
        {
            // mulm4 — the branch is decided *before* the rotate, on the bits the register holds now. For
            // MULU it is the next multiplier bit (bit 1). For MULS it is a Booth digit read off the low
            // two bits — 01 adds, 10 subtracts, 00 and 11 do neither — which is the whole difference
            // between the two instructions' clock columns. The counter's zero test comes first, so the
            // sixteenth rotate is the one that ends the loop.
            int branch;
            if (AuZeroOnSixBits)
            {
                branch = 0;
            }
            else if (signed)
            {
                branch = (_alue & 3) switch { 1 => 1, 2 => 2, _ => 3 };
            }
            else
            {
                branch = (_alue & 2) != 0 ? 1 : 2;
            }

            AuMinusOne();

            // the half-step of the recurrence: the pair shifts right one bit, with the previous add's
            // carry (MULU) or the accumulator's sign (MULS) entering at bit 31. The counter has already
            // been decremented, so the rotate's own clock is charged before the exit is taken.
            if (signed)
            {
                AluRoxr32ms();
            }
            else
            {
                AluRoxr32mu();
            }

            SrNzvcU();
            await IdleAsync();

            if (branch == 0)
            {
                break;
            }

            if (branch == 1)
            {
                // mulm3 — add the multiplicand to the accumulator's high half. The carry out of this
                // sixteen-bit add is the bit the next rotate shifts back in at bit 31: the accumulator is
                // only half of a thirty-two-bit register, so the bit that falls off its top is caught in
                // C and put back. That round trip is the multiply's column arithmetic.
                AluAdd16(multiplicand, _aluo);                  // mulm3
                SrNzvcU();
                await IdleAsync();
            }
            else if (branch == 2 && signed)
            {
                // mulm5 — MULS's digit is -1: subtract the multiplicand instead. A borrow here means the
                // accumulator went negative, and the next rotate's sign-in bit is what keeps that from
                // corrupting the multiplier's remaining digits.
                AluSub16(multiplicand, _aluo);                  // mulm5
                SrNzvcU();
                await IdleAsync();
            }
        }

        // the adder was the counter; the instruction's position comes back from the PC register, which the
        // loop left alone (it fetches nothing) — the one statement that makes `B1` possible before the
        // full split
        AuEqualsConsumed();

        // mulm6 — the pair read whole is the product: the high half is the accumulator, the low half what
        // is left of the multiplier (which is why the product's low sixteen bits were never a separate
        // value). The flags come from the high half: N is bit 31, and Z — set before the loop and only
        // ever cleared — is "the whole product is zero". V and C are cleared by this same and, X survives.
        _d[rx] = ((_aluo & 0xFFFF) << 16) | (_alue & 0xFFFF);
        AluAnd16(_aluo, 0xFFFF);
        SrNzvcU();
        await IdleAsync();
    }

    /// <summary>
    /// DIVU — <c>dvur1</c>, <c>dvum2</c>, <c>dvum3</c>, the sixteen times <c>dvum5</c>…<c>dvumc</c>, then
    /// <c>dvum0</c>. Restoring long division in base 2: N = Q·D + R with 0 ≤ R &lt; D, one bit per
    /// iteration, sixteen iterations because the quotient is sixteen bits wide.
    /// </summary>
    /// <remarks>
    /// <para>The schoolbook step, per bit: <c>R = 2R + n_i</c>, then "if R is at least D, R = R − D and
    /// the quotient bit is 1, else the quotient bit is 0" — with <c>Q = sum(q_i · 2^(15−i))</c>. What
    /// makes the microcode cheap is that both halves of one register do the work: (<c>m_aluo</c> :
    /// <c>m_alue</c>) holds the remainder in its high half and the dividend's *remaining bits* in its low
    /// half, so one left shift is the doubling *and* the bring-down of the next bit — free, because the
    /// dividend is already in the register — and the bit the shift pushes in at the bottom is the
    /// quotient bit being assembled in the low half's tail. After sixteen shifts the dividend's bits have
    /// all been consumed into comparisons and the low half holds the quotient, which <c>dvum0</c> stores;
    /// the remainder is left in the high half, which <c>dvumc</c>/<c>dvum9</c> store.</para>
    /// <para><c>m_at</c> is the safety net: its low word keeps the remainder as it stood *before* each
    /// subtract, so the restore path (<c>dvume</c>) puts it back by moving a word (and its flags) rather
    /// than adding the divisor again, and its high word keeps the divisor.</para>
    /// <para>The two exits. A zero divisor is the group-1/2 exception (vector 5), taken at <c>dvum2</c>
    /// before anything is written. And <c>dvum3</c> subtracts the divisor from the dividend's high half:
    /// a borrow means the high half is smaller than the divisor, i.e. the quotient fits — no borrow means
    /// it does not, and <c>dvum4</c> sets <b>N and V</b> (<c>alu_over</c>) with Z and C cleared and Dn
    /// untouched. The manual says V; the corpus's 585 DIVU and 874 DIVS overflow records all want
    /// <c>N|V</c>, and that difference is 1 459 records of the gate.</para>
    /// </remarks>
    async ValueTask DivideUnsignedAsync(ushort op)
    {
        int rx = (op >> 9) & 0x7;
        int mode = FtuMode;
        int reg = FtuRegister;

        FrameSavesFirstExtensionWord();

        uint? operand = await ReadWordSourceAsync(mode, reg);
        if (operand is null)
        {
            return;
        }

        uint dividend = _d[rx];
        _alub = operand.Value & 0xFFFF;

        // dvur1 — the divisor into the working register's high word (where the states that need it read
        // it), the dividend's low word into the shift register's low half (its bits are the ones the loop
        // will compare, one per shift), and the divisor's own flags.
        _alue = dividend & 0xFFFF;
        uint at = _alub << 16;
        AluAnd16(_alub, 0xFFFF);
        SrNzvc();
        await IdleAsync();

        // dvum2 — the divide-by-zero test is "is the divisor zero" read off the flags just set, and the
        // same state measures the dividend's high half against the divisor: subtracting the divisor from
        // it leaves a borrow in C exactly when the high half is smaller, i.e. when the quotient fits.
        bool zero = (_isr & FlagZ) != 0;
        AluSub16(_alub, High16(dividend));
        SrNzvc();
        await IdleAsync();
        if (zero)
        {
            await EnterExceptionAsync(5, 0);
            return;
        }

        // dvum3 — latch the borrow (the overflow verdict), put the dividend's high half into the working
        // register's low word (it is the loop's starting remainder), and load the counter with ftu + 1:
        // sixteen iterations, because sixteen quotient bits are what has to be produced.
        bool borrow = (_isr & FlagC) != 0;
        at = Set16L(at, (ushort)(High16(dividend)));
        AluAnd16(High16(dividend), 0xFFFF);
        SrNzvc();
        await IdleAsync();

        if (!borrow)
        {
            // dvum4 — the quotient does not fit in sixteen bits: N and V (alu_over), Z and C cleared, Dn
            // untouched, and the one closing read. Ten clocks in total: this exit is why the corpus's
            // shortest DIVU records are ten long.
            AluOver();
            SrNzvc();
            await TrailingFetchAsync();
            return;
        }

        // dvum3 falls into dvum5's shift with a 0, which is the first "bring down a bit" of the divide.
        // The counter is the address adder again, one above the field register's constant (`au = ftu + 1`
        // — sixteen iterations for sixteen quotient bits), and the adder is free for it for the same
        // reason as the multiply's: the loop forms no address and issues no fetch.
        AtEqualsAu();          // m_at = m_au — the position the adder is about to spend
        ConstToFtu(15);
        AuEqualsFtu();
        AuPlusOne();
        bool shiftOne = false;
        while (true)
        {
            // dvum5 / dvum6 — the shift: the pair moves left one bit, doubling the remainder and pulling
            // the next dividend bit up into it, with the *previous* comparison's quotient bit entering at
            // the bottom (1 for "the subtraction stood", 0 for "it was restored"). The sign the previous
            // subtract left is what chooses between the two, so a reader should follow N, not a variable.
            bool negative = (_isr & FlagN) != 0;
            AuMinusOne();
            AluSla(shiftOne ? 1u : 0u);
            SrNzvcU();
            await IdleAsync();

            // dvum8 / dvum7 — the comparison, which is one sixteen-bit subtract of the remainder's high
            // half minus the divisor, and at the same time the copy of the pre-subtract remainder into
            // the working register (the value the restore path will want). The counter's zero test is the
            // last bit's exit, taken after the subtract so the final quotient bit is decided.
            bool last = AuZeroOnSixBits;
            at = Set16L(at, (ushort)(_aluo));
            AluSub16(_alub, _aluo);
            SrNzvc();
            await IdleAsync();

            if (last)
            {
                // dvumc (the borrow test) or dvum9 — the remainder, which is the pair's high half, into
                // Dn's high word; then the last quotient bit into the low half. There are two states
                // because the very last bit still has to be shifted in, and which of them runs is what
                // costs the divide its final two clocks.
                bool carry = (_isr & FlagC) != 0;
                _d[rx] = (_d[rx] & 0xFFFF) | (_aluo << 16);
                AluAnd16(_aluo, 0);
                await IdleAsync();
                if (!negative && carry)
                {
                    _alub = _aluo;                            // dvumf — the remainder's high half stands
                    _d[rx] = (_d[rx] & 0xFFFF) | (at << 16);
                    AluSla(0);
                }
                else
                {
                    _alub = _aluo;                            // dvumd — the restored remainder
                    AluSla(1);
                }

                break;
            }

            if (negative)
            {
                // dvum7 falls into dvum6's shift with a 1: the previous subtract's value stands as the new
                // remainder, and this bit's quotient bit is a 1. This path is one state shorter than the
                // restore path, which is the other half of why a divide's clock follows its quotient.
                shiftOne = true;
                continue;
            }

            // dvumb — the borrow test: no borrow means the subtraction stood, so the next shift brings in
            // a 1; a borrow means the remainder went negative and must be restored — the flags (and the
            // latch) are reloaded from the working register's low word, and the next shift brings in a 0.
            bool restore = (_isr & FlagC) != 0;
            await IdleAsync();
            if (!restore)
            {
                shiftOne = true;
                continue;
            }

            // dvume — the restore: reload the pre-subtract remainder and its flags. Note this is the only
            // place a divide adds a state for a *zero* quotient bit, so more 0s in the quotient means more
            // restores, i.e. a slower instruction — the data-dependent clock in its plainest form.
            AluAnd16(at, 0xFFFF);                             // dvume — the flags of the remainder
            SrNzvc();
            await IdleAsync();
            shiftOne = false;
        }

        // the adder was the counter; the position it latched at the loop's entry comes back
        AuEqualsAt();

        // dvum0 — the quotient into Dn's low word (the pair's low half after sixteen shifts *is* the
        // quotient), and the flags from a subtract of zero: so N and Z are the quotient's, V and C are
        // clear, and X survives. The closing read is the instruction's second bus cycle.
        _d[rx] = Set16L(_d[rx], (ushort)((_alue & 0xFFFF)));
        AluSub16(_alub, _alue);
        SrNzvc();
        await TrailingFetchAsync();
    }

    /// <summary>
    /// DIVS — the signed division: the same sixteen-bit restoring loop inside a sign-fixing prologue and
    /// epilogue. Written as MAME's states with their own labels, because the flow doubles back on itself
    /// (the quotient's sign decides where the remainder's is fixed) in a way a structured loop cannot
    /// state without a state variable that is harder to check against the listing.
    /// </summary>
    /// <remarks>
    /// <para>The algorithm is <c>|N| / |D|</c> with the signs put back afterwards. Both operands are
    /// two's-complement, so the loop can only divide magnitudes: the prologue complements the divisor
    /// when it is negative (<c>dvs05</c>) and the dividend's high word — with <c>alu_subc</c>, whose
    /// borrow chain is what makes a sixteen-bit complement of a thirty-two-bit value come out right
    /// (<c>dvs10</c>/<c>dvs11</c>) — so that what the loop sees is positive. The overflow test is the
    /// same borrow as DIVU's, taken on the sign-adjusted high half.</para>
    /// <para>The epilogue then fixes two signs, independently, which is the part people get wrong: the
    /// quotient's is the exclusive or of the operands' (<c>dvs16</c>/<c>dvs17</c> against
    /// <c>dvs1d</c>/<c>dvs1e</c>), and the **remainder takes the dividend's sign** — the 68000's rule,
    /// and the reason a signed remainder here is not C's <c>N % D</c>. <c>leaa2</c> then stores the whole
    /// working register: quotient in the low word, remainder in the high, which is also why this
    /// instruction's destination is written once rather than half at a time.</para>
    /// </remarks>
    async ValueTask DivideSignedAsync(ushort op)
    {
        int rx = (op >> 9) & 0x7;
        int mode = FtuMode;
        int reg = FtuRegister;

        FrameSavesFirstExtensionWord();

        uint? operand = await ReadWordSourceAsync(mode, reg);
        if (operand is null)
        {
            return;
        }

        uint dividend = _d[rx];
        uint at = 0;
        bool flag;

        // dvs02 — the divisor into m_alub and into the working register's high word, the dividend's low
        // word into the shift register, and the divisor's flags (which is how the next state reads its
        // sign without touching it again).
        _alub = operand.Value & 0xFFFF;
        _alue = dividend & 0xFFFF;
        at = _alub << 16;
        AluAnd16(_alub, 0xFFFF);
        SrNzvc();
        await IdleAsync();

        // dvs03 — three cases in one branch: zero divisor (the exception), negative divisor (the dividend
        // is complemented first, so the loop still divides magnitudes while the dividend keeps its own
        // sign for the epilogue), positive divisor. `alu_sub(m_alub, 0)` is the divisor's own sign read
        // back out of the ALU, N being bit 15 of the word.
        int sign = (_isr & FlagZ) != 0 ? 2 : (_isr & FlagN) != 0 ? 1 : 0;
        AluSub16(_alub, 0);
        SrNzvc();
        await IdleAsync();

        if (sign == 2)
        {
            await EnterExceptionAsync(5, 0);
            return;
        }

        if (sign == 1)
        {
            // dvs05 — the divisor is negative: its complement stays in the ALU latch (the subtract's
            // result) and becomes m_alub, so the loop divides by |D|.
            _alub = _aluo;
        }

        goto dvs04;

    dvs04:
        // dvs04 / dvs05 — the dividend's high word into the working register's low word (the loop's
        // starting remainder) and the sixteen-iteration counter, which is **the address adder**: the
        // die loads it from the field register (`m_au = merge_16_32(0, m_ftu) + 1`, the constants ROM's
        // 15 plus the one bit the sixteen quotient bits need), exactly as DIVU does. `AT` keeps the
        // position while the adder counts (`m_pc = m_au` at `dvs02` is the die's own latch; on this
        // tree the fetch state's copy is not yet writable — see the plan §8 item 3 — so the latch is
        // the measured equivalent DIVU uses), because this family's trailing fetch comes *after* the
        // loop and `_pc` therefore does not hold the consumed position.
        AtEqualsAu();          // m_at = m_au — the position the adder is about to spend
        at = Set16L(at, (ushort)(High16(dividend)));
        ConstToFtu(15);
        AuEqualsFtu();
        AuPlusOne();
        AluAnd16(High16(dividend), 0xFFFF);
        SrNzvc();
        await IdleAsync();
        goto dvs06;

    dvs06:
        // dvs06 — the dividend's own sign decides whether the first subtraction is a plain one or a
        // complemented one: a negative dividend is complemented here (its high word, through the borrow
        // chain), which is the "divide the magnitudes" step for the other operand.
        flag = (_isr & FlagN) != 0;
        AluSub16(dividend, 0);
        await IdleAsync();
        if (flag)
        {
            goto dvs10;
        }

        // dvs07 — positive dividend: the ordinary overflow test, high half minus divisor.
        AluSub16(_alub, at);
        SrNzvc();
        await IdleAsync();
        goto dvs08;

    dvs10:
        // dvs10 / dvs11 — the negative dividend: complement the high word with the borrow chain
        // (alu_subc, whose borrow-in is the previous subtract's C — that is what carries through the
        // thirty-two bits), then the overflow test against the complemented value.
        _alue = _aluo;
        AluSubC16(High16(dividend), 0);
        await IdleAsync();

        // dvs11
        at = Set16L(at, (ushort)(_aluo));
        AluSub16(_alub, _aluo);
        SrNzvc();
        await IdleAsync();

    dvs08:
        // dvs08 — the borrow is the overflow verdict, exactly as in DIVU: no borrow means the quotient
        // will not fit, and the exit is dvuma's (N and V, register untouched).
        flag = (_isr & FlagC) != 0;
        AluAnd16(at, 0xFFFF);
        SrNzvc();
        await IdleAsync();
        if (!flag)
        {
            // dvumz — the quotient does not fit, so the loop never runs: the die restores the adder
            // from its latch in this state (`m_au = m_pc + 2`) and reads at the latched position, which
            // is why this exit is one state, not the loop's tail
            AuEqualsAt();
            goto dvuma;
        }

    dvs09:
        // dvs09 — the loop's shift, 0 in at the bottom: entered from dvs08 with the first bit, and from
        // dvs0f after a restore, so it is the same "bring down a bit" state DIVU calls dvum5. Its
        // `m_au = m_au - 1` is the counter's step — the adder, counting down from the field register's
        // constant plus one.
        AuMinusOne();
        AluSla(0);
        await IdleAsync();

    dvs0c:
        // dvs0c — the comparison: remainder's high half minus divisor, the pre-subtract remainder copied
        // into the working register, and the counter's zero test (`!(m_au & 0x3f)`, the six low bits —
        // the adder is shaped like a small address) as the last bit's exit.
        flag = AuZeroOnSixBits;
        at = Set16L(at, (ushort)(_aluo));
        AluSub16(_alub, _aluo);
        SrNzvc();
        await IdleAsync();
        if (flag)
        {
            goto dvs0e;
        }

        goto dvs0d;

    dvs0d:
        // dvs0d — the borrow test that DIVU calls dvumb: a borrow sends the loop to the restore state
        // (dvs0f), no borrow to the shift that brings in a 1 (dvs0a).
        flag = (_isr & FlagC) != 0;
        await IdleAsync();
        if (flag)
        {
            goto dvs0f;
        }

        goto dvs0a;

    dvs0a:
        // dvs0a — the shift with a 1 at the bottom: the subtraction stood, so this quotient bit is a 1.
        AuMinusOne();
        AluSla(1);
        await IdleAsync();
        goto dvs0c;

    dvs0f:
        // dvs0f — the restore: the pre-subtract remainder's flags back from the working register, then
        // the 0-in shift at dvs09.
        AluAnd16(at, 0xFFFF);
        SrNzvc();
        await IdleAsync();
        goto dvs09;

    dvs0e:
        // dvs0e — the division loop is done; the last quotient bit is still to be shifted in, and which
        // way depends on the last comparison's borrow, so the epilogue starts with that branch.
        flag = (_isr & FlagC) != 0;
        await IdleAsync();
        if (flag)
        {
            goto dvs12;
        }

        goto dvs13;

    dvs13:
        // dvs13 — the last bit was a 1: the subtraction stands, and its quotient bit is shifted in.
        at = Set16L(at, (ushort)(_aluo));
        AluSla(1);
        await IdleAsync();
        goto dvs14;

    dvs12:
        // dvs12 — the last bit was a 0: restore, and shift in a 0. Its `m_au = m_au - 1` is a *dead*
        // decrement the loop has already left — the counter is not read again — which is why the adder
        // is restored at dvs14 and not at either of these two states.
        AuMinusOne();
        AluSla(0);
        await IdleAsync();
        goto dvs14;

    dvs14:
        // dvs14 — the division is over, and the adder gives the position back from its latch before any
        // of the tails reads it: from here every exit (`dvuma`, `dvum4`, `leaa2`) is a closing read at
        // the latched position. The die states the same restore at each tail (`m_au = m_pc + 2`), and
        // one state earlier is the same machine because nothing between writes the adder or the bus.
        AuEqualsAt();
        // the remainder's sign, read off the working register's high word; then the quotient's,
        // read off the dividend's own sign. Those two bits are the whole of the epilogue's decision.
        AluAnd16(at >> 16, 0xFFFF);
        SrNzvc();
        await IdleAsync();

        // dvs15 — `m_alub = m_alue` keeps the quotient in reach (m_alue is where the low half's tail
        // holds it) while the dividend's sign is latched: the remainder must follow the dividend, not the
        // quotient.
        flag = (_isr & FlagN) != 0;
        _alub = _alue;
        AluAnd16(High16(dividend), 0xFFFF);
        SrNzvc();
        await IdleAsync();
        if (flag)
        {
            goto dvs1d;
        }

        goto dvs16;

    dvs16:
        // dvs16 — positive quotient: copy the remainder's high word down (set_16h(m_at, m_at) is
        // at_hi = at_lo, the value the epilogue works on), re-read the dividend's sign for the
        // remainder's, and branch on it.
        flag = (_isr & FlagN) != 0;
        at = (at & 0xFFFF) | ((at & 0xFFFF) << 16);            // set_16h(m_at, m_at)
        AluAnd16(_alub, 0xFFFF);
        SrNzvc();
        await IdleAsync();
        if (flag)
        {
            goto dvs1a;
        }

        goto dvs17;

    dvs1d:
        // dvs1d — negative quotient: the same two fetches, then the other pair of exits.
        flag = (_isr & FlagN) != 0;
        at = (at & 0xFFFF) | ((at & 0xFFFF) << 16);
        AluAnd16(_alub, 0xFFFF);
        SrNzvc();
        await IdleAsync();
        if (flag)
        {
            goto dvs1e;
        }

        goto dvs1f;

    dvs17:
        // dvs17 — positive quotient, positive dividend: no complement, the result is final. The `if` is
        // on the *remainder's* sign because a zero remainder or a zero quotient takes the other door.
        flag = (_isr & FlagN) != 0;
        at = Set16L(at, (ushort)(_aluo));
        AluAnd16(_aluo, 0xFFFF);
        SrNzvc();
        if (flag)
        {
            goto dvuma;
        }

        goto leaa2;

    dvs1a:
        // dvs1a / dvs1b — positive quotient, negative dividend: the remainder must be complemented, and
        // a zero remainder is left alone (Z or N sends it to the overflow-checked exit instead).
        AluSub16(_alub, 0);
        SrNzvc();
        await IdleAsync();

        // dvs1b
        flag = (_isr & (FlagZ | FlagN)) != 0;
        _alub = _aluo;
        at = Set16L(at, (ushort)(_aluo));
        AluSub16(High16(at), 0);
        await IdleAsync();
        if (flag)
        {
            goto dvs1c;
        }

        goto dvum4;

    dvs1f:
        // dvs1f / dvs20 — negative quotient, positive dividend: the quotient's magnitude is final, only
        // the remainder's sign is still to be decided.
        AluSub16(_alub, 0);
        SrNzvc();
        await IdleAsync();

        // dvs20
        flag = (_isr & (FlagZ | FlagN)) != 0;
        at = Set16L(at, (ushort)(_aluo));
        AluAnd16(_aluo, 0xFFFF);
        SrNzvc();
        if (flag)
        {
            goto leaa2;
        }

        goto dvuma;

    dvs1e:
        // dvs1e — negative quotient, negative dividend: both complement, so the quotient is the
        // magnitude and the remainder keeps the dividend's (negative) sign.
        flag = (_isr & FlagN) != 0;
        _alub = _aluo;
        at = Set16L(at, (ushort)(_aluo));
        AluSub16(High16(at), 0);
        await IdleAsync();
        if (flag)
        {
            goto dvum4;
        }

        goto dvs1c;

    dvs1c:
        // dvs1c — the remainder's complement into the high word (the low word already holds the
        // quotient), then leaa2.
        at = (at & 0x0000FFFF) | (_aluo << 16);
        AluAnd16(_alub, 0xFFFF);
        SrNzvc();
        goto leaa2;

    dvum4:
    dvuma:
        // dvum4 / dvuma — the overflow exit, shared with DIVU: N and V (alu_over), Z and C cleared, the
        // register untouched, then the closing read. Every path to it costs the same two clocks, which is
        // why the corpus's shortest DIVS records sit just above their shortest DIVU ones.
        AluOver();
        SrNzvc();
        await TrailingFetchAsync();
        return;

    leaa2:
        // leaa2 — the working register read whole is the result: quotient in the low word, remainder in
        // the high, written in one go (which is why DIVS's destination is never half-written the way
        // DIVU's is), then the closing read.
        _d[rx] = at;
        await TrailingFetchAsync();
    }

    /// <summary>
    /// CHK — <c>chkm1</c>/<c>chkr1</c>, <c>chkr2</c>, then <c>chkr3</c> or the trap (vector 6), then
    /// <c>chkr4</c>'s closing read. `CHK <ea>,Dn` traps when the signed value in Dn is outside
    /// 0 … bound — and the two tests that decide it are the whole instruction.
    /// </summary>
    /// <remarks>
    /// <para>The first test is one signed subtract, <c>bound − value</c>: N or V means the value is out of
    /// range — N because the result went negative (the value is above the bound), V because the true
    /// difference did not fit in sixteen bits (a value far below zero against a large bound). Those two
    /// flags do not catch every negative value (a slightly negative one against a small bound subtracts
    /// cleanly to a positive result), and that is exactly why there is a second test: <c>chkr2</c> reads
    /// the value's own sign, and a negative value traps there. So the two comparisons partition the
    /// range, and the flags the instruction *leaves* are the second one's — <b>N and Z from the value,
    /// V and C cleared</b>, X untouched.</para>
    /// <para>Six clocks clean and four on the trap path: <c>chkr2</c> branches straight to <c>trap1</c>,
    /// so the trap never runs <c>chkr3</c>. Charging three states on both paths made 924 records two
    /// clocks long, and the old table's flat six was the same error from the other side.</para>
    /// <para><b>And this family has no loop, so `B1` has nothing to convert here.</b> The listing is
    /// <c>chkr1</c>, <c>chkr2</c>, then <c>chkr3</c> (clean) or <c>trap1</c> — four states and no
    /// counter. Its adder traffic is a net-zero transfer pair: <c>chkr1</c> steps it down one word
    /// (<c>m_au = m_au - 2</c>) and <c>chkr2</c> takes the fetch-state copy and steps it back
    /// (<c>m_pc = m_au; m_au = m_au + 2</c>), which is the counter the trap's frame is built from.
    /// Our script expresses no adder arithmetic at all and the frame's counter is <c>_pc</c>'s, which
    /// the corpus measures directly: 2 500/2 500 state and exact-clock, with **both** paths present —
    /// 1 447 records carry vector 6 (the trap) and 1 053 vector 3 (an address error on a memory
    /// operand), so CHK's trap path is <em>not</em> latent, unlike the trace exception's.</para>
    /// </remarks>
    async ValueTask CheckAsync(ushort op)
    {
        int rx = (op >> 9) & 0x7;
        int mode = FtuMode;
        int reg = FtuRegister;

        FrameSavesFirstExtensionWord();

        uint? operand = await ReadWordSourceAsync(mode, reg);
        if (operand is null)
        {
            return;
        }

        uint value = _d[rx] & 0xFFFF;

        // The script, in the listing's order (`microcode-walk.py --op 4180` — the `Dn` source form; every
        // other form's bound is the addressing class's read above, `ReadWordSourceAsync`).
        //
        //   ours: no adder arithmetic, which is `chkr1`'s `m_au = m_au - 2` and `chkr2`'s `m_pc = m_au;
        //   m_au = m_au + 2` — a net-zero transfer pair, and the trap's frame counter is `_pc`'s. The
        //   corpus's CHK file measures the pair directly: 2 500/2 500 state and exact-clock, both paths
        //   present (1 447 records vector 6, 1 053 vector 3).
        //
        // chkr1 (152)  m_alub = m_da[rx]; m_au = m_au - 2; alu_sub(m_da[rx], m_da[ry]); sr_nzvc()     2
        _alub = value;
        AluSub16(value, operand.Value & 0xFFFF);
        SrNzvc();
        await IdleAsync();

        // chkr2 (153)  m_t = !(m_isr & (SR_V|SR_N)); m_pc = m_au; m_au = m_au + 2;
        //              alu_and(m_alub, 0xffff); sr_nzvc()                                             2
        bool outOfRange = (_isr & (FlagV | FlagN)) != 0;
        AluAnd16(_alub, 0xFFFF);
        SrNzvc();
        await IdleAsync();

        //   ours: the trap path is the exception entry's code, not this method's — `EnterExceptionAsync(6, 0)`,
        //   which `chkr2`'s branch below and `chkr3`'s sign test both call — so its states are pasted once
        //   here, in the listing's order, rather than expanded by CHK. The entry closes on the handler's
        //   fetch, the pair `b`/`mmrw3` (`RefillAsync`'s, named once at `CloseInstructionAsync`), and
        //   `chkr4` below is the in-range path's own `b` — the same four statements, uncharged — whose
        //   read is that same pair.
        //
        // trap1 (1d0)  m_ftu = m_sr; m_au = m_pc - 2                                                  2
        // trap2 (347)  m_sr = (m_sr & ~SR_T) | SR_S; update_user_super(); update_interrupt();
        //              m_au = m_da[16] - 2; alu_and(m_pc, 0xffff)                                     2
        // trap3 (3c2)  m_aob = m_au; m_dbout = m_aluo; m_au = m_au - 4; alu_and(m_ftu, 0xffff);
        //              write m_dbout at m_aob                                                         4
        // trap4 (360)  m_aob = m_au; m_dbout = m_aluo; debugger_exception_hook(0x06);
        //              m_da[16] = m_au; m_ftu = 0x0018; m_au = m_au + 2;
        //              alu_and(high16(m_pc), 0xffff); write m_dbout at m_aob                          4
        // trap5 (0ef)  m_aob = m_au; m_dbout = m_aluo; m_at = ext32(m_ftu); m_au = m_au - 4;
        //              alu_and(m_alub, 0xffff); write m_dbout at m_aob                                4
        // trap6 (367)  m_aob = m_at; m_pc = m_at; m_au = m_at + 2; read at m_aob                      4
        // trap7 (11a)  m_aob = m_au; set_16h(m_at, m_dbin); m_au = m_au + 2; read at m_aob            4
        // trap8 (2b7)  m_aob = merge_16_32(high16(m_at), m_dbin);
        //              m_au = merge_16_32(high16(m_at), m_dbin) + 2; read at m_aob                    4
        // trap9 (11c)  (no statement of its own: the `n 2` the vector's read is followed by)          2
        // b (363) + mmrw3 (34c)  m_aob = m_au; m_ir = m_irc; m_pc = m_au; m_au = m_au + 2;
        //              m_ird = m_ir; read at m_aob                                                  0+4
        if (outOfRange)
        {
            await EnterExceptionAsync(6, 0);                  // chkr2 branches straight to the trap
            return;
        }

        // chkr3 (110)  m_t = m_isr & SR_N                                                             2
        bool negative = (_isr & FlagN) != 0;
        await IdleAsync();
        if (negative)
        {
            await EnterExceptionAsync(6, 0);
            return;
        }

        // chkr4 (150)  m_aob = m_au; m_ir = m_irc; m_pc = m_au; m_au = m_au + 2                       0
        await TrailingFetchAsync();                           // chkr4 — the closing read
    }
}
