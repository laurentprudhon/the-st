// ============================================================================================
// The measurements. Every routine prints one machine-readable line:
//   MEASURE <name> <value> <unit> <n>
// so Program.cs can assemble the results document without transcription errors.
// ============================================================================================

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace TheST.SuspensionBench;

public static class Bench
{
    public static readonly double NsPerTick = 1e9 / Stopwatch.Frequency;

    static void Emit(string name, double value, string unit, long n, string note = "")
        => Console.WriteLine($"MEASURE\t{name}\t{value:F4}\t{unit}\t{n}\t{note}");

    // ---------------------------------------------------------------------------------------
    // 0. The sanity anchor: reproduce the two microbenchmark figures the ADR quotes, once, so the
    //    in-situ numbers have something to be compared against (ADR 0002 §Notes on evidence:
    //    0.32 ns fast path, ~53 ns real yield, desktop CoreCLR, bare Task loop).
    // ---------------------------------------------------------------------------------------

    static async Task AnchorCompletedAsync(long n)
    {
        for (long i = 0; i < n; i++) await Task.CompletedTask;
    }

    static async Task AnchorYieldAsync(long n)
    {
        for (long i = 0; i < n; i++) await Task.Yield();
    }

    static async Task AnchorEmptyAsync(long n)
    {
        for (long i = 0; i < n; i++) { }
    }

    public static void Anchor(long completedAwaits = 100_000_000, long yields = 10_000_000)
    {
        // Warm up: default tiering means Tier0 first, then Tier1. The shipping configuration is the
        // default tiering, so warm-up then measure is the honest reading, not the cold one.
        AnchorCompletedAsync(2_000_000).GetAwaiter().GetResult();
        var sw = Stopwatch.StartNew();
        AnchorCompletedAsync(completedAwaits).GetAwaiter().GetResult();
        sw.Stop();
        double completedNs = sw.Elapsed.TotalMilliseconds * 1e6 / completedAwaits;
        Emit("anchor.completed_await", completedNs, "ns/await", completedAwaits,
            "bare Task loop, microbenchmark anchor (ADR 0002 quotes 0.32)");

        AnchorEmptyAsync(2_000_000).GetAwaiter().GetResult();
        sw.Restart();
        AnchorEmptyAsync(completedAwaits).GetAwaiter().GetResult();
        sw.Stop();
        double emptyNs = sw.Elapsed.TotalMilliseconds * 1e6 / completedAwaits;
        Emit("anchor.empty_async_loop", emptyNs, "ns/iteration", completedAwaits,
            "the same async loop with the await removed: the loop's own cost");
        Emit("anchor.await_over_loop", completedNs - emptyNs, "ns/await", completedAwaits,
            "completed-await cost net of the loop");

        AnchorYieldAsync(200_000).GetAwaiter().GetResult();
        sw.Restart();
        AnchorYieldAsync(yields).GetAwaiter().GetResult();
        sw.Stop();
        Emit("anchor.real_yield", sw.Elapsed.TotalMilliseconds * 1e6 / yields, "ns/yield", yields,
            "Task.Yield to the default pool — NOT the scheduler context (ADR 0002 quotes 53)");
    }

    // ---------------------------------------------------------------------------------------
    // 1b. The same fast path with no per-window instrumentation at all: one chip, N reads of an
    //     instant at or behind its own clock, started through the dispatcher, timed end to end.
    //     This is the number the results document uses; `fastpath` exists to cross-check it.
    // ---------------------------------------------------------------------------------------

    sealed class PlainFastChip : Chip
    {
        readonly Wire _w;
        readonly long _reads;
        long _acc;
        public PlainFastChip(int id, Scheduler s, Wire w, long reads) : base(id, s) { _w = w; _reads = reads; _acc = 0x9E3779B9; }
        public long Sink => _acc;
        public override void Start() => Context.Run(Run);
        async void Run()
        {
            await ReadAt(Clock + 1, _w);        // one suspension: the rest of the loop runs on resumed stacks
            for (long i = 0; i < _reads; i++)
            {
                int v = await ReadAt(Clock, _w);
                _acc = (_acc ^ v) * 2654435761L;
            }
            Done = true;
        }
    }

