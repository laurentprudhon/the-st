using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TheST.Machine;
using MachineModel = TheST.Machine.Machine;

namespace TheST.Harness;

/// <summary>
/// <c>boot</c> — the machine's own trace (design/validation.md §3, G1/G2/G6). It boots
/// <c>roms/emutos-1.4/etos256uk.img</c> — hash-checked against the manifest before the first
/// instruction — and runs to the GEM desktop.
/// </summary>
internal static class BootCommand
{
    const string DefaultImage = "roms/emutos-1.4/etos256uk.img";
    const string DefaultManifest = "roms/emutos-1.4-manifest.json";

    internal static int Run(string[] args)
    {
        int frames = 400;
        bool inject = false;
        string? directory = null;
        string image = DefaultImage;
        bool trace = false;
        int probeEvery = 25;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--frames":
                    if (++index >= args.Length || !int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out frames) || frames <= 0)
                    {
                        return Program.Unknown(args[Math.Max(index - 1, 0)]);
                    }

                    break;

                case "--inject":
                    inject = true;
                    break;

                case "--trace":
                    trace = true;
                    break;

                case "--probe-every":
                    if (++index >= args.Length || !int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out probeEvery))
                    {
                        return Program.Unknown(args[Math.Max(index - 1, 0)]);
                    }

                    break;

                case "--image":
                    if (++index >= args.Length)
                    {
                        return Program.Unknown(args[index - 1]);
                    }

                    image = args[index];
                    break;

                case "--out":
                    if (++index >= args.Length)
                    {
                        return Program.Unknown(args[index - 1]);
                    }

                    directory = args[index];
                    break;

                default:
                    return Program.Unknown(args[index]);
            }
        }

        directory ??= Path.Combine(
            "runs",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + (inject ? "-inject" : string.Empty));

        Directory.CreateDirectory(directory);

        byte[] rom = File.ReadAllBytes(image);
        string digestOfImage = Convert.ToHexString(SHA256.HashData(rom)).ToLowerInvariant();
        if (!ManifestAccepts(DefaultManifest, Path.GetFileName(image), digestOfImage))
        {
            Console.Error.WriteLine($"boot FAILED — {image} is not the image the manifest names (sha256 {digestOfImage})");
            return 1;
        }

        string symPath = Path.ChangeExtension(image, ".sym");
        Dictionary<string, uint> symbols = ReadSymbols(symPath);
        if (!symbols.TryGetValue("_deskstart", out uint desktopStart))
        {
            Console.Error.WriteLine($"boot FAILED — {symPath} carries no _deskstart");
            return 1;
        }

        var machine = new MachineModel();
        machine.LoadRom(rom);

        var ring = new Queue<string>();
        string? tracePath = trace ? Path.Combine(directory, "cpu.trace") : null;
        using var traceWriter = tracePath is null ? null : new StreamWriter(tracePath);

        machine.Cpu.Observer = (instant, opcode, pc) =>
        {
            if (traceWriter is not null)
            {
                traceWriter.WriteLine($"{instant} {pc:x6} {opcode:x4} sr={machine.Cpu.SR:x4} d0={machine.Cpu.PeekD(0):x8} d1={machine.Cpu.PeekD(1):x8} a0={machine.Cpu.PeekA(0):x8} a1={machine.Cpu.PeekA(1):x8} a6={machine.Cpu.PeekA(6):x8} a7={machine.Cpu.PeekA(7):x8}");
            }

            ring.Enqueue($"{instant} {pc:x6} {opcode:x4}");
            while (ring.Count > 40)
            {
                ring.Dequeue();
            }
        };

        machine.Start();

        long frameTicks = TheST.Machine.Ticks.Frame;
        int desktopFrames = -1;
        long firstNvbls = -1;
        long lastNvbls = -1;
        var probes = new List<string>();
        var random = new Random(20261111);

        try
        {
            for (int frame = 1; frame <= frames; frame++)
            {
                machine.Engine.Run(frame * frameTicks);

                if (inject)
                {
                    Thread.Yield();
                    if (random.Next(0, 4) == 0)
                    {
                        byte[] noise = new byte[random.Next(64, 4096)];
                        GC.KeepAlive(noise);
                    }

                    if (frame % 10 == 0)
                    {
                        GC.Collect(0);
                    }
                }

                long nvbls = machine.PeekLong(symbols["_nvbls"]);
                if (firstNvbls < 0)
                {
                    firstNvbls = nvbls;
                }

                lastNvbls = nvbls;
                uint pc = machine.Cpu.PC;

                if (frame % probeEvery == 0 || frame <= 5)
                {
                    probes.Add($"frame {frame,5}  pc {pc:x6}  nvbls {nvbls,5}  inkernel={machine.Cpu.Instructions,10}");
                }

                if (machine.Cpu.Halted)
                {
                    probes.Add($"frame {frame,5}  HALTED  {machine.Cpu.HaltReason}");
                    break;
                }

                if (pc >= desktopStart && pc < desktopStart + 0x20000)
                {
                    if (desktopFrames < 0)
                    {
                        desktopFrames = frame;
                    }

                    if (frame - desktopFrames >= 50)
                    {
                        break;   // 50 frames on the desktop is the boot's own settle time
                    }
                }
            }
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine($"boot FAILED — {failure.Message}");
            File.WriteAllLines(Path.Combine(directory, "failure.log"), ring);
            foreach (string line in ring)
            {
                Console.Error.WriteLine(line);
            }

            return 1;
        }

        uint glApid = machine.PeekLong(symbols.TryGetValue("_gl_apid", out uint apid) ? apid : 0xE32A);

        var state = new List<string>
        {
            "trace=machine",
            $"image={Path.GetFileName(image)}",
            $"sha256={digestOfImage}",
            $"frames_asked={frames.ToString(CultureInfo.InvariantCulture)}",
            $"frames_to_desktop={(desktopFrames < 0 ? "never" : desktopFrames.ToString(CultureInfo.InvariantCulture))}",
            $"machine_time={machine.Engine.MachineTime.ToString(CultureInfo.InvariantCulture)}",
            $"instructions={machine.Cpu.Instructions.ToString(CultureInfo.InvariantCulture)}",
            $"interrupts={machine.Cpu.InterruptsTaken.ToString(CultureInfo.InvariantCulture)}",
            $"exceptions={machine.Cpu.Exceptions.ToString(CultureInfo.InvariantCulture)}",
            $"bus_cycles={machine.Cpu.BusCycles.ToString(CultureInfo.InvariantCulture)}",
            $"bus_answers={machine.SystemControlUnit.BusAnswers.ToString(CultureInfo.InvariantCulture)}",
            $"bus_faults={machine.SystemControlUnit.BusFaults.ToString(CultureInfo.InvariantCulture)}",
            $"groups={machine.SystemControlUnit.GroupDeliveries.ToString(CultureInfo.InvariantCulture)}",
            $"scu_events={machine.SystemControlUnit.Events.ToString(CultureInfo.InvariantCulture)}",
            $"scu_loops={machine.SystemControlUnit.WakeLoops.ToString(CultureInfo.InvariantCulture)}",
            $"scu_line={machine.SystemControlUnit.CurrentLine.ToString(CultureInfo.InvariantCulture)}",
            $"scu_group={machine.SystemControlUnit.CurrentGroupIndex.ToString(CultureInfo.InvariantCulture)}",
            $"scu_de={(machine.SystemControlUnit.DisplayWindowOpen ? "open" : "shut")}",
            $"de_records={machine.SystemControlUnit.DeRecords.ToString(CultureInfo.InvariantCulture)}",
            $"hbl={machine.SystemControlUnit.HblRecords.ToString(CultureInfo.InvariantCulture)}",
            $"vbl={machine.SystemControlUnit.VblRecords.ToString(CultureInfo.InvariantCulture)}",
            $"chunks={machine.Video.ChunkWrites.ToString(CultureInfo.InvariantCulture)}",
            $"timer_c={machine.Timers.TimerCExpiries.ToString(CultureInfo.InvariantCulture)}",
            $"timer_b_counts={machine.Timers.DeCounts.ToString(CultureInfo.InvariantCulture)}",
            $"irq={machine.Timers.IrqAssertions.ToString(CultureInfo.InvariantCulture)}",
            $"pc={machine.Cpu.PC.ToString("x6", CultureInfo.InvariantCulture)}",
            $"deskstart={desktopStart.ToString("x6", CultureInfo.InvariantCulture)}",
            $"_nvbls={lastNvbls.ToString(CultureInfo.InvariantCulture)}",
            $"_nvbls_first={firstNvbls.ToString(CultureInfo.InvariantCulture)}",
            $"_gl_apid={glApid.ToString("x8", CultureInfo.InvariantCulture)}",
            $"queue_pops={machine.Engine.Pops.ToString(CultureInfo.InvariantCulture)}",
            $"fast_path_reads={machine.Engine.FastPathReads.ToString(CultureInfo.InvariantCulture)}",
            $"read_suspensions={machine.Engine.ReadSuspensions.ToString(CultureInfo.InvariantCulture)}",
            $"subscriptions={machine.Engine.Subscriptions.ToString(CultureInfo.InvariantCulture)}",
            $"awaited_writes={machine.Engine.AwaitWriteSuspensions.ToString(CultureInfo.InvariantCulture)}",
            $"stale_wakes={machine.Engine.StaleWakes.ToString(CultureInfo.InvariantCulture)}",
        };

