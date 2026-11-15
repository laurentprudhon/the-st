// cpu-dissection — the #47 bench. NOT the emulator: one chip, one wire set, one trace, and a machine
// loop whose only job is to resume it. The CPU sources are the engine's own, linked in unchanged; what
// is missing is every other chip, so the numbers are the CPU's own coroutine cost and nothing else.
//
// The trace is a real boot's bus cycles (`boot --frames 300` with a temporary capture), so the opcode
// mix and the await shapes are the machine's, not a synthetic loop's.

using System.Collections.Generic;
using System.Diagnostics;
using TheST.Machine.Cpu;

namespace TheST.Machine;

/// <summary>How an await resolves. <c>Suspending</c> is the machine as it runs today; <c>Inline</c> is
/// the same code with every awaited answer already on the stack (variant B).</summary>
/// <summary>The trace ran out: the replay is over, and the pass's counters are final.</summary>
public sealed class EndOfTraceException : Exception
{
    public EndOfTraceException()
        : base("the trace's last cycle was asserted")
    {
    }
}

public enum Mode
{
    Suspending = 0,
    Inline = 1,
}

/// <summary>The machine loop, reduced to what one chip needs: a queue of wakes, a device that answers
/// the bus at the trace's instants, and the two log kinds.</summary>
public sealed class Engine
{
    public Engine(BusTrace trace, Mode mode = Mode.Suspending)
    {
        BusTrace = trace;
        Mode = mode;
    }

    public BusTrace BusTrace { get; }

    public Mode Mode { get; set; }

    /// <summary>Attribution only: read the thread's allocation counter around a suspension.</summary>
    public bool Attr { get; set; }

    /// <summary>The instant of the wake being drained — what the machine's clock is at a resume.</summary>
    public long MachineTime { get; private set; }

    /// <summary>Wakes delivered.</summary>
    public long Resumes { get; internal set; }

    /// <summary>Suspensions armed.</summary>
    public long Suspensions { get; internal set; }

    // ---- the attribution (Debug-only in spirit: nothing decides anything by it) --------------

    public readonly long[] SiteCount = new long[5];
    public readonly long[] SiteBytes = new long[5];
    public static readonly string[] SiteNames = ["fetch", "read", "write", "acknowledge", "stop-wait"];

    public readonly long[] LineCount = new long[16];
    public readonly long[] LineBytes = new long[16];
    public readonly long[] LineInstructions = new long[16];

    /// <summary>The opcode line the CPU is executing, set by the observer in the attribution pass.</summary>
    public int CurrentLine { get; set; }

    /// <summary>Attribution: the bytes each suspension allocated, in a histogram — the boxes and the
    /// continuation Actions are allocated before the engine is reached, so a bracket that starts at the
    /// previous suspension is the only one that can see them.</summary>
    public readonly Dictionary<long, long> SuspensionBytes = [];

    long _sinceLastSuspension = -1;

    /// <summary>Attribution: the async chain a sampled suspension stood on, verbatim from the stack —
    /// which is the "which await site resumed" question answered by the frames themselves.</summary>
    public readonly Dictionary<string, long> Chains = [];
    public const int ChainSampleEvery = 997;
    long _sampled;

    int _site = 4;

    static int SiteOf(Chip chip, Channel channel, int cycle, BusTrace trace)
    {
        if (channel.Id != Wires.Answer.Id)
        {
            return 4;
        }

        if (trace.IsCpuSpace[cycle])
        {
            return 3;
        }

        if (trace.IsWrite[cycle])
        {
            return 2;
        }

        // the program counter has not advanced yet at the await, so a fetch is a read at the counter
        return chip is M68000 cpu && trace.Address[cycle] == cpu.PC ? 0 : 1;
    }

    // ---- the logs (the machine's own types, linked in unchanged) -----------------------------

    readonly ValueLog?[] _wireLogs = new ValueLog?[64];
    readonly Dictionary<int, ValueLog> _internalLogs = [];

    public ValueLog Log(Channel channel)
    {
        if (channel.Kind == ChannelKind.Wire)
        {
            return _wireLogs[channel.Id] ??= new ValueLog(channel);
        }

        if (!_internalLogs.TryGetValue(channel.Id, out ValueLog? log))
        {
            log = new ValueLog(channel);
            _internalLogs[channel.Id] = log;
        }

        return log;
    }

    internal ushort ValueAt(Channel channel, long instant) => Log(channel).ValueAt(instant);

    long _seq;

    void AppendRecord(long instant, Channel channel, ushort mask, ushort value)
    {
        var record = new Record(instant, _seq++, channel, 1, mask, value);
        Log(channel).Append(record);
        if (channel.Kind == ChannelKind.Wire && channel.Id == Wires.Ipl.Id)
        {
            _iplSeqs.Add(record.Seq);
        }
    }

