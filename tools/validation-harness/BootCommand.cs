using System.Globalization;
using System.Linq;
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

    /// <summary>EmuTOS's own sysvars, from the image's .sym (etos256uk.sym): LONGs at these addresses.</summary>
    const uint Frclock = 0x466;
    const uint Vbclock = 0x462;
    const uint HZ200 = 0x4BA;

    /// <summary>The drives TOS found, one bit each (include/tosvars.h): zero means none answered.</summary>
    const uint Drvbits = 0x4C2;

    /// <summary>
    /// The ROM's own panic record (bios/vectors.S: any_vec dumps the registers, the exception number and
    /// 16 words of stack here before jmp dopanic). <c>_proc_lives</c> is the magic 0x12345678 that says a
    /// panic happened at all — the fastest way to tell "the ROM gave up" from "the ROM is waiting".
    /// </summary>
    const uint ProcLives = 0x380;
    const uint ProcEnum = 0x3C4;
    const uint ProcStack = 0x3CC;
    const uint ProcDregs = 0x384;
    const uint ProcAregs = 0x3A4;
    const uint ProcUsp = 0x3C8;

    internal static int Run(string[] args)
    {
        int frames = 400;
        bool inject = false;
        string? directory = null;
        string image = DefaultImage;
        bool trace = false;
        bool ringAtEnd = false;
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

                case "--ring":
                    ringAtEnd = true;    // append the last 40 instructions to probes.log
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
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var random = new Random(20261111);

        // ---- G8's measurement, and the per-frame figures of §4 ------------------------------------
        // Wall time is read around the engine's own drain and nowhere else: the harness's own frame
        // bookkeeping (the probes, the .sym reads) is not the machine's cost, and a budget measured
        // with it in would be a measurement of the harness.
        double frameMsTotal = 0;
        double frameMsMax = 0;
        double frameMsOnDesktop = 0;
        int framesTimed = 0;
        int framesTimedOnDesktop = 0;
        long allocatedAtStart = GC.GetTotalAllocatedBytes(false);

        // The boot's emulated waits (walking-skeleton.md §1's facts). A run of frames where the
        // program counter stays inside one small ROM loop is the ROM waiting on a timeout rather
        // than working; the FDC's own register traffic inside the run says which wait it was.
        const int WaitFrames = 10;
        const int WaitBox = 16;
        var waits = new List<WaitWindow>();
        int waitFrom = 1;
        uint waitPc = machine.Cpu.PC;
        long waitHz = machine.PeekLong(HZ200);
        long waitDiskWrites = machine.Disk.Writes;
        long waitDiskReads = machine.Disk.Reads;

        // G5 measures rates over whole frames, and never over the desktop's own settling. The window is
        // the four frames after the first frame on the desktop: enough that a wrong rate cannot land on
        // the right number by accident, short enough to stay inside the boot's cost.
        const int RateWindow = 4;
        RateSample? rateFrom = null;
        RateSample? rateTo = null;
        int lastFrame = 0;
        long frclockAtDesktop = 0;
        int desktopPcFrames = 0;

        try
        {
            for (int frame = 1; frame <= frames; frame++)
            {
                long frameStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                machine.Engine.Run(frame * frameTicks);
                double frameMs = (System.Diagnostics.Stopwatch.GetTimestamp() - frameStarted)
                    * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                frameMsTotal += frameMs;
                framesTimed++;
                if (frameMs > frameMsMax)
                {
                    frameMsMax = frameMs;
                }

                if (desktopFrames > 0)
                {
                    frameMsOnDesktop += frameMs;
                    framesTimedOnDesktop++;
                }

                lastFrame = frame;

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

                // _nvbls is a WORD (include/tosvars.h: the number of slots in the vbl queue, ARRAY_SIZE
                // = 8), not a long — reading four bytes there shows the next sysvar's high half too.
                long nvbls = machine.PeekWord(symbols["_nvbls"]);
                if (firstNvbls < 0)
                {
                    firstNvbls = nvbls;
                }

                lastNvbls = nvbls;
                uint pc = machine.Cpu.PC;

                // one wait window: a run of frames whose program counter stays inside one 16-byte box
                long hz = machine.PeekLong(HZ200);
                if (Math.Abs((long)pc - (long)waitPc) > WaitBox)
                {
                    if (frame - waitFrom >= WaitFrames)
                    {
                        waits.Add(new WaitWindow(
                            waitFrom, frame - 1, waitPc,
                            hz - waitHz,
                            machine.Disk.Writes - waitDiskWrites,
                            machine.Disk.Reads - waitDiskReads));
                    }

                    waitFrom = frame;
                    waitPc = pc;
                    waitHz = hz;
                    waitDiskWrites = machine.Disk.Writes;
                    waitDiskReads = machine.Disk.Reads;
                }

                if (frame % probeEvery == 0 || frame <= 5)
                {
                    // The boot's progress is read from EmuTOS's own variables, not from a guess: _frclock
                    // and _vbclock are the VBL counters, _hz_200 the 200 Hz tick that every BIOS wait
                    // loop spins on, and the MFP's TCDCR/TCDR + IERA/IMRA say whether the tick was ever
                    // programmed and enabled.
                    probes.Add(
                        $"t {clock.Elapsed.TotalSeconds,7:F1}s  frame {frame,5}  pc {pc:x6}  nvbls {nvbls,5}  " +
                        $"queue {machine.Engine.QueueCount,6}  retained {machine.Engine.RetainedRecords,9}  " +
                        $"frclock {machine.PeekLong(Frclock),6}  hz200 {machine.PeekLong(HZ200),6}  " +
                        $"tcdcr {machine.Timers.Register(0x1D, 0, false, machine.Engine.MachineTime):x2}  tcdr {machine.Timers.Register(0x23, 0, false, machine.Engine.MachineTime):x2}  " +
                        $"iera {machine.Timers.Register(0x07, 0, false, machine.Engine.MachineTime):x2}  imra {machine.Timers.Register(0x13, 0, false, machine.Engine.MachineTime):x2}  " +
                        $"ierb {machine.Timers.Register(0x09, 0, false, machine.Engine.MachineTime):x2}  imrb {machine.Timers.Register(0x15, 0, false, machine.Engine.MachineTime):x2}  " +
                        $"isrb {machine.Timers.Register(0x11, 0, false, machine.Engine.MachineTime):x2}  iprb {machine.Timers.Register(0x0D, 0, false, machine.Engine.MachineTime):x2}  " +
                        $"mfpw {machine.Timers.RegisterWrites,5}  tc {machine.Timers.TimerCExpiries,5}  irq {machine.Timers.IrqAssertions,4}  " +
                        $"sr {machine.Cpu.SR:x4}  ipl {machine.SystemControlUnit.ResolvedLevelAt(machine.Engine.MachineTime)}  " +
                        $"inkernel={machine.Cpu.Instructions,10}  " +
                        $"stack={string.Join(",", Enumerable.Range(0, 6).Select(k => machine.PeekLong(machine.Cpu.PeekA(7) + (uint)(k * 4)).ToString("x6")))}");
                }

                if (machine.Cpu.Halted)
                {
                    probes.Add($"frame {frame,5}  HALTED  {machine.Cpu.HaltReason}");
                    break;
                }

                if (desktopFrames < 0 && pc >= desktopStart && pc < desktopStart + 0x20000)
                {
                    desktopFrames = frame;
                    frclockAtDesktop = machine.PeekLong(Frclock);
                }

                if (desktopFrames > 0)
                {
                    if (pc >= desktopStart && pc < desktopStart + 0x20000)
                    {
                        desktopPcFrames++;
                    }

                    // The rate window is whole frames once the desktop is up — the PC wanders through the
                    // AES event loop and in and out of the desktop's text, so the window is a frame count,
                    // not a condition on the program counter.
                    if (frame == desktopFrames + 1)
                    {
                        rateFrom = RateSample.Of(machine);
                    }
                    else if (frame == desktopFrames + 1 + RateWindow)
                    {
                        rateTo = RateSample.Of(machine);
                    }

                    if (frame - desktopFrames >= 150)
                    {
                        break;   // 150 frames on the desktop: the AES event loop's own settle time
                    }
                }
            }
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine($"boot FAILED — {failure}");   // the stack, not just the message: an index in an emulator is a bug report
            File.WriteAllLines(Path.Combine(directory, "failure.log"), ring);
            foreach (string line in ring)
            {
                Console.Error.WriteLine(line);
            }

            return 1;
        }

        if (lastFrame - waitFrom + 1 >= WaitFrames)
        {
            waits.Add(new WaitWindow(
                waitFrom, lastFrame, waitPc,
                machine.PeekLong(HZ200) - waitHz,
                machine.Disk.Writes - waitDiskWrites,
                machine.Disk.Reads - waitDiskReads));
        }

        // ---- G8: the frame budget (design/validation.md §3) ---------------------------------------
        // Release only. A Debug frame carries the per-instant digest and the seven assertions, so its
        // wall time is a measurement of the instruments, not of the machine (validation.md §2's table).
        double meanFrameMs = framesTimed == 0 ? 0 : frameMsTotal / framesTimed;
        double meanDesktopMs = framesTimedOnDesktop == 0 ? 0 : frameMsOnDesktop / framesTimedOnDesktop;
        // The budget is 50 Hz with 2x headroom (10 ms of the 20 ms frame), not the 5 ms #8 first
        // proposed: crossings alone cost 9.4 ms a frame at the measured 107 215.6 pops and the engine's
        // 88 ns suspend/resume floor, so a 5 ms gate would gate ADR 0006's design rather than this
        // engine (#38's ruling, 2026-11-14; design/validation.md G8).
        const double BudgetMs = 10.0;
        const double FrameMs = 20.0;
#if DEBUG
        const bool budgetMeasured = false;
        const string configuration = "Debug";
#else
        const bool budgetMeasured = true;
        const string configuration = "Release";
#endif
        bool g8Green = budgetMeasured && meanFrameMs <= BudgetMs;

        long allocatedTotal = GC.GetTotalAllocatedBytes(false) - allocatedAtStart;
        TimeSpan gcPaused = GC.GetTotalPauseDuration();

        // the per-frame figures of §4 — every one of them a division of two measured totals
        double popsPerFrame = PerFrame(machine.Engine.Pops, lastFrame);
        double subscriptionsPerFrame = PerFrame(machine.Engine.Subscriptions, lastFrame);
        double suspensionsPerFrame = PerFrame(machine.Engine.ReadSuspensions, lastFrame);
        double stalePerFrame = PerFrame(machine.Engine.StaleWakes, lastFrame);
        double awaitedPerFrame = PerFrame(machine.Engine.AwaitWriteSuspensions, lastFrame);
        double readTotal = machine.Engine.FastPathReads + machine.Engine.ReadSuspensions;
        double fastShare = readTotal == 0 ? 0 : 100.0 * machine.Engine.FastPathReads / readTotal;
        double allocatedPerFrame = framesTimed == 0 ? 0 : allocatedTotal / 1048576.0 / framesTimed;
        double gcMsPerFrame = framesTimed == 0 ? 0 : gcPaused.TotalMilliseconds / framesTimed;
        double popsPerBusCycle = machine.Cpu.BusCycles == 0 ? 0 : (double)machine.Engine.Pops / machine.Cpu.BusCycles;
        string cpuName = CpuName();

        uint glApid = machine.PeekLong(symbols.TryGetValue("_gl_apid", out uint apid) ? apid : 0xE32A);
        long drvbits = machine.PeekLong(Drvbits) & 0xFF;

        // ---- G5 and G6, the two rows a boot is the only way to prove ---------------------------------
        var gate = new List<string>();
        var g5 = new List<string>();
        var g6 = new List<string>();
        bool g5Green = false;
        bool g6Green = false;

        if (rateFrom is { } from && rateTo is { } to)
        {
            long windowFrames = RateWindow;
            g5Green = true;
            foreach ((string name, long measured, long designed) in new[]
            {
                ("video groups", (to.Groups - from.Groups) / windowFrames, 4_000),
                ("DE records", (to.DeRecords - from.DeRecords) / windowFrames, 400),
                ("video chunks", (to.Chunks - from.Chunks) / windowFrames, 4_000),
                ("Timer B counts", (to.TimerBCounts - from.TimerBCounts) / windowFrames, 200),
                ("HBL (per 2 frames)", (to.Hbl - from.Hbl) * 2 / windowFrames, 625),
                ("VBL", (to.Vbl - from.Vbl) / windowFrames, 1),
            })
            {
                bool exact = measured == designed;
                g5Green &= exact || name == "VBL";   // VBL's own row is G6's business, never a rate gate
                g5.Add($"  {name,-16} {measured,8} / frame   catalogue {designed,6}   {(exact ? "ok" : "MISMATCH")}");
            }
        }
        else
        {
            g5.Add("  no rate window: the boot never reached the desktop, so no frame was measured");
        }

        long framesOnDesktop = desktopFrames < 0 ? 0 : lastFrame - desktopFrames;
        if (desktopFrames >= 0)
        {
            long frclockAtEnd = machine.PeekLong(Frclock);
            long frclockAdvanced = frclockAtEnd - frclockAtDesktop;
            bool apidNonZero = glApid != 0;
            bool pcSeenOnDesktop = desktopPcFrames > 0;
            bool frclockOk = Math.Abs(frclockAdvanced - framesOnDesktop) <= 1;

            g6Green = apidNonZero && pcSeenOnDesktop && frclockOk;
            g6.Add($"  _frclock         advanced {frclockAdvanced} over {framesOnDesktop} frames on the desktop {(frclockOk ? "(±1: ok)" : "(MISMATCH)")}");
            g6.Add($"  _gl_apid         {glApid:x8} {(apidNonZero ? "(non-zero: ok)" : "(zero: MISMATCH)")}");
            g6.Add($"  pc in desktop text  {desktopPcFrames} of {framesOnDesktop} frames sampled in [{desktopStart:x6}, {desktopStart + 0x20000:x6}) {(pcSeenOnDesktop ? "(ok)" : "(never: MISMATCH)")}");
            g6.Add($"  pc at the end    {machine.Cpu.PC:x6} (the AES event loop waits inside the BIOS's evnt_multi, so this need not be the desktop's own text)");
            g6.Add($"  _nvbls           {lastNvbls} (the vbl queue's slot count, ARRAY_SIZE(vbl_list) = 8 — reported, never a frame counter)");
        }
        else
        {
            g6.Add("  the boot never reached the desktop");
        }

        if (desktopFrames >= 0)
        {
            gate.Add($"g5={(g5Green ? "green" : "red")}");
            gate.Add($"g6={(g6Green ? "green" : "red")}");
        }
        else
        {
            gate.Add("g5=not measured");
            gate.Add("g6=not measured");
        }

        gate.Add(budgetMeasured ? $"g8={(g8Green ? "green" : "red")}" : "g8=not measured (Debug: the assertions would be the measurement)");

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
            $"_frclock={machine.PeekLong(Frclock).ToString(CultureInfo.InvariantCulture)}",
            $"_vbclock={machine.PeekLong(Vbclock).ToString(CultureInfo.InvariantCulture)}",
            $"_hz_200={machine.PeekLong(HZ200).ToString(CultureInfo.InvariantCulture)}",
            $"panic_lives={machine.PeekLong(ProcLives):x8}",
            $"panic_enum={machine.PeekLong(ProcEnum)}",
            $"panic_stack={string.Join(",", Enumerable.Range(0, 8).Select(k => machine.PeekLong(ProcStack + (uint)(k * 4)).ToString("x8")))}",
            "panic_dregs=" + string.Join(",", Enumerable.Range(0, 8).Select(k => machine.PeekLong(ProcDregs + (uint)(k * 4)).ToString("x8"))),
            "panic_aregs=" + string.Join(",", Enumerable.Range(0, 8).Select(k => machine.PeekLong(ProcAregs + (uint)(k * 4)).ToString("x8"))),
            $"panic_usp={machine.PeekLong(ProcUsp):x8}",
            $"queue_count={machine.Engine.QueueCount}",
            $"drain_ms={(machine.Engine.DrainTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency):F0}",
            $"instrument_ms={(machine.Engine.InstrumentTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency):F0}",
            $"trim_calls={machine.Engine.TrimCalls}",
            $"trim_ms={(machine.Engine.TrimTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency):F0}",
            $"records_retained={machine.Engine.RetainedRecords.ToString(CultureInfo.InvariantCulture)}",
            $"records_discarded={machine.Engine.DiscardedRecords.ToString(CultureInfo.InvariantCulture)}",
            "retention=" + string.Join(";", machine.Engine.Logs.Where(l => l.Discarded > 0).OrderByDescending(l => l.Discarded).Take(8).Select(l => $"{l.Channel.Name}:{l.Discarded}:{l.OldestInstant}")),
            $"_gl_apid={glApid.ToString("x8", CultureInfo.InvariantCulture)}",
            $"queue_pops={machine.Engine.Pops.ToString(CultureInfo.InvariantCulture)}",
            $"fast_path_reads={machine.Engine.FastPathReads.ToString(CultureInfo.InvariantCulture)}",
            $"read_suspensions={machine.Engine.ReadSuspensions.ToString(CultureInfo.InvariantCulture)}",
            $"subscriptions={machine.Engine.Subscriptions.ToString(CultureInfo.InvariantCulture)}",
            $"awaited_writes={machine.Engine.AwaitWriteSuspensions.ToString(CultureInfo.InvariantCulture)}",
            $"stale_wakes={machine.Engine.StaleWakes.ToString(CultureInfo.InvariantCulture)}",
            $"frames_run={lastFrame.ToString(CultureInfo.InvariantCulture)}",
            $"_frclock_at_desktop={frclockAtDesktop.ToString(CultureInfo.InvariantCulture)}",
            $"g5={(desktopFrames < 0 ? "not measured" : g5Green ? "green" : "red")}",
            $"g6={(desktopFrames < 0 ? "not measured" : g6Green ? "green" : "red")}",
            $"g8={(budgetMeasured ? g8Green ? "green" : "red" : "not measured")}",
            $"config={configuration}",
            $"cpu={cpuName}",
            $"cores={Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)}",
            $"frame_n={framesTimed.ToString(CultureInfo.InvariantCulture)}",
            $"frame_mean_ms={meanFrameMs.ToString("F2", CultureInfo.InvariantCulture)}",
            $"frame_mean_on_desktop_ms={meanDesktopMs.ToString("F2", CultureInfo.InvariantCulture)}",
            $"frame_max_ms={frameMsMax.ToString("F2", CultureInfo.InvariantCulture)}",
            $"frame_budget_ms={BudgetMs.ToString("F2", CultureInfo.InvariantCulture)}",
            $"pops_per_frame={popsPerFrame.ToString("F1", CultureInfo.InvariantCulture)}",
            $"subscriptions_per_frame={subscriptionsPerFrame.ToString("F1", CultureInfo.InvariantCulture)}",
            $"suspensions_per_frame={suspensionsPerFrame.ToString("F1", CultureInfo.InvariantCulture)}",
            $"stale_wakes_per_frame={stalePerFrame.ToString("F1", CultureInfo.InvariantCulture)}",
            $"awaited_writes_per_frame={awaitedPerFrame.ToString("F1", CultureInfo.InvariantCulture)}",
            $"fast_path_share_pct={fastShare.ToString("F2", CultureInfo.InvariantCulture)}",
            $"alloc_mb_per_frame={allocatedPerFrame.ToString("F2", CultureInfo.InvariantCulture)}",
            $"gc_ms_per_frame={gcMsPerFrame.ToString("F2", CultureInfo.InvariantCulture)}",
            $"pops_per_bus_cycle={popsPerBusCycle.ToString("F2", CultureInfo.InvariantCulture)}",
            $"drvbits={drvbits.ToString("x2", CultureInfo.InvariantCulture)}",
            "waits=" + string.Join(";", waits
                .Where(w => desktopFrames < 0 || w.To < desktopFrames)
                .OrderByDescending(w => w.To - w.From)
                .Take(6)
                .Select(w => $"f{w.From}-{w.To}@{w.Pc:x6}/{w.Hz * 5}ms/fdc{w.DiskWrites}w{w.DiskReads}r")),
        };

