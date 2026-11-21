// cpu-dissection — ticket #47's measurement, run as one process so host drift cannot reach a delta.
//
//   check             one pass of each variant; the counts and the final register state must agree
//   bench [passes]    alternating adjacent passes over the four variants, ns per bus cycle
//   attr              variant A only: what a suspension costs, by site and by opcode line, and the
//                     size of every state machine the CPU boxes
//
// The denominator is bus cycles and emulated instructions, never frames: variant C never waits, so it
// executes a different number of frames per second than the machine does.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using TheST.Machine;
using CpuA = TheST.Machine.Cpu.M68000;
using CpuC = TheST.Machine.Cpu.Sync.M68000;
using CpuD = TheST.Machine.Cpu.Folded.M68000;

namespace TheST.Dissect;

internal static class Program
{
    const int TraceCycles = 300_000;      // ~16 frames of the boot: long enough to settle, short enough to repeat

    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "check";
        string directory = Environment.GetEnvironmentVariable("ST_DISSECT_DIR") ?? "/opt/data/cache/scratch/dissect";

        BusTrace full = BusTrace.Load(directory);
        Console.WriteLine($"trace\tcycles={full.Count}\tipl={full.IplCount}");

        BusTrace trace = full;   // a replay must start where the boot did: the state at cycle n needs 0..n-1