    public static void FastPathPlain(long reads = 40_000_000)
    {
        var scheduler = new Scheduler();
        var wire = scheduler.Port(1);
        scheduler.Write(new PlainFastChip(99, scheduler, wire, 0), 0, wire, 1, 0x5A5A);

        var chip = new PlainFastChip(0, scheduler, wire, reads);
        var sw = Stopwatch.StartNew();
        chip.Start();
        scheduler.RunSerial();      // one suspension at the start, then the whole loop on resumed stacks
        sw.Stop();

        Emit("fastpath.plain_in_situ", sw.Elapsed.TotalMilliseconds * 1e6 / reads, "ns/read", reads,
            $"in situ, no per-read instrument, fast={chip.FastReads} resumed={chip.ResumedReads} sink={chip.Sink & 0xFF:x2}");
    }

    // ---------------------------------------------------------------------------------------
    // 2. A genuine suspension: a read at a future instant, woken by the queue, resumed through the
    //    scheduler's own context. Per-event cost = (suspend + queue insert + resume + continuation)
    //    / iterations; the chip's own work between events is two increments, so what dominates is
    //    the mechanism.
    // ---------------------------------------------------------------------------------------

    sealed class SuspendChip : Chip
    {
        readonly Wire _w;
        readonly long _end;
        readonly long _delta;
        long _acc;
        public SuspendChip(int id, Scheduler s, Wire w, long end, long delta) : base(id, s)
        { _w = w; _end = end; _delta = delta; _acc = 0x85EBCA6B; }
        public long Sink => _acc;
        public override void Start() => Context.Run(Run);
        async void Run()
        {
            while (Clock < _end)
            {
                Advance(1);
                int v = await ReadAt(Clock + _delta, _w);
                _acc = (_acc ^ v) * 2654435761L;
                if ((_acc & 15) == 0) Scheduler.Write(this, Clock, _w, 1, (int)_acc);
                Iterations++;
            }
        }
    }

    public static void Suspension(long iterations = 4_000_000, long delta = 1)
    {
        var scheduler = new Scheduler();
        var chip = new SuspendChip(0, scheduler, scheduler.Port(1), iterations * 2 + 8, delta);

        var sw = Stopwatch.StartNew();
        chip.Start();
        scheduler.RunSerial();
        sw.Stop();

        double ns = sw.Elapsed.TotalMilliseconds * 1e6 / iterations;
        Emit("suspension.genuine", ns, "ns/event", iterations,
            $"delta={delta} ticks, suspends={scheduler.Counters.SuspendedReads} resumes={scheduler.Counters.Resumes} records={scheduler.TotalRecords}");
    }

    // ---------------------------------------------------------------------------------------
    // 3. The awaited write: append + fire + the writer's own resume behind its record's receivers.
    // ---------------------------------------------------------------------------------------

    sealed class AwaitedWriteChip : Chip
    {
        readonly Wire _w;
        readonly long _end;
        long _acc;
        public AwaitedWriteChip(int id, Scheduler s, Wire w, long end) : base(id, s)
        { _w = w; _end = end; _acc = 0xC2B2AE35; }
        public long Sink => _acc;
        public override void Start() => Context.Run(Run);
        async void Run()
        {
            while (Clock < _end)
            {
                Advance(1);
                await Scheduler.WriteAndWait(this, Clock, _w, 1, (int)_acc);
                _acc = (_acc ^ Clock) * 2654435761L;
                Iterations++;
            }
        }
    }

    public static void AwaitedWrite(long iterations = 2_000_000)
    {
        var scheduler = new Scheduler();
        var chip = new AwaitedWriteChip(0, scheduler, scheduler.Port(1), iterations + 8);
        var sw = Stopwatch.StartNew();
        chip.Start();
        scheduler.RunSerial();
        sw.Stop();
        double ns = sw.Elapsed.TotalMilliseconds * 1e6 / iterations;
        Emit("write.awaited", ns, "ns/event", iterations,
            $"awaited_writes={scheduler.Counters.AwaitedWrites} resumes={scheduler.Counters.Resumes} records={scheduler.TotalRecords}");
    }

    // ---------------------------------------------------------------------------------------
    // 4. Continuation dispatch through the scheduler-owned SynchronizationContext (a chip awaiting a
    //    Task, e.g. Task.Yield, comes back through Post → queue → Resume).
    // ---------------------------------------------------------------------------------------

    /// <summary>One entry of each kind, reused: costs nothing per iteration but the dispatch itself.</summary>
    static void Noop() { }

