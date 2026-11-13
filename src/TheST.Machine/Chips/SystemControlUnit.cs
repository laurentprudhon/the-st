using TheST.Stores;

namespace TheST.Machine.Chips;

/// <summary>
/// The System Control Unit (design/system-control-unit.md, ADR 0011, ADR 0014): the 8-phase slot
/// counter, the owner equation, the 4-word video fetch buffer, <c>DE</c>/<c>BLANK</c>, the chip-select
/// decode with <c>BERR</c>, <c>DTACK</c> and the <c>IPL0–IPL2</c> arbitration, plus the two stores.
///
/// Its two wake sources are its own counters (a line every 2 048 ticks, a group every 64 during the
/// window) and the bus (the CPU's assertion, a subscription). That is what the engine's several-wakes
/// rule exists for: a chip whose timeline is its own must still answer whoever writes to it.
/// </summary>
public sealed class SystemControlUnit : Chip
{
    // ---- the slot grid (design/system-control-unit.md §3) -----------------------------------
    /// <summary>One bus cycle: 16 ticks (500 ns) — one internal slot, then one external slot.</summary>
    public const int CycleTicks = 16;

    /// <summary>One slot: 8 ticks (250 ns).</summary>
    public const int SlotTicks = 8;

    /// <summary>The population: 2 banks × 512 × 512 × 16 bits = 1 MB (ADR 0014 §2).</summary>
    public const uint RamSize = 0x100000;

    /// <summary>One PAL line.</summary>
    public const int LineTicks = (int)Ticks.Line;

    /// <summary>Lines a frame's counter keeps (ADR 0001: 312.5 rounded up).</summary>
    public const int LinesPerFrame = Ticks.LinesPerFrameRounded;

    // ---- the display window (design/video.md §3) ------------------------------------------
    /// <summary>Cycles into the line at which <c>DE</c> is asserted (28 pixel clocks = 56 ticks).</summary>
    public const int DeAssertOffset = 56;

    /// <summary>The display area: 320 low-res pixels at 4 ticks each.</summary>
    public const int DisplayTicks = 320 * 4;

    /// <summary>One group is four words, i.e. four bus cycles.</summary>
    public const int GroupTicks = 4 * CycleTicks;

    /// <summary>The first vertically active line, and how many there are (video.md §5).</summary>
    public const int FirstActiveLine = 39;
    public const int ActiveLines = 200;

    /// <summary>Groups delivered on an active line: 1 280 dry ticks ÷ 64.</summary>
    public const int GroupsPerLine = DisplayTicks / GroupTicks;

    /// <summary>Vertical blanking starts here; <c>VBL</c> is asserted for 20 lines.</summary>
    public const int FirstBlankLine = FirstActiveLine + ActiveLines;

    const int Registers = 0;

    readonly Ram _ram;
    readonly Rom _rom;
    readonly Machine _machine;

    // the counter
    int _line;
    long _lineStart;
    int _groupIndex;

    // the beam
    bool _de;
    bool _blank;
    bool _vbl;
    bool _hbl;
    long _hblReleaseAt = -1;

    // the register file
    readonly byte[] _regs = new byte[0x2000];   // $FF8000-$FF9FFF, byte indexed
    bool _romAtZero = true;
    byte _memConfig = 0x04;                     // 1 MB: bank0 = 512K → code 1 at bits [3:2]

    // the bus
    long _assertionInstant = -1;
    long _answerInstant = -1;
    long _assertionSeq = -1;
    long _busSeenSeq = long.MinValue;

    // interrupts
    int _mfpLevel;
    int _resolvedLevel;

    public SystemControlUnit(Engine engine, int id, Machine machine, Ram ram, Rom rom)
        : base(engine, id, "SystemControlUnit")
    {
        _machine = machine;
        _ram = ram;
        _rom = rom;
        Reset();
    }

