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
            || (op & 0xF138) == 0xB108)                         // CMPM
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
    /// The <b>control transfers</b> — `BSR` (line 6's condition field 1, not the conditional branches),
    /// `JMP` and `JSR` (line 4's two opcode ranges) and `UNLK`. Their scripts are in this file;
    /// `M68000.Timing.cs` charges nothing for them.
    /// </summary>
    static bool IsScriptedBranch(ushort op) => (op >> 12) == 0x6 && ((op >> 8) & 0xF) == 1;

    static bool IsScriptedJump(ushort op) => (op & 0xFFC0) is 0x4EC0 or 0x4ED0;

    static bool IsScriptedJsr(ushort op) => (op & 0xFFC0) == 0x4E80;

    static bool IsScriptedUnlink(ushort op) => (op & 0xFFF8) == 0x4E58;

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
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;

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
        _alub = await ReadWordAsync(address);              // adrl1 — m_dbin -> m_alub, m_alue
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        _dbin = await ReadWordAsync(address + 2);          // adrl2 — m_dbin
    }

    /// <summary>
    /// adsl2, adsl3 as the PC-relative shapes read them — the high word with the instruction stream's
    /// bus status (<see cref="ReadOperandProgramAsync"/>), the low one as a data read.
    /// </summary>
    async ValueTask ReadLongOperandFromStreamAsync(uint address)
    {
        _alub = await ReadOperandProgramAsync(address);    // adsl2 — SSW_PROGRAM | SSW_R
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

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
            await ReadLongOperandFromStreamAsync(_pc - 2 + Ext32(displacement));
            return !_busFaulted;
        }

        // dpci — aixw0 (2 clocks), then the refill, adsl2, adsl3
        await IdleAsync();
        ushort extension = await FetchAsync();
        if (_busFaulted) return false;
        await ReadLongOperandFromStreamAsync(
            _pc - 2 + (uint)ExtensionDisplacement(extension) + IndexValue(extension));
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
        await TrailingFetchAsync();                        // morl1 — m_aob = m_au, the one read
        if (_busFaulted) return;

        uint destination = Merge16_32(_alub, _dbin);

        AluLongOp(source, destination, kind);              // morl1/morl2 — the low half, then the carry
        SetLongFlags(kind, source, destination, _aluo, extend: kind is AluLong.Add or AluLong.Sub);
        uint result = _aluo;

        await WriteWordAsync(address + 2, (ushort)Low16(result));   // morl2 — m_aob = m_at + 2
        if (_busFaulted) return;
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
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;

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
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;

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

        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
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

        await IdleAsync();                                 // bsrw1 — `alu r=0 c=1`, the transfer's own 2

        uint returnAddress = _pc;                          // the latch: bsrw1's `m_at = m_au`
        uint stack = A7 - 4;
        A7 = stack;

        await WriteWordAsync(stack, (ushort)High16(returnAddress));      // bsrw2 — high word first
        if (_busFaulted) return;
        await WriteWordAsync(stack + 2, (ushort)Low16(returnAddress));   // bsrw3
        if (_busFaulted) return;

        long target = (long)_opcodeAddress + 2 + displacement;
        FrameSavesOnFetch((uint)target);                   // `b`: `m_pc = m_au` = the target
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
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        long target;

        switch (mode)
        {
            case 2:                                            // jmp_an — jmpa1: a read at the register
                target = GetA(reg);
                break;

            case 5:                                            // jmp_das — jmpd1
                await IdleAsync();
                target = GetA(reg) + (uint)(short)await JumpWordAsync(0);
                if (_busFaulted) return;
                break;

            case 6:                                            // jmp_dais — jmpx0, jmpx1, jmpx2/jmpx3
            {
                await IdleAsync(2);                            // jmpx0 — `alu_ext` of the brief extension word
                ushort extension = await JumpWordAsync(0);
                await IdleAsync(4);                            // jmpx1 and the index state
                target = GetA(reg) + (uint)ExtensionDisplacement(extension) + IndexValue(extension);
                break;
            }

            case 7 when reg == 0:                              // jmp_adr16 — jmaw1
                await IdleAsync();
                target = (uint)(short)await JumpWordAsync(0);
                break;

            case 7 when reg == 1:                              // jmp_adr32 — jmal1, jmal2
            {
                // no idle run at all: `jmal1` merges the queue's word with the one read at the fetch pointer,
                // and `jmal2` reads at the address it built (12 clocks, three reads, nothing between them)
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
        _fetchStateAddress = address + 2;
        _ir = await ReadProgramAsync(address + 2);
        if (_busFaulted) return;
        _ircValid = false;
        _pc = address;
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
    /// (`FrameSavesOnFetch(_pc)`), the stack untouched and nothing pushed (FINDINGS row 43). The pushes
    /// are the stacked copy of that counter, so both are `_pc` at one instant.
    /// </para>
    /// </summary>
    async ValueTask JumpSubroutineAsync(ushort op)
    {
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        long target;

        switch (mode)
        {
            case 2:                                            // jsr_ais — jsra1, no idle run
                target = GetA(reg);
                break;

            case 5:                                            // jsr_das — jsrd1
                await IdleAsync();
                target = GetA(reg) + (uint)(short)await JumpWordAsync(0);
                if (_busFaulted) return;
                break;

            case 6:                                            // jsr_dais — jsrx0, jsrx1, jsrx2/jsrx3
            {
                await IdleAsync(2);                            // jsrx0 — `alu_ext` of the brief extension word
                ushort extension = await JumpWordAsync(0);
                await IdleAsync(4);                            // jsrx1 and the index state
                target = GetA(reg) + (uint)ExtensionDisplacement(extension) + IndexValue(extension);
                break;
            }

            case 7 when reg == 0:                              // jsr_adr16 — one idle state
                await IdleAsync();
                target = (uint)(short)await JumpWordAsync(0);
                break;

            case 7 when reg == 1:                              // jsr_adr32 — jsal1, jsal2, no idle run
            {
                uint high = await JumpWordAsync(0);            // jsal1 — the word the queue holds
                uint low = await JumpWordAsync(1);             // jsal2 — one read at the fetch pointer
                if (_busFaulted) return;

                // jsal2 also commits `m_pc = m_au`, and that read moved `m_au` by 2 — so this shape's
                // counter is the only one that a queue read *does* advance (or the return address and
                // the frame are both a word low: 90 records)
                _pc += 2;
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
        FrameSavesOnFetch(_pc);
        ushort first = await ReadTargetAsync(address);
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        // jsaw2, jsaw3 — the return address's two words, high first, the stack pointer committed with
        // the first one (as `m_da[m_sp] = m_au` does)
        uint returnAddress = _pc;
        uint stack = A7 - 4;
        A7 = stack;
        await WriteWordAsync(stack, (ushort)High16(returnAddress));
        if (_busFaulted) return;
        await WriteWordAsync(stack + 2, (ushort)Low16(returnAddress));
        if (_busFaulted) return;

        // b + mmrw3 — `m_pc = m_au` at the target's successor, `m_ird = m_ir` (the target's own word)
        _flowTransferred = true;
        _ird = first;
        _fetchStateAddress = address + 2;
        _ir = await ReadProgramAsync(address + 2);
        if (_busFaulted) return;
        _ircValid = false;
        _pc = address;
    }

    /// <summary>
    /// `UNLK An` — unlk1, unlk2, unlk3, unlk4. The two words are read at the stack pointer the register
    /// names, the new stack pointer is committed to `m_da[m_sp]` **after** both are in (an odd `An`
    /// therefore enters the address error with the stack pointer untouched — 1 115 records, whose first
    /// differing field was `ssp`/`usp`), and the instruction's one trailing fetch is `unlk4`'s read at
    /// `m_pc`. That `m_pc = m_au` of unlk1 is also the frame's saved counter for an odd `An`:
    /// `opcode + 4`, the boundary's word, not the word before it.
    /// </summary>
    async ValueTask UnlinkAsync(ushort op)
    {
        int reg = op & 0x7;

        BeginFetchState();                                 // unlk1 — `m_pc = m_au`
        uint address = GetA(reg);                          // m_at = m_da[ry]; m_aob = m_da[ry]
        uint value = await ReadLongAsync(address);         // unlk1 then unlk2, at An and An + 2
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        A7 = address + 4;                                  // unlk3 — m_da[m_sp] = m_au
        SetA(reg, value);                                  // unlk4 — merge_16_32 of the two words
    }
}
