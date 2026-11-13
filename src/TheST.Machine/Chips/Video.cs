namespace TheST.Machine.Chips;

/// <summary>
/// The Video chip (design/video.md, ADR 0012): the shifter, the palette, the pixel counter and — the
/// reason it is a real chip and not a stub — the two committed screens it writes a chunk at a time.
///
/// Its work is driven by the System Control Unit's group delivery (one chunk per group in low res) and
/// by the display window. It takes no <c>nextDeadline</c>: every iteration is ended by a subscription,
/// which is exactly the shape ADR 0008 §5 asks for.
/// </summary>
public sealed class Video : Chip
{
    /// <summary>The PAL colour raster, borders included: 840 × 284, 16-bit 0RGB.</summary>
    public const int ColourColumns = 840;
    public const int ColourRows = 284;

    /// <summary>The SM124 raster: 640 × 400.</summary>
    public const int MonoColumns = 640;
    public const int MonoRows = 400;

    /// <summary>Where the display area starts inside the colour buffer: 100 low-res pixels of border.</summary>
    public const int LeftBorderColumns = 100;

    readonly Machine _machine;

    readonly ushort[] _colour = new ushort[ColourColumns * ColourRows];
    readonly ushort[] _mono = new ushort[MonoColumns * MonoRows];

    readonly ushort[] _palette = new ushort[16];
    readonly byte[] _shifter = new byte[0x24];      // $FF8260 … $FF8265, byte indexed

    readonly ushort[] _words = new ushort[4];

    long _groupSeq = long.MinValue;
    long _deAssertedAt = -1;
    int _column;
    int _row = -1;

    // ---- reported, never gated -----------------------------------------------------------
    public long ChunkWrites { get; private set; }
    public long GroupWrites { get; private set; }
    public long LineCompletions { get; private set; }
    public long FrameCompletions { get; private set; }
    public long LineAnchors { get; private set; }

    public Video(Engine engine, int id, Machine machine)
        : base(engine, id, "Video")
    {
        _machine = machine;

        // mono is paper white on black by default
        Array.Fill(_palette, (ushort)0x0777);
        _palette[0] = 0;
    }

    /// <summary>The colour raster, 0RGB — what a host paints.</summary>
    public ReadOnlySpan<ushort> ColourScreen => _colour;

    public ReadOnlySpan<ushort> MonoScreen => _mono;

    public void Launch()
    {
        _ = Run();
    }

    async ValueTask Run()
    {
        while (true)
        {
            ValueLog groups = Engine.Log(Wires.Group);
            int index = groups.IndexAfter(_groupSeq);

            if (index < 0 || index >= groups.Count)
            {
                await WaitFor(long.MaxValue, Wires.Group, _groupSeq);
                continue;
            }

            Record delivery = groups[index];
            _groupSeq = delivery.Seq;

            if (Engine.ValueAt(Wires.De, delivery.Instant) == 0)
            {
                continue;   // a group outside the window paints nothing
            }

            PaintGroup(delivery.Instant);
        }
    }

    /// <summary>
    /// One group delivery = one chunk: four words shifted out as sixteen pixels, each resolved through
    /// the palette and written twice across (a low-res pixel is 2 columns at the 16 MHz reference).
    /// </summary>
    void PaintGroup(long instant)
    {
        int line = _machine.SystemControlUnitLineAt(instant);
        int row = line - Chips.SystemControlUnit.FirstActiveLine;
        if (row < 0 || row >= ColourRows)
        {
            return;
        }

        if (row != _row)
        {
            _row = row;
            _deAssertedAt = instant;
            _column = LeftBorderColumns;
            FillBorder(row, 0, LeftBorderColumns);
            Anchor(Channels.Slot(0, "pixel counter"), (ushort)row);
            LineAnchors++;
        }

        // the four words the System Control Unit put on MD0–MD15 at this instant. Four records, not
        // one: this is a live sampling window, so retention may not pass the delivery instant while
        // this read runs (ADR 0004's revision).
        Engine.NoteSampled(instant);
        ValueLog md = Engine.Log(Wires.Md);
        int first = md.IndexAtOrBefore(instant) - 3;
        if (first < 0)
        {
            return;
        }

        for (int word = 0; word < 4; word++)
        {
            _words[word] = md[first + word].Value;
        }

        for (int pixel = 0; pixel < 16; pixel++)
        {
            int bit = 15 - pixel;
            int colour =
                ((_words[0] >> bit) & 1) |
                (((_words[1] >> bit) & 1) << 1) |
                (((_words[2] >> bit) & 1) << 2) |
                (((_words[3] >> bit) & 1) << 3);

            ushort rgb = _palette[colour];
            int column = _column + pixel * 2;
            if (column + 1 < ColourColumns)
            {
                _colour[row * ColourColumns + column] = rgb;
                _colour[row * ColourColumns + column + 1] = rgb;
            }
        }

        _column += 32;
        ChunkWrites++;
        GroupWrites++;

        if (_column >= LeftBorderColumns + 640)
        {
            FillBorder(row, LeftBorderColumns + 640, ColourColumns);
            LineCompletions++;
            if (row == ColourRows - 1)
            {
                FrameCompletions++;
            }
        }
    }

    void FillBorder(int row, int from, int to)
    {
        ushort colour = _palette[0];
        for (int column = from; column < to && column < ColourColumns; column++)
        {
            _colour[row * ColourColumns + column] = colour;
        }
    }

    // ---- the register file ----------------------------------------------------------------

    public ushort RegisterPalette(int index, ushort value, bool isWrite)
    {
        if (index < 0 || index >= 16)
        {
            return 0;
        }

        if (isWrite)
        {
            _palette[index] = value;
            Anchor(Channels.Slot(1, "palette"), value);
        }

        return _palette[index];
    }

    /// <summary>
    /// The Shifter's own registers. <c>$FF8260</c> is written by software and read back; <c>$FF8264</c>
    /// and <c>$FF8265</c> hold the pixel offset (0–15).
    /// </summary>
    public ushort RegisterShifter(int offset, ushort value, bool isWrite)
    {
        int index = offset & 0x3F;
        if (index < 0 || index >= _shifter.Length)
        {
            return 0;
        }

        if (isWrite)
        {
            _shifter[index] = (byte)(value & 0xFF);
            Anchor(Channels.Slot(2, "shifter"), (ushort)((index << 8) | (value & 0xFF)));
            return _shifter[index];
        }

        return _shifter[index];
    }

    /// <summary>Keep the video base the System Control Unit decodes; the Shifter does not read it.</summary>
    public void RegisterBase(uint address, ushort value)
    {
        _ = address;
        _ = value;
    }

    int PixelOffset => (_shifter[0x20] & 0x0F) | (_shifter[0x21] & 0x0F);
}
