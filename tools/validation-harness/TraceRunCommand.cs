using System.Globalization;
using System.Text;
using TheST.Machine;

namespace TheST.Harness;

/// <summary>
/// <c>run</c> — the synthetic trace, ended by a digest stream (design/validation.md §3, G1/G2).
///
/// What G1/G2 mean here and not yet: the real row is "same image, same N, byte-identical digest
/// streams over a TOS boot", which is #34's. This stage runs the same experiment on the synthetic
/// trace, so the instrument is proven before the machine it will measure exists. Frames are never
/// compared; the exit code is the verdict.
/// </summary>
internal static class TraceRunCommand
{
    const int DefaultFrames = 100;

    internal static int Run(string[] args)
    {
        int frames = DefaultFrames;
        bool inject = false;
        string? directory = null;

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

        long end = frames * TheST.Machine.Ticks.Frame;
        var engine = new Engine();
        SyntheticTrace.Launch(engine, end);

        var random = new Random(20261111);
        try
        {
            // One host step per frame. G2's injection may only touch host scheduling — a yield, an
            // allocation, a collection, a pause — never an instant, a record or the queue's key.
            for (int frame = 0; frame < frames; frame++)
            {
                engine.Run((frame + 1) * TheST.Machine.Ticks.Frame);

                if (inject)
                {
                    Inject(random, frame);
                }
            }
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine($"run FAILED — {failure.Message}");
            return 1;
        }

        Directory.CreateDirectory(directory);

        int records = 0;
        foreach (ValueLog log in engine.Logs)
        {
            records += log.Count;
        }

        var state = new List<string>
        {
            "trace=synthetic",
            $"frames={frames.ToString(CultureInfo.InvariantCulture)}",
            $"inject={(inject ? "yes" : "no")}",
            $"machine_time={engine.MachineTime.ToString(CultureInfo.InvariantCulture)}",
            $"records={records.ToString(CultureInfo.InvariantCulture)}",
            $"queue_pops={engine.Pops.ToString(CultureInfo.InvariantCulture)}",
            $"fast_path_reads={engine.FastPathReads.ToString(CultureInfo.InvariantCulture)}",
            $"read_suspensions={engine.ReadSuspensions.ToString(CultureInfo.InvariantCulture)}",
            $"subscriptions={engine.Subscriptions.ToString(CultureInfo.InvariantCulture)}",
            $"awaited_writes={engine.AwaitWriteSuspensions.ToString(CultureInfo.InvariantCulture)}",
        };

#if DEBUG
        string digestPath = Path.Combine(directory, "run.digest");
        var digest = new StringBuilder(engine.DigestWords.Count * 17);
        foreach (ulong word in engine.DigestWords)
        {
            digest.Append(word.ToString("x16", CultureInfo.InvariantCulture)).Append('\n');
        }

        File.WriteAllText(digestPath, digest.ToString());
        state.Add($"batches={engine.DigestWords.Count.ToString(CultureInfo.InvariantCulture)}");
        state.Add("digest=run.digest");
#else
        // A Release build has no digest and no assertions at all (ADR 0008 §6) — the file's absence is
        // the evidence, and `scripts/validate.sh` asserts it.
        state.Add("digest=absent (release build)");
#endif

        File.WriteAllLines(Path.Combine(directory, "run.state"), state);

        var report = new List<string>
        {
            "The ST — validation harness, run report",
            string.Empty,
            "trace: synthetic — no image is read and no chip boots (stage 1, ticket #33)",
            $"frames: {Program.Number(frames)} of {Program.Number(TheST.Machine.Ticks.Frame)} ticks = {Program.Number(end)} ticks of machine time",
            $"host injection: {(inject ? "on (yields, allocation noise, collections, frame pacing)" : "off")}",
            string.Empty,
            "reported, never gated (design/validation.md §4):",
            $"  queue pops              {Program.Number(engine.Pops)}",
            $"  records appended        {Program.Number(records)} (wire and internal anchors alike)",
            $"  fast-path reads         {Program.Number(engine.FastPathReads)} resolved on a chip's own stack",
            $"  read suspensions        {Program.Number(engine.ReadSuspensions)} dated reads + {Program.Number(engine.Subscriptions)} subscriptions",
            $"  awaited writes          {Program.Number(engine.AwaitWriteSuspensions)}",
            $"  machine time reached    {Program.Number(engine.MachineTime)} ticks ({Program.Number(engine.MachineTime / TheST.Machine.Ticks.Frame)} frames)",
            string.Empty,
            "what this does not prove: G1/G2 on the real trace. The stream here is the fixture's, not the",
            "machine's — the TOS boot and its digest streams are #34's.",
        };

#if DEBUG
        report.Insert(3, $"build: Debug — digest stream {Program.Number(engine.DigestWords.Count)} words (one per batch)");
#else
        report.Insert(3, "build: Release — no digest, no assertions (ADR 0008 §6)");
#endif

        File.WriteAllLines(Path.Combine(directory, "run.report"), report);

        foreach (string line in state)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine($"wrote {directory}");
        return 0;
    }

    // ---- G2's perturbation: host scheduling only --------------------------------------------
    static void Inject(Random random, int frame)
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

        if (frame % 25 == 0)
        {
            Thread.Sleep(1);   // frame pacing
        }
    }
}