        switch (mode)
        {
            case "check":
                Check(trace);
                break;

            case "bench":
            case "full":
                Bench(trace, args.Length > 1 ? int.Parse(args[1]) : 4, mode == "full");
                break;

            case "attr":
                Attributes(trace, full);
                break;

            default:
                Console.WriteLine("modes: check | bench [passes] | full [passes] | attr");
                break;
        }
    }

    /// <summary>Every variant declares its own <c>Launch</c> (it is the M68000 source's own method).</summary>
    static void Launch(Chip cpu) => cpu.GetType().GetMethod("Launch")!.Invoke(cpu, null);

    // ---- one pass of one variant -----------------------------------------------------------

    readonly record struct Pass(string Name, long BusCycles, long Instructions, uint Pc, ushort Sr,
        uint D0, uint A7, long Resumes, long AllocatedBytes, double Ms)
    {
        public double NsPerCycle => Ms * 1e6 / Math.Max(BusCycles, 1);

        public double NsPerInstruction => Ms * 1e6 / Math.Max(Instructions, 1);
    }

    static Pass RunVariant(string name, BusTrace trace, Mode mode)
    {
        var engine = new Engine(trace, mode);
        var cpu = name switch
        {
            "C" => new CpuC(engine, 10),
            "D" => new CpuD(engine, 10),
            _ => (Chip)new CpuA(engine, 10),
        };

        long allocated = GC.GetAllocatedBytesForCurrentThread();
        CpuCounters counters = CpuCounters.Of(cpu);
        var watch = Stopwatch.StartNew();
        try
        {
            Launch(cpu);
            engine.Run();
        }
        catch (Exception end)
        {
            Exception inner = end is System.Reflection.TargetInvocationException { InnerException: { } wrapped } ? wrapped : end;
            if (inner is not EndOfTraceException)
            {
                throw;
            }
        }

        watch.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        return new Pass(name, counters.BusCycles, counters.Instructions, counters.Pc, counters.Sr,
            counters.D0, counters.A7, engine.Resumes, allocated, watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>The CPU's own reported counters, read through one shape for all four variants.</summary>
    readonly struct CpuCounters
    {
        readonly Chip _cpu;
        readonly object? _instruments;
        readonly PropertyInfo? _instructions, _busCycles, _pc, _sr, _d0, _a7;

        CpuCounters(Chip cpu)
        {
            _cpu = cpu;
            Type type = cpu.GetType();
            // the counters are the emulator's interface since #85: `cpu.Instruments.BusCycles`
            PropertyInfo? instruments = type.GetProperty("Emulator");
            _instruments = instruments?.GetValue(cpu);
            Type counters = instruments?.PropertyType ?? type;
            _instructions = counters.GetProperty("Instructions");
            _busCycles = counters.GetProperty("BusCycles");
            _pc = type.GetProperty("PC");
            _sr = type.GetProperty("SR");
            // the manual's own names since #85: D0 and A7 are views, and the probes that were
            // PeekD/PeekA now read the programmer's model directly
            _d0 = type.GetProperty("D0");
            _a7 = type.GetProperty("A7");
        }

        public static CpuCounters Of(Chip cpu) => new(cpu);

        public long Instructions => (long)(_instructions!.GetValue(_instruments) ?? 0L);
        public long BusCycles => (long)(_busCycles!.GetValue(_instruments) ?? 0L);
        public uint Pc => (uint)(_pc!.GetValue(_cpu) ?? 0u);
        public ushort Sr => (ushort)(_sr!.GetValue(_cpu) ?? (ushort)0);
        public uint D0 => (uint)(_d0!.GetValue(_cpu) ?? 0u);
        public uint A7 => (uint)(_a7!.GetValue(_cpu) ?? 0u);
    }

    // ---- check -----------------------------------------------------------------------------

    static void Check(BusTrace trace)
    {
        Console.WriteLine("variant\tbus_cycles\tinstructions\tpc\tsr\td0\ta7\tresumes");
        Pass[] passes =
        [
            RunVariant("A", trace, Mode.Suspending),
            RunVariant("B", trace, Mode.Inline),
            RunVariant("C", trace, Mode.Suspending),
            RunVariant("D", trace, Mode.Suspending),
        ];

        foreach (Pass pass in passes)
        {
            Console.WriteLine($"{pass.Name}\t{pass.BusCycles}\t{pass.Instructions}\t{pass.Pc:x6}\t{pass.Sr:x4}\t{pass.D0:x8}\t{pass.A7:x8}\t{pass.Resumes}");
        }

        Pass first = passes[0];
        bool same = passes.All(p => p.BusCycles == first.BusCycles && p.Instructions == first.Instructions
            && p.Pc == first.Pc && p.Sr == first.Sr && p.D0 == first.D0 && p.A7 == first.A7);
        Console.WriteLine($"same_work\t{(same ? "yes" : "NO")}\tcycles={first.BusCycles}\tinstructions={first.Instructions}" +
            $"\ttrace_cycles={trace.Count}");
        if (first.BusCycles != trace.Count)
        {
            Console.WriteLine($"MISMATCH\t{first.BusCycles} bus cycles replayed of {trace.Count} in the trace");
        }
    }

    // ---- bench -----------------------------------------------------------------------------

    static void Bench(BusTrace trace, int passes, bool full)
    {
        // warm-up: the JIT tiers are part of every variant equally, and the shipping configuration is
        // default tiering, so a cold reading is not the honest one
        RunVariant("A", trace, Mode.Suspending);
        RunVariant("B", trace, Mode.Inline);
        RunVariant("C", trace, Mode.Suspending);
        RunVariant("D", trace, Mode.Suspending);

        string[] order = ["A", "B", "C", "D"];
        var results = new Dictionary<string, List<Pass>>();
        foreach (string name in order)
        {
            results[name] = [];
        }

        for (int pass = 0; pass < passes; pass++)
        {
            string[] thisPass = pass % 2 == 0 ? order : order.Reverse().ToArray();
            foreach (string name in thisPass)
            {
                Pass result = RunVariant(name, trace, name == "B" ? Mode.Inline : Mode.Suspending);
                results[name].Add(result);
            }
        }

        foreach (string name in order)
        {
            List<Pass> list = results[name];
            double ms = Median(list.Select(p => p.Ms).ToArray());
            double perCycle = ms * 1e6 / list[0].BusCycles;
            double perInstruction = ms * 1e6 / list[0].Instructions;
            double allocated = Median(list.Select(p => (double)p.AllocatedBytes).ToArray());
            Console.WriteLine(
                $"MEASURE\t{name}.ms\t{ms:F3}\tms/pass\t{list.Count}\t{string.Join(",", list.Select(p => p.Ms.ToString("F2")))}" +
                $"\tcycles={list[0].BusCycles}\tinstructions={list[0].Instructions}");
            Console.WriteLine($"MEASURE\t{name}.ns_per_bus_cycle\t{perCycle:F1}\tns/cycle\t{list[0].BusCycles}\tmedian of {list.Count} passes");
            Console.WriteLine($"MEASURE\t{name}.ns_per_instruction\t{perInstruction:F1}\tns/instruction\t{list[0].Instructions}\tmedian of {list.Count} passes");
            Console.WriteLine($"MEASURE\t{name}.bytes_per_pass\t{allocated:F0}\tB/pass\t{list.Count}\tallocation on the run's own thread");
            Console.WriteLine($"MEASURE\t{name}.resumes\t{list[0].Resumes}\tcount\t1\tone a bus cycle plus the stop-waits");
        }

        // per-pair deltas, each against the pass it was adjacent to — the whole point of alternating
        if (full || passes > 0)
        {
            Report("B", "A", results);
            Report("C", "B", results);
            Report("A", "D", results);
            Report("C", "A", results);
        }
    }

    static void Report(string minus, string plus, Dictionary<string, List<Pass>> results)
    {
        List<Pass> a = results[plus];
        List<Pass> b = results[minus];
        var deltas = new List<double>();
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            deltas.Add(a[i].Ms - b[i].Ms);
        }

        double mean = deltas.Average();
        int positive = deltas.Count(d => d > 0);
        Console.WriteLine(
            $"MEASURE\tdelta.{plus}-{minus}.ms\t{mean:F3}\tms/pass\t{deltas.Count}\t{string.Join(",", deltas.Select(d => d.ToString("F2")))}" +
            $"\t{positive}/{deltas.Count} positive\tper_bus_cycle={mean * 1e6 / a[0].BusCycles:F1} ns");
    }

    static double Median(double[] values)
    {
        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    // ---- attribution -----------------------------------------------------------------------

    static void Attributes(BusTrace trace, BusTrace full)
    {
        Console.WriteLine("state machines the CPU declares (managed size, boxed with a 16-byte header):");
        long total = 0;
        foreach (Type nested in typeof(CpuA).GetNestedTypes(BindingFlags.NonPublic))
        {
            if (!nested.IsValueType)
            {
                continue;
            }

            int size = SizeOf(nested);
            int boxed = ((size + 16 + 7) / 8) * 8;
            total += boxed;
            Console.WriteLine($"  {nested.Name,-40} {size,4} B struct → {boxed,4} B boxed");
        }

        Console.WriteLine($"  {"sum of every box the class can make",-40}      → {total,4} B");

        var engine = new Engine(trace) { Attr = true };
        var cpu = new CpuA(engine, 10);
        cpu.Emulator.Observer = (_, opcode, _) =>
        {
            engine.CurrentLine = opcode >> 12;
            engine.LineInstructions[opcode >> 12]++;
        };

        var watch = Stopwatch.StartNew();
        try
        {
            Launch(cpu);
            engine.Run();
        }
        catch (Exception end)
        {
            Exception inner = end is System.Reflection.TargetInvocationException { InnerException: { } wrapped } ? wrapped : end;
            if (inner is not EndOfTraceException)
            {
                throw;
            }
        }

        watch.Stop();

        Console.WriteLine($"attribution pass: {watch.Elapsed.TotalMilliseconds:F0} ms over {engine.Suspensions} suspensions " +
            $"({engine.Resumes} resumes), {trace.Count} bus cycles");

        Console.WriteLine("site\tsuspensions\tbytes/suspension\ttotal_MB");
        for (int site = 0; site < Engine.SiteNames.Length; site++)
        {
            long count = engine.SiteCount[site];
            Console.WriteLine($"{Engine.SiteNames[site]}\t{count}\t{(count == 0 ? 0 : (double)engine.SiteBytes[site] / count):F1}\t{engine.SiteBytes[site] / 1048576.0:F1}");
        }

        Console.WriteLine("bytes a suspension allocated, histogram (the box of every suspended frame plus its");
        Console.WriteLine("continuation Action plus the Wake; the first suspension of a run is not counted):");
        foreach ((long bytes, long count) in engine.SuspensionBytes.OrderByDescending(pair => pair.Value).Take(20))
        {
            Console.WriteLine($"  {count,9} suspensions at {bytes,6} B   = {bytes / (double)count,6:F1} B");
        }

        Console.WriteLine($"the async chain a sampled suspension stands on (one in {Engine.ChainSampleEvery}):");
        foreach ((string chain, long count) in engine.Chains.OrderByDescending(pair => pair.Value).Take(12))
        {
            int frames = chain.Split('<').Length;
            Console.WriteLine($"  {count,6} x  {frames} frames  {chain}");
        }

        long beforeDelegate = GC.GetAllocatedBytesForCurrentThread();
        Action probe = Noop;
        GC.KeepAlive(probe);
        Console.WriteLine($"an Action allocation: {GC.GetAllocatedBytesForCurrentThread() - beforeDelegate} B");

        Console.WriteLine("line\tinstructions\tsuspensions\tsusp/ins\tbytes/suspension");
        for (int line = 0; line < 16; line++)
        {
            long count = engine.LineCount[line];
            long instructions = engine.LineInstructions[line];
            Console.WriteLine($"{line:x}\t{instructions}\t{count}\t{(instructions == 0 ? 0 : (double)count / instructions):F2}\t{(count == 0 ? 0 : (double)engine.LineBytes[line] / count):F1}");
        }

        // the same suspension, priced by the whole-boot allocation line: variant A's own allocation over
        // the slice, divided by the suspensions it made
        Pass a = RunVariant("A", trace, Mode.Suspending);
        Console.WriteLine($"A allocation {a.AllocatedBytes} B over {a.Resumes} resumes = {a.AllocatedBytes / (double)a.Resumes:F1} B a resume " +
            $"({a.AllocatedBytes / 1048576.0:F1} MB for {trace.Count} bus cycles); D {RunVariant("D", trace, Mode.Suspending).AllocatedBytes / 1048576.0:F1} MB");

        _ = full;
    }

    static void Noop()
    {
    }

    static int SizeOf(Type type)
    {
        MethodInfo method = typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type);
        return (int)method.Invoke(null, null)!;
    }
}