#if DEBUG
        int digestWords = DigestArtifacts.Write(directory, machine.Engine);
        state.Add($"batches={digestWords.ToString(CultureInfo.InvariantCulture)}");
        state.Add("digest=run.digest (run.batches: the sparse batch→instant index; run.tail: the last 8 batches)");
#else
        state.Add("digest=absent (release build)");
#endif

        File.WriteAllLines(Path.Combine(directory, "run.state"), state);
        if (ringAtEnd)
        {
            probes.Add("last instructions:");
            probes.AddRange(ring);
        }

        File.WriteAllLines(Path.Combine(directory, "probes.log"), probes);
        traceWriter?.Flush();

        // ---- the page: the §3 tables with measured values filled in (design/validation.md §2) -----
        var report = new List<string>
        {
            "The ST — validation harness, boot report",
            string.Empty,
            $"image: {Path.GetFileName(image)} ({rom.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes, sha256 {digestOfImage[..16]}…)",
            $"frames: asked {frames}, reached the desktop at {(desktopFrames < 0 ? "never" : desktopFrames.ToString(CultureInfo.InvariantCulture))}",
            $"pc {machine.Cpu.PC:x6}  deskstart {desktopStart:x6}  _nvbls {lastNvbls}  _gl_apid {glApid:x8}",
            $"configuration: {configuration} on {cpuName} × {Environment.ProcessorCount} cores",
            string.Empty,
            "G5 — per-chip rates over the four frames after the desktop is up (design/crossings.md):",
        };

        report.AddRange(g5);
        report.Add(string.Empty);
        report.Add("G6 — the machine's own clocks, read from the OS (design/validation.md §3):");
        report.AddRange(g6);
        report.Add(string.Empty);
        report.Add("G8 — the frame budget (design/validation.md §3):");

