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

            await AluIntoMemoryLongAsync(rx, address.Value, kind);
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
    async ValueTask AluIntoMemoryLongAsync(int rx, uint address, AluLong kind)
    {
        await TrailingFetchAsync();                        // morl1 — m_aob = m_au, the one read
        if (_busFaulted) return;

        uint source = _d[rx];
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

        AluLongOp(source.Value, destination, AluLong.Sub); // cpml1/cpml2 — alu_sub + alu_subc
        SetLongFlags(AluLong.Sub, source.Value, destination, _aluo, extend: false);

        await TrailingFetchAsync();                        // cpml2's read
        if (_busFaulted) return;

        await IdleAsync();                                 // 08d rcal3
    }
}