    /// <summary>
    /// Continuation dispatch through the scheduler-owned context: enqueue a resume, pop it, run it with
    /// the context current. This is the per-event half of a suspension that is not the suspend path —
    /// the number a topology change would move.
    /// </summary>
    public static void DispatchMicro(long iterations = 2_000_000)
    {
        var scheduler = new Scheduler();
        var chip = new PlainFastChip(0, scheduler, scheduler.Port(1), 0);
        Action cont = Noop;

        var sw = Stopwatch.StartNew();
        for (long i = 0; i < iterations; i++)
        {
            scheduler.EnqueueResume(chip, i, cont);
            scheduler.RunNext();
        }
        sw.Stop();

        Emit("dispatch.context", sw.Elapsed.TotalMilliseconds * 1e6 / iterations, "ns/event", iterations,
            $"enqueue+pop+Context.Run, resumes={scheduler.Counters.Resumes}");
    }

    // ---------------------------------------------------------------------------------------
    // 5. The frame workload: N crossings per 20 ms PAL frame, 8 chips, mix of fast reads, genuine
    //    suspensions and awaited writes. Allocation is measured on the scheduler thread, which is
    //    where every managed allocation of the dispatch path is made in the serial topology.
    // ---------------------------------------------------------------------------------------

    public sealed record FrameResult(
        int Crossings, int Frames, int Chips, double MsPerFrame, double NsPerCrossing,
        double BytesPerFrame, long FastReads, long Suspensions, long AwaitedWrites, long Records,
        int Gen0, int Gen1, int Gen2, double GcPauseMs, ulong Digest, double FastShare);

    public static FrameResult Frame(int crossings, int frames = 60, int chips = 8, int fastPerCrossing = 8, bool collide = false)
    {
        var scheduler = new Scheduler();
        var wires = new Wire[chips + 1];
        for (int i = 0; i <= chips; i++) wires[i] = scheduler.Port(i + 1);

        long end = frames * Scheduler.FrameTicks;
        long stride = collide
            ? Math.Max(1, Scheduler.FrameTicks / Math.Max(1, crossings))
            : Math.Max(1, (long)chips * Scheduler.FrameTicks / Math.Max(1, crossings));
        var chipSet = new SynthChip[chips];
        for (int i = 0; i < chips; i++)
            chipSet[i] = new SynthChip(i, scheduler, wires[i], wires[i + 1], end, stride, fastPerCrossing);

        // prime the wires so reads sample real values
        for (int i = 0; i < chips; i++) scheduler.Write(chipSet[i], 0, wires[i], 0xF, i * 7919);

        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var pause0 = GC.GetTotalPauseDuration();

        var sw = Stopwatch.StartNew();
        foreach (var c in chipSet) c.Start();
        scheduler.RunSerial();
        sw.Stop();

        long alloc1 = GC.GetAllocatedBytesForCurrentThread();
        var pause1 = GC.GetTotalPauseDuration();

        long fast = 0, susp = 0, aw = 0;
        foreach (var c in chipSet) { fast += c.FastReads; susp += c.SuspendedReads; aw += c.AwaitedWrites; }

        double msPerFrame = sw.Elapsed.TotalMilliseconds / frames;
        var r = new FrameResult(
            crossings, frames, chips,
            msPerFrame,
            msPerFrame * 1e6 / crossings,
            (alloc1 - alloc0) / (double)frames,
            fast, susp, aw, scheduler.TotalRecords,
            GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2,
            (pause1 - pause0).TotalMilliseconds,
            scheduler.RunDigest,
            fast == 0 ? 0 : (double)fast / (fast + susp));

        Emit($"frame.c{r.Crossings}.ms_per_frame", r.MsPerFrame, "ms/frame", frames,
            $"chips={chips} fast={fast} susp={susp} awaited_writes={aw} records={r.Records} digest={r.Digest:X16}");
        Emit($"frame.c{r.Crossings}.ns_per_crossing", r.NsPerCrossing, "ns/crossing", crossings, "derived from ms/frame");
        Emit($"frame.c{r.Crossings}.bytes_per_frame", r.BytesPerFrame, "B/frame", frames,
            $"gen0={r.Gen0} gen1={r.Gen1} gen2={r.Gen2}");
        Emit($"frame.c{r.Crossings}.fast_share", r.FastShare * 100, "%", fast + susp,
            $"fast={fast} suspended={susp} (mix: {fastPerCrossing} past reads + 1 future read per crossing)");
        Emit($"frame.c{r.Crossings}.gc_pause", r.GcPauseMs / frames, "ms/frame", frames, "GC.GetTotalPauseDuration delta");
        return r;
    }