#if DEBUG
        report.Add($"  mean frame              {meanFrameMs,8:F2} ms of {FrameMs:F2} ms   budget {BudgetMs:F2} ms (50 %)   not measured");
        report.Add("  a Debug frame carries the per-instant digest and the seven assertions: its wall time is a");
        report.Add("  measurement of the instruments, not of the machine (validation.md §2's two-table row).");
#else
        report.Add($"  mean frame              {meanFrameMs,8:F2} ms of {FrameMs:F2} ms   budget {BudgetMs:F2} ms (50 %)   {(g8Green ? "green" : "RED")}");
#endif

        report.Add($"  n                       {Program.Number(framesTimed)} frames timed (max {frameMsMax:F2} ms)");
        report.Add($"  mean, on the desktop    {meanDesktopMs,8:F2} ms over {Program.Number(framesTimedOnDesktop)} frames (the boot's own frames are cheaper)");
        report.Add($"  cpu                     {cpuName} × {Environment.ProcessorCount} cores");
        report.Add($"  configuration           {configuration}");
        report.Add($"  crossings               pops {Program.Number(machine.Engine.Pops)} ({popsPerFrame:N1}/frame) — subscriptions {Program.Number(machine.Engine.Subscriptions)} ({subscriptionsPerFrame:N1}), date suspensions {Program.Number(machine.Engine.ReadSuspensions)} ({suspensionsPerFrame:N1}), stale wakes {Program.Number(machine.Engine.StaleWakes)} ({stalePerFrame:N1})");
        report.Add($"  bus cycles              {Program.Number(machine.Cpu.BusCycles)} ({Program.Number(machine.Cpu.BusCycles / Math.Max(lastFrame, 1))}/frame) — {popsPerBusCycle:F2} pops a cycle, against ADR 0006's ~4");
        report.Add($"  catalogue               ≈ 4 734 class-1 wakes a frame at the desktop's idle workload (design/crossings.md §10)");
        report.Add(string.Empty);
        report.Add("reported, never gated (design/validation.md §4), per frame:");
        report.Add($"  fast-path share of reads  {fastShare:F2} % — {Program.Number(machine.Engine.FastPathReads)} of {Program.Number((long)readTotal)} reads resolved on the chip's own stack");
        report.Add($"  suspensions / frame     {suspensionsPerFrame:N1} dated reads and {subscriptionsPerFrame:N1} subscriptions; {stalePerFrame:N1} wakes ({100.0 * machine.Engine.StaleWakes / Math.Max(machine.Engine.Pops, 1):F0} % of every pop) fired after the chip had already been resumed by another one");
        report.Add($"  awaited writes / frame  {awaitedPerFrame:N1}");
        report.Add($"  allocation / frame      {allocatedPerFrame:F2} MB (process-wide, the harness's own frame bookkeeping included)");
        report.Add($"  GC pause / frame        {gcMsPerFrame:F2} ms — {Program.Number(GC.CollectionCount(0))} gen-0, {Program.Number(GC.CollectionCount(1))} gen-1, {Program.Number(GC.CollectionCount(2))} gen-2 collections, {Program.Number(allocatedTotal / 1048576)} MB allocated in all");
        report.Add($"  subscriptions / frame   {subscriptionsPerFrame:N1} registered against the catalogue's ≈ 4 734 class-1 wakes at the desktop's idle workload — the excess is the bus's class-2 exchanges, which the catalogue counts separately (design/crossings.md §10)");
        report.Add($"  _frclock advance        {machine.PeekLong(Frclock) - frclockAtDesktop} over {framesOnDesktop} frames on the desktop (G6 gates it); _nvbls {lastNvbls} is the vbl queue's slot count, never a counter");
        report.Add($"  instructions / frame    {Program.Number(machine.Cpu.Instructions / Math.Max(lastFrame, 1))} ({Program.Number(machine.Cpu.Instructions)} in all, {Program.Number(machine.Cpu.InterruptsTaken)} interrupts, {Program.Number(machine.Cpu.Exceptions)} exceptions)");
        report.Add($"  records                 {Program.Number(machine.SystemControlUnit.GroupDeliveries)} group deliveries, {Program.Number(machine.SystemControlUnit.DeRecords)} DE, {Program.Number(machine.SystemControlUnit.HblRecords)} HBL / {Program.Number(machine.SystemControlUnit.VblRecords)} VBL, {Program.Number(machine.Video.ChunkWrites)} chunk writes");
        report.Add($"  retention               {Program.Number(machine.Engine.RetainedRecords)} records retained of {Program.Number(machine.Engine.RetainedRecords + machine.Engine.DiscardedRecords)} appended; {Program.Number(machine.Engine.TrimCalls)} trims in {machine.Engine.TrimTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F0} ms");
