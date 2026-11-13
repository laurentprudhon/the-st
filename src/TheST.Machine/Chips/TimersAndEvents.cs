namespace TheST.Machine.Chips;

/// <summary>
/// The Timers and events chip — the MFP (design/timers-and-events.md, ADR 0013). Four counters on the
/// 2.4576 MHz crystal, the event selector over sixteen sources, and one <c>IRQ</c> line into the System
/// Control Unit.
///
/// Its wakes are the counters' expiries (dates it computes itself) and the register selects it answers
/// (a subscription on <c>MFPCS</c>); the events its sources carry arrive as records and are counted as
/// they land.
/// </summary>
public sealed class TimersAndEvents : Chip
{
    readonly Machine _machine;
    readonly SystemControlUnit _scu;

    readonly byte[] _r = new byte[0x40];      // the byte register file, index = low address byte

    // counters
    long _cExpiry = long.MaxValue;
    long _dExpiry = long.MaxValue;
    long _cRemainder;
    long _dRemainder;
    int _cCounter;
    int _dCounter;
    int _bCounter;

    // the event selector
    ushort _pending;      // IPRA in the high byte, IPRB in the low byte
    ushort _enabled;      // IERA/IERB
    ushort _masked;       // IMRA/IMRB
    ushort _inService;    // ISRA/ISRB

    bool _irqAsserted;
    long _selectSeq = long.MinValue;

    // ---- reported, never gated -----------------------------------------------------------
    public long TimerCExpiries { get; private set; }
    public long TimerBExpiries { get; private set; }
    public long IrqAssertions { get; private set; }
    public long ExecutedAcknowledges { get; private set; }
    public long StaleAcknowledges { get; private set; }
    public long RegisterWrites { get; private set; }
    public long DeCounts { get; private set; }

    public TimersAndEvents(Engine engine, int id, Machine machine, SystemControlUnit scu)
        : base(engine, id, "TimersAndEvents")
    {
        _machine = machine;
        _scu = scu;
        Reset();
    }

    public void Reset()
    {
        Array.Clear(_r);
        _r[0x05] = 0x00;      // DDR: all inputs
        _r[0x01] = 0xFB;      // GPIP: the tied lines read inactive-high
        _r[0x03] = 0x00;      // AER
        _pending = 0;
        _enabled = 0;
        _masked = 0;
        _inService = 0;
        _irqAsserted = false;
        _cExpiry = long.MaxValue;
        _dExpiry = long.MaxValue;
        _bCounter = 0;
    }

    public void Launch()
    {
        _ = Run();
    }

    async ValueTask Run()
    {
        while (true)
        {
            ValueLog select = Engine.Log(Wires.MfpSelect);
            int index = select.IndexAfter(_selectSeq);

            if (index >= 0 && index < select.Count && select[index].Instant <= Clock)
            {
                _selectSeq = select[index].Seq;
            }

            if (Clock >= _cExpiry)
            {
                ExpireTimerC();
            }

            if (Clock >= _dExpiry)
            {
                ExpireTimerD();
            }

            CountEventRecords();

            await WaitFor(Math.Min(_cExpiry, _dExpiry), Wires.MfpSelect, _selectSeq);
        }
    }

    // Both cursors are sequence numbers, never indices: retention discards records from the oldest
    // end, so an index saved across a suspension names a different record — or no record at all —
    // once anything was trimmed in between (it threw IndexOutOfRange on the DE log).
    long _deSeq = long.MinValue;
    long _sintSeq = long.MinValue;

