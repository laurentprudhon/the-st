namespace TheST.Machine.Cpu;

/// <summary>
/// The instruction set. One <c>switch</c> per opcode line, the effective-address machine underneath it,
/// and a bus cycle at every access — so an instruction's cost is whichever answer instants its accesses
/// land on, never a table.
/// </summary>
public sealed partial class M68000
{
    static int SizeBytes(int size) => size == 0 ? 1 : size == 1 ? 2 : 4;

    async ValueTask ExecuteAsync(ushort op)
    {
        switch (op >> 12)
        {
            case 0x0: await Line0Async(op); break;
            case 0x1: await MoveAsync(op, 0); break;
            case 0x2: await MoveAsync(op, 2); break;
            case 0x3: await MoveAsync(op, 1); break;
            case 0x4: await Line4Async(op); break;
            case 0x5: await Line5Async(op); break;
            case 0x6: await Line6Async(op); break;
            case 0x7: await MoveQAsync(op); break;
            case 0x8: await Line8Async(op); break;
            case 0x9: await Line9Async(op); break;
            case 0xA: await EnterExceptionAsync(10, 0); break;
            case 0xB: await LineBAsync(op); break;
            case 0xC: await LineCAsync(op); break;
            case 0xD: await LineDAsync(op); break;
            case 0xE: await LineEAsync(op); break;
            default: await EnterExceptionAsync(11, 0); break;
        }
    }

    // ---- effective addressing -------------------------------------------------------------

    async ValueTask<uint> EaAddressAsync(int mode, int reg, int size)
    {
        switch (mode)
        {
            case 2:
                return GetA(reg);

            case 3:
            {
                uint address = GetA(reg);
                SetA(reg, address + (uint)(size == 0 && reg == 7 ? 2 : SizeBytes(size)));
                return address;
            }

            case 4:
            {
                uint delta = (uint)(size == 0 && reg == 7 ? 2 : SizeBytes(size));
                uint address = GetA(reg) - delta;
                SetA(reg, address);
                return address;
            }

            case 5:
            {
                short displacement = (short)await FetchAsync();
                return GetA(reg) + (uint)displacement;
            }

            case 6:
            {
                ushort extension = await FetchAsync();
                return GetA(reg) + (uint)ExtensionDisplacement(extension);
            }

            case 7:
                switch (reg)
                {
                    case 0: return (uint)(short)await FetchAsync();
                    case 1: return (await FetchLongAsync()) & 0x00FFFFFF;
                    case 2:
                    {
                        short displacement = (short)await FetchAsync();
                        return _pc - 2 + (uint)displacement;
                    }

                    case 3:
                    {
                        ushort extension = await FetchAsync();
                        return _pc - 2 + (uint)ExtensionDisplacement(extension);
                    }
                }

                break;
        }

        return 0;   // register-direct or immediate: no address
    }

    /// <summary>The 8-bit displacement of an indexed effective address, sign-extended.</summary>
    static int ExtensionDisplacement(ushort extension)
    {
        int displacement = (int)(extension & 0xFF);
        return displacement >= 0x80 ? displacement - 0x100 : displacement;
    }

    uint IndexValue(ushort extension)
    {
        int indexRegister = (extension >> 12) & 0x7;
        bool addressRegister = (extension & 0x8000) != 0;
        uint value = addressRegister ? GetA(indexRegister) : _d[indexRegister];
        if ((extension & 0x0800) == 0)
        {
            value = (uint)(short)(value & 0xFFFF);   // word index, sign-extended
        }

        return value;
    }

    async ValueTask<uint> ReadEaAsync(int mode, int reg, int size)
    {
        switch (mode)
        {
            case 0:
                return _d[reg] & SizeMask(size);

            case 1:
                return size == 1 ? (uint)(short)(GetA(reg) & 0xFFFF) : GetA(reg);

            case 7 when reg == 4:
                return await ReadImmediateAsync(size);
        }

        uint address = await EaAddressAsync(mode, reg, size);
        if (_busFaulted) return 0;

        if (size == 0) return await ReadByteAsync(address);
        if (size == 1) return await ReadWordAsync(address);
        return await ReadLongAsync(address);
    }

    async ValueTask<uint> ReadImmediateAsync(int size)
    {
        if (size == 2)
        {
            return await FetchLongAsync();
        }

        uint word = await FetchAsync();
        return size == 0 ? (word & 0xFF) : word;
    }

    async ValueTask WriteEaAsync(int mode, int reg, int size, uint value)
    {
        switch (mode)
        {
            case 0:
            {
                uint mask = SizeMask(size);
                int shift = size == 0 ? 0 : size == 1 ? 0 : 0;
                _d[reg] = (_d[reg] & ~mask) | (value & mask);
                _ = shift;
                return;
            }

            case 1:
                SetA(reg, size == 1 ? (uint)(short)(value & 0xFFFF) : value);
                return;
        }

        uint address = await EaAddressAsync(mode, reg, size);
        if (_busFaulted) return;

        if (size == 0) await WriteByteAsync(address, (byte)value);
        else if (size == 1) await WriteWordAsync(address, (ushort)value);
        else await WriteLongAsync(address, value);
    }

    // ---- MOVE ------------------------------------------------------------------------------