    // ---- counters, for the harness and the assertions --------------------------------------
    public long GroupDeliveries { get; private set; }
    public long DeRecords { get; private set; }
    public long DeFirings { get; private set; }
    public long LineAnchors { get; private set; }
    public long VblRecords { get; private set; }
    public long HblRecords { get; private set; }
    public long BlankRecords { get; private set; }
    public long RefreshAnchors { get; private set; }
    public long BusAnswers { get; private set; }
    public long BusFaults { get; private set; }
    public long RegisterWrites { get; private set; }
    public long Frames { get; private set; }

    public int CurrentLine => _line;
    public int CurrentGroupIndex => _groupIndex;
    public bool DisplayWindowOpen => _de;

    /// <summary>The line the beam is on at an instant — the piece of state Video needs from here.</summary>
    public int LineAt(long instant) => (int)((instant / LineTicks) % LinesPerFrame);

    public void Launch()
    {
        _ = Run();
    }

    async ValueTask Run()
    {
        BeginLine(0);

        while (true)
        {
            WakeLoops++;
            ConsumeBusRecords();

            while (NextEvent() <= Clock)
            {
                HandleEvent(Clock);
            }

            if (_answerInstant >= 0 && Clock >= _answerInstant)
            {
                // The cycle is answered at the instant this chip is actually awake, never at the
                // earliest slot it could have been: a record stamped before the batch being drained
                // is backdated, and the resume it fires lands in the past and never runs.
                PerformBusCycle(Clock);
                _answerInstant = -1;
                _assertionInstant = -1;
                _assertionSeq = -1;
            }

            long due = NextEvent();
            await WaitFor(_answerInstant >= 0 ? Math.Min(due, _answerInstant) : due, Wires.Bus, _busSeenSeq);
        }
    }

    /// <summary>
    /// Reads every bus record appended since the last look. A record on the address-high lane begins an
    /// exchange; the release lane ends one, so the two can never be confused.
    /// </summary>
    void ConsumeBusRecords()
    {
        ValueLog bus = Engine.Log(Wires.Bus);

        for (int index = bus.IndexAfter(_busSeenSeq); index >= 0 && index < bus.Count; index++)
        {
            Record record = bus[index];

            if (record.Mask == 0xFF00 && _answerInstant < 0)
            {
                _assertionInstant = record.Instant;

                // The exchange's four records are held by sequence number, never by index: retention
                // discards records from the oldest end, so an index captured now and used at the next
                // external slot names a different record once anything was trimmed in between (the
                // first symptom was a dead bus: three cycles started, no answer, the CPU suspended).
                _assertionSeq = record.Seq;
                _answerInstant = NextExternalSlot(record.Instant);
            }

            _busSeenSeq = record.Seq;
        }
    }

    /// <summary>
    /// Every instant the chip's own counters have an opinion about: the <c>DE</c> edges of the window,
    /// the group boundaries inside it, the end of the line, and the <c>HBL</c> pulse's release.
    /// </summary>
    long NextEvent()
    {
        long lineStart = _lineStart;
        long next = lineStart + LineTicks;

        if (_hbl && _hblReleaseAt >= Clock && _hblReleaseAt < next)
        {
            next = _hblReleaseAt;
        }

        bool active = _line >= FirstActiveLine && _line < FirstActiveLine + ActiveLines;
        if (!active)
        {
            return next;
        }

        long deAssert = lineStart + DeAssertOffset;
        long deRelease = deAssert + DisplayTicks;

        if (!_de)
        {
            // the window has not opened yet on this line, or has already closed: either way the
            // only remaining event is the end of the line
            return Clock <= deAssert ? Math.Min(next, deAssert) : next;
        }

        long group = deAssert + (long)_groupIndex * GroupTicks;
        if (_groupIndex < GroupsPerLine && group >= Clock && group < next)
        {
            next = group;
        }

        if (deRelease >= Clock && deRelease < next)
        {
            next = deRelease;
        }

        return next;
    }

    public long Events { get; private set; }
    public long WakeLoops { get; private set; }

