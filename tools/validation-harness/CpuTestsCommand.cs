using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TheST.Machine;
using TheST.Machine.Corpus;
using TheST.Machine.Cpu;

namespace TheST.Harness;

/// <summary>
/// <c>cpu-tests</c> — the 68000 core against the vendored 680x0 corpus (<c>tests/680x0/</c>, ticket #34).
///
/// <para>
/// Each test is one instruction in isolation: the corpus hands over the registers, both stack pointers,
/// the status register, the program counter and a sparse byte map, and expects the same state back
/// after exactly one instruction. The bus it runs against is <see cref="SingleStepBus"/> — plain 24-bit
/// big-endian RAM, no beam, no counters — so a failure is the core's and not a chip's.
/// </para>
///
/// <para>
/// What is gated: every register (D0–D7, A0–A6, USP, SSP), the status register's architecturally
/// defined bits, the program counter, and every byte of the expected final memory map. What is
/// <b>reported and never gated</b>: the corpus's <c>length</c> (CPU clock cycles), because this core
/// models the bus's cycles and not the instruction's internal ones — the fidelity gap the rulings file
/// records. Bus-cycle counts are printed beside it so the divergence is visible rather than implied.
/// </para>
/// </summary>
internal static class CpuTestsCommand
{
    const string DefaultDirectory = "tests/680x0/v1";

    /// <summary>The bits of SR a 68000 defines: T, S, I2–I0 and the condition codes.</summary>
    const ushort DefinedSr = 0xA71F;

    internal static int Run(string[] args)
    {
        string directory = DefaultDirectory;
        string? only = null;
        int limit = int.MaxValue;
        int show = 10;
        bool quiet = false;
        string? outPath = null;
        string? failurePath = null;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--dir":
                    if (++index >= args.Length) return Program.Unknown(args[index - 1]);
                    directory = args[index];
                    break;

                case "--file":
                    if (++index >= args.Length) return Program.Unknown(args[index - 1]);
                    only = args[index];
                    break;

                case "--limit":
                    if (++index >= args.Length || !int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out limit) || limit <= 0)
                    {
                        return Program.Unknown(args[Math.Max(index - 1, 0)]);
                    }

                    break;