    // ---------------------------------------------------------------------------------------
    // 6. Topology: T threads, chips multiplexed over them, every scheduler touch under one lock —
    //    the naive "fixed pool with chips multiplexed" the ticket asks to test. The determinism
    //    instrument is the digest; a digest that differs between two runs of the same input at T>1
    //    is ADR 0002 falsifier 1 (same-instant order) firing.
    // ---------------------------------------------------------------------------------------

    public sealed record PoolResult(int Threads, double MsPerFrame, ulong Digest, long LateRecords, int Runs);

    public static List<PoolResult> Pool(int crossings, int frames = 20, int chips = 8, int runs = 3, bool collide = false)
    {
        var results = new List<PoolResult>();
        // Serial reference on the same workload, so pool digests are compared against the model's own order.
        var serial = Frame(crossings, frames, chips, 8, collide);
        Console.WriteLine($"MEASURE\tpool.serial.digest\t0\tvalue\t{frames}\tdigest={serial.Digest:X16} collide={collide}");
        foreach (int threads in new[] { 1, 2, 4, 8 })
        {
            var digests = new List<ulong>();
            long late = 0;
            double ms = 0;
            for (int run = 0; run < runs; run++)
            {
                var (digest, lateRecords, msPerFrame) = PoolRun(threads, crossings, frames, chips, collide);
                digests.Add(digest);
                late += lateRecords;
                ms += msPerFrame;
            }
            Emit($"pool.t{threads}.ms_per_frame", ms / runs, "ms/frame", frames,
                $"runs={runs} digests={string.Join(",", digests.Select(d => d.ToString("X16")))} " +
                $"stable={(digests.Distinct().Count() == 1 ? "yes" : "NO")} late_records={late} collide={collide} " +
                $"matches_serial={(digests[0] == serial.Digest ? "yes" : "NO")}");
            results.Add(new PoolResult(threads, ms / runs, digests[0], late, runs));
        }
        return results;
    }

    static (ulong digest, long lateRecords, double msPerFrame) PoolRun(int threads, int crossings, int frames, int chips, bool collide = false)
    {
        var scheduler = new Scheduler { PoolMode = true };
        var wires = new Wire[chips + 1];
        for (int i = 0; i <= chips; i++) wires[i] = scheduler.Port(i + 1);
        long end = frames * Scheduler.FrameTicks;
        long stride = collide
            ? Math.Max(1, Scheduler.FrameTicks / Math.Max(1, crossings))
            : Math.Max(1, (long)chips * Scheduler.FrameTicks / Math.Max(1, crossings));
        var chipSet = new SynthChip[chips];
        for (int i = 0; i < chips; i++)
            chipSet[i] = new SynthChip(i, scheduler, wires[i], wires[i + 1], end, stride, 8);
        for (int i = 0; i < chips; i++) scheduler.Write(chipSet[i], 0, wires[i], 0xF, i * 7919);

        var sw = Stopwatch.StartNew();
        foreach (var c in chipSet) c.Start();

        // Workers drain the queue; chips' own threads only started them.
        var workers = new Thread[threads];
        using var stop = new ManualResetEventSlim(false);
        int alive = threads;
        for (int w = 0; w < threads; w++)
        {
            workers[w] = new Thread(() =>
            {
                while (true)
                {
                    if (scheduler.TryPopPool(out var entry, out var t))
                    {
                        if (entry.Kind == EntryKind.Resume)
                        {
                            scheduler.Counters.Resumes++;
                            entry.Chip!.Clock = t;
                            entry.Chip.Resuming = true;
                            entry.Chip.Context.Run(entry.Continuation!);
                        }
                        continue;
                    }
                    if (chipSet.All(c => c.Done)) break;
                    Thread.SpinWait(20);
                }
                if (Interlocked.Decrement(ref alive) == 0) stop.Set();
            }) { IsBackground = true };
            workers[w].Start();
        }
        stop.Wait();
        sw.Stop();
        scheduler.FinishDigest();

        return (scheduler.RunDigest, scheduler.Counters.LateRecords, sw.Elapsed.TotalMilliseconds / frames);
    }
}