    void HandleEvent(long instant)
    {
        Events++;

        if (_hbl && _hblReleaseAt == instant)
        {
            _hbl = false;
            _hblReleaseAt = -1;
            RecomputeIpl(instant);
            return;
        }

        long lineStart = _lineStart;
        bool active = _line >= FirstActiveLine && _line < FirstActiveLine + ActiveLines;

        if (active && !_de && instant == lineStart + DeAssertOffset)
        {
            _de = true;
            _groupIndex = 0;
            Write(instant, Wires.De, 0x0001, 0x0001);
            DeRecords++;
            DeliverGroup(instant);
            return;
        }

        if (_de && _groupIndex < GroupsPerLine && instant == lineStart + DeAssertOffset + (long)_groupIndex * GroupTicks)
        {
            DeliverGroup(instant);
            return;
        }

        if (_de && instant == lineStart + DeAssertOffset + DisplayTicks)
        {
            _de = false;
            Write(instant, Wires.De, 0x0001, 0x0000);
            DeRecords++;
            return;
        }

        if (instant >= lineStart + LineTicks)
        {
            EndLine(instant);
            return;
        }
    }

    void BeginLine(long instant)
    {
        Anchor(Channels.Slot(Registers, "line"), (ushort)_line);
        LineAnchors++;

        if (_line == 0)
        {
            if (_vbl)
            {
                _vbl = false;
                RecomputeIpl(instant);
            }

            if (_blank)
            {
                _blank = false;
                Write(instant, Wires.Blank, 0x0001, 0x0000);
                BlankRecords++;
            }

            Write(instant, Wires.Vsync, 0x0001, 0x0001);
            Write(instant, Wires.Vsync, 0x0001, 0x0000);
        }

        _groupIndex = 0;
        _de = false;

        // refresh is bookkeeping computed on demand (ADR 0011 §5): one row per four lines, anchored
        if (_line % 4 == 0)
        {
            RefreshAnchors++;
            Anchor(Channels.Slot(Registers + 1, "refresh"), (ushort)((_line / 4) & 0xFF));
            _ram.Refresh((_line / 4) & 0x1FF);
        }
    }

    void EndLine(long instant)
    {
        long lineEnd = _lineStart + LineTicks;

        _hbl = true;
        _hblReleaseAt = lineEnd + 32;
        HblRecords++;
        Write(lineEnd, Wires.Hsync, 0x0001, 0x0001);
        Write(lineEnd, Wires.Hsync, 0x0001, 0x0000);

        if (_line == FirstBlankLine - 1)
        {
            _vbl = true;
            _blank = true;
            VblRecords++;
            Write(lineEnd, Wires.Blank, 0x0001, 0x0001);
            BlankRecords++;
            Write(lineEnd, Wires.Vsync, 0x0001, 0x0001);
        }

        if (_line == FirstBlankLine + 19)
        {
            Write(lineEnd, Wires.Vsync, 0x0001, 0x0000);
        }

        RecomputeIpl(instant);

        _line++;
        _lineStart = lineEnd;

        if (_line >= LinesPerFrame)
        {
            _line = 0;
            Frames++;
        }

        BeginLine(lineEnd);
    }

    /// <summary>
    /// One group delivery: the four words the fetch buffer has accumulated go out on <c>MD0–MD15</c>,
    /// and one <c>DCYC</c>+<c>LOAD</c> record announces them (ADR 0011 §4).
    /// </summary>
    void DeliverGroup(long instant)
    {
        int lineWidth = LineWidthBytes();
        int line = _line;

        for (int word = 0; word < 4; word++)
        {
            uint offset = (uint)((line - FirstActiveLine) * lineWidth + _groupIndex * 8 + word * 2);
            ushort value = ReadVideoMemory(offset);
            Write(instant, Wires.Md, 0xFFFF, value);
        }

        Write(instant, Wires.Group, 0xFFFF, (ushort)(_groupIndex | (line << 8)));
        GroupDeliveries++;
        _groupIndex++;
    }

