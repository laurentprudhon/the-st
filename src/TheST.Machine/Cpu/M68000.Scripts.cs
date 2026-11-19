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
/// The families are ported one at a time and the table beside the old handlers is the port's own
/// ledger: a family is scripted when it has a method here and its entry in <c>M68000.Timing.cs</c> is
/// gone, because a script carries its own clocks. <b>OR.l is the first</b> — 18 shapes, 2 500 records,
/// the whole of the commit-order and saved-<c>pc</c> residue for that file.
/// </para>
/// </summary>
public sealed partial class M68000
{
    /// <summary>
    /// The families whose clocks are carried by a script instead of <c>M68000.Timing.cs</c>'s table.
    /// This predicate is the ledger: a family is added here when its script lands, and its row in the
    /// table is deleted — one place to read what is ported and what is still measured by shape.
    /// </summary>
    static bool IsScriptedOrLong(ushort op) =>
        (op >> 12) == 0x8 && ((op >> 6) & 0x3) == 2 && (((op >> 6) & 0x7) == 2 || ((op >> 6) & 0x7) == 6);

    // =========================================================================================
    // OR.l — `or.l <ea>, Dn` (opmode 2) and `or.l Dn, <ea>` (opmode 6)
    // =========================================================================================

    /// <summary>
    /// OR.l, all 18 shapes. The opmode picks the direction; the mode/reg pair picks the script, and the
    /// scripts differ only in how the operand's address is built and read.
    /// </summary>
    async ValueTask OrLongAsync(ushort op)
    {
        int rx = (op >> 9) & 0x7;
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;

        if (((op >> 6) & 0x7) == 2)
        {
            await OrLongFromEaAsync(mode, reg, rx);       // or_l_<shape>_dd
        }
        else
        {
            await OrLongIntoEaAsync(mode, reg, rx);       // or_l_dd_<shape>
        }
    }

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
        if (_busFaulted) return;
        _dbin = await ReadWordAsync(address + 2);          // adrl2 — m_dbin
    }

    /// <summary>
    /// adsl2, adsl3 as the PC-relative shapes read them — the high word with the instruction stream's
    /// bus status (<see cref="ReadOperandProgramAsync"/>), the low one as a data read.
    /// </summary>
    async ValueTask ReadLongOperandFromStreamAsync(uint address)
    {
        _alub = await ReadOperandProgramAsync(address);    // adsl2 — SSW_PROGRAM | SSW_R
        if (_busFaulted) return;
        _dbin = await ReadWordAsync(address + 2);          // adsl3 — SSW_DATA | SSW_R
    }

    /// <summary>
    /// The source operand of `or.l <ea>, Dn`: the EA's own states (the script's prefix), which differ
    /// per shape in their reads and in where the idle run sits.
    /// </summary>
    async ValueTask OrLongFromEaAsync(int mode, int reg, int rx)
    {
        switch (mode)
        {
            case 0:
                // or_l_ds_dd — rorl1/rorl2/25e/25d: no bus at all before the closing prefetch
                _alub = High16(_d[reg]);
                _dbin = (ushort)_d[reg];
                await OrIntoDataRegisterAsync(rx, clocks: 4);
                return;

            case 2:
                // or_l_ais_dd — adrl1, adrl2, roml1/roml2, 25e, 25d
                await ReadLongOperandAsync(GetA(reg));
                if (_busFaulted) return;
                await OrIntoDataRegisterAsync(rx, clocks: 2);
                return;

            case 3:
                // or_l_aips_dd — pinl1, pinl2, pinl3 (the commit), roml1/roml2, 25e, 25d. The
                // increment commits *after* the second word's read: a word pair that faults leaves
                // the register where it was (pinl3 is never reached)
                await ReadLongOperandAsync(GetA(reg));
                if (_busFaulted) return;
                SetA(reg, GetA(reg) + 4);                  // pinl3 — m_da[ry] = m_au
                await OrIntoDataRegisterAsync(rx, clocks: 2);
                return;

            case 4:
            {
                // or_l_pais_dd — pdcl1 (2 clocks), pdcl2 (the decrement commits), adrl2
                uint address = GetA(reg) - 4;              // pdcl1 — m_au = m_da[ry] - 4
                await IdleAsync();                         // pdcl1's own 2 clocks
                SetA(reg, address);                        // pdcl2 — m_da[ry] = m_au
                await ReadLongOperandAsync(address);
                if (_busFaulted) return;
                await OrIntoDataRegisterAsync(rx, clocks: 2);
                return;
            }

            case 5:
            {
                // or_l_das_dd — adsl1 (the displacement out of the queue), adsl2, adsl3
                ushort displacement = await FetchAsync();
                if (_busFaulted) return;
                await ReadLongOperandAsync(GetA(reg) + Ext32(displacement));
                if (_busFaulted) return;
                await OrIntoDataRegisterAsync(rx, clocks: 2);
                return;
            }

            case 6:
            {
                // or_l_dais_dd — aixw0 (2 clocks), aixw1/aixw2 (the refill), adsl2, adsl3
                await IdleAsync();                         // aixw0 — alu_ext, its own 2 clocks
                ushort extension = await FetchAsync();
                if (_busFaulted) return;
                uint address = GetA(reg) + (uint)ExtensionDisplacement(extension) + IndexValue(extension);
                await ReadLongOperandAsync(address);
                if (_busFaulted) return;
                await OrIntoDataRegisterAsync(rx, clocks: 2);
                return;
            }

            case 7:
                switch (reg)
                {
                    case 0:
                    {
                        // or_l_adr16_dd — abww1 (the address out of the queue, m_pc = m_au), ablw3, adrl2
                        BeginFetchState();
                        ushort absolute = await FetchAsync();
                        if (_busFaulted) return;
                        await ReadLongOperandAsync(Ext32(absolute));
                        if (_busFaulted) return;
                        await OrIntoDataRegisterAsync(rx, clocks: 2);
                        return;
                    }

                    case 1:
                    {
                        // or_l_adr32_dd — ablw1, ablw2 (two words of address, m_pc = m_au in the
                        // second), ablw3, adrl2
                        ushort high = await FetchAsync();
                        if (_busFaulted) return;
                        BeginFetchState();
                        ushort low = await FetchAsync();
                        if (_busFaulted) return;
                        uint address = Merge16_32(high, low);
                        await ReadLongOperandAsync(address);
                        if (_busFaulted) return;
                        await OrIntoDataRegisterAsync(rx, clocks: 2);
                        return;
                    }

                    case 2:
                    {
                        // or_l_dpc_dd — adsl1, adsl2 (program-space read), adsl3, with the
                        // displacement based on the extension word's own address
                        ushort displacement = await FetchAsync();
                        if (_busFaulted) return;
                        await ReadLongOperandFromStreamAsync(_pc - 2 + Ext32(displacement));
                        if (_busFaulted) return;
                        await OrIntoDataRegisterAsync(rx, clocks: 2);
                        return;
                    }

                    case 3:
                    {
                        // or_l_dpci_dd — aixw0 (2 clocks), the refill, adsl2, adsl3
                        await IdleAsync();                 // aixw0
                        ushort extension = await FetchAsync();
                        if (_busFaulted) return;
                        uint address = _pc - 2 + (uint)ExtensionDisplacement(extension) + IndexValue(extension);
                        await ReadLongOperandFromStreamAsync(address);
                        if (_busFaulted) return;
                        await OrIntoDataRegisterAsync(rx, clocks: 2);
                        return;
                    }

                    case 4:
                    {
                        // or_l_imm32_dd — e#l1, e#w1 (the immediate's two words), rorl1/rorl2, 25e, 25d
                        _alub = await FetchAsync();
                        if (_busFaulted) return;
                        _dbin = await FetchAsync();
                        if (_busFaulted) return;
                        await OrIntoDataRegisterAsync(rx, clocks: 4);
                        return;
                    }
                }

                return;
        }
    }

    /// <summary>
    /// roml1, roml2, 25e, 25d — OR the operand read into <c>m_alub</c>/<c>m_dbin</c> into Dn, word by
    /// word: the low half first, the instruction's own prefetch between the halves, then the high half
    /// with Z only ever cleared. <paramref name="clocks"/> is the shape's closing idle run — 4 for a
    /// register or immediate source (25e and 25d both miss the bus), 2 otherwise.
    /// </summary>
    async ValueTask OrIntoDataRegisterAsync(int rx, int clocks)
    {
        _aluWidth = 1;
        AluOr(_d[rx], _dbin);                              // roml1 — alu_or(Dn, m_dbin)
        SetNzvc();                                         //        sr_nzvc
        Set16L(rx, _aluo);                                 // 25c — set_16l

        await TrailingFetchAsync();                        // roml2 — the one read of the instruction
        if (_busFaulted) return;

        AluOr(High16(_d[rx]), _alub);                      // 25e — alu_or(high16(Dn), m_alub)
        SetNzvcU();                                        //        sr_nzvc_u
        Set16H(rx, _aluo);                                 // 25d — set_16h

        await IdleAsync(clocks);                           // the shape's closing idle clocks
    }

    /// <summary>
    /// The memory-destination scripts of `or.l Dn, <ea>`: the operand's read block, then the shared
    /// tail (morl1, morl2, morw2).
    /// </summary>
    async ValueTask OrLongIntoEaAsync(int mode, int reg, int rx)
    {
        switch (mode)
        {
            case 2:
                // or_l_dd_ais — adrl1, adrl2, morl1, morl2, morw2
                await OrIntoMemoryAsync(mode, reg, rx);
                return;

            case 3:
                // or_l_dd_aips — pinl1, pinl2, pinl3, morl1, morl2, morw2
                await OrIntoMemoryAsync(mode, reg, rx);
                return;

            case 4:
                // or_l_dd_pais — pdcl1 (2 clocks), pdcl2, adrl2, morl1, morl2, morw2
                await IdleAsync();                         // pdcl1's own 2 clocks
                await OrIntoMemoryAsync(mode, reg, rx);
                return;

            case 5:
                // or_l_dd_das — adsl1, adsl2, adsl3, morl1, morl2, morw2
                await OrIntoMemoryAsync(mode, reg, rx);
                return;

            case 6:
                // or_l_dd_dais — aixw0 (2 clocks), the refill, adsl2, adsl3, morl1, morl2, morw2
                await IdleAsync();                         // aixw0's own 2 clocks
                await OrIntoMemoryAsync(mode, reg, rx);
                return;

            case 7:
                // or_l_dd_adr16 / or_l_dd_adr32 — abww1 or ablw1+ablw2, ablw3, adrl2, then the tail
                await OrIntoMemoryAsync(mode, reg, rx);
                return;
        }
    }

    /// <summary>
    /// The destination's address and its two words, then the RMW tail. The address is built by the
    /// same state blocks the source side uses; the predecrement's decrement and the incremented
    /// register's commit are the only shape-specific writes, and they stay where the script puts them.
    /// </summary>
    async ValueTask OrIntoMemoryAsync(int mode, int reg, int rx)
    {
        uint address;

        switch (mode)
        {
            case 2:
                address = GetA(reg);                       // adrl1 — m_aob = m_da[ry]
                break;

            case 3:
                address = GetA(reg);                       // pinl1
                break;

            case 4:
                address = GetA(reg) - 4;                   // pdcl1 — m_au = m_da[ry] - 4
                break;

            case 5:
            {
                ushort displacement = await FetchAsync();  // adsl1
                if (_busFaulted) return;
                address = GetA(reg) + Ext32(displacement);
                break;
            }

            case 6:
            {
                ushort extension = await FetchAsync();     // aixw1/aixw2
                if (_busFaulted) return;
                address = GetA(reg) + (uint)ExtensionDisplacement(extension) + IndexValue(extension);
                break;
            }

            case 7:
            {
                if (reg == 0)
                {
                    BeginFetchState();                     // abww1 — m_pc = m_au
                    ushort absolute = await FetchAsync();
                    if (_busFaulted) return;
                    address = Ext32(absolute);
                }
                else
                {
                    ushort high = await FetchAsync();      // ablw1 — no m_pc of its own
                    if (_busFaulted) return;
                    BeginFetchState();                     // ablw2 — m_pc = m_au
                    ushort low = await FetchAsync();
                    if (_busFaulted) return;
                    address = Merge16_32(high, low);
                }

                break;
            }

            default:
                return;
        }

        if (mode == 4)
        {
            SetA(reg, address);                            // pdcl2 — m_da[ry] = m_au
        }

        _alub = await ReadWordAsync(address);              // adrl1 / pinl1 — the high word
        if (_busFaulted) return;
        _dbin = await ReadWordAsync(address + 2);          // adrl2 / pinl2 — the low word
        if (_busFaulted) return;

        if (mode == 3)
        {
            SetA(reg, address + 4);                        // pinl3 — the increment commits, after the words
        }

        await OrIntoMemoryLongAsync(rx, address);
    }

    /// <summary>
    /// morl1, morl2, morw2 — the destination's words ORed with Dn and written back. The prefetch is
    /// <b>before</b> the writes (that is what a faulting or an interrupted instruction's frame saves),
    /// and the low word goes out first: the chip's ALU is sixteen bits wide, so a long write is two
    /// word cycles, `m_au - 2` in between.
    /// </summary>
    async ValueTask OrIntoMemoryLongAsync(int rx, uint address)
    {
        await TrailingFetchAsync();                        // morl1 — m_aob = m_au, the one read
        if (_busFaulted) return;

        _aluWidth = 1;
        AluOr(_d[rx], _dbin);                              // morl1 — the low half
        SetNzvc();
        ushort low = (ushort)_aluo;

        AluOr(High16(_d[rx]), _alub);                      // morl2 — the high half
        SetNzvcU();
        ushort high = (ushort)_aluo;

        await WriteWordAsync(address + 2, low);            // morl2 — m_aob = m_au (= m_at + 2)
        if (_busFaulted) return;
        await WriteWordAsync(address, high);               // morw2 — m_au -= 2, the high word
    }
}