    async ValueTask MoveAsync(ushort op, int size)
    {
        int sourceReg = op & 0x7;
        int sourceMode = (op >> 3) & 0x7;
        int destinationMode = (op >> 6) & 0x7;
        int destinationReg = (op >> 9) & 0x7;

        uint value = await ReadEaAsync(sourceMode, sourceReg, size);
        if (_busFaulted) return;

        await WriteEaAsync(destinationMode, destinationReg, size, value);
        if (_busFaulted) return;

        if (destinationMode != 1)
        {
            SetLogicFlags(value, size);
        }

        Clocks(1);
    }

    async ValueTask MoveQAsync(ushort op)
    {
        int data = op & 0xFF;
        if (data >= 0x80) data -= 0x100;
        _d[(op >> 9) & 0x7] = (uint)data;
        SetLogicFlags((uint)data, 2);
        Clocks(1);
    }

    // ---- line 0: immediate and bit operations ---------------------------------------------

    async ValueTask Line0Async(ushort op)
    {
        int size = (op >> 6) & 0x3;
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        int operation = (op >> 9) & 0x7;

        if (size == 3 && operation == 0 && mode == 7 && reg == 4)
        {
            await ImmediateToCcrOrSrAsync(op);
            return;
        }

        if (operation == 4)
        {
            await BitOperationAsync(op, size, mode, reg);
            return;
        }

        uint immediate = await ReadImmediateAsync(size);
        if (_busFaulted) return;

        if (mode == 0)
        {
            uint mask = SizeMask(size);
            uint old = _d[reg] & mask;
            uint result = operation switch
            {
                0 => old | immediate,          // ORI
                1 => old & immediate,          // ANDI
                2 => SubtractValue(old, immediate, size),  // SUBI
                3 => AddValue(old, immediate, size),       // ADDI
                5 => old ^ immediate,          // EORI
                _ => old,                      // CMPI
            };

            if (operation == 6)
            {
                SetSubFlags(immediate, old, old - immediate, size);
                return;
            }

            _d[reg] = (_d[reg] & ~mask) | (result & mask);
            if (operation == 3) SetAddFlags(immediate, old, result, size);
            else if (operation == 2) SetSubFlags(immediate, old, result, size);
            else SetLogicFlags(result, size);

            Clocks(1);
            return;
        }

        uint address = await EaAddressAsync(mode, reg, size);
        if (_busFaulted) return;

        uint current = await ReadAtAsync(address, size);
        if (_busFaulted) return;

        uint computed = operation switch
        {
            0 => current | immediate,
            1 => current & immediate,
            2 => SubtractValue(current, immediate, size),
            3 => AddValue(current, immediate, size),
            5 => current ^ immediate,
            _ => current,
        };

        if (operation == 6)
        {
            SetSubFlags(immediate, current, current - immediate, size);
            return;
        }

        await WriteAtAsync(address, size, computed);
        if (operation == 3) SetAddFlags(immediate, current, computed, size);
        else if (operation == 2) SetSubFlags(immediate, current, computed, size);
        else SetLogicFlags(computed, size);

        Clocks(2);
    }

    uint AddValue(uint left, uint right, int size) => (left + right) & SizeMask(size);
    uint SubtractValue(uint left, uint right, int size) => (left - right) & SizeMask(size);

    async ValueTask<uint> ReadAtAsync(uint address, int size)
    {
        if (size == 0) return await ReadByteAsync(address);
        if (size == 1) return await ReadWordAsync(address);
        return await ReadLongAsync(address);
    }

    async ValueTask WriteAtAsync(uint address, int size, uint value)
    {
        if (size == 0) await WriteByteAsync(address, (byte)value);
        else if (size == 1) await WriteWordAsync(address, (ushort)value);
        else await WriteLongAsync(address, value);
    }

    async ValueTask ImmediateToCcrOrSrAsync(ushort op)
    {
        uint immediate = await FetchAsync();
        if ((op & 0x00FF) == 0x3C)
        {
            _sr = (ushort)((_sr & 0xFF00) | (int)(immediate & 0xFF));   // ORI to CCR
        }
        else
        {
            if (!Supervisor)
            {
                await EnterExceptionAsync(8, 0);
                return;
            }

            _sr = (ushort)(_sr | immediate);                        // ORI to SR
        }

        Clocks(2);
    }

    async ValueTask BitOperationAsync(ushort op, int size, int mode, int reg)
    {
        int operation = (op >> 6) & 0x3;   // 0 BTST, 1 BCHG, 2 BCLR, 3 BSET
        bool registerBit = (op & 0x0100) != 0;

        uint bitNumber = registerBit ? _d[(op >> 9) & 0x7] : (uint)(await FetchAsync() & 0xFF);
        if (_busFaulted) return;

        if (mode == 0)
        {
            int bit = (int)(bitNumber & 31);
            if (size == 0) bit &= 7;
            uint value = _d[reg];
            bool set = ((value >> bit) & 1) != 0;

            switch (operation)
            {
                case 1: _d[reg] = value ^ (1u << bit); break;
                case 2: _d[reg] = value & ~(1u << bit); break;
                case 3: _d[reg] = value | (1u << bit); break;
            }

            _sr = (ushort)(set ? _sr & ~FlagZ : _sr | FlagZ);
            Clocks(2);
            return;
        }

        uint address = await EaAddressAsync(mode, reg, 0);
        if (_busFaulted) return;

        byte current = (byte)await ReadAtAsync(address, 0);
        int position = (int)(bitNumber & 7);
        bool wasSet = ((current >> position) & 1) != 0;

        byte updated = operation switch
        {
            1 => (byte)(current ^ (1 << position)),
            2 => (byte)(current & ~(1 << position)),
            3 => (byte)(current | (1 << position)),
            _ => current,
        };

        if (operation != 0)
        {
            await WriteAtAsync(address, 0, updated);
        }

        _sr = (ushort)(wasSet ? _sr & ~FlagZ : _sr | FlagZ);
        Clocks(2);
    }