    /// <summary>The line-to-line address step, in bytes (video.md §6).</summary>
    int LineWidthBytes() => _regs[0x20F] switch
    {
        0 => 160,   // 80 words: low res
        1 => 320,   // 160 words: medium
        2 => 640,   // 320 words: mono
        _ => 160,
    };

    ushort ReadVideoMemory(uint offset)
    {
        long baseAddress = ((long)(_regs[0x201] & 0xFE) << 16) | ((long)(_regs[0x203] & 0xFE) << 8);
        uint address = (uint)(baseAddress + offset);
        return ReadMemory(address & 0xFFFFE);
    }

    // ---- the bus --------------------------------------------------------------------------

    /// <summary>
    /// The answering instant lies on the external slot grid: the next boundary at or after one full bus
    /// cycle past the assertion (ADR 0009's "round up to 4 clocks", and the whole wait-state model).
    /// </summary>
    static long NextExternalSlot(long assertion)
    {
        long earliest = assertion + CycleTicks;
        long aligned = ((earliest + CycleTicks - 1) / CycleTicks) * CycleTicks;
        return aligned + SlotTicks;
    }

    void PerformBusCycle(long instant)
    {
        ValueLog log = Engine.Log(Wires.Bus);
        int at = log.IndexAt(_assertionSeq);
        if (at < 0 || at + 3 >= log.Count)
        {
            return;
        }

        ushort addressHigh = log[at].Value;
        ushort addressLow = log[at + 1].Value;
        ushort control = log[at + 2].Value;
        ushort writeData = log[at + 3].Value;

        // the address bus carries the even address; A0 never reaches it, so a byte access announces
        // its lane in the control word and the chip must put it back to reach the register it named
        int size = (control >> 4) & 0x1;
        int lane = control & 0xF;

        // A24-A31 are not on the bus: a 68000 hands the chips $FFFF8001 for a short absolute address
        // and the chip decodes $FF8001. Without the mask every "$FFxxxx" comparison in the decode is
        // false, and the whole I/O page reads as nothing at all.
        uint address = (((uint)addressHigh << 16) | addressLow | (uint)(size == 0 ? (lane & 1) : 0)) & 0x00FFFFFF;
        bool isWrite = (control & 0x0100) != 0;
        bool isCpuSpace = ((control >> 12) & 0x7) == 7;

        ushort data = isWrite ? writeData : (ushort)0;
        bool faulted = false;
        bool vectorProvided = false;

        if (isCpuSpace)
        {
            // the acknowledge: the MFP supplies its vector byte, everything else autovectors
            int level = (int)((address >> 1) & 0x7);

            // the acknowledge is what clears a level request the System Control Unit raised itself
            if (level == 4 && _vbl)
            {
                _vbl = false;
                RecomputeIpl(Clock);
            }
            else if (level == 2 && _hbl)
            {
                _hbl = false;
                _hblReleaseAt = -1;
                RecomputeIpl(Clock);
            }

            if (level == 6 && _machine.Timers.TryVector(instant, out byte vector))
            {
                data = vector;
                vectorProvided = true;
            }
            else if (level == 6)
            {
                faulted = true;   // a stale acknowledge is a bus error → spurious vector 24
            }

            if (faulted)
            {
                BusFaults++;
                Write(instant, Wires.Answer, 0x0002, 0x0002);
            }
            else
            {
                BusAnswers++;
                Write(instant, Wires.Md, 0xFFFF, data);
                Write(instant, Wires.Answer, 0x0007, (ushort)(vectorProvided ? 0x0005 : 0x0001));
            }

            return;
        }

        Access(instant, address, size, isWrite, lane, data, ref data, ref faulted);

        if (faulted)
        {
            BusFaults++;
            Write(instant, Wires.Answer, 0x0002, 0x0002);
        }
        else
        {
            BusAnswers++;
            if (!isWrite)
            {
                Write(instant, Wires.Md, 0xFFFF, data);
            }

            Write(instant, Wires.Answer, 0x0001, 0x0001);
        }
    }

