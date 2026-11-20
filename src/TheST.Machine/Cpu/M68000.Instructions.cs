namespace TheST.Machine.Cpu;

/// <summary>
/// The instruction set. One <c>switch</c> per opcode line, the effective-address machine underneath it,
/// and a bus cycle at every access — so an instruction's cost is whichever answer instants its accesses
/// land on, never a table.
/// </summary>
public sealed partial class M68000
{
    static int SizeBytes(int size) => size == 0 ? 1 : size == 1 ? 2 : 4;

    /// <summary>The number of significant bits of a quotient — a divide's own clock counts them.</summary>
    static int BitLength(uint value) => value == 0 ? 0 : 32 - System.Numerics.BitOperations.LeadingZeroCount(value);

    async ValueTask ExecuteAsync(ushort op)
    {
        // the internal clock (M68000.Timing.cs): the family's own work, charged before the closing
        // fetch — or before the instruction's first access, for the families whose clocks lead
        int own = InstructionOwnClocks(op);
        if (LeadsOwnClocks(op))
        {
            await ClocksAsync(own);
        }
        else
        {
            _own = own;
        }

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
        // the effective address's own clocks (M68000.Timing.cs): a predecrement or an indexed address
        // costs 2, charged once per instruction because two predecrements share one (ADDX -(An),-(An))
        if (!_eaCharged && (mode == 4 || mode == 6 || (mode == 7 && reg == 3)))
        {
            _eaCharged = true;
            await ClocksAsync(2, prefetch: true);
        }

        switch (mode)
        {
            case 2:
                return GetA(reg);

            case 3:
            {
                uint address = GetA(reg);
                SetA(reg, address + (uint)(size == 0 && reg == 7 ? 2 : SizeBytes(size)));
                PostIncremented(reg, address);
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
                // (d8, An, Xn): the displacement is the brief extension word's low byte, the index its
                // top bits — forgetting the index here makes every indexed operand point elsewhere
                ushort extension = await FetchAsync();
                if (_busFaulted) return 0;
                return GetA(reg) + (uint)ExtensionDisplacement(extension) + IndexValue(extension);
            }

            case 7:
                switch (reg)
                {
                    case 0: return (uint)(short)await FetchAsync();
                    // the full 32-bit address: the bus is 24-bit and truncates at the wire, but the
                    // program counter a jump stores is the whole value the corpus hands back
                    case 1: return await FetchLongAsync();
                    case 2:
                    {
                        short displacement = (short)await FetchAsync();
                        return _pc - 2 + (uint)displacement;
                    }

                    case 3:
                    {
                        ushort extension = await FetchAsync();
                        if (_busFaulted) return 0;
                        return _pc - 2 + (uint)ExtensionDisplacement(extension) + IndexValue(extension);
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
        if (_busFaulted) return 0;
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

        // the prefetch class: a plain MOVE's trailing fetch is issued before its write. The corpus's
        // traces put it there only for a `-(An)` destination — that address's own step is where the
        // bus comes free — while `(An)`/`(An)+` and the stream-built addresses take it at the closing
        // steps or lose it to the fault
        if (PrefetchBeforeWrite && mode == 4)
        {
            await TrailingFetchAsync();
        }

        // A long store into -(An) steps down a word at a time and writes the low word first, so an
        // odd register faults on that first word with the register down 2, not 4 — while a long
        // *read* steps down 4 and faults at A − 4. The corpus measures both (FINDINGS row 44).
        bool plainMove = (_opcode >> 12) is 1 or 2 or 3;
        if (plainMove && mode == 4 && size == 2 && (GetA(reg) & 1) != 0)
        {
            uint start = GetA(reg);
            SetA(reg, start - 2);
            await WriteWordAsync(start - 2, (ushort)value);
            return;
        }

        // A plain MOVE's absolute-long destination is ADR 0009 §4's per-opcode placement: the pair is
        // consumed by one pop (the reference's MOVE_bw_AbsoluteLong), so the address costs one read
        // and the queue is left a word short — see FetchMoveAbsoluteLongAsync for the whole rule.
        bool moveAbsoluteLong = plainMove && mode == 7 && reg == 1;
        uint address = moveAbsoluteLong
            ? await FetchMoveAbsoluteLongAsync()
            : await EaAddressAsync(mode, reg, size);
        if (_busFaulted) return;

        // The class's other half: a source that cost no bus cycle of its own — a register or an
        // immediate (the reference's MOVE_bw/MOVE_l_AbsoluteLong_prefetch_first states) — leaves the
        // bus free, so the word after the address is fetched *before* the store and an odd
        // destination's fault keeps it. Every other source stores first and the fault cancels it:
        // that single datum is all that separates records `33ea` (2 reads) and `33fc` (3).
        if (moveAbsoluteLong && MoveSourceNeedsNoBusCycle)
        {
            await TrailingFetchAsync();
            if (_busFaulted) return;
        }

        // An address the instruction stream built is validated where it is built: a word access to an
        // odd `(d16,An)`/`(d8,An,Xn)`/absolute destination faults there, *before* the trailing fetch,
        // which is why the corpus's traces show one fetch fewer for those destinations than for a
        // `-(An)` one (whose address comes from a register and is checked at the bus cycle).
        if (size != 0 && (address & 1) != 0 && mode is 5 or 6 or 7)
        {
            _faultAddress = address;
            _faultWrite = true;
            _faultIsFetch = false;
            _addressError = true;
            _busFaulted = true;
            return;
        }

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

        // The flags are the corpus's, and they are set by the *move*, not by the store landing: a
        // destination that faults on an odd address still leaves N, Z, V and C read off the value
        // that was on its way there (the model's `move()` updates the status, then the bus cycle
        // raises the address error).
        if (destinationMode != 1)
        {
            SetLogicFlags(value, size);
        }

        await WriteEaAsync(destinationMode, destinationReg, size, value);
        if (_busFaulted) return;

    }

    async ValueTask MoveQAsync(ushort op)
    {
        int data = op & 0xFF;
        if (data >= 0x80) data -= 0x100;
        _d[(op >> 9) & 0x7] = (uint)data;
        SetLogicFlags((uint)data, 2);
    }

    // ---- line 0: immediate and bit operations ---------------------------------------------

    async ValueTask Line0Async(ushort op)
    {
        int size = (op >> 6) & 0x3;
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        int operation = (op >> 9) & 0x7;

        if ((op & 0x0100) == 0 && mode == 7 && reg == 4 && size <= 1 && (operation == 0 || operation == 1 || operation == 5))
        {
            await ImmediateToCcrOrSrAsync(op, operation);
            return;
        }

        // the long immediates are scripts (M68000.Scripts.cs): o#l1/o#w1 and #79's tails
        if (IsScriptedImmediateLong(op))
        {
            await ImmediateLongAsync(op);
            return;
        }

        // MOVEP sits in the bit-operation block's space: mode 001 (address-register direct, which no bit
        // operation can use) with bit 8 set. Its own bits 7-6 are the form, so a word form has size 0 —
        // testing size here sent every MOVEP.w memory → register into the bit-operation path instead,
        // where it silently did nothing.
        if ((op & 0x0100) != 0 && mode == 1)
        {
            await MovepAsync(op);
            return;
        }

        // the bit operations: bit 8 selects the register form (BITNUM in Dn), the 0x08xx block is the
        // static form (bit number in the instruction word's bits 11-9). Reading bit 8 as "immediate"
        // sent every static form (BTST #0,D1 and friends) down the immediate-arithmetic path instead,
        // where operation 4 means nothing and the flags were left as they were.
        if ((op & 0x0100) != 0 || (op & 0x0F00) == 0x0800)
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

            if (operation == 6)   // CMPI: a comparison, so X is untouched
            {
                SetCompareFlags(immediate, old, old - immediate, size);
                return;
            }

            _d[reg] = (_d[reg] & ~mask) | (result & mask);
            if (operation == 3) SetAddFlags(immediate, old, result, size);
            else if (operation == 2) SetSubFlags(immediate, old, result, size);
            else SetLogicFlags(result, size);

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

        if (operation == 6)   // CMPI: a comparison, so X is untouched
        {
            SetCompareFlags(immediate, current, current - immediate, size);
            return;
        }

        await WriteAtAsync(address, size, computed);
        if (_busFaulted) return;
        if (operation == 3) SetAddFlags(immediate, current, computed, size);
        else if (operation == 2) SetSubFlags(immediate, current, computed, size);
        else SetLogicFlags(computed, size);

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

    /// <summary>
    /// ORI / ANDI / EORI to CCR and to SR — the same three operations on the status register, with the
    /// SR forms privileged and their unused bits written back as zero.
    /// </summary>
    async ValueTask ImmediateToCcrOrSrAsync(ushort op, int operation)
    {
        const ushort DefinedSr = 0xA71F;

        uint immediate = await FetchAsync();
        if (_busFaulted) return;

        bool toStatusRegister = ((op >> 6) & 0x1) != 0;
        if (toStatusRegister && !Supervisor)
        {
            await EnterExceptionAsync(8, 0);
            return;
        }

        ushort mask = toStatusRegister ? DefinedSr : (ushort)0x00FF;
        ushort value = (ushort)(immediate & mask);

        _sr = operation switch
        {
            0 => (ushort)(_sr | value),                    // ORI
            1 => (ushort)(_sr & (value | ~mask)),          // ANDI
            _ => (ushort)(_sr ^ value),                    // EORI
        };

        _sr = (ushort)(_sr & DefinedSr);
    }

    async ValueTask BitOperationAsync(ushort op, int size, int mode, int reg)
    {
        int operation = (op >> 6) & 0x3;   // 0 BTST, 1 BCHG, 2 BCLR, 3 BSET
        bool registerBit = (op & 0x0100) != 0;

        // the static form carries its bit number in the extension word that follows the opcode (which
        // is why it is a 4-byte instruction); only the register form takes it from a data register
        uint bitNumber = registerBit ? _d[(op >> 9) & 0x7] : (uint)(await FetchAsync() & 0xFF);
        if (_busFaulted) return;

        if (mode == 0)
        {
            // a bit operation on a data register is always long, whatever the operation field says
            int bit = (int)(bitNumber & 31);
            uint value = _d[reg];
            bool set = ((value >> bit) & 1) != 0;

            switch (operation)
            {
                case 1: _d[reg] = value ^ (1u << bit); break;
                case 2: _d[reg] = value & ~(1u << bit); break;
                case 3: _d[reg] = value | (1u << bit); break;
            }

            _sr = (ushort)(set ? _sr & ~FlagZ : _sr | FlagZ);
            return;
        }

        // `BTST Dn,#data`: the operand is the immediate word that follows the opcode — not an
        // address. Without this the EA helper answered 0 and the bit was read out of low memory,
        // and the extension word was never fetched (the corpus's pc was two bytes short).
        if (mode == 7 && reg == 4)
        {
            ushort immediate = await FetchAsync();
            if (_busFaulted) return;

            // `BTST Dn,#data` is a *byte* test — only the immediate's low byte and the bit numbers
            // 0–7 are reachable, whatever a matching word might hold
            bool immediateSet = (((immediate & 0xFF) >> (int)(bitNumber & 7)) & 1) != 0;
            _sr = (ushort)(immediateSet ? _sr & ~FlagZ : _sr | FlagZ);
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
            if (_busFaulted) return;
        }

        _sr = (ushort)(wasSet ? _sr & ~FlagZ : _sr | FlagZ);
    }

    /// <summary>
    /// MOVEP — alternating bytes between a data register and memory, high byte first, odd addresses
    /// skipped (UM §4.2). Its four forms are bits 7-6: 10 and 11 are Dn to memory, word then long.
    /// </summary>
    async ValueTask MovepAsync(ushort op)
    {
        int dataReg = (op >> 9) & 0x7;
        int addressReg = op & 0x7;
        int form = (op >> 6) & 0x3;

        uint address = GetA(addressReg) + (uint)(short)await FetchAsync();
        if (_busFaulted) return;

        // Bits 7-6: 00 and 01 are memory → register (word, long), 10 and 11 are register → memory.
        bool toMemory = form >= 2;
        bool isLong = (form & 1) != 0;

        if (toMemory)
        {
            uint value = _d[dataReg];
            if (isLong)
            {
                await WriteByteAsync(address, (byte)(value >> 24));
                await WriteByteAsync(address + 2, (byte)(value >> 16));
                await WriteByteAsync(address + 4, (byte)(value >> 8));
                await WriteByteAsync(address + 6, (byte)value);
            }
            else
            {
                await WriteByteAsync(address, (byte)(value >> 8));
                await WriteByteAsync(address + 2, (byte)value);
            }

            if (_busFaulted) return;
        }
        else
        {
            uint value = 0;
            if (isLong)
            {
                value = (uint)await ReadByteAsync(address) << 24;
                if (_busFaulted) return;
                value |= (uint)await ReadByteAsync(address + 2) << 16;
                if (_busFaulted) return;
                value |= (uint)await ReadByteAsync(address + 4) << 8;
                if (_busFaulted) return;
                value |= await ReadByteAsync(address + 6);
            }
            else
            {
                value = (uint)await ReadByteAsync(address) << 8;
                if (_busFaulted) return;
                value |= await ReadByteAsync(address + 2);
            }

            if (_busFaulted) return;
            _d[dataReg] = isLong ? value : (_d[dataReg] & 0xFFFF0000) | value;
        }

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
            if (_busFaulted) return;
            _sr = (ushort)status;
            return;
        }

        if ((op & 0xFFC0) == 0x44C0)
        {
            uint condition = await ReadEaAsync(mode, reg, 1);
            if (_busFaulted) return;
            _sr = (ushort)((_sr & 0xFF00) | (int)(condition & 0xFF));
            return;
        }

        // MOVE from CCR (0x42C0–0x42FF, i.e. the whole 0x42xx block with operand size 11) is a 68010
        // instruction: on this 68000 it is not an instruction at all. Executing it was not a cosmetic
        // problem — EmuTOS's _detect_cpu probes exactly here first (bios/processor.S: MOVEW_CCR_D0,
        // commented "legal on 68010+") and only an illegal-instruction trap tells it "68000". With the
        // probe silently succeeding, _mcpu came back non-zero, _longframe was set, and every exception
        // frame afterwards carried a word the 68000 does not push: the first BIOS trap read its
        // argument two bytes high (a0 = $74CA0000 instead of $000074CA) and the ROM panicked at
        // _getmpb, about 170 frames into the boot.
        if ((op & 0xFF00) == 0x4200 && ((op >> 6) & 0x3) == 0x3)
        {
            await EnterExceptionAsync(4, 0);
            return;
        }

        if (IsScriptedSingleOperandLong(op))
        {
            // scripted (M68000.Scripts.cs): NOT/CLR/NEG/NEGX/TST .l — one RMW block (nnml1 -> nnml2 ->
            // morw2, or tsml1/tsml2 for TST), which is where the table had nothing to charge: CLR.l's
            // clock column was 8 short on 1 580 records because the microcode charges two states there
            await SingleOperandLongAsync(op);
            return;
        }

        if (IsScriptedSingleOperandWord(op))
        {
            // and the same five in .w, over the word block: adrw1/adrw2, pinw1/pinw2, pdcw1/pdcw2,
            // adsw1/adsw2, aixw*, abwl1/abll3, then nnmw1 + morw2 (TST: tsmw1 + mmrw3)
            await SingleOperandWordOrByteAsync(op, 1);
            return;
        }

        if (IsScriptedSingleOperandByte(op))
        {
            // and in .b over the *same* block — the byte forms are the word ones with a lane and a
            // one-byte unit step (A7's step stays two). CLR reads the byte it zeroes, which is the
            // whole of the byte clock column: 4 clocks on every memory record
            await SingleOperandWordOrByteAsync(op, 0);
            return;
        }

        // LEA
        if ((op & 0x01C0) == 0x01C0)
        {
            uint address = await EaAddressAsync(mode, reg, 2);
            SetA((op >> 9) & 0x7, address);
            return;
        }

        // CHK
        if ((op & 0x01C0) == 0x0180)   // CHK (bits 8-6 = 110)
        {
            // The flags are the corpus's, and they are not the PRM's "undefined": Z, V and C are always
            // cleared, N is *set* when the trap is Dn < 0, *cleared* when it is Dn > bound, and left
            // alone when there is no trap at all; X is untouched. The comparison is signed.
            int bound = (short)(await ReadEaAsync(mode, reg, 1) & 0xFFFF);
            if (_busFaulted) return;
            int value = (short)(_d[(op >> 9) & 0x7] & 0xFFFF);

            _sr = (ushort)(_sr & ~(FlagZ | FlagV | FlagC));
            if (value < 0)
            {
                _sr = (ushort)(_sr | FlagN);
                await EnterExceptionAsync(6, 0);
            }
            else if (value > bound)
            {
                _sr = (ushort)(_sr & ~FlagN);
                await EnterExceptionAsync(6, 0);
            }

            return;
        }

        switch ((op >> 8) & 0xF)
        {
            case 0x0:   // NEGX / MOVE from SR
            {
                int destinationMode = (op >> 3) & 0x7;
                int destinationReg = op & 0x7;
                int sz = (op >> 6) & 0x3;

                if (sz != 3)   // NEGX: 0 - operand - X
                {
                    uint negxAddress = destinationMode == 0 ? 0 : await EaAddressAsync(destinationMode, destinationReg, sz);
                    if (_busFaulted) return;

                    uint negxValue = destinationMode == 0
                        ? _d[destinationReg] & SizeMask(sz)
                        : await ReadAtAsync(negxAddress, sz);
                    if (_busFaulted) return;

                    uint extend = (uint)((_sr & FlagX) != 0 ? 1 : 0);
                    uint negxResult = (0 - negxValue - extend) & SizeMask(sz);

                    if (destinationMode == 0)
                    {
                        _d[destinationReg] = (_d[destinationReg] & ~SizeMask(sz)) | negxResult;
                    }
                    else
                    {
                        await WriteAtAsync(negxAddress, sz, negxResult);
                        if (_busFaulted) return;
                    }

                    // NEGX carries ADDX/SUBX's zero rule: Z is only ever cleared, so a chain of
                    // borrows reports the whole result (0 − operand − X is a subtract from zero)
                    SetExchangeFlags(true, negxValue, 0, negxResult, sz);
                    return;
                }

                if (sz == 3)
                {
                    await WriteEaAsync(destinationMode, destinationReg, 1, (uint)(_sr & 0xA71F));
                    if (_busFaulted) return;
                    return;
                }

                await EnterExceptionAsync(4, 0);   // MOVE from CCR is 68010+
                return;
            }

            case 0x2:   // CLR
            {
                await WriteEaAsync(mode, reg, size, 0);
                if (_busFaulted) return;
                _sr = (ushort)((_sr & ~(FlagN | FlagV | FlagC)) | FlagZ);
                return;
            }

            case 0x4:   // NEG
            {
                // the operand is addressed once: addressing it again to write would re-read the
                // displacement word and move the destination
                uint negAddress = mode == 0 ? 0 : await EaAddressAsync(mode, reg, size);
                if (_busFaulted) return;

                uint value = mode == 0 ? _d[reg] & SizeMask(size) : await ReadAtAsync(negAddress, size);
                if (_busFaulted) return;

                uint result = (0 - value) & SizeMask(size);
                if (mode == 0)
                {
                    _d[reg] = (_d[reg] & ~SizeMask(size)) | result;
                }
                else
                {
                    await WriteAtAsync(negAddress, size, result);
                    if (_busFaulted) return;
                }

                SetSubFlags(value, 0, result, size);
                return;
            }

            case 0x6:   // NOT
            {
                uint notAddress = mode == 0 ? 0 : await EaAddressAsync(mode, reg, size);
                if (_busFaulted) return;

                uint value = mode == 0 ? _d[reg] & SizeMask(size) : await ReadAtAsync(notAddress, size);
                if (_busFaulted) return;

                uint result = ~value & SizeMask(size);
                if (mode == 0)
                {
                    _d[reg] = (_d[reg] & ~SizeMask(size)) | result;
                }
                else
                {
                    await WriteAtAsync(notAddress, size, result);
                    if (_busFaulted) return;
                }

                SetLogicFlags(result, size);
                return;
            }

            case 0x8:   // NBCD / SWAP / EXT / PEA
            {
                if ((op & 0xFFF8) == 0x4840)   // SWAP Dn
                {
                    _d[reg] = (_d[reg] >> 16) | (_d[reg] << 16);
                    SetLogicFlags(_d[reg], 2);
                    return;
                }

                if ((op & 0xFFF8) == 0x4880)   // EXT.W Dn
                {
                    _d[reg] = (_d[reg] & 0xFFFF0000) | (uint)(ushort)(short)(sbyte)(_d[reg] & 0xFF);
                    SetLogicFlags(_d[reg] & 0xFFFF, 1);
                    return;
                }

                if ((op & 0xFFF8) == 0x48C0)   // EXT.L Dn
                {
                    _d[reg] = (uint)(int)(short)(_d[reg] & 0xFFFF);
                    SetLogicFlags(_d[reg], 2);
                    return;
                }

                // MOVEM registers → memory shares this block with EXT: 0x4880/0x48C0 with a mode and a
                // register is the transfer, and only its mode-0 form (0x4880-0x4887/0x48C0-0x48C7) is
                // EXT.W/EXT.L Dn. Without this the instruction fell through to NBCD below, which is
                // exactly the shape of "memset wrote one zero byte per movem and walked its pointer
                // down through the stack".
                if ((op & 0xFF80) == 0x4880 && (op & 0x0038) >= 0x10)
                {
                    await MovemToMemoryAsync(op);
                    return;
                }

                if ((op & 0xFFC0) == 0x4840)   // PEA <ea>
                {
                    uint effective = await EaAddressAsync(mode, reg, 2);
                    uint stack = A7 - 4;
                    A7 = stack;
                    await WriteLongAsync(stack, effective);
                    return;
                }

                // NBCD: 0 - operand - X, in packed BCD (UM §4.2)
                {
                    uint nbcdAddress = mode == 0 ? 0 : await EaAddressAsync(mode, reg, 0);
                    if (_busFaulted) return;

                    uint nv = mode == 0 ? _d[reg] & 0xFF : await ReadAtAsync(nbcdAddress, 0);
                    if (_busFaulted) return;

                    BcdCore(nv, 0, subtract: true, (_sr & FlagX) != 0, out uint nbcdResult, out bool carry, out bool overflow);
                    SetBcdFlags(nbcdResult, carry, overflow);

                    if (mode == 0)
                    {
                        _d[reg] = (_d[reg] & 0xFFFFFF00) | nbcdResult;
                    }
                    else
                    {
                        await WriteAtAsync(nbcdAddress, 0, nbcdResult);
                        if (_busFaulted) return;
                    }

                    return;
                }
            }

            case 0xA:   // TST / TAS
            {
                if ((op & 0x00C0) == 0x00C0)   // TAS: a read-modify-write, in a register or in memory
                {
                    if (mode == 0)
                    {
                        byte registerValue = (byte)(_d[reg] & 0xFF);
                        SetLogicFlags(registerValue, 0);
                        _d[reg] = (_d[reg] & 0xFFFFFF00) | (uint)(registerValue | 0x80);
                    }
                    else
                    {
                        uint tasAddress = await EaAddressAsync(mode, reg, 0);
                        if (_busFaulted) return;
                        uint tasValue = await ReadByteAsync(tasAddress);
                        if (_busFaulted) return;
                        SetLogicFlags(tasValue, 0);
                        await WriteByteAsync(tasAddress, (byte)(tasValue | 0x80));
                    }

                    return;
                }

                uint current = await ReadEaAsync(mode, reg, size);
                if (_busFaulted) return;
                SetLogicFlags(current, size);
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

    /// <summary>
    /// MOVEM memory → registers (0x4C80 word, 0x4CC0 long; UM §4.6). Mask bit n is Dn for n in 0–7 and
    /// A(n−8) above that, and the transfer walks the mask upwards, so memory is read in that same order.
    /// The width is 2 or 4 **bytes** for the address arithmetic; the shared effective-address helper wants
    /// a size *index* (1 = word, 2 = long). Conflating the two moved every register by half its width,
    /// which no flag or register comparison could see until the drift walked a stack into a return address.
    /// </summary>
    async ValueTask MovemToRegistersAsync(ushort op)
    {
        int size = (op & 0x0040) != 0 ? 2 : 1;   // the helper's vocabulary: 1 = word, 2 = long
        int bytes = size == 2 ? 4 : 2;
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        ushort mask = await FetchAsync();
        if (_busFaulted) return;

        // register-direct, and the predecrement form, do not exist for this direction
        if (mode is 0 or 1 or 4)
        {
            await EnterExceptionAsync(4, 0);
            return;
        }

        // (An)+ is the one form whose base register moves, and it moves by the transfer's total, which
        // the helper would get wrong (it advances by a single operand's width), so it is kept here.
        // The corpus pins the granularity: a word transfer advances An by two bytes even when its access
        // faults, a long transfer only once its four bytes have been read (a fault on the first word of
        // a long transfer leaves An where it was). That is two 2-byte bus cycles, not one 4-byte access.
        uint address = mode == 3 ? GetA(reg) : await EaAddressAsync(mode, reg, size);
        if (_busFaulted) return;

        // the transfer's own prefetch: MAME's microcode keeps the pipeline full while it walks the mask,
        // which is why a `MOVEM` fault saves a counter two words deeper than its word count (#76)
        await PrefetchAsync();
        if (_busFaulted) return;

        for (int bit = 0; bit < 16; bit++)
        {
            if ((mask & (1 << bit)) == 0) continue;
            if (mode == 3) SetA(reg, address + 2);

            uint value = size == 2 ? await ReadLongAsync(address) : await ReadWordAsync(address);
            if (_busFaulted) return;
            if (mode == 3 && size == 2) SetA(reg, address + 4);

            uint loaded = size == 2 ? value : (uint)(short)(value & 0xFFFF);
            if (bit < 8) _d[bit] = loaded;
            else SetA(bit - 8, loaded);

            address += (uint)bytes;
        }

        if (mode == 3) SetA(reg, address);
    }

    /// <summary>
    /// MOVEM registers → memory (0x4880 word, 0x48C0 long; UM §4.6). The predecrement form stores the
    /// highest register at the highest address and D0 last, at the lowest — so memory reads upwards in
    /// register order while the base decrements once per store. Every other form stores upwards in mask
    /// order, D0 first, and leaves the base register alone unless it is (An)+. The width is in bytes, as
    /// in the other direction.
    ///
    /// The predecrement form's mask is **reversed**: bit n selects register 15−n, not register n. That is
    /// not a typo and not this core's invention — it is how the 68000 encodes the list for −(An)
    /// (the assembler writes `movem.l d2-d7/a2-a6,-(sp)` as 0x48E7 0x3F3E, the bit-reversal of the
    /// 0x7CFC it writes for the same list in the other modes), and it is what makes the mnemonic's list
    /// mean the same thing in both directions: a save of that list and a restore of 0x7CFC agree.
    /// EmuTOS's `_memmove` relies on it, and 528 of the corpus's clean predecrement tests pin it. Read
    /// straight, the save lands one register off and `_memmove` restores a clobbered A6 — which is
    /// exactly how this core's boot died inside `doprintf`.
    /// </summary>
    async ValueTask MovemToMemoryAsync(ushort op)
    {
        int size = (op & 0x0040) != 0 ? 2 : 1;
        int bytes = size == 2 ? 4 : 2;
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        ushort mask = await FetchAsync();
        if (_busFaulted) return;

        // register-direct, and the PC-relative and immediate forms, are not in this direction
        if (mode is 0 or 1 || (mode == 7 && reg >= 2))
        {
            await EnterExceptionAsync(4, 0);
            return;
        }

        bool predecrement = mode == 4;
        uint address = mode is 3 or 4 ? GetA(reg) : await EaAddressAsync(mode, reg, size);
        if (_busFaulted) return;

        // the transfer's own prefetch, as in the other direction (#76)
        await PrefetchAsync();
        if (_busFaulted) return;

        if (predecrement)
        {
            // Bit upwards (the address travels downwards, so the first store is the highest address) and
            // the register reversed: bit 0 is A7, bit 15 is D0.
            for (int bit = 0; bit < 16; bit++)
            {
                if ((mask & (1 << bit)) == 0) continue;

                // A long store into -(An) steps down a word at a time, so an odd base faults on the low
                // word at A − 2 rather than on the long at A − 4 — the same rule the plain MOVE's store
                // follows (FINDINGS row 44). The corpus measures it: the frame's access address is A − 2.
                if (size == 2 && (address & 1) != 0)
                {
                    int faulting = 15 - bit;
                    ushort low = (ushort)(faulting < 8 ? _d[faulting] : GetA(faulting - 8));
                    await WriteWordAsync(address - 2, low);
                    if (_busFaulted) return;
                }

                address -= (uint)bytes;
                await StoreMovemAsync(address, 15 - bit, size);
                if (_busFaulted) return;
            }

            SetA(reg, address);
        }
        else
        {
            for (int bit = 0; bit < 16; bit++)
            {
                if ((mask & (1 << bit)) == 0) continue;
                if (mode == 3) SetA(reg, address + 2);

                await StoreMovemAsync(address, bit, size);
                if (_busFaulted) return;
                if (mode == 3 && size == 2) SetA(reg, address + 4);

                address += (uint)bytes;
            }

            if (mode == 3) SetA(reg, address);
        }

    }

    async ValueTask StoreMovemAsync(uint address, int bit, int size)
    {
        uint value = bit < 8 ? _d[bit] : GetA(bit - 8);
        if (size == 2) await WriteLongAsync(address, value);
        else await WriteWordAsync(address, (ushort)value);
    }

    async ValueTask MiscAsync(ushort op)
    {
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;

        switch (op)
        {
            case 0x4E71:   // NOP
                return;

            case 0x4E70:   // RESET
                if (!Supervisor)
                {
                    await EnterExceptionAsync(8, 0);
                    return;
                }

                return;

            case 0x4E73:   // RTE
                await RteAsync();
                return;

            case 0x4E75:   // RTS
            {
                _pc = await ReadLongAsync(A7);
                if (_busFaulted)
                {
                    await ChargeAbortedReadAsync();
                    return;
                }

                A7 += 4;
                await RefillAsync(_pc);

                // an odd return address aborts the refill's first read: the state's own 4 is the
                // return's to charge (1 237 records of the file were exactly 4 short)
                await ChargeAbortedReadAsync();
                return;
            }

            case 0x4E76:   // TRAPV
                if ((_sr & FlagV) != 0) await EnterExceptionAsync(7, 0);
                return;

            case 0x4E77:   // RTR
            {
                ushort status = await ReadWordAsync(A7);
                if (_busFaulted)
                {
                    await ChargeAbortedReadAsync();
                    return;
                }

                A7 += 2;
                _sr = (ushort)((_sr & 0xFF00) | (status & 0xFF));
                _pc = await ReadLongAsync(A7);
                if (_busFaulted)
                {
                    await ChargeAbortedReadAsync();
                    return;
                }

                A7 += 4;
                await RefillAsync(_pc);
                await ChargeAbortedReadAsync();
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
                // the machine stops *inside* the instruction: MAME's `aaa01` cancels the closing
                // prefetch (`m_au = m_au - 2`, twice), so a stopped CPU's counter is left on the STOP
                // *opcode's* address (1 230 records). The single-step seam reports that (#76); the
                // machine's own loop keeps the successor, which is where an `RTE` must resume.
                _flowTransferred = true;
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
            uint previous = GetA(reg);      // latched before A7 moves: `LINK A7,#` is the same register
            uint stack = A7 - 4;
            A7 = stack;
            await WriteLongAsync(stack, previous);
            SetA(reg, stack);
            A7 += immediate;
            return;
        }

        if (IsScriptedUnlink(op))   // UNLINK
        {
            // scripted (M68000.Scripts.cs): unlk1..unlk4, with the frame's saved counter taken from the
            // boundary's word and the stack pointer committed after both reads. An odd An therefore enters
            // the address error with the stack pointer untouched and saves `opcode + 4` — which is the
            // 1 115 records the file was red on, in both columns
            await UnlinkAsync(op);
            return;
        }

        if ((op & 0xFFF0) == 0x4E60)   // MOVE An,USP (bit 3 clear) / MOVE USP,An (bit 3 set)
        {
            if (!Supervisor)
            {
                await EnterExceptionAsync(8, 0);
                return;
            }

            if ((op & 0x0008) != 0)
            {
                SetA(op & 0x7, _usp);
            }
            else
            {
                _usp = GetA(op & 0x7);
            }

            return;
        }

        if (IsScriptedJsr(op))   // JSR
        {
            // scripted (M68000.Scripts.cs): the seven `jsr_*` scripts — the target's read before the push,
            // the shape's own leading idle run, the free extension word, and a frame that saves the counter
            // past the last extension word
            await JumpSubroutineAsync(op);
            return;
        }

        if (IsScriptedJump(op))   // JMP
        {
            // scripted (M68000.Scripts.cs): the seven `jmp_*` scripts. The target's read is `bbci3` — not a
            // fetch state — so an odd target's frame saves `opcode + 2` on every shape (911 records), and
            // the shape's idle run is charged here because a jump never runs the closing fetch that spends
            // `_own` (which is why the clock column was short by 2/4/6/8 per shape before the port)
            await JumpAsync(op);
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
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

        A7 += 2;
        uint pc = await ReadLongAsync(A7);
        if (_busFaulted)
        {
            await ChargeAbortedReadAsync();
            return;
        }

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
        await RefillAsync(_pc);

        // an odd target aborts the refill's first read, and the aborting state's own 4 is the return's
        await ChargeAbortedReadAsync();
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
                    // the counter is written *after* the instruction's last fetch: the microcode's
                    // `dbcc3` adds −1 into the ALU and only `dbcc4`/`dbcc5` write it back, so a branch
                    // whose target is odd enters the address error with the register untouched (632
                    // records of the DBcc file)
                    if (counter != 0xFFFF)
                    {
                        await ClocksAsync(2);                       // a taken DBcc (M68000.Timing.cs)
                        short displacement = (short)await FetchAsync();
                        uint target = _pc - 2 + (uint)displacement;
                        await RefillAsync(target);
                        if (_busFaulted) return;
                        _d[reg] = (_d[reg] & 0xFFFF0000) | counter;
                        return;
                    }

                    await ClocksAsync(4);                           // a falling-through DBcc
                    await FetchAsync();   // the displacement is consumed either way
                    if (_busFaulted) return;
                    _d[reg] = (_d[reg] & 0xFFFF0000) | counter;
                    return;
                }

                // the condition is true: no decrement at all, and the counter is left as it was
                await ClocksAsync(4);
                await FetchAsync();
                return;
            }

            bool set = Condition(condition);
            if (set && mode == 0)
            {
                _own = 2;                                // Scc Dn: 4 false, 6 true (M68000.Timing.cs)
            }

            await WriteEaAsync(mode, reg, 0, set ? 0xFFu : 0x00u);
            if (_busFaulted) return;
            return;
        }

        // the long quicks are scripts (M68000.Scripts.cs): raql1/maql1 and #79's tails
        if (IsScriptedQuickLong(op))
        {
            await QuickLongAsync(op);
            return;
        }

        if (mode == 1)
        {
            // an address register is never affected by flags, and the whole 32-bit register takes part
            // whatever the size field says (UM: byte and word are the same as long here)
            SetA(reg, GetA(reg) + (uint)((op & 0x0100) != 0 ? -data : data));
            return;
        }

        if (mode == 0)
        {
            uint inRegister = _d[reg] & SizeMask(size);
            uint registerResult;
            if ((op & 0x0100) != 0)
            {
                registerResult = (inRegister - (uint)data) & SizeMask(size);
                SetSubFlags((uint)data, inRegister, registerResult, size);
            }
            else
            {
                registerResult = (inRegister + (uint)data) & SizeMask(size);
                SetAddFlags((uint)data, inRegister, registerResult, size);
            }

            _d[reg] = (_d[reg] & ~SizeMask(size)) | registerResult;
            return;
        }

        // The memory form is the one read-modify-write whose operand can carry extension words, so its
        // address is resolved once here and the store goes to that address: resolving it a second time
        // (as ReadEaAsync + WriteEaAsync would) fetched the *next instruction's* opcode word as a second
        // displacement, wrote to it and skipped a word — which is how EmuTOS's VBL handler died on its
        // first `subq.w #1,_vblsem.w`, with an address error at 0x004EB9 (the following `jsr` word).
        uint addAddress = await EaAddressAsync(mode, reg, size);
        if (_busFaulted) return;

        uint current = await ReadAtAsync(addAddress, size);
        if (_busFaulted) return;
        uint result;
        if ((op & 0x0100) != 0)
        {
            result = (current - (uint)data) & SizeMask(size);
            await WriteAtAsync(addAddress, size, result);
            if (_busFaulted) return;
            SetSubFlags((uint)data, current, result, size);
        }
        else
        {
            result = (current + (uint)data) & SizeMask(size);
            await WriteAtAsync(addAddress, size, result);
            if (_busFaulted) return;
            SetAddFlags((uint)data, current, result, size);
        }

    }

    // ---- line 6: branches ------------------------------------------------------------------

    async ValueTask Line6Async(ushort op)
    {
        int condition = (op >> 8) & 0xF;

        if (IsScriptedBranch(op))   // BSR
        {
            // scripted (M68000.Scripts.cs): bsrw1..bsrw3 take the displacement out of the queue itself, so
            // the word form costs no read and the return address is latched after the pop (1 271 records
            // wanted the target in the frame, and the six `BSR.w` records wanted the free extension word)
            await BranchSubroutineAsync(op);
            return;
        }

        int displacement = op & 0xFF;
        long opcodeAddress = _pc - 2;

        if (displacement == 0)
        {
            displacement = (short)await FetchAsync();
        }
        else if (displacement >= 0x80)
        {
            // the 68000 has no 32-bit branch form: 0xFF is a displacement of −1, not an escape (adding
            // the 68020's long form here turned every "branch back one byte" into a four-byte read)
            displacement -= 0x100;
        }

        if (_busFaulted) return;

        // the target is relative to the address after the opcode word (UM §2.2's branch rule)
        long target = opcodeAddress + 2 + displacement;

        if (Condition(condition))
        {
            await ClocksAsync(2);                                   // taken: 10 clocks, 2 of them its own
            await RefillAsync((uint)target);
        }
        else
        {
            await ClocksAsync(4);                                   // not taken: 8 clocks, 4 of them its own
        }

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

        if (((op >> 6) & 0x4) != 0 && size != 3)   // OR Dn,<ea> (opmode 100)
        {
            if (size == 2)
            {
                // the long forms are scripted (M68000.Scripts.cs): the shape's read block and the
                // RMW tail, in the microcode's order, charging their own clocks
                await AluLongAsync(op);
                return;
            }

            uint value = _d[(op >> 9) & 0x7] & SizeMask(size);
            uint address = await EaAddressAsync(mode, reg, size);
            if (_busFaulted) return;
            uint current = await ReadAtAsync(address, size);
            if (_busFaulted) return;
            uint result = current | value;
            await WriteAtAsync(address, size, result);
            if (_busFaulted) return;
            SetLogicFlags(result, size);
            return;
        }

        if (size == 3)   // DIVU / DIVS
        {
            await DivideAsync(op);
            return;
        }

        if (size == 2)
        {
            await AluLongAsync(op);                // or.l <ea>, Dn — scripted
            return;
        }

        uint source = await ReadEaAsync(mode, reg, size);
        if (_busFaulted) return;
        uint destination = _d[(op >> 9) & 0x7] & SizeMask(size);
        uint combined = destination | source;
        _d[(op >> 9) & 0x7] = (_d[(op >> 9) & 0x7] & ~SizeMask(size)) | (combined & SizeMask(size));
        SetLogicFlags(combined, size);
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
                // an overflow leaves the register alone and touches V and C only: N, Z and X are the
                // instruction's own undefined case and the corpus keeps whatever they were
                _sr = (ushort)((_sr | FlagV) & ~FlagC);
                _own = 12;                        // the short exit (LENGTH.md §5)
                return;
            }

            _own = 122 + 2 * BitLength((uint)Math.Abs(quotient));
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
                _sr = (ushort)((_sr | FlagV) & ~FlagC);
                _own = 6;                         // the short exit (LENGTH.md §5)
                return;
            }

            _own = 76 + 2 * BitLength(quotient);
            _d[(op >> 9) & 0x7] = (remainder << 16) | quotient;
        }

        SetLogicFlags(_d[(op >> 9) & 0x7] & 0xFFFF, 1);
    }

    async ValueTask Line9Async(ushort op)   // SUB / SUBX / SUBA
    {
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        int size = (op >> 6) & 0x3;

        if ((op & 0xF130) == 0x9100 && size != 3)   // SUBX
        {
            if (size == 2)
            {
                await ExchangeLongAsync(op, subtract: true);    // subx.l — scripted
                return;
            }

            await ExchangeArithmeticAsync(op, subtract: true);
            return;
        }

        if (((op >> 6) & 0x4) != 0 && size != 3)   // SUB Dn,<ea>
        {
            if (size == 2)
            {
                await AluLongAsync(op);            // sub.l Dn,<ea> — scripted
                return;
            }

            uint value = _d[(op >> 9) & 0x7] & SizeMask(size);
            uint address = await EaAddressAsync(mode, reg, size);
            if (_busFaulted) return;
            uint current = await ReadAtAsync(address, size);
            if (_busFaulted) return;
            uint result = (current - value) & SizeMask(size);
            await WriteAtAsync(address, size, result);
            if (_busFaulted) return;
            SetSubFlags(value, current, result, size);
            return;
        }

        if (size == 3)   // SUBA
        {
            if (((op >> 6) & 0x7) == 7)
            {
                await AluLongAsync(op);            // suba.l <ea>,An — scripted
                return;
            }

            uint value = (op & 0x0100) != 0 ? await ReadEaAsync(mode, reg, 2) : (uint)(short)(await ReadEaAsync(mode, reg, 1) & 0xFFFF);
            SetA((op >> 9) & 0x7, GetA((op >> 9) & 0x7) - value);
            return;
        }

        if (size == 2)
        {
            await AluLongAsync(op);                // sub.l <ea>,Dn — scripted
            return;
        }

        uint source = await ReadEaAsync(mode, reg, size);
        if (_busFaulted) return;
        uint destination = _d[(op >> 9) & 0x7] & SizeMask(size);
        uint difference = (destination - source) & SizeMask(size);
        _d[(op >> 9) & 0x7] = (_d[(op >> 9) & 0x7] & ~SizeMask(size)) | difference;
        SetSubFlags(source, destination, difference, size);
    }

    async ValueTask ExchangeArithmeticAsync(ushort op, bool subtract)
    {
        int size = (op >> 6) & 0x3;
        int sourceReg = op & 0x7;
        int destinationReg = (op >> 9) & 0x7;
        bool predecrement = (op & 0x0008) != 0;

        // The memory form's own idle state (`asxw1`/`asxb1`, 2 clocks) — the register form has none
        // and is exact without it. Only the byte and word sizes reach this method: the long form is
        // the `asxl` block (M68000.Scripts.cs, ExchangeLongAsync), whose single idle sits with it.
        if (predecrement)
        {
            await IdleAsync();
        }

        // ADDX/SUBX issue their prefetch before the access even with no idle clock to spend (#76: their
        // memory forms charge no own clocks, and MAME's frame still saves one prefetch deeper)
        await PrefetchAsync();
        if (_busFaulted) return;

        uint source;
        uint destination;

        if (predecrement)
        {
            source = await PredecrementAsync(sourceReg, size);

            // The source's read decides the rest: if it faulted, the destination register is untouched.
            // The corpus pins it — on an odd address the CPU never reaches the destination's own
            // predecrement, so `ADDX.w -(A2), -(A6)` leaves A6 where it was (FINDINGS row 39).
            if (_busFaulted) return;

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

        // the extend bit is read inside the flag setter, from the status as it stands before the
        // flags move — passing `source + extended` here would count it twice
        if (subtract) SetExchangeFlags(true, source, destination, result, size);
        else SetExchangeFlags(false, source, destination, result, size);

        if (predecrement)
        {
            await WriteAtAsync(GetA(destinationReg), size, result);
            if (_busFaulted) return;
        }
        else
        {
            _d[destinationReg] = (_d[destinationReg] & ~SizeMask(size)) | result;
        }

    }

    /// <summary>
    /// <c>-(An)</c>: the register steps down <em>before</em> the read, one access at a time. A long
    /// steps down a word at a time and reads the low word first (at <c>A − 2</c>), which is what the
    /// corpus pins: an odd <c>A</c> faults on that first word with the register down 2, not 4
    /// (FINDINGS row 40).
    /// <para>
    /// The long form's <b>commit</b> is the ADDX rule, not the word rule: <c>asxl3</c> writes the
    /// register only once the low word has come back, so an odd <c>A</c> leaves it where it stood.
    /// Since <c>asxl</c> is a script, <see cref="ExchangeLongAsync"/> is the call site that does this
    /// in the microcode's order; the branch here keeps the same rule for any long that still comes
    /// through the EA machine.
    /// </para>
    /// </summary>
    async ValueTask<uint> PredecrementAsync(int reg, int size)
    {
        if (size == 2)
        {
            uint start = GetA(reg);

            ushort low = await ReadWordAsync(start - 2);
            if (_busFaulted) return 0;

            SetA(reg, start - 4);                          // asxl3 — the commit, after the low word
            ushort high = await ReadWordAsync(start - 4);
            if (_busFaulted) return 0;

            return ((uint)high << 16) | low;
        }

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
            // a byte steps the register by one, unless it is A7, which always moves a whole word
            uint delta = (uint)(size == 0 && (op & 0x7) == 7 ? 2 : SizeBytes(size));
            uint sourceAddress = GetA(reg);
            SetA(reg, sourceAddress + delta);
            uint source = await ReadAtAsync(sourceAddress, size);
            if (_busFaulted) return;

            // the destination has its own step: a byte moves A7 by a word, every other register by one
            int destinationReg = (op >> 9) & 0x7;
            uint destinationDelta = (uint)(size == 0 && destinationReg == 7 ? 2 : SizeBytes(size));
            uint destinationAddress = GetA(destinationReg);
            SetA(destinationReg, destinationAddress + destinationDelta);
            uint destination = await ReadAtAsync(destinationAddress, size);
            if (_busFaulted) return;
            SetCompareFlags(source, destination, (destination - source) & SizeMask(size), size);
            return;
        }

        if ((op & 0x0100) != 0 && size != 3)   // EOR Dn,<ea>
        {
            if (size == 2)
            {
                await AluLongAsync(op);        // eor.l Dn,<ea> — scripted
                return;
            }

            uint value = _d[(op >> 9) & 0x7] & SizeMask(size);

            // EOR Dn,Dm — mode 000 is a *register* destination, not an address. Without this case the
            // EA helper answered "no address" (0) and the instruction read and wrote low memory
            // instead: EmuTOS's monitor-detect loop (`eor.b d0,d1` over two GPIP reads) set its flags
            // from whatever byte sits at address 0, `blt` never stopped branching, and the boot span
            // for ever inside detect_monitor_change at frame 258.
            if (mode == 0)
            {
                uint registerResult = (_d[reg] ^ value) & SizeMask(size);
                _d[reg] = (_d[reg] & ~SizeMask(size)) | registerResult;
                SetLogicFlags(registerResult, size);
                return;
            }

            uint address = await EaAddressAsync(mode, reg, size);
            if (_busFaulted) return;
            uint current = await ReadAtAsync(address, size);
            if (_busFaulted) return;
            uint result = current ^ value;
            await WriteAtAsync(address, size, result);
            if (_busFaulted) return;
            SetLogicFlags(result, size);
            return;
        }

        if (size == 3)   // CMPA
        {
            if (((op >> 6) & 0x7) == 7)
            {
                await AluLongAsync(op);        // cmpa.l <ea>,An — scripted
                return;
            }

            uint value = (op & 0x0100) != 0 ? await ReadEaAsync(mode, reg, 2) : (uint)(short)(await ReadEaAsync(mode, reg, 1) & 0xFFFF);
            if (_busFaulted) return;
            uint destination = GetA((op >> 9) & 0x7);
            SetCompareFlags(value, destination, destination - value, 2);
            return;
        }

        if (size == 2)
        {
            await AluLongAsync(op);            // cmp.l <ea>,Dn — scripted
            return;
        }

        uint operand = await ReadEaAsync(mode, reg, size);
        if (_busFaulted) return;
        uint current2 = _d[(op >> 9) & 0x7] & SizeMask(size);
        SetCompareFlags(operand, current2, (current2 - operand) & SizeMask(size), size);
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
            return;
        }

        if (((op >> 6) & 0x4) != 0 && size != 3)   // AND Dn,<ea>
        {
            if (size == 2)
            {
                await AluLongAsync(op);            // and.l Dn,<ea> — scripted
                return;
            }

            uint value = _d[(op >> 9) & 0x7] & SizeMask(size);
            uint address = await EaAddressAsync(mode, reg, size);
            if (_busFaulted) return;
            uint current = await ReadAtAsync(address, size);
            if (_busFaulted) return;
            uint result = current & value;
            await WriteAtAsync(address, size, result);
            if (_busFaulted) return;
            SetLogicFlags(result, size);
            return;
        }

        if (size == 3)   // MULU / MULS
        {
            bool signed = (op & 0x0100) != 0;
            uint source = await ReadEaAsync(mode, reg, 1);
            if (_busFaulted) return;
            uint destination = _d[(op >> 9) & 0x7] & 0xFFFF;

            _d[(op >> 9) & 0x7] = signed
                ? (uint)((short)destination * (short)source)
                : destination * source;

            // MULU/MULS: 34 clocks plus 2 for every 1 bit in the operand's magnitude — the corpus's own
            // trailing run is 34, 36, … 62/64 for MULU and MULS (LENGTH.md §5)
            uint magnitude = signed ? (uint)Math.Abs((short)source) : source;
            _own = 34 + 2 * System.Numerics.BitOperations.PopCount(magnitude);

            SetLogicFlags(_d[(op >> 9) & 0x7], 2);
            return;
        }

        if (size == 2)
        {
            await AluLongAsync(op);                // and.l <ea>,Dn — scripted
            return;
        }

        uint operand = await ReadEaAsync(mode, reg, size);
        if (_busFaulted) return;
        uint destination2 = _d[(op >> 9) & 0x7] & SizeMask(size);
        uint combined = destination2 & operand;
        _d[(op >> 9) & 0x7] = (_d[(op >> 9) & 0x7] & ~SizeMask(size)) | combined;
        SetLogicFlags(combined, size);
    }

    /// <summary>
    /// The arithmetic all three packed-BCD instructions share (ABCD, SBCD, NBCD — UM §4.2): a binary
    /// add or subtract of the two bytes with X as the carry in, then a decimal adjust on each nibble.
    /// Written from the corpus, which pins the behaviour on operands that are *not* valid BCD (0xBD +
    /// 0xBD) — the adjust follows the binary sum's carry through both nibbles rather than adding the
    /// decimal digits, so a digit sum of 26 still lands on a legal-looking byte. The flags are the
    /// other half of the same finding: X and C take the decimal carry/borrow, V takes FX68K's overflow
    /// of the *adjusted* high nibble (a nibble that stayed under 8 while the binary byte went negative),
    /// and Z is the one flag a BCD instruction never sets — it only clears, so a multi-digit chain
    /// keeps the Z of its first digit.
    /// </summary>
    static void BcdCore(uint source, uint destination, bool subtract, bool extend, out uint result, out bool carry, out bool overflow)
    {
        int extension = extend ? 1 : 0;
        int sourceByte = (int)(source & 0xFF);
        int destinationByte = (int)(destination & 0xFF);
        int binary;
        bool lowAdjust;
        int highNibble;

        if (subtract)
        {
            bool borrow = sourceByte + extension > destinationByte;
            binary = (destinationByte - sourceByte - extension) & 0xFF;
            lowAdjust = (sourceByte & 0xF) + extension > (destinationByte & 0xF);
            int adjusted = (binary - (lowAdjust ? 6 : 0)) & 0x1FF;
            highNibble = ((adjusted >> 4) - (borrow ? 6 : 0)) & 0xFF;
            overflow = (highNibble & 8) == 0 && (binary & 0x80) != 0;
            result = (uint)(((highNibble & 0xF) << 4) + (adjusted & 0xF));
            carry = (highNibble & 0x10) != 0 || borrow;
        }
        else
        {
            int sum = sourceByte + destinationByte + extension;
            bool survived = sum > 0xFF;
            binary = sum & 0xFF;
            lowAdjust = (sourceByte & 0xF) + extension + (destinationByte & 0xF) > 0xF || (binary & 0xF) > 9;
            int adjusted = (binary + (lowAdjust ? 6 : 0)) & 0x1FF;
            bool highCarry = survived || (adjusted & 0xF0) > 0x90 || (adjusted & 0x100) != 0;
            highNibble = ((adjusted >> 4) + (highCarry ? 6 : 0)) & 0xFF;
            overflow = (highNibble & 8) != 0 && (binary & 0x80) == 0;
            result = (uint)(((highNibble & 0xF) << 4) + (adjusted & 0xF));
            carry = (highNibble & 0x10) != 0 || survived;
        }
    }

    /// <summary>The BCD flags: C and X the decimal carry, V the adjusted-nibble overflow, N the result's
    /// bit 7, Z cleared only (never set) by a non-zero result.</summary>
    void SetBcdFlags(uint result, bool carry, bool overflow)
    {
        _sr = (ushort)(carry ? _sr | (FlagC | FlagX) : _sr & ~(FlagC | FlagX));
        _sr = (ushort)(overflow ? _sr | FlagV : _sr & ~FlagV);
        _sr = (ushort)((result & 0x80) != 0 ? _sr | FlagN : _sr & ~FlagN);
        if (result != 0)
        {
            _sr = (ushort)(_sr & ~FlagZ);
        }
    }

    /// <summary>
    /// ABCD / SBCD — packed BCD between two bytes, with the extend bit as the carry in; -(Ay),-(Ax)
    /// for the memory form (both bytes read before either is written, the source first).
    /// </summary>
    async ValueTask BcdAsync(ushort op, bool subtract)
    {
        int sourceReg = op & 0x7;
        int destinationReg = (op >> 9) & 0x7;
        bool predecrement = (op & 0x0008) != 0;

        uint source;
        uint destination;

        if (predecrement)
        {
            source = await PredecrementAsync(sourceReg, 0);
            if (_busFaulted) return;
            destination = await PredecrementAsync(destinationReg, 0);
            if (_busFaulted) return;
        }
        else
        {
            source = _d[sourceReg] & 0xFF;
            destination = _d[destinationReg] & 0xFF;
        }

        BcdCore(source, destination, subtract, (_sr & FlagX) != 0, out uint result, out bool carry, out bool overflow);
        SetBcdFlags(result, carry, overflow);

        if (predecrement)
        {
            await WriteAtAsync(GetA(destinationReg), 0, result);
            if (_busFaulted) return;
        }
        else
        {
            _d[destinationReg] = (_d[destinationReg] & 0xFFFFFF00) | result;
        }

    }

    async ValueTask LineDAsync(ushort op)   // ADD / ADDX / ADDA
    {
        int mode = (op >> 3) & 0x7;
        int reg = op & 0x7;
        int size = (op >> 6) & 0x3;

        if ((op & 0xF130) == 0xD100 && size != 3)   // ADDX
        {
            if (size == 2)
            {
                await ExchangeLongAsync(op, subtract: false);   // addx.l — scripted
                return;
            }

            await ExchangeArithmeticAsync(op, subtract: false);
            return;
        }

        if (((op >> 6) & 0x4) != 0 && size != 3)   // ADD Dn,<ea>
        {
            if (size == 2)
            {
                await AluLongAsync(op);            // add.l Dn,<ea> — scripted
                return;
            }

            uint value = _d[(op >> 9) & 0x7] & SizeMask(size);
            uint address = await EaAddressAsync(mode, reg, size);
            if (_busFaulted) return;
            uint current = await ReadAtAsync(address, size);
            if (_busFaulted) return;
            uint result = (current + value) & SizeMask(size);
            await WriteAtAsync(address, size, result);
            if (_busFaulted) return;
            SetAddFlags(value, current, result, size);
            return;
        }

        if (size == 3)   // ADDA
        {
            if (((op >> 6) & 0x7) == 7)
            {
                await AluLongAsync(op);            // adda.l <ea>,An — scripted
                return;
            }

            uint value = (op & 0x0100) != 0 ? await ReadEaAsync(mode, reg, 2) : (uint)(short)(await ReadEaAsync(mode, reg, 1) & 0xFFFF);
            SetA((op >> 9) & 0x7, GetA((op >> 9) & 0x7) + value);
            return;
        }

        if (size == 2)
        {
            await AluLongAsync(op);                // add.l <ea>,Dn — scripted
            return;
        }

        uint source = await ReadEaAsync(mode, reg, size);
        if (_busFaulted) return;
        uint destination = _d[(op >> 9) & 0x7] & SizeMask(size);
        uint sum = (destination + source) & SizeMask(size);
        _d[(op >> 9) & 0x7] = (_d[(op >> 9) & 0x7] & ~SizeMask(size)) | sum;
        SetAddFlags(source, destination, sum, size);
    }

    // ---- line E: shifts and rotates --------------------------------------------------------

    async ValueTask LineEAsync(ushort op)
    {
        int size = (op >> 6) & 0x3;
        bool left = (op & 0x0100) != 0;

        if (size != 3)
        {
            // a register shift: bits 5-3 are 000 (count in the opcode) or 100 (count in a data
            // register), and the operation is bits 4-3
            int reg = op & 0x7;
            bool registerCount = (op & 0x0020) != 0;
            int kind = (op >> 3) & 0x3;

            int count = registerCount ? (int)(_d[(op >> 9) & 0x7] & 63) : ((op >> 9) & 0x7);
            if (!registerCount && count == 0)
            {
                count = 8;
            }

            uint value = _d[reg] & SizeMask(size);
            uint result = Shift(value, count, size, kind, left);
            _d[reg] = (_d[reg] & ~SizeMask(size)) | (result & SizeMask(size));
            return;
        }

        // a memory shift is one word, by one: bits 5-3 are the addressing mode and bits 10-9 the kind
        int mode = (op >> 3) & 0x7;
        int memoryKind = (op >> 9) & 0x3;
        await MemoryShiftAsync(mode, op & 0x7, memoryKind, left);
    }

    /// <summary>
    /// One shift or rotate, <paramref name="kind"/> 0 = arithmetic, 1 = logical, 2 = rotate through
    /// extend, 3 = rotate. <paramref name="left"/> is the direction bit.
    ///
    /// <para>
    /// The flag rules are the ones the corpus pins down — and they are not the manual's "a zero count
    /// touches nothing":
    /// </para>
    /// <list type="bullet">
    /// <item>N and Z are always read off the result, a zero count included.</item>
    /// <item>The shifting kinds move the <em>last</em> bit out into C <i>and</i> X, and that bit is decided
    /// by the width, not by the loop: a count past the operand shifts that bit out and nothing else, so C
    /// and X come out <b>zero</b> (ASR.b by 17 on `0x92` clears both — the loop would keep the sign bit).</item>
    /// <item>The rotate-through-extend kinds move it into C and X alike, the plain rotates into C alone
    /// (X untouched).</item>
    /// <item>V is set only by a left arithmetic shift whose sign changed at some step.</item>
    /// <item>A zero count clears C for every kind but the through-extend ones, which leave C and X as
    /// they were, and never writes X.</item>
    /// </list>
    /// </summary>
    uint Shift(uint value, int count, int size, int kind, bool left)
    {
        int bits = SizeBits(size);
        uint mask = SizeMask(size);
        uint sign = (uint)SignBit(size);
        uint operand = value & mask;
        uint result = operand;
        bool extend = (_sr & FlagX) != 0;
        bool carry = false;
        bool overflow = false;

        count &= 63;

        // the bit a shift kind moves out last — empty when the count runs past the operand's width
        uint lastOut = 0;
        if (kind <= 1 && count != 0 && count <= bits)
        {
            lastOut = left ? (operand >> (bits - count)) & 1u : (operand >> (count - 1)) & 1u;
        }

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

                case 1:   // LSL / LSR (V is not affected by a logical shift)
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
                        bool outBit = (result & sign) != 0;
                        result = ((result << 1) & mask) | (extend ? 1u : 0u);
                        extend = outBit;
                        carry = outBit;
                    }
                    else
                    {
                        bool outBit = (result & 1) != 0;
                        result = (result >> 1) | (extend ? sign : 0u);
                        extend = outBit;
                        carry = outBit;
                    }

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

        // C: the shifting kinds take it from the width rule above; a rotate from the last bit the loop
        // turned; through-extend moves C with X, and leaves both alone when nothing shifts.
        if (kind <= 1)
        {
            _sr = (ushort)(lastOut != 0 ? _sr | FlagC : _sr & ~FlagC);
            if (count != 0)
            {
                _sr = (ushort)(lastOut != 0 ? _sr | FlagX : _sr & ~FlagX);   // a shift moves the bit into X too
            }
        }
        else if (kind == 2)
        {
            // through extend: C takes the extend bit's value always (at a zero count that is a copy of
            // X), and X itself is written only when something actually turns
            _sr = (ushort)(extend ? _sr | FlagC : _sr & ~FlagC);
            if (count != 0)
            {
                _sr = (ushort)(extend ? _sr | FlagX : _sr & ~FlagX);
            }
        }
        else
        {
            _sr = (ushort)(carry ? _sr | FlagC : _sr & ~FlagC);
        }

        _sr = (ushort)((result & mask) == 0 ? _sr | FlagZ : _sr & ~FlagZ);
        _sr = (ushort)((result & sign) != 0 ? _sr | FlagN : _sr & ~FlagN);
        _sr = (ushort)(overflow ? _sr | FlagV : _sr & ~FlagV);

        return result & mask;
    }

    async ValueTask MemoryShiftAsync(int mode, int reg, int kind, bool left)
    {
        uint address = await EaAddressAsync(mode, reg, 1);
        uint value = await ReadWordAsync(address);
        if (_busFaulted) return;
        uint result = Shift(value, 1, 1, kind, left);
        await WriteWordAsync(address, (ushort)result);
    }
}