    // ---- line 4 ----------------------------------------------------------------------------

    async ValueTask Line4Async(ushort op)
    {
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        int size = (op >> 6) & 0x3;

        // MOVE to SR / to CCR / from CCR — the 0x46xx and 0x44xx/0x42xx blocks otherwise collide
        // with NOT and NEG/CLR
        if ((op & 0xFFC0) == 0x46C0)
        {
            if (!Supervisor)
            {
                await EnterExceptionAsync(8, 0);
                return;
            }

            uint status = await ReadEaAsync(mode, reg, 1);
            _sr = (ushort)status;
            Clocks(1);
            return;
        }

        if ((op & 0xFFC0) == 0x44C0)
        {
            uint condition = await ReadEaAsync(mode, reg, 1);
            _sr = (ushort)((_sr & 0xFF00) | (int)(condition & 0xFF));
            Clocks(1);
            return;
        }

        if ((op & 0xFFC0) == 0x42C0)
        {
            await WriteEaAsync(mode, reg, 1, (uint)(_sr & 0xFF));
            Clocks(1);
            return;
        }

        // LEA
        if ((op & 0x01C0) == 0x01C0)
        {
            uint address = await EaAddressAsync(mode, reg, 2);
            SetA((op >> 9) & 0x7, address);
            Clocks(1);
            return;
        }

        // CHK
        if ((op & 0x01C0) == 0x0180 && size == 3)
        {
            uint bound = (uint)(short)(await ReadEaAsync(mode, reg, 1) & 0xFFFF);
            int value = (short)(_d[(op >> 9) & 0x7] & 0xFFFF);
            if (value < 0 || value > (int)bound)
            {
                await EnterExceptionAsync(6, 0);
            }

            Clocks(6);
            return;
        }

        switch ((op >> 8) & 0xF)
        {
            case 0x0:   // MOVE from SR
            {
                int destinationMode = (op >> 3) & 0x7;
                int destinationReg = op & 0x7;
                int sz = (op >> 6) & 0x3;
                if (sz == 1) await WriteEaAsync(destinationMode, destinationReg, 1, _sr);
                else await EnterExceptionAsync(4, 0);   // MOVE from CCR / MOVE to CCR is 68010+
                Clocks(1);
                return;
            }

            case 0x2:   // CLR
            {
                await WriteEaAsync(mode, reg, size, 0);
                _sr = (ushort)((_sr & ~(FlagN | FlagV | FlagC)) | FlagZ);
                Clocks(1);
                return;
            }

            case 0x4:   // NEG
            {
                uint value = await ReadEaAsync(mode, reg, size);
                uint result = (0 - value) & SizeMask(size);
                await WriteEaAsync(mode, reg, size, result);
                SetSubFlags(value, 0, result, size);
                Clocks(1);
                return;
            }

            case 0x6:   // NOT
            {
                uint value = await ReadEaAsync(mode, reg, size);
                uint result = ~value & SizeMask(size);
                await WriteEaAsync(mode, reg, size, result);
                SetLogicFlags(result, size);
                Clocks(1);
                return;
            }

            case 0x8:   // NBCD / SWAP / EXT / PEA
            {
                if ((op & 0xFFF8) == 0x4840)   // SWAP Dn
                {
                    _d[reg] = (_d[reg] >> 16) | (_d[reg] << 16);
                    SetLogicFlags(_d[reg], 2);
                    Clocks(1);
                    return;
                }

                if ((op & 0xFFF8) == 0x4880)   // EXT.W Dn
                {
                    _d[reg] = (_d[reg] & 0xFFFF0000) | (uint)(ushort)(short)(sbyte)(_d[reg] & 0xFF);
                    SetLogicFlags(_d[reg] & 0xFFFF, 1);
                    Clocks(1);
                    return;
                }

                if ((op & 0xFFF8) == 0x48C0)   // EXT.L Dn
                {
                    _d[reg] = (uint)(int)(short)(_d[reg] & 0xFFFF);
                    SetLogicFlags(_d[reg], 2);
                    Clocks(1);
                    return;
                }

                if ((op & 0xFFC0) == 0x4840)   // PEA <ea>
                {
                    uint effective = await EaAddressAsync(mode, reg, 2);
                    uint stack = A7 - 4;
                    A7 = stack;
                    await WriteLongAsync(stack, effective);
                    Clocks(1);
                    return;
                }

                await EnterExceptionAsync(4, 0);   // NBCD is not used by TOS
                return;
            }

            case 0xA:   // TST / TAS
            {
                if ((op & 0x00C0) == 0x00C0 && mode == 0)
                {
                    byte value = (byte)(_d[reg] & 0xFF);   // TAS
                    SetLogicFlags(value, 0);
                    _d[reg] = (_d[reg] & 0xFFFFFF00) | (uint)(value | 0x80);
                    Clocks(1);
                    return;
                }

                uint current = await ReadEaAsync(mode, reg, size);
                SetLogicFlags(current, size);
                Clocks(1);
                return;
            }

            case 0xC:   // MOVEM memory → registers
                await MovemToRegistersAsync(op);
                return;

            case 0xE:
                await MiscAsync(op);
                return;
        }

        await EnterExceptionAsync(4, 0);   // illegal / unimplemented
    }