    /// <summary>
    /// Events that arrive as records are counted from the logs at each of this chip's own wakes: a
    /// counter's expiry is a date the chip computed, so the chip is awake often enough to count them
    /// without a second subscription (ADR 0008 §5 — a wake only where the chip meets another chip).
    /// </summary>
    void CountEventRecords()
    {
        ValueLog de = Engine.Log(Wires.De);
        for (int index = de.IndexAfter(_deSeq); index >= 0 && index < de.Count; index = de.IndexAfter(_deSeq))
        {
            Record record = de[index];
            if (record.Instant > Clock)
            {
                break;
            }

            if ((record.Value & 0x0001) != 0)
            {
                CountDe(record.Instant);
            }

            _deSeq = record.Seq;
        }

        ValueLog sint = Engine.Log(Wires.SoundFrameEnd);
        for (int index = sint.IndexAfter(_sintSeq); index >= 0 && index < sint.Count; index = sint.IndexAfter(_sintSeq))
        {
            Record record = sint[index];
            if (record.Instant > Clock)
            {
                break;
            }

            SignalCompletion(15, record.Instant);
            _sintSeq = record.Seq;
        }
    }

    void ApplySelect(ushort encoded)
    {
        int address = encoded & 0x3F;
        if (address == 0x3F)
        {
            return;   // a read select: nothing changes
        }
    }

    // ---- the register file ----------------------------------------------------------------
    /// <summary>
    /// One byte register access. <paramref name="offset"/> is the address's low byte
    /// (<c>$FFFA00</c> + offset); every register is at an odd address and the even ones alias.
    ///
    /// <para>
    /// <paramref name="instant"/> is the bus cycle's instant, never the chip's own clock: the System
    /// Control Unit answers an MFP cycle inside the CPU's exchange (a class-2 leaf), so this chip may
    /// still be suspended at an older instant when its register file changes. Stamping a counter's
    /// new deadline, or the <c>IRQ</c> it raises, with a stale clock would backdate a record and
    /// restart the 200 Hz tick from the wrong instant.
    /// </para>
    /// </summary>
    public ushort Register(int offset, ushort value, bool isWrite, long instant)
    {
        int index = offset & 0x3F;
        byte data = (byte)(value & 0xFF);

        if (!isWrite)
        {
            return ReadRegister(index);
        }

        RegisterWrites++;
        Anchor(Channels.Slot(0, "register"), (ushort)((index << 8) | data));

        switch (index)
        {
            case 0x01:   // GPIP
                _r[0x01] = data;
                EvaluateEdges(instant);
                return 0;

            case 0x03:   // AER
                _r[0x03] = data;
                EvaluateEdges(instant);
                return 0;

            case 0x05:   // DDR
                _r[0x05] = data;
                EvaluateEdges(instant);
                return 0;

            case 0x07:   // IERA — clears the pending bits it disables
                _r[0x07] = data;
                _enabled = (ushort)((_enabled & 0x00FF) | (data << 8));
                _pending = (ushort)(_pending & (ushort)~((data ^ 0xFF) << 8));
                UpdateIrq(instant);
                return 0;

            case 0x09:   // IERB
                _r[0x09] = data;
                _enabled = (ushort)((_enabled & 0xFF00) | data);
                _pending &= (ushort)(0xFF00 | (data ^ 0xFF));
                UpdateIrq(instant);
                return 0;

            case 0x0B:   // IPRA: value = old & written
                _r[0x0B] = (byte)(_r[0x0B] & data);
                _pending = (ushort)((_pending & 0x00FF) | (_r[0x0B] << 8));
                UpdateIrq(instant);
                return 0;

            case 0x0D:   // IPRB
                _r[0x0D] = (byte)(_r[0x0D] & data);
                _pending = (ushort)((_pending & 0xFF00) | _r[0x0D]);
                UpdateIrq(instant);
                return 0;

            case 0x0F:   // ISRA
                _r[0x0F] = (byte)(_r[0x0F] & data);
                _inService = (ushort)((_inService & 0x00FF) | (_r[0x0F] << 8));
                UpdateIrq(instant);
                return 0;

            case 0x11:   // ISRB
                _r[0x11] = (byte)(_r[0x11] & data);
                _inService = (ushort)((_inService & 0xFF00) | _r[0x11]);
                UpdateIrq(instant);
                return 0;

            case 0x13:   // IMRA
                _r[0x13] = data;
                _masked = (ushort)((_masked & 0x00FF) | (data << 8));
                UpdateIrq(instant);
                return 0;

            case 0x15:   // IMRB
                _r[0x15] = data;
                _masked = (ushort)((_masked & 0xFF00) | data);
                UpdateIrq(instant);
                return 0;

            case 0x17:   // VR
                _r[0x17] = data;
                return 0;

            case 0x19:   // TACR
            case 0x1B:   // TBCR
            case 0x1D:   // TCDCR
                _r[index] = data;
                RestartCounters(instant);
                return 0;

            case 0x1F:   // TADR
            case 0x21:   // TBDR
            case 0x23:   // TCDR
            case 0x25:   // TDDR
                _r[index] = data;
                RestartCounters(instant);
                return 0;

            case 0x29:   // UCR
            case 0x27:   // SCR
                _r[index] = data;
                return 0;

            case 0x2F:   // UDR — accepted and discarded
                _r[index] = data;
                return 0;
        }

        if (index >= 0x31 && index <= 0x3F)
        {
            _r[index - 0x30] = data;    // the block is mirrored at $FFFA30
        }

        return 0;
    }

