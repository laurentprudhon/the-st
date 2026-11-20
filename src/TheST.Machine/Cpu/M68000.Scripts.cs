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

        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;

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

        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;

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
}
