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
        int traceIndex = -1;
        string? readPath = null;
        string? clocksPath = null;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--clocks":
                    if (++index >= args.Length) return Program.Unknown(args[index - 1]);
                    clocksPath = args[index];
                    break;

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

                case "--reads":
                    if (++index >= args.Length) return Program.Unknown(args[index - 1]);
                    readPath = args[index];
                    break;

                case "--trace":
                    if (++index >= args.Length || !int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out traceIndex) || traceIndex < 0)
                    {
                        return Program.Unknown(args[Math.Max(index - 1, 0)]);
                    }

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
            .Where(LooksLikeTests)
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

        if (traceIndex >= 0)
        {
            return TraceRecord(files[0], traceIndex);
        }

        if (readPath is not null)
        {
            return ReadCounts(files[0], readPath);
        }

        var failures = new List<string>();
        var failureLines = new List<string>();
        var byKind = new Dictionary<string, int>(StringComparer.Ordinal);
        var report = new StringBuilder();
        long total = 0;
        long passed = 0;
        long failed = 0;
        ClockDump = clocksPath is null ? null : new StreamWriter(clocksPath);
        ClockDump?.WriteLine("file\tname\tlength\tour_bus\tour_internal\tour_total");
        long busCycles = 0;
        long internalClocks = 0;
        long expectedCycles = 0;
        long exactCycles = 0;
        long exactClocks = 0;

        foreach (string file in files)
        {
            string name = OpcodeName(file);
            var (fileTotal, filePassed, fileFailures, fileBusCycles, fileInternalClocks, fileExpectedCycles,
                fileExactCycles, fileExactClocks) = RunFile(file, limit, show, byKind);

            total += fileTotal;
            passed += filePassed;
            failed += fileTotal - filePassed;
            busCycles += fileBusCycles;
            internalClocks += fileInternalClocks;
            expectedCycles += fileExpectedCycles;
            exactCycles += fileExactCycles;
            exactClocks += fileExactClocks;
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
        Console.WriteLine($"  internal clocks         {Program.Number(internalClocks)} (the work the core charges between bus cycles)");
        Console.WriteLine($"  corpus length (clocks)  {Program.Number(expectedCycles)}");
        Console.WriteLine($"  exact-cycle tests       {Program.Number(exactCycles)} (bus cycles alone; reported, never gated)");
        Console.WriteLine($"  exact-clock tests       {Program.Number(exactClocks)} (4 x bus + internal == length: " +
            $"{100.0 * exactClocks / Math.Max(1, total):f1} %)");

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

        ClockDump?.Dispose();
        ClockDump = null;

        return failed == 0 ? 0 : 1;
    }

    /// <summary>The per-record clock dump <c>--clocks</c> writes: one line per record, for the
    /// calibration the timing table needs (name, the corpus's length, our bus cycles and internal
    /// clocks, and the total the two add up to).</summary>
    static StreamWriter? ClockDump;

    /// <summary>
    /// A corpus directory may also carry the decode map (<c>68000.official.json</c>), which is an object:
    /// only a file whose JSON opens with an array is a test file.
    /// </summary>
    static bool LooksLikeTests(string path)
    {
        using Stream file = File.OpenRead(path);
        using Stream stream = path.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(file, CompressionMode.Decompress) : file;

        int first = stream.ReadByte();
        while (first is ' ' or '\n' or '\r' or '\t')
        {
            first = stream.ReadByte();
        }

        return first == '[';
    }

    static string OpcodeName(string path)
    {
        string name = Path.GetFileName(path);
        if (name.EndsWith(".json.gz", StringComparison.Ordinal)) name = name[..^".json.gz".Length];
        else if (name.EndsWith(".json", StringComparison.Ordinal)) name = name[..^".json".Length];
        return name;
    }

    static (long Total, long Passed, List<string> Failures, long BusCycles, long InternalClocks, long ExpectedCycles,
        long ExactCycles, long ExactClocks) RunFile(
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
        long internalClocks = 0;
        long expectedCycles = 0;
        long exactCycles = 0;
        long exactClocks = 0;
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
                pc,
                (ushort)prefetch[0].GetUInt32(),
                (ushort)prefetch[1].GetUInt32());

            cpu.Load(state);

            long cyclesBefore = cpu.BusCycles;
            long internalBefore = cpu.InternalClocks;
            string? mismatch;
            try
            {
                mismatch = Step(cpu, engine, bus, final);
            }
            catch (Exception failure)
            {
                mismatch = $"harness: {failure.GetType().Name}: {failure.Message}";
            }

            long ourBus = cpu.BusCycles - cyclesBefore;
            long ourInternal = cpu.InternalClocks - internalBefore;
            ClockDump?.WriteLine($"{OpcodeName(path)}\t{name}\t{length}\t{ourBus}\t{ourInternal}\t{ourBus * 4 + ourInternal}");
            busCycles += ourBus;
            internalClocks += ourInternal;
            expectedCycles += length;
            if (ourBus == length)
            {
                exactCycles++;
            }
            if (ourBus * 4 + ourInternal == length)
            {
                exactClocks++;
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

        return (total, passed, failures, busCycles, internalClocks, expectedCycles, exactCycles, exactClocks);
    }

    /// <summary>
    /// Per record: the program-space fetch addresses this core issued beside the reference's, so the
    /// read's placement inside an instruction can be compared across a whole family instead of one
    /// record at a time. Writes a tab-separated line per record to <paramref name="outPath"/>.
    /// </summary>
    static int ReadCounts(string path, string outPath)
    {
        using Stream file = File.OpenRead(path);
        using Stream input = path.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(file, CompressionMode.Decompress) : file;
        using JsonDocument document = JsonDocument.Parse(input);

        var lines = new List<string>();
        foreach (JsonElement test in document.RootElement.EnumerateArray())
        {
            JsonElement initial = test.GetProperty("initial");
            JsonElement final = test.GetProperty("final");
            uint pc = initial.GetProperty("pc").GetUInt32();
            JsonElement prefetch = initial.GetProperty("prefetch");

            var engine = new Engine();
            var bus = new SingleStepBus(engine, 1);
            var cpu = new M68000(engine, 2);
            engine.Add(bus);
            engine.Add(cpu);
            bus.Launch();

            bus.PokeWord(pc, (ushort)prefetch[0].GetUInt32());
            bus.PokeWord(pc + 2, (ushort)prefetch[1].GetUInt32());
            foreach (JsonElement pair in initial.GetProperty("ram").EnumerateArray())
            {
                bus.Poke(pair[0].GetUInt32(), (byte)pair[1].GetUInt32());
            }

            cpu.Load(new M68000.SingleStepState(
                Registers(initial, "d"),
                Registers(initial, "a", 7),
                initial.GetProperty("usp").GetUInt32(),
                initial.GetProperty("ssp").GetUInt32(),
                (ushort)initial.GetProperty("sr").GetUInt32(),
                pc,
                (ushort)prefetch[0].GetUInt32(),
                (ushort)prefetch[1].GetUInt32()));

            var ours = new List<string>();
#if DEBUG
            cpu.CycleObserver = (write, code, address, data) =>
            {
                if (!write && code == 6)
                {
                    ours.Add($"{address:x6}");
                }
            };
#endif

            string? mismatch;
            try
            {
                mismatch = Step(cpu, engine, bus, final);
            }
            catch (Exception failure)
            {
                mismatch = $"harness: {failure.GetType().Name}";
            }

            var reference = new List<string>();
            foreach (JsonElement cycle in test.GetProperty("transactions").EnumerateArray())
            {
                if (cycle[0].GetString() == "r" && cycle[2].GetInt32() == 6)
                {
                    reference.Add($"{cycle[3].GetUInt32():x6}");
                }
            }

            // the handler-entry reads are the same in both; keep them, they are part of the count
            string name = test.GetProperty("name").GetString() ?? "(unnamed)";
            lines.Add($"{name}\t{string.Join(",", reference)}\t{string.Join(",", ours)}\t{(mismatch ?? "ok")}");
        }

        File.WriteAllLines(outPath, lines);
        Console.WriteLine($"wrote {outPath} ({lines.Count} records)");
        return 0;
    }

    /// <summary>
    /// Prints the core's own cycle list beside one record's <c>transactions</c> — the bar ADR 0009 §4's
    /// revision sets for the prefetch: the same addresses, in the same order, with the same function
    /// codes (6 = program fetch, 5 = supervisor data), not merely the same final state. A green
    /// <c>final</c> reached by a different fetch pattern is agreement by luck.
    /// </summary>
    static int TraceRecord(string path, int index)
    {
        using Stream file = File.OpenRead(path);
        using Stream input = path.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(file, CompressionMode.Decompress) : file;
        using JsonDocument document = JsonDocument.Parse(input);

        JsonElement[] records = document.RootElement.EnumerateArray().ToArray();
        if (index >= records.Length)
        {
            Console.Error.WriteLine($"cpu-tests FAILED — {OpcodeName(path)} has {records.Length} records, no index {index}");
            return 1;
        }

        JsonElement test = records[index];
        JsonElement initial = test.GetProperty("initial");
        JsonElement final = test.GetProperty("final");
        uint pc = initial.GetProperty("pc").GetUInt32();
        JsonElement prefetch = initial.GetProperty("prefetch");

        var engine = new Engine();
        var bus = new SingleStepBus(engine, 1);
        var cpu = new M68000(engine, 2);
        engine.Add(bus);
        engine.Add(cpu);
        bus.Launch();

        bus.PokeWord(pc, (ushort)prefetch[0].GetUInt32());
        bus.PokeWord(pc + 2, (ushort)prefetch[1].GetUInt32());
        foreach (JsonElement pair in initial.GetProperty("ram").EnumerateArray())
        {
            bus.Poke(pair[0].GetUInt32(), (byte)pair[1].GetUInt32());
        }

        cpu.Load(new M68000.SingleStepState(
            Registers(initial, "d"),
            Registers(initial, "a", 7),
            initial.GetProperty("usp").GetUInt32(),
            initial.GetProperty("ssp").GetUInt32(),
            (ushort)initial.GetProperty("sr").GetUInt32(),
            pc,
            (ushort)prefetch[0].GetUInt32(),
            (ushort)prefetch[1].GetUInt32()));

        var ours = new List<string>();
#if DEBUG
        cpu.CycleObserver = (write, code, address, data) =>
            ours.Add($"{(write ? "w" : "r")} fc{code} {address:x6} {data:x4}");
#else
        Console.Error.WriteLine("cpu-tests --trace needs a Debug build: the cycle observer is a debug instrument");
#endif

        string? mismatch;
        try
        {
            mismatch = Step(cpu, engine, bus, final);
        }
        catch (Exception failure)
        {
            mismatch = $"harness: {failure.GetType().Name}: {failure.Message}";
        }

        var reference = new List<string>();
        foreach (JsonElement cycle in test.GetProperty("transactions").EnumerateArray())
        {
            if (cycle[0].GetString() == "n")
            {
                reference.Add($"n {cycle[1].GetInt32(),-16}");
                continue;
            }

            reference.Add($"{cycle[0].GetString()} fc{cycle[2].GetInt32()} {cycle[3].GetUInt32():x6} {cycle[5].GetUInt32():x4}");
        }

        string name = test.GetProperty("name").GetString() ?? "(unnamed)";
        Console.WriteLine($"{OpcodeName(path)} #{index}  {name}");
        Console.WriteLine($"  pc {pc:x6}  prefetch {prefetch[0].GetUInt32():x4} {prefetch[1].GetUInt32():x4}  " +
            $"final pc {final.GetProperty("pc").GetUInt32():x6}  length {test.GetProperty("length").GetInt32()}");
        Console.WriteLine();
        Console.WriteLine($"  {"reference's transactions",-34}   ours");

        int rows = Math.Max(reference.Count, ours.Count);
        int first = -1;
        for (int row = 0; row < rows; row++)
        {
            string left = row < reference.Count ? reference[row] : string.Empty;
            string right = row < ours.Count ? ours[row] : string.Empty;
            bool same = left == right;
            if (!same && first < 0)
            {
                first = row;
            }

            Console.WriteLine($"  {left,-34} {(same ? string.Empty : "≠ ")}{right}");
        }

        Console.WriteLine();
        Console.WriteLine($"  cycles: reference {reference.Count(row => !row.StartsWith("n "))}, ours {ours.Count}" +
            (first < 0 ? ", identical" : $", first difference at row {first}"));
        Console.WriteLine($"  state: {(mismatch ?? "matches the corpus")}");
        return first < 0 && mismatch is null ? 0 : 1;
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