    ushort ReadRegister(int index)
    {
        switch (index)
        {
            case 0x01: return _r[0x01];
            case 0x03: return _r[0x03];
            case 0x05: return _r[0x05];
            case 0x07:
            case 0x09:
            case 0x0B:
            case 0x0D:
            case 0x0F:
            case 0x11:
            case 0x13:
            case 0x15:
            case 0x17:
                return _r[index];
            case 0x19:
            case 0x1B:
            case 0x1D:
                return _r[index];
            case 0x1F: return (byte)_cCounter;   // Timer A's live counter
            case 0x21: return (byte)_bCounter;
            case 0x23: return (byte)_cCounter;
            case 0x25: return (byte)_dCounter;
            case 0x2B: return 0x00;              // RSR: no received byte, no overrun
            case 0x2D: return 0x80;              // TSR: transmitter empty
            case 0x2F: return 0x00;              // UDR
        }

        if (index >= 0x31 && index <= 0x3F)
        {
            return _r[index - 0x30];
        }

        return _r[index];
    }

    // ---- the counters ---------------------------------------------------------------------

    static int Divider(int field) => field switch
    {
        0 => 0,       // stopped
        1 => 4,
        2 => 10,
        3 => 16,
        4 => 50,
        5 => 64,
        6 => 100,
        7 => 200,
        _ => 0,
    };

    void RestartCounters(long instant)
    {
        // Every deadline below starts at the write's instant, never at the chip's own clock: the System
        // Control Unit performs the write inside the CPU's cycle, so this chip may not have run yet at
        // that instant. Restarting the 200 Hz tick from a stale clock shifts the whole machine's tick.

        // Timer C: interval mode on its own prescaler (the OS's 200 Hz tick)
        int cField = (_r[0x1D] >> 4) & 0x07;
        int cDivider = Divider(cField);
        int cData = _r[0x23] == 0 ? 256 : _r[0x23];
        _cCounter = cData;

        if (cDivider != 0)
        {
            _cRemainder = 0;
            long ticks = MfpClock.TicksFor((long)cData * cDivider, ref _cRemainder);
            _cExpiry = instant + Math.Max(1, ticks);
        }
        else
        {
            _cExpiry = long.MaxValue;
        }

        // Timer D is arithmetic only while the USART is a stub: it counts, nothing observes it
        int dField = _r[0x1D] & 0x07;
        int dDivider = Divider(dField);
        int dData = _r[0x25] == 0 ? 256 : _r[0x25];
        _dCounter = dData;

        if (dDivider != 0)
        {
            _dRemainder = 0;
            long ticks = MfpClock.TicksFor((long)dData * dDivider, ref _dRemainder);
            _dExpiry = instant + Math.Max(1, ticks);
        }
        else
        {
            _dExpiry = long.MaxValue;
        }

        // Timer B: an event counter on DE while its mode bit is set, otherwise interval
        if ((_r[0x1B] & 0x08) != 0)
        {
            _bCounter = _r[0x21] == 0 ? 256 : _r[0x21];
        }
        else
        {
            int bDivider = Divider(_r[0x1B] & 0x07);
            int bData = _r[0x21] == 0 ? 256 : _r[0x21];
            _bCounter = bData;
            if (bDivider != 0)
            {
                long remainder = 0;
                long ticks = MfpClock.TicksFor((long)bData * bDivider, ref remainder);
                _bExpiry = instant + Math.Max(1, ticks);
            }
            else
            {
                _bExpiry = long.MaxValue;
            }
        }
    }