    void Access(long instant, uint address, int size, bool isWrite, int lane, ushort writeValue, ref ushort data, ref bool faulted)
    {
        if (address < 0x400000)
        {
            if (_romAtZero && address < 8 && !isWrite)
            {
                // the reset vectors are read from the ROM the GLUE maps over address zero; every other
                // low address is RAM, which is what lets the startup install its early handlers there
                AccessRom(address, size, isWrite, lane, ref data);
                return;
            }

            // the GSTMCU's MMU decides which physical cell a logical address reaches; past the two
            // banks' logical total there is no RAM on this machine at all, which is a bus error
            int physical = TranslateRam(address);
            if (physical < 0)
            {
                faulted = true;
                return;
            }

            AccessRam(physical, size, isWrite, lane, writeValue, ref data);
            return;
        }

        if (address >= 0xE00000 && address < 0xE40000)
        {
            AccessRom(address - 0xE00000, size, isWrite, lane, ref data);   // the 256K image at $E00000
            return;
        }

        // the I/O page comes first: the ROM's second window starts at $FC0000 and would otherwise
        // swallow every register access from $FF8000 up, silenty reading ROM bytes instead
        if (address >= 0xFF8000)
        {
            AccessIo(instant, address, size, isWrite, lane, writeValue, ref data, ref faulted);
            return;
        }

        if (address >= 0xFC0000)
        {
            AccessRom(address - 0xFC0000, size, isWrite, lane, ref data);   // and at $FC0000
            return;
        }

        if (address >= 0xFA0000 && address < 0xFC0000)
        {
            data = 0xFFFF;   // no cartridge in the port: reads float high, nothing faults
            return;
        }

        faulted = true;
    }

    // ---- the MMU's decode ------------------------------------------------------------------
    // A logical bank size comes from $FF8001 (bits 3-2 for bank 0; the GSTMCU gives both banks the
    // same size, which is Hatari's STMemory_MMU_ConfToBank on an STE), the population is two 512 KB
    // banks, and each bank cell is reached by the address bits the DRAMs actually have. This is
    // STMemory_MMU_Translate_Addr_STE ported: with a 512 KB bank $40008 and $8 are different cells,
    // with a 128 KB bank they are the same one — and that difference is exactly what EmuTOS's pattern
    // test in bios/memory.S reads to size the machine.
    const int RamBankBytes = 512 * 1024;

    static int MmuBankBytes(int code) => code switch
    {
        0 => 128 * 1024,
        1 => 512 * 1024,
        2 => 2 * 1024 * 1024,
        _ => 0,                                  // 3 is not a size the GSTMCU decodes
    };

    /// <summary>The physical byte a logical address reaches, or -1 if the MMU does not decode it.</summary>
    int TranslateRam(uint address)
    {
        // the MMU decodes the 4 MB below the I/O page and nothing above it
        if (address >= 0x400000) return -1;

        int logicalBank = MmuBankBytes((_memConfig >> 2) & 0x3);
        if (logicalBank == 0) logicalBank = 128 * 1024;   // an undecoded config still answers the low bank
        int total = 2 * logicalBank;

        if (address < logicalBank) return (int)(address & (RamBankBytes - 1));
        if (address < total) return RamBankBytes + (int)((address - logicalBank) & (RamBankBytes - 1));

        // Below 4 MB the strobes still come out, so a logical address past the configured banks aliases
        // onto the population by the bits the DRAMs have — which is what lets EmuTOS's early stack sit
        // at $602E0206 while the machine still reports a 1 MB configuration.
        return (int)(address & (2 * RamBankBytes - 1));
    }