    async ValueTask MovemToRegistersAsync(ushort op)
    {
        int size = (op & 0x0040) != 0 ? 2 : 1;
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        ushort mask = await FetchAsync();
        if (_busFaulted) return;

        uint address = await EaAddressAsync(mode, reg, size);
        if (_busFaulted) return;

        for (int bit = 0; bit < 16; bit++)
        {
            if ((mask & (1 << bit)) == 0) continue;
            int index = 15 - bit;
            uint value = size == 2 ? await ReadLongAsync(address) : await ReadWordAsync(address);
            if (_busFaulted) return;

            if (index < 8) _d[index] = size == 2 ? value : (uint)(short)(value & 0xFFFF);
            else SetA(index - 8, size == 2 ? value : (uint)(short)(value & 0xFFFF));

            address += (uint)size;
        }

        SetA(reg, address);
        Clocks(3);
    }

    async ValueTask MovemToMemoryAsync(ushort op)
    {
        int size = (op & 0x0040) != 0 ? 2 : 1;
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        ushort mask = await FetchAsync();
        if (_busFaulted) return;

        bool predecrement = mode == 4;
        uint address = await EaAddressAsync(mode, reg, size);
        if (_busFaulted) return;

        int last = -1;

        for (int bit = 0; bit < 16; bit++)
        {
            if ((mask & (1 << bit)) == 0) continue;
            int index = predecrement ? bit : 15 - bit;
            uint value = index < 8 ? _d[index] : GetA(index - 8);

            if (predecrement)
            {
                address -= (uint)size;
                if (size == 2) await WriteLongAsync(address, value);
                else await WriteWordAsync(address, (ushort)value);
            }
            else
            {
                if (size == 2) await WriteLongAsync(address, value);
                else await WriteWordAsync(address, (ushort)value);
                address += (uint)size;
            }

            if (_busFaulted) return;
            last = index;
        }

        _ = last;

        if (predecrement || mode == 3)
        {
            SetA(reg, address);   // predecrement and postincrement both move the base
        }

        Clocks(3);
    }

    async ValueTask MiscAsync(ushort op)
    {
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;

        switch (op)
        {
            case 0x4E71:   // NOP
                Clocks(1);
                return;

            case 0x4E70:   // RESET
                if (!Supervisor)
                {
                    await EnterExceptionAsync(8, 0);
                    return;
                }

                Clocks(32);
                return;

            case 0x4E73:   // RTE
                await RteAsync();
                return;

            case 0x4E75:   // RTS
            {
                _pc = await ReadLongAsync(_sspOrUsp());
                Clocks(1);
                return;
            }

            case 0x4E76:   // TRAPV
                if ((_sr & FlagV) != 0) await EnterExceptionAsync(7, 0);
                return;

            case 0x4E77:   // RTR
            {
                ushort status = await ReadWordAsync(A7);
                A7 += 2;
                _sr = (ushort)((_sr & 0xFF00) | (status & 0xFF));
                _pc = await ReadLongAsync(A7);
                A7 += 4;
                Clocks(1);
                return;
            }

            case 0x4E72:   // STOP
            {
                if (!Supervisor)
                {
                    await EnterExceptionAsync(8, 0);
                    return;
                }

                _sr = await FetchAsync();
                _stopped = true;
                return;
            }
        }

        if ((op & 0xFFF0) == 0x4E40)   // TRAP #n
        {
            await EnterExceptionAsync(32 + (op & 0xF), 0);
            return;
        }

        if ((op & 0xFFF8) == 0x4E50)   // LINK
        {
            uint immediate = (uint)(short)await FetchAsync();
            uint stack = A7 - 4;
            A7 = stack;
            await WriteLongAsync(stack, GetA(reg));
            SetA(reg, stack);
            A7 += immediate;
            Clocks(2);
            return;
        }

        if ((op & 0xFFF8) == 0x4E58)   // UNLK
        {
            A7 = GetA(reg);
            uint value = await ReadLongAsync(A7);
            A7 += 4;
            SetA(reg, value);
            Clocks(2);
            return;
        }

        if ((op & 0xFFF0) == 0x4E60)   // MOVE An,USP
        {
            _usp = GetA(op & 0x7);
            Clocks(1);
            return;
        }

        if ((op & 0xFFF0) == 0x4E68)   // MOVE USP,An
        {
            SetA(op & 0x7, _usp);
            Clocks(1);
            return;
        }

        if ((op & 0xFFC0) == 0x4E80)   // JSR
        {
            uint target = await EaAddressAsync(mode, reg, 2);
            uint stack = A7 - 4;
            A7 = stack;
            await WriteLongAsync(stack, _pc);
            _pc = target;
            Clocks(2);
            return;
        }

        if ((op & 0xFFC0) == 0x4EC0)   // JMP
        {
            _pc = await EaAddressAsync(mode, reg, 2);
            Clocks(1);
            return;
        }

        if ((op & 0xFFC0) == 0x4C80)   // MOVEM registers → memory
        {
            await MovemToMemoryAsync(op);
            return;
        }

        await EnterExceptionAsync(4, 0);
    }