    long _bExpiry = long.MaxValue;

    void ExpireTimerC()
    {
        TimerCExpiries++;
        Anchor(Channels.Slot(1, "timer C"), (ushort)_cCounter);
        SetPending(5, Clock);

        int cDivider = Divider((_r[0x1D] >> 4) & 0x07);
        long ticks = MfpClock.TicksFor((long)_cCounter * cDivider, ref _cRemainder);
        _cExpiry = Clock + Math.Max(1, ticks);
    }

    void ExpireTimerD()
    {
        Anchor(Channels.Slot(2, "timer D"), (ushort)_dCounter);
        int dDivider = Divider(_r[0x1D] & 0x07);
        long ticks = MfpClock.TicksFor((long)_dCounter * dDivider, ref _dRemainder);
        _dExpiry = Clock + Math.Max(1, ticks);
    }

    /// <summary>A <c>DE</c> record: Timer B's event input (crossings.md §2, one count per active line).</summary>
    public void CountDe(long instant)
    {
        DeCounts++;

        if ((_r[0x1B] & 0x08) == 0)
        {
            return;
        }

        _bCounter--;
        if (_bCounter <= 0)
        {
            _bCounter = _r[0x21] == 0 ? 256 : _r[0x21];
            TimerBExpiries++;
            Anchor(Channels.Slot(3, "timer B"), (ushort)instant);
            SetPending(8, instant);
        }
    }

    public void SignalCompletion(int source, long instant)
    {
        Anchor(Channels.Slot(4, "event"), (ushort)((source << 8) | 1));
        SetPending(source, instant);
    }

    // ---- the event selector ---------------------------------------------------------------

    void SetPending(int source, long instant)
    {
        int bit = source >= 8 ? source - 8 : source;
        ushort set = (ushort)(source >= 8 ? (1 << (8 + bit)) : (1 << bit));
        if ((_pending & set) != 0)
        {
            return;
        }

        _pending |= set;

        // the pending registers read back what is pending: a handler reads IPRB to find out why it was
        // entered, and a write to an IPR clears only the bits it names ("old & written")
        _r[0x0B] = (byte)(_pending >> 8);
        _r[0x0D] = (byte)_pending;

        if (!_irqAsserted && (Connected() & set) != 0)
        {
            Anchor(Channels.Slot(5, "IRQ"), (ushort)source);
        }

        UpdateIrq(instant);
    }

    void ClearPending(int source)
    {
        int bit = source >= 8 ? source - 8 : source;
        ushort clear = (ushort)(source >= 8 ? (1 << (8 + bit)) : (1 << bit));
        _pending &= (ushort)~clear;
        _r[0x0B] = (byte)(_pending >> 8);
        _r[0x0D] = (byte)_pending;
    }