    void AccessRam(int physical, int size, bool isWrite, int lane, ushort writeValue, ref ushort data)
    {
        // RAS0/RAS1 pick the bank, then the word's 18 remaining bits are 9 row bits (latched on RAS)
        // and 9 column bits (latched on CAS). Splitting them one bit off makes bit 9 belong to both,
        // and then a 512 KB bank wraps every 256 KB — which reads as "the RAM is half its size".
        int bank = physical >> 19;
        int word = (physical >> 1) & 0x3FFFF;
        int row = (word >> 9) & 0x1FF;
        int column = word & 0x1FF;

        if (size == 0)
        {
            if (isWrite) _ram.WriteByte(bank, row, column, (Lane)lane, (byte)writeValue);
            else data = _ram.ReadByte(bank, row, column, (Lane)lane);
            return;
        }

        if (isWrite) _ram.Write(bank, row, column, writeValue);
        else data = _ram.Read(bank, row, column);
    }

    void AccessRom(uint offset, int size, bool isWrite, int lane, ref ushort data)
    {
        if (isWrite)
        {
            return;   // the ROM is read-only: the write is accepted and does nothing
        }

        int word = (int)((offset & 0x3FFFE) >> 1);
        data = size == 0 ? _rom.ReadByte(word, (Lane)lane) : _rom.Read(word);
    }

    void AccessIo(long instant, uint address, int size, bool isWrite, int lane, ushort writeValue, ref ushort data, ref bool faulted)
    {
        // ---- the Blitter is absent: $FF8A00–$FF8A3F faults (walking-skeleton.md §1) --------
        if (address >= 0xFF8A00 && address <= 0xFF8A3F)
        {
            faulted = true;
            return;
        }

        // ---- the MFP: $FFFA01–$FFFA3F, odd bytes ------------------------------------------
        if (address >= 0xFFFA00 && address <= 0xFFFA3F)
        {
            // The select is a crossing, not a function call (crossings.md: MFPCS, one record per
            // access): the record is what wakes the chip, which then re-arms its counters' deadlines.
            // Without it a chip suspended on an old deadline never learns that software just
            // programmed a new one — which is how the 200 Hz tick stayed dead while TCDCR/TCDR read
            // back 0x51/192 and IERA/IMRA both said Timer C was enabled and unmasked.
            int index = (int)(address - 0xFFFA00) & 0x3F;
            data = _machine.Timers.Register(index, writeValue, isWrite, instant);
            Write(instant, Wires.MfpSelect, 0xFFFF, (ushort)((index << 8) | (isWrite ? 0x80 : 0x00)));
            return;
        }

        // ---- the keyboard ACIA and MIDI: Controllers ---------------------------------------
        if (address >= 0xFFFC00 && address <= 0xFFFC07)
        {
            data = _machine.Controllers.Register((int)(address - 0xFFFC00), writeValue, isWrite);
            return;
        }

        if (address < 0xFF9000)
        {
            AccessShifterOrDma(address, size, isWrite, lane, writeValue, ref data, ref faulted);
            return;
        }

        // ---- Microwire and DMA sound: Sound ------------------------------------------------
        if ((address >= 0xFF9200 && address <= 0xFF9217) || (address >= 0xFF8900 && address <= 0xFF89FF))
        {
            data = _machine.Sound.Register((int)(address & 0xFF), writeValue, isWrite);
            return;
        }

        faulted = true;
    }