                case "--show":
                    if (++index >= args.Length || !int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out show) || show < 0)
                    {
                        return Program.Unknown(args[Math.Max(index - 1, 0)]);
                    }

                    break;

                case "--quiet":
                    quiet = true;
                    break;

                case "--out":
                    if (++index >= args.Length) return Program.Unknown(args[index - 1]);
                    outPath = args[index];
                    break;

                case "--failures":
                    if (++index >= args.Length) return Program.Unknown(args[index - 1]);
                    failurePath = args[index];
                    break;

                default:
                    return Program.Unknown(args[index]);
            }
        }

        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"cpu-tests FAILED — no corpus at {directory} (scripts/fetch-680x0-tests.sh fetches it)");
            return 1;
        }

        List<string> files = Directory
            .GetFiles(directory)
            .Where(path => path.EndsWith(".json", StringComparison.Ordinal) || path.EndsWith(".json.gz", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        if (only is not null)
        {
            files = files.Where(path => OpcodeName(path).Contains(only, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (files.Count == 0)
        {
            Console.Error.WriteLine($"cpu-tests FAILED — no test file matches '{only}' in {directory}");
            return 1;
        }

        var failures = new List<string>();
        var failureLines = new List<string>();
        var byKind = new Dictionary<string, int>(StringComparer.Ordinal);
        var report = new StringBuilder();
        long total = 0;
        long passed = 0;
        long failed = 0;
        long busCycles = 0;
        long expectedCycles = 0;
        long exactCycles = 0;

        foreach (string file in files)
        {
            string name = OpcodeName(file);
            var (fileTotal, filePassed, fileFailures, fileBusCycles, fileExpectedCycles, fileExactCycles) =
                RunFile(file, limit, show, byKind);

            total += fileTotal;
            passed += filePassed;
            failed += fileTotal - filePassed;
            busCycles += fileBusCycles;
            expectedCycles += fileExpectedCycles;
            exactCycles += fileExactCycles;
            failures.AddRange(fileFailures);

            foreach (string failure in fileFailures)
            {
                string[] parts = failure.Split('\n');
                failureLines.Add($"{name}\t{parts[0]}\t{(parts.Length > 1 ? parts[1].Trim() : string.Empty)}");
            }

            string verdict = filePassed == fileTotal ? "ok  " : "FAIL";

            if (!quiet)
            {
                Console.WriteLine($"{verdict} {name,-12} {filePassed,6}/{fileTotal,-6}" +
                    (fileTotal == filePassed
                        ? string.Empty
                        : "  " + fileFailures.FirstOrDefault()?.Replace('\n', ' ')));
            }

            report.AppendLine($"{verdict} {name} {filePassed}/{fileTotal}");
        }

        Console.WriteLine();
        Console.WriteLine($"cpu-tests: {Program.Number(passed)} / {Program.Number(total)} passed, {Program.Number(failed)} failed");
        Console.WriteLine($"  corpus directory        {directory}");
        Console.WriteLine($"  opcode files            {files.Count}");
        Console.WriteLine($"  bus cycles              {Program.Number(busCycles)} over {Program.Number(total)} instructions");
        Console.WriteLine($"  corpus length (clocks)  {Program.Number(expectedCycles)}");
        Console.WriteLine($"  exact-cycle tests       {Program.Number(exactCycles)} (reported, never gated: the core models the bus's cycles only)");

        if (byKind.Count > 0)
        {
            Console.WriteLine("  mismatch kinds (the first differing field of each failed test):");
            foreach (var pair in byKind.OrderByDescending(pair => pair.Value))
            {
                Console.WriteLine($"    {pair.Key,-24} {Program.Number(pair.Value)}");
            }
        }

        if (failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  the first {Math.Min(show, failures.Count)} failures:");
            foreach (string failure in failures.Take(show))
            {
                Console.WriteLine();
                foreach (string line in failure.Split('\n'))
                {
                    Console.WriteLine($"    {line}");
                }
            }
        }

        if (failurePath is not null)
        {
            File.WriteAllLines(failurePath, failureLines);
            Console.WriteLine($"wrote {failurePath} ({Program.Number(failureLines.Count)} failures)");
        }

        if (outPath is not null)
        {
            report.Insert(0, $"cpu-tests: {passed}/{total} passed, {failed} failed\n\n");
            File.WriteAllText(outPath, report.ToString());
            Console.WriteLine($"wrote {outPath}");
        }

        return failed == 0 ? 0 : 1;
    }

    static string OpcodeName(string path)
    {
        string name = Path.GetFileName(path);
        if (name.EndsWith(".json.gz", StringComparison.Ordinal)) name = name[..^".json.gz".Length];
        else if (name.EndsWith(".json", StringComparison.Ordinal)) name = name[..^".json".Length];
        return name;
    }

    static (long Total, long Passed, List<string> Failures, long BusCycles, long ExpectedCycles, long ExactCycles) RunFile(
        string path,
        int limit,
        int show,
        Dictionary<string, int> byKind)
    {
        using Stream file = File.OpenRead(path);
        using Stream stream = path.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(file, CompressionMode.Decompress) : file;
        using JsonDocument document = JsonDocument.Parse(stream);

        var engine = new Engine();
        var bus = new SingleStepBus(engine, 1);
        var cpu = new M68000(engine, 2);
        engine.Add(bus);
        engine.Add(cpu);
        bus.Launch();

        long total = 0;
        long passed = 0;
        long busCycles = 0;
        long expectedCycles = 0;
        long exactCycles = 0;
        var failures = new List<string>();
        var failureLines = new List<string>();

        foreach (JsonElement test in document.RootElement.EnumerateArray())
        {
            if (total >= limit)
            {
                break;
            }

            total++;
            JsonElement initial = test.GetProperty("initial");
            JsonElement final = test.GetProperty("final");
            string name = test.GetProperty("name").GetString() ?? "(unnamed)";
            int length = test.GetProperty("length").GetInt32();

            uint pc = initial.GetProperty("pc").GetUInt32();

            // the corpus's base memory: zero everywhere, with the instruction stream's first two words
            // at the program counter
            JsonElement prefetch = initial.GetProperty("prefetch");
            bus.PokeWord(pc, (ushort)prefetch[0].GetUInt32());
            bus.PokeWord(pc + 2, (ushort)prefetch[1].GetUInt32());

            foreach (JsonElement pair in initial.GetProperty("ram").EnumerateArray())
            {
                bus.Poke(pair[0].GetUInt32(), (byte)pair[1].GetUInt32());
            }

            var state = new M68000.SingleStepState(
                Registers(initial, "d"),
                Registers(initial, "a", 7),
                initial.GetProperty("usp").GetUInt32(),
                initial.GetProperty("ssp").GetUInt32(),
                (ushort)initial.GetProperty("sr").GetUInt32(),
                pc);

            cpu.Load(state);

            long cyclesBefore = cpu.BusCycles;
            string? mismatch;
            try
            {
                mismatch = Step(cpu, engine, bus, final);
            }
            catch (Exception failure)
            {
                mismatch = $"harness: {failure.GetType().Name}: {failure.Message}";
            }

            busCycles += cpu.BusCycles - cyclesBefore;
            expectedCycles += length;
            if ((cpu.BusCycles - cyclesBefore) == length)
            {
                exactCycles++;
            }

            if (mismatch is null)
            {
                passed++;
            }
            else
            {
                string kind = mismatch.Split(' ')[0];
                byKind[kind] = byKind.GetValueOrDefault(kind) + 1;
                if (failures.Count < Math.Max(show, 20))
                {
                    failures.Add($"{name}\n  {mismatch}");
                }
            }

            bus.Clear();
        }

        return (total, passed, failures, busCycles, expectedCycles, exactCycles);
    }

    /// <summary>A0–A6 (or D0–D7) — the corpus keeps A7 out of the set and splits it into usp/ssp.</summary>
    static uint[] Registers(JsonElement state, string prefix, int count = 8)
    {
        var values = new uint[8];
        for (int index = 0; index < count; index++)
        {
            values[index] = state.GetProperty(prefix + index.ToString(CultureInfo.InvariantCulture)).GetUInt32();
        }

        return values;
    }

    /// <summary>
    /// Runs exactly one instruction and returns null when the state is the corpus's, or the first
    /// difference in the corpus's own vocabulary ("d3", "sr", "pc", "ram", "cycles").
    /// </summary>
    static string? Step(M68000 cpu, Engine engine, SingleStepBus bus, JsonElement final)
    {
        ValueTask step = cpu.StepAsync();
        int guard = 0;

        while (!step.IsCompleted)
        {
            engine.Run(long.MaxValue);

            if (step.IsCompleted)
            {
                break;
            }

            if (engine.QueueCount == 0)
            {
                return $"stalled at pc={cpu.PC:x6}: nothing is queued and the instruction is not finished";
            }

            if (++guard > 100_000)
            {
                return $"stalled at pc={cpu.PC:x6}: the instruction did not finish in 100 000 engine passes";
            }
        }

        step.GetAwaiter().GetResult();

        M68000.SingleStepResult state = cpu.Capture();

        for (int index = 0; index < 8; index++)
        {
            uint wanted = final.GetProperty("d" + index.ToString(CultureInfo.InvariantCulture)).GetUInt32();
            if (state.D[index] != wanted)
            {
                return $"d{index} {state.D[index]:x8} (corpus {wanted:x8})";
            }
        }

        // A0–A6 are their own registers; A7 is whichever stack pointer the status register selects, and
        // the corpus checks it that way too — both stack pointers, and the live one is the one compared.
        for (int index = 0; index < 7; index++)
        {
            uint wanted = final.GetProperty("a" + index.ToString(CultureInfo.InvariantCulture)).GetUInt32();
            if (state.A[index] != wanted)
            {
                return $"a{index} {state.A[index]:x8} (corpus {wanted:x8})";
            }
        }

        uint wantedUsp = final.GetProperty("usp").GetUInt32();
        if (state.Usp != wantedUsp)
        {
            return $"usp {state.Usp:x8} (corpus {wantedUsp:x8})";
        }

        uint wantedSsp = final.GetProperty("ssp").GetUInt32();
        if (state.Ssp != wantedSsp)
        {
            return $"ssp {state.Ssp:x8} (corpus {wantedSsp:x8})";
        }

        ushort wantedSr = (ushort)final.GetProperty("sr").GetUInt32();
        if ((state.Sr & DefinedSr) != (wantedSr & DefinedSr))
        {
            return $"sr {state.Sr:x4} (corpus {wantedSr:x4})";
        }

        uint wantedPc = final.GetProperty("pc").GetUInt32();
        if (state.Pc != wantedPc)
        {
            return $"pc {state.Pc:x6} (corpus {wantedPc:x6})";
        }

        foreach (JsonElement pair in final.GetProperty("ram").EnumerateArray())
        {
            uint address = pair[0].GetUInt32() & 0xFFFFFF;
            byte wanted = (byte)pair[1].GetUInt32();
            byte got = bus.Read(address);
            if (got != wanted)
            {
                return $"ram {address:x6} {got:x2} (corpus {wanted:x2})";
            }
        }

        return null;
    }
}
