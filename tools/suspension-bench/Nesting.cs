// ============================================================================================
// ADR 0009's falsifier: the per-instruction cost of the three nested machines (instruction →
// instruction-specific → addressing) against a flat `switch`. Threshold: 1 CPU clock = 4 ticks =
// 125 ns at 32 MHz, i.e. 12.5 % of the cheapest instruction on this machine.
//
// This is a dispatch-shape microbenchmark, not the 68000 core: both variants advance the clock by
// the same 8 ticks per instruction and do the same arithmetic, so the difference between them is
// the cost of the nesting and nothing else.
// ============================================================================================

using System.Diagnostics;

namespace TheST.SuspensionBench;

public static class Nesting
{
    const int Instructions = 20_000_000;

    /// <summary>Nested: three state fields, three switches, three calls per instruction.</summary>
    sealed class Nested
    {
        int _instrState;      // instruction/interrupt machine
        int _opState;         // instruction-specific machine
        int _addrState;       // addressing machine
        int _ir;
        long _acc;
        public long Clock;
        public long Retired;

        public long Run(long count)
        {
            while (Retired < count) Step();
            return _acc;
        }

        void Step()
        {
            switch (_instrState)
            {
                case 0:                                  // fetch
                    _ir = (int)((_acc * 1103515245 + 12345) >> 16) & 0xFFFF;
                    Clock += 4;
                    _instrState = 1;
                    break;
                default:                                 // execute: enter the instruction-specific machine
                    Clock += OpStep();
                    if (_opState == 0 && _addrState == 0)
                    {
                        _instrState = 0;
                        Retired++;
                    }
                    break;
            }
        }

        int OpStep()
        {
            switch (_opState)
            {
                case 0:
                    _opState = 1;
                    _acc = (_acc ^ _ir) * 31;
                    return 2;
                default:
                {
                    int ticks = AddrStep();
                    if (_addrState == 0) _opState = 0;
                    return ticks;
                }
            }
        }

        int AddrStep()
        {
            switch (_addrState)
            {
                case 0:
                    _addrState = 1;
                    _acc += _ir & 7;
                    return 1;
                default:
                    _addrState = 0;
                    _acc ^= (_ir >> 3) & 0x1F;
                    return 1;
            }
        }
    }

    /// <summary>Flat: one state field, one switch, one call per instruction — the collapse the falsifier names.</summary>
    sealed class Flat
    {
        int _state;           // 0..3, one field for all three levels
        int _ir;
        long _acc;
        public long Clock;
        public long Retired;

        public long Run(long count)
        {
            while (Retired < count) Step();
            return _acc;
        }

        void Step()
        {
            switch (_state)
            {
                case 0:
                    _ir = (int)((_acc * 1103515245 + 12345) >> 16) & 0xFFFF;
                    Clock += 4;
                    _state = 1;
                    _acc = (_acc ^ _ir) * 31;
                    break;
                case 1:
                    Clock += 2;
                    _acc += _ir & 7;
                    _state = 2;
                    break;
                case 2:
                    Clock += 1;
                    _acc ^= (_ir >> 3) & 0x1F;
                    _state = 3;
                    break;
                default:
                    Clock += 1;
                    _state = 0;
                    Retired++;
                    break;
            }
        }
    }

    public static void Run()
    {
        var nested = new Nested();
        var flat = new Flat();

        // warm up both (default tiering: Tier0 then Tier1)
        nested.Run(2_000_000);
        flat.Run(2_000_000);

        var sw = Stopwatch.StartNew();
        long a = nested.Run(Instructions);
        sw.Stop();
        double nestedNs = sw.Elapsed.TotalMilliseconds * 1e6 / Instructions;

        sw.Restart();
        long b = flat.Run(Instructions);
        sw.Stop();
        double flatNs = sw.Elapsed.TotalMilliseconds * 1e6 / Instructions;

        Console.WriteLine($"MEASURE\tnesting.nested\t{nestedNs:F4}\tns/instruction\t{Instructions}\t" +
                          $"3 switches, 3 state fields, {nested.Clock / Instructions} ticks/instr, sink={a & 0xFF:x2}");
        Console.WriteLine($"MEASURE\tnesting.flat\t{flatNs:F4}\tns/instruction\t{Instructions}\t" +
                          $"1 switch, 1 state field, {flat.Clock / Instructions} ticks/instr, sink={b & 0xFF:x2}");
        Console.WriteLine($"MEASURE\tnesting.delta\t{nestedNs - flatNs:F4}\tns/instruction\t{Instructions}\t" +
                          "threshold 125 ns (1 CPU clock, 4 ticks)");
    }
}