#if DEBUG
        string digestPath = Path.Combine(directory, "run.digest");
        var digest = new StringBuilder(machine.Engine.DigestWords.Count * 17);
        foreach (ulong word in machine.Engine.DigestWords)
        {
            digest.Append(word.ToString("x16", CultureInfo.InvariantCulture)).Append('\n');
        }

        File.WriteAllText(digestPath, digest.ToString());
        state.Add($"batches={machine.Engine.DigestWords.Count.ToString(CultureInfo.InvariantCulture)}");
        state.Add("digest=run.digest");
#else
        state.Add("digest=absent (release build)");
#endif

        File.WriteAllLines(Path.Combine(directory, "run.state"), state);
        File.WriteAllLines(Path.Combine(directory, "probes.log"), probes);
        traceWriter?.Flush();

        var report = new List<string>
        {
            "The ST — validation harness, boot report",
            string.Empty,
            $"image: {Path.GetFileName(image)} ({rom.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes, sha256 {digestOfImage[..16]}…)",
            $"frames: asked {frames}, reached the desktop at {(desktopFrames < 0 ? "never" : desktopFrames.ToString(CultureInfo.InvariantCulture))}",
            $"pc {machine.Cpu.PC:x6}  deskstart {desktopStart:x6}  _nvbls {lastNvbls}  _gl_apid {glApid:x8}",
            string.Empty,
            "reported, never gated (design/validation.md §4):",
            $"  instructions            {Program.Number(machine.Cpu.Instructions)}",
            $"  interrupts taken        {Program.Number(machine.Cpu.InterruptsTaken)}",
            $"  exceptions              {Program.Number(machine.Cpu.Exceptions)}",
            $"  bus cycles              {Program.Number(machine.Cpu.BusCycles)} ({Program.Number(machine.SystemControlUnit.BusFaults)} faults)",
            $"  group deliveries        {Program.Number(machine.SystemControlUnit.GroupDeliveries)}",
            $"  DE records              {Program.Number(machine.SystemControlUnit.DeRecords)}",
            $"  HBL / VBL               {Program.Number(machine.SystemControlUnit.HblRecords)} / {Program.Number(machine.SystemControlUnit.VblRecords)}",
            $"  chunk writes            {Program.Number(machine.Video.ChunkWrites)}",
            $"  timer C expiries        {Program.Number(machine.Timers.TimerCExpiries)}",
            $"  queue pops              {Program.Number(machine.Engine.Pops)}",
            $"  stale wakes             {Program.Number(machine.Engine.StaleWakes)}",
            $"  machine time            {Program.Number(machine.Engine.MachineTime)} ticks",
            string.Empty,
            "the two boot facts this stage reports (walking-skeleton.md §1):",
            "  the floppy probe costs 2 × 3 s of emulated time (≈ 300 frames) and no drive is ever",
            "  registered; no key ever arrives, and TOS gives up after its own 300 ms timeout.",
        };

        File.WriteAllLines(Path.Combine(directory, "run.report"), report);

        foreach (string line in state)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine($"wrote {directory}");
        return desktopFrames >= 0 ? 0 : 2;
    }

    static bool ManifestAccepts(string manifestPath, string file, string sha256)
    {
        if (!File.Exists(manifestPath))
        {
            return true;
        }

        string text = File.ReadAllText(manifestPath);
        int index = text.IndexOf($"\"file\": \"{file}\"", StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        int hashIndex = text.IndexOf("\"sha256\": \"", index, StringComparison.Ordinal);
        if (hashIndex < 0)
        {
            return false;
        }

        hashIndex += "\"sha256\": \"".Length;
        string recorded = text[hashIndex..text.IndexOf('"', hashIndex)];
        return string.Equals(recorded, sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The image's own symbol table: <c>_deskstart</c> is per image and never a constant.</summary>
    internal static Dictionary<string, uint> ReadSymbols(string path)
    {
        var symbols = new Dictionary<string, uint>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return symbols;
        }

        foreach (string line in File.ReadLines(path))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !parts[0].StartsWith("0x", StringComparison.Ordinal))
            {
                continue;
            }

            if (uint.TryParse(parts[0][2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
            {
                symbols.TryAdd(parts[^1], value);
            }
        }

        return symbols;
    }
}
