using System.Reflection;

namespace TheST.Harness;

/// <summary>
/// G4 (design/validation.md §3): the clock arithmetic is ADR 0001's. Four assertions, pure arithmetic,
/// no emulation — frame = 640 000 ticks; every clock 32 MHz ÷ 2ⁿ; the four dividers give exactly
/// 1 000 / 500 / 250 / 125 samples per frame; no period is a floating-point value.
/// </summary>
internal static class ClockChecks
{
    static int _failures;

    internal static int Run()
    {
        _failures = 0;
        Console.WriteLine("check-clocks — G4, the clock arithmetic of ADR 0001");
        Console.WriteLine();

        Frame();
        Dividers();
        SampleCounts();
        IntegerPeriods();
        Console.WriteLine();
        MfpCrystal();

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "G4 PASS" : $"G4 FAIL — {_failures} assertion(s)");
        return _failures == 0 ? 0 : 1;
    }

    // 1 — the frame is 640 000 ticks: 20.000 ms, 50.000 Hz, 312.5 lines.
    static void Frame()
    {
        Console.WriteLine("  the frame");

        Check(
            TheST.Machine.Ticks.Frame == 640_000,
            "frame = 640 000 ticks",
            $"{Program.Number(TheST.Machine.Ticks.Frame)} ticks");

        Check(
            TheST.Machine.Ticks.Frame * 50 == TheST.Machine.Ticks.Second,
            "50 frames a second exactly: 640 000 × 50 = 32 000 000",
            $"{Program.Number(TheST.Machine.Ticks.Frame)} × 50 = {Program.Number(TheST.Machine.Ticks.Second)}");

        Check(
            TheST.Machine.Ticks.Line == 2_048 && TheST.Machine.Ticks.LinesPerFrameTimes2 == 625,
            "one line = 2 048 ticks (64 µs), and a frame is 312.5 of them",
            $"{TheST.Machine.Ticks.Line} ticks/line; {TheST.Machine.Ticks.LinesPerFrameTimes2} half-lines per frame");

        Check(
            TheST.Machine.Ticks.LinesPerFrameRounded == 313,
            "a keeping counter rounds the 312.5 lines to 313",
            $"{TheST.Machine.Ticks.LinesPerFrameRounded} lines");

        Console.WriteLine();
    }

    // 2 — every clock is 32 MHz ÷ 2ⁿ, and the five are the ones ADR 0001 names.
    static void Dividers()
    {
        Console.WriteLine("  the clocks (32 000 000 ÷ 2ⁿ)");

        long[] expectedHertz = [16_000_000, 8_000_000, 8_000_000, 2_000_000, 8_000_000];
        TheST.Machine.Clock[] clocks = TheST.Machine.Clocks.All;

        bool allPowerOfTwo = clocks.Length == expectedHertz.Length;
        bool allExact = allPowerOfTwo;
        bool allExpected = allPowerOfTwo;

        for (int index = 0; index < clocks.Length && allPowerOfTwo; index++)
        {
            TheST.Machine.Clock clock = clocks[index];
            long ticksPerClock = clock.TicksPerClock;

            bool powerOfTwo = (ticksPerClock & (ticksPerClock - 1)) == 0 && ticksPerClock > 0
                && clock.Hertz == TheST.Machine.Ticks.Second / ticksPerClock;
            bool exact = clock.Hertz * ticksPerClock == TheST.Machine.Ticks.Second;
            bool expected = clock.Hertz == expectedHertz[index];

            allPowerOfTwo &= powerOfTwo;
            allExact &= exact;
            allExpected &= expected;

            Console.WriteLine(
                $"        {clock.Name,-10} 32 000 000 ÷ 2^{clock.PowerOfTwo} = " +
                $"{Program.Number(clock.Hertz),10} Hz   {ticksPerClock} ticks per clock" +
                (powerOfTwo && exact && expected ? "" : "   <-- WRONG"));
        }

        Check(
            allPowerOfTwo && allExact,
            "every clock is the oscillator divided by a power of two, exactly",
            $"{clocks.Length} clocks, each with an exact integer ticks-per-clock");

        Check(
            allExpected,
            "the five frequencies are ADR 0001's: 16, 8, 8, 2 and 8 MHz",
            "video 16 · CPU/bus 8 · sound DMA 8 · PSG 2 · floppy bit 8 MHz");

        Console.WriteLine();
    }

    // 3 — the four dividers give exactly 1 000 / 500 / 250 / 125 samples per frame.
    static void SampleCounts()
    {
        Console.WriteLine("  the four sample rates");

        long[] expectedPerFrame = [1_000, 500, 250, 125];
        TheST.Machine.SampleRate[] rates = TheST.Machine.SampleRates.All;

        bool counts = rates.Length == expectedPerFrame.Length;
        bool exact = counts;

        for (int index = 0; index < rates.Length && counts; index++)
        {
            TheST.Machine.SampleRate rate = rates[index];
            bool right = rate.SamplesPerFrame == expectedPerFrame[index]
                && rate.TicksPerSample * rate.SamplesPerFrame == TheST.Machine.Ticks.Frame;

            counts &= right;
            exact &= rate.IsExact;

            Console.WriteLine(
                $"        {Program.Number(rate.Hertz),7} Hz  ->  {rate.TicksPerSample,5} ticks per sample  ->  " +
                $"{rate.SamplesPerFrame,5} samples per frame" + (right && rate.IsExact ? "" : "   <-- WRONG"));
        }

        Check(counts, "exactly 1 000 / 500 / 250 / 125 samples per frame", "each rate's count over one 640 000-tick frame");
        Check(exact, "every rate divides both the oscillator and the frame exactly", "no accumulator, no drifting remainder");
        Console.WriteLine();
    }

    // 4 — no period is a floating-point value: the timing types carry integer ticks only.
    static void IntegerPeriods()
    {
        Console.WriteLine("  integer periods");

        List<string> offenders = [];
        Type[] types =
        [
            typeof(TheST.Machine.Ticks),
            typeof(TheST.Machine.Clock),
            typeof(TheST.Machine.Clocks),
            typeof(TheST.Machine.SampleRate),
            typeof(TheST.Machine.SampleRates),
            typeof(TheST.Machine.MfpClock),
        ];

        foreach (Type type in types)
        {
            const BindingFlags declared = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            foreach (PropertyInfo property in type.GetProperties(declared))
            {
                if (IsFloating(property.PropertyType))
                {
                    offenders.Add($"{type.Name}.{property.Name}");
                }
            }

            foreach (FieldInfo field in type.GetFields(declared))
            {
                if (IsFloating(field.FieldType))
                {
                    offenders.Add($"{type.Name}.{field.Name}");
                }
            }

            foreach (MethodInfo method in type.GetMethods(declared))
            {
                if (method.IsSpecialName && method.Name is not "op_Addition" and not "op_Subtraction")
                {
                    continue;   // property accessors were covered above
                }

                if (IsFloating(method.ReturnType) || method.GetParameters().Any(p => IsFloating(p.ParameterType)))
                {
                    offenders.Add($"{type.Name}.{method.Name}");
                }
            }
        }

        Check(
            offenders.Count == 0,
            "no floating-point period anywhere in the clock arithmetic",
            offenders.Count == 0
                ? $"0 float/double/decimal members over {types.Length} timing types"
                : string.Join(", ", offenders));

        Console.WriteLine();
    }

    // ADR 0001's revision: the MFP's 2.4576 MHz crystal is exact, so it stays integer arithmetic too.
    static void MfpCrystal()
    {
        Console.WriteLine("  the MFP crystal (ADR 0001, revision 2026-11-11)");

        Check(
            TheST.Machine.Ticks.Second * TheST.Machine.MfpClock.TicksDenominator
                == TheST.Machine.MfpClock.Hertz * TheST.Machine.MfpClock.TicksNumerator,
            "32 000 000 ÷ 2 457 600 = 625/48 exactly",
            "an exact rational, not a quantised approximation");

        long remainder = 0;
        long ticks = TheST.Machine.MfpClock.TicksFor(TheST.Machine.MfpClock.ClocksPerFrameQuarter, ref remainder);

        Check(
            ticks == 160_000 && remainder == 0 && ticks * 4 == TheST.Machine.Ticks.Frame,
            "the OS's 200 Hz tick is 12 288 MFP clocks = 160 000 ticks = four per frame",
            $"{Program.Number(ticks)} ticks, remainder {remainder}, ×4 = {Program.Number(ticks * 4)}");
    }

    static bool IsFloating(Type type) =>
        type == typeof(double) || type == typeof(float) || type == typeof(decimal)
        || (type.IsGenericType && type.GetGenericArguments().Any(IsFloating));

    static void Check(bool condition, string assertion, string measured)
    {
        Console.WriteLine($"  {(condition ? "ok  " : "FAIL")}  {assertion}");
        Console.WriteLine($"        {measured}");

        if (!condition)
        {
            _failures++;
        }
    }
}