    uint _sspOrUsp() => A7;

    async ValueTask RteAsync()
    {
        if (!Supervisor)
        {
            await EnterExceptionAsync(8, 0);
            return;
        }

        ushort status = await ReadWordAsync(A7);
        A7 += 2;
        uint pc = await ReadLongAsync(A7);
        A7 += 4;

        bool wasSupervisor = (_sr & 0x2000) != 0;
        _sr = status;
        bool nowSupervisor = (_sr & 0x2000) != 0;

        if (wasSupervisor && !nowSupervisor)
        {
            uint userStack = _usp;
            _usp = A7;
            A7 = userStack;
        }

        _pc = pc;
        Clocks(2);
    }

    // ---- line 5: ADDQ/SUBQ, Scc, DBcc ------------------------------------------------------

    async ValueTask Line5Async(ushort op)
    {
        int data = (op >> 9) & 0x7;
        if (data == 0) data = 8;

        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        int size = (op >> 6) & 0x3;

        if (size == 3)
        {
            int condition = (op >> 8) & 0xF;

            if (mode == 1)   // DBcc
            {
                if (!Condition(condition))
                {
                    uint counter = (uint)((short)(_d[reg] & 0xFFFF) - 1) & 0xFFFF;
                    _d[reg] = (_d[reg] & 0xFFFF0000) | counter;
                    if (counter != 0xFFFF)
                    {
                        short displacement = (short)await FetchAsync();
                        _pc = _pc - 2 + (uint)displacement;
                        Clocks(2);
                        return;
                    }
                }

                await FetchAsync();   // the displacement is consumed either way
                Clocks(1);
                return;
            }

            await WriteEaAsync(mode, reg, 0, Condition(condition) ? 0xFFu : 0x00u);
            Clocks(1);
            return;
        }

        if (mode == 1)
        {
            // an address register is never affected by flags
            uint value = GetA(reg) + (uint)((op & 0x0100) != 0 ? -data : data);
            SetA(reg, size == 1 ? (uint)(short)(value & 0xFFFF) : value);
            Clocks(1);
            return;
        }

        uint current = await ReadEaAsync(mode, reg, size);
        uint result;
        if ((op & 0x0100) != 0)
        {
            result = (current - (uint)data) & SizeMask(size);
            await WriteEaAsync(mode, reg, size, result);
            SetSubFlags((uint)data, current, result, size);
        }
        else
        {
            result = (current + (uint)data) & SizeMask(size);
            await WriteEaAsync(mode, reg, size, result);
            SetAddFlags((uint)data, current, result, size);
        }

        Clocks(1);
    }

    // ---- line 6: branches ------------------------------------------------------------------

    async ValueTask Line6Async(ushort op)
    {
        int condition = (op >> 8) & 0xF;
        int displacement = op & 0xFF;
        long opcodeAddress = _pc - 2;

        if (displacement == 0)
        {
            displacement = (short)await FetchAsync();
        }
        else if (displacement == 0xFF)
        {
            displacement = (int)await FetchLongAsync();
        }
        else if (displacement >= 0x80)
        {
            displacement -= 0x100;
        }

        if (_busFaulted) return;

        // the target is relative to the address after the opcode word (UM §2.2's branch rule)
        long target = opcodeAddress + 2 + displacement;

        if (condition == 1)   // BSR
        {
            uint stack = A7 - 4;
            A7 = stack;
            await WriteLongAsync(stack, _pc);
            _pc = (uint)target;
            Clocks(2);
            return;
        }

        if (Condition(condition))
        {
            _pc = (uint)target;
        }

        Clocks(1);
    }

    // ---- lines 8 … D: the two-operand families ---------------------------------------------

    async ValueTask Line8Async(ushort op)   // OR / DIVU / DIVS / SBCD
    {
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        int size = (op >> 6) & 0x3;

        if ((op & 0xF1F0) == 0x8100)   // SBCD
        {
            await BcdAsync(op, subtract: true);
            return;
        }

        if ((op & 0x01C0) == 0x00C0 && size != 3)   // OR Dn,<ea>
        {
            uint value = _d[(op >> 9) & 0x7] & SizeMask(size);
            uint current = await ReadEaAsync(mode, reg, size);
            uint result = current | value;
            await WriteEaAsync(mode, reg, size, result);
            SetLogicFlags(result, size);
            Clocks(1);
            return;
        }

        if (size == 3)   // DIVU / DIVS
        {
            await DivideAsync(op);
            return;
        }

        uint source = await ReadEaAsync(mode, reg, size);
        uint destination = _d[(op >> 9) & 0x7] & SizeMask(size);
        uint combined = destination | source;
        _d[(op >> 9) & 0x7] = (_d[(op >> 9) & 0x7] & ~SizeMask(size)) | (combined & SizeMask(size));
        SetLogicFlags(combined, size);
        Clocks(1);
    }