    // ---- the device: the trace, and nothing else --------------------------------------------
    // In the machine the System Control Unit answers the bus. Here the answer is already known, and
    // the only thing that has to be reproduced is *when* it arrives and what it carried.

    readonly List<long> _iplSeqs = [];
    int _iplCursor;
    int _cycle = -1;          // the trace cycle whose assertion is on the wire
    int _lane;                // which of the four assertion lanes the CPU is writing
    ushort _addressHigh, _addressLow, _control, _data;

    /// <summary>A cycle's assertion is on the wire: the trace's own record is claimed, and its asserted
    /// instant is checked against the chip's clock — a replay that drifts fails here, not later.</summary>
    void BeginCycle(Chip chip, ushort addressHigh)
    {
        _cycle++;
        if (_cycle >= BusTrace.Count)
        {
            throw new EndOfTraceException();
        }

        if (BusTrace.Asserted[_cycle] != chip.Clock)
        {
            throw new InvalidOperationException(
                $"cycle {_cycle}: asserted at {chip.Clock} but the trace says {BusTrace.Asserted[_cycle]}");
        }

        _addressHigh = addressHigh;
        _lane = 1;
    }

    /// <summary>The fourth lane: the cycle now matches the trace word for word, or the replay is not
    /// the trace's workload and no delta may be published.</summary>
    void EndAssertion()
    {
        if (_addressHigh != (ushort)(BusTrace.Address[_cycle] >> 16)
            || _addressLow != (ushort)(BusTrace.Address[_cycle] & 0xFFFE)
            || _control != BusTrace.Control[_cycle]
            || _data != (BusTrace.IsWrite[_cycle] ? BusTrace.WriteData[_cycle] : (ushort)0))
        {
            throw new InvalidOperationException(
                $"cycle {_cycle}: the replay asserted {_addressHigh:x4}/{_addressLow:x4}/ctl {_control:x4}/data {_data:x4}, " +
                $"the trace says {BusTrace.Address[_cycle] >> 16:x4}/{BusTrace.Address[_cycle] & 0xFFFE:x4}/ctl {BusTrace.Control[_cycle]:x4}/data {BusTrace.WriteData[_cycle]:x4}");
        }

        _lane = 0;
    }

    /// <summary>The answer arrives: the two records the CPU will read, stamped at the instant the
    /// trace says, and nothing else.</summary>
    void AppendAnswer(int cycle, long instant)
    {
        AppendRecord(instant, Wires.Answer, 0xFFFF, BusTrace.Answer[cycle]);
        AppendRecord(instant, Wires.Md, 0xFFFF, BusTrace.Md[cycle]);
    }

    /// <summary>Every IPL record stamped at or before an instant is in the log before the chip runs at
    /// it (the machine appends as time advances; so does this).</summary>
    void AdvanceTimeTo(long instant)
    {
        MachineTime = instant;
        while (_iplCursor < BusTrace.IplCount && BusTrace.IplInstant[_iplCursor] <= instant)
        {
            AppendRecord(BusTrace.IplInstant[_iplCursor], Wires.Ipl, 0xFFFF, BusTrace.IplValue[_iplCursor]);
            _iplCursor++;
        }
    }

    /// <summary>The next IPL record after a sequence number the chip has already seen: its instant, or
    /// <c>long.MaxValue</c> when the wire carries nothing more.</summary>
    long NextIplInstant(long afterSeq)
    {
        int index = FirstSeqAfter(_iplSeqs, afterSeq);
        return index < BusTrace.IplCount ? BusTrace.IplInstant[index] : long.MaxValue;
    }