    void AccessShifterOrDma(uint address, int size, bool isWrite, int lane, ushort writeValue, ref ushort data, ref bool faulted)
    {
        int index = (int)(address - 0xFF8000);

        if (address >= 0xFF8240 && address <= 0xFF825F)
        {
            data = _machine.Video.RegisterPalette((int)((address - 0xFF8240) >> 1), writeValue, isWrite);
            return;
        }

        if (address == 0xFF8260 || address == 0xFF8261 || address == 0xFF8262 || address == 0xFF8263 ||
            address == 0xFF8264 || address == 0xFF8265)
        {
            data = _machine.Video.RegisterShifter(index, writeValue, isWrite);
            if (isWrite) RegisterWrites++;
            return;
        }

        switch (address)
        {
            case 0xFF8001:
                if (isWrite) _memConfig = (byte)(writeValue & 0x0F);
                else data = _memConfig;
                return;

            case 0xFF8201:
            case 0xFF8203:
            case 0xFF820A:
            case 0xFF820D:
            case 0xFF820F:
            {
                if (isWrite)
                {
                    _regs[index] = (byte)(writeValue & 0xFF);
                    RegisterWrites++;
                    if (address == 0xFF820A)
                    {
                        _romAtZero = false;   // the first sync-mode write releases the ROM from zero
                    }
                }
                else
                {
                    data = size == 0 ? _regs[index] : (ushort)((int)_regs[index] | ((int)_regs[index] << 8));
                }

                return;
            }

            case 0xFF8604:
            case 0xFF8606:
            case 0xFF8609:
            case 0xFF860B:
            case 0xFF860D:
                data = _machine.Disk.Register(index & 0x1F, writeValue, isWrite);
                return;
        }

        if (address >= 0xFF8800 && address <= 0xFF88FF)
        {
            data = _machine.Sound.Register(index & 0xFF, writeValue, isWrite);
            return;
        }

        if (address >= 0xFF8000 && address < 0xFF9000)
        {
            if (isWrite)
            {
                _regs[index] = (byte)(writeValue & 0xFF);
            }
            else
            {
                data = 0xFF;
            }

            return;
        }

        faulted = true;
    }

    // ---- interrupts -----------------------------------------------------------------------

    /// <summary>
    /// Timers and events asserts or releases <c>IRQ</c>: level 6, nothing else. The instant is the
    /// MFP's — the chip that drove the line — never this chip's own clock: the System Control Unit may
    /// still be suspended at an older instant when the MFP asserts, and an <c>IPL</c> record stamped
    /// there is a record dated before the batch being drained, which resumes the CPU in the past.
    /// </summary>
    public void SetMfpInterrupt(bool asserted, long instant)
    {
        int level = asserted ? 6 : 0;
        if (level == _mfpLevel)
        {
            return;
        }

        _mfpLevel = level;
        RecomputeIpl(instant);
    }

    /// <summary><c>max(6, 4, 2)</c> — one three-lane record per change (crossings.md §4).</summary>
    void RecomputeIpl(long instant)
    {
        int level = Math.Max(_mfpLevel, Math.Max(_vbl ? 4 : 0, _hbl ? 2 : 0));
        if (level == _resolvedLevel)
        {
            return;
        }

        _resolvedLevel = level;
        Write(instant, Wires.Ipl, 0x0007, (ushort)(level & 0x7));
    }

    /// <summary>What the CPU sees on <c>IPL0–IPL2</c> at an instant.</summary>
    public int ResolvedLevelAt(long instant) => Engine.ValueAt(Wires.Ipl, instant) & 0x7;

    // ---- the two stores, for the host ----------------------------------------------------
    public Ram Ram => _ram;
    public Rom Rom => _rom;

    public void Reset()
    {
        Array.Clear(_regs);
        _memConfig = 0x04;
        _romAtZero = true;
        _mfpLevel = 0;
        _resolvedLevel = 0;
        _line = 0;
        _lineStart = 0;
        _groupIndex = 0;
        _de = false;
        _blank = false;
        _vbl = false;
        _hbl = false;
        _hblReleaseAt = -1;
    }

    ushort ReadMemory(uint address)
    {
        if (address >= 0xE00000 && address < 0xE40000)
        {
            return _rom.Read((int)((address - 0xE00000) >> 1));
        }

        if (address >= 0xFC0000)
        {
            return _rom.Read((int)((address & 0x3FFFE) >> 1));
        }

        if ((_romAtZero && address < 8) || address >= 0x800000)
        {
            return _rom.Read((int)((address & 0x3FFFE) >> 1));
        }

        int physical = TranslateRam(address);
        if (physical < 0) return 0xFFFF;   // nothing drives the bus there

        int bank = physical >> 19;
        int word = (physical >> 1) & 0x3FFFF;
        return _ram.Read(bank, (word >> 9) & 0x1FF, word & 0x1FF);
    }

}
