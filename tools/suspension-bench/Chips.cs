// ============================================================================================
// Synthetic chips: an endless `async` loop with a device-like read/write mix. Eight of them run the
// "frame" workload — a stated number of crossings per 20 ms PAL frame — the shape the scheduler
// actually serves (design/crossings.md: ≈4 730 class-1 wakes/frame idle, ≤40 000 class-2 bus
// entries). Deliberately NOT an emulator: no opcodes, no video, no TOS.
// ============================================================================================

namespace TheST.SuspensionBench;

public sealed class SynthChip : Chip
{
    readonly Wire _in;
    readonly Wire _out;
    readonly long _end;
    readonly long _stride;          // ticks between this chip's own future reads
    readonly int _fastPerCrossing;  // already-past reads per crossing (device logic sampling state)
    long _acc;

    public SynthChip(int id, Scheduler scheduler, Wire inWire, Wire outWire, long end, long stride, int fastPerCrossing)
        : base(id, scheduler)
    {
        _in = inWire;
        _out = outWire;
        _end = end;
        _stride = stride;
        _fastPerCrossing = fastPerCrossing;
        _acc = 0x2545F491 + id * 7919;
    }

    public long Sink => _acc;

    public override void Start() => Context.Run(Run);

    async void Run()
    {
        while (Clock < _end)
        {
            // 1. Sample state already committed at this instant: the fast path, N times.
            for (int k = 0; k < _fastPerCrossing; k++)
            {
                int v = await ReadAt(Clock, _in);
                _acc = (_acc ^ v) * 2654435761L;
            }

            // 2. Device-like internal work: a few ticks, non-constant.
            Advance(1 + (int)((_acc >> 7) & 7));

            // 3. Wait for the next crossing on the incoming wire: a genuine suspension.
            int x = await ReadAt(Clock + _stride, _in);
            _acc = (_acc ^ x) * 2654435761L;

            // 4. Drive the outgoing wire. `Write` does not suspend; `WriteAndWait` does, and is rare
            //    by design (only where the hardware orders a write before the same instant's
            //    receivers — ADR 0008 §2).
            if ((_acc & 63) == 0) Scheduler.Write(this, Clock, _out, 1, (int)_acc);
            if ((_acc & 8191) == 0) await Scheduler.WriteAndWait(this, Clock, _out, 2, (int)(_acc ^ 0x5A));

            Iterations++;
        }
        Done = true;
    }
}
