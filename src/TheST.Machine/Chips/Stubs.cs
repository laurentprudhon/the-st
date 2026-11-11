namespace TheST.Machine.Chips;

/// <summary>
/// A stub: it owns its register file and nothing else (walking-skeleton.md §1's stub rule). It decodes
/// its addresses, remembers what software wrote wherever software reads it back, answers idle
/// everywhere else, and asserts no wire — no level, no <c>IPL</c> lane, no <c>VBL</c>, no <c>HBL</c>,
/// no deadline expiry. A stub that wakes something is a fabricated input path, not a simplification.
///
/// A stub never runs: it has no coroutine, no clock of its own and no wake, which is exactly what
/// "asserts nothing" means in this machine.
/// </summary>
public abstract class Stub
{
    readonly byte[] _registers = new byte[256];

    protected Stub(string name) => Name = name;

    public string Name { get; }

    /// <summary>Every write the software made, for the debug witness.</summary>
    public long Writes { get; private set; }

    /// <summary>Every read the software made.</summary>
    public long Reads { get; protected set; }

    protected byte Read(int index) => _registers[index & 0xFF];

    protected void Store(int index, byte value)
    {
        _registers[index & 0xFF] = value;
        Writes++;
    }

    /// <summary>One byte access at <paramref name="offset"/> from the stub's base.</summary>
    public virtual ushort Register(int offset, ushort value, bool isWrite)
    {
        if (isWrite)
        {
            Store(offset, (byte)value);
            return 0;
        }

        Reads++;
        return Read(offset);
    }

    public void Reset() => Array.Clear(_registers);
}

/// <summary>
/// Sound (walking-skeleton.md §1): <c>$FF8800</c>/<c>$FF8802</c> (PSG select/data), <c>$FF8900</c>…
/// (DMA sound), <c>$FF9200–$FF9217</c> (Microwire). It asserts no <c>SREQ</c>, so the System Control
/// Unit's owner equation never picks it and the refill row stays unexercised.
/// </summary>
public sealed class Sound : Stub
{
    int _selected;

    public Sound()
        : base("Sound")
    {
    }

    public override ushort Register(int offset, ushort value, bool isWrite)
    {
        if (offset == 0x00 && isWrite)
        {
            _selected = (byte)value & 0x0F;
        }

        return base.Register(offset, value, isWrite);
    }

    /// <summary>The PSG register the last select named — read back through the data port.</summary>
    public int SelectedRegister => _selected;
}

/// <summary>
/// Disk (walking-skeleton.md §1): <c>$FF8604</c> (FDC command/status), <c>$FF8606</c> (DMA mode),
/// <c>$FF8609</c>/<c>B</c>/<c>D</c> (DMA address). Status answers with BUSY and TRACK0 clear and asserts
/// no <c>FDINT</c>, no <c>DRQ</c>, no <c>RDY</c>: TOS therefore times out the probe and registers no
/// drive. Answering TRACK0 would make it wait for a boot sector nobody produces.
/// </summary>
public sealed class Disk : Stub
{
    public Disk()
        : base("Disk")
    {
    }

    /// <summary>The FDC status word the stub always answers: no BUSY, no TRACK0, no INTERRUPT.</summary>
    public const ushort StatusWord = 0x0000;

    /// <summary>True if anything ever read <c>$FF8604</c> — the debug witness of §1.</summary>
    public bool StatusRead { get; private set; }

    public override ushort Register(int offset, ushort value, bool isWrite)
    {
        if (!isWrite && offset == 0x04)
        {
            StatusRead = true;
            return StatusWord;
        }

        return base.Register(offset, value, isWrite);
    }
}

/// <summary>
/// Controllers (walking-skeleton.md §1): <c>$FFFC00</c>/<c>$FFFC02</c> (ACIA), <c>$FFFC04</c>/<c>6</c>
/// (MIDI), and the joystick ports. It answers "transmitter empty, no received byte" and delivers no
/// IKBD byte: TOS gives up after its own 300 ms timeout, and a phantom key would change the desktop
/// state with no input path behind it.
/// </summary>
public sealed class Controllers : Stub
{
    public Controllers()
        : base("Controllers")
    {
    }

    /// <summary>ACIA status: transmitter empty always, no received byte ever.</summary>
    public const byte AciaStatus = 0x02;

    public override ushort Register(int offset, ushort value, bool isWrite)
    {
        if (!isWrite && offset == 0x00)
        {
            Reads++;
            return AciaStatus;
        }

        if (!isWrite && offset == 0x02)
        {
            Reads++;
            return 0x00;   // no received byte
        }

        return base.Register(offset, value, isWrite);
    }
}