    static int FirstSeqAfter(List<long> values, long afterSeq)
    {
        int lo = 0, hi = values.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (values[mid] <= afterSeq)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    // ---- the queue --------------------------------------------------------------------------

    readonly PriorityQueue<Entry, Key> _queue = new();
    long _arrival;

    /// <summary>A chip suspended: the wake it is owed. One entry per suspension, exactly as the engine
    /// enqueues a `Resume` — the object exists because the machine's does.</summary>
    internal void WaitForEither(Chip chip, long instant, Channel channel, long afterSeq, Action continuation, long allocatedBefore)
    {
        Suspensions++;

        if (Mode == Mode.Inline)
        {
            Inline(chip, channel, instant, afterSeq);
            return;
        }

        long at;
        int cycle = -1;
        if (channel.Id == Wires.Answer.Id)
        {
            cycle = _cycle;
            at = BusTrace.AnswerInstant[_cycle];
        }
        else
        {
            at = Math.Min(instant, NextIplInstant(afterSeq));
            if (at == long.MaxValue)
            {
                throw new InvalidOperationException("a wake with no wire record after it and no date: nothing can resume the chip");
            }
        }

        var wake = new Wake(chip, continuation, at, cycle);
        _queue.Enqueue(wake, new Key(at, _arrival++));

        if (Attr)
        {
            long now = GC.GetAllocatedBytesForCurrentThread();
            long bytes = _sinceLastSuspension < 0 ? 0 : now - _sinceLastSuspension;
            _sinceLastSuspension = now;

            int site = SiteOf(chip, channel, cycle < 0 ? 0 : cycle, BusTrace);
            SiteCount[site]++;
            SiteBytes[site] += bytes;
            LineCount[CurrentLine]++;
            LineBytes[CurrentLine] += bytes;
            SuspensionBytes[bytes] = SuspensionBytes.GetValueOrDefault(bytes) + 1;
            _site = site;

            if (++_sampled % ChainSampleEvery == 0)
            {
                string chain = ChainOf();
                Chains[chain] = Chains.GetValueOrDefault(chain) + 1;
            }
            _ = allocatedBefore;
        }
    }

    /// <summary>The M68000 frames of the live stack, innermost first: the suspended chain.</summary>
    static string ChainOf()
    {
        var frames = new List<string>();
        foreach (StackFrame frame in new StackTrace().GetFrames())
        {
            string name = frame.GetMethod()?.DeclaringType?.FullName ?? string.Empty;
            if (name.StartsWith("TheST.Machine.Cpu.M68000", StringComparison.Ordinal))
            {
                frames.Add(frame.GetMethod()!.Name);
            }
        }

        return frames.Count == 0 ? "(none)" : string.Join("<", frames);
    }

    /// <summary>Variant B (and C): the answer is already here — the same two records, the same clock,
    /// no suspension, no queue entry, no `Wake` object.</summary>
    internal void Inline(Chip chip, Channel channel, long instant, long afterSeq)
    {
        if (channel.Id == Wires.Answer.Id)
        {
            long at = BusTrace.AnswerInstant[_cycle];
            AdvanceTimeTo(at);
            AppendAnswer(_cycle, at);
            chip.Clock = at;
            return;
        }

        long next = Math.Min(instant, NextIplInstant(afterSeq));
        if (next == long.MaxValue)
        {
            throw new InvalidOperationException("a wake with no wire record after it and no date: nothing can resume the chip");
        }

        AdvanceTimeTo(next);
        chip.Clock = next;
    }

    /// <summary>Drains every wake, earliest first: the machine's own loop, with one chip in it.</summary>
    public void Run()
    {
        while (_queue.TryDequeue(out Entry? entry, out _))
        {
            var wake = (Wake)entry;
            AdvanceTimeTo(wake.Instant);
            if (wake.Cycle >= 0)
            {
                AppendAnswer(wake.Cycle, wake.Instant);
            }

            wake.Chip.Clock = wake.Instant;
            Resumes++;
            wake.Continuation();
        }
    }

    // ---- the chip's verbs -------------------------------------------------------------------

    internal void Write(Chip chip, long instant, Channel channel, ushort mask, ushort value)
    {
        if (channel.Kind == ChannelKind.Wire && channel.Id == Wires.Bus.Id)
        {
            // the lanes of one exchange, in the CPU's own order: address high, address low, control,
            // the data lanes (zero on a read), and later the release
            switch (mask)
            {
                case 0xFF00:
                    BeginCycle(chip, value);
                    break;

                case 0xFFFE:
                    _addressLow = value;
                    _lane = 2;
                    break;

                case 0xFFFF when _lane == 2:
                    _control = value;
                    _lane = 3;
                    break;

                case 0xFFFF when _lane == 3:
                    _data = value;
                    EndAssertion();
                    break;
            }
        }

        AppendRecord(instant, channel, mask, value);
    }

    internal void AppendAnchor(Chip chip, long instant, Channel channel, ushort value)
        => AppendRecord(instant, new Channel(ChannelKind.Internal, (ushort)((chip.Id << 8) | (channel.Id & 0xFF)), channel.Name), 0xFFFF, value);

    readonly record struct Key(long Instant, long Seq) : IComparable<Key>
    {
        public int CompareTo(Key other) =>
            Instant != other.Instant ? Instant.CompareTo(other.Instant) : Seq.CompareTo(other.Seq);
    }

    abstract class Entry(long instant)
    {
        public long Instant { get; } = instant;
    }

    /// <summary>The machine's own `Resume`, kept in its shape: a chip, the continuation of the frame that
    /// suspended, and the queue's arrival number. Its allocation is part of what a suspension costs.</summary>
    sealed class Wake(Chip chip, Action continuation, long instant, int cycle) : Entry(instant)
    {
        public Chip Chip { get; } = chip;
        public Action Continuation { get; } = continuation;

        /// <summary>The trace cycle whose answer this wake carries, or −1 for a wire wake (IPL).</summary>
        public int Cycle { get; } = cycle;
    }
}