    async ValueTask DivideAsync(ushort op)
    {
        bool signed = (op & 0x0100) != 0;
        int reg = op & 0x7;
        uint divisorValue = await ReadEaAsync((op >> 3) & 0x7, reg, 1);
        if (_busFaulted) return;

        uint dividend = _d[(op >> 9) & 0x7];

        if (signed)
        {
            int divisor = (short)divisorValue;
            int value = (int)dividend;
            if (divisor == 0)
            {
                await EnterExceptionAsync(5, 0);   // divide by zero
                return;
            }

            int quotient = value / divisor;
            int remainder = value % divisor;

            if (quotient > 32767 || quotient < -32768)
            {
                _sr |= FlagV;
                return;
            }

            _d[(op >> 9) & 0x7] = (((uint)(ushort)remainder) << 16) | (uint)(ushort)quotient;
        }
        else
        {
            if (divisorValue == 0)
            {
                await EnterExceptionAsync(5, 0);
                return;
            }

            uint quotient = dividend / divisorValue;
            uint remainder = dividend % divisorValue;

            if (quotient > 0xFFFF)
            {
                _sr |= FlagV;
                return;
            }

            _d[(op >> 9) & 0x7] = (remainder << 16) | quotient;
        }

        SetLogicFlags(_d[(op >> 9) & 0x7] & 0xFFFF, 1);
        Clocks(70);
    }

    async ValueTask Line9Async(ushort op)   // SUB / SUBX / SUBA
    {
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        int size = (op >> 6) & 0x3;

        if ((op & 0xF130) == 0x9100 && size != 3)   // SUBX
        {
            await ExchangeArithmeticAsync(op, subtract: true);
            return;
        }

        if ((op & 0x01C0) == 0x00C0 && size != 3)   // SUB Dn,<ea>
        {
            uint value = _d[(op >> 9) & 0x7] & SizeMask(size);
            uint current = await ReadEaAsync(mode, reg, size);
            uint result = (current - value) & SizeMask(size);
            await WriteEaAsync(mode, reg, size, result);
            SetSubFlags(value, current, result, size);
            Clocks(1);
            return;
        }

        if (size == 3)   // SUBA
        {
            uint value = (op & 0x0100) != 0 ? await ReadEaAsync(mode, reg, 2) : (uint)(short)(await ReadEaAsync(mode, reg, 1) & 0xFFFF);
            SetA((op >> 9) & 0x7, GetA((op >> 9) & 0x7) - value);
            Clocks(1);
            return;
        }

        uint source = await ReadEaAsync(mode, reg, size);
        uint destination = _d[(op >> 9) & 0x7] & SizeMask(size);
        uint difference = (destination - source) & SizeMask(size);
        _d[(op >> 9) & 0x7] = (_d[(op >> 9) & 0x7] & ~SizeMask(size)) | difference;
        SetSubFlags(source, destination, difference, size);
        Clocks(1);
    }

    async ValueTask ExchangeArithmeticAsync(ushort op, bool subtract)
    {
        int size = (op >> 6) & 0x3;
        int sourceReg = op & 0x7;
        int destinationReg = (op >> 9) & 0x7;
        bool predecrement = (op & 0x0008) != 0;

        uint source;
        uint destination;

        if (predecrement)
        {
            source = await PredecrementAsync(sourceReg, size);
            destination = await PredecrementAsync(destinationReg, size);
            if (_busFaulted) return;
        }
        else
        {
            source = _d[sourceReg] & SizeMask(size);
            destination = _d[destinationReg] & SizeMask(size);
        }

        uint extended = (uint)((_sr & FlagX) != 0 ? 1 : 0);
        uint result = subtract
            ? (destination - source - extended) & SizeMask(size)
            : (destination + source + extended) & SizeMask(size);

        if (subtract) SetSubFlags(source + extended, destination, result, size);
        else SetAddFlags(source + extended, destination, result, size);

        _d[destinationReg] = (_d[destinationReg] & ~SizeMask(size)) | result;

        Clocks(2);
    }

    async ValueTask<uint> PredecrementAsync(int reg, int size)
    {
        uint delta = (uint)(size == 0 && reg == 7 ? 2 : SizeBytes(size));
        uint address = GetA(reg) - delta;
        SetA(reg, address);
        return await ReadAtAsync(address, size);
    }

    async ValueTask LineBAsync(ushort op)   // CMP / CMPA / EOR / CMPM
    {
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        int size = (op >> 6) & 0x3;

        if ((op & 0xF138) == 0xB108 && size != 3)   // CMPM
        {
            uint delta = (uint)(size == 0 ? 2 : SizeBytes(size));
            uint sourceAddress = GetA(reg);
            SetA(reg, sourceAddress + delta);
            uint source = await ReadAtAsync(sourceAddress, size);
            uint destinationAddress = GetA((op >> 9) & 0x7);
            SetA((op >> 9) & 0x7, destinationAddress + delta);
            uint destination = await ReadAtAsync(destinationAddress, size);
            SetSubFlags(source, destination, (destination - source) & SizeMask(size), size);
            Clocks(2);
            return;
        }

        if ((op & 0x01C0) == 0x0040 && size != 3)   // EOR Dn,<ea>
        {
            uint value = _d[(op >> 9) & 0x7] & SizeMask(size);
            uint current = await ReadEaAsync(mode, reg, size);
            uint result = current ^ value;
            await WriteEaAsync(mode, reg, size, result);
            SetLogicFlags(result, size);
            Clocks(1);
            return;
        }

        if (size == 3)   // CMPA
        {
            uint value = (op & 0x0100) != 0 ? await ReadEaAsync(mode, reg, 2) : (uint)(short)(await ReadEaAsync(mode, reg, 1) & 0xFFFF);
            uint destination = GetA((op >> 9) & 0x7);
            SetSubFlags(value, destination, destination - value, 2);
            Clocks(1);
            return;
        }

        uint operand = await ReadEaAsync(mode, reg, size);
        uint current2 = _d[(op >> 9) & 0x7] & SizeMask(size);
        SetSubFlags(operand, current2, (current2 - operand) & SizeMask(size), size);
        Clocks(1);
    }