#if DEBUG
        report.Add($"  digest stream           {Program.Number(digestWords)} words, one per batch; run.batches names their instants, run.tail the last 8 batches' records");
#else
        report.Add("  digest stream           absent — a Release build carries no digest and no assertion (ADR 0008 §6)");
#endif
        report.Add(string.Empty);
        report.Add("the boot's two facts, measured (walking-skeleton.md §1) — not asserted:");
        report.Add("  emulated waits before the desktop, from the probe timeline: a run of frames whose program");
        report.Add("  counter stays inside one 16-byte box is the ROM waiting on a timeout rather than working,");
        report.Add("  and its length is the ROM's own _hz_200 advanced over it (5 ms a tick):");
        var preDesktop = waits.Where(w => desktopFrames < 0 || w.To < desktopFrames).OrderByDescending(w => w.To - w.From).Take(6).ToList();
        // every (address, name) the .sym carries, in address order — not the name-keyed dictionary
        // above, which keeps only the first entry a name has and so loses a module's text section to
        // its BSS marker (`clock.o` is both a data marker and its code's first address)
        var named = ReadAddressList(symPath);
        if (preDesktop.Count == 0)
        {
            report.Add("    none — no frame run stayed inside one loop box while the boot was up");
        }
        else
        {
            foreach (WaitWindow wait in preDesktop)
            {
                report.Add($"    frames {wait.From,4}–{wait.To,-4} {wait.To - wait.From + 1,4} frames = {wait.Hz * 5.0 / 1000:F2} s of emulated time   pc ${wait.Pc:x6} ({NearestSymbol(named, wait.Pc)})   FDC {wait.DiskWrites} write(s), {wait.DiskReads} read(s)");
            }
        }

        report.Add($"  _drvbits at the desktop {drvbits:x2} — TOS's own drive bitmap: {(drvbits == 0 ? "no drive answered (zero drives registered)" : $"{System.Numerics.BitOperations.PopCount((uint)drvbits)} drive(s)")}");
        report.Add($"  keyboard                the IKBD/ACIA stub was read {Program.Number(machine.Controllers.Reads)} time(s) and produced no byte; the ROM's own 300 ms timeout (15 frames) is what carried the boot past it");
        report.Add($"  machine time            {Program.Number(machine.Engine.MachineTime)} ticks ({machine.Engine.MachineTime / (double)TheST.Machine.Ticks.Frame:F1} frames of emulated time) in {clock.Elapsed.TotalSeconds:F1} s of host time");

        File.WriteAllLines(Path.Combine(directory, "run.report"), report);

        foreach (string line in state)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine($"wrote {directory}");

        if (desktopFrames < 0)
        {
            Console.Error.WriteLine("boot: never reached the desktop — see run.state and probes.log");
            return 2;
        }

        if (!g5Green || !g6Green || (budgetMeasured && !g8Green))
        {
            Console.Error.WriteLine(
                $"boot: reached the desktop at frame {desktopFrames}, but G5 is {(g5Green ? "green" : "red")}, " +
                $"G6 is {(g6Green ? "green" : "red")} and G8 is {(budgetMeasured ? g8Green ? "green" : $"red ({meanFrameMs:F2} ms a frame of {BudgetMs:F2})" : "not measured in Debug")} — see run.report");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// The counters G5 differences: every structural rate the catalogue states per frame, read from the
    /// chips that produce it. A sample is a whole-frame boundary; the row divides two samples by the
    /// number of frames between them, so a rate that is right per frame cannot be right by accident.
    /// </summary>
    readonly record struct RateSample(long Groups, long DeRecords, long Chunks, long TimerBCounts, long Hbl, long Vbl)
    {
        internal static RateSample Of(MachineModel machine) => new(
            machine.SystemControlUnit.GroupDeliveries,
            machine.SystemControlUnit.DeRecords,
            machine.Video.ChunkWrites,
            machine.Timers.DeCounts,
            machine.SystemControlUnit.HblRecords,
            machine.SystemControlUnit.VblRecords);
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

    /// <summary>
    /// The image's own name for the code a wait sits in: the nearest symbol at or below the counter, and
    /// the object file it belongs to — a PC is a number, and "$e13ca2" says nothing while
    /// "bios.o: _stop_until_interrupt" says where the ROM was standing.
    /// </summary>
    static string NearestSymbol(List<(uint Address, string Name)> symbols, uint pc)
    {
        string name = "no symbol";
        uint best = 0;

        foreach ((uint address, string symbol) in symbols)
        {
            if (address > pc)
            {
                break;
            }

            // the same address can carry two names — an object file's text section and its first
            // function; the file is the one a reader wants ("clock.o", not its first entry point)
            if (address > best || (address == best && symbol.EndsWith(".o", StringComparison.Ordinal)))
            {
                best = address;
                name = symbol;
            }
        }

        return best == 0 ? name : $"{name}+${pc - best:x}";
    }

    /// <summary>A division of one measured total by the frames it took — every §4 figure is one of these.</summary>
    static double PerFrame(long total, int frames) => frames <= 0 ? 0 : (double)total / frames;

    /// <summary>The host's own name for the core G8 was measured on: a timing without it is unreadable later.</summary>
    static string CpuName()
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.Ordinal))
                {
                    int colon = line.IndexOf(':');
                    if (colon >= 0)
                    {
                        return line[(colon + 1)..].Trim();
                    }
                }
            }
        }
        catch (IOException)
        {
            // a host without /proc: the name is a label, never a verdict
        }
        catch (UnauthorizedAccessException)
        {
        }

        return "unknown cpu";
    }

    /// <summary>
    /// One emulated wait: a run of frames whose program counter stayed inside one 16-byte box, with the
    /// ROM's own <c>_hz_200</c> advanced over it (5 ms a tick of emulated time) and the FDC's register
    /// traffic inside the run.
    /// </summary>
    readonly record struct WaitWindow(int From, int To, uint Pc, long Hz, long DiskWrites, long DiskReads);

    /// <summary>Every symbol the image's <c>.sym</c> carries, as (address, name), in address order.</summary>
    static List<(uint Address, string Name)> ReadAddressList(string path)
    {
        var symbols = new List<(uint Address, string Name)>(16384);
        if (!File.Exists(path))
        {
            return symbols;
        }

        foreach (string line in File.ReadLines(path))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3
                && parts[0].StartsWith("0x", StringComparison.Ordinal)
                && uint.TryParse(parts[0][2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
            {
                symbols.Add((value, parts[^1]));
            }
        }

        symbols.Sort((left, right) => left.Address.CompareTo(right.Address));
        return symbols;
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
