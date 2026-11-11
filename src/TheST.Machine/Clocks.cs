// ============================================================================================
// The clock arithmetic of ADR 0001 — read this file before writing anything that touches time.
//
// One idealised 32 MHz oscillator. Every clock is 32 MHz ÷ 2ⁿ. One line is 2 048 ticks, one frame is
// 640 000 ticks (20.000 ms, 50.000 Hz). Every period is a whole number of ticks: there is no
// floating-point period anywhere in the model, and no instant is ever converted between clock
// domains (machine time is the only timeline). G4 (`check-clocks`) asserts exactly this file.
//
// The one rate that is not a power-of-two division — the MFP's 2.4576 MHz crystal — is carried as an
// exact rational (ADR 0001's revision of 2026-11-11), so it stays integer arithmetic too.
// ============================================================================================

namespace TheST.Machine;

/// <summary>
/// Machine time, in ticks of the idealised 32 MHz oscillator.
/// </summary>
public static class Ticks
{
    /// <summary>The idealised master frequency. The real PAL crystal is 32.084988 MHz; ADR 0001 accepts the divergence.</summary>
    public const long Second = 32_000_000;

    /// <summary>One PAL scanline, 64 µs.</summary>
    public const long Line = 2_048;

    /// <summary>One PAL frame: 20.000 ms at 50.000 Hz.</summary>
    public const long Frame = 640_000;

    /// <summary>A frame is 312.5 lines, kept integral as 625 half-lines; a keeping counter rounds to 313 (ADR 0001).</summary>
    public const long LinesPerFrameTimes2 = 2 * Frame / Line;

    /// <summary>The line count a keeping counter uses: 313 (312.5 rounded up, ADR 0001).</summary>
    public const int LinesPerFrameRounded = (int)((LinesPerFrameTimes2 + 1) / 2);
}

/// <summary>
/// One clock of the machine: the oscillator divided by 2ⁿ. A clock is a phase, not a timeline — it
/// produces no records and no subscriptions (ADR 0006, class 3).
/// </summary>
public readonly record struct Clock(string Name, int PowerOfTwo)
{
    /// <summary>Ticks of the oscillator per edge of this clock.</summary>
    public long TicksPerClock => 1L << PowerOfTwo;

    /// <summary>This clock's frequency in Hz. Exact by construction: the oscillator's 2⁵·10⁶ divides by 2ⁿ.</summary>
    public long Hertz => Ticks.Second >> PowerOfTwo;
}

/// <summary>The five clocks ADR 0001 derives. Each is 32 000 000 ÷ 2ⁿ, and that is the whole table.</summary>
public static class Clocks
{
    public static readonly Clock Video = new("video", 1);          // 16 MHz
    public static readonly Clock CpuBus = new("CPU/bus", 2);       //  8 MHz
    public static readonly Clock SoundDma = new("sound DMA", 2);   //  8 MHz
    public static readonly Clock Psg = new("PSG", 4);              //  2 MHz
    public static readonly Clock FloppyBit = new("floppy bit", 2); //  8 MHz

    public static readonly Clock[] All = [Video, CpuBus, SoundDma, Psg, FloppyBit];
}

/// <summary>
/// One DMA sample rate. 32 000 000 / Hertz is the period in ticks — a whole number for the four rates
/// ADR 0001 chose, which is why the four counts per frame are 1 000 / 500 / 250 / 125 exactly.
/// </summary>
public readonly record struct SampleRate(int Hertz)
{
    /// <summary>Ticks between two samples (integer: the whole point of the idealised timebase).</summary>
    public long TicksPerSample => Ticks.Second / Hertz;

    /// <summary>Samples in one frame: 640 000 ÷ ticks-per-sample.</summary>
    public long SamplesPerFrame => Ticks.Frame / TicksPerSample;

    /// <summary>True when both divisions are exact — i.e. no accumulator is needed and no drift exists.</summary>
    public bool IsExact => Ticks.Second % Hertz == 0 && Ticks.Frame % TicksPerSample == 0;
}

/// <summary>The four DMA sample rates (ADR 0001). 125/250/500/1 000 samples per frame, exactly.</summary>
public static class SampleRates
{
    public static readonly SampleRate Hz50k = new(50_000);  // 1 000 per frame
    public static readonly SampleRate Hz25k = new(25_000);  //   500 per frame
    public static readonly SampleRate Hz125k = new(12_500); //   250 per frame
    public static readonly SampleRate Hz6250 = new(6_250);  //   125 per frame

    public static readonly SampleRate[] All = [Hz50k, Hz25k, Hz125k, Hz6250];
}

/// <summary>
/// The MFP's own crystal, 2.4576 MHz, which is not a division of the oscillator (ADR 0001's revision
/// of 2026-11-11). The ratio is exact — 32 000 000 / 2 457 600 = 625/48 — so a counter keeps its
/// position in forty-eighths of a tick and an expiry is rounded up to a whole tick with the remainder
/// carried. Every instant in the queue stays an integer.
/// </summary>
public static class MfpClock
{
    public const long Hertz = 2_457_600;

    /// <summary>Oscillator ticks per MFP clock, as the exact fraction 625/48.</summary>
    public const long TicksNumerator = 625;
    public const long TicksDenominator = 48;

    /// <summary>The OS's 200 Hz tick: 12 288 MFP clocks, i.e. 160 000 ticks — exactly four per frame.</summary>
    public const long ClocksPerFrameQuarter = 12_288;

    /// <summary>
    /// Whole oscillator ticks for <paramref name="count"/> MFP clocks, carrying the remainder (in
    /// 1/48 tick) in <paramref name="remainder"/> so a long series of expiries never drifts.
    /// </summary>
    public static long TicksFor(long count, ref long remainder)
    {
        long scaled = count * TicksNumerator + remainder;
        remainder = scaled % TicksDenominator;
        return scaled / TicksDenominator;
    }
}