    async ValueTask LineCAsync(ushort op)   // AND / MULU / MULS / ABCD / EXG
    {
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        int size = (op >> 6) & 0x3;

        if ((op & 0xF1F0) == 0xC100)   // ABCD
        {
            await BcdAsync(op, subtract: false);
            return;
        }

        if ((op & 0xF1F8) == 0xC140)   // EXG Dn,Dn
        {
            int other = (op >> 9) & 0x7;
            (_d[reg], _d[other]) = (_d[other], _d[reg]);
            Clocks(1);
            return;
        }

        if ((op & 0xF1F8) == 0xC148 || (op & 0xF1F8) == 0xC188)   // EXG An,An / Dn,An
        {
            int other = (op >> 9) & 0x7;
            uint left = GetA(reg);
            uint right = (op & 0x00F8) == 0x88 ? _d[other] : GetA(other);
            SetA(reg, right);
            if ((op & 0x00F8) == 0x88) _d[other] = left;
            else SetA(other, left);
            Clocks(1);
            return;
        }

        if ((op & 0x01C0) == 0x00C0 && size != 3)   // AND Dn,<ea>
        {
            uint value = _d[(op >> 9) & 0x7] & SizeMask(size);
            uint current = await ReadEaAsync(mode, reg, size);
            uint result = current & value;
            await WriteEaAsync(mode, reg, size, result);
            SetLogicFlags(result, size);
            Clocks(1);
            return;
        }

        if (size == 3)   // MULU / MULS
        {
            bool signed = (op & 0x0100) != 0;
            uint source = await ReadEaAsync(mode, reg, 1);
            uint destination = _d[(op >> 9) & 0x7] & 0xFFFF;

            _d[(op >> 9) & 0x7] = signed
                ? (uint)((short)destination * (short)source)
                : destination * source;

            SetLogicFlags(_d[(op >> 9) & 0x7], 2);
            Clocks(35);
            return;
        }

        uint operand = await ReadEaAsync(mode, reg, size);
        uint destination2 = _d[(op >> 9) & 0x7] & SizeMask(size);
        uint combined = destination2 & operand;
        _d[(op >> 9) & 0x7] = (_d[(op >> 9) & 0x7] & ~SizeMask(size)) | combined;
        SetLogicFlags(combined, size);
        Clocks(1);
    }

    async ValueTask BcdAsync(ushort op, bool subtract)
    {
        int size = 0;
        int sourceReg = op & 0x7;
        int destinationReg = (op >> 9) & 0x7;
        bool predecrement = (op & 0x0008) != 0;

        uint source;
        uint destination;

        if (predecrement)
        {
            source = await PredecrementAsync(sourceReg, size);
            destination = await PredecrementAsync(destinationReg, size);
        }
        else
        {
            source = _d[sourceReg] & 0xFF;
            destination = _d[destinationReg] & 0xFF;
        }

        int carry = (_sr & FlagX) != 0 ? 1 : 0;
        int result;
        bool overflow;

        if (subtract)
        {
            result = (int)(destination & 0x0F) - (int)(source & 0x0F) - carry;
            if (result < 0) result += 10;
            int high = (int)((destination >> 4) & 0x0F) - (int)((source >> 4) & 0x0F) - (destination & 0x0F) < (source & 0x0F) + carry ? 1 : 0;
            overflow = high < 0;
            if (high < 0) high += 10;
            result = ((high & 0x0F) << 4) | (result & 0x0F);
            _sr = (ushort)(overflow ? _sr | (FlagC | FlagX) : _sr & ~(FlagC | FlagX));
        }
        else
        {
            result = (int)(destination & 0x0F) + (int)(source & 0x0F) + carry;
            if (result > 9) result += 6;
            int high = (int)((destination >> 4) & 0x0F) + (int)((source >> 4) & 0x0F) + (result > 0x0F ? 1 : 0);
            overflow = high > 9;
            if (overflow) high += 6;
            result = ((high & 0x0F) << 4) | (result & 0x0F);
            _sr = (ushort)(overflow ? _sr | (FlagC | FlagX) : _sr & ~(FlagC | FlagX));
        }

        _sr = (ushort)((_sr & ~FlagZ) | (result == 0 ? FlagZ : 0));

        if (predecrement)
        {
            _d[destinationReg] = (_d[destinationReg] & 0xFFFFFF00) | (uint)(result & 0xFF);
        }
        else
        {
            _d[destinationReg] = (_d[destinationReg] & 0xFFFFFF00) | (uint)(result & 0xFF);
        }

        Clocks(2);
    }