    /// <summary>
    /// pending ∧ IER ∧ IMR — the candidates, before the in-service filter.
    ///
    /// <para>
    /// <b>IMR is a second enable, not a mask</b>, whatever its name suggests. Two independent
    /// witnesses: EmuTOS's <c>enable_mfp_interrupt</c> sets <i>both</i> the IER and the IMR bit
    /// (<c>mfp-&gt;ierb |= mask; mfp-&gt;imrb |= mask;</c>), and Hatari's <c>MFP_UpdateIRQ</c> tests
    /// <c>(IPRA &amp; IMRA) | (IPRB &amp; IMRB)</c> before asking which source is selected. Requiring the
    /// IMR bit to be <i>clear</i> — which is what this chip did until the boot stopped here — makes
    /// every interrupt the ROM enables permanently invisible: Timer C expired four times a frame and
    /// <c>IRQ</c> never once went low, so <c>hz_200</c> stayed 0 and the BIOS's ACSI wait loop in
    /// <c>acsi_begin()</c> spun on <c>hz_200 &lt; next_acsi_time</c> forever.
    /// </para>
    /// </summary>
    ushort Connected() => (ushort)(_pending & _enabled & _masked);

    /// <summary>
    /// The selected source: the highest-numbered candidate with no source numbered at or above it in
    /// service. The number <i>is</i> the priority, so no table is written down.
    /// </summary>
    int Selected()
    {
        ushort candidates = Connected();
        if (candidates == 0)
        {
            return -1;
        }

        for (int source = 15; source >= 0; source--)
        {
            int bit = source >= 8 ? source - 8 : source;
            ushort mask = (ushort)(source >= 8 ? (1 << (8 + bit)) : (1 << bit));
            if ((candidates & mask) == 0)
            {
                continue;
            }

            bool blocked = false;
            for (int other = source; other <= 15; other++)
            {
                int otherBit = other >= 8 ? other - 8 : other;
                ushort otherMask = (ushort)(other >= 8 ? (1 << (8 + otherBit)) : (1 << otherBit));
                if ((_inService & otherMask) != 0)
                {
                    blocked = true;
                    break;
                }
            }

            if (!blocked)
            {
                return source;
            }
        }

        return -1;
    }

    void UpdateIrq(long instant)
    {
        bool asserted = Selected() >= 0;
        if (asserted == _irqAsserted)
        {
            return;
        }

        _irqAsserted = asserted;
        if (asserted)
        {
            IrqAssertions++;
        }

        Write(instant, Wires.Irq, 0x0001, (ushort)(asserted ? 0 : 1));   // active low
        _scu.SetMfpInterrupt(asserted, instant);
    }

    /// <summary>
    /// The acknowledge instant: the vector byte names the source selected <i>now</i>; pending clears and
    /// in-service is set here, never at <c>RTE</c> (ADR 0013).
    /// </summary>
    public bool TryVector(long instant, out byte vector)
    {
        int source = Selected();
        if (source < 0)
        {
            StaleAcknowledges++;
            vector = 24;
            return false;
        }

        vector = (byte)((_r[0x17] & 0xF0) + source);
        ClearPending(source);

        if ((_r[0x17] & 0x08) != 0)
        {
            int bit = source >= 8 ? source - 8 : source;
            _inService |= (ushort)(source >= 8 ? (1 << (8 + bit)) : (1 << bit));
            _r[0x0F] = (byte)(_inService >> 8);
            _r[0x11] = (byte)_inService;
        }

        ExecutedAcknowledges++;
        Anchor(Channels.Slot(6, "acknowledge"), (ushort)((source << 8) | vector));
        UpdateIrq(instant);
        return true;
    }

    /// <summary>The GPIP line levels — sampled by a read, never a wake.</summary>
    public byte Gpip => _r[0x01];

    void EvaluateEdges(long instant)
    {
        // a level change on an input line sets its pending bit when GPIP ⊕ AER falls to zero
        ushort level = (ushort)(_r[0x01] & 0xFF);
        ushort aer = (ushort)(_r[0x03] & 0xFF);
        ushort ddr = (ushort)(_r[0x05] & 0xFF);
        ushort inputs = (ushort)(~ddr & 0xFF);

        for (int bit = 0; bit < 8; bit++)
        {
            if ((inputs & (1 << bit)) == 0)
            {
                continue;
            }

            if (((level ^ aer) & (1 << bit)) == 0)
            {
                SetPending(bit, instant);   // GPIP bit n is source n for n < 8
            }
        }
    }
}