    async ValueTask LineDAsync(ushort op)   // ADD / ADDX / ADDA
    {
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        int size = (op >> 6) & 0x3;

        if ((op & 0xF130) == 0xD100 && size != 3)   // ADDX
        {
            await ExchangeArithmeticAsync(op, subtract: false);
            return;
        }

        if ((op & 0x01C0) == 0x00C0 && size != 3)   // ADD Dn,<ea>
        {
            uint value = _d[(op >> 9) & 0x7] & SizeMask(size);
            uint current = await ReadEaAsync(mode, reg, size);
            uint result = (current + value) & SizeMask(size);
            await WriteEaAsync(mode, reg, size, result);
            SetAddFlags(value, current, result, size);
            Clocks(1);
            return;
        }

        if (size == 3)   // ADDA
        {
            uint value = (op & 0x0100) != 0 ? await ReadEaAsync(mode, reg, 2) : (uint)(short)(await ReadEaAsync(mode, reg, 1) & 0xFFFF);
            SetA((op >> 9) & 0x7, GetA((op >> 9) & 0x7) + value);
            Clocks(1);
            return;
        }

        uint source = await ReadEaAsync(mode, reg, size);
        uint destination = _d[(op >> 9) & 0x7] & SizeMask(size);
        uint sum = (destination + source) & SizeMask(size);
        _d[(op >> 9) & 0x7] = (_d[(op >> 9) & 0x7] & ~SizeMask(size)) | sum;
        SetAddFlags(source, destination, sum, size);
        Clocks(1);
    }

    // ---- line E: shifts and rotates --------------------------------------------------------

    async ValueTask LineEAsync(ushort op)
    {
        int size = (op >> 6) & 0x3;
        int mode = (op >> 3) & 0x7;

        int kind = (op >> 3) & 0x3;
        bool left = (op & 0x0100) != 0;

        if (mode == 0 || mode == 1)   // register shift
        {
            int reg = op & 0x7;
            int count = (op >> 9) & 0x7;

            if (count == 0) count = 8;

            uint value = mode == 0
                ? _d[reg] & SizeMask(size)
                : (uint)(short)(_d[reg] & 0xFFFF);

            uint result = Shift(value, count, size, kind, left);
            if (mode == 0)
            {
                _d[reg] = (_d[reg] & ~SizeMask(size)) | (result & SizeMask(size));
            }
            else
            {
                _d[reg] = (_d[reg] & 0xFFFF0000) | (result & 0xFFFF);
            }

            return;
        }

        // memory shift by one: the size field is the direction's own encoding
        int memoryKind = (op >> 9) & 0x3;
        await MemoryShiftAsync(mode, op & 0x7, memoryKind, left);
    }

    /// <summary>
    /// One shift or rotate, <paramref name="kind"/> 0 = arithmetic, 1 = logical, 2 = rotate through
    /// extend, 3 = rotate. <paramref name="left"/> is the direction bit; flags are the 68000's.
    /// </summary>
    uint Shift(uint value, int count, int size, int kind, bool left)
    {
        int bits = SizeBits(size);
        uint mask = SizeMask(size);
        uint sign = (uint)SignBit(size);
        uint result = value & mask;
        bool carry = (_sr & FlagC) != 0;
        bool extend = (_sr & FlagX) != 0;

        count &= bits - 1 == 0 ? 31 : 63;
        if (count == 0)
        {
            carry = false;
        }

        bool overflow = false;

        for (int step = 0; step < count; step++)
        {
            switch (kind)
            {
                case 0:   // ASL / ASR
                    if (left)
                    {
                        carry = (result & sign) != 0;
                        result = (result << 1) & mask;
                        if (carry != ((result & sign) != 0)) overflow = true;
                    }
                    else
                    {
                        carry = (result & 1) != 0;
                        result = (result >> 1) | (result & sign);   // the sign is preserved
                    }

                    break;

                case 1:   // LSL / LSR
                    if (left)
                    {
                        carry = (result & sign) != 0;
                        result = (result << 1) & mask;
                    }
                    else
                    {
                        carry = (result & 1) != 0;
                        result >>= 1;
                    }

                    break;

                case 2:   // ROXL / ROXR
                    if (left)
                    {
                        carry = (result & sign) != 0;
                        result = (result << 1) & mask;
                        if (extend) result |= 1;
                    }
                    else
                    {
                        carry = (result & 1) != 0;
                        result >>= 1;
                        if (extend) result |= sign;
                    }

                    extend = carry;
                    break;

                default:   // ROL / ROR
                    if (left)
                    {
                        carry = (result & sign) != 0;
                        result = ((result << 1) & mask) | (carry ? 1u : 0u);
                    }
                    else
                    {
                        carry = (result & 1) != 0;
                        result = (result >> 1) | (carry ? sign : 0u);
                    }

                    break;
            }
        }

        if (kind == 0 && !left)
        {
            // an arithmetic right shift keeps the sign, and V is always cleared
            if ((value & sign) != 0)
            {
                for (int i = 0; i < count && i < bits; i++)
                {
                    result |= sign;
                }
            }
        }

        _sr = (ushort)(carry ? _sr | FlagC : _sr & ~FlagC);
        _sr = (ushort)((kind == 2) ? (extend ? _sr | FlagX : _sr & ~FlagX) : _sr & ~FlagX);
        _sr = (ushort)((result & mask) == 0 ? _sr | FlagZ : _sr & ~FlagZ);
        _sr = (ushort)((result & sign) != 0 ? _sr | FlagN : _sr & ~FlagN);
        _sr = (ushort)(overflow ? _sr | FlagV : _sr & ~FlagV);

        Clocks(2 + Math.Min(count, 8));
        return result & mask;
    }

    async ValueTask MemoryShiftAsync(int mode, int reg, int kind, bool left)
    {
        uint address = await EaAddressAsync(mode, reg, 1);
        uint value = await ReadWordAsync(address);
        uint result = Shift(value, 1, 1, kind, left);
        await WriteWordAsync(address, (ushort)result);
    }
}